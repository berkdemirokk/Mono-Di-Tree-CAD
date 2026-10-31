using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class EdgeDistanceInsertionTests
{
    [Fact]
    public void DistanceSplitsEdgeWithoutChangingSourceOrPerimeter()
    {
        var source = new PatternPiece { Closed=true, Points=[new(0,0),new(100,0),new(100,60),new(0,60)] };
        var result = PatternTools.InsertPointAtDistance(source, 0, 30);
        Assert.Equal(new Point2(30,0), result.Points[1]);
        Assert.Equal(4, source.Points.Count);
        Assert.Equal(ContourMeasurement.ClosedPerimeter(source.Points), ContourMeasurement.ClosedPerimeter(result.Points));
    }

    [Fact]
    public void ClosingEdgeAndRepeatedClosingVertexFollowStoredContourSemantics()
    {
        var source = new PatternPiece { Closed=true, Points=[new(0,0),new(100,0),new(100,60),new(0,60),new(0,0)] };
        var closing = PatternTools.InsertPointAtDistance(source, 3, 20);
        Assert.Equal(new Point2(0,40), closing.Points[4]);
        var repeated = PatternTools.InsertPointAtDistance(source, 4, 30);
        Assert.Equal(new Point2(30,0), repeated.Points[1]);
        Assert.Equal(repeated.Points[0], repeated.Points[^1]);
    }

    [Fact]
    public void DiagonalEdgeAcceptsPracticalDistanceWithBoundedDeviation()
    {
        var source = new PatternPiece { Closed=false, Points=[new(0,0),new(60,80)] };
        var result = PatternTools.InsertPointAtDistance(source, 0, 30);
        Assert.Equal(new Point2(18,24), result.Points[1]);
        var diagonal = new PatternPiece { Closed=false, Points=[new(1,2),new(98,79)] };
        var rounded = PatternTools.InsertPointAtDistance(diagonal, 0, 30).Points[1];
        var dx = 97d; var dy = 77d;
        Assert.InRange(Math.Abs(dx * (rounded.Y-2) - dy * (rounded.X-1)) / Math.Sqrt(dx*dx+dy*dy), 0, 0.000001);
        Assert.InRange(Math.Abs(Geometry.Distance(new(1,2), rounded)-30), 0, 0.000001);
        Assert.Equal(2, diagonal.Points.Count);
    }

    [Fact]
    public void RejectsCollapsedPointAtLargeOriginAndHonorsCancellation()
    {
        var source = new PatternPiece { Closed=false, Points=[new(90000000,90000000),new(90000100,90000000)] };
        Assert.Throws<ArgumentException>(() => PatternTools.InsertPointAtDistance(source, 0, 0.000000001));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => PatternTools.InsertPointAtDistance(source, 0, 30, cancellation.Token));
        Assert.Equal(2, source.Points.Count);
    }

    [Fact]
    public void InvalidDistanceAndMissingEdgeLeaveSourceUnchanged()
    {
        var source = new PatternPiece { Closed=false, Points=[new(0,0),new(100,0)] };
        foreach (var distance in new[] { 0d, -1d, 100d, 101d, double.NaN })
            Assert.Throws<ArgumentException>(() => PatternTools.InsertPointAtDistance(source, 0, distance));
        Assert.Throws<ArgumentException>(() => PatternTools.InsertPointAtDistance(source, 1, 10));
        Assert.Equal(new[] { new Point2(0,0), new Point2(100,0) }, source.Points);
    }
}
