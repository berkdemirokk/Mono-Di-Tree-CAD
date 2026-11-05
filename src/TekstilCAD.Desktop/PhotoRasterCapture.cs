using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Captures a WPF preview rectangle as a detached, white-composited BGR buffer.</summary>
internal static class PhotoRasterCapture
{
    internal static byte[] CaptureBgr(BitmapSource image,Int32Rect rectangle,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(image);token.ThrowIfCancellationRequested();
        // Frozen source can be used by a background worker; do not silently
        // cross a WPF dispatcher boundary for an unfrozen image.
        if(!image.IsFrozen && !image.CheckAccess())throw new InvalidOperationException("Önizleme iş parçacığı için sabitlenmelidir.");
        if(rectangle.X<0 || rectangle.Y<0 || rectangle.Width<1 || rectangle.Height<1 ||
           (long)rectangle.X+rectangle.Width>image.PixelWidth || (long)rectangle.Y+rectangle.Height>image.PixelHeight ||
           rectangle.Width>PhotoRasterConversion.MaximumDimension || rectangle.Height>PhotoRasterConversion.MaximumDimension ||
           (long)rectangle.Width*rectangle.Height>PhotoRasterConversion.MaximumPixels)
            throw new ArgumentException("Önizleme alanı geçersiz veya çok büyük.",nameof(rectangle));
        var converted=image.Format==PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);
        var stride=checked(rectangle.Width*4);var bytes=new byte[checked(stride*rectangle.Height)];
        converted.CopyPixels(rectangle,bytes,stride,0);token.ThrowIfCancellationRequested();
        return PhotoRasterConversion.BgraToBgrOnWhite(bytes,rectangle.Width,rectangle.Height,token);
    }
}
