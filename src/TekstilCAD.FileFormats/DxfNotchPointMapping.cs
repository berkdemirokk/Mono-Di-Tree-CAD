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
            if(sourceByPiece.TryGetValue(piece.Id,out var source) && source.EntityType=="LWPOLYLINE")
            {
                if(!sourceVertices.TryGetValue(piece.Id,out var originalVertices))
                {
                    if(import.ImportParameters.BulgeToleranceMm is not null || source.Section!="ENTITIES" || source.Block is not null ||
                       source.SourceOrdinals.IsDefault || source.SourceOrdinals.Length!=1 ||
                       source.InsertPath.IsDefault || !source.InsertPath.IsEmpty || source.ChildSourceOrdinals.IsDefault || !source.ChildSourceOrdinals.IsEmpty ||
                       !geometryByOrdinal.TryGetValue(source.SourceOrdinals[0],out var geometryRecord) || geometryRecord.Section!="ENTITIES" ||
                       geometryRecord.Block is not null || geometryRecord.EntityType!="LWPOLYLINE")
                        throw new InvalidDataException("Konturun doğrudan kaynak köşeleri doğrulanamadı.");
                    originalVertices=ReadVertices(geometryRecord,piece,cancellationToken);
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
