using TekstilCAD.Core;
using TekstilCAD.Patterns;
namespace TekstilCAD.Tests;
public class SplitOpenPathGradingTests
{
    [Fact]
    public void SplitCopiesMatchingIncrementsAndSharesBoundaryWithoutMutatingSource()
    {
        var z=BitConverter.Int64BitsToDouble(long.MinValue);
        var source=new PatternPiece { Closed=false,Points=[new(0,0),new(10,0),new(20,10),new(30,10)] };
        var rule=new GradingRuleSet { SourcePieceId=source.Id,SourceClosed=false,SourcePoints=[..source.Points],Deltas=[new(1,2),new(z,3),new(5,6),new(7,8)] };
        var before=new ProjectDocument { Pieces=[source.Clone()],GradingRules=[rule.Clone()] };var (a,b)=OpenPathSplit.Split(source,1);
        var mapped=rule.SplitOpenPathFor(source,a,b,1);Assert.NotNull(mapped);
        Assert.Equal(a.Id,mapped!.Value.First.SourcePieceId);Assert.Equal(b.Id,mapped.Value.Second.SourcePieceId);
        Assert.Equal(rule.Deltas.Take(2),mapped.Value.First.Deltas);Assert.Equal(rule.Deltas.Skip(1),mapped.Value.Second.Deltas);
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(mapped.Value.First.Deltas[^1].X));
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(mapped.Value.Second.Deltas[0].X));
        mapped.Value.First.Deltas[0]=new(99,99);
        Assert.True(DocumentSession.ContentEquals(before,new ProjectDocument { Pieces=[source],GradingRules=[rule] }));
    }
    [Fact]
    public void SplitRejectsStaleOrBitDifferentSourceAndAlteredOutput()
    {
        var z=BitConverter.Int64BitsToDouble(long.MinValue);
        var source=new PatternPiece { Closed=false,Points=[new(0,0),new(10,0),new(20,10)] };
        var rule=new GradingRuleSet { SourcePieceId=source.Id,SourceClosed=false,SourcePoints=[..source.Points],Deltas=[new(1,2),new(3,4),new(5,6)] };
        var (a,b)=OpenPathSplit.Split(source,1);rule.SourcePoints[0]=new(z,0);
        Assert.Null(rule.SplitOpenPathFor(source,a,b,1));rule.SourcePoints[0]=source.Points[0];
        b.Points[0]=new(11,0);Assert.Null(rule.SplitOpenPathFor(source,a,b,1));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>rule.SplitOpenPathFor(source,a,b,1,cancelled.Token));
    }
}
