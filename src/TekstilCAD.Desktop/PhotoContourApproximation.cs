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
        var measurement=MeasureCore(source,candidate,token,true);
        if(!measurement.WithinOnePixel)throw new InvalidDataException("Sadeleştirilmiş sınır 1 piksel hedefini aşıyor.");
        return new Result(candidate,measurement.Maximum);
    }
    // Every original vertex is matched to the chord replacing its own ordered
    // subchain. In exact arithmetic, convexity and continuous projection give
    // a two-way geometric bound; double calculations are approximate.
    internal static double MeasureCorresponding(Point[] source,Point[] candidate,CancellationToken token)=>MeasureCore(source,candidate,token,false).Maximum;
    internal static bool CorrespondingWithinOnePixel(Point[] source,Point[] candidate,CancellationToken token)=>MeasureCore(source,candidate,token,true).WithinOnePixel;
    private static (double Maximum,bool WithinOnePixel) MeasureCore(Point[] source,Point[] candidate,CancellationToken token,bool exactPixelTest)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(candidate);token.ThrowIfCancellationRequested();
        if(source.Length<3 || source.Length>MaximumRawPoints || candidate.Length<3 || candidate.Length>source.Length)
            throw new InvalidDataException("Sınır eşleme boyutu geçersiz.");
        if(exactPixelTest && (source.Any(p=>p.X<0 || p.Y<0 || p.X>=RegionProposal.MaxDimension || p.Y>=RegionProposal.MaxDimension) || candidate.Any(p=>p.X<0 || p.Y<0 || p.X>=RegionProposal.MaxDimension || p.Y>=RegionProposal.MaxDimension)))
            throw new InvalidDataException("Kesin piksel hesabı desteklenen görüntü sınırları dışındaki koordinatları kabul etmez.");
        var positions=new Dictionary<Point,int>(source.Length);
        for(var i=0;i<source.Length;i++) { if((i & 1023)==0)token.ThrowIfCancellationRequested();if(!positions.TryAdd(source[i],i))throw new InvalidDataException("Kaynak sınırında tekrarlanan nokta var."); }
        var indices=new int[candidate.Length];
        for(var i=0;i<candidate.Length;i++)if(!positions.TryGetValue(candidate[i],out indices[i]))throw new InvalidDataException("Öneri noktası kaynak sınırında yok.");
        var total=0;var maximumSquared=0d;var withinOnePixel=true;
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
                var t=Math.Clamp((((double)p.X-a.X)*dx+((double)p.Y-a.Y)*dy)/denominator,0,1);
                var x=p.X-(a.X+t*dx);var y=p.Y-(a.Y+t*dy);maximumSquared=Math.Max(maximumSquared,x*x+y*y);
                if(exactPixelTest)
                {
                    // Coordinates are integers in [0,2047]; every squared cross
                    // product is < 2^47 and safely fits signed 64-bit arithmetic.
                    long ex=(long)b.X-a.X,ey=(long)b.Y-a.Y,wx=(long)p.X-a.X,wy=(long)p.Y-a.Y;
                    var den=ex*ex+ey*ey;var dot=wx*ex+wy*ey;
                    if(dot<=0)withinOnePixel &= wx*wx+wy*wy<=1;
                    else if(dot>=den) { long ux=(long)p.X-b.X,uy=(long)p.Y-b.Y;withinOnePixel &= ux*ux+uy*uy<=1; }
                    else { var cross=wx*ey-wy*ex;withinOnePixel &= cross*cross<=den; }
                }
            }
        }
        if(total!=source.Length)throw new InvalidDataException("Öneri kaynak sınırını bir kez dolaşmıyor.");
        token.ThrowIfCancellationRequested();return (Math.Sqrt(maximumSquared),withinOnePixel);
    }
}