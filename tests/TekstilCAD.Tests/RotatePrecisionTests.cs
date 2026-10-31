using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public class RotatePrecisionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(360)]
    [InlineData(-720)]
    public void FullTurnsPreserveSmallCoordinatesAtDistantPivot(double degrees)
    {
        Point2[] source = [new(0.00000001, -0.00000001), new(0.00000002, 0.00000003)];
        var result = Geometry.Rotate(source, degrees, new(100_000_000, -100_000_000));

        Assert.Equal(source, result);
        Assert.NotSame(source, result);
    }

    [Theory]
    [InlineData(90, -1, 100000000)]
    [InlineData(-270, -1, 100000000)]
    [InlineData(180, -100000000, -1)]
    [InlineData(-90, 1, -100000000)]
    public void QuarterTurnsUseExactCoefficients(double degrees, double x, double y)
    {
        var result = Geometry.Rotate([new(100_000_000, 1)], degrees);
        Assert.Equal(new Point2(x, y), result[0]);
    }

    [Fact]
    public void DistantPivotCannotCollapseRotatedHorizontalDetail()
        => Assert.Throws<ArgumentException>(() => Geometry.Rotate(
            [new(0, 0), new(0, 0.000000001)], 90, new(50_000_000, 50_000_000)));

    [Fact]
    public void DistantPivotCannotCollapseRotatedVerticalDetail()
        => Assert.Throws<ArgumentException>(() => Geometry.Rotate(
            [new(0, 0), new(0.000000001, 0)], 90, new(50_000_000, -50_000_000)));

    [Fact]
    public void DistantPivotCannotEraseSinglePointPosition()
        => Assert.Throws<ArgumentException>(() => Geometry.Rotate(
            [new(0.000000001, 0)], 90, new(50_000_000, 50_000_000)));

    [Fact]
    public void DistantPivotCannotShiftAllPointsWhilePreservingTheirVectors()
        => Assert.Throws<ArgumentException>(() => Geometry.Rotate(
            [new(0.000000001, 0), new(0.000000001, 1)], 90, new(50_000_000, 50_000_000)));

    [Fact]
    public void OrdinaryQuarterTurnPreservesDetail()
    {
        Point2[] source = [new(1, 2), new(4, 6)];
        Assert.Equal(new Point2[] { new(8, 1), new(4, 4) }, Geometry.Rotate(source, 90, new(5, 5)));
    }

    [Theory]
    [InlineData(90, 0, double.Epsilon)]
    [InlineData(-90, 0, -double.Epsilon)]
    [InlineData(180, -double.Epsilon, 0)]
    public void OrthogonalTurnPreservesSubnormalCoordinate(double angle, double expectedX, double expectedY)
    {
        var result = Geometry.Rotate([new(double.Epsilon, 0)], angle);
        Assert.Equal(new Point2(expectedX, expectedY), Assert.Single(result));
    }

    [Fact]
    public void FullTurnStillValidatesOrigin()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Geometry.Rotate([new(1, 2)], 0, new(double.NaN, 0)));
}
