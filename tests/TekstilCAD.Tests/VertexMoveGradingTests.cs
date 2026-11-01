using TekstilCAD.Core;
namespace TekstilCAD.Tests;
public class VertexMoveGradingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void RepeatedClosureCarriesExactIncrements(int index)
    {
        var minusZero=BitConverter.Int64BitsToDouble(long.MinValue);
        var piece=new PatternPiece { Points=[new(0,0),new(20,0),new(20,20),new(0,20),new(0,0)] };
        var rule=new GradingRuleSet { SourcePieceId=piece.Id,SourceClosed=true,SourcePoints=[..piece.Points],Deltas=[new(minusZero,1),new(2,3),new(4,5),new(6,7),new(minusZero,1)] };
        var moved=piece.Points.ToList();moved[0]=new(-1,0);moved[^1]=moved[0];
        var mapped=rule.WithVertexMoveFor(piece,moved,index);
        Assert.NotNull(mapped);Assert.Equal(moved,mapped!.SourcePoints);Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(mapped.Deltas[0].X));
        Assert.Equal(piece.Points,rule.SourcePoints);Assert.NotSame(rule.Deltas,mapped.Deltas);
    }
    [Fact]
    public void StaleSourcesExtraMovesAndClosureBitConflictsAreRejected()
    {
        var zero=BitConverter.Int64BitsToDouble(long.MinValue);
        var p=new PatternPiece { Points=[new(0,0),new(10,0),new(10,10),new(0,10),new(0,0)] };
        var r=new GradingRuleSet { SourcePieceId=p.Id,SourceClosed=true,SourcePoints=[..p.Points],Deltas=Enumerable.Repeat(new Point2(0,0),5).ToList() };
        var next=p.Points.ToList();next[1]=new(11,0);Assert.NotNull(r.WithVertexMoveFor(p,next,1));
        next[2]=new(11,10);Assert.Null(r.WithVertexMoveFor(p,next,1));next[2]=p.Points[2];
        r.SourcePoints[0]=new(zero,0);Assert.Null(r.WithVertexMoveFor(p,next,1));r.SourcePoints[0]=p.Points[0];
        r.Deltas[^1]=new(zero,0);Assert.Null(r.WithVertexMoveFor(p,next,1));
    }
}
