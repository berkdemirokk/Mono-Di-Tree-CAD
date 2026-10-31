using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public class GeometryRobustnessTests
{
    [Theory]
    [InlineData(1e308)]
    [InlineData(-1e308)]
    [InlineData(100000001)]
    public void FiniteButOutOfRangeCoordinatesAreRejected(double coordinate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Geometry.Distance(new(coordinate, 0), default));
        Assert.Throws<ArgumentOutOfRangeException>(() => Geometry.SignedArea([default, new(coordinate, 0), new(0, 1)]));
    }

    [Fact]
    public void FullSupportedSpanHasFiniteLengthAndArea()
    {
        var limit = Geometry.MaxCoordinate;
        Assert.Equal(2 * limit, Geometry.Distance(new(-limit, 0), new(limit, 0)));
        Assert.Equal(4 * limit * limit, Geometry.SignedArea([new(-limit, -limit), new(limit, -limit), new(limit, limit), new(-limit, limit)]));
    }

    [Fact]
    public void TransformCannotEscapeSupportedCoordinates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Geometry.Translate([new(Geometry.MaxCoordinate, 0)], 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Geometry.Mirror([new(-Geometry.MaxCoordinate, 0)], true, Geometry.MaxCoordinate));
    }

    [Fact]
    public void BoundsMidpointDoesNotOverflowFiniteEndpoints()
    {
        Assert.Equal(new Point2(1e308, 0), new Bounds2(1e308, -1e308, 1e308, 1e308).Center);
    }

    [Fact]
    public void ValidClosedContourAcceptsExplicitClosure()
    {
        Geometry.ValidateContour([new(0, 0), new(100, 0), new(100, 100), new(0, 100), new(0, 0)]);
    }

    [Fact]
    public void InvalidProductionContoursAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Geometry.ValidateContour([]));
        Assert.Throws<ArgumentException>(() => Geometry.ValidateContour([new(0, 0), new(10, 0), new(20, 0)]));
        Assert.Throws<ArgumentException>(() => Geometry.ValidateContour([new(0, 0), new(10, 10), new(0, 10), new(10, 0)]));
        Assert.Throws<ArgumentException>(() => Geometry.ValidateContour([new(0, 0), new(10, 0), new(10, 10), new(10, 0), new(0, 10)]));
    }

    [Fact]
    public void QuadraticValidationRejectsOversizedContourBeforeTraversal()
    {
        var points = Enumerable.Range(0, Geometry.MaxValidatedContourVertices + 1).Select(i => new Point2(i, i % 2)).ToArray();
        Assert.Throws<ArgumentException>(() => Geometry.ValidateContour(points));
    }
}
