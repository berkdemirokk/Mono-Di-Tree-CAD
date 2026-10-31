using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental indexed feature-size comparison. Checks every vertex-pair
/// chord and every non-adjacent edge-pair separation against a caller-supplied
/// retained fraction. This is not an offset, isotopy or production certificate.
/// </summary>
internal static class OffsetIndexedDimensionsDiagnostic
{
    internal static bool TryVerify(IReadOnlyList<GridPoint> source, IReadOnlyList<GridPoint> target,
        int numerator, int denominator, out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        token.ThrowIfCancellationRequested();
        reason = "Invalid size or fraction.";
        int n = source.Count;
        if (n < 3 || n > 1024 || target.Count != n || numerator <= 0 || denominator < numerator || denominator > 1_000_000)
            return false;
        var s = source.ToArray(); var t = target.ToArray();
        BigInteger p2 = (BigInteger)numerator * numerator, q2 = (BigInteger)denominator * denominator;
        for (int i = 0; i < n; i++)
        for (int j = i + 1; j < n; j++)
        {
            token.ThrowIfCancellationRequested();
            var a = DistanceSquared(s[i], s[j]); var b = DistanceSquared(t[i], t[j]);
            if (a.IsZero || b * q2 < a * p2)
            { reason = $"Vertex chord {i},{j} shrinks beyond the permitted fraction or is degenerate."; return false; }
            if (j == i + 1 || (i == 0 && j == n - 1)) continue;
            var x = SegmentDistance(s[i], s[(i + 1) % n], s[j], s[(j + 1) % n]);
            var y = SegmentDistance(t[i], t[(i + 1) % n], t[j], t[(j + 1) % n]);
            if (x.Num.IsZero || y.Num * x.Den * q2 < x.Num * y.Den * p2)
            { reason = $"Edge separation {i},{j} shrinks beyond the permitted fraction or is degenerate."; return false; }
        }
        reason = "All indexed vertex chords and non-adjacent edge separations meet the fraction; offset and feature completeness are not certified.";
        return true;
    }

    private readonly record struct Fraction(BigInteger Num, BigInteger Den);
    private static BigInteger Cross(GridPoint a, GridPoint b, GridPoint c)
        => ((BigInteger)b.X-a.X)*((BigInteger)c.Y-a.Y)-((BigInteger)b.Y-a.Y)*((BigInteger)c.X-a.X);
    private static BigInteger DistanceSquared(GridPoint a, GridPoint b)
    { var x=(BigInteger)a.X-b.X; var y=(BigInteger)a.Y-b.Y; return x*x+y*y; }
    private static bool On(GridPoint a, GridPoint b, GridPoint p)
        => p.X>=Math.Min(a.X,b.X) && p.X<=Math.Max(a.X,b.X) && p.Y>=Math.Min(a.Y,b.Y) && p.Y<=Math.Max(a.Y,b.Y);
    private static bool Intersects(GridPoint a, GridPoint b, GridPoint c, GridPoint d)
    {
        var x=Cross(a,b,c).Sign; var y=Cross(a,b,d).Sign; var z=Cross(c,d,a).Sign; var w=Cross(c,d,b).Sign;
        return (x==0 && On(a,b,c)) || (y==0 && On(a,b,d)) || (z==0 && On(c,d,a)) || (w==0 && On(c,d,b)) || (x*y<0 && z*w<0);
    }
    private static Fraction PointSegment(GridPoint p, GridPoint a, GridPoint b)
    {
        var dx=(BigInteger)b.X-a.X; var dy=(BigInteger)b.Y-a.Y;
        var px=(BigInteger)p.X-a.X; var py=(BigInteger)p.Y-a.Y;
        var length=dx*dx+dy*dy; var dot=px*dx+py*dy;
        if (length.IsZero || dot<=0) return new(DistanceSquared(p,a),1);
        if (dot>=length) return new(DistanceSquared(p,b),1);
        var cross=dx*py-dy*px;
        return new(cross*cross,length);
    }
    private static Fraction SegmentDistance(GridPoint a, GridPoint b, GridPoint c, GridPoint d)
    {
        if (Intersects(a,b,c,d)) return new(0,1);
        var best=PointSegment(a,c,d);
        foreach(var f in new[] { PointSegment(b,c,d), PointSegment(c,a,b), PointSegment(d,a,b) })
            if (f.Num*best.Den < best.Num*f.Den) best=f;
        return best;
    }
}
