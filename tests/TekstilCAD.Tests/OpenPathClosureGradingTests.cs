using TekstilCAD.Core;
using TekstilCAD.Patterns;
namespace TekstilCAD.Tests;
public class OpenPathClosureGradingTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MatchingOpenRuleBecomesIndependentClosedRule(bool shared)
    {
        var z=BitConverter.Int64BitsToDouble(long.MinValue);
        var points=new List<Point2> { new(0,0),new(10,0),new(10,10),new(0,10) };if(shared)points.Add(points[0]);
        var source=new PatternPiece { Closed=false,Points=points,Notches=[new NotchMark { VertexIndex=1,Position=points[1] }] };
        var rule=new GradingRuleSet { SourcePieceId=source.Id,SourceClosed=false,SourcePoints=[..points],Deltas=Enumerable.Repeat(new Point2(z,2),points.Count).ToList() };
        var before=new ProjectDocument { Pieces=[source.Clone()],GradingRules=[rule.Clone()] };
        var closure=OpenPathClosure.Close(source,shared ? 0 : 10);var mapped=OpenPathClosure.CloseGrading(source,rule,closure);
        Assert.NotNull(mapped);Assert.True(mapped!.SourceClosed);Assert.Equal(closure.Piece.Id,mapped.SourcePieceId);Assert.Equal(points,mapped.SourcePoints);
        Assert.All(mapped.Deltas,p=>Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(p.X)));
        mapped.Deltas[0]=new(99,99);Assert.True(DocumentSession.ContentEquals(before,new ProjectDocument { Pieces=[source],GradingRules=[rule] }));
    }
    [Fact]
    public void RejectsDifferentEndpointIncrementsAndStaleOrTamperedGeometry()
    {
        var z=BitConverter.Int64BitsToDouble(long.MinValue);
        var source=new PatternPiece { Closed=false,Points=[new(0,0),new(10,0),new(10,10),new(0,10)] };
        var rule=new GradingRuleSet { SourcePieceId=source.Id,SourceClosed=false,SourcePoints=[..source.Points],Deltas=Enumerable.Repeat(new Point2(0,0),4).ToList() };
        var closure=OpenPathClosure.Close(source,10);rule.Deltas[^1]=new(z,0);Assert.Null(OpenPathClosure.CloseGrading(source,rule,closure));
        rule.Deltas[^1]=new(0,0);rule.SourcePoints[0]=new(z,0);Assert.Null(OpenPathClosure.CloseGrading(source,rule,closure));
        rule.SourcePoints[0]=source.Points[0];closure.Piece.Points[0]=new(z,0);Assert.Null(OpenPathClosure.CloseGrading(source,rule,closure));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();Assert.Throws<OperationCanceledException>(()=>OpenPathClosure.CloseGrading(source,rule,closure,cancelled.Token));
    }
    [Fact]
    public void RejectsOppositeSignedZeroInCoincidentSourceEndpoints()
    {
        var z=BitConverter.Int64BitsToDouble(long.MinValue);
        var source=new PatternPiece { Closed=false,Points=[new(0,0),new(10,0),new(10,10),new(0,10),new(z,0)] };
        var rule=new GradingRuleSet { SourcePieceId=source.Id,SourceClosed=false,SourcePoints=[..source.Points],Deltas=Enumerable.Repeat(new Point2(0,0),5).ToList() };
        var closure=OpenPathClosure.Close(source,0);Assert.Null(OpenPathClosure.CloseGrading(source,rule,closure));
    }
}
