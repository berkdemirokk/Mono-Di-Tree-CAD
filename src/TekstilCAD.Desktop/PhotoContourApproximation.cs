using System.IO;
using OpenCvSharp;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Opt-in pixel contour approximation; never establishes a physical tolerance.</summary>
internal static class PhotoContourApproximation
{
    internal const double PixelTolerance=1;
    internal sealed record Result(Point[] Points,double MaximumVertexDistance);
    internal static Result Create(Point[] source,int maximumPoints,CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);token.ThrowIfCancellationRequested();
        if(source.Length<3 || source.Length>LargeContourValidator.MaximumStoredPoints || maximumPoints<3)throw new InvalidDataException("Sınır sadeleştirme için desteklenen nokta sayısı dışında.");
        var original=source.Select(p=>new Point2(p.X,p.Y)).ToArray();
        LargeContourValidator.Validate(original,token);
        token.ThrowIfCancellationRequested();var candidate=Cv2.ApproxPolyDP(source,PixelTolerance,true);token.ThrowIfCancellationRequested();
        if(candidate.Length<3 || candidate.Length>maximumPoints || candidate.Length>=source.Length)throw new InvalidDataException("1 piksel sadeleştirme bu sınırı desteklenen nokta sayısına indiremedi.");
        LargeContourValidator.Validate(candidate.Select(p=>new Point2(p.X,p.Y)).ToArray(),token);
        var maximumSquared=0d;
        foreach(var p in source)
        {
            token.ThrowIfCancellationRequested();var closest=double.PositiveInfinity;
            for(var i=0;i<candidate.Length;i++)
            {
                var a=candidate[i];var b=candidate[(i+1)%candidate.Length];var dx=(double)b.X-a.X;var dy=(double)b.Y-a.Y;
                var t=Math.Clamp(((p.X-a.X)*dx+(p.Y-a.Y)*dy)/(dx*dx+dy*dy),0,1);
                var x=p.X-(a.X+t*dx);var y=p.Y-(a.Y+t*dy);closest=Math.Min(closest,x*x+y*y);
            }
            maximumSquared=Math.Max(maximumSquared,closest);
            if(maximumSquared>PixelTolerance*PixelTolerance+1e-12)throw new InvalidDataException("Sadeleştirilmiş sınır 1 piksel nokta sapması sınırını aşıyor.");
        }
        token.ThrowIfCancellationRequested();return new Result(candidate,Math.Sqrt(maximumSquared));
    }
}
