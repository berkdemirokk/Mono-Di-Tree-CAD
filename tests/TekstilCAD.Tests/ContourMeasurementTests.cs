using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public sealed class ContourMeasurementTests
{
    [Fact]
    public void SeamEaseUsesSignedTargetAndInclusiveTolerance()
    {
        var first = new ContourPath([new(0, 0), new(100, 0)], false, 0, 1);
        var second = new ContourPath([new(0, 0), new(103, 0)], false, 0, 1);
        var comparison = ContourMeasurement.ComparePaths(first, second);
        var accepted = ContourMeasurement.AssessEase(comparison, 2, 1);
        Assert.Equal(1, accepted.Deviation);
        Assert.True(accepted.WithinTolerance);
        Assert.False(ContourMeasurement.AssessEase(comparison, -2, 1).WithinTolerance);
        Assert.Throws<ArgumentOutOfRangeException>(() => ContourMeasurement.AssessEase(comparison, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContourMeasurement.AssessEase(comparison, double.NaN, 1));
    }

    [Fact]
    public void ClosedPerimeterIncludesClosingEdgeForEitherRepresentation()
    {
        Point2[] vertices = [new(0, 0), new(3, 0), new(3, 4)];
        Point2[] repeated = [new(0, 0), new(3, 0), new(3, 4), new(0, 0)];
        Assert.Equal(12, ContourMeasurement.ClosedPerimeter(vertices));
        Assert.Equal(12, ContourMeasurement.ClosedPerimeter(repeated));
        Assert.Equal(12, ContourMeasurement.ClosedPerimeter(vertices.Reverse().ToArray()));
    }

    [Fact]
    public void DegenerateInputsAndInvalidCoordinatesAreHandledExplicitly()
    {
        Assert.Equal(0, ContourMeasurement.ClosedPerimeter([]));
        Assert.Equal(0, ContourMeasurement.ClosedPerimeter([new(2, 3)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContourMeasurement.ClosedPerimeter([new(double.NaN, 0)]));
        Assert.Throws<ArgumentNullException>(() => ContourMeasurement.ClosedPerimeter(null!));
    }
}
