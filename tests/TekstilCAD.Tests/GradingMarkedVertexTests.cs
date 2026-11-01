using TekstilCAD.Core;
using TekstilCAD.Grading;

namespace TekstilCAD.Tests;

public sealed class GradingMarkedVertexTests
{
    private static PatternPiece Source()
    {
        var negativeZero = BitConverter.Int64BitsToDouble(long.MinValue);
        return new PatternPiece { Points = [new(negativeZero,0),new(20,0),new(20,20),new(0,20),new(negativeZero,0)],
            Notches = [new NotchMark { VertexIndex = 0, Position = new(0,negativeZero) },
                new NotchMark { VertexIndex = 2, Position = new(20,20) }] };
    }

    [Theory] [InlineData(-1)] [InlineData(0)] [InlineData(1)]
    public void NonuniformGradingMapsMarksToSameIndicesAndPreservesUnmovedBits(double steps)
    {
        var source = Source(); var before = source.Clone();
        Point2[] increments = [new(0,0),new(2,0),new(2,1),new(0,1),new(0,0)];
        var generated = GradingEngine.Generate(source, increments, steps, "M");
        Assert.Equal(new[] { 0,2 }, generated.Notches!.Select(m => m.VertexIndex));
        Assert.Equal(generated.Points[0], generated.Notches[0].Position);
        Assert.Equal(generated.Points[2], generated.Notches[1].Position);
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(generated.Points[0].X));
        Assert.Equal(0,BitConverter.DoubleToInt64Bits(generated.Notches[0].Position.X));
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(generated.Notches[0].Position.Y));
        Assert.Equal(generated.Points[0],generated.Points[^1]);
        Assert.NotSame(source.Notches![0],generated.Notches[0]);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces = [before] },
            new ProjectDocument { Pieces = [source] }));
    }

    [Fact]
    public void BatchResultsHaveIndependentMarksAndMatchEachResult()
    {
        var source = Source();
        Point2[] increments = [new(1,0),new(1,0),new(1,0),new(1,0),new(1,0)];
        var results = GradingEngine.GenerateSizes(source,increments,[new("S",-1),new("L",1)]);
        Assert.Equal(2,results.Count);
        foreach (var result in results)
            Assert.All(result.Notches!,mark=>Assert.Equal(result.Points[mark.VertexIndex],mark.Position));
        Assert.NotSame(results[0].Notches![0],results[1].Notches![0]);
        results[0].Notches![0].Position = new(99,99);
        Assert.Equal(results[1].Points[0],results[1].Notches![0].Position);
        Assert.Equal(source.Points[0],source.Notches![0].Position);
    }

    [Fact]
    public void InvalidAndAmbiguousMarksRemainRejected()
    {
        var source = Source();
        Point2[] increments = [new(0,0),new(0,0),new(0,0),new(0,0),new(0,0)];
        source.Notches![0].Position = new(1,1);
        Assert.Throws<ArgumentException>(()=>GradingEngine.Generate(source,increments,0,"M"));
        source = Source(); source.Notches!.Add(source.Notches[0].Clone());
        Assert.Throws<ArgumentException>(()=>GradingEngine.Generate(source,increments,0,"M"));
        source = Source(); source.Notches![0].VertexIndex = source.Points.Count - 1;
        Assert.Throws<ArgumentException>(()=>GradingEngine.Generate(source,increments,0,"M"));
        source = Source(); source.Notches = [];
        Assert.Throws<ArgumentException>(()=>GradingEngine.Generate(source,increments,0,"M"));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>GradingEngine.Generate(source,increments,0,"M",cancellation.Token));
    }
}
