using System.Numerics;
using TekstilCAD.Core;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Sufficient exact certificate that the straight-line motion from the binary64
/// source vertices to the indexed grid vertices stays a simple polygon. This
/// proves topology of that motion only, not preservation of notch dimensions,
/// offset correctness, or fitness for production.
/// </summary>
internal static class OffsetSourceGridIsotopyExperiment
{
    private readonly record struct P(BigInteger X, BigInteger Y);

    internal static bool TryVerify(IReadOnlyList<Point2> source, IReadOnlyList<GridPoint> grid,
        CancellationToken token = default)
    {
        if (source.Count < 3 || source.Count > 1024 || source.Count != grid.Count) return false;
        int shift = 1074;
        foreach (var p in source)
        {
            token.ThrowIfCancellationRequested();
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y)) return false;
            shift = Math.Min(shift, Math.Min(Exponent(p.X), Exponent(p.Y)) + 6);
        }
        shift = Math.Min(shift, 1074);
        var a = new P[source.Count]; var b = new P[source.Count];
        for (int i = 0; i < a.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            a[i] = new((Exact(source[i].X) * 1_000_000) >> shift, (Exact(source[i].Y) * 1_000_000) >> shift);
            b[i] = new((BigInteger)grid[i].X << (1074 - shift), (BigInteger)grid[i].Y << (1074 - shift));
        }
        for (int i = 0; i < a.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            int next = (i + 1) % a.Length;
            int previous = (i + a.Length - 1) % a.Length;
            if (Side(a[previous], a[i], a[next], b[previous], b[i], b[next]) == 0) return false;
            // One coordinate of every moving edge must retain a strict sign.
            if (!SameStrict(a[next].X-a[i].X, b[next].X-b[i].X) &&
                !SameStrict(a[next].Y-a[i].Y, b[next].Y-b[i].Y)) return false;
            for (int j = i + 1; j < a.Length; j++)
            {
                if (j == next || (i == 0 && j == a.Length - 1)) continue;
                int k = (j + 1) % a.Length;
                // If both endpoints of either segment remain on one strict side
                // of the other segment's moving supporting line, they cannot meet.
                int c = Side(a[i], a[next], a[j], b[i], b[next], b[j]);
                int d = Side(a[i], a[next], a[k], b[i], b[next], b[k]);
                if (c != 0 && c == d) continue;
                int e = Side(a[j], a[k], a[i], b[j], b[k], b[i]);
                int f = Side(a[j], a[k], a[next], b[j], b[k], b[next]);
                if (e == 0 || e != f) return false;
            }
        }
        return true;
    }

    // The cross product under linear vertex motion is quadratic. Its three
    // Bernstein coefficients, doubled, are 2*c0, c1, 2*c2 below. Equal strict
    // signs bound the entire polynomial away from zero for 0 <= t <= 1.
    private static int Side(P a, P b, P c, P d, P e, P f)
    {
        var u = Sub(b,a); var v = Sub(c,a); var x = Sub(e,d); var y = Sub(f,d);
        var first = Cross(u,v); var last = Cross(x,y);
        var middle = Cross(u,y) + Cross(x,v);
        return first.Sign != 0 && first.Sign == middle.Sign && first.Sign == last.Sign ? first.Sign : 0;
    }
    private static bool SameStrict(BigInteger a, BigInteger b) => a.Sign != 0 && a.Sign == b.Sign;
    private static P Sub(P a, P b) => new(a.X-b.X, a.Y-b.Y);
    private static BigInteger Cross(P a, P b) => a.X*b.Y-a.Y*b.X;
    private static int Exponent(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        int e = (int)((bits >> 52) & 0x7ff);
        return e == 0 ? 0 : e - 1;
    }
    private static BigInteger Exact(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        int e = (int)((bits >> 52) & 0x7ff);
        long fraction = bits & 0x000f_ffff_ffff_ffffL;
        BigInteger m = e == 0 ? fraction : fraction | (1L << 52);
        if (bits < 0) m = -m;
        return m << (e == 0 ? 0 : e - 1);
    }
}

