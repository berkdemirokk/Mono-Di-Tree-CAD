using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

internal sealed record ReferencePhotoPreview(BitmapSource Bitmap,int OrientedSourceWidth,int OrientedSourceHeight,int SourceOrientation,string SourceSha256,int SourceByteLength);

/// <summary>
/// Decodes a detached, reduced still-image preview; it does not interpret a garment
/// or establish measurements. JPEG EXIF orientation is applied
/// to the preview only. WIC runs in this process, so
/// these limits do not provide process isolation or a hard codec memory/time limit.
/// </summary>
internal static class ReferencePhotoDecoder
{
    internal const int MaximumDimension=12_000;
    internal const long MaximumPixels=24_000_000;
    internal const int MaximumPreviewDimension=1_024;

    internal static void ValidateV2(DesignReference reference)
    {
        var data=reference.Data;
        if(reference.MediaType=="image/png")
        {
            // Reject APNG metadata even when a decoder exposes a single frame.
            if(data.Length<8)throw new InvalidDataException("PNG eksik.");
            var offset=8;var ended=false;
            while(offset<=data.Length-12)
            {
                var length=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset,4));
                if(length>(uint)(data.Length-offset-12))throw new InvalidDataException("PNG parçası eksik.");
                var type=data.AsSpan(offset+4,4);
                if(type.SequenceEqual("acTL"u8))throw new InvalidDataException("Hareketli PNG desteklenmiyor.");
                if(type.SequenceEqual("IEND"u8))
                {
                    if(length!=0 || offset+12!=data.Length)throw new InvalidDataException("PNG sonundan sonra veri var veya son parça geçersiz.");
                    ended=true;break;
                }
                offset+=checked((int)length+12);
            }
            if(!ended)throw new InvalidDataException("PNG sonu eksik.");
        }
        _=DecodeWithGeometry(data);
    }

    internal static BitmapSource Decode(byte[] data)=>DecodeWithGeometry(data).Bitmap;
    internal static ReferencePhotoPreview DecodeWithGeometry(byte[] data)
    {
        if(data is null || data.Length==0 || data.Length>DesignRequestStorage.MaximumReferenceBytes)
            throw new InvalidDataException("Görsel boyutu önizleme sınırının dışında.");
        // Decoding must not retain or change the request's image buffer.
        var bytes=(byte[])data.Clone();
        var png=bytes.Length>=8 && bytes.AsSpan(0,8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10});
        var jpeg=bytes.Length>=3 && bytes[0]==0xff && bytes[1]==0xd8 && bytes[2]==0xff;
        if(!png && !jpeg)throw new InvalidDataException("Önizleme yalnız PNG veya JPEG dosyalarını destekler.");
        try
        {
            int width,height;var orientation=1;
            using(var header=new MemoryStream(bytes,false))
            {
                var decoder=BitmapDecoder.Create(header,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.None);
                if((png && decoder is not PngBitmapDecoder) || (jpeg && decoder is not JpegBitmapDecoder) || decoder.Frames.Count!=1)
                    throw new InvalidDataException("Görsel biçimi veya kare sayısı önizleme için desteklenmiyor.");
                width=decoder.Frames[0].PixelWidth;height=decoder.Frames[0].PixelHeight;
                if(jpeg && decoder.Frames[0].Metadata is BitmapMetadata metadata)
                {
                    var tag=metadata.GetQuery("/app1/ifd/{ushort=274}");
                    if(tag is not null)
                    {
                        if(tag is not ushort value || value is <1 or >8)
                            throw new InvalidDataException("JPEG yön bilgisi geçersiz; önizleme açılamadı.");
                        orientation=value;
                    }
                }
            }
            if(width<=0 || height<=0 || width>MaximumDimension || height>MaximumDimension || (long)width*height>MaximumPixels)
                throw new InvalidDataException("Görselin piksel boyutu önizleme sınırını aşıyor.");

            using var source=new MemoryStream(bytes,false);
            var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;
            image.CreateOptions=BitmapCreateOptions.PreservePixelFormat;image.StreamSource=source;
            if(Math.Max(width,height)>MaximumPreviewDimension)
            {
                if(width>=height)image.DecodePixelWidth=MaximumPreviewDimension;
                else image.DecodePixelHeight=MaximumPreviewDimension;
            }
            image.EndInit();
            if(image.PixelWidth<=0 || image.PixelHeight<=0 || image.PixelWidth>MaximumPreviewDimension || image.PixelHeight>MaximumPreviewDimension)
                throw new InvalidDataException("Görsel güvenli önizleme boyutuna indirgenemedi.");
            // CopyPixels forces raster decoding and detaches the result from the
            // stream and decoder; BGRA32 bounds the retained pixels to 4 MiB.
            var converted=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);
            var stride=checked(converted.PixelWidth*4);var pixels=new byte[checked(stride*converted.PixelHeight)];
            converted.CopyPixels(pixels,stride,0);
            var sourceWidth=converted.PixelWidth;var sourceHeight=converted.PixelHeight;
            var outputWidth=orientation>=5 ? sourceHeight : sourceWidth;
            var outputHeight=orientation>=5 ? sourceWidth : sourceHeight;
            if(orientation!=1)
            {
                var oriented=new byte[pixels.Length];
                for(var y=0;y<sourceHeight;y++)for(var x=0;x<sourceWidth;x++)
                {
                    var (dx,dy)=orientation switch
                    {
                        2=>(sourceWidth-1-x,y),3=>(sourceWidth-1-x,sourceHeight-1-y),4=>(x,sourceHeight-1-y),
                        5=>(y,x),6=>(sourceHeight-1-y,x),7=>(sourceHeight-1-y,sourceWidth-1-x),8=>(y,sourceWidth-1-x),
                        _=>throw new InvalidDataException("JPEG yön bilgisi geçersiz.")
                    };
                    Buffer.BlockCopy(pixels,(y*sourceWidth+x)*4,oriented,(dy*outputWidth+dx)*4,4);
                }
                pixels=oriented;
            }
            var orientedWidth=orientation>=5 ? height : width;var orientedHeight=orientation>=5 ? width : height;
            // WIC rounds the reduced dimensions independently. Use logical DPI
            // to display the reduced raster at the original oriented aspect ratio;
            // these values do not represent a physical photo or garment scale.
            var preview=BitmapSource.Create(outputWidth,outputHeight,96d*outputWidth/orientedWidth,96d*outputHeight/orientedHeight,PixelFormats.Bgra32,null,pixels,outputWidth*4);
            preview.Freeze();return new ReferencePhotoPreview(preview,orientedWidth,orientedHeight,orientation,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),bytes.Length);
        }
        catch(Exception ex) when(ex is IOException or FormatException or NotSupportedException or ArgumentException or InvalidOperationException or COMException or OverflowException)
        {
            throw new InvalidDataException("Görsel önizleme için çözümlenemedi; kayıtlı görsel değiştirilmedi.",ex);
        }
    }
}
