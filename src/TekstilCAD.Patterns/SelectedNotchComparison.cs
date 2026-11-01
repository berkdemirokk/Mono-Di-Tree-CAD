using System.Numerics;
using TekstilCAD.Core;

namespace TekstilCAD.Patterns;

/// <summary>Measurements for explicitly paired vertices, not offset or production approval.</summary>
public sealed record SelectedNotchComparisonResult(bool WithinTolerance,double SourceWidth,double CandidateWidth,
    double SourceDepth,double CandidateDepth);

/// <summary>Approximate displayed measurements without a tolerance verdict.</summary>
public sealed record SelectedNotchMeasurements(double SourceWidth,double CandidateWidth,double SourceDepth,double CandidateDepth);

/// <summary>Compares only the two explicitly identified mouth/witness triples.</summary>
public static class SelectedNotchComparison
{
    /// <summary>Measures two explicitly selected triples without an acceptance verdict.</summary>
    /// <remarks>The caller must choose corresponding mouth endpoints in the same order.
    /// Measurements are approximate display values, not proof of an offset or feature preservation.</remarks>
    public static SelectedNotchMeasurements Measure(IReadOnlyList<Point2> source,IReadOnlyList<Point2> candidate,
        int sourceStart,int sourceWitness,int sourceEnd,int candidateStart,int candidateWitness,int candidateEnd,
        CancellationToken cancellationToken=default)
    {
        try
        {
            var value=Compare(source,candidate,sourceStart,sourceWitness,sourceEnd,candidateStart,candidateWitness,candidateEnd,
                double.MaxValue,double.MaxValue,cancellationToken);
            return new(value.SourceWidth,value.CandidateWidth,value.SourceDepth,value.CandidateDepth);
        }
        catch(ArgumentException)
        {
            // Measurement alone may report a completely flat candidate; this
            // never changes Compare's stricter acceptance predicate.
            ArgumentNullException.ThrowIfNull(candidate);cancellationToken.ThrowIfCancellationRequested();
            if(candidateStart<0 || candidateWitness<0 || candidateEnd<0 || candidateStart>=candidate.Count || candidateWitness>=candidate.Count || candidateEnd>=candidate.Count ||
                candidateStart==candidateWitness || candidateStart==candidateEnd || candidateWitness==candidateEnd)throw;
            var a=candidate[candidateStart];var w=candidate[candidateWitness];var b=candidate[candidateEnd];
            Geometry.Validate(a);Geometry.Validate(w);Geometry.Validate(b);
            static BigInteger Units(double value)
            {
                long bits=BitConverter.DoubleToInt64Bits(value);int e=(int)((bits>>52)&0x7ff);long f=bits&0x000f_ffff_ffff_ffffL;
                var n=new BigInteger(e==0 ? f : f|(1L<<52)) << (e==0 ? 0 : e-1);return bits<0 ? -n : n;
            }
            var dx=Units(b.X)-Units(a.X);var dy=Units(b.Y)-Units(a.Y);var wx=Units(w.X)-Units(a.X);var wy=Units(w.Y)-Units(a.Y);
            var length=dx*dx+dy*dy;var dot=dx*wx+dy*wy;
            if(length.IsZero || dx*wy-dy*wx!=BigInteger.Zero || dot<=0 || dot>=length)throw;
            var width=Geometry.Distance(a,b);
            if(!double.IsFinite(width) || width<2.2250738585072014e-308)throw new ArgumentException("Aday ağız genişliği gösterilemiyor.");
            var original=Compare(source,source,sourceStart,sourceWitness,sourceEnd,sourceStart,sourceWitness,sourceEnd,
                double.MaxValue,double.MaxValue,cancellationToken);
            return new(original.SourceWidth,width,original.SourceDepth,0);
        }
    }
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

            // Scale before squaring so valid subnormal edges do not become zero.
            double scale=Math.Max(Math.Abs(dx),Math.Abs(dy));
            if(scale==0 || !double.IsFinite(scale))throw new ArgumentException("Seçili ağız ölçüsü gösterilemiyor.");
            double ux=dx/scale,uy=dy/scale,norm=Math.Sqrt(ux*ux+uy*uy);
            double width=scale*norm;
            // Compute the cross product on exact binary64 values. A floating-point
            // subtraction of nearly equal products can otherwise report a depth
            // several times too large while still remaining nonzero.
            static BigInteger Units(double value)
            {
                long bits=BitConverter.DoubleToInt64Bits(value);int exponent=(int)((bits>>52)&0x7ff);
                long fraction=bits&0x000f_ffff_ffff_ffffL;
                var mantissa=new BigInteger(exponent==0 ? fraction : fraction|(1L<<52));
                var integer=mantissa << (exponent==0 ? 0 : exponent-1);
                return bits<0 ? -integer : integer;
            }
            var ax=Units(p[0].X);var ay=Units(p[0].Y);
            var cross=BigInteger.Abs((Units(p[2].X)-ax)*(Units(p[1].Y)-ay)-(Units(p[2].Y)-ay)*(Units(p[1].X)-ax));
            // Each integer unit is 2^-1074 mm. Keep the leading 53 bits
            // before conversion and combine exponents only at the final scaling.
            int shift=(int)Math.Max(0,cross.GetBitLength()-53);
            int widthExponent=Math.ILogB(width);
            double significand=Math.ScaleB(width,-widthExponent);
            double depth=Math.ScaleB((double)(cross >> shift)/significand,shift-2148-widthExponent);
            // The exact predicate proves nonzero dimensions; if rounded display
            // arithmetic loses either, fail rather than display zero or NaN.
            if(width<2.2250738585072014e-308 || depth<2.2250738585072014e-308 || !double.IsFinite(width) || !double.IsFinite(depth))
                throw new ArgumentException("Seçili ölçü gösterim hassasiyetinde güvenilir biçimde temsil edilemiyor.");
            return (width,depth);
        }
        var am=Measure(a);var bm=Measure(b);
        return new(ok,am.Width,bm.Width,am.Depth,bm.Depth);
    }
}
