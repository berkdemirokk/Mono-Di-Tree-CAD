using System.Numerics;

namespace TekstilCAD.Tests;

// Research-only oracle: rounds the exact affine expression using the supplied binary64
// coefficients. It does not measure the error in those coefficients as approximations
// to mathematical sine and cosine, nor is it a production geometry implementation.
internal static class ExperimentalExactAffineRotation
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

    public static (double X, double Y) Rotate(double x, double y, double c, double s, double tx = 0, double ty = 0)
    {
        var dx = Dyadic.FromDouble(x); var dy = Dyadic.FromDouble(y);
        var dc = Dyadic.FromDouble(c); var ds = Dyadic.FromDouble(s);
        return ((dc * dx - ds * dy + Dyadic.FromDouble(tx)).Round(),
                (ds * dx + dc * dy + Dyadic.FromDouble(ty)).Round());
    }
}

public sealed class ExperimentalExactAffineRotationTests
{
    [Fact]
    public void SingleProductAndTranslationMatchIndependentFusedMultiplyAdd()
    {
        var random = new Random(71173);
        double Finite()
        {
            while (true)
            {
                var bytes = new byte[8]; random.NextBytes(bytes);
                var value = BitConverter.Int64BitsToDouble(BitConverter.ToInt64(bytes));
                if (double.IsFinite(value)) return value;
            }
        }
        for (var i = 0; i < 2000; i++)
        {
            var x = Finite(); var c = Finite(); var offset = Finite();
            var actual = ExperimentalExactAffineRotation.Rotate(x, 0, c, 0, offset).X;
            var expected = Math.FusedMultiplyAdd(c, x, offset);
            Assert.True(actual == expected || (actual == 0 && expected == 0), $"Case {i}: {x:R} * {c:R} + {offset:R}");
        }
    }

    [Fact]
    public void OverflowBoundaryMatchesFusedMultiplyAdd()
    {
        var midpoint = Math.ScaleB(1, 970);
        foreach (var offset in new[] { Math.BitDecrement(midpoint), midpoint, Math.BitIncrement(midpoint) })
        {
            var expected = Math.FusedMultiplyAdd(double.MaxValue, 1, offset);
            var actual = ExperimentalExactAffineRotation.Rotate(double.MaxValue, 0, 1, 0, offset).X;
            Assert.Equal(expected, actual);
        }
        Assert.Equal(double.PositiveInfinity, Math.FusedMultiplyAdd(double.MaxValue, 1, midpoint));
    }

    [Fact]
    public void OrdinaryRotationUsesRepresentedCoefficients()
    {
        double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6);
        var p = ExperimentalExactAffineRotation.Rotate(2, 0, c, s, 10, -3);
        Assert.Equal(10 + 2 * c, p.X);
        Assert.Equal(-3 + 2 * s, p.Y);
    }

    [Fact]
    public void CancellationRetainsDifferenceLostByIntermediateRounding()
    {
        double a = Math.ScaleB(1, 53);
        var p = ExperimentalExactAffineRotation.Rotate(a, a, 1, 1, 1, 0);
        Assert.Equal(1, p.X);
        Assert.Equal(Math.ScaleB(1, 54), p.Y);
        // A second example avoids intermediate product overflow entirely.
        p = ExperimentalExactAffineRotation.Rotate(double.MaxValue, double.MaxValue, 2, 2, 3, 0);
        Assert.Equal(3, p.X);
        Assert.Equal(double.PositiveInfinity, p.Y);
    }

    [Fact]
    public void RoundToNearestEvenHandlesNormalAndSubnormalTies()
    {
        double halfUlp = Math.ScaleB(1, -53);
        Assert.Equal(1, ExperimentalExactAffineRotation.Rotate(1, halfUlp, 1, -1).X);
        Assert.Equal(Math.BitIncrement(Math.BitIncrement(1)),
            ExperimentalExactAffineRotation.Rotate(Math.BitIncrement(1), halfUlp, 1, -1).X);
        double tiny = double.Epsilon;
        Assert.Equal(0, ExperimentalExactAffineRotation.Rotate(tiny, 0, 0.5, 0).X);
        Assert.Equal(2 * tiny, ExperimentalExactAffineRotation.Rotate(3 * tiny, 0, 0.5, 0).X);
        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(
            ExperimentalExactAffineRotation.Rotate(-tiny, 0, 0.5, 0).X));
        Assert.Equal(Math.ScaleB(1, -1022), ExperimentalExactAffineRotation.Rotate(
            Math.ScaleB(1, -1022), tiny, 1, 0.5).X);
    }

    [Fact]
    public void RejectsNonFiniteInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExperimentalExactAffineRotation.Rotate(0, 0, double.NaN, 0));
    }
}

