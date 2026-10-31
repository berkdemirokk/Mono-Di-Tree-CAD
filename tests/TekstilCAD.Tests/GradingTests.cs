using TekstilCAD.Core;
using TekstilCAD.Grading;
namespace TekstilCAD.Tests;
public class GradingTests
{
    private static PatternPiece Square() => new() { Name="Ön", Points=[new(0,0),new(100,0),new(100,100),new(0,100)] };
    private static Point2[] Growth => [new(-2,-3),new(2,-3),new(2,3),new(-2,3)];
    [Fact] public void TwoSizeStepsProduceExactMillimeterCoordinates()
    {
        var source=Square(); var result=GradingEngine.Generate(source,Growth,2,"XL");
        Assert.Equal(new Point2(-4,-6), result.Points[0]);
        Assert.Equal(new Point2(104,106), result.Points[2]);
        Assert.Equal(108,Geometry.Bounds(result.Points).Width);
        Assert.Equal(112,Geometry.Bounds(result.Points).Height);
        Assert.NotEqual(source.Id,result.Id); Assert.True(result.Closed);
        Assert.Equal(new Point2(0,0),source.Points[0]); Assert.Equal("Ön · XL",result.Name);
    }
    [Fact] public void NegativeStepsCreateSmallerSize() => Assert.Equal(96,Geometry.Bounds(GradingEngine.Generate(Square(),Growth,-1,"S").Points).Width);
    [Fact] public void ZeroStepsCopiesBaseWithoutAliasing()
    { var source=Square();var result=GradingEngine.Generate(source,Growth,0,"M");Assert.Equal(source.Points,result.Points);result.Points[0]=new(1,1);Assert.Equal(new Point2(0,0),source.Points[0]); }
    [Fact] public void MissingPointRuleRejected() => Assert.Throws<ArgumentException>(()=>GradingEngine.Generate(Square(),[new(1,1)],1,"L"));
    [Theory][InlineData(double.NaN)][InlineData(double.PositiveInfinity)]
    public void NonfiniteStepsRejected(double steps) => Assert.Throws<ArgumentOutOfRangeException>(()=>GradingEngine.Generate(Square(),Growth,steps,"L"));
    [Fact] public void CrossingResultRejected() => Assert.Throws<InvalidDataException>(()=>GradingEngine.Generate(Square(),[new(0,0),new(-100,100),new(0,0),new(100,-100)],1,"L"));
    [Fact] public void CollapsedResultRejected() => Assert.Throws<InvalidDataException>(()=>GradingEngine.Generate(Square(),[new(0,0),new(-100,0),new(-100,-100),new(0,-100)],1,"L"));
    [Fact] public void InvalidBaseRejected() { var piece=Square();piece.Points=[new(0,0),new(100,100),new(0,100),new(100,0)];Assert.Throws<InvalidDataException>(()=>GradingEngine.Generate(piece,Growth,1,"L")); }
    [Fact] public void OpenPolylineRemainsOpen() { var piece=new PatternPiece {Closed=false,Points=[new(0,0),new(100,0)]};var result=GradingEngine.Generate(piece,[new(1,2),new(3,4)],1,"L");Assert.False(result.Closed);Assert.Equal(new Point2(103,4),result.Points[1]); }
    [Fact] public void MultipleNamedSizesAreIndependent()
    {var result=GradingEngine.GenerateSizes(Square(),Growth,[new("S",-1),new("M",0),new("L",1)]);Assert.Equal(3,result.Count);Assert.Equal(3,result.Select(p=>p.Id).Distinct().Count());}
    [Fact] public void DuplicateSizeNamesRejected() => Assert.Throws<ArgumentException>(()=>GradingEngine.GenerateSizes(Square(),Growth,[new("L",1),new(" l ",2)]));
    [Fact] public void RuleJsonPreservesPointOrderAndIncrements()
    {var rule=new GradeRule {Name="Gömlek",BaseSize="M",Increments=[..Growth]};var copy=GradeRule.FromJson(rule.ToJson());Assert.Equal(rule.Name,copy.Name);Assert.Equal(rule.BaseSize,copy.BaseSize);Assert.Equal(rule.Increments,copy.Increments);}
    [Fact] public void NullRuleListRejected() => Assert.Throws<InvalidDataException>(()=>GradeRule.FromJson("{\"Name\":\"Test\",\"BaseSize\":\"M\",\"Increments\":null}"));
}
