using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public sealed class GeometryTranslationPrecisionTests
{
    [Theory]
    [InlineData(0.000000001)]
    [InlineData(0.0000011)]
    public void HorizontalTranslationRejectsCollapsedOrDistortedThinShapeWithoutMutatingSource(double width)
    {
        var source = new List<Point2> { new(0, 0), new(width, 0), new(width, 10), new(0, 10) };
        var before = source.ToArray();
        var representedWidth = (90_000_000d + width) - 90_000_000d;
        Assert.True(Math.Abs(representedWidth - width) / width > 0.001);
        Assert.ThrowsAny<ArgumentException>(() => Geometry.Translate(source, 90_000_000, 0));
        Assert.Equal(before, source);
    }

    [Theory]
    [InlineData(0.000000001)]
    [InlineData(0.0000011)]
    public void VerticalTranslationRejectsCollapsedOrDistortedThinShapeWithoutMutatingSource(double height)
    {
        var source = new List<Point2> { new(0, 0), new(20, 0), new(20, height), new(0, height) };
        var before = source.ToArray();
        Assert.True(Math.Abs((90_000_000d + height) - 90_000_000d - height) / height > 0.001);
        Assert.ThrowsAny<ArgumentException>(() => Geometry.Translate(source, 0, 90_000_000));
        Assert.Equal(before, source);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(2000)]
    public void OrdinaryCircleRemainsUsable(int count)
    {
        var source = Enumerable.Range(0, count).Select(i =>
        {
            var angle = 2 * Math.PI * i / count;
            return new Point2(-12.3 + 30 * Math.Cos(angle), -23.7 + 30 * Math.Sin(angle));
        }).ToList();
        var before = source.ToArray();
        var result = Geometry.Translate(source, 1000.25, 500.5);
        Assert.Equal(source.Count, result.Count);
        Assert.Equal(before, source);
        Assert.Equal(Geometry.Bounds(source).Width, Geometry.Bounds(result).Width, 8);
        Assert.Equal(Geometry.Bounds(source).Height, Geometry.Bounds(result).Height, 8);
    }

    [Fact]
    public void OpenPathAndEmptyPathCanBeMovedWithoutClosingOrMutatingThem()
    {
        var source = new List<Point2> { new(0.25, 0.5), new(20.25, 10.5), new(40.25, 0.5) };
        var before = source.ToArray();
        var result = Geometry.Translate(source, 10, -3);
        Assert.Equal(new[] { new Point2(10.25, -2.5), new Point2(30.25, 7.5), new Point2(50.25, -2.5) }, result);
        Assert.Equal(before, source);
        Assert.Empty(Geometry.Translate(Array.Empty<Point2>(), 10, 20));
    }

    [Fact]
    public void DeltaCanExceedWorldBoundWhenBothSourceAndDestinationAreValid()
    {
        var source = new[] { new Point2(-90_000_000, 0), new Point2(-89_999_900, 20) };
        var result = Geometry.Translate(source, 180_000_000, 0);
        Assert.Equal(new[] { new Point2(90_000_000, 0), new Point2(90_000_100, 20) }, result);
        Assert.Equal(-90_000_000, source[0].X);
    }

    [Fact]
    public void InvalidDestinationAndNonfiniteDeltaAreRejectedWithoutMutatingSource()
    {
        var source = new List<Point2> { new(99_999_990, 0), new(99_999_999, 10) };
        var before = source.ToArray();
        Assert.ThrowsAny<ArgumentException>(() => Geometry.Translate(source, 20, 0));
        Assert.ThrowsAny<ArgumentException>(() => Geometry.Translate(source, double.NaN, 0));
        Assert.Equal(before, source);
    }
}
