using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class OffsetShadowAdversarialTests
{
    private static List<Point2> Square(double x = 0, double y = 0) =>
        [new(x,y), new(x+100,y), new(x+100,y+100), new(x,y+100)];

    private static List<Point2> Expected(double x = 0, double y = 0) =>
        [new(x-1,y-1), new(x+101,y-1), new(x+101,y+101), new(x-1,y+101)];

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(50000000.0, -50000000.0)]
    public void MissingMiddleSegmentIsNotCovered(double x, double y)
    {
        // A short inward notch replaces a middle section of the expected top edge.
        List<Point2> broken = [new(x-1,y-1), new(x+101,y-1), new(x+101,y+101),
            new(x+51,y+101), new(x+51,y+100), new(x+49,y+100),
            new(x+49,y+101), new(x-1,y+101)];
        var result = OffsetShadowDiagnostic.Compare(Square(x,y), 1, broken, .001);
        Assert.True(result.Compared);
        Assert.False(result.FullyCovered);
        Assert.True(result.CandidateUncoveredMm > .001);
        Assert.True(result.OutputUncoveredMm > .001);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(50000000.0, -50000000.0)]
    public void ReversedOutlineStillCoversSameGeometry(double x, double y)
    {
        var output = Expected(x,y);
        output.Reverse();
        var result = OffsetShadowDiagnostic.Compare(Square(x,y), 1, output, .001);
        Assert.True(result.Compared);
        Assert.True(result.FullyCovered, $"Uncovered: {result.CandidateUncoveredMm}, {result.OutputUncoveredMm}");
    }

    [Fact]
    public void ExtraOutwardFeatureIsNotCovered()
    {
        List<Point2> altered = [new(-1,-1), new(101,-1), new(101,101),
            new(51,101), new(51,103), new(49,103), new(49,101), new(-1,101)];
        var result = OffsetShadowDiagnostic.Compare(Square(), 1, altered, .001);
        Assert.True(result.Compared);
        Assert.False(result.FullyCovered);
        Assert.True(result.OutputUncoveredMm > .001);
    }

    [Fact]
    public void SubToleranceNotchIsNotReportedAsCovered()
    {
        // Short transverse edges must not disappear inside the total length tolerance;
        // this comparison remains diagnostic and does not authorize production.
        List<Point2> altered = [new(-1,-1), new(101,-1), new(101,101),
            new(50.0005,101), new(50.0005,100.995), new(49.9995,100.995),
            new(49.9995,101), new(-1,101)];
        var result = OffsetShadowDiagnostic.Compare(Square(), 1, altered, .02);
        Assert.True(result.Compared);
        Assert.False(result.FullyCovered);
    }
}
