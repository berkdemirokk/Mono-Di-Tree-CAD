using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

public sealed record DxfNotchReviewItem(Guid PieceId,string PieceName,string ContourLayer,string MarkerLayer,int VertexIndex,Point2 Position);

/// <summary>Detached inspection data, not a machine-ready or textile-standard export.</summary>
public sealed class DxfNotchReviewPlan
{
    private readonly ProjectDocument source;
    internal string Text { get; }
    public ImmutableArray<DxfNotchReviewItem> Items { get; }
    public ImmutableArray<DxfExportOmission> Omissions { get; }
    public bool FlipY { get; }
    public string Sha256 { get; }
    internal DxfNotchReviewPlan(ProjectDocument source,string text,ImmutableArray<DxfNotchReviewItem> items,ImmutableArray<DxfExportOmission> omissions,bool flipY)
    { this.source=source;Text=text;Items=items;Omissions=omissions;FlipY=flipY;Sha256=Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false,true).GetBytes(text))); }
    public bool Matches(ProjectDocument current)=>current is not null && DocumentSession.ContentEquals(source,current);
}

/// <summary>Explicit review-only DXF; POINT markers have no cutting or textile semantics.</summary>
public static class DxfNotchReviewExport
{
    private const int MaxBytes=64*1024*1024, MaxMarkers=10_000;
    private static string Number(double n)=>n.ToString("G17",CultureInfo.InvariantCulture);
    public static DxfNotchReviewPlan Prepare(ProjectDocument source,bool flipY,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(source);cancellationToken.ThrowIfCancellationRequested();ProjectStorage.ValidateForDxfExport(source);
        var snapshot=source.Clone();ProjectStorage.ValidateForDxfExport(snapshot);
        if(!DocumentSession.ContentEquals(source,snapshot))throw new InvalidOperationException("Proje hazırlanırken değişti; incelemeyi yeniden başlatın.");
        if(snapshot.Pieces.Any(p=>p.Grainline is not null))throw new InvalidDataException("İplik yönü bu inceleme çıktısına aktarılmıyor; işaretli projede veri kaybını önlemek için çıktı hazırlanmadı.");
        var markerCount=snapshot.Pieces.Sum(p=>(long)(p.Notches?.Count ?? 0));
        if(markerCount==0)throw new InvalidDataException("İncelenecek köşe işareti bulunamadı.");
        if(markerCount>MaxMarkers)throw new InvalidDataException("İnceleme çıktısı en fazla 10.000 köşe işaretini destekler; proje değiştirilmedi.");
        // Analyze a private copy with marks removed solely to reuse the existing
        // contour/layer and omission validation. Source and output retain marks.
        var analysisCopy=snapshot.Clone();foreach(var p in analysisCopy.Pieces)p.Notches=null;
        var analysis=DxfFormat.AnalyzeExport(analysisCopy,cancellationToken);
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
        var layers=new List<string> { "0" };layers.AddRange(analysis.Layers.Select(x=>x.LayerName).Where(x=>!string.Equals(x,"0",StringComparison.OrdinalIgnoreCase)));
        layers.AddRange(markerLayers.Where(x=>x is not null).Select(x=>x!));
        ulong next=0x10;string Handle()=>(next++).ToString("X",CultureInfo.InvariantCulture);
        var ltype=Handle();var continuous=Handle();var layerTable=Handle();var layerHandles=layers.Select(_=>Handle()).ToArray();
        var contourHandles=snapshot.Pieces.Select(_=>Handle()).ToArray();var markerHandles=items.Select(_=>Handle()).ToArray();
        var b=new StringBuilder("0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1021\n9\n$INSUNITS\n70\n4\n9\n$HANDSEED\n5\n");
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
        b.Append("0\nENDSEC\n0\nEOF\n");var text=b.ToString();if(new UTF8Encoding(false,true).GetByteCount(text)>MaxBytes)throw new InvalidDataException("DXF boyut sınırını aşıyor.");
        cancellationToken.ThrowIfCancellationRequested();if(!DocumentSession.ContentEquals(source,snapshot))throw new InvalidOperationException("Proje hazırlanırken değişti; incelemeyi yeniden başlatın.");
        var omissions=analysis.Omissions.AddRange(snapshot.Pieces.Select(p=>new DxfExportOmission(p.Id,DxfExportOmissionKind.PieceIdentity)));
        return new(snapshot,text,items.ToImmutable(),omissions,flipY);
    }
    public static void Write(DxfNotchReviewPlan plan,string path,ProjectFileVersion expected,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(plan);ArgumentNullException.ThrowIfNull(expected);cancellationToken.ThrowIfCancellationRequested();
        if(expected.Sha256 is not null)throw new InvalidOperationException("İnceleme için yeni bir dosya adı seçin.");
        ProjectStorage.AtomicWriteIfUnchanged(path,plan.Text,expected,cancellationToken);
    }
}
