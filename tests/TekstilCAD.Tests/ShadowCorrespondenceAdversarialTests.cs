using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

// These comparisons exercise only the experimental diagnostic; passing them does not
// certify the production offset algorithm or establish numeric error bounds.
public class ShadowCorrespondenceAdversarialTests
{
    private static List<Point2> Square(double x = 0, double y = 0) =>
        [new(x, y), new(x + 100, y), new(x + 100, y + 100), new(x, y + 100)];

    [Theory]
    [InlineData(false, 0.0)]
    [InlineData(true, 0.0)]
    [InlineData(false, 50000000.0)]
    [InlineData(true, 50000000.0)]
    public void LostNarrowSourceNotchIsNotHiddenByTolerance(bool reverse, double origin)
    {
        // The 0.04 mm wide recess survives a 0.001 mm offset. A plain rectangle
        // omits it; its 0.005 mm depth is smaller than the comparison tolerance.
        List<Point2> source = [new(origin, 0), new(origin + 100, 0), new(origin + 100, 100),
            new(origin + 50.02, 100), new(origin + 50.02, 99.995),
            new(origin + 49.98, 99.995), new(origin + 49.98, 100), new(origin, 100)];
        List<Point2> omitted = [new(origin - .001, -.001), new(origin + 100.001, -.001),
            new(origin + 100.001, 100.001), new(origin - .001, 100.001)];
        if (reverse) { source.Reverse(); omitted.Reverse(); }
        var result = OffsetShadowDiagnostic.Compare(source, .001, omitted, .02);
        Assert.True(result.Compared);
        Assert.False(result.FullyCovered);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollinearOutputSubdivisionDoesNotChangeCoverage(bool reverse)
    {
        // Every point lies exactly on the four edges of the expected offset.
        // Segment boundaries and the cyclic starting point are deliberately different.
        List<Point2> outline = [new(50, -1), new(101, -1), new(101, 50), new(101, 101),
            new(50, 101), new(-1, 101), new(-1, 50), new(-1, -1)];
        if (reverse) outline.Reverse();
        var result = OffsetShadowDiagnostic.Compare(Square(), 1, outline, .001);
        Assert.True(result.Compared);
        Assert.True(result.FullyCovered, $"Uncovered: {result.CandidateUncoveredMm}, {result.OutputUncoveredMm}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NarrowExtraFeatureCannotBeExcusedByLongMatchingEdges(bool reverse)
    {
        List<Point2> outline = [new(-1, -1), new(101, -1), new(101, 101),
            new(50.001, 101), new(50.001, 101.006), new(49.999, 101.006),
            new(49.999, 101), new(-1, 101)];
        if (reverse) outline.Reverse();
        var result = OffsetShadowDiagnostic.Compare(Square(), 1, outline, .02);
        Assert.True(result.Compared);
        Assert.False(result.FullyCovered);
    }
}
