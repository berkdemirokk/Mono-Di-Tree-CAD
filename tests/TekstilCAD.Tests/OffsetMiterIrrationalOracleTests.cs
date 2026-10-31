using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>
/// Independent line-intersection oracle for irrational edge lengths. Square roots
/// are bracketed by integer binary search at 160 fractional bits; all subsequent
/// calculations are exact integer arithmetic. This checks enclosure only.
/// </summary>
public sealed class OffsetMiterIrrationalOracleTests
{
    private static readonly BigInteger Precision = BigInteger.One << 160;

    [Fact]
    public void IrrationalCornersEncloseIndependentlyBracketedIntersections()
    {
        (long X, long Y)[] vectors = [
            (1, 1), (2, 1), (-1, 2), (7, -3), (-11, -2),
            (101, 100), (100, 99), (1_000_000, 1), (-999_999, 2)
        ];
        GridPoint[] origins = [
            new(0, 0), new(1, -1), new(-1_000_000_000_000, 1_000_000_000_000),
            new(long.MaxValue - 2_000_000, long.MinValue + 2_000_000)
        ];
        long[] deltas = [1, 17, 1_000_000];
        var tested = 0;
        foreach (var u in vectors)
        foreach (var v in vectors)
        foreach (var orientation in new[] { -1, 1 })
        foreach (var origin in origins)
        foreach (var delta in deltas)
        {
            BigInteger det = (BigInteger)u.X * v.Y - (BigInteger)u.Y * v.X;
            if (det.IsZero) continue;
            var a = new GridPoint(checked(origin.X - u.X), checked(origin.Y - u.Y));
            var c = new GridPoint(checked(origin.X + v.X), checked(origin.Y + v.Y));
            var lu = RootBounds((BigInteger)u.X * u.X + (BigInteger)u.Y * u.Y);
            var lv = RootBounds((BigInteger)v.X * v.X + (BigInteger)v.Y * v.Y);
            // cross(u, displacement) = -orientation * delta * |u|.
            // Cramer's rule gives each coordinate as a linear combination of
            // the two independently bracketed lengths, divided by det.
            var x = Combine(origin.X, -(BigInteger)orientation * delta * v.X, lu,
                (BigInteger)orientation * delta * u.X, lv, det);
            var y = Combine(origin.Y, -(BigInteger)orientation * delta * v.Y, lu,
                (BigInteger)orientation * delta * u.Y, lv, det);
            Assert.True(TryBound(a, origin, c, delta, orientation, out var box),
                $"Bound rejected {u}, {v}, {orientation}, {origin}, {delta}.");
            Assert.True(box.X.Lower * x.Denominator <= x.Lower * Scale &&
                        x.Upper * Scale <= box.X.Upper * x.Denominator,
                $"X enclosure failed for {u}, {v}, {orientation}, {origin}, {delta}.");
            Assert.True(box.Y.Lower * y.Denominator <= y.Lower * Scale &&
                        y.Upper * Scale <= box.Y.Upper * y.Denominator,
                $"Y enclosure failed for {u}, {v}, {orientation}, {origin}, {delta}.");
            tested++;
        }
        Assert.True(tested > 1_000);
    }

    private static (BigInteger Lower, BigInteger Upper) RootBounds(BigInteger n)
    {
        var target = n * Precision * Precision;
        BigInteger lower = 0, upper = (n + 1) * Precision;
        while (upper - lower > 1)
        {
            var mid = (lower + upper) / 2;
            if (mid * mid <= target) lower = mid;
            else upper = mid;
        }
        // The invariant lower² <= target < upper² proves this enclosure.
        return (lower, lower * lower == target ? lower : upper);
    }

    private static (BigInteger Lower, BigInteger Upper, BigInteger Denominator) Combine(
        long origin, BigInteger a, (BigInteger Lower, BigInteger Upper) x,
        BigInteger b, (BigInteger Lower, BigInteger Upper) y, BigInteger det)
    {
        if (det.Sign < 0) { det = -det; a = -a; b = -b; }
        var baseValue = (BigInteger)origin * det * Precision;
        var ax = a.Sign < 0 ? (a * x.Upper, a * x.Lower) : (a * x.Lower, a * x.Upper);
        var by = b.Sign < 0 ? (b * y.Upper, b * y.Lower) : (b * y.Lower, b * y.Upper);
        return (baseValue + ax.Item1 + by.Item1, baseValue + ax.Item2 + by.Item2, det * Precision);
    }
}
