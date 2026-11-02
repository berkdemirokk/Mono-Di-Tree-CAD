using System.IO;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>Reads a bounded, detached source image; never writes to the source.</summary>
internal static class ReferencePhotoFileLoader
{
    internal static ReferencePhotoPreview Load(string path)
        =>ReferencePhotoDecoder.DecodeWithGeometry(ReadBytes(path));

    internal static DesignReference LoadRequestReference(string path)
    {
        var data=ReadBytes(path);
        var png=data.Length>=8 && data.AsSpan(0,8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10});
        var candidate=new DesignReference(Path.GetFileName(path),png ? "image/png" : "image/jpeg",data);
        DesignRequestStorage.Validate(new DesignRequest("","",candidate));
        // Validate with the exact decoder used by photo tracing before accepting
        // the original bytes. The reduced bitmap is not stored in the request.
        _=ReferencePhotoDecoder.DecodeWithGeometry(data);
        return candidate;
    }

    private static byte[] ReadBytes(string path)
    {
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(file.Length>DesignRequestStorage.MaximumReferenceBytes)throw new InvalidDataException("Görsel 8 MiB sınırını aşıyor.");
        using var bytes=new MemoryStream();var buffer=new byte[81920];int count;
        while((count=file.Read(buffer,0,buffer.Length))>0)
        {
            if(bytes.Length+count>DesignRequestStorage.MaximumReferenceBytes)throw new InvalidDataException("Görsel 8 MiB sınırını aşıyor.");
            bytes.Write(buffer,0,count);
        }
        return bytes.ToArray();
    }
}
