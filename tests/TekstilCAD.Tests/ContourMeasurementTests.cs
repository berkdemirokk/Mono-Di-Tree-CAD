using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public sealed class ContourMeasurementTests
{
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
