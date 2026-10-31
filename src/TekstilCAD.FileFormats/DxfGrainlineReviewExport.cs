using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

/// <summary>Inspection evidence only; the mapping and review state are not encoded in DXF.</summary>
public sealed record DxfGrainlineReviewItem(Guid PieceId, string PieceName, string ContourLayer,
    string? MarkerLayer, Point2? Start, Point2? End, bool? NeedsReview);

/// <summary>An immutable prepared output and report; not a physical machine or textile standard certification.</summary>
public sealed class DxfGrainlineReviewPlan
{
    private readonly ProjectDocument source;
    internal string Text { get; }
    internal DxfGrainlineReviewPlan(ProjectDocument source, string text, string fingerprint,
        ImmutableArray<DxfGrainlineReviewItem> items, ImmutableArray<DxfExportOmission> omissions, string hash, int length)
    { this.source=source;Text=text;PieceFingerprint=fingerprint;Items=items;Omissions=omissions;Sha256=hash;ByteLength=length; }
    public string PieceFingerprint { get; }
    public ImmutableArray<DxfGrainlineReviewItem> Items { get; }
    public ImmutableArray<DxfExportOmission> Omissions { get; }
    public string Sha256 { get; }
    public int ByteLength { get; }
    /// <summary>The caller must prevent concurrent mutation while comparing.</summary>
    public bool Matches(ProjectDocument current) => current is not null && DocumentSession.ContentEquals(source,current);
}

/// <summary>
/// Explicit DXF review output. Marker lines are ordinary DXF geometry in separate
/// layers: third-party CAD or cutters may interpret them as cutting paths.
/// The caller must prevent source mutation during capture. No automatic DXF
/// grainline association or review-status preservation is claimed.
/// </summary>
public static class DxfGrainlineReviewExport
{
    private const int MaxBytes = 64 * 1024 * 1024;
    private static string Number(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    public static DxfGrainlineReviewPlan Prepare(ProjectDocument source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        ProjectStorage.ValidateForDxfExport(source);
        var snapshot = source.Clone();
        ProjectStorage.ValidateForDxfExport(snapshot);
        if (!DocumentSession.ContentEquals(snapshot, source)) throw new InvalidOperationException("Proje hazırlanırken değişti; incelemeyi yeniden başlatın.");
        var analysis = DxfFormat.AnalyzeExport(snapshot,cancellationToken);
        if (analysis.Layers.Length == 0) throw new InvalidDataException("DXF için en az bir parça gereklidir.");
        var used = analysis.Layers.Select(layer=>layer.LayerName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        used.Add("0");
        var items = ImmutableArray.CreateBuilder<DxfGrainlineReviewItem>(snapshot.Pieces.Count);
        var omissions = analysis.Omissions.Where(o=>o.Kind!=DxfExportOmissionKind.GrainlineUnsupported).ToList();
        for (int i=0;i<snapshot.Pieces.Count;i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var piece=snapshot.Pieces[i]; string? marker=null;
            if (piece.Grainline is not null)
            {
                var baseName="TCD_GRAIN_"+piece.Id.ToString("N");marker=baseName;int suffix=1;
                while(!used.Add(marker)) { cancellationToken.ThrowIfCancellationRequested(); marker=baseName+"_"+(suffix++).ToString(CultureInfo.InvariantCulture); }
                omissions.Add(new(piece.Id,DxfExportOmissionKind.GrainlineReviewState));
            }
            omissions.Add(new(piece.Id,DxfExportOmissionKind.PieceIdentity));
            items.Add(new(piece.Id,piece.Name,analysis.Layers[i].LayerName,marker,piece.Grainline?.Start,piece.Grainline?.End,piece.Grainline?.NeedsReview));
        }
        var layers=new List<string> { "0" };
        layers.AddRange(analysis.Layers.Select(l=>l.LayerName).Where(name=>!string.Equals(name,"0",StringComparison.OrdinalIgnoreCase)));
        layers.AddRange(items.Where(item=>item.MarkerLayer is not null).Select(item=>item.MarkerLayer!));
        ulong next=0x10;string Handle()=>(next++).ToString("X",CultureInfo.InvariantCulture);
        var ltypeTable=Handle();var continuous=Handle();var layerTable=Handle();
        var layerHandles=layers.Select(_=>Handle()).ToArray();
        var contourHandles=items.Select(_=>Handle()).ToArray();
        var markerHandles=items.Select(item=>item.MarkerLayer is null ? null : Handle()).ToArray();
        var seed=next.ToString("X",CultureInfo.InvariantCulture);
        var b=new StringBuilder("0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1021\n9\n$INSUNITS\n70\n4\n9\n$HANDSEED\n5\n");
        b.Append(seed).Append("\n0\nENDSEC\n0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLTYPE\n5\n").Append(ltypeTable)
            .Append("\n330\n0\n100\nAcDbSymbolTable\n70\n1\n0\nLTYPE\n5\n").Append(continuous)
            .Append("\n330\n").Append(ltypeTable).Append("\n100\nAcDbSymbolTableRecord\n100\nAcDbLinetypeTableRecord\n2\nCONTINUOUS\n70\n0\n3\nSolid line\n72\n65\n73\n0\n40\n0\n0\nENDTAB\n0\nTABLE\n2\nLAYER\n5\n").Append(layerTable)
            .Append("\n330\n0\n100\nAcDbSymbolTable\n70\n").Append(layers.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        for(int i=0;i<layers.Count;i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            b.Append("0\nLAYER\n5\n").Append(layerHandles[i]).Append("\n330\n").Append(layerTable)
                .Append("\n100\nAcDbSymbolTableRecord\n100\nAcDbLayerTableRecord\n2\n").Append(layers[i]).Append("\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n");
        }
        b.Append("0\nENDTAB\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n");
        for(int i=0;i<snapshot.Pieces.Count;i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var piece=snapshot.Pieces[i]; var item=items[i];
            b.Append("0\nLWPOLYLINE\n5\n").Append(contourHandles[i]).Append("\n100\nAcDbEntity\n8\n").Append(item.ContourLayer)
                .Append("\n100\nAcDbPolyline\n90\n").Append(piece.Points.Count.ToString(CultureInfo.InvariantCulture)).Append("\n70\n").Append(piece.Closed ? 1 : 0).Append('\n');
            for(int j=0;j<piece.Points.Count;j++)
            {
                if((j&255)==0)cancellationToken.ThrowIfCancellationRequested();
                var point=piece.Points[j];b.Append("10\n").Append(Number(point.X)).Append("\n20\n").Append(Number(point.Y)).Append('\n');
            }
            if(item.MarkerLayer is not null)
            {
                var a=item.Start!.Value;var z=item.End!.Value;
                b.Append("0\nLINE\n5\n").Append(markerHandles[i]).Append("\n100\nAcDbEntity\n8\n").Append(item.MarkerLayer)
                    .Append("\n100\nAcDbLine\n10\n").Append(Number(a.X)).Append("\n20\n").Append(Number(a.Y)).Append("\n30\n0\n11\n").Append(Number(z.X)).Append("\n21\n").Append(Number(z.Y)).Append("\n31\n0\n");
            }
            if(b.Length>MaxBytes)throw new InvalidDataException("DXF boyut sınırını aşıyor.");
        }
        b.Append("0\nENDSEC\n0\nEOF\n");
        cancellationToken.ThrowIfCancellationRequested();
        var text=b.ToString();var utf8=new UTF8Encoding(false,true);int byteLength=utf8.GetByteCount(text);
        if(byteLength>MaxBytes)throw new InvalidDataException("DXF boyut sınırını aşıyor.");
        var bytes=utf8.GetBytes(text);var hash=Convert.ToHexString(SHA256.HashData(bytes));
        cancellationToken.ThrowIfCancellationRequested();
        if(!DocumentSession.ContentEquals(snapshot,source))throw new InvalidOperationException("Proje hazırlanırken değişti; incelemeyi yeniden başlatın.");
        return new(snapshot,text,analysis.PieceFingerprint,items.MoveToImmutable(),omissions.ToImmutableArray(),hash,byteLength);
    }
    public static void Write(DxfGrainlineReviewPlan plan,string path,ProjectFileVersion expected,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(plan);ArgumentNullException.ThrowIfNull(expected);
        cancellationToken.ThrowIfCancellationRequested();
        ProjectStorage.AtomicWriteIfUnchanged(path,plan.Text,expected,cancellationToken);
    }
}
