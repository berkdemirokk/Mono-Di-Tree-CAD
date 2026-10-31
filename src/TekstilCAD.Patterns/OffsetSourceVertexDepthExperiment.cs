using System.Numerics;
using TekstilCAD.Core;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental per-vertex test against the actual binary64 source, using exact
/// integer arithmetic. It does not certify feature width, multi-vertex pockets,
/// topology, offset accuracy, or authorize use in production.
/// </summary>
internal static class OffsetSourceVertexDepthExperiment
{
    // A candidate must retain at least half of each nonzero source vertex height
    // over its neighbors' chord, with the same turn sign. This is deliberately a
    // trial policy, not an established garment tolerance.
    internal static bool TryVerify(IReadOnlyList<Point2> source, IReadOnlyList<GridPoint> candidate,
        CancellationToken token = default)
    {
        if (source.Count < 3 || source.Count > 1024 || source.Count != candidate.Count) return false;
        var original = new (BigInteger X, BigInteger Y)[source.Count];
        var output = new (BigInteger X, BigInteger Y)[source.Count];
        for (int i = 0; i < source.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            if (!double.IsFinite(source[i].X) || !double.IsFinite(source[i].Y)) return false;
            // Exact source millimeters and grid coordinates share units of
            // 1/(1e6 * 2^1074) millimeter.
            original[i] = (Exact(source[i].X) * 1_000_000, Exact(source[i].Y) * 1_000_000);
            output[i] = ((BigInteger)candidate[i].X << 1074, (BigInteger)candidate[i].Y << 1074);
        }
        for (int i = 0; i < source.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var (s, sc) = Measure(original[(i + source.Count - 1) % source.Count], original[i], original[(i + 1) % source.Count]);
            var (c, cc) = Measure(output[(i + source.Count - 1) % source.Count], output[i], output[(i + 1) % source.Count]);
            if (sc.IsZero || cc.IsZero || s.Sign != c.Sign) return false;
            if (!s.IsZero && 4 * c * c * sc < s * s * cc) return false;
        }
        return true;
    }

    private static (BigInteger Cross, BigInteger ChordSquared) Measure(
        (BigInteger X, BigInteger Y) a, (BigInteger X, BigInteger Y) b, (BigInteger X, BigInteger Y) c)
    {
        var ux = b.X - a.X; var uy = b.Y - a.Y;
        var vx = c.X - b.X; var vy = c.Y - b.Y;
        var dx = c.X - a.X; var dy = c.Y - a.Y;
        return (ux * vy - uy * vx, dx * dx + dy * dy);
    }

    private static BigInteger Exact(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        int exponent = (int)((bits >> 52) & 0x7ff);
        long fraction = bits & 0x000f_ffff_ffff_ffffL;
        BigInteger significand = exponent == 0 ? fraction : fraction | (1L << 52);
        if (bits < 0) significand = -significand;
        return significand << (exponent == 0 ? 0 : exponent - 1);
    }
}
