using System.Numerics;
using TekstilCAD.Core;

namespace TekstilCAD.Patterns;

/// <summary>Exact comparison of represented coordinates and a represented
/// nonnegative tolerance. Inputs are validated by the calling operation.</summary>
internal static class EndpointTolerance
{
    internal static bool Contains(Point2 a, Point2 b, double tolerance)
    {
        static BigInteger Units(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            int exponent = (int)((bits >> 52) & 0x7ff);
            long fraction = bits & 0x000f_ffff_ffff_ffffL;
            var mantissa = new BigInteger(exponent == 0 ? fraction : fraction | (1L << 52));
            var result = mantissa << (exponent == 0 ? 0 : exponent - 1);
            return bits < 0 ? -result : result;
        }
        // All represented finite binary64 numbers are integer multiples of
        // 2^-1074. Squared-distance comparison needs no rounded square root.
        var dx = Units(a.X) - Units(b.X); var dy = Units(a.Y) - Units(b.Y);
        var limit = Units(tolerance);
        return dx * dx + dy * dy <= limit * limit;
    }
}
