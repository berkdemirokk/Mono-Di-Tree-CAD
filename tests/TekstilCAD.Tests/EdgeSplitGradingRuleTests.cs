using TekstilCAD.Core;
using TekstilCAD.Grading;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class EdgeSplitGradingRuleTests
{
    [Fact]
    public void PercentageSplitInterpolatesRuleWithoutChangingSource()
    {
        var source=new PatternPiece { Closed=false,Points=[new(0,0),new(60,80)] };
        var rule=new GradingRuleSet { SourcePieceId=source.Id,SourcePoints=[..source.Points],Deltas=[new(0,0),new(10,10)] };
        var edited=PatternTools.InsertPointAtPercentage(source,0,30);
        var result=rule.WithEdgeSplitFor(source,edited,0)!;
        Assert.Equal(new Point2(3,3),result.Deltas[1]);
        var graded=GradingEngine.Generate(edited,result.Deltas,1,"L");
        Assert.Equal(new Point2(21,27),graded.Points[1]);
        Assert.Equal(2,rule.Deltas.Count);
    }

    [Fact]
    public void RoundedOrOffEdgeInterpolationLeavesOriginalRuleForReview()
    {
        var source=new PatternPiece { Closed=false,Points=[new(0,0),new(3,0)] };
        var rule=new GradingRuleSet { SourcePieceId=source.Id,SourcePoints=[..source.Points],Deltas=[new(0,0),new(1,0)] };
        var edited=new PatternPiece { Closed=false,Points=[new(0,0),new(1,0),new(3,0)] };
        Assert.Null(rule.WithEdgeSplitFor(source,edited,0));
        edited.Points[1]=new(1,0.0000001);
        Assert.Null(rule.WithEdgeSplitFor(source,edited,0));
        Assert.Equal(source.Points,rule.SourcePoints);
    }

    [Fact]
    public void ClosingEdgeAndRepeatedClosingVertexUseTheCorrectEndpoints()
    {
        var source=new PatternPiece { Points=[new(0,0),new(20,0),new(0,20),new(0,0)] };
        var rule=new GradingRuleSet { SourcePieceId=source.Id,SourceClosed=true,SourcePoints=[..source.Points],Deltas=[new(0,0),new(4,0),new(0,4),new(0,0)] };
        var closing=PatternTools.InsertPointAtPercentage(source,2,25);
        Assert.Equal(new Point2(0,3),rule.WithEdgeSplitFor(source,closing,2)!.Deltas[3]);
        var first=PatternTools.InsertPointAtPercentage(source,3,25);
        Assert.Equal(new Point2(1,0),rule.WithEdgeSplitFor(source,first,3)!.Deltas[1]);
        source.Points[1]=new(21,0);
        Assert.Null(rule.WithEdgeSplitFor(source,first,3));
    }
}
