using System.Numerics;

namespace TekstilCAD.Tests;

// Research-only: exact arithmetic on the represented inputs and coefficients,
// with a single binary64 rounding per coordinate. Coefficient approximation
// and preservation of a complete contour are separate production concerns.
internal static class ExperimentalExactPivotRotation
{
    private readonly record struct Dyadic(BigInteger N, int E)
    {
        public static Dyadic FromDouble(double value)
        {
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            ulong bits = (ulong)BitConverter.DoubleToInt64Bits(value);
            int exponent = (int)((bits >> 52) & 0x7ff);
            ulong fraction = bits & 0x000f_ffff_ffff_ffffUL;
            BigInteger n = exponent == 0 ? fraction : fraction | (1UL << 52);
            if ((bits >> 63) != 0) n = -n;
            return new(n, exponent == 0 ? -1074 : exponent - 1075);
        }

        public static Dyadic operator +(Dyadic a, Dyadic b)
        {
            int e = Math.Min(a.E, b.E);
            return new((a.N << (a.E - e)) + (b.N << (b.E - e)), e);
        }
        public static Dyadic operator -(Dyadic a, Dyadic b) => a + new Dyadic(-b.N, b.E);
        public static Dyadic operator *(Dyadic a, Dyadic b) => new(a.N * b.N, a.E + b.E);

        public double Round()
        {
            if (N.IsZero) return 0d;
            bool negative = N.Sign < 0;
            BigInteger n = BigInteger.Abs(N);
            int top = checked((int)n.GetBitLength() - 1 + E);
            // Normal results have a 53-bit significand; subnormal spacing is 2^-1074.
            int unit = Math.Max(top - 52, -1074);
            int shift = unit - E;
            BigInteger rounded;
            if (shift > 0)
            {
                rounded = n >> shift;
                BigInteger remainder = n - (rounded << shift);
                BigInteger half = BigInteger.One << (shift - 1);
                if (remainder > half || (remainder == half && !rounded.IsEven)) rounded++;
            }
            else rounded = n << -shift;
            if (rounded.IsZero) return BitConverter.Int64BitsToDouble(negative ? long.MinValue : 0);
            // A carry can raise the exponent. At the overflow midpoint, ties round to infinity.
            if (rounded.GetBitLength() > 53) { rounded >>= 1; unit++; }
            int resultTop = checked((int)rounded.GetBitLength() - 1 + unit);
            ulong sign = negative ? 1UL << 63 : 0;
            if (resultTop > 1023) return BitConverter.Int64BitsToDouble((long)(sign | 0x7ff0_0000_0000_0000UL));
            ulong bits;
            if (resultTop < -1022) bits = (ulong)rounded;
            else
            {
                // A subnormal rounding carry may produce the minimum normal.
                int left = 53 - (int)rounded.GetBitLength();
                rounded <<= left;
                bits = ((ulong)(resultTop + 1023) << 52) | ((ulong)rounded & 0x000f_ffff_ffff_ffffUL);
            }
            return BitConverter.Int64BitsToDouble((long)(sign | bits));
        }
    }

    public static (double X, double Y) Rotate(double x, double y, double c, double s, double originX, double originY)
    {
        var dx = Dyadic.FromDouble(x); var dy = Dyadic.FromDouble(y);
        var dc = Dyadic.FromDouble(c); var ds = Dyadic.FromDouble(s);
        var ox = Dyadic.FromDouble(originX); var oy = Dyadic.FromDouble(originY);
        return ((ox + (dx - ox) * dc - (dy - oy) * ds).Round(),
                (oy + (dx - ox) * ds + (dy - oy) * dc).Round());
    }
}

public sealed class ExperimentalExactPivotRotationTests
{
    [Fact]
    public void PivotCancellationKeepsSmallCoordinate()
    {
        var result = ExperimentalExactPivotRotation.Rotate(1, 0, 1, 0, Math.ScaleB(1, 53), 0);
        Assert.Equal(1, result.X);
        Assert.Equal(0, result.Y);
    }

    [Fact]
    public void CancellationDoesNotOverflowIntermediateProducts()
    {
        var result = ExperimentalExactPivotRotation.Rotate(double.MaxValue, double.MaxValue, 2, 2, 3, 3);
        Assert.Equal(3, result.X);
        Assert.Equal(double.PositiveInfinity, result.Y);
    }

    [Fact]
    public void RoundsSubnormalTiesToEvenAndPreservesNegativeZero()
    {
        var result = ExperimentalExactPivotRotation.Rotate(-double.Epsilon, 0, 0.5, 0, 0, 0);
        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(result.X));
        result = ExperimentalExactPivotRotation.Rotate(3 * double.Epsilon, 0, 0.5, 0, 0, 0);
        Assert.Equal(2 * double.Epsilon, result.X);
    }

    [Fact]
    public void RejectsNonFiniteInputsIncludingPivot()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExperimentalExactPivotRotation.Rotate(0, 0, 1, 0, double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExperimentalExactPivotRotation.Rotate(0, 0, double.PositiveInfinity, 0, 0, 0));
    }
}
