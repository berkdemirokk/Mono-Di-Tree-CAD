using System.Numerics;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental fixed-point enclosure of one ideal, unquantized polygon miter.
/// This is an interval calculation only: it does not certify a contour, Clipper's
/// output, topology, feature preservation, or authorize production geometry.
/// Inputs and outputs are in integer grid units; interval endpoints use Scale.
/// </summary>
public static class OffsetMiterIntervalDiagnostic
{
    public static readonly BigInteger Scale = BigInteger.One << 80;
    public readonly record struct GridPoint(long X, long Y);
    public readonly record struct Interval(BigInteger Lower, BigInteger Upper)
    {
        public bool ContainsInteger(long value) => Lower <= (BigInteger)value * Scale && (BigInteger)value * Scale <= Upper;
    }
    public readonly record struct MiterBox(Interval X, Interval Y);

    /// <summary>
    /// Encloses b + delta*(n1+n2)/(1+dot(n1,n2)), where normals point
    /// outward for the supplied contour orientation (+1 CCW, -1 CW).
    /// Returns false for zero edges, invalid inputs or an uncertain denominator.
    /// Exact input arithmetic and directed integer rounding are used throughout.
    /// </summary>
    public static bool TryBound(GridPoint a, GridPoint b, GridPoint c, long delta, int orientation, out MiterBox box)
    {
        box = default;
        if (delta <= 0 || orientation is not (1 or -1)) return false;
        BigInteger ux = (BigInteger)b.X - a.X, uy = (BigInteger)b.Y - a.Y;
        BigInteger vx = (BigInteger)c.X - b.X, vy = (BigInteger)c.Y - b.Y;
        var uu = ux * ux + uy * uy; var vv = vx * vx + vy * vy;
        if (uu.IsZero || vv.IsZero) return false;
        var lu = SqrtInterval(uu); var lv = SqrtInterval(vv);
        // For a CCW path the right-hand normal is outward.
        var nx1 = Div(Exact(orientation * uy), lu);
        var ny1 = Div(Exact(-orientation * ux), lu);
        var nx2 = Div(Exact(orientation * vy), lv);
        var ny2 = Div(Exact(-orientation * vx), lv);
        var denominator = Add(Exact(BigInteger.One), Add(Mul(nx1,nx2), Mul(ny1,ny2)));
        if (denominator.Lower <= 0) return false;
        var factor = Div(Exact(delta), denominator);
        var x = Add(Exact(b.X), Mul(factor, Add(nx1,nx2)));
        var y = Add(Exact(b.Y), Mul(factor, Add(ny1,ny2)));
        box = new(x,y);
        return true;
    }

    private static Interval Exact(BigInteger n) => new(n * Scale, n * Scale);
    private static Interval Add(Interval a, Interval b) => new(a.Lower + b.Lower, a.Upper + b.Upper);
    private static Interval Mul(Interval a, Interval b)
    {
        BigInteger[] p = [a.Lower*b.Lower, a.Lower*b.Upper, a.Upper*b.Lower, a.Upper*b.Upper];
        var lo=p.Min(); var hi=p.Max();
        return new(FloorDiv(lo,Scale), CeilDiv(hi,Scale));
    }
    private static Interval Div(Interval a, Interval b)
    {
        if (b.Lower <= 0) throw new ArgumentOutOfRangeException(nameof(b));
        BigInteger[] lo = [FloorDiv(a.Lower*Scale,b.Lower),FloorDiv(a.Lower*Scale,b.Upper),FloorDiv(a.Upper*Scale,b.Lower),FloorDiv(a.Upper*Scale,b.Upper)];
        BigInteger[] hi = [CeilDiv(a.Lower*Scale,b.Lower),CeilDiv(a.Lower*Scale,b.Upper),CeilDiv(a.Upper*Scale,b.Lower),CeilDiv(a.Upper*Scale,b.Upper)];
        return new(lo.Min(),hi.Max());
    }
    private static BigInteger FloorDiv(BigInteger a, BigInteger positive)
    {
        var q=BigInteger.DivRem(a,positive,out var remainder);
        return remainder.Sign < 0 ? q-1 : q;
    }
    private static BigInteger CeilDiv(BigInteger a, BigInteger positive) => -FloorDiv(-a,positive);
    private static Interval SqrtInterval(BigInteger integer)
    {
        var n=integer*Scale*Scale; var root=IntegerSqrt(n);
        return new(root,root*root==n ? root : root+1);
    }
    private static BigInteger IntegerSqrt(BigInteger n)
    {
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
        if (n < 2) return n;
        var bits=n.GetBitLength();
        var x=BigInteger.One << checked((int)((bits+1)/2));
        while (true)
        {
            var y=(x+n/x)>>1;
            if (y>=x) return x;
            x=y;
        }
    }
}
