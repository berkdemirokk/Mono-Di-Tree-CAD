using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

internal static class PhotoTraceSession
{
    internal sealed record Pixel(double X,double Y);
    internal sealed record Data(int Version,string Sha256,int ByteLength,int Orientation,int SourceWidth,int SourceHeight,int PreviewWidth,int PreviewHeight,
        double? PlaneWidth,double? PlaneHeight,string Length,string XInput,string YInput,bool Closure,bool Color,bool Wide,Pixel[] Points);
    private static readonly JsonSerializerOptions Options=new() { UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=8 };
    internal static string Encode(Data data) { Validate(data);return JsonSerializer.Serialize(data,Options); }
    internal static Data Decode(string path,CancellationToken token)
    {
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length>131072)throw new InvalidDataException("Taslak dosyası çok büyük.");
        using var memory=new MemoryStream();var buffer=new byte[8192];int n;
        while((n=stream.Read(buffer,0,buffer.Length))>0) { token.ThrowIfCancellationRequested();if(memory.Length+n>131072)throw new InvalidDataException("Taslak dosyası çok büyük.");memory.Write(buffer,0,n); }
        token.ThrowIfCancellationRequested();using var doc=JsonDocument.Parse(memory.ToArray(),new JsonDocumentOptions { MaxDepth=8 });
        static void Unique(JsonElement e) { if(e.ValueKind==JsonValueKind.Object) { var names=new HashSet<string>(StringComparer.Ordinal);foreach(var p in e.EnumerateObject()) { if(!names.Add(p.Name))throw new InvalidDataException("Tekrarlanan alan var.");Unique(p.Value); } } else if(e.ValueKind==JsonValueKind.Array)foreach(var x in e.EnumerateArray())Unique(x); }
        Unique(doc.RootElement);
        if(doc.RootElement.ValueKind!=JsonValueKind.Object || doc.RootElement.EnumerateObject().Count()!=17)throw new InvalidDataException("Taslak alanları eksik veya fazla.");
        if(doc.RootElement.TryGetProperty("Points",out var points) && points.ValueKind==JsonValueKind.Array)foreach(var point in points.EnumerateArray())if(point.ValueKind!=JsonValueKind.Object || point.EnumerateObject().Count()!=2 || !point.TryGetProperty("X",out _) || !point.TryGetProperty("Y",out _))throw new InvalidDataException("Nokta alanları eksik veya fazla.");
        var data=doc.RootElement.Deserialize<Data>(Options) ?? throw new InvalidDataException("Taslak boş.");Validate(data);return data;
    }
    internal static void Validate(Data d)
    {
        if(d.Version!=1 || d.Sha256 is null || d.Sha256.Length!=64 || d.Sha256.Any(c=>!Uri.IsHexDigit(c)) || d.ByteLength<=0 || d.Orientation is <1 or >8 || d.SourceWidth<=0 || d.SourceHeight<=0 || d.PreviewWidth<=0 || d.PreviewHeight<=0 ||
           d.Length is null || d.Length.Length>32 || d.XInput is null || d.XInput.Length>24 || d.YInput is null || d.YInput.Length>24 || d.Points is null || d.Points.Length>PhotoTraceCanvas.MaximumPoints || d.PlaneWidth.HasValue!=d.PlaneHeight.HasValue)
            throw new InvalidDataException("Taslak alanları geçersiz.");
        if(d.PlaneWidth is double w && d.PlaneHeight is double h && (!double.IsFinite(w) || !double.IsFinite(h) || w<=0 || h<=0 || w>Geometry.MaxCoordinate || h>Geometry.MaxCoordinate || d.Length!=MeasurementInput.Format(w)))throw new InvalidDataException("Düzlem ölçüsü geçersiz.");
        var reference=d.PlaneWidth.HasValue ? 4 : 2;
        for(var i=0;i<d.Points.Length;i++) { var p=d.Points[i];if(p is null || !double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X<0 || p.Y<0 || p.X>d.PreviewWidth-1 || p.Y>d.PreviewHeight-1 || (i>0 && i!=reference && p==d.Points[i-1]))throw new InvalidDataException("Taslak noktası geçersiz."); }
    }
    internal static string SavePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);var name=Path.GetFileName(path);
        if(string.IsNullOrWhiteSpace(name) || name.EndsWith('.') || char.IsWhiteSpace(name[^1]))throw new InvalidDataException("Dosya adı geçersiz.");
        var ext=Path.GetExtension(name);if(ext.Length==0)path+=".tdtrace";else if(!ext.Equals(".tdtrace",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Dosya uzantısı .tdtrace olmalı.");return Path.GetFullPath(path);
    }
}
