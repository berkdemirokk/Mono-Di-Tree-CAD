using TekstilCAD.Core;

namespace TekstilCAD.Patterns;

/// <summary>Measurements for explicitly paired vertices, not offset or production approval.</summary>
public sealed record SelectedNotchComparisonResult(bool WithinTolerance,double SourceWidth,double CandidateWidth,
    double SourceDepth,double CandidateDepth);

/// <summary>Compares only the two explicitly identified mouth/witness triples.</summary>
public static class SelectedNotchComparison
{
    /// <remarks>All indices are zero-based. The caller is responsible for matching the same physical feature.
    /// Neither contour validity nor the correctness of a seam allowance is established.</remarks>
    public static SelectedNotchComparisonResult Compare(IReadOnlyList<Point2> source,IReadOnlyList<Point2> candidate,
        int sourceStart,int sourceWitness,int sourceEnd,int candidateStart,int candidateWitness,int candidateEnd,
        double widthTolerance,double depthTolerance,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        static Point2[] Select(IReadOnlyList<Point2> p,int a,int b,int c)
        {
            if(a<0||b<0||c<0||a>=p.Count||b>=p.Count||c>=p.Count||a==b||a==c||b==c)
                throw new ArgumentException("Her kontur için üç farklı, geçerli nokta indeksi seçin.");
            Point2[] result=[p[a],p[b],p[c]];
            foreach(var point in result)Geometry.Validate(point);
            return result;
        }
        if(!double.IsFinite(widthTolerance)||widthTolerance<0 || !double.IsFinite(depthTolerance)||depthTolerance<0)
            throw new ArgumentException("Toleranslar sıfır veya pozitif, sonlu milimetre değerleri olmalıdır.");
        var a=Select(source,sourceStart,sourceWitness,sourceEnd);
        var b=Select(candidate,candidateStart,candidateWitness,candidateEnd);
        bool ok=OffsetUserFeatureToleranceExperiment.TryVerify(a,b,0,1,2,widthTolerance,depthTolerance,out var reason,cancellationToken);
        if(reason.StartsWith("Feature is flat",StringComparison.Ordinal))
            throw new ArgumentException("Çentik düz, ters yönlü veya seçilen dip noktası ağız uçları arasında değil.");
        static (double Width,double Depth) Measure(Point2[] p)
        {
            double dx=p[2].X-p[0].X,dy=p[2].Y-p[0].Y;
            double x=p[1].X-p[0].X,y=p[1].Y-p[0].Y;
            // Scale before squaring so valid subnormal edges do not become zero.
            double scale=Math.Max(Math.Abs(dx),Math.Abs(dy));
            if(scale==0 || !double.IsFinite(scale))throw new ArgumentException("Seçili ağız ölçüsü gösterilemiyor.");
            double ux=dx/scale,uy=dy/scale,norm=Math.Sqrt(ux*ux+uy*uy);
            double width=scale*norm,depth=Math.Abs(ux*y-uy*x)/norm;
            // The exact predicate proves nonzero dimensions; if rounded display
            // arithmetic loses either, fail rather than display zero or NaN.
            if(width<=0 || depth<=0 || !double.IsFinite(width) || !double.IsFinite(depth))
                throw new ArgumentException("Seçili ölçü gösterim hassasiyetinde güvenilir biçimde temsil edilemiyor.");
            return (width,depth);
        }
        var am=Measure(a);var bm=Measure(b);
        return new(ok,am.Width,bm.Width,am.Depth,bm.Depth);
    }
}

