using System.Numerics;
using TekstilCAD.Core;

namespace TekstilCAD.Tests;

// Test-only directed interval arithmetic on exact binary64 inputs. Bound is
// for corresponding indexed finite segments to the ideal miter of the source
// binary64 polygon and caller-supplied INTEGER GRID delta. No feature,
// topology, machine, original-measurement, or production approval is implied.
internal static class ExperimentalSourceMiterIntervalBound
{
    private static readonly BigInteger S=BigInteger.One<<80;
    private static readonly BigInteger BinaryDenominator=BigInteger.One<<1074;
    private readonly record struct I(BigInteger Lo,BigInteger Hi);
    private static I Add(I a,I b)=>new(a.Lo+b.Lo,a.Hi+b.Hi);
    private static I Sub(I a,I b)=>new(a.Lo-b.Hi,a.Hi-b.Lo);
    private static I Neg(I a)=>new(-a.Hi,-a.Lo);
    private static BigInteger Floor(BigInteger n,BigInteger d){var q=BigInteger.DivRem(n,d,out var r);return r.Sign<0?q-1:q;}
    private static BigInteger Ceil(BigInteger n,BigInteger d)=>-Floor(-n,d);
    private static I Mul(I a,I b)
    {
        BigInteger[] p=[a.Lo*b.Lo,a.Lo*b.Hi,a.Hi*b.Lo,a.Hi*b.Hi];
        return new(Floor(p.Min(),S),Ceil(p.Max(),S));
    }
    private static bool Divide(I a,I b,out I result)
    {
        result=default;if(b.Lo<=0)return false;
        BigInteger[] p=[Floor(a.Lo*S,b.Lo),Floor(a.Lo*S,b.Hi),Floor(a.Hi*S,b.Lo),Floor(a.Hi*S,b.Hi)];
        BigInteger[] q=[Ceil(a.Lo*S,b.Lo),Ceil(a.Lo*S,b.Hi),Ceil(a.Hi*S,b.Lo),Ceil(a.Hi*S,b.Hi)];
        result=new(p.Min(),q.Max());return true;
    }
    private static I Square(I a)
    {
        BigInteger lo=a.Lo<=0 && a.Hi>=0?0:BigInteger.Min(a.Lo*a.Lo,a.Hi*a.Hi);
        BigInteger hi=BigInteger.Max(a.Lo*a.Lo,a.Hi*a.Hi);
        return new(Floor(lo,S),Ceil(hi,S));
    }
    private static BigInteger SqrtFloor(BigInteger n)
    {
        if(n<=0)return BigInteger.Zero;
        var x=BigInteger.One<<(int)((n.GetBitLength()+1)/2);
        while(true){var next=(x+n/x)>>1;if(next>=x)return x;x=next;}
    }
    private static bool Length(I x,I y,out I result)
    {
        var sum=Add(Square(x),Square(y));result=default;if(sum.Lo<=0)return false;
        var lo=SqrtFloor(sum.Lo*S);var hival=sum.Hi*S;var hi=SqrtFloor(hival);
        if(hi*hi<hival)hi++;
        result=new(lo,hi);return lo>0;
    }
    private static BigInteger Units(double x)
    {
        long bits=BitConverter.DoubleToInt64Bits(x);int e=(int)((bits>>52)&0x7ff);
        long f=bits&0x000f_ffff_ffff_ffffL;BigInteger m=e==0?f:f|(1L<<52);if(bits<0)m=-m;
        return m<<(e==0?0:e-1);
    }
    private static I Coordinate(double x)
    {
        var n=Units(x)*1_000_000*S;
        return new(Floor(n,BinaryDenominator),Ceil(n,BinaryDenominator));
    }
    private static bool Valid(Point2 p)=>p.IsFinite && Math.Abs(p.X)<=Geometry.MaxCoordinate && Math.Abs(p.Y)<=Geometry.MaxCoordinate;
    internal static bool TryVerify(IReadOnlyList<Point2> source,IReadOnlyList<Point2> candidate,long deltaGrid,double toleranceMm,
        out double upperBoundMm,out string reason,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(candidate);token.ThrowIfCancellationRequested();
        upperBoundMm=double.NaN;reason="Invalid size, coordinate, delta, or explicit tolerance.";
        int n=source.Count;if(n<3 || n>707 || candidate.Count!=n || deltaGrid<=0 || !double.IsFinite(toleranceMm) || toleranceMm<=0)return false;
        var s=new Point2[n];var c=new Point2[n];var xs=new I[n];var ys=new I[n];BigInteger area=0;
        for(int i=0;i<n;i++)
        {
            token.ThrowIfCancellationRequested();s[i]=source[i];c[i]=candidate[i];
            if(!Valid(s[i]) || !Valid(c[i]))return false;xs[i]=Coordinate(s[i].X);ys[i]=Coordinate(s[i].Y);
        }
        for(int i=0;i<n;i++){token.ThrowIfCancellationRequested();int j=(i+1)%n;area+=Units(s[i].X)*Units(s[j].Y)-Units(s[i].Y)*Units(s[j].X);}
        if(area.IsZero){reason="Source orientation is zero or ambiguous.";return false;}
        var tol=Coordinate(toleranceMm).Lo;if(tol<=0){reason="Tolerance is below interval resolution.";return false;}
        BigInteger maxSquared=0;int orientation=area.Sign;
        for(int i=0;i<n;i++)
        {
            token.ThrowIfCancellationRequested();int prev=(i+n-1)%n,next=(i+1)%n;
            var ux=Sub(xs[i],xs[prev]);var uy=Sub(ys[i],ys[prev]);
            var vx=Sub(xs[next],xs[i]);var vy=Sub(ys[next],ys[i]);
            if(!Length(ux,uy,out var ul) || !Length(vx,vy,out var vl))
            {reason=$"Edge length at {i} cannot be bounded away from zero.";return false;}
            var a=orientation>0?uy:Neg(uy);var b=orientation>0?Neg(ux):ux;
            var d=orientation>0?vy:Neg(vy);var e=orientation>0?Neg(vx):vx;
            if(!Divide(a,ul,out var n1x) || !Divide(b,ul,out var n1y) ||
               !Divide(d,vl,out var n2x) || !Divide(e,vl,out var n2y))
            {reason=$"Normal at {i} is numerically ambiguous.";return false;}
            var denominator=Add(new I(S,S),Add(Mul(n1x,n2x),Mul(n1y,n2y)));
            if(!Divide(new I((BigInteger)deltaGrid*S,(BigInteger)deltaGrid*S),denominator,out var factor))
            {reason=$"Miter denominator at {i} is nonpositive or ambiguous.";return false;}
            var mx=Add(xs[i],Mul(factor,Add(n1x,n2x)));var my=Add(ys[i],Mul(factor,Add(n1y,n2y)));
            var cx=Coordinate(c[i].X);var cy=Coordinate(c[i].Y);
            var dx=BigInteger.Max(BigInteger.Abs(mx.Lo-cx.Hi),BigInteger.Abs(mx.Hi-cx.Lo));
            var dy=BigInteger.Max(BigInteger.Abs(my.Lo-cy.Hi),BigInteger.Abs(my.Hi-cy.Lo));
            var squared=dx*dx+dy*dy;if(squared>maxSquared)maxSquared=squared;
            if(squared>tol*tol){reason=$"Miter deviation at {i} exceeds or cannot prove the supplied bound.";return false;}
        }
        var upper=SqrtFloor(maxSquared);if(upper*upper<maxSquared)upper++;
        // Round each binary64 presentation step upward; the acceptance test
        // itself above uses only integer comparisons.
        double displayed=Math.BitIncrement((double)upper);
        displayed=Math.BitIncrement(displayed/(double)S);
        upperBoundMm=Math.BitIncrement(displayed/1_000_000d);
        reason="Corresponding source-binary64 ideal-miter and candidate segments have the explicit two-way bound, conditional on this interval implementation; feature, topology and production fitness are unverified.";
        return true;
    }
}
