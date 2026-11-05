namespace TekstilCAD.Core;

/// <summary>Converts detached, straight-alpha BGRA pixels to BGR over white.</summary>
public static class PhotoRasterConversion
{
    public const int MaximumDimension=12_000;
    public const int MaximumPixels=1_048_576;

    /// <summary>
    /// Produces a new tightly packed BGR buffer. The caller must keep the source
    /// stable until the copy completes; no input bytes are modified.
    /// </summary>
    public static byte[] BgraToBgrOnWhite(ReadOnlySpan<byte> bgra,int width,int height,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        if(width<1 || height<1 || width>MaximumDimension || height>MaximumDimension ||
           (long)width*height>MaximumPixels || bgra.Length!=(long)width*height*4)
            throw new ArgumentException("Görsel piksel boyutu veya BGRA veri uzunluğu geçersiz.");
        var count=checked(width*height);var result=new byte[checked(count*3)];
        for(var i=0;i<count;i++)
        {
            if((i & 4095)==0)token.ThrowIfCancellationRequested();
            var source=i*4;var target=i*3;var alpha=bgra[source+3];
            for(var channel=0;channel<3;channel++)
                result[target+channel]=(byte)((bgra[source+channel]*alpha+255*(255-alpha)+127)/255);
        }
        token.ThrowIfCancellationRequested();return result;
    }
}
