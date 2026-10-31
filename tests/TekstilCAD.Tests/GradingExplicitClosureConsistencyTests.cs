using TekstilCAD.Core;
using TekstilCAD.Grading;

namespace TekstilCAD.Tests;

public class GradingExplicitClosureConsistencyTests
{
    private static PatternPiece Square() => new() { Points = [new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0)] };

    [Fact]
    public void DifferentClosingRuleRejectsSingleAndBatchWithoutChangingSource()
    {
        var source = Square();
        var before = source.Points.ToArray();
        Point2[] increments = [new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(1, 1)];
        Assert.Throws<ArgumentException>(() => GradingEngine.Generate(source, increments, 1, "L"));
        Assert.Throws<ArgumentException>(() => GradingEngine.GenerateSizes(source, increments, [new("M", 0), new("L", 1)]));
        Assert.Equal(before, source.Points);
    }

    [Fact]
    public void MatchingClosingRulePreservesExplicitClosure()
    {
        Point2[] increments = [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1), new(-1, -1)];
        var generated = GradingEngine.Generate(Square(), increments, 1, "L");
        Assert.Equal(new Point2(-1, -1), generated.Points[0]);
        Assert.Equal(generated.Points[0], generated.Points[^1]);
    }

}
