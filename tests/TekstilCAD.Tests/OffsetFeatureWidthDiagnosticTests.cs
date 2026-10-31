using System.Reflection;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetFeatureWidthDiagnosticTests
{
    private static bool Check(GridPoint[] p, int first, int second, long width)
    {
        var type = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetFeatureWidthDiagnostic", true)!;
        var method = type.GetMethod("HasMinimumWidth", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)method.Invoke(null, [p, first, second, width, CancellationToken.None])!;
    }

    // Edges 3 and 5 are the opposing sides of the explicitly supplied notch.
    private static GridPoint[] Notch(long halfWidth) =>
        [new(0, 0), new(100, 0), new(100, 100), new(50 + halfWidth, 100),
         new(50 + halfWidth, 50), new(50 - halfWidth, 50), new(50 - halfWidth, 100), new(0, 100)];

    [Fact]
    public void ExactWidthThresholdDetectsNarrowing()
    {
        Assert.True(Check(Notch(5), 3, 5, 10));
        Assert.False(Check(Notch(2), 3, 5, 5));
        Assert.True(Check(Notch(3), 3, 5, 5));
    }

    [Fact]
    public void CrossingsCollinearOverlapAndDegenerateEdgesFailClosed()
    {
        Assert.False(Check([new(0, 0), new(10, 10), new(0, 10), new(10, 0)], 0, 2, 1));
        Assert.False(Check([new(0, 0), new(10, 0), new(5, 0), new(15, 0)], 0, 2, 1));
        Assert.False(Check([new(0, 0), new(0, 0), new(5, 1), new(5, 10)], 0, 2, 1));
    }

    [Fact]
    public void HandlesLargeCoordinatesAndEndpointDistancesWithoutOverflow()
    {
        long x = long.MaxValue - 100;
        GridPoint[] p = [new(x, 0), new(x + 10, 0), new(x + 13, 4), new(x + 13, 10)];
        Assert.True(Check(p, 0, 2, 5));
        Assert.False(Check(p, 0, 2, 6));
        Assert.False(Check(p, 0, 1, 1));
    }
}
