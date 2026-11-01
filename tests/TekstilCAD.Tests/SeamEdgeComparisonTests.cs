using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public sealed class SeamEdgeComparisonTests
{
    private static readonly Point2[] Rectangle = [new(0, 0), new(3, 0), new(3, 4), new(0, 4)];
    [Fact]
    public void ClosedPathRequiresDirectionAndMeasuresBothWays()
    {
        Assert.Equal(7, ContourMeasurement.MeasurePath(new(Rectangle, true, 0, 2, ContourDirection.Forward)));
        Assert.Equal(7, ContourMeasurement.MeasurePath(new(Rectangle, true, 0, 2, ContourDirection.Backward)));
        Assert.Equal(4, ContourMeasurement.MeasurePath(new(Rectangle, true, 0, 3, ContourDirection.Backward)));
        Assert.Throws<ArgumentException>(() => ContourMeasurement.MeasurePath(new(Rectangle, true, 0, 1)));
        Assert.Throws<ArgumentException>(() => ContourMeasurement.MeasurePath(new(Rectangle, true, 0, 1, (ContourDirection)100)));
    }
    [Fact]
    public void OpenPathUsesUniqueRouteAndComparisonIsSecondMinusFirst()
    {
        var first = new ContourPath(Rectangle, false, 3, 1);
        var second = new ContourPath([new(0, 0), new(1, 0)], false, 0, 1);
        var result = ContourMeasurement.ComparePaths(first, second);
        Assert.Equal(7, result.FirstLength);
        Assert.Equal(1, result.SecondLength);
        Assert.Equal(-6, result.SignedDifference);
        Assert.Equal(6, result.AbsoluteDifference);
        Assert.Throws<ArgumentException>(() => ContourMeasurement.MeasurePath(first with { Direction = ContourDirection.Forward }));
    }
    [Fact]
    public void RepeatedClosingVertexAliasesFirstButOtherDuplicatesRemainSegments()
    {
        Point2[] vertices = [new(0, 0), new(3, 0), new(3, 0), new(3, 4), new(0, 0)];
        Assert.Equal(0, ContourMeasurement.MeasurePath(new(vertices, true, 0, 4, ContourDirection.Forward)));
        Assert.Equal(5, ContourMeasurement.MeasurePath(new(vertices, true, 4, 3, ContourDirection.Backward)));
        Assert.Equal(7, ContourMeasurement.MeasurePath(new(vertices, true, 0, 3, ContourDirection.Forward)));
    }
    [Fact]
    public void ValidatesAllCoordinatesIndicesAndBoundWithoutChangingSource()
    {
        Point2[] points = [new(-100_000_000, 0), new(100_000_000, 0), new(0, 0)];
        var original = points.ToArray();
        Assert.Equal(200_000_000, ContourMeasurement.MeasurePath(new(points, false, 0, 1)));
        Assert.Equal(original, points);
        Assert.Throws<ArgumentOutOfRangeException>(() => ContourMeasurement.MeasurePath(new(points, false, -1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContourMeasurement.MeasurePath(new([new(0, 0), new(double.NaN, 0)], false, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContourMeasurement.MeasurePath(new(new Point2[10_001], false, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContourMeasurement.MeasurePath(new([], false, 0, 0)));
        Assert.Equal(0, ContourMeasurement.MeasurePath(new(new Point2[10_000], false, 0, 9_999)));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void EaseVerdictUsesExactRepresentedValuesAtToleranceBoundary(bool negative)
    {
        double signed = negative ? -1 : 1;
        double target = Math.ScaleB(negative ? 1 : -1, -54);
        var comparison = new SeamEdgeComparison(negative ? 1 : 0, negative ? 0 : 1, signed, 1);
        var assessment = ContourMeasurement.AssessEase(comparison, target, 1);
        // The displayed subtraction rounds to ±1, but the exact difference
        // between the represented values is 1 + 2^-54, outside the tolerance.
        Assert.Equal(signed, assessment.Deviation);
        Assert.False(assessment.WithinTolerance);
        Assert.True(ContourMeasurement.AssessEase(comparison, target, Math.BitIncrement(1)).WithinTolerance);
        Assert.True(ContourMeasurement.AssessEase(comparison, 0, 1).WithinTolerance);
    }

    [Fact]
    public void EaseVerdictDoesNotLoseShortMeasuredLengthInDisplayedDifference()
    {
        var first = new ContourPath([new(0,0),new(0.000000011,0)], false, 0, 1);
        var second = new ContourPath([new(-100_000_000,0),new(100_000_000,0)], false, 0, 1);
        var comparison = ContourMeasurement.ComparePaths(first, second);
        Assert.Equal(200_000_000, comparison.SignedDifference);
        var assessment = ContourMeasurement.AssessEase(comparison, 200_000_000, 0);
        Assert.Equal(0, assessment.Deviation);
        Assert.False(assessment.WithinTolerance);
        Assert.True(ContourMeasurement.AssessEase(comparison, 200_000_000, 0.000000011).WithinTolerance);
        var inconsistent = comparison with { SignedDifference = 0 };
        Assert.Throws<ArgumentException>(() => ContourMeasurement.AssessEase(inconsistent, 0, 0));
    }
}
