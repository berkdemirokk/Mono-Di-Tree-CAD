using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class OpenPathJoinTests
{
    private static PatternPiece Path(params Point2[] points) => new() { Name = "Kaynak", Closed = false, Points = [.. points] };

    [Fact]
    public void ReversesEndpointsWithoutMutatingSourcesOrLosingBridge()
    {
        var a = Path(new(1, 0), new(0, 0));
        var b = Path(new(2, 0), new(1.1, 0));
        var originalA = a.Points.ToArray(); var originalB = b.Points.ToArray();
        var result = OpenPathJoin.Join(a, b, 0.11);
        Assert.Equal(new[] { new Point2(0, 0), new(1, 0), new(1.1, 0), new(2, 0) }, result.Piece.Points);
        Assert.Equal(originalA, a.Points); Assert.Equal(originalB, b.Points);
        Assert.Equal(a.Id, result.FirstSourceId); Assert.Equal(b.Id, result.SecondSourceId);
        Assert.Equal(0, result.FirstEndpointIndex); Assert.Equal(1, result.SecondEndpointIndex);
        Assert.NotEqual(a.Id, result.Piece.Id); Assert.NotEqual(b.Id, result.Piece.Id);
        Assert.Equal(0.1, result.BridgeLength, 10);
    }

    [Fact]
    public void SharedEndpointIsStoredOnce()
    {
        var result = OpenPathJoin.Join(Path(new(0, 0), new(1, 0)), Path(new(1, 0), new(2, 0)), 0);
        Assert.Equal(new[] { new Point2(0, 0), new(1, 0), new(2, 0) }, result.Piece.Points);
        Assert.False(result.Piece.Closed);
        Assert.Equal(1, result.FirstEndpointIndex); Assert.Equal(0, result.SecondEndpointIndex);
    }

    [Theory] [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void ReportsExactSelectedSourceEndpointIndices(bool reverseFirst, bool reverseSecond)
    {
        // The second source's far endpoint is just outside the 1 mm circle,
        // though its rounded double distance is exactly 1 mm.
        var first = Path(new(0,-10),new(0,0));
        var second = Path(new(1,0),new(0,10),new(-1,Math.ScaleB(1,-27)));
        if (reverseFirst) first.Points.Reverse();
        if (reverseSecond) second.Points.Reverse();
        var beforeFirst = first.Points.ToArray(); var beforeSecond = second.Points.ToArray();
        var result = OpenPathJoin.Join(first, second, 1);
        Assert.Equal(reverseFirst ? 0 : 1, result.FirstEndpointIndex);
        Assert.Equal(reverseSecond ? 2 : 0, result.SecondEndpointIndex);
        Assert.Equal(new Point2(0,0), first.Points[result.FirstEndpointIndex]);
        Assert.Equal(new Point2(1,0), second.Points[result.SecondEndpointIndex]);
        Assert.Equal(beforeFirst, first.Points); Assert.Equal(beforeSecond, second.Points);
    }

    [Fact]
    public void RejectsAmbiguousAndUnmatchedEndpoints()
    {
        var a = Path(new(0, 0), new(1, 0)); var b = Path(new(0, 1), new(1, 1));
        Assert.Throws<ArgumentException>(() => OpenPathJoin.Join(a, b, 1));
        Assert.Throws<ArgumentException>(() => OpenPathJoin.Join(a, b, 0.5));
    }

    [Fact]
    public void RejectsIntersectionAndPreservesSources()
    {
        var a = Path(new(0, 0), new(2, 0), new(2, 2));
        var b = Path(new(2, 2), new(1, -1));
        var before = a.Points.ToArray();
        Assert.Throws<ArgumentException>(() => OpenPathJoin.Join(a, b, 0));
        Assert.Equal(before, a.Points);
    }

    [Fact]
    public void RejectsUnresolvedMetadataIdentityAndCancellation()
    {
        var a = Path(new(0, 0), new(1, 0)); var b = Path(new(1, 0), new(2, 0));
        b.Metadata = new PieceMetadata { Size = "M" };
        Assert.Throws<ArgumentException>(() => OpenPathJoin.Join(a, b, 0));
        b.Metadata = null; b.Id = a.Id;
        Assert.Throws<ArgumentException>(() => OpenPathJoin.Join(a, b, 0));
        b.Id = Guid.NewGuid(); b.Grainline = new Grainline();
        Assert.Throws<ArgumentException>(() => OpenPathJoin.Join(a, b, 0));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => OpenPathJoin.Join(a, b, 0, cancelled.Token));
    }

    [Fact]
    public void RejectsExcessiveResultAndInvalidTolerance()
    {
        var a = Path(Enumerable.Range(0, 5001).Select(i => new Point2(i, 0)).ToArray());
        var b = Path(Enumerable.Range(5001, 5000).Select(i => new Point2(i, 0)).ToArray());
        Assert.Throws<ArgumentException>(() => OpenPathJoin.Join(a, b, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => OpenPathJoin.Join(a, b, double.NaN));
    }
}
