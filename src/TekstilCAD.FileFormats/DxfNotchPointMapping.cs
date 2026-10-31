using System.Collections.Immutable;
using System.Globalization;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

/// <summary>Explicit operator choice; POINT ordinal is not an authenticated DXF handle.</summary>
public sealed record DxfNotchPointSelection(int PointSourceOrdinal,Guid PieceId,int VertexIndex);
public sealed record DxfNotchPointAudit(int PointSourceOrdinal,Guid PieceId,int VertexIndex,Point2 Position,string Layer);
public sealed record DxfNotchPointMappingResult(ProjectDocument Project,ImmutableArray<DxfNotchPointAudit> Applied);

/// <summary>
/// Maps explicitly selected model-space DXF POINT records to exactly matching
/// contour vertices on a detached project. This is a visual vertex annotation,
/// not a physical notch, textile standard, or machine instruction.
/// Only millimeter source units and exact coordinates are supported.
/// The caller must keep the import stable through the complete operation.
/// </summary>
public static class DxfNotchPointMapping
{
    private const long MaximumComparisons=2_000_000;

    public static DxfNotchPointMappingResult Apply(DxfImportResult import,
        IReadOnlyCollection<DxfNotchPointSelection> selections,string approvedPointLayer,string expectedFingerprint,
        CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(import);ArgumentNullException.ThrowIfNull(selections);
        ArgumentNullException.ThrowIfNull(expectedFingerprint);
        cancellationToken.ThrowIfCancellationRequested();
        if(string.IsNullOrWhiteSpace(approvedPointLayer))throw new ArgumentException("İşaret katmanı açıkça seçilmelidir.",nameof(approvedPointLayer));
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import,cancellationToken);
        if(!string.Equals(fingerprint,expectedFingerprint,StringComparison.Ordinal))throw new InvalidDataException("DXF veya proje değişti; incelemeyi yeniden başlatın.");
        if(import.ImportParameters!.MillimetersPerUnit!=1)throw new InvalidDataException("İşaret eşlemesi yalnız milimetre kaynak ölçeğinde desteklenir.");
        if(import.SupplementalGeometry is not { Truncated:false } report || report.Records.IsDefault ||
           import.OpaqueMetadata is not { Truncated:false } opaque || opaque.Groups.IsDefault)
            throw new InvalidDataException("DXF nokta envanteri veya özel veri kayıtları eksik; eşleme uygulanmadı.");
        if(import.PieceSourcesFingerprint is null || !string.Equals(import.PieceSourcesFingerprint,
            NestingSnapshot.ComputeLayoutFingerprint(import.Project,cancellationToken),StringComparison.Ordinal))
            throw new InvalidDataException("Konturlar kaynak envanterinden sonra değişti; eşleme uygulanmadı.");
        var chosen=DxfBoundedSelections.Copy(selections,Math.Min(report.Records.Length,ProjectStorage.MaximumTotalPoints),cancellationToken);
        if(chosen.Length==0)throw new ArgumentException("En az bir işaret seçilmelidir.",nameof(selections));
        long vertexCount=0;foreach(var piece in import.Project.Pieces) { cancellationToken.ThrowIfCancellationRequested();vertexCount+=piece.Points.Count; }
        if(vertexCount*chosen.Length>MaximumComparisons)throw new InvalidDataException("İşaret eşleme çalışma sınırı aşıldı.");
        var points=new Dictionary<int,Point2>();var pointRecords=new Dictionary<int,DxfSupplementalGeometryRecord>();var counts=new Dictionary<Point2,int>();
        var opaqueOrdinals=new HashSet<int>();
        foreach(var group in opaque.Groups) { cancellationToken.ThrowIfCancellationRequested();opaqueOrdinals.Add(group.SourceOrdinal); }
        foreach(var record in report.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(record.EntityType!="POINT" || record.Section!="ENTITIES" || record.Block is not null)continue;
            var layers=record.Groups.Where(g=>g.Code==8).ToArray();
            if(layers.Length!=1 || !string.Equals(layers[0].RawValue.Trim(),approvedPointLayer,StringComparison.OrdinalIgnoreCase))continue;
            if(opaqueOrdinals.Contains(record.SourceOrdinal))throw new InvalidDataException("İşaret özel veri içeriyor; anlamı güvenle eşlenemez.");
            var point=ReadPoint(record);
            if(!points.TryAdd(record.SourceOrdinal,point))throw new InvalidDataException("DXF nokta kaydı yinelenmiş.");
            pointRecords.Add(record.SourceOrdinal,record);
            counts.TryGetValue(point,out var count);counts[point]=count+1;
        }
        var byId=import.Project.Pieces.ToDictionary(p=>p.Id);var ordinals=new HashSet<int>();var targets=new HashSet<(Guid,int)>();
        var sourceByPiece=new Dictionary<Guid,DxfPieceSource>();
        foreach(var item in import.PieceSources is { } capturedSources && !capturedSources.IsDefault ? capturedSources : ImmutableArray<DxfPieceSource>.Empty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(!sourceByPiece.TryAdd(item.PieceId,item))throw new InvalidDataException("Parça kaynak kaydı yinelenmiş.");
        }
        var geometryByOrdinal=new Dictionary<int,DxfSourceGeometryRecord>();
        foreach(var item in import.SourceGeometry is { Truncated:false } geometry && !geometry.Records.IsDefault ? geometry.Records : ImmutableArray<DxfSourceGeometryRecord>.Empty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(!geometryByOrdinal.TryAdd(item.SourceOrdinal,item))throw new InvalidDataException("DXF geometri kaynak kaydı yinelenmiş.");
        }
        var sourceVertices=new Dictionary<Guid,(string X,string Y)[]>();
        var audit=ImmutableArray.CreateBuilder<DxfNotchPointAudit>(chosen.Length);
        foreach(var selection in chosen)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(selection is null || !ordinals.Add(selection.PointSourceOrdinal) || !points.TryGetValue(selection.PointSourceOrdinal,out var position) ||
               counts[position]!=1 || !byId.TryGetValue(selection.PieceId,out var piece) || selection.VertexIndex<0 || selection.VertexIndex>=piece.Points.Count)
                throw new InvalidDataException("İşaret seçimi geçersiz, yinelenmiş veya onaylanan katmanda değil.");
            int index=selection.VertexIndex;
            if(piece.Closed && piece.Points.Count>1 && piece.Points[0]==piece.Points[^1] && index==piece.Points.Count-1)index=0;
            if(!targets.Add((piece.Id,index)) || piece.Notches?.Any(n=>n.VertexIndex==index)==true || piece.Points[index]!=position)
                throw new InvalidDataException("İşaret köşeyle tam eşleşmiyor veya köşede zaten işaret var.");
            int matchingVertices=0;
            foreach(var other in import.Project.Pieces)
            for(int i=0;i<other.Points.Count;i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if(other.Closed && i==other.Points.Count-1 && i>0 && other.Points[i]==other.Points[0])continue;
                if(other.Points[i]==position && ++matchingVertices>1)throw new InvalidDataException("Nokta birden fazla kontur köşesiyle eşleşiyor; seçim belirsiz.");
            }
            if(matchingVertices!=1)throw new InvalidDataException("İşaret köşesi bulunamadı.");
            var record=pointRecords[selection.PointSourceOrdinal];
            var x=record.Groups.Single(g=>g.Code==10).RawValue;var y=record.Groups.Single(g=>g.Code==20).RawValue;
            if(sourceByPiece.TryGetValue(piece.Id,out var source) &&
               source.EntityType is "LWPOLYLINE" or "POLYLINE" or "LINE" &&
               import.SourceGeometry is { Truncated:false } && import.ImportParameters.BulgeToleranceMm is null)
            {
                if(!sourceVertices.TryGetValue(piece.Id,out var originalVertices))
                {
                    originalVertices=ReadSourceVertices(source,geometryByOrdinal,piece,cancellationToken);
                    sourceVertices.Add(piece.Id,originalVertices);
                }
                // Compare original source decimals, not a rounded double's spelling.
                if(!DxfExactDecimal.AreEqual(x,originalVertices[index].X,cancellationToken) ||
                   !DxfExactDecimal.AreEqual(y,originalVertices[index].Y,cancellationToken))
                    throw new InvalidDataException("DXF noktasının hassasiyeti köşeyle eşleşmeyi kanıtlamıyor.");
            }
            // Historical conservative fallback for line chains and other
            // non-indexed source forms: require the POINT decimal to equal the
            // canonical binary64 spelling exactly, in addition to vertex equality.
            else if(!DxfExactDecimal.AreEqual(x,position.X.ToString("G17",CultureInfo.InvariantCulture),cancellationToken) ||
                    !DxfExactDecimal.AreEqual(y,position.Y.ToString("G17",CultureInfo.InvariantCulture),cancellationToken))
                throw new InvalidDataException("DXF noktasının hassasiyeti köşeyle eşleşmeyi kanıtlamıyor.");
            audit.Add(new(selection.PointSourceOrdinal,piece.Id,index,position,approvedPointLayer));
        }
        var copy=import.Project.Clone();var copied=copy.Pieces.ToDictionary(p=>p.Id);
        foreach(var item in audit)
        {
            cancellationToken.ThrowIfCancellationRequested();var piece=copied[item.PieceId];
            (piece.Notches ??= []).Add(new NotchMark { VertexIndex=item.VertexIndex,Position=piece.Points[item.VertexIndex] });
        }
        ProjectStorage.Validate(copy);cancellationToken.ThrowIfCancellationRequested();
        if(!string.Equals(DxfTextileGrainlineMapping.CaptureFingerprint(import,cancellationToken),expectedFingerprint,StringComparison.Ordinal))
            throw new InvalidDataException("DXF veya proje işlem sırasında değişti; eşleme uygulanmadı.");
        return new(copy,audit.MoveToImmutable());
    }

    private static (string X,string Y)[] ReadSourceVertices(DxfPieceSource source,
        IReadOnlyDictionary<int,DxfSourceGeometryRecord> records,PatternPiece piece,CancellationToken token)
    {
        if(source.Section!="ENTITIES" || source.Block is not null || source.SourceOrdinals.IsDefault || source.SourceOrdinals.IsEmpty ||
           source.InsertPath.IsDefault || !source.InsertPath.IsEmpty || source.ChildSourceOrdinals.IsDefault)
            throw new InvalidDataException("Konturun doğrudan kaynak köşeleri doğrulanamadı.");
        DxfSourceGeometryRecord Get(int ordinal,string type)
        {
            token.ThrowIfCancellationRequested();
            if(!records.TryGetValue(ordinal,out var value) || value.Section!="ENTITIES" || value.Block is not null || value.EntityType!=type ||
               value.Groups.IsDefault || value.GroupOrdinals.IsDefault || value.Groups.Length!=value.GroupOrdinals.Length)
                throw new InvalidDataException("Kontur kaynak kayıtları eksik veya uyuşmuyor.");
            for(int i=0;i<value.Groups.Length;i++)
                if(value.Groups[i] is null || value.Groups[i].GroupOrdinal!=value.GroupOrdinals[i])
                    throw new InvalidDataException("Kontur kaynak grup sırası geçersiz.");
            return value;
        }
        if(source.EntityType=="LWPOLYLINE")
        {
            if(source.SourceOrdinals.Length!=1 || !source.ChildSourceOrdinals.IsEmpty)
                throw new InvalidDataException("Polilin kaynak ilişkisi belirsiz.");
            return ReadVertices(Get(source.SourceOrdinals[0],"LWPOLYLINE"),piece,token);
        }
        if(source.EntityType=="POLYLINE")
        {
            if(source.SourceOrdinals.Length!=1 || source.ChildSourceOrdinals.Length!=piece.Points.Count+1)
                throw new InvalidDataException("POLYLINE köşe kaynak ilişkisi belirsiz.");
            var parent=source.SourceOrdinals[0];_ = Get(parent,"POLYLINE");
            var result=new (string X,string Y)[piece.Points.Count];
            for(int i=0;i<source.ChildSourceOrdinals.Length;i++)
            {
                token.ThrowIfCancellationRequested();var ordinal=source.ChildSourceOrdinals[i];
                if((long)ordinal!=(long)parent+i+1)throw new InvalidDataException("POLYLINE kaynak sırası belirsiz.");
                var vertex=Get(ordinal,i==piece.Points.Count ? "SEQEND" : "VERTEX");
                if(i==piece.Points.Count)continue;
                var bulges=vertex.Groups.Where(g=>g.Code==42).ToArray();
                if(bulges.Length>1 || (bulges.Length==1 && !DxfExactDecimal.AreEqual(bulges[0].RawValue,"0",token)))
                    throw new InvalidDataException("Yaylı POLYLINE köşesi doğrulanamıyor.");
                result[i]=ReadSourcePoint(vertex,10,20,piece.Points[i]);
            }
            return result;
        }
        if(!source.ChildSourceOrdinals.IsEmpty)throw new InvalidDataException("LINE kaynak ilişkisi belirsiz.");
        int count=piece.Points.Count,edges=piece.Closed ? count : count-1;
        if(count<2 || source.SourceOrdinals.Length!=edges)throw new InvalidDataException("LINE zinciri kaynak sayısı uyuşmuyor.");
        var endpoints=new Dictionary<(Point2,Point2),((string X,string Y) A,(string X,string Y) B,int Ordinal)>();
        var seenOrdinals=new HashSet<int>();
        foreach(var ordinal in source.SourceOrdinals)
        {
            token.ThrowIfCancellationRequested();
            if(!seenOrdinals.Add(ordinal))throw new InvalidDataException("LINE kaynağı yinelenmiş.");
            var line=Get(ordinal,"LINE");var a=ReadSourcePoint(line,10,20,null);var b=ReadSourcePoint(line,11,21,null);
            var pa=ParseSourcePoint(a);var pb=ParseSourcePoint(b);
            if(pa==pb || endpoints.ContainsKey((pa,pb)) || endpoints.ContainsKey((pb,pa)))throw new InvalidDataException("LINE zinciri yinelenmiş veya belirsiz.");
            endpoints.Add((pa,pb),(a,b,ordinal));
        }
        var vertices=new (string X,string Y)?[count];var used=new HashSet<int>();
        void Assign(int index,(string X,string Y) point)
        {
            if(vertices[index] is { } prior && (!DxfExactDecimal.AreEqual(prior.X,point.X,token) || !DxfExactDecimal.AreEqual(prior.Y,point.Y,token)))
                throw new InvalidDataException("LINE uçları tam ondalık eşleşmiyor; köşe belirsiz.");
            vertices[index]=point;
        }
        for(int i=0;i<edges;i++)
        {
            token.ThrowIfCancellationRequested();int j=(i+1)%count;
            if(endpoints.TryGetValue((piece.Points[i],piece.Points[j]),out var forward))
            { if(!used.Add(forward.Ordinal))throw new InvalidDataException("LINE zincirinde kaynak yinelendi.");Assign(i,forward.A);Assign(j,forward.B); }
            else if(endpoints.TryGetValue((piece.Points[j],piece.Points[i]),out var reverse))
            { if(!used.Add(reverse.Ordinal))throw new InvalidDataException("LINE zincirinde kaynak yinelendi.");Assign(i,reverse.B);Assign(j,reverse.A); }
            else throw new InvalidDataException("LINE sırası veya bağlantısı kaynakta doğrulanamadı.");
        }
        if(used.Count!=source.SourceOrdinals.Length || vertices.Any(v=>v is null))throw new InvalidDataException("LINE zinciri eksik.");
        return vertices.Select(v=>v!.Value).ToArray();
    }

    private static (string X,string Y) ReadSourcePoint(DxfSourceGeometryRecord record,int xCode,int yCode,Point2? expected)
    {
        var x=record.Groups.Where(g=>g.Code==xCode).ToArray();var y=record.Groups.Where(g=>g.Code==yCode).ToArray();
        if(x.Length!=1 || y.Length!=1)throw new InvalidDataException("Kaynak nokta eksik veya yinelenmiş.");
        var point=(x[0].RawValue,y[0].RawValue);var parsed=ParseSourcePoint(point);
        if(expected is { } target && parsed!=target)throw new InvalidDataException("Kaynak nokta aktarılan köşeyle uyuşmuyor.");
        return point;
    }
    private static Point2 ParseSourcePoint((string X,string Y) point)
    {
        if(!double.TryParse(point.X,NumberStyles.Float,CultureInfo.InvariantCulture,out var x) || !double.IsFinite(x) ||
           !double.TryParse(point.Y,NumberStyles.Float,CultureInfo.InvariantCulture,out var y) || !double.IsFinite(y) ||
           (x==0 && !DxfExactDecimal.AreEqual(point.X,"0")) || (y==0 && !DxfExactDecimal.AreEqual(point.Y,"0")))
            throw new InvalidDataException("Kaynak koordinat geçersiz veya sıfıra yuvarlandı.");
        return new Point2(x,y);
    }

    private static (string X,string Y)[] ReadVertices(DxfSourceGeometryRecord record,PatternPiece piece,CancellationToken token)
    {
        if(record.Groups.IsDefault || record.GroupOrdinals.IsDefault || record.Groups.Length!=record.GroupOrdinals.Length)
            throw new InvalidDataException("Kontur kaynak grupları eksik.");
        var vertices=new List<(string X,string Y)>();string? x=null;
        for(int i=0;i<record.Groups.Length;i++)
        {
            token.ThrowIfCancellationRequested();var group=record.Groups[i];
            if(group is null || group.GroupOrdinal!=record.GroupOrdinals[i])throw new InvalidDataException("Kontur kaynak grup sırası geçersiz.");
            if(group.Code==42 && !DxfExactDecimal.AreEqual(group.RawValue,"0",token))
                throw new InvalidDataException("Yaylı kaynak konturda köşe eşlemesi desteklenmiyor.");
            if(group.Code==10) { if(x is not null)throw new InvalidDataException("Kontur köşe sırası geçersiz.");x=group.RawValue; }
            else if(group.Code==20)
            {
                if(x is null || vertices.Count>=piece.Points.Count ||
                   !double.TryParse(x,NumberStyles.Float,CultureInfo.InvariantCulture,out var px) ||
                   !double.TryParse(group.RawValue,NumberStyles.Float,CultureInfo.InvariantCulture,out var py) ||
                   !double.IsFinite(px) || !double.IsFinite(py) || new Point2(px,py)!=piece.Points[vertices.Count])
                    throw new InvalidDataException("Kontur kaynak köşesiyle aktarılmış geometri eşleşmiyor.");
                vertices.Add((x,group.RawValue));x=null;
            }
        }
        if(x is not null || vertices.Count!=piece.Points.Count)throw new InvalidDataException("Kontur kaynak köşe sayısı uyuşmuyor.");
        return vertices.ToArray();
    }

    private static Point2 ReadPoint(DxfSupplementalGeometryRecord record)
    {
        if(record.Groups.IsDefault || !record.Children.IsEmpty)throw new InvalidDataException("DXF nokta kaydı geçersiz.");
        double Read(int code,double? fallback=null)
        {
            var groups=record.Groups.Where(g=>g.Code==code).ToArray();
            if(groups.Length==0 && fallback.HasValue)return fallback.Value;
            if(groups.Length!=1 || !double.TryParse(groups[0].RawValue,NumberStyles.Float,CultureInfo.InvariantCulture,out var value) || !double.IsFinite(value))
                throw new InvalidDataException("DXF nokta koordinatı eksik veya yinelenmiş.");
            return value;
        }
        var x=Read(10);var y=Read(20);
        if((x==0 && !DxfExactDecimal.AreEqual(record.Groups.Single(g=>g.Code==10).RawValue,"0")) ||
           (y==0 && !DxfExactDecimal.AreEqual(record.Groups.Single(g=>g.Code==20).RawValue,"0")))
            throw new InvalidDataException("DXF nokta koordinatı sayısal dönüşümde sıfıra düştü.");
        foreach(var code in new[] { 30,39,210,220,230,60,67 })
        {
            var groups=record.Groups.Where(g=>g.Code==code).ToArray();if(groups.Length==0)continue;
            var expected=code==230 ? 1d : 0d;
            if(Read(code)!=expected || !DxfExactDecimal.IsDifferenceWithinTolerance(groups[0].RawValue,expected.ToString("G17",CultureInfo.InvariantCulture),0))
                throw new InvalidDataException("DXF noktası görünür model düzleminde değil veya belirsiz.");
        }
        var papers=record.Groups.Where(g=>g.Code==410).ToArray();
        if(papers.Length>1 || (papers.Length==1 && !papers[0].RawValue.Trim().Equals("Model",StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("DXF noktası model alanında değil.");
        var point=new Point2(x,y);Geometry.Validate(point);return point;
    }
}
