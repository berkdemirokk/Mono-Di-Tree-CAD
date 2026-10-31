using TekstilCAD.Core;
using TekstilCAD.Grading;

namespace TekstilCAD.Tests;

public class GradingClosureTopologyTests
{
    [Fact]
    public void ImplicitClosingEdgeCannotCollapseIntoExplicitClosingVertex()
    {
        var source = new PatternPiece { Points = [new(0, 0), new(100, 0), new(100, 100), new(0, 100)] };
        Point2[] increments = [default, default, default, new(0, -100)];

        Assert.Throws<InvalidDataException>(() => GradingEngine.Generate(source, increments, 1, "L"));
        Assert.Equal(new Point2(0, 100), source.Points[^1]);
    }

    [Fact]
    public void ExistingExplicitClosureRemainsSupported()
    {
        var source = new PatternPiece { Points = [new(0, 0), new(100, 0), new(100, 100), new(0, 100), new(0, 0)] };
        Point2[] increments = [new(-1, 0), default, default, default, new(-1, 0)];

        var result = GradingEngine.Generate(source, increments, 1, "L");
        Assert.Equal(result.Points[0], result.Points[^1]);
        Assert.Equal(source.Points.Count, result.Points.Count);
    }
}
