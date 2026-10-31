using TekstilCAD.Core;
using TekstilCAD.Grading;

namespace TekstilCAD.Tests;

public class GradingUniformTranslationPrecisionTests
{
    [Fact]
    public void LargeUniformTranslationMustNotDistortShortEdge()
    {
        var source = new PatternPiece { Points = [new(0, 0), new(0.00001, 0), new(0.00001, 1), new(0, 1)] };
        var before = source.Points.ToArray();
        Point2[] increments = [new(99999999, 0), new(99999999, 0), new(99999999, 0), new(99999999, 0)];

        Assert.Throws<InvalidDataException>(() => GradingEngine.Generate(source, increments, 1, "L"));
        Assert.Throws<InvalidDataException>(() => GradingEngine.GenerateSizes(source, increments, [new("M", 0), new("L", 1)]));
        Assert.Equal(before, source.Points);
    }

    [Fact]
    public void RepresentableUniformTranslationStillSucceeds()
    {
        var source = new PatternPiece { Points = [new(0, 0), new(10, 0), new(10, 20), new(0, 20)] };
        Point2[] increments = [new(50, 10), new(50, 10), new(50, 10), new(50, 10)];

        var result = GradingEngine.Generate(source, increments, 1, "L");
        Assert.Equal(new Point2[] { new(50, 10), new(60, 10), new(60, 30), new(50, 30) }, result.Points);
    }
}
