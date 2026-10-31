using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class OffsetOrderedShallowNotchTests
{
    [Theory]
    [InlineData(false, 0.0, 0.1, 0.005)]
    [InlineData(true, 0.0, 0.1, 0.005)]
    [InlineData(false, 50000000.0, 0.1, 0.005)]
    [InlineData(true, 50000000.0, 0.1, 0.005)]
    [InlineData(false, 0.0, 1.0, 0.000005)]
    [InlineData(true, 0.0, 1.0, 0.000005)]
    [InlineData(false, 50000000.0, 1.0, 0.000005)]
    [InlineData(true, 50000000.0, 1.0, 0.000005)]
    public void ExtraShallowFeatureInsideCorridorCannotBeCertified(bool reverse, double origin, double width, double depth)
    {
        List<Point2> source = [new(origin, origin), new(origin + 100, origin),
            new(origin + 100, origin + 100), new(origin, origin + 100)];
        // The two sides have a turn below 30 degrees, but the V is an extra
        // feature, not a subdivision of the straight expected offset edge.
        List<Point2> output = [new(origin - 1, origin - 1), new(origin + 101, origin - 1),
            new(origin + 101, origin + 101), new(origin + 50 + width / 2, origin + 101),
            new(origin + 50, origin + 101 - depth), new(origin + 50 - width / 2, origin + 101),
            new(origin - 1, origin + 101)];
        if (reverse) { source.Reverse(); output.Reverse(); }
        Geometry.ValidateContour(source);
        Geometry.ValidateContour(output);
        var sourceBefore = source.ToArray(); var outputBefore = output.ToArray();
        var result = OffsetOrderedDiagnostic.Compare(source, 1, output, .02);
        Assert.False(result.OrderedMatch, "The added V must not be certified merely because it fits the corridor.");
        Assert.Equal(sourceBefore, source); Assert.Equal(outputBefore, output);
    }
}
