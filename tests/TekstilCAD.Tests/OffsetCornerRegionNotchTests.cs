using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class OffsetCornerRegionNotchTests
{
    [Theory]
    [InlineData(false, 0.0)]
    [InlineData(true, 0.0)]
    [InlineData(false, 50000000.0)]
    [InlineData(true, 50000000.0)]
    public void ExtraNotchInsideCornerNeighborhoodCannotBeCertified(bool reverse, double origin)
    {
        List<Point2> source = [new(origin, origin), new(origin + 100, origin),
            new(origin + 100, origin + 100), new(origin, origin + 100)];
        // A 0.005 mm-deep extra recess lies wholly within the 0.02 mm
        // neighborhood of the expected upper-right offset corner.
        List<Point2> output = [new(origin - 1, origin - 1), new(origin + 101, origin - 1),
            new(origin + 101, origin + 101), new(origin + 100.99, origin + 101),
            new(origin + 100.99, origin + 100.995), new(origin + 100.985, origin + 100.995),
            new(origin + 100.985, origin + 101), new(origin - 1, origin + 101)];
        if (reverse) { source.Reverse(); output.Reverse(); }
        Geometry.ValidateContour(source);
        Geometry.ValidateContour(output);
        var result = OffsetOrderedDiagnostic.Compare(source, 1, output, .02);
        Assert.False(result.OrderedMatch, "An unmatched notch in a corner neighborhood cannot be certified.");
    }
}
