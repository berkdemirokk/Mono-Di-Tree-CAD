using TekstilCAD.Core;
using TekstilCAD.Patterns;
namespace TekstilCAD.Tests;
public class OpenPathJoinGradingTests
{
    [Theory]
    [InlineData(false,false,false)] [InlineData(false,false,true)] [InlineData(false,true,false)] [InlineData(false,true,true)]
    [InlineData(true,false,false)] [InlineData(true,false,true)] [InlineData(true,true,false)] [InlineData(true,true,true)]
    public void GradingFollowsChosenEndpointDirectionsForSharedOrBridgedPaths(bool reverseA,bool reverseB,bool shared)
    {
        var a=new List<Point2> { new(0,0),new(10,0) };var b=new List<Point2> { new(shared ? 10 : 11,0),new(20,5) };
        var da=new List<Point2> { new(1,2),new(3,4) };var db=new List<Point2> { new(3,4),new(5,6) };
        if(reverseA) { a.Reverse();da.Reverse(); }if(reverseB) { b.Reverse();db.Reverse(); }
        var first=new PatternPiece { Closed=false,Points=a };var second=new PatternPiece { Closed=false,Points=b };
        var ra=new GradingRuleSet { SourcePieceId=first.Id,SourceClosed=false,SourcePoints=[..a],Deltas=da };
        var rb=new GradingRuleSet { SourcePieceId=second.Id,SourceClosed=false,SourcePoints=[..b],Deltas=db };
        var before=new ProjectDocument { Pieces=[first.Clone(),second.Clone()],GradingRules=[ra.Clone(),rb.Clone()] };
        var joined=OpenPathJoin.JoinSelected(first,second,reverseA ? 0 : 1,reverseB ? 1 : 0,shared ? 0 : 1);
        var result=OpenPathJoin.JoinGrading(first,second,ra,rb,joined);
        Assert.NotNull(result);Assert.Equal(joined.Piece.Id,result!.SourcePieceId);Assert.Equal(joined.Piece.Points,result.SourcePoints);
        Assert.Equal(shared ? new[] { new Point2(1,2),new Point2(3,4),new Point2(5,6) } : new[] { new Point2(1,2),new Point2(3,4),new Point2(3,4),new Point2(5,6) },result.Deltas);
        result.Deltas[0]=new(99,99);Assert.True(DocumentSession.ContentEquals(before,new ProjectDocument { Pieces=[first,second],GradingRules=[ra,rb] }));
    }
    [Fact]
    public void RejectsStaleOrDifferentBitsAtJoinAndTamperedOutput()
    {
        var z=BitConverter.Int64BitsToDouble(long.MinValue);
        var a=new PatternPiece { Closed=false,Points=[new(0,0),new(10,0)] };var b=new PatternPiece { Closed=false,Points=[new(11,0),new(20,5)] };
        var ra=new GradingRuleSet { SourcePieceId=a.Id,SourceClosed=false,SourcePoints=[..a.Points],Deltas=[new(1,2),new(0,0)] };
        var rb=new GradingRuleSet { SourcePieceId=b.Id,SourceClosed=false,SourcePoints=[..b.Points],Deltas=[new(0,0),new(3,4)] };
        var joined=OpenPathJoin.JoinSelected(a,b,1,0,1);Assert.NotNull(OpenPathJoin.JoinGrading(a,b,ra,rb,joined));
        rb.Deltas[0]=new(z,0);Assert.Null(OpenPathJoin.JoinGrading(a,b,ra,rb,joined));rb.Deltas[0]=new(0,0);
        ra.SourcePoints[0]=new(z,0);Assert.Null(OpenPathJoin.JoinGrading(a,b,ra,rb,joined));ra.SourcePoints[0]=a.Points[0];
        joined.Piece.Points[0]=new(z,0);Assert.Null(OpenPathJoin.JoinGrading(a,b,ra,rb,joined));
        using var cancel=new CancellationTokenSource();cancel.Cancel();Assert.Throws<OperationCanceledException>(()=>OpenPathJoin.JoinGrading(a,b,ra,rb,joined,cancel.Token));
    }
}
