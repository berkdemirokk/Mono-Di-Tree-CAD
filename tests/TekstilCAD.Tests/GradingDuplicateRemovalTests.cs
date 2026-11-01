using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class GradingDuplicateRemovalTests
{
    private static (PatternPiece Piece,GradingRuleSet Rule) Fixture()
    {
        var piece=new PatternPiece { Closed=true,Points=[new(0,0),new(10,0),new(10,0),new(10,10),new(0,10),new(0,0),new(0,0)],
            Notches=[new NotchMark { VertexIndex=2,Position=new(10,0) }] };
        var rule=new GradingRuleSet { SourcePieceId=piece.Id,SourceClosed=true,SourcePoints=piece.Points.ToList(),
            Deltas=[new(1,2),new(3,4),new(3,4),new(5,6),new(7,8),new(1,2),new(1,2)] };
        return (piece,rule);
    }
    [Fact]
    public void MapsMatchingCopiesAndPreservesMarkersWithoutChangingSources()
    {
        var (piece,rule)=Fixture();var before=piece.Clone();var oldRule=rule.Clone();
        var cleaned=PatternTools.RemoveConsecutiveDuplicateVertices(piece);var mapped=rule.WithDuplicateRemovalFor(piece,cleaned)!;
        Assert.Equal(cleaned.Points,mapped.SourcePoints);Assert.Equal(new[] { new Point2(1,2),new Point2(3,4),new Point2(5,6),new Point2(7,8),new Point2(1,2) },mapped.Deltas);
        Assert.Equal(1,Assert.Single(cleaned.Notches!).VertexIndex);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before],GradingRules=[oldRule] },new ProjectDocument { Pieces=[piece],GradingRules=[rule] }));
    }
    [Fact]
    public void RejectsDifferentIncrementsOrSourceBitsAndClosingAliasMismatch()
    {
        var (piece,rule)=Fixture();var cleaned=PatternTools.RemoveConsecutiveDuplicateVertices(piece);
        rule.Deltas[2]=new(3,5);Assert.Null(rule.WithDuplicateRemovalFor(piece,cleaned));
        rule.Deltas[2]=new(3,4);rule.Deltas[6]=new(1,3);Assert.Null(rule.WithDuplicateRemovalFor(piece,cleaned));
        rule.Deltas[6]=new(1,2);rule.Deltas[5]=new(1,9);rule.Deltas[6]=new(1,9);Assert.Null(rule.WithDuplicateRemovalFor(piece,cleaned));
        rule.Deltas[5]=new(1,2);rule.Deltas[6]=new(1,2);rule.SourcePoints[0]=new(BitConverter.Int64BitsToDouble(long.MinValue),0);
        Assert.Null(rule.WithDuplicateRemovalFor(piece,cleaned));
        rule.SourcePoints[0]=piece.Points[0];rule.Deltas[2]=new(3,BitConverter.Int64BitsToDouble(long.MinValue));rule.Deltas[1]=new(3,0);
        Assert.Null(rule.WithDuplicateRemovalFor(piece,cleaned));
    }
    [Fact]
    public void RejectsUnrelatedEditedGeometryAndObservesCancellation()
    {
        var (piece,rule)=Fixture();var edited=PatternTools.RemoveConsecutiveDuplicateVertices(piece);
        edited.Points[1]=new(11,0);Assert.Null(rule.WithDuplicateRemovalFor(piece,edited));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>rule.WithDuplicateRemovalFor(piece,edited,cancelled.Token));
    }
}
