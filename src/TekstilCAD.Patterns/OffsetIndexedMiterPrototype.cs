using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental construction of one grid vertex per source corner. The interval
/// calculation encloses the ideal miter on the input grid, and rounding must be
/// unambiguous. Exact integer predicates reject changed turn signs, reversed
/// edges and intersections. These conditions preserve the combinatorial turns,
/// not feature dimensions, original pre-quantization geometry, or the swept
/// offset region; callers must not use this as a production acceptance check.
/// </summary>
internal static class OffsetIndexedMiterPrototype
{
    internal static bool TryConstruct(IReadOnlyList<GridPoint> input, long delta,
        out GridPoint[] result, CancellationToken token = default)
        => TryConstructCore(input, delta, false, out result, token);

    internal static bool TryConstructInward(IReadOnlyList<GridPoint> input, long delta,
        out GridPoint[] result, CancellationToken token = default)
        => TryConstructCore(input, delta, true, out result, token);

    private static bool TryConstructCore(IReadOnlyList<GridPoint> input, long delta, bool inward,
        out GridPoint[] result, CancellationToken token)
    {
        result = [];
        if (input.Count < 3 || input.Count > 1024 || delta <= 0) return false;
        var source = input.ToArray();
        int n = source.Length;
        BigInteger area = 0;
        for (int i = 0; i < n; i++) area += Cross(source[i], source[(i+1)%n]);
        int orientation = area.Sign;
        if (orientation == 0 || !Simple(source, token)) return false;
        var candidate = new GridPoint[n];
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            if (!TryBound(source[(i+n-1)%n], source[i], source[(i+1)%n], delta, inward ? -orientation : orientation, out var box) ||
                !Round(box.X, out var x) || !Round(box.Y, out var y)) return false;
            candidate[i] = new(x, y);
        }
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            int j = (i+1)%n, k = (i+2)%n;
            var sx = (BigInteger)source[j].X-source[i].X; var sy = (BigInteger)source[j].Y-source[i].Y;
            var cx = (BigInteger)candidate[j].X-candidate[i].X; var cy = (BigInteger)candidate[j].Y-candidate[i].Y;
            if (sx*cx+sy*cy <= 0 || Turn(source[i],source[j],source[k]).Sign != Turn(candidate[i],candidate[j],candidate[k]).Sign)
                return false;
        }
        if (!Simple(candidate, token)) return false;
        result = candidate;
        return true;
    }

    private static bool Round(Interval value, out long result)
    {
        result = 0;
        // Strict inequalities reject ties and interval uncertainty at a half grid.
        BigInteger Floor(BigInteger v) { var q = BigInteger.DivRem(v, Scale, out var r); return r.Sign < 0 ? q-1 : q; }
        var rounded = Floor(value.Lower + Scale/2);
        if (value.Lower*2 <= (2*rounded-1)*Scale || value.Upper*2 >= (2*rounded+1)*Scale ||
            rounded < long.MinValue || rounded > long.MaxValue) return false;
        result = (long)rounded; return true;
    }

    private static BigInteger Cross(GridPoint a, GridPoint b) => (BigInteger)a.X*b.Y-(BigInteger)a.Y*b.X;
    private static BigInteger Turn(GridPoint a, GridPoint b, GridPoint c) =>
        ((BigInteger)b.X-a.X)*((BigInteger)c.Y-a.Y)-((BigInteger)b.Y-a.Y)*((BigInteger)c.X-a.X);
    private static bool On(GridPoint a, GridPoint b, GridPoint p) =>
        p.X >= Math.Min(a.X,b.X) && p.X <= Math.Max(a.X,b.X) && p.Y >= Math.Min(a.Y,b.Y) && p.Y <= Math.Max(a.Y,b.Y);
    private static bool Intersects(GridPoint a, GridPoint b, GridPoint c, GridPoint d)
    {
        int p = Turn(a,b,c).Sign, q = Turn(a,b,d).Sign, r = Turn(c,d,a).Sign, s = Turn(c,d,b).Sign;
        return (p == 0 && On(a,b,c)) || (q == 0 && On(a,b,d)) ||
            (r == 0 && On(c,d,a)) || (s == 0 && On(c,d,b)) || (p*q < 0 && r*s < 0);
    }
    private static bool Simple(GridPoint[] p, CancellationToken token)
    {
        for (int i = 0; i < p.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (p[i] == p[(i+1)%p.Length]) return false;
            for (int j = i+1; j < p.Length; j++)
            {
                if (j == i+1 || (i == 0 && j == p.Length-1)) continue;
                if (Intersects(p[i],p[(i+1)%p.Length],p[j],p[(j+1)%p.Length])) return false;
            }
        }
        return true;
    }
}

