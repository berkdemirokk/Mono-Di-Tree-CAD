using System.IO;
using OpenCvSharp;
using PhotoRegionSharpPrototype;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Opt-in pixel contour approximation; never establishes a physical tolerance.</summary>
internal static class PhotoContourApproximation
{
    internal const double PixelTolerance=1;
    internal const int MaximumRawPoints=100_000;
    internal sealed record Result(Point[] Points,double MaximumVertexDistance);
    internal static Result Create(Point[] source,int maximumPoints,CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);token.ThrowIfCancellationRequested();
        if(source.Length<3 || source.Length>MaximumRawPoints || maximumPoints<3)throw new InvalidDataException("Sınır sadeleştirme için desteklenen nokta sayısı dışında.");
        // Reduce only strictly forward collinear integer vertices. The full raw
        // path is still checked for duplicates and used for every error bound.
        var exact=ContourCollinearReduction.Reduce(source,token);
        if(source.Length>LargeContourValidator.MaximumStoredPoints)
        {
            var unique=new HashSet<Point>();
            for(var i=0;i<source.Length;i++) { if((i & 1023)==0)token.ThrowIfCancellationRequested();if(!unique.Add(source[i]))throw new InvalidDataException("Kaynak sınırında tekrarlanan nokta var."); }
        }
        var validationSource=source.Length<=LargeContourValidator.MaximumStoredPoints ? source : exact;
        if(validationSource.Length>LargeContourValidator.MaximumStoredPoints)throw new InvalidDataException("Kayıpsız azaltılmış sınır geometri denetimi için çok ayrıntılı.");
        LargeContourValidator.Validate(validationSource.Select(p=>new Point2(p.X,p.Y)).ToArray(),token);
        token.ThrowIfCancellationRequested();var candidate=Cv2.ApproxPolyDP(source,PixelTolerance,true);token.ThrowIfCancellationRequested();
        if(candidate.Length<3 || candidate.Length>maximumPoints || candidate.Length>=source.Length)throw new InvalidDataException("1 piksel sadeleştirme bu sınırı desteklenen nokta sayısına indiremedi.");
        LargeContourValidator.Validate(candidate.Select(p=>new Point2(p.X,p.Y)).ToArray(),token);
        var maximum=MeasureCorresponding(source,candidate,token);
        if(maximum>PixelTolerance)throw new InvalidDataException("Sadeleştirilmiş sınır karşılık gelen kenarda 1 piksel sapma sınırını aşıyor.");
        return new Result(candidate,maximum);
    }
    // Every original vertex is matched to the chord replacing its own ordered
    // subchain. Segment-distance convexity bounds each original edge; continuity
    // of projection between shared endpoints also bounds each candidate chord.
    internal static double MeasureCorresponding(Point[] source,Point[] candidate,CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(candidate);token.ThrowIfCancellationRequested();
        if(source.Length<3 || source.Length>MaximumRawPoints || candidate.Length<3 || candidate.Length>source.Length)
            throw new InvalidDataException("Sınır eşleme boyutu geçersiz.");
        var positions=new Dictionary<Point,int>(source.Length);
        for(var i=0;i<source.Length;i++) { if((i & 1023)==0)token.ThrowIfCancellationRequested();if(!positions.TryAdd(source[i],i))throw new InvalidDataException("Kaynak sınırında tekrarlanan nokta var."); }
        var indices=new int[candidate.Length];
        for(var i=0;i<candidate.Length;i++)if(!positions.TryGetValue(candidate[i],out indices[i]))throw new InvalidDataException("Öneri noktası kaynak sınırında yok.");
        var total=0;var maximumSquared=0d;
        for(var i=0;i<indices.Length;i++)
        {
            token.ThrowIfCancellationRequested();var start=indices[i];var end=indices[(i+1)%indices.Length];
            var count=(end-start+source.Length)%source.Length;if(count==0)throw new InvalidDataException("Öneri noktası tekrarlanmış.");
            total+=count;if(total>source.Length)throw new InvalidDataException("Öneri kaynak nokta sırasını korumuyor.");
            var a=candidate[i];var b=candidate[(i+1)%candidate.Length];var dx=(double)b.X-a.X;var dy=(double)b.Y-a.Y;var denominator=dx*dx+dy*dy;
            if(denominator==0)throw new InvalidDataException("Öneride sıfır uzunluklu kenar var.");
            for(var j=0;j<count;j++)
            {
                if((j & 1023)==0)token.ThrowIfCancellationRequested();var p=source[(start+j)%source.Length];
                var t=Math.Clamp(((p.X-a.X)*dx+(p.Y-a.Y)*dy)/denominator,0,1);
                var x=p.X-(a.X+t*dx);var y=p.Y-(a.Y+t*dy);maximumSquared=Math.Max(maximumSquared,x*x+y*y);
            }
        }
        if(total!=source.Length)throw new InvalidDataException("Öneri kaynak sınırını bir kez dolaşmıyor.");
        token.ThrowIfCancellationRequested();return Math.Sqrt(maximumSquared);
    }
}