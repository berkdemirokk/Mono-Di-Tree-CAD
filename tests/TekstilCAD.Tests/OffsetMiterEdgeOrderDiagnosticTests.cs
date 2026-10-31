using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;
using static TekstilCAD.Patterns.OffsetMiterEdgeOrderDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetMiterEdgeOrderDiagnosticTests
{
    private static List<GridPoint> Translate(IEnumerable<GridPoint> points, long x, long y)
        => points.Select(p => new GridPoint(checked(p.X + x), checked(p.Y + y))).ToList();

    [Theory]
    [InlineData(false, 0L)]
    [InlineData(true, 0L)]
    [InlineData(false, 50000000000000L)]
    [InlineData(true, 50000000000000L)]
    public void RectangleIdealEdgesAdvanceInBothWindingsAndAtLargeOrigins(bool reverse, long origin)
    {
        List<GridPoint> points = [new(0,0),new(100,0),new(100,80),new(0,80)];
        if (reverse) points.Reverse();
        points = Translate(points, origin, -origin);
        var before = points.ToArray();
        var result = Analyze(points, 5, reverse ? -1 : 1);
        Assert.Equal(Finding.DirectionsPreserved, result.Finding);
        Assert.Null(result.EdgeIndex);
        Assert.Equal(before, points);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedNarrowRecessHasAnIdealEdgeThatReverses(bool reverse)
    {
        List<GridPoint> points = [new(0,0),new(100,0),new(100,100),new(52,100),
            new(52,20),new(48,20),new(48,100),new(0,100)];
        if (reverse) points.Reverse();
        var result = Analyze(points, 5, reverse ? -1 : 1);
        Assert.Equal(Finding.ReversedOrCollapsed, result.Finding);
        Assert.NotNull(result.EdgeIndex);
    }

    [Fact]
    public void LargerRecessStillHasIdealEdgesThatAdvance()
    {
        List<GridPoint> points = [new(0,0),new(100,0),new(100,100),new(60,100),
            new(60,20),new(40,20),new(40,100),new(0,100)];
        Assert.Equal(Finding.DirectionsPreserved, Analyze(points, 5, 1).Finding);
    }

    [Fact]
    public void NearReversalAndInvalidOrOverBudgetInputsAreUnknown()
    {
        List<GridPoint> nearReversal = [new(long.MinValue+1,0),new(0,0),new(long.MinValue+1,1),new(long.MinValue+1,10)];
        Assert.Equal(Finding.Unknown, Analyze(nearReversal, 1, 1).Finding);
        List<GridPoint> square = [new(0,0),new(10,0),new(10,10),new(0,10)];
        Assert.Equal(Finding.Unknown, Analyze(square, 1, 1, maximumVertices:3).Finding);
        Assert.Equal(Finding.Unknown, Analyze(square, 0, 1).Finding);
        Assert.Equal(Finding.Unknown, Analyze(square, 1, 0).Finding);
        Assert.Equal(Finding.Unknown, Analyze([new(0,0),new(0,0),new(1,1)],1,1).Finding);
    }
}
