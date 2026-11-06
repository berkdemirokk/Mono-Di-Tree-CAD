using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

public sealed record DxfNotchContourReviewItem(Guid PieceId,string PieceName,string ContourLayer,int MarkerCount,bool HasGrainline);
public sealed record DxfNotchReviewItem(Guid PieceId,string PieceName,string ContourLayer,string MarkerLayer,int VertexIndex,Point2 Position);
public sealed record DxfNotchGrainReviewItem(Guid PieceId,string PieceName,string ContourLayer,string MarkerLayer,Point2 Start,Point2 End,bool NeedsReview);

/// <summary>Detached inspection data, not a machine-ready or textile-standard export.</summary>
public sealed class DxfNotchReviewPlan
{
    private readonly ProjectDocument source;
    internal void EnsureExperimentalAllowed() { var ids=Contours.Select(c=>c.PieceId).ToHashSet();var pieces=source.Pieces.Where(p=>ids.Contains(p.Id)).ToArray();if(pieces.Length!=ids.Count)throw new InvalidOperationException("Hazırlanan parça kaynağı eksik.");ExperimentalOutputGuard.EnsureAllowed(pieces); }
    internal string Text { get; }
    public ImmutableArray<DxfNotchContourReviewItem> Contours { get; }
    public ImmutableArray<DxfNotchReviewItem> Items { get; }
    public ImmutableArray<DxfNotchGrainReviewItem> Grainlines { get; }
    public ImmutableArray<DxfExportOmission> Omissions { get; }
    public bool FlipY { get; }
    public string Sha256 { get; }
    internal DxfNotchReviewPlan(ProjectDocument source,string text,ImmutableArray<DxfNotchContourReviewItem> contours,ImmutableArray<DxfNotchReviewItem> items,ImmutableArray<DxfNotchGrainReviewItem> grainlines,ImmutableArray<DxfExportOmission> omissions,bool flipY)
    { this.source=source;Text=text;Contours=contours;Items=items;Grainlines=grainlines;Omissions=omissions;FlipY=flipY;Sha256=Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false,true).GetBytes(text))); }
    public bool Matches(ProjectDocument current)=>current is not null && DocumentSession.ContentEquals(source,current);
}

/// <summary>Explicit review-only DXF; POINT markers have no cutting or textile semantics.</summary>
public static class DxfNotchReviewExport
{
    private const int MaxBytes=64*1024*1024, MaxMarkers=10_000;
    private static string Number(double n)=>n.ToString("G17",CultureInfo.InvariantCulture);
    /// <summary>Review explicitly selected pieces, at least one marked, while binding approval to the full source snapshot.</summary>
    public static DxfNotchReviewPlan PrepareSelected(ProjectDocument source,IReadOnlyCollection<Guid> selectedIds,bool flipY,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(selectedIds);cancellationToken.ThrowIfCancellationRequested();
        ProjectStorage.ValidateForDxfExport(source,cancellationToken);var snapshot=source.Clone();ProjectStorage.ValidateForDxfExport(snapshot,cancellationToken);
        if(!DocumentSession.ContentEquals(source,snapshot))throw new InvalidOperationException("Proje hazırlanırken değişti; incelemeyi yeniden başlatın.");
        if(selectedIds.Count is < 1 or > ProjectStorage.MaximumPieces)throw new ArgumentException("İnceleme için işaretli parça seçin.");
        var ids=new HashSet<Guid>();var visited=0;
        foreach(var id in selectedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(++visited>selectedIds.Count || id==Guid.Empty || !ids.Add(id))throw new ArgumentException("Parça seçimi yinelenmiş veya geçersiz.");
        }
        if(visited!=selectedIds.Count)throw new ArgumentException("Parça seçimi geçersiz.");
        var pieces=snapshot.Pieces.Where(piece=>ids.Contains(piece.Id)).Select(piece=>piece.Clone()).ToList();
        if(pieces.Count!=ids.Count)throw new ArgumentException("Seçilen parça bulunamadı.");
        if(!pieces.Any(piece=>piece.Notches is { Count:>0 }))throw new ArgumentException("Seçimde en az bir işaretli parça bulunmalıdır.");
        var subset=new ProjectDocument { Name=snapshot.Name,Pieces=pieces,
            GradingRules=snapshot.GradingRules.Where(rule=>ids.Contains(rule.SourcePieceId)).Select(rule=>rule.Clone()).ToList(),
            ImportProvenance=snapshot.ImportProvenance?.Clone() };
        var prepared=Prepare(subset,flipY,cancellationToken);
        var omissions=prepared.Omissions;
        if(snapshot.NestingSnapshot is not null && !omissions.Any(item=>item.Kind==DxfExportOmissionKind.NestingSnapshot))
            omissions=omissions.Add(new(null,DxfExportOmissionKind.NestingSnapshot));
        if(snapshot.GradingRules.Count>0 && !omissions.Any(item=>item.Kind==DxfExportOmissionKind.GradingRules))
            omissions=omissions.Add(new(null,DxfExportOmissionKind.GradingRules));
        // Photo history is document-wide historical evidence. The selected
        // subset deliberately excludes it, but the report must disclose this
        // loss even when the recorded source piece was not selected.
        if(snapshot.PhotoTraceHistory is { Count:>0 } && !omissions.Any(item=>item.Kind==DxfExportOmissionKind.PhotoTraceHistory))
            omissions=omissions.Add(new(null,DxfExportOmissionKind.PhotoTraceHistory));
        cancellationToken.ThrowIfCancellationRequested();
        if(!DocumentSession.ContentEquals(source,snapshot))throw new InvalidOperationException("Proje hazırlanırken değişti; incelemeyi yeniden başlatın.");
        return new DxfNotchReviewPlan(snapshot,prepared.Text,prepared.Contours,prepared.Items,prepared.Grainlines,omissions,flipY);
    }

    public static DxfNotchReviewPlan Prepare(ProjectDocument source,bool flipY,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(source);cancellationToken.ThrowIfCancellationRequested();ProjectStorage.ValidateForDxfExport(source,cancellationToken);
        var snapshot=source.Clone();ProjectStorage.ValidateForDxfExport(snapshot,cancellationToken);
        ExperimentalOutputGuard.EnsureAllowed(snapshot.Pieces);
        if(!DocumentSession.ContentEquals(source,snapshot))throw new InvalidOperationException("Proje hazırlanırken değişti; incelemeyi yeniden başlatın.");
        var markerCount=snapshot.Pieces.Sum(p=>(long)(p.Notches?.Count ?? 0));
        if(markerCount==0)throw new InvalidDataException("İncelenecek köşe işareti bulunamadı.");
        if(markerCount>MaxMarkers)throw new InvalidDataException("İnceleme çıktısı en fazla 10.000 köşe işaretini destekler; proje değiştirilmedi.");
        // Analyze a private copy with marks removed solely to reuse the existing
        // contour/layer and omission validation. Source and output retain marks.
        var analysisCopy=snapshot.Clone();foreach(var p in analysisCopy.Pieces)p.Notches=null;
        var analysis=DxfFormat.AnalyzeExport(analysisCopy,cancellationToken);
        // Review labels must be unambiguous both in our table and in the DXF.
        // PlanLayers can preserve bidi/format characters, which can make two
        // distinct layer names appear deceptively similar in a recipient CAD.
        foreach(var mapping in analysis.Layers)
        foreach(var rune in mapping.LayerName.EnumerateRunes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                throw new InvalidDataException("Kontur katman adında görünmez yönlendirme veya kontrol karakteri var. Parça adını düzeltip inceleme çıktısını yeniden hazırlayın.");
        }
        var contours=snapshot.Pieces.Select((piece,i)=>new DxfNotchContourReviewItem(piece.Id,piece.Name,analysis.Layers[i].LayerName,piece.Notches?.Count ?? 0,piece.Grainline is not null)).ToImmutableArray();
        var used=analysis.Layers.Select(x=>x.LayerName).ToHashSet(StringComparer.OrdinalIgnoreCase);used.Add("0");
        var markerLayers=new string?[snapshot.Pieces.Count];var items=ImmutableArray.CreateBuilder<DxfNotchReviewItem>();
        for(int i=0;i<snapshot.Pieces.Count;i++)
        {
            cancellationToken.ThrowIfCancellationRequested();var piece=snapshot.Pieces[i];
            if(piece.Notches is not { Count:>0 } marks)continue;
            var baseName="TCD_NOTCH_"+piece.Id.ToString("N");var layer=baseName;var suffix=1;
            while(!used.Add(layer))layer=baseName+"_"+(suffix++).ToString(CultureInfo.InvariantCulture);
            markerLayers[i]=layer;
            foreach(var mark in marks) { cancellationToken.ThrowIfCancellationRequested();items.Add(new(piece.Id,piece.Name,analysis.Layers[i].LayerName,layer,mark.VertexIndex,mark.Position)); }
        }
        var grainlines=ImmutableArray.CreateBuilder<DxfNotchGrainReviewItem>();
        for(int i=0;i<snapshot.Pieces.Count;i++)
        {
            cancellationToken.ThrowIfCancellationRequested();var piece=snapshot.Pieces[i];
            if(piece.Grainline is not { } grain)continue;
            var baseName="TCD_GRAIN_"+piece.Id.ToString("N");var layer=baseName;var suffix=1;
            while(!used.Add(layer)) { cancellationToken.ThrowIfCancellationRequested();layer=baseName+"_"+(suffix++).ToString(CultureInfo.InvariantCulture); }
            grainlines.Add(new(piece.Id,piece.Name,analysis.Layers[i].LayerName,layer,grain.Start,grain.End,grain.NeedsReview));
        }
        var layers=new List<string> { "0" };layers.AddRange(analysis.Layers.Select(x=>x.LayerName).Where(x=>!string.Equals(x,"0",StringComparison.OrdinalIgnoreCase)));
        layers.AddRange(markerLayers.Where(x=>x is not null).Select(x=>x!));
        layers.AddRange(grainlines.Select(x=>x.MarkerLayer));
        ulong next=0x10;string Handle()=>(next++).ToString("X",CultureInfo.InvariantCulture);
        var ltype=Handle();var continuous=Handle();var layerTable=Handle();var layerHandles=layers.Select(_=>Handle()).ToArray();
        var contourHandles=snapshot.Pieces.Select(_=>Handle()).ToArray();var markerHandles=items.Select(_=>Handle()).ToArray();
        var grainHandles=grainlines.Select(_=>Handle()).ToArray();
        // PDMODE 34 requests a circle and cross; negative PDSIZE is a
        // viewport-relative percentage, not a physical cutting dimension.
        var b=new StringBuilder("0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1021\n9\n$INSUNITS\n70\n4\n9\n$PDMODE\n70\n34\n9\n$PDSIZE\n40\n-2\n9\n$HANDSEED\n5\n");
        b.Append(next.ToString("X",CultureInfo.InvariantCulture)).Append("\n0\nENDSEC\n0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLTYPE\n5\n").Append(ltype)
         .Append("\n330\n0\n100\nAcDbSymbolTable\n70\n1\n0\nLTYPE\n5\n").Append(continuous).Append("\n330\n").Append(ltype)
         .Append("\n100\nAcDbSymbolTableRecord\n100\nAcDbLinetypeTableRecord\n2\nCONTINUOUS\n70\n0\n3\nSolid line\n72\n65\n73\n0\n40\n0\n0\nENDTAB\n0\nTABLE\n2\nLAYER\n5\n").Append(layerTable)
         .Append("\n330\n0\n100\nAcDbSymbolTable\n70\n").Append(layers.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        for(int i=0;i<layers.Count;i++) { cancellationToken.ThrowIfCancellationRequested();b.Append("0\nLAYER\n5\n").Append(layerHandles[i]).Append("\n330\n").Append(layerTable).Append("\n100\nAcDbSymbolTableRecord\n100\nAcDbLayerTableRecord\n2\n").Append(layers[i]).Append("\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n"); }
        b.Append("0\nENDTAB\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n");
        double Y(double y)=>flipY ? -y : y;
        for(int i=0;i<snapshot.Pieces.Count;i++)
        {
            cancellationToken.ThrowIfCancellationRequested();var piece=snapshot.Pieces[i];var count=piece.Points.Count;
            if(piece.Closed && count>1 && piece.Points[0]==piece.Points[^1])count--;
            b.Append("0\nLWPOLYLINE\n5\n").Append(contourHandles[i]).Append("\n100\nAcDbEntity\n8\n").Append(analysis.Layers[i].LayerName).Append("\n100\nAcDbPolyline\n90\n").Append(count.ToString(CultureInfo.InvariantCulture)).Append("\n70\n").Append(piece.Closed ? 1 : 0).Append('\n');
            for(int j=0;j<count;j++) { if((j&255)==0)cancellationToken.ThrowIfCancellationRequested();var p=piece.Points[j];b.Append("10\n").Append(Number(p.X)).Append("\n20\n").Append(Number(Y(p.Y))).Append('\n'); }
            if(b.Length>MaxBytes)throw new InvalidDataException("DXF boyut sınırını aşıyor.");
        }
        for(int i=0;i<items.Count;i++)
        {
            if((i&255)==0)cancellationToken.ThrowIfCancellationRequested();var item=items[i];
            b.Append("0\nPOINT\n5\n").Append(markerHandles[i]).Append("\n100\nAcDbEntity\n8\n").Append(item.MarkerLayer).Append("\n100\nAcDbPoint\n10\n").Append(Number(item.Position.X)).Append("\n20\n").Append(Number(Y(item.Position.Y))).Append("\n30\n0\n");
            if(b.Length>MaxBytes)throw new InvalidDataException("DXF boyut sınırını aşıyor.");
        }
        for(int i=0;i<grainlines.Count;i++)
        {
            cancellationToken.ThrowIfCancellationRequested();var line=grainlines[i];
            b.Append("0\nLINE\n5\n").Append(grainHandles[i]).Append("\n100\nAcDbEntity\n8\n").Append(line.MarkerLayer)
             .Append("\n100\nAcDbLine\n10\n").Append(Number(line.Start.X)).Append("\n20\n").Append(Number(Y(line.Start.Y)))
             .Append("\n30\n0\n11\n").Append(Number(line.End.X)).Append("\n21\n").Append(Number(Y(line.End.Y))).Append("\n31\n0\n");
            if(b.Length>MaxBytes)throw new InvalidDataException("DXF boyut sınırını aşıyor.");
        }
        b.Append("0\nENDSEC\n0\nEOF\n");var text=b.ToString();if(new UTF8Encoding(false,true).GetByteCount(text)>MaxBytes)throw new InvalidDataException("DXF boyut sınırını aşıyor.");
        cancellationToken.ThrowIfCancellationRequested();if(!DocumentSession.ContentEquals(source,snapshot))throw new InvalidOperationException("Proje hazırlanırken değişti; incelemeyi yeniden başlatın.");
        var omissions=analysis.Omissions.Where(o=>o.Kind!=DxfExportOmissionKind.GrainlineUnsupported).ToImmutableArray()
            .AddRange(grainlines.Select(g=>new DxfExportOmission(g.PieceId,DxfExportOmissionKind.GrainlineReviewState)))
            .AddRange(snapshot.Pieces.Select(p=>new DxfExportOmission(p.Id,DxfExportOmissionKind.PieceIdentity)));
        return new(snapshot,text,contours,items.ToImmutable(),grainlines.ToImmutable(),omissions,flipY);
    }
    public static void Write(DxfNotchReviewPlan plan,string path,ProjectFileVersion expected,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(plan);ArgumentNullException.ThrowIfNull(expected);plan.EnsureExperimentalAllowed();cancellationToken.ThrowIfCancellationRequested();
        if(expected.Sha256 is not null)throw new InvalidOperationException("İnceleme için yeni bir dosya adı seçin.");
        ProjectStorage.AtomicWriteIfUnchanged(path,plan.Text,expected,cancellationToken);
    }
}
