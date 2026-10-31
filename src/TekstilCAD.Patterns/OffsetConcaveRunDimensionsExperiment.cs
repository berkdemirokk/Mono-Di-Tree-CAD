using System.Numerics;
using TekstilCAD.Core;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Exact trial check of every contiguous interval within every maximal run of
/// concave source turns. For each interval, the candidate's endpoint distance
/// and maximum chord depth must be at least one half of the source measurements;
/// corresponding interior vertices must remain on the same side of the chord.
/// This covers only single concave runs, not pockets spanning several runs.
/// The one-half threshold is experimental, not a production tolerance.
/// </summary>
internal static class OffsetConcaveRunDimensionsExperiment
{
    private readonly record struct P(BigInteger X, BigInteger Y);
    internal static bool TryVerify(IReadOnlyList<Point2> source, IReadOnlyList<GridPoint> candidate,
        CancellationToken token = default)
    {
        int n = source.Count;
        if (n < 3 || n > 1024 || candidate.Count != n) return false;
        int shift = 1074;
        foreach (var p in source)
        {
            token.ThrowIfCancellationRequested();
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y)) return false;
            shift = Math.Min(shift, Math.Min(Exponent(p.X), Exponent(p.Y)) + 6);
        }
        shift = Math.Min(shift, 1074);
        var a = new P[n]; var b = new P[n];
        for (int i = 0; i < n; i++)
        {
            a[i] = new((Exact(source[i].X) * 1_000_000) >> shift, (Exact(source[i].Y) * 1_000_000) >> shift);
            b[i] = new((BigInteger)candidate[i].X << (1074-shift), (BigInteger)candidate[i].Y << (1074-shift));
        }
        BigInteger area = 0;
        for (int i = 0; i < n; i++) area += a[i].X*a[(i+1)%n].Y - a[i].Y*a[(i+1)%n].X;
        int orientation = area.Sign;
        if (orientation == 0) return false;
        var concave = new bool[n];
        for (int i = 0; i < n; i++) concave[i] = Cross(a[(i+n-1)%n], a[i], a[(i+1)%n]).Sign == -orientation;
        int anchor = Array.FindIndex(concave, value => !value);
        if (anchor < 0) return false;
        int k = 1;
        while (k < n)
        {
            token.ThrowIfCancellationRequested();
            if (!concave[(anchor+k)%n]) { k++; continue; }
            int first = k;
            while (k < n && concave[(anchor+k)%n]) k++;
            int last = k-1;
            // Include the neighbors at both ends of the run. Every contiguous
            // source subchain with at least one run vertex is measured.
            for (int left = first-1; left <= last-1; left++)
                for (int right = left+2; right <= last+1; right++)
                {
                    token.ThrowIfCancellationRequested();
                    int l = (anchor+left)%n, r = (anchor+right)%n;
                    BigInteger sl = Length2(a[l],a[r]), cl = Length2(b[l],b[r]);
                    if (sl.IsZero || 4*cl < sl) return false;
                    BigInteger sd = 0, cd = 0;
                    for (int inner = left+1; inner < right; inner++)
                    {
                        int v = (anchor+inner)%n;
                        BigInteger s = Cross(a[l],a[r],a[v]), c = Cross(b[l],b[r],b[v]);
                        if (s.Sign == 0 || c.Sign != s.Sign) return false;
                        sd = BigInteger.Max(sd,BigInteger.Abs(s)); cd = BigInteger.Max(cd,BigInteger.Abs(c));
                    }
                    if (4*cd*cd*sl < sd*sd*cl) return false;
                }
        }
        return true;
    }
    private static BigInteger Cross(P a, P b, P c) => (b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
    private static BigInteger Length2(P a, P b) => (b.X-a.X)*(b.X-a.X)+(b.Y-a.Y)*(b.Y-a.Y);
    private static int Exponent(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        int e = (int)((bits >> 52) & 0x7ff);
        if ((bits & 0x7fff_ffff_ffff_ffffL) == 0) return 1074;
        return e == 0 ? 0 : e-1;
    }
    private static BigInteger Exact(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        int e = (int)((bits >> 52) & 0x7ff);
        long f = bits & 0x000f_ffff_ffff_ffffL;
        BigInteger m = e == 0 ? f : f | (1L << 52);
        if (bits < 0) m = -m;
        return m << (e == 0 ? 0 : e-1);
    }
}
