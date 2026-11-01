using System.Collections.Immutable;
using System.Globalization;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

/// <summary>Detached block definition geometry. This is not a placed instance or production approval.</summary>
public sealed record DxfBlockDefinitionPointResult(ProjectDocument WorkingCopy,int BlockHeaderOrdinal,int PointOrdinal,int VertexOrdinal,string Notice);

/// <summary>Explicitly selects one unplaced block definition and one coincident visual vertex marker.</summary>
public static class DxfBlockDefinitionPointMapping
{
    public static DxfBlockDefinitionPointResult Create(DxfImportResult import,Stream source,
        DxfPointVertexSelection selection,int blockHeaderOrdinal,bool acceptUnplacedDefinition,CancellationToken token=default)
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
        var fresh=DxfFormat.Import(source,import.Project.Name,parameters.MillimetersPerUnit,parameters.Encoding,token);
        if(fresh.SourceByteLength!=import.SourceByteLength || !string.Equals(fresh.SourceSha256,import.SourceSha256,StringComparison.OrdinalIgnoreCase) ||
           fresh.SkippedInsertEntities!=false || fresh.ImportedBlockDefinitions!=true || fresh.SourceGeometry is not { Truncated:false } geometry ||
           fresh.SupplementalGeometry is not { Truncated:false } supplemental || fresh.PieceSources is not { } freshLinks || freshLinks.IsDefault)
            throw new InvalidDataException("Kaynak baytları, blok envanteri veya INSERT durumu doğrulanamadı.");
        var link=freshLinks.SingleOrDefault(p=>p.BlockHeaderOrdinal==blockHeaderOrdinal && p.EntityType=="POLYLINE" &&
            p.SourceOrdinals.Length==1 && p.SourceOrdinals[0]==oldLink.SourceOrdinals[0]);
        if(link is null || link.ChildSourceOrdinals.Length<2 || selection.VertexIndex<0 || selection.VertexIndex>=link.ChildSourceOrdinals.Length-1)
            throw new InvalidDataException("POLYLINE çocuk sırası doğrulanamadı.");
        // Legacy LINE chain provenance does not yet carry its owning block
        // occurrence. Do not claim uniqueness while any LINE in this block
        // could contribute an untracked coincident endpoint.
        if(geometry.Records.Any(r=>r.BlockHeaderOrdinal==blockHeaderOrdinal && r.EntityType=="LINE") ||
           supplemental.Records.Any(r=>r.BlockHeaderOrdinal==blockHeaderOrdinal && r.EntityType!="POINT"))
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
        var blockPieceIds=freshLinks.Where(p=>p.BlockHeaderOrdinal==blockHeaderOrdinal).Select(p=>p.PieceId).ToHashSet();
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
