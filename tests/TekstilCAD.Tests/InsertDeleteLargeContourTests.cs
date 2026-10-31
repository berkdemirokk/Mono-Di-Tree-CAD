using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class InsertDeleteLargeContourTests
{
    private static PatternPiece OpenLine(int count) => new()
    {
        Name = "Açık yol", Closed = false,
        Points = Enumerable.Range(0, count).Select(i => new Point2(i * 2.0, 0)).ToList()
    };

    private static PatternPiece ClosedWithRepeatedEndpoint() => new()
    {
        Name = "Kapalı kontur", Closed = true,
        Points = [new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0)]
    };

    [Fact]
    public void InsertCanCross4096LimitAndDeleteCanReturnAcrossIt()
    {
        var source = OpenLine(4096);
        var original = source.Points.ToArray();
        var inserted = PatternTools.InsertPoint(source, 0, new Point2(1, 0), CancellationToken.None);
        Assert.Equal(4097, inserted.Points.Count);
        Assert.Equal(original, source.Points);
        var restored = PatternTools.DeletePoint(inserted, 1, CancellationToken.None);
        Assert.Equal(original, restored.Points);
        Assert.Equal(4097, inserted.Points.Count);
    }

    [Fact]
    public void InsertAtStoredPointLimitFailsWithoutChangingSource()
    {
        var source = OpenLine(10_000);
        var original = source.Points.ToArray();
        Assert.Throws<ArgumentException>(() => PatternTools.InsertPoint(source, 0, new Point2(1, 0), CancellationToken.None));
        Assert.Equal(original, source.Points);
    }

    [Fact]
    public void ClosedContourCanCrossAndReturnAcrossValidationThreshold()
    {
        var source = new PatternPiece { Closed = true, Points = Enumerable.Range(0, 4096)
            .Select(i => new Point2(1000 * Math.Cos(Math.Tau * i / 4096), 1000 * Math.Sin(Math.Tau * i / 4096))).ToList() };
        var before = source.Points.ToArray();
        var midpoint = new Point2((before[0].X + before[1].X) / 2, (before[0].Y + before[1].Y) / 2);
        var inserted = PatternTools.InsertPoint(source, 0, midpoint, CancellationToken.None);
        Assert.Equal(4097, inserted.Points.Count);
        var restored = PatternTools.DeletePoint(inserted, 1, CancellationToken.None);
        Assert.Equal(before, restored.Points);
        Assert.Equal(before, source.Points);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void RepeatedClosingVertexCanBeDeletedFromEitherIndex(int index)
    {
        var source = ClosedWithRepeatedEndpoint();
        var original = source.Points.ToArray();
        var result = PatternTools.DeletePoint(source, index, CancellationToken.None);
        Assert.Equal(new[] { new Point2(10, 0), new Point2(10, 10), new Point2(0, 10), new Point2(10, 0) }, result.Points);
        Assert.Equal(original, source.Points);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void RepeatedClosingVertexInsertsAfterTheSameActualVertex(int index)
    {
        var source = ClosedWithRepeatedEndpoint();
        var original = source.Points.ToArray();
        var result = PatternTools.InsertPoint(source, index, new Point2(5, 0), CancellationToken.None);
        Assert.Equal(new[] { new Point2(0, 0), new Point2(5, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 10), new Point2(0, 0) }, result.Points);
        Assert.Equal(original, source.Points);
    }

    [Fact]
    public void DeletingRepeatedClosureCannotReduceTriangleBelowThreeVertices()
    {
        var source = new PatternPiece { Points = [new(0, 0), new(10, 0), new(0, 10), new(0, 0)] };
        var original = source.Points.ToArray();
        Assert.Throws<ArgumentException>(() => PatternTools.DeletePoint(source, 0, CancellationToken.None));
        Assert.Equal(original, source.Points);
    }

    [Fact]
    public void InvalidEdgesAndCrossingAreRejectedWithoutMutatingEitherSource()
    {
        var open = new PatternPiece { Closed = false, Points = [new(0, 0), new(10, 0), new(10, 10)] };
        var closed = ClosedWithRepeatedEndpoint();
        var openBefore = open.Points.ToArray(); var closedBefore = closed.Points.ToArray();
        Assert.Throws<ArgumentException>(() => PatternTools.InsertPoint(open, 0, new Point2(0, 0), CancellationToken.None));
        Assert.Throws<ArgumentException>(() => PatternTools.InsertPoint(closed, 0, new Point2(-5, 15), CancellationToken.None));
        Assert.Equal(openBefore, open.Points); Assert.Equal(closedBefore, closed.Points);
    }

    [Fact]
    public void CancelledOperationsLeaveSourceUnchanged()
    {
        var source = OpenLine(4097); var before = source.Points.ToArray();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => PatternTools.InsertPoint(source, 0, new Point2(1, 0), cancellation.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => PatternTools.DeletePoint(source, 0, cancellation.Token));
        Assert.Equal(before, source.Points);
    }
}
