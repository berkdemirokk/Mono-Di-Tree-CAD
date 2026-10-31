using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class OrthogonalRotationTests
{
    private static PatternPiece Piece() => new() { Name = "A", Points = [new(1, 2), new(5, 2), new(1, 7)],
        Metadata = new PieceMetadata { Size = "M" }, Grainline = new Grainline { Start = new(2, 3), End = new(2, 6), NeedsReview = true } };

    [Theory]
    [InlineData(0, 1, 2, 2, 3)]
    [InlineData(90, -2, 1, -3, 2)]
    [InlineData(180, -1, -2, -2, -3)]
    [InlineData(270, 2, -1, 3, -2)]
    public void RotatesContourAndGrainlineOnDetachedClone(int degrees, double x, double y, double gx, double gy)
    {
        var source = Piece(); var result = OrthogonalRotation.Rotate(source, degrees);
        Assert.Equal(new Point2(x, y), result.Points[0]);
        Assert.Equal(new Point2(gx, gy), result.Grainline!.Start);
        Assert.True(result.Grainline.NeedsReview);
        Assert.Equal(source.Id, result.Id);
        Assert.NotSame(source.Points, result.Points);
        Assert.NotSame(source.Metadata, result.Metadata);
        Assert.NotSame(source.Grainline, result.Grainline);
        result.Points[0] = default; result.Grainline.Start = default; result.Metadata!.Size = "L";
        Assert.Equal(new Point2(1, 2), source.Points[0]);
        Assert.Equal(new Point2(2, 3), source.Grainline!.Start);
        Assert.Equal("M", source.Metadata!.Size);
    }

    [Fact]
    public void FourQuarterTurnsRecoverExactCoordinates()
    {
        var source = Piece(); var result = source;
        for (int i = 0; i < 4; i++) result = OrthogonalRotation.Rotate(result, 90);
        Assert.Equal(source.Points, result.Points);
        Assert.Equal(source.Grainline!.Start, result.Grainline!.Start);
        Assert.Equal(source.Grainline.End, result.Grainline.End);
    }

    [Fact]
    public void RejectsInvalidInputAndObservesCancellation()
    {
        var source = Piece();
        Assert.Throws<ArgumentOutOfRangeException>(() => OrthogonalRotation.Rotate(source, 45));
        source.Points[0] = new(double.NaN, 0);
        Assert.ThrowsAny<ArgumentException>(() => OrthogonalRotation.Rotate(source, 90));
        source = Piece(); source.Grainline!.End = source.Grainline.Start;
        Assert.Throws<ArgumentException>(() => OrthogonalRotation.Rotate(source, 90));
        source = Piece(); source.Points[0] = new(Geometry.MaxCoordinate + 1, 0);
        Assert.ThrowsAny<ArgumentException>(() => OrthogonalRotation.Rotate(source, 90));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => OrthogonalRotation.Rotate(Piece(), 90, cts.Token));
    }
}
