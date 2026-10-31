using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

/// <summary>One review-only point and its source vertex; DXF does not preserve this association.</summary>
public sealed record DxfNotchReviewItem(Guid PieceId, int VertexIndex, Point2 Position, string ContourLayer, string MarkerLayer);

/// <summary>Detached review bytes and evidence, not an instruction or certificate for a machine.</summary>
public sealed class DxfNotchReviewPlan
{
    private readonly ProjectDocument source;
    internal string Text { get; }
    internal DxfNotchReviewPlan(ProjectDocument source, string text, string markerLayer,
        ImmutableArray<DxfNotchReviewItem> items, string sha256, int byteLength)
    { this.source=source;Text=text;MarkerLayer=markerLayer;Items=items;Sha256=sha256;ByteLength=byteLength; }
    public string MarkerLayer { get; }
    public ImmutableArray<DxfNotchReviewItem> Items { get; }
    public string Sha256 { get; }
    public int ByteLength { get; }
    public string Warning => DxfNotchReviewExport.Warning;
    /// <summary>The caller must prevent source mutation during this comparison.</summary>
    public bool Matches(ProjectDocument current) => current is not null && DocumentSession.ContentEquals(source,current);
}

/// <summary>
/// Explicit review-only DXF: marks are POINT entities on a distinct layer and carry
/// no physical size, cut depth, machine instruction or standardized notch identity.
/// Some CAD programs may hide or discard POINT entities; cutters may act on them.
/// The caller must keep the source stable throughout preparation.
/// </summary>
public static class DxfNotchReviewExport
{
    public const string Warning = "Yalnız inceleme içindir; DXF noktaları fiziksel çentik veya kesim komutu değildir. Hedef CAD'de nokta görünürlüğünü, ölçeği (mm) ve kaynak konturla eşleşmeyi kontrol edin; makineye göndermeyin. Parça kimliği, işaret ilişkisi ve diğer proje bilgileri DXF'te korunmaz.";
    private const int MaxBytes=64*1024*1024;
    private static string Number(double value)=>value.ToString("G17",CultureInfo.InvariantCulture);

    public static DxfNotchReviewPlan Prepare(ProjectDocument source,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        ProjectStorage.ValidateForDxfExport(source);
        GrainlineExportGuard.EnsureSupported(source);
        var snapshot=source.Clone();
        ProjectStorage.ValidateForDxfExport(snapshot);
        GrainlineExportGuard.EnsureSupported(snapshot);
        if(!DocumentSession.ContentEquals(source,snapshot))throw new InvalidOperationException("Proje hazırlanırken değişti; incelemeyi yeniden başlatın.");
        var mapping=DxfFormat.PlanLayers(snapshot);
        if(mapping.Count==0 || !snapshot.Pieces.Any(p=>p.Notches is { Count:>0 }))
            throw new InvalidDataException("Çentik incelemesi için en az bir işaretli parça gerekir.");
        var used=mapping.Select(x=>x.LayerName).ToHashSet(StringComparer.OrdinalIgnoreCase);used.Add("0");
        const string baseLayer="TCD_REVIEW_NOTCH";
        var markerLayer=baseLayer;int suffix=1;
        while(!used.Add(markerLayer)) { cancellationToken.ThrowIfCancellationRequested();markerLayer=baseLayer+"_"+(suffix++).ToString(CultureInfo.InvariantCulture); }
        var items=ImmutableArray.CreateBuilder<DxfNotchReviewItem>();
        for(int i=0;i<snapshot.Pieces.Count;i++)
            if(snapshot.Pieces[i].Notches is { } marks)
                foreach(var mark in marks)
                { cancellationToken.ThrowIfCancellationRequested();items.Add(new(snapshot.Pieces[i].Id,mark.VertexIndex,mark.Position,mapping[i].LayerName,markerLayer)); }
        var layers=new List<string> { "0" };
        layers.AddRange(mapping.Select(x=>x.LayerName).Where(x=>!x.Equals("0",StringComparison.OrdinalIgnoreCase)));
        layers.Add(markerLayer);
        ulong next=0x10;string Handle()=>(next++).ToString("X",CultureInfo.InvariantCulture);
        var ltypeTable=Handle();var continuous=Handle();var layerTable=Handle();
        var layerHandles=layers.Select(_=>Handle()).ToArray();
        var contourHandles=mapping.Select(_=>Handle()).ToArray();
        var markHandles=items.Select(_=>Handle()).ToArray();
        var seed=next.ToString("X",CultureInfo.InvariantCulture);
        var b=new StringBuilder("999\nREVIEW ONLY - POINTS ARE NOT CUT INSTRUCTIONS - VERIFY UNITS MM\n0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1021\n9\n$INSUNITS\n70\n4\n9\n$HANDSEED\n5\n");
        b.Append(seed).Append("\n0\nENDSEC\n0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLTYPE\n5\n").Append(ltypeTable)
            .Append("\n330\n0\n100\nAcDbSymbolTable\n70\n1\n0\nLTYPE\n5\n").Append(continuous)
            .Append("\n330\n").Append(ltypeTable).Append("\n100\nAcDbSymbolTableRecord\n100\nAcDbLinetypeTableRecord\n2\nCONTINUOUS\n70\n0\n3\nSolid line\n72\n65\n73\n0\n40\n0\n0\nENDTAB\n0\nTABLE\n2\nLAYER\n5\n").Append(layerTable)
            .Append("\n330\n0\n100\nAcDbSymbolTable\n70\n").Append(layers.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        for(int i=0;i<layers.Count;i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            b.Append("0\nLAYER\n5\n").Append(layerHandles[i]).Append("\n330\n").Append(layerTable)
                .Append("\n100\nAcDbSymbolTableRecord\n100\nAcDbLayerTableRecord\n2\n").Append(layers[i]).Append("\n70\n0\n62\n7\n290\n").Append(i==layers.Count-1 ? "0" : "1").Append("\n6\nCONTINUOUS\n");
        }
        b.Append("0\nENDTAB\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n");
        for(int i=0;i<snapshot.Pieces.Count;i++)
        {
            cancellationToken.ThrowIfCancellationRequested();var piece=snapshot.Pieces[i];
            b.Append("0\nLWPOLYLINE\n5\n").Append(contourHandles[i]).Append("\n100\nAcDbEntity\n8\n").Append(mapping[i].LayerName)
                .Append("\n100\nAcDbPolyline\n90\n").Append(piece.Points.Count.ToString(CultureInfo.InvariantCulture)).Append("\n70\n").Append(piece.Closed?1:0).Append('\n');
            foreach(var point in piece.Points)
            { cancellationToken.ThrowIfCancellationRequested();b.Append("10\n").Append(Number(point.X)).Append("\n20\n").Append(Number(point.Y)).Append('\n'); }
            if(b.Length>MaxBytes)throw new InvalidDataException("DXF boyut sınırını aşıyor.");
        }
        for(int i=0;i<items.Count;i++)
        {
            cancellationToken.ThrowIfCancellationRequested();var mark=items[i];
            b.Append("0\nPOINT\n5\n").Append(markHandles[i]).Append("\n100\nAcDbEntity\n8\n").Append(markerLayer)
                .Append("\n100\nAcDbPoint\n10\n").Append(Number(mark.Position.X)).Append("\n20\n").Append(Number(mark.Position.Y)).Append("\n30\n0\n");
            if(b.Length>MaxBytes)throw new InvalidDataException("DXF boyut sınırını aşıyor.");
        }
        b.Append("0\nENDSEC\n0\nEOF\n");cancellationToken.ThrowIfCancellationRequested();
        var text=b.ToString();var utf8=new UTF8Encoding(false,true);var byteLength=utf8.GetByteCount(text);
        if(byteLength>MaxBytes)throw new InvalidDataException("DXF boyut sınırını aşıyor.");
        var sha=Convert.ToHexString(SHA256.HashData(utf8.GetBytes(text)));
        cancellationToken.ThrowIfCancellationRequested();
        if(!DocumentSession.ContentEquals(source,snapshot))throw new InvalidOperationException("Proje hazırlanırken değişti; incelemeyi yeniden başlatın.");
        return new(snapshot,text,markerLayer,items.ToImmutable(),sha,byteLength);
    }

    /// <summary>Writes the reviewed bytes only if the destination still matches its observed version.</summary>
    public static void Write(DxfNotchReviewPlan plan,string path,ProjectFileVersion expected,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(plan);ArgumentNullException.ThrowIfNull(expected);
        cancellationToken.ThrowIfCancellationRequested();
        ProjectStorage.AtomicWriteIfUnchanged(path,plan.Text,expected,cancellationToken);
    }
}
