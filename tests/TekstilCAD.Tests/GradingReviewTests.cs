using System.IO;
using TekstilCAD.Core;
using TekstilCAD.Grading;

namespace TekstilCAD.Tests;

public class GradingReviewTests
{
    private static PatternPiece Square() => new() { Name = "Ön", Points = [new(0,0), new(100,0), new(100,100), new(0,100)] };
    private static Point2[] Zero => [default, default, default, default];

    [Fact]
    public void GeneratedNameCannotExceedPersistenceLimit()
    {
        var source = Square(); source.Name = new string('a', 1024);
        Assert.Throws<ArgumentException>(() => GradingEngine.Generate(source, Zero, 0, "L"));
    }
    [Fact]
    public void InvalidSizeNameCannotAllocateUnboundedLabel()
    {
        Assert.Throws<ArgumentException>(() => GradingEngine.Generate(Square(), Zero, 0, new string('L', 129)));
        Assert.Throws<ArgumentException>(() => GradingEngine.Generate(Square(), Zero, 0, " \t "));
    }
    [Fact]
    public void NullSourceNameIsRejectedBeforeGeneratingGeometry()
    {
        var source = Square(); source.Name = null!;
        Assert.Throws<ArgumentException>(() => GradingEngine.Generate(source, Zero, 0, "L"));
    }
    [Fact]
    public void EmptySizeBatchStillChecksRequiredArguments()
    {
        Assert.Throws<ArgumentNullException>(() => GradingEngine.GenerateSizes(null!, Zero, []));
        Assert.Throws<ArgumentNullException>(() => GradingEngine.GenerateSizes(Square(), null!, []));
    }
    [Fact]
    public void OversizedBatchRejectedBeforeQuadraticWork()
    {
        var source = new PatternPiece { Points = Enumerable.Range(0, 1000).Select(i => new Point2(i, i % 2)).ToList() };
        var rules = new Point2[1000];
        var sizes = Enumerable.Range(0, 51).Select(i => new GradeSize(i.ToString(), i)).ToArray();
        Assert.Throws<ArgumentException>(() => GradingEngine.GenerateSizes(source, rules, sizes));
    }
    [Fact]
    public void RuleNameAndRulePointCountAreBounded()
    {
        var rule = new GradeRule { Name = new string('x', 1025), Increments = [..Zero] };
        Assert.Throws<InvalidDataException>(() => rule.ToJson());
        rule.Name = "Test"; rule.Increments = Enumerable.Repeat(default(Point2), GradingEngine.MaximumGradePoints + 1).ToList();
        Assert.Throws<InvalidDataException>(() => rule.ToJson());
    }
    [Fact]
    public void OutputAtSupportedCoordinateEdgeRemainsValidAndSourceUnchanged()
    {
        var source = Square();
        var result = GradingEngine.Generate(source, Enumerable.Repeat(new Point2(Geometry.MaxCoordinate - 100, 0),4).ToArray(),1,"L");
        Assert.Equal(Geometry.MaxCoordinate, result.Points[1].X);
        Assert.Equal(100, source.Points[1].X);
    }
}
