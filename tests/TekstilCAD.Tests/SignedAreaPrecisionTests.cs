using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public sealed class SignedAreaPrecisionTests
{
    [Fact]
    public void NarrowTriangleRetainsAreaAndOrientationAtLargeCoordinates()
    {
        Point2[] points = [new(0, 0), new(100_000_000, 99_999_999), new(99_999_999, 99_999_998)];
        Assert.Equal(-0.5, Geometry.SignedArea(points));
        Assert.Equal(0.5, Geometry.SignedArea(points.Reverse().ToArray()));
    }

    [Fact]
    public void ExactCancellationStillProducesZero()
    {
        Point2[] points = [new(0, 0), new(100_000_000, 100_000_000), new(50_000_000, 50_000_000)];
        Assert.Equal(0, Geometry.SignedArea(points));
    }

    [Fact]
    public void TranslatedFractionalTriangleRetainsItsSignedArea()
    {
        Point2[] points = [new(-50_000_000, -50_000_000), new(50_000_000, 49_999_999.5), new(49_999_999, 49_999_998.5)];
        Assert.Equal(-0.25, Geometry.SignedArea(points));
        Assert.Equal(0.25, Geometry.SignedArea(points.Reverse().ToArray()));
    }

    [Fact]
    public void LongNearDegenerateContourPreservesArea()
    {
        var points = Enumerable.Repeat(new Point2(0, 0), 20_000).ToList();
        points.Add(new(100_000_000, 99_999_999));
        points.Add(new(99_999_999, 99_999_998));
        Assert.Equal(-0.5, Geometry.SignedArea(points));
    }
}
