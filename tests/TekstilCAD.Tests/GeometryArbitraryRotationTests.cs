using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public sealed class GeometryArbitraryRotationTests
{
    [Fact]
    public void OrdinaryRectangleRotatesAboutPivot()
    {
        var source = new[] { new Point2(0, 0), new Point2(40, 0), new Point2(40, 20), new Point2(0, 20) };
        var result = Geometry.Rotate(source, 30, new Point2(20, 10));
        for (int i = 0; i < source.Length; i++)
            Assert.Equal(Geometry.Distance(source[i], source[(i + 1) % source.Length]),
                Geometry.Distance(result[i], result[(i + 1) % source.Length]), 10);
        Assert.Equal(new Point2(0, 0), source[0]);
    }

    [Fact]
    public void SmallCoordinateSurvivesDistantPivotCancellation()
    {
        // The X terms cancel exactly, leaving a representable subnormal result.
        var result = Geometry.Rotate(new[] { new Point2(double.Epsilon, 0) }, 180, new Point2(0, 0));
        Assert.Equal(-double.Epsilon, result[0].X);
        Assert.Equal(0, result[0].Y);
    }

    [Fact]
    public void NearZeroComponentDoesNotCauseSingularRelativeErrorRejection()
    {
        var source = new[] { new Point2(1, 0), new Point2(2, 1), new Point2(1, 2) };
        var result = Geometry.Rotate(source, 45, new Point2(1, 1));
        Assert.Equal(source.Length, result.Count);
        Assert.Equal(1 + Math.Sqrt(0.5), result[0].X, 14);
    }

    [Fact]
    public void TenThousandPointsAreSupported()
    {
        var source = Enumerable.Range(0, 10_000).Select(i =>
        {
            var a = 2 * Math.PI * i / 10_000;
            return new Point2(20 + 10 * Math.Cos(a), 30 + 10 * Math.Sin(a));
        }).ToArray();
        var result = Geometry.Rotate(source, 17, new Point2(20, 30));
        Assert.Equal(source.Length, result.Count);
        Assert.Equal(10, Geometry.Distance(result[0], new Point2(20, 30)), 10);
    }

    [Fact]
    public void RejectsLossOfShallowOrthogonalAxisSpan()
    {
        var source = new[] { new Point2(0, 0), new Point2(1e-10, 1) };
        Assert.Throws<ArgumentException>(() => Geometry.Rotate(source, 180, new Point2(50_000_000, 0)));
    }

    [Fact]
    public void RejectsLossOfMeaningfulShallowArbitraryComponent()
    {
        var source = new[] { new Point2(0, 0), new Point2(1, 1 + 1e-10) };
        Assert.Throws<ArgumentException>(() => Geometry.Rotate(source, 45, new Point2(50_000_000, 0)));
    }

    [Fact]
    public void RejectsSeverelyDistortedNonzeroShallowComponent()
    {
        var source = new[] { new Point2(0, 0), new Point2(100, 2e-9) };
        Assert.Throws<ArgumentException>(() => Geometry.Rotate(source, 90.00000000000001, new Point2(10_000_000, 10_000_000)));
    }
    [Fact]
    public void RejectsExactPositionBeyondWorldEvenWhenItRoundsToBoundary()
    {
        var source = new[] { new Point2(-double.Epsilon, 0) };
        Assert.Throws<ArgumentOutOfRangeException>(() => Geometry.Rotate(source, 180, new Point2(50_000_000, 0)));
    }
    [Fact]
    public void RejectsCollapsedShapeAndOutOfRangeResults()
    {
        var tiny = new[] { new Point2(0, 0), new Point2(double.Epsilon, 0) };
        Assert.ThrowsAny<ArgumentException>(() => Geometry.Rotate(tiny, 90, new Point2(90_000_000, 0)));
        Assert.ThrowsAny<ArgumentException>(() => Geometry.Rotate(new[] { new Point2(100_000_000, 0) }, 180, new Point2(-100_000_000, 0)));
    }
}



