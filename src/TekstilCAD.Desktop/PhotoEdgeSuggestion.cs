using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TekstilCAD.Desktop;

/// <summary>A local contrast hint, not garment segmentation or a pattern.</summary>
internal static class PhotoEdgeSuggestion
{
    internal static bool TryFind(BitmapSource source,Point selected,out Point suggestion)
    {
        suggestion=default;
        if(source.PixelWidth<3 || source.PixelHeight<3 || !double.IsFinite(selected.X) || !double.IsFinite(selected.Y) ||
           selected.X<0 || selected.Y<0 || selected.X>source.PixelWidth-1 || selected.Y>source.PixelHeight-1)return false;
        const int radius=8;
        int left=Math.Max(1,(int)Math.Ceiling(selected.X-radius)),right=Math.Min(source.PixelWidth-2,(int)Math.Floor(selected.X+radius));
        int top=Math.Max(1,(int)Math.Ceiling(selected.Y-radius)),bottom=Math.Min(source.PixelHeight-2,(int)Math.Floor(selected.Y+radius));
        if(left>right || top>bottom)return false;
        var converted=source.Format==PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);
        var rect=new Int32Rect(left-1,top-1,right-left+3,bottom-top+3);
        int stride=checked(rect.Width*4);var pixels=new byte[checked(stride*rect.Height)];converted.CopyPixels(rect,pixels,stride,0);
        int Luma(int x,int y)
        {
            int i=(y-(top-1))*stride+(x-(left-1))*4;int a=pixels[i+3];
            // Composite transparency over the white tracing canvas before measuring contrast.
            int gray=(29*pixels[i]+150*pixels[i+1]+77*pixels[i+2]+128)>>8;
            return (gray*a+255*(255-a)+127)/255;
        }
        int bestStrength=0;double bestDistance=double.PositiveInfinity;
        for(int y=top;y<=bottom;y++)for(int x=left;x<=right;x++)
        {
            double dx=x-selected.X,dy=y-selected.Y,distance=dx*dx+dy*dy;if(distance>radius*radius)continue;
            int gx=Luma(x+1,y)-Luma(x-1,y),gy=Luma(x,y+1)-Luma(x,y-1),strength=gx*gx+gy*gy;
            // Ignore weak contrast. Prefer the nearest qualifying edge; strength
            // breaks ties deterministically, then scan order breaks exact ties.
            if(strength<32*32 || distance>bestDistance || (distance==bestDistance && strength<=bestStrength))continue;
            bestDistance=distance;bestStrength=strength;suggestion=new Point(x,y);
        }
        return bestStrength!=0;
    }
}
