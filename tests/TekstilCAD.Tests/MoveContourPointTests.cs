using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class MoveContourPointTests
{
    private static PatternPiece Square(bool explicitClosing = false)
    {
        var piece = new PatternPiece
        {
            Name = "Ön",
            Closed = true,
            Points = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)],
            Metadata = new PieceMetadata { Role = "Kesim", Size = "M", Material = "Kumaş", Quantity = 2 }
        };
        if (explicitClosing) piece.Points.Add(piece.Points[0]);
        return piece;
    }

    [Fact]
    public void MovingClosedContourPointReturnsIndependentGeometryWithoutChangingSource()
    {
        var source = Square(); var sourceId = source.Id; var original = source.Points.ToArray();
        var moved = PatternTools.MovePoint(source, 1, new Point2(12.5, 0.25));
        Assert.Equal(original, source.Points);
        Assert.Equal(sourceId, source.Id);
        Assert.Equal(2, source.Metadata!.Quantity);
        Assert.Equal(new Point2(12.5, 0.25), moved.Points[1]);
        Assert.Equal(original[0], moved.Points[0]);
        Assert.True(moved.Closed);
        Assert.Equal(source.Name, moved.Name);
        Assert.NotSame(source.Points, moved.Points);
        Assert.NotEqual(source.Id, moved.Id);
        Assert.Null(moved.Metadata);
        moved.Points[0] = new Point2(1, 1);
        Assert.Equal(original, source.Points);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void MovingEitherCopyOfExplicitClosingPointMovesBoth(int index)
    {
        var source = Square(explicitClosing: true); var original = source.Points.ToArray();
        var moved = PatternTools.MovePoint(source, index, new Point2(-1, -2));
        Assert.Equal(new Point2(-1, -2), moved.Points[0]);
        Assert.Equal(moved.Points[0], moved.Points[^1]);
        Assert.Equal(original, source.Points);
    }

    [Fact]
    public void OpenPathEndpointCanMoveWithoutClosingThePath()
    {
        var source = new PatternPiece { Closed = false, Points = [new(0, 0), new(5, 0), new(5, 5)] };
        var moved = PatternTools.MovePoint(source, 0, new Point2(-2, -1));
        Assert.False(moved.Closed);
        Assert.Equal(new Point2(-2, -1), moved.Points[0]);
        Assert.Equal(new Point2(5, 5), moved.Points[^1]);
        Assert.Equal(new Point2(0, 0), source.Points[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANewCrossingIsRejectedWithoutMutatingSource(bool closed)
    {
        var source = Square(); source.Closed = closed; var before = source.Points.ToArray();
        Assert.ThrowsAny<ArgumentException>(() => PatternTools.MovePoint(source, 1, new Point2(5, 15)));
        Assert.Equal(before, source.Points);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroLengthEdgeIsRejectedWithoutMutatingSource(bool closed)
    {
        var source = Square(); source.Closed = closed; var before = source.Points.ToArray();
        Assert.ThrowsAny<ArgumentException>(() => PatternTools.MovePoint(source, 1, source.Points[0]));
        Assert.Equal(before, source.Points);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void OutOfRangeIndexIsRejected(int index)
    {
        var source = Square(); var before = source.Points.ToArray();
        Assert.ThrowsAny<ArgumentException>(() => PatternTools.MovePoint(source, index, new Point2(1, 1)));
        Assert.Equal(before, source.Points);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(100000001, 0)]
    [InlineData(0, -100000001)]
    public void InvalidPositionIsRejected(double x, double y)
    {
        var source = Square(); var before = source.Points.ToArray();
        Assert.ThrowsAny<ArgumentException>(() => PatternTools.MovePoint(source, 1, new Point2(x, y)));
        Assert.Equal(before, source.Points);
    }

    [Fact]
    public void IdenticalPositionDoesNotMutateSourceAndProducesEquivalentIndependentPoints()
    {
        var source = Square(); var before = source.Points.ToArray();
        var result = PatternTools.MovePoint(source, 1, source.Points[1]);
        Assert.Equal(before, source.Points);
        Assert.Equal(before, result.Points);
        Assert.NotSame(source.Points, result.Points);
    }

    private static PatternPiece CircularContour(int count) => new()
    {
        Points = Enumerable.Range(0, count).Select(i =>
        {
            var angle = Math.Tau * i / count;
            return new Point2(1000 * Math.Cos(angle), 1000 * Math.Sin(angle));
        }).ToList()
    };

    [Theory]
    [InlineData(4096)]
    [InlineData(4097)]
    public void ValidContourOnEitherSideOfLargeValidationThresholdCanMove(int count)
    {
        var source = CircularContour(count); var before = source.Points.ToArray();
        var result = PatternTools.MovePoint(source, 0, new Point2(1000.1, 0));
        Assert.Equal(count, result.Points.Count);
        Assert.Equal(new Point2(1000.1, 0), result.Points[0]);
        Assert.Equal(before, source.Points);
    }

    [Fact]
    public void ExceedingStoredPointLimitIsRejectedWithoutMutatingSource()
    {
        var source = CircularContour(LargeContourValidator.MaximumStoredPoints + 1);
        var before = source.Points.ToArray();
        Assert.ThrowsAny<ArgumentException>(() => PatternTools.MovePoint(source, 0, new Point2(1001, 0)));
        Assert.Equal(before, source.Points);
    }

    [Fact]
    public void CancellationPreservesSource()
    {
        var source = CircularContour(4097); var before = source.Points.ToArray();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => PatternTools.MovePoint(source, 0, new Point2(1001, 0), cancellation.Token));
        Assert.Equal(before, source.Points);
    }
}
