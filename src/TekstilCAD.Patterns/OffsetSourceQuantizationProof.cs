using System.Numerics;
using TekstilCAD.Core;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental exact check of the displacement introduced by the production
/// millimeter-to-grid conversion. Success bounds each coordinate by half a grid
/// unit relative to the actual binary64 input, not its printed decimal value.
/// It does not establish topology, notch preservation or offset correctness.
/// </summary>
internal static class OffsetSourceQuantizationProof
{
    internal static bool TryValidate(IReadOnlyList<Point2> source, IReadOnlyList<GridPoint> grid,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(grid);
        token.ThrowIfCancellationRequested();
        if (source.Count < 3 || source.Count > 1024 || source.Count != grid.Count) return false;
        for (int i = 0; i < source.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            if (!WithinHalf(source[i].X, grid[i].X) || !WithinHalf(source[i].Y, grid[i].Y)) return false;
        }
        return true;
    }

    private static bool WithinHalf(double value, long grid)
    {
        if (!double.IsFinite(value)) return false;
        long bits = BitConverter.DoubleToInt64Bits(value);
        int exponentBits = (int)((bits >> 52) & 0x7ff);
        long fraction = bits & 0x000f_ffff_ffff_ffffL;
        BigInteger significand = exponentBits == 0 ? fraction : fraction | (1L << 52);
        if (bits < 0) significand = -significand;
        int exponent = exponentBits == 0 ? -1074 : exponentBits - 1023 - 52;
        BigInteger numerator = significand * 1_000_000;
        BigInteger denominator = BigInteger.One;
        if (exponent >= 0) numerator <<= exponent;
        else denominator <<= -exponent;
        // Equality permits either tie direction: this certifies displacement,
        // not which point the conversion algorithm ought to choose.
        return 2 * BigInteger.Abs(numerator - (BigInteger)grid * denominator) <= denominator;
    }
}
