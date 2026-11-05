using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoRegionSharpPrototype;

namespace TekstilCAD.Desktop;

/// <summary>A local review checkpoint bound to decoded BGR pixels, not a CAD pattern.</summary>
internal static class PhotoRegionSession
{
    private const int MaximumBytes=3_000_000;
    private static readonly JsonSerializerOptions Options=new() { UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow };
    internal sealed record Data(int Version,int Width,int Height,string ImageHash,int X,int Y,int RectangleWidth,int RectangleHeight,byte[] Labels,byte[] AppliedMarks);

    internal static string Encode(RegionProposal.Snapshot snapshot,byte[]? applied,int x,int y,int width,int height,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();ArgumentNullException.ThrowIfNull(snapshot);
        var data=new Data(1,snapshot.Width,snapshot.Height,snapshot.ImageHash,x,y,width,height,snapshot.CopyLabels(),applied is null ? new byte[checked(snapshot.Width*snapshot.Height)] : (byte[])applied.Clone());
        Validate(data,null,token);var json=JsonSerializer.Serialize(data,Options);token.ThrowIfCancellationRequested();
        if(System.Text.Encoding.UTF8.GetByteCount(json)>MaximumBytes)throw new InvalidDataException("Bölge dosyası boyut sınırını aşıyor.");
        return json;
    }
    internal static Data Decode(string path,byte[] bgr,int width,int height,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();ArgumentNullException.ThrowIfNull(bgr);
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length>MaximumBytes)throw new InvalidDataException("Bölge dosyası boyut sınırını aşıyor.");
        using var memory=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=stream.Read(buffer,0,buffer.Length))>0) { token.ThrowIfCancellationRequested();if(memory.Length+count>MaximumBytes)throw new InvalidDataException("Bölge dosyası boyut sınırını aşıyor.");memory.Write(buffer,0,count); }
        var bytes=memory.ToArray();
        using var document=JsonDocument.Parse(bytes);
        if(document.RootElement.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Bölge dosyası geçersiz.");
        string[] fields=["Version","Width","Height","ImageHash","X","Y","RectangleWidth","RectangleHeight","Labels","AppliedMarks"];
        var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in document.RootElement.EnumerateObject())if(!fields.Contains(item.Name,StringComparer.Ordinal) || !names.Add(item.Name))throw new InvalidDataException("Bölge dosyası alanları geçersiz veya yinelenmiş.");
        if(names.Count!=fields.Length)throw new InvalidDataException("Bölge dosyasında alan eksik.");
        var data=JsonSerializer.Deserialize<Data>(bytes,Options) ?? throw new InvalidDataException("Bölge dosyası geçersiz.");
        if(data.Width!=width || data.Height!=height || bgr.LongLength!=(long)width*height*3)throw new InvalidDataException("Bölge dosyası bu fotoğraf boyutuna ait değil.");
        Validate(data,Convert.ToHexString(SHA256.HashData(bgr)),token);return data;
    }
    private static void Validate(Data data,string? expectedHash,CancellationToken token)
    {
        if(data.Version!=1 || data.Width<3 || data.Height<3 || data.Width>RegionProposal.MaxDimension || data.Height>RegionProposal.MaxDimension || (long)data.Width*data.Height>RegionProposal.MaxPixels ||
           data.ImageHash is null || data.ImageHash.Length!=64 || !data.ImageHash.All(Uri.IsHexDigit) || (expectedHash is not null && !string.Equals(data.ImageHash,expectedHash,StringComparison.OrdinalIgnoreCase)) ||
           data.X<0 || data.Y<0 || data.RectangleWidth<2 || data.RectangleHeight<2 || (long)data.X+data.RectangleWidth>data.Width || (long)data.Y+data.RectangleHeight>data.Height ||
           (long)data.RectangleWidth*data.RectangleHeight<5 || (long)data.Width*data.Height-(long)data.RectangleWidth*data.RectangleHeight<5 ||
           data.Labels is null || data.AppliedMarks is null || data.Labels.LongLength!=(long)data.Width*data.Height || data.AppliedMarks.LongLength!=(long)data.Width*data.Height)
            throw new InvalidDataException("Bölge dosyası geçersiz veya farklı fotoğrafa ait.");
        bool foreground=false,background=false;
        for(var i=0;i<data.Labels.Length;i++) {
            if((i & 4095)==0)token.ThrowIfCancellationRequested();
            var label=data.Labels[i];var mark=data.AppliedMarks[i];
            if(label>3 || mark>2 || (mark==1 && label!=1) || (mark==2 && label!=0))throw new InvalidDataException("Bölge dosyası maske veya işaretleri geçersiz.");
            if(label is 1 or 3)foreground=true;else background=true;
        }
        if(!foreground || !background)throw new InvalidDataException("Bölge dosyasında ön ve arka plan bulunmalı.");
    }
}
