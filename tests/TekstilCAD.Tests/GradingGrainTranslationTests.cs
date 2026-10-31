using TekstilCAD.Core;
using TekstilCAD.Grading;
using Xunit;

namespace TekstilCAD.Tests;

public class GradingGrainTranslationTests
{
    private static PatternPiece Source() => new() {
        Points = [new(0,0), new(100,0), new(100,200), new(0,200)],
        Grainline = new() { Start = new(50,20), End = new(50,180) }
    };

    [Fact]
    public void UniformGradingMovesGrainlineWithContourAndRequiresReview()
    {
        var source = Source();
        var generated = GradingEngine.Generate(source, [new(25,10),new(25,10),new(25,10),new(25,10)], 2, "L");
        Assert.Equal(new Point2(100,40), generated.Grainline!.Start);
        Assert.Equal(new Point2(100,200), generated.Grainline.End);
        Assert.True(generated.Grainline.NeedsReview);
        Assert.Equal(new Point2(50,20), source.Grainline!.Start);
    }

    [Fact]
    public void NonuniformGradingKeepsReferenceAndRequiresReview()
    {
        var generated = GradingEngine.Generate(Source(), [new(0,0),new(10,0),new(10,0),new(0,0)], 1, "L");
        Assert.Equal(new Point2(50,20), generated.Grainline!.Start);
        Assert.True(generated.Grainline.NeedsReview);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(-1, -20, -8)]
    [InlineData(0.5, 10, 4)]
    public void UniformStepsMoveContourAndGrainBySameDisplacement(double steps, double dx, double dy)
    {
        var source = Source();
        var generated = GradingEngine.Generate(source, [new(20,8),new(20,8),new(20,8),new(20,8)], steps, "size");
        var shift = new Point2(dx,dy);
        for (var i = 0; i < source.Points.Count; i++)
            Assert.Equal(source.Points[i] + shift, generated.Points[i]);
        Assert.Equal(source.Grainline!.Start + shift, generated.Grainline!.Start);
        Assert.Equal(source.Grainline.End + shift, generated.Grainline.End);
        Assert.True(generated.Grainline.NeedsReview);
    }

    [Fact]
    public void LostGrainDisplacementRejectsEntireBatchWithoutChangingSource()
    {
        var source = Source();
        source.Grainline = new() { Start = new(100_000_000,20), End = new(100_000_000,180) };
        var before = source.Clone();
        IReadOnlyList<PatternPiece>? result = null;
        Assert.Throws<InvalidDataException>(() => result = GradingEngine.GenerateSizes(source,
            [new(1e-9,0),new(1e-9,0),new(1e-9,0),new(1e-9,0)], [new("M",0),new("L",1)]));
        Assert.Null(result);
        Assert.Equal(before.Points, source.Points);
        Assert.Equal(before.Grainline!.Start, source.Grainline!.Start);
        Assert.Equal(before.Grainline.End, source.Grainline.End);
        Assert.False(source.Grainline.NeedsReview);
    }

    [Fact]
    public void TranslationRejectsDistortionOfShortGrainlineWithoutMutatingSource()
    {
        var source = Source();
        source.Grainline = new() { Start = new(50,20), End = new(50.01,20) };
        var before = source.Clone();
        Assert.ThrowsAny<ArgumentException>(() => GradingEngine.Generate(source,
            [new(99_999_800,0),new(99_999_800,0),new(99_999_800,0),new(99_999_800,0)], 1, "L"));
        Assert.Equal(before.Points, source.Points);
        Assert.Equal(before.Grainline!.Start, source.Grainline!.Start);
        Assert.Equal(before.Grainline.End, source.Grainline.End);
        Assert.False(source.Grainline.NeedsReview);
    }
}
