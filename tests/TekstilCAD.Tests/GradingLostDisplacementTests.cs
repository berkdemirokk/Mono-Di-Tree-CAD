using TekstilCAD.Core;
using TekstilCAD.Grading;

namespace TekstilCAD.Tests;

public class GradingLostDisplacementTests
{
    private static PatternPiece Square() => new() { Points = [new(100, 100), new(110, 100), new(110, 110), new(100, 110)] };

    [Fact]
    public void IncrementLostDuringAdditionIsRejectedForSingleAndBatch()
    {
        var source = Square();
        var original = source.Points.ToArray();
        Point2[] increments = [new(1e-20, 0), new(0, 0), new(0, 0), new(0, 0)];
        Assert.Throws<InvalidDataException>(() => GradingEngine.Generate(source, increments, 1, "L"));
        Assert.Throws<InvalidDataException>(() => GradingEngine.GenerateSizes(source, increments, [new("M", 0), new("L", 1)]));
        Assert.Equal(original, source.Points);
    }

    [Fact]
    public void IncrementLostDuringMultiplicationIsRejected()
    {
        var source = Square();
        Point2[] increments = [new(double.Epsilon, 0), new(0, 0), new(0, 0), new(0, 0)];
        Assert.Throws<InvalidDataException>(() => GradingEngine.Generate(source, increments, 0.5, "L"));
    }

    [Fact]
    public void ZeroStepsRemainValidAndRepresentableDisplacementIsApplied()
    {
        var source = Square();
        Point2[] increments = [new(1, 0), new(0, 0), new(0, 0), new(0, 0)];
        Assert.Equal(source.Points, GradingEngine.Generate(source, increments, 0, "M").Points);
        Assert.Equal(new Point2(101, 100), GradingEngine.Generate(source, increments, 1, "L").Points[0]);
    }

    [Fact]
    public void DisplacementWithLargeRelativeRoundingErrorIsRejected()
    {
        var source = new PatternPiece { Points = [new(99_999_900, 0), new(99_999_910, 0), new(99_999_910, 10), new(99_999_900, 10)] };
        Point2[] increments = [new(1e-8, 0), new(0, 0), new(0, 0), new(0, 0)];
        Assert.Throws<InvalidDataException>(() => GradingEngine.Generate(source, increments, 1, "L"));
    }

    [Fact]
    public void OrdinaryDecimalDisplacementIsAccepted()
    {
        var source = Square();
        Point2[] increments = [new(0.1, 0), new(0, 0), new(0, 0), new(0, 0)];
        Assert.Equal(100.1, GradingEngine.Generate(source, increments, 1, "L").Points[0].X);
    }

    [Theory]
    [InlineData(1.0, -1e-20)]
    [InlineData(-1.0, 1e-20)]
    public void NegativeDisplacementsLostOnYAxisIdentifyThePoint(double steps, double delta)
    {
        var source = Square();
        Point2[] increments = [new(0, 0), new(0, delta), new(0, 0), new(0, 0)];
        var error = Assert.Throws<InvalidDataException>(() => GradingEngine.Generate(source, increments, steps, "S"));
        Assert.Contains("Nokta 1, Y", error.Message);
        Assert.Equal(new Point2(110, 100), source.Points[1]);
    }
}
