using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class OffsetOrderedTinyNotchAdversarialTests
{
    [Theory]
    [InlineData(false, 0.0)]
    [InlineData(true, 0.0)]
    [InlineData(false, 50000000.0)]
    [InlineData(true, 50000000.0)]
    public void AdditionalSubCorridorNotchCannotBeCertified(bool reverse, double origin)
    {
        List<Point2> source = [new(origin, origin), new(origin + 100, origin),
            new(origin + 100, origin + 100), new(origin, origin + 100)];
        List<Point2> output = [new(origin - 1, origin - 1), new(origin + 101, origin - 1),
            new(origin + 101, origin + 101), new(origin + 50.001, origin + 101),
            new(origin + 50.0005, origin + 100.995), new(origin + 50, origin + 101),
            new(origin - 1, origin + 101)];
        if (reverse) { source.Reverse(); output.Reverse(); }
        Geometry.ValidateContour(source);
        Geometry.ValidateContour(output);
        var result = OffsetOrderedDiagnostic.Compare(source, 1, output, .02);
        Assert.False(result.OrderedMatch, "A sub-corridor extra V feature cannot be certified as preserved.");
    }
}
