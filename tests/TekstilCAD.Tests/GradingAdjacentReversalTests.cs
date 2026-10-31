using TekstilCAD.Core;
using TekstilCAD.Grading;

namespace TekstilCAD.Tests;

public class GradingAdjacentReversalTests
{
    [Fact]
    public void ShortOverlappingEdgesAreRejected()
    {
        var source = new PatternPiece { Closed = false, Points = [new(0, 0), new(0.00001, 0), new(0.000005, 0)] };
        Point2[] increments = [default, default, default];
        Assert.Throws<InvalidDataException>(() => GradingEngine.Generate(source, increments, 0, "M"));
    }

    [Fact]
    public void StraightContinuationRemainsValid()
    {
        var source = new PatternPiece { Closed = false, Points = [new(0, 0), new(0.00001, 0), new(0.00002, 0)] };
        Point2[] increments = [default, default, default];
        Assert.Equal(source.Points, GradingEngine.Generate(source, increments, 0, "M").Points);
    }
}
