using System.Collections.Immutable;
using System.Globalization;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

/// <summary>Detached block definition geometry. This is not a placed instance or production approval.</summary>
public sealed record DxfBlockDefinitionPointResult(ProjectDocument WorkingCopy,int BlockHeaderOrdinal,int PointOrdinal,int VertexOrdinal,string Notice);
public sealed record DxfBlockDefinitionPointAssignment(int PointOrdinal,int VertexOrdinal,int VertexIndex);
public sealed record DxfBlockDefinitionPointsResult(ProjectDocument WorkingCopy,int BlockHeaderOrdinal,ImmutableArray<DxfBlockDefinitionPointAssignment> Assignments,string Notice);

/// <summary>Explicitly selects one unplaced block definition and one coincident visual vertex marker.</summary>
public static class DxfBlockDefinitionPointMapping
{
    /// <summary>All-or-nothing batch on one selected piece. Each selection is
    /// independently revalidated against one source snapshot; no partial
    /// working copy is returned if any selection fails. The caller must keep the
    /// input stream and import stable until completion.</summary>
    public static DxfBlockDefinitionPointsResult CreateMany(DxfImportResult import,Stream source,
        IReadOnlyCollection<DxfPointVertexSelection> selections,int blockHeaderOrdinal,bool acceptUnplacedDefinition,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(import);ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(selections);
        token.ThrowIfCancellationRequested();
        if(!source.CanRead || !source.CanSeek || source.Position!=0)throw new InvalidDataException("DXF kaynak akışı baştan okunabilir olmalıdır.");
        const int maximumSelections=32;
        var selected=DxfBoundedSelections.Copy(selections,maximumSelections,token);
        if(selected.Length==0)throw new InvalidDataException("En az bir POINT seçilmelidir.");
        var points=new HashSet<int>();var vertices=new HashSet<int>();Guid? pieceId=null;
        foreach(var item in selected)
        {
            token.ThrowIfCancellationRequested();
            if(item is null || (pieceId.HasValue && item.PieceId!=pieceId.Value) || !points.Add(item.SourceOrdinal) || !vertices.Add(item.VertexIndex))
                throw new InvalidDataException("Seçimler aynı parçaya ait, benzersiz POINT ve köşeler olmalıdır.");
            pieceId=item.PieceId;
        }
        if(!acceptUnplacedDefinition || import.ImportParameters is not { Mode:DxfImportMode.General, BulgeToleranceMm:null } parameters)
            throw new InvalidDataException("Blok tanımı onayı veya DXF aktarım seçenekleri geçersiz.");
        // Capture the source once, then parse and hash exactly those detached
        // bytes. Any concurrent alteration must still match the original import
        // fingerprint; later source changes cannot affect individual choices.
        using var snapshot=new MemoryStream();var buffer=new byte[81920];
        while(true)
        {
            token.ThrowIfCancellationRequested();var count=source.Read(buffer,0,buffer.Length);if(count==0)break;
            if(snapshot.Length+count>DxfSourceSnapshotCheck.MaximumBytes)throw new InvalidDataException("DXF kaynak sınırını aşıyor.");
            snapshot.Write(buffer,0,count);
        }
        token.ThrowIfCancellationRequested();snapshot.Position=0;
        var fresh=DxfFormat.Import(snapshot,import.Project.Name,parameters.MillimetersPerUnit,parameters.Encoding,token);
        ProjectDocument? copy=null;var assignments=ImmutableArray.CreateBuilder<DxfBlockDefinitionPointAssignment>(selected.Length);
        string? notice=null;
        foreach(var item in selected)
        {
            token.ThrowIfCancellationRequested();snapshot.Position=0;
            var verified=CreateCore(import,snapshot,item,blockHeaderOrdinal,acceptUnplacedDefinition,token,fresh);
            copy ??= verified.WorkingCopy;
            if(assignments.Count>0)
            {
                var marker=verified.WorkingCopy.Pieces.Single().Notches!.Single();
                copy.Pieces.Single().Notches!.Add(marker.Clone());
            }
            assignments.Add(new(verified.PointOrdinal,verified.VertexOrdinal,item.VertexIndex));notice=verified.Notice;
        }
        token.ThrowIfCancellationRequested();ProjectStorage.Validate(copy!,token);token.ThrowIfCancellationRequested();
        // Recheck the same stream after all choices. This detects a changed
        // source since capture; it cannot lock out a later noncooperating writer.
        source.Position=0;
        var finalSource=DxfSourceSnapshotCheck.CheckStream(source,import,token);
        if(finalSource.Status!=DxfSourceSnapshotCheck.Status.MatchesReadBytes || !finalSource.ImportIdentityVerified)
            throw new InvalidDataException("DXF kaynağı eşleme sırasında değişti; çalışma kopyası oluşturulmadı.");
        token.ThrowIfCancellationRequested();
        return new(copy!,blockHeaderOrdinal,assignments.MoveToImmutable(),notice!);
    }

    public static DxfBlockDefinitionPointResult Create(DxfImportResult import,Stream source,
        DxfPointVertexSelection selection,int blockHeaderOrdinal,bool acceptUnplacedDefinition,CancellationToken token=default)
        => CreateCore(import,source,selection,blockHeaderOrdinal,acceptUnplacedDefinition,token,null);

    private static DxfBlockDefinitionPointResult CreateCore(DxfImportResult import,Stream source,
        DxfPointVertexSelection selection,int blockHeaderOrdinal,bool acceptUnplacedDefinition,CancellationToken token,DxfImportResult? captured)
    {
        ArgumentNullException.ThrowIfNull(import);ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(selection);
        token.ThrowIfCancellationRequested();
        if(!acceptUnplacedDefinition)throw new InvalidDataException("Blok tanımının yerleştirilmemiş çalışma kopyası olduğu açıkça onaylanmalıdır.");
        if(!source.CanRead || !source.CanSeek || source.Position!=0 || import.ImportParameters is not { Mode:DxfImportMode.General, BulgeToleranceMm:null } parameters ||
           import.SourceSha256 is null || import.AnnotationSnapshotSha256!=import.SourceSha256 || import.AnnotationSnapshotByteLength!=import.SourceByteLength ||
           import.PieceSources is not { } oldLinks || oldLinks.IsDefault || import.Project.NestingSnapshot is not null || import.Project.GradingRules.Count!=0 ||
           !string.Equals(import.PieceSourcesFingerprint,NestingSnapshot.ComputeLayoutFingerprint(import.Project,token),StringComparison.Ordinal))
            throw new InvalidDataException("Kaynak kimliği veya içe aktarma durumu doğrulanamadı.");
        var oldLink=oldLinks.SingleOrDefault(p=>p.PieceId==selection.PieceId);
        if(oldLink is null || oldLink.BlockHeaderOrdinal!=blockHeaderOrdinal || oldLink.EntityType!="POLYLINE" || oldLink.Section!="BLOCKS" ||
           oldLink.SourceOrdinals.Length!=1 || !oldLink.InsertPath.IsDefaultOrEmpty)
            throw new InvalidDataException("Seçilen parça doğrudan blok POLYLINE tanımı değil.");
        var oldPiece=import.Project.Pieces.Single(p=>p.Id==selection.PieceId);
        var fresh=captured ?? DxfFormat.Import(source,import.Project.Name,parameters.MillimetersPerUnit,parameters.Encoding,token);
        if(fresh.SourceByteLength!=import.SourceByteLength || !string.Equals(fresh.SourceSha256,import.SourceSha256,StringComparison.OrdinalIgnoreCase) ||
           fresh.SkippedInsertEntities!=false || fresh.ImportedBlockDefinitions!=true || fresh.SourceGeometry is not { Truncated:false } geometry ||
           fresh.SupplementalGeometry is not { Truncated:false } supplemental || fresh.PieceSources is not { } freshLinks || freshLinks.IsDefault ||
           fresh.BlockEntityInventory is not { Truncated:false } inventory || inventory.Records.IsDefault)
            throw new InvalidDataException("Kaynak baytları, blok envanteri veya INSERT durumu doğrulanamadı.");
        if(inventory.Records.Any(r=>r.BlockHeaderOrdinal==blockHeaderOrdinal && r.EntityType is not ("POINT" or "POLYLINE" or "VERTEX" or "SEQEND" or "LINE" or "TEXT" or "MTEXT")))
            throw new InvalidDataException("Blokta bilinmeyen veya desteklenmeyen kaynak varlığı var.");
        bool sequenceOpen=false;int sequenceVertices=0;
        foreach(var entry in inventory.Records.Where(r=>r.BlockHeaderOrdinal==blockHeaderOrdinal))
        {
            token.ThrowIfCancellationRequested();
            if(entry.EntityType=="POLYLINE")
            {
                if(sequenceOpen)throw new InvalidDataException("İç içe POLYLINE dizisi desteklenmiyor.");
                sequenceOpen=true;sequenceVertices=0;
            }
            else if(entry.EntityType=="VERTEX")
            {
                if(!sequenceOpen)throw new InvalidDataException("Sahipsiz VERTEX kaydı var.");
                sequenceVertices++;
            }
            else if(entry.EntityType=="SEQEND")
            {
                if(!sequenceOpen || sequenceVertices<2)throw new InvalidDataException("Sahipsiz veya eksik POLYLINE sonu var.");
                sequenceOpen=false;
            }
            else if(sequenceOpen)throw new InvalidDataException("POLYLINE dizisi tamamlanmadan başka kayıt başladı.");
        }
        if(sequenceOpen)throw new InvalidDataException("POLYLINE dizisi kapatılmadı.");
        var link=freshLinks.SingleOrDefault(p=>p.BlockHeaderOrdinal==blockHeaderOrdinal && p.EntityType=="POLYLINE" &&
            p.SourceOrdinals.Length==1 && p.SourceOrdinals[0]==oldLink.SourceOrdinals[0]);
        if(link is null || link.ChildSourceOrdinals.Length<2 || selection.VertexIndex<0 || selection.VertexIndex>=link.ChildSourceOrdinals.Length-1)
            throw new InvalidDataException("POLYLINE çocuk sırası doğrulanamadı.");
        if(supplemental.Records.Any(r=>r.BlockHeaderOrdinal==blockHeaderOrdinal && r.EntityType!="POINT"))
            throw new InvalidDataException("Bu blokta desteklenmeyen geometrinin köşe eşlemesi doğrulanamıyor.");
        var piece=fresh.Project.Pieces.Single(p=>p.Id==link.PieceId).Clone();piece.Id=oldPiece.Id;
        if(!DocumentSession.ContentEquals(new ProjectDocument { Pieces=[piece] },new ProjectDocument { Pieces=[oldPiece] }) ||
           !piece.Closed || piece.Notches is not null || selection.VertexIndex>=piece.Points.Count ||
           (selection.VertexIndex==piece.Points.Count-1 && piece.Points[0]==piece.Points[^1]))
            throw new InvalidDataException("Kaynak POLYLINE veya hedef köşe değişmiş.");
        var point=supplemental.Records.SingleOrDefault(p=>p.SourceOrdinal==selection.SourceOrdinal && p.EntityType=="POINT");
        int vertexOrdinal=link.ChildSourceOrdinals[selection.VertexIndex];
        var vertex=geometry.Records.SingleOrDefault(p=>p.SourceOrdinal==vertexOrdinal && p.EntityType=="VERTEX");
        if(point is null || vertex is null || point.BlockHeaderOrdinal!=blockHeaderOrdinal || vertex.BlockHeaderOrdinal!=blockHeaderOrdinal ||
           point.Section!="BLOCKS" || vertex.Section!="BLOCKS" || point.Groups.Any(g=>g.Code is not (5 or 8 or 10 or 20 or 30 or 100)))
            throw new InvalidDataException("POINT ve VERTEX aynı kaynak blok oluşumunda değil.");
        string Value(ImmutableArray<DxfAnnotationGroup> groups,int code,bool optional=false)
        {
            var values=groups.Where(g=>g.Code==code).ToArray();if(optional && values.Length==0)return "0";
            if(values.Length!=1)throw new InvalidDataException("Kaynak koordinatı eksik veya yinelenmiş.");return values[0].RawValue.Trim();
        }
        var px=Value(point.Groups,10);var py=Value(point.Groups,20);var vx=Value(vertex.Groups,10);var vy=Value(vertex.Groups,20);
        if(!DxfExactDecimal.AreEqual(px,vx,token) || !DxfExactDecimal.AreEqual(py,vy,token) ||
           !DxfExactDecimal.AreEqual(Value(point.Groups,30,true),"0",token) || !DxfExactDecimal.AreEqual(Value(vertex.Groups,30,true),"0",token) ||
           !double.TryParse(px,NumberStyles.Float,CultureInfo.InvariantCulture,out var x) || !double.TryParse(py,NumberStyles.Float,CultureInfo.InvariantCulture,out var y) ||
           !DxfExactDecimal.AreEqual(px,x.ToString("R",CultureInfo.InvariantCulture),token) || !DxfExactDecimal.AreEqual(py,y.ToString("R",CultureInfo.InvariantCulture),token))
            throw new InvalidDataException("Kaynak koordinatları tam eşleşmiyor.");
        var position=new Point2(x*parameters.MillimetersPerUnit,y*parameters.MillimetersPerUnit);
        if(!double.IsFinite(position.X) || !double.IsFinite(position.Y) || position!=piece.Points[selection.VertexIndex])
            throw new InvalidDataException("Hedef köşe belirsiz veya ölçek sonrası farklı.");
        // An explicit source ordinal expresses the user's choice but cannot
        // distinguish coincident source records geometrically. Refuse either
        // exact decimal coincidence or collision introduced by binary scaling.
        foreach(var other in supplemental.Records.Where(r=>r.BlockHeaderOrdinal==blockHeaderOrdinal && r.EntityType=="POINT" && r.SourceOrdinal!=selection.SourceOrdinal))
        {
            token.ThrowIfCancellationRequested();
            if(other.Groups.IsDefault || other.Groups.Any(g=>g.Code is not (5 or 8 or 10 or 20 or 30 or 50 or 100 or 210 or 220 or 230)))
                throw new InvalidDataException("Bloktaki diğer POINT kaydı doğrulanamadı.");
            var ox=Value(other.Groups,10);var oy=Value(other.Groups,20);
            var oz=Value(other.Groups,30,true);
            if(!double.TryParse(oz,NumberStyles.Float,CultureInfo.InvariantCulture,out var dz) || !double.IsFinite(dz) ||
               !DxfExactDecimal.AreEqual(oz,dz.ToString("R",CultureInfo.InvariantCulture),token) ||
               !double.TryParse(ox,NumberStyles.Float,CultureInfo.InvariantCulture,out var dx) ||
               !double.TryParse(oy,NumberStyles.Float,CultureInfo.InvariantCulture,out var dy) || !double.IsFinite(dx) || !double.IsFinite(dy))
                throw new InvalidDataException("Bloktaki diğer POINT koordinatı doğrulanamadı.");
            foreach(var code in new[]{210,220,230})
                if(!DxfExactDecimal.AreEqual(Value(other.Groups,code,true),code==230 && other.Groups.Any(g=>g.Code==230) ? "1" : "0",token))
                    throw new InvalidDataException("Bloktaki diğer POINT düzlemi doğrulanamadı.");
            if(other.Groups.Any(g=>g.Code==50))
            {
                var angle=Value(other.Groups,50);
                if(!double.TryParse(angle,NumberStyles.Float,CultureInfo.InvariantCulture,out var a) || !double.IsFinite(a))
                    throw new InvalidDataException("Bloktaki diğer POINT açısı geçersiz.");
            }
            var scaledX=dx*parameters.MillimetersPerUnit;var scaledY=dy*parameters.MillimetersPerUnit;
            if(!double.IsFinite(scaledX) || !double.IsFinite(scaledY) || (dx!=0 && scaledX==0) || (dy!=0 && scaledY==0))
                throw new InvalidDataException("Bloktaki diğer POINT ölçek sonrası doğrulanamadı.");
            if((DxfExactDecimal.AreEqual(ox,px,token) && DxfExactDecimal.AreEqual(oy,py,token)) ||
               (scaledX==position.X && scaledY==position.Y))
                throw new InvalidDataException("Aynı blokta birden fazla POINT aynı konuma eşleşiyor.");
        }
        // Inspect every raw LINE, including lines excluded by the contour importer.
        // Unknown 3-D plane semantics or a rounded endpoint collision fail closed.
        foreach(var line in geometry.Records.Where(r=>r.BlockHeaderOrdinal==blockHeaderOrdinal && r.EntityType=="LINE"))
        {
            token.ThrowIfCancellationRequested();
            foreach(var code in new[]{30,31,38,39,210,220,230})
                if(!DxfExactDecimal.AreEqual(Value(line.Groups,code,true),code==230 && line.Groups.Any(g=>g.Code==230) ? "1" : "0",token))
                    throw new InvalidDataException("LINE düzlemi doğrulanamadı.");
            foreach(var codes in new[]{(10,20),(11,21)})
            {
                var rx=Value(line.Groups,codes.Item1);var ry=Value(line.Groups,codes.Item2);
                if(!double.TryParse(rx,NumberStyles.Float,CultureInfo.InvariantCulture,out var lx) ||
                   !double.TryParse(ry,NumberStyles.Float,CultureInfo.InvariantCulture,out var ly) || !double.IsFinite(lx) || !double.IsFinite(ly))
                    throw new InvalidDataException("LINE uç koordinatı doğrulanamadı.");
                if((DxfExactDecimal.AreEqual(rx,px,token) && DxfExactDecimal.AreEqual(ry,py,token)) ||
                   (lx*parameters.MillimetersPerUnit==position.X && ly*parameters.MillimetersPerUnit==position.Y))
                    throw new InvalidDataException("POINT aynı blokta LINE ucuyla da eşleşiyor.");
            }
        }
        var blockPieceIds=freshLinks.Where(p=>p.BlockHeaderOrdinal==blockHeaderOrdinal && p.EntityType!="LINE").Select(p=>p.PieceId).ToHashSet();
        int matches=0;
        foreach(var candidate in fresh.Project.Pieces.Where(p=>blockPieceIds.Contains(p.Id)))
            for(int i=0;i<candidate.Points.Count;i++)
            {
                token.ThrowIfCancellationRequested();
                if(candidate.Closed && i==candidate.Points.Count-1 && candidate.Points[0]==candidate.Points[^1])continue;
                if(candidate.Points[i]==position && ++matches>1)throw new InvalidDataException("Blokta birden fazla köşe eşleşiyor.");
            }
        if(matches!=1)throw new InvalidDataException("Blok köşesi bulunamadı.");
        // The POINT may spell zero with the other sign. Preserve the exact
        // target vertex bits, as required by the persisted marker contract.
        piece.Notches=[new NotchMark { VertexIndex=selection.VertexIndex,Position=piece.Points[selection.VertexIndex] }];
        var result=new ProjectDocument { Name=import.Project.Name+" · blok tanımı",Pieces=[piece] };ProjectStorage.Validate(result,token);
        return new(result,blockHeaderOrdinal,selection.SourceOrdinal,vertexOrdinal,"Yerleştirilmemiş blok tanımından çalışma kopyası; fiziksel çentik veya üretim onayı değildir.");
    }
}
