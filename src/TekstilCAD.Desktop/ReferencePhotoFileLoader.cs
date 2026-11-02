using System.IO;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>Reads a bounded, detached source image; never writes to the source.</summary>
internal static class ReferencePhotoFileLoader
{
    internal static ReferencePhotoPreview Load(string path)
    {
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(file.Length>DesignRequestStorage.MaximumReferenceBytes)throw new InvalidDataException("Görsel 8 MiB sınırını aşıyor.");
        using var bytes=new MemoryStream();var buffer=new byte[81920];int count;
        while((count=file.Read(buffer,0,buffer.Length))>0)
        {
            if(bytes.Length+count>DesignRequestStorage.MaximumReferenceBytes)throw new InvalidDataException("Görsel 8 MiB sınırını aşıyor.");
            bytes.Write(buffer,0,count);
        }
        return ReferencePhotoDecoder.DecodeWithGeometry(bytes.ToArray());
    }
}
