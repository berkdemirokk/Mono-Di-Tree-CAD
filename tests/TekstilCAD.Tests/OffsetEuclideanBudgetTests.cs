using System.Reflection;
using System.Numerics;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetEuclideanBudgetTests
{
    [Theory]
    [InlineData(8, 0, true)]
    [InlineData(0, -8, true)]
    [InlineData(6, 6, false)]
    [InlineData(4, -6, true)]
    public void EntireReverseIntervalMustFitEuclideanBudget(long dx, long dy, bool expected)
    {
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof", true)!
            .GetMethod("InsideErrorBudget", BindingFlags.Static | BindingFlags.NonPublic)!;
        const long originX = 50_000_000_000_000, originY = -50_000_000_000_000;
        BigInteger x = ((BigInteger)originX + dx) * Scale;
        BigInteger y = ((BigInteger)originY + dy) * Scale;
        Assert.Equal(expected, (bool)method.Invoke(null, [new Interval(x, x), new Interval(y, y), originX, originY])!);
    }

    [Fact]
    public void IntervalIsRejectedWhenItsFarthestCornerExceedsBudget()
    {
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof", true)!
            .GetMethod("InsideErrorBudget", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.False((bool)method.Invoke(null, [new Interval(0, 6 * Scale), new Interval(0, 6 * Scale), 0L, 0L])!);
    }
}
