using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class EdgePercentageInsertionTests
{
    [Fact]
    public void PercentageSplitsDiagonalAndClosingEdgeWithoutChangingSource()
    {
        var open = new PatternPiece { Closed=false, Points=[new(0,0),new(60,80)] };
        var result = PatternTools.InsertPointAtPercentage(open, 0, 30);
        Assert.Equal(new Point2(18,24), result.Points[1]);
        Assert.Equal(2, open.Points.Count);
        var closed = new PatternPiece { Closed=true, Points=[new(0,0),new(100,0),new(100,60),new(0,60),new(0,0)] };
        Assert.Equal(new Point2(0,45), PatternTools.InsertPointAtPercentage(closed, 3, 25).Points[4]);
        Assert.Equal(new Point2(25,0), PatternTools.InsertPointAtPercentage(closed, 4, 25).Points[1]);
        Assert.Equal(5, closed.Points.Count);
    }

    [Fact]
    public void InvalidPercentageMissingEdgeAndCancellationLeaveSourceUntouched()
    {
        var source = new PatternPiece { Closed=false, Points=[new(0,0),new(100,0)] };
        foreach (var value in new[] { -1d, 0d, 100d, 101d, double.NaN, double.PositiveInfinity })
            Assert.Throws<ArgumentException>(() => PatternTools.InsertPointAtPercentage(source, 0, value));
        Assert.Throws<ArgumentException>(() => PatternTools.InsertPointAtPercentage(source, 1, 25));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => PatternTools.InsertPointAtPercentage(source, 0, 25, cancellation.Token));
        Assert.Equal(new[] { new Point2(0,0),new Point2(100,0) }, source.Points);
    }
}
