using TekstilCAD.Core;
using TekstilCAD.Grading;

namespace TekstilCAD.Tests;

public sealed class GradingSignedZeroTests
{
    [Theory] [InlineData(0)] [InlineData(1)]
    public void UniformGradingPreservesBitsOnEveryUnmovedAxisIncludingGrainline(double steps)
    {
        var negativeZero = BitConverter.Int64BitsToDouble(long.MinValue);
        var source = new PatternPiece { Points = [new(negativeZero,negativeZero),new(20,negativeZero),
            new(20,20),new(negativeZero,20)],
            Grainline = new Grainline { Start = new(5,negativeZero), End = new(6,negativeZero) } };
        Point2[] increments = [new(1,0),new(1,0),new(1,0),new(1,0)];
        var result = GradingEngine.Generate(source, increments, steps, "M");
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(result.Points[0].Y));
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(result.Points[1].Y));
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(result.Grainline!.Start.Y));
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(result.Grainline.End.Y));
        if (steps == 0) Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(result.Points[0].X));
        Assert.True(result.Grainline.NeedsReview);
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(source.Points[0].X));
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(source.Points[0].Y));
    }

    [Theory] [InlineData(0)] [InlineData(1)]
    public void MarkedSourceRemainsRejectedEvenForZeroMovement(double steps)
    {
        var source = new PatternPiece { Points = [new(0,0),new(20,0),new(20,20),new(0,20)],
            Notches = [new NotchMark { VertexIndex = 0, Position = new(0,0) }] };
        Point2[] increments = [new(1,0),new(1,0),new(1,0),new(1,0)];
        var before = source.Clone();
        Assert.Throws<ArgumentException>(() => GradingEngine.Generate(source, increments, steps, "M"));
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces = [before] },
            new ProjectDocument { Pieces = [source] }));
    }

    [Fact]
    public void NonuniformRulePreservesZeroDisplacementBits()
    {
        var negativeZero = BitConverter.Int64BitsToDouble(long.MinValue);
        var source = new PatternPiece { Points = [new(negativeZero,negativeZero),new(20,negativeZero),
            new(20,20),new(negativeZero,20)] };
        Point2[] increments = [new(0,0),new(1,0),new(1,1),new(0,1)];
        var result = GradingEngine.Generate(source, increments, 1, "L");
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(result.Points[0].X));
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(result.Points[0].Y));
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(result.Points[1].Y));
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(result.Points[3].X));
    }
}
