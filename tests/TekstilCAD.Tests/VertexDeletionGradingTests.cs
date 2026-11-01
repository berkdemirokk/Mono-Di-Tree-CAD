using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class VertexDeletionGradingTests
{
    private static (PatternPiece Piece,GradingRuleSet Rule) Fixture(bool repeated)
    {
        var piece=new PatternPiece { Points=[new(0,0),new(20,0),new(20,20),new(0,20)],
            Notches=[new NotchMark { VertexIndex=2,Position=new(20,20) }] };
        var delta=new List<Point2> { new(1,2),new(3,4),new(5,6),new(7,8) };
        if(repeated) { piece.Points.Add(piece.Points[0]);delta.Add(delta[0]); }
        return (piece,new GradingRuleSet { SourcePieceId=piece.Id,SourceClosed=true,SourcePoints=[..piece.Points],Deltas=delta });
    }
    [Theory]
    [InlineData(false,1)]
    [InlineData(true,1)]
    [InlineData(true,0)]
    [InlineData(true,4)]
    public void DeletedVertexRemapsRemainingIncrementsAndMarks(bool repeated,int index)
    {
        var (piece,rule)=Fixture(repeated);var before=piece.Clone();var oldRule=rule.Clone();
        var edited=PatternTools.DeletePoint(piece,index);edited.Id=piece.Id;
        var mapped=rule.WithVertexDeletionFor(piece,edited,index);
        Assert.NotNull(mapped);Assert.Equal(edited.Points,mapped!.SourcePoints);
        Assert.Equal(edited.Points.Count,mapped.Deltas.Count);
        Assert.Equal(index==1 ? new Point2(5,6) : new Point2(3,4),mapped.Deltas[index==1 ? 1 : 0]);
        Assert.Equal(edited.Points[edited.Notches![0].VertexIndex],edited.Notches[0].Position);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before],GradingRules=[oldRule] },new ProjectDocument { Pieces=[piece],GradingRules=[rule] }));
    }
    [Fact]
    public void OpenPathEndpointDeletionKeepsRemainingRuleAndMarker()
    {
        var piece=new PatternPiece { Closed=false,Points=[new(0,0),new(10,0),new(20,10)],
            Notches=[new NotchMark { VertexIndex=2,Position=new(20,10) }] };
        var negativeZero=BitConverter.Int64BitsToDouble(long.MinValue);
        var rule=new GradingRuleSet { SourcePieceId=piece.Id,SourceClosed=false,SourcePoints=[..piece.Points],
            Deltas=[new(9,9),new(negativeZero,2),new(3,4)] };
        var edited=PatternTools.DeletePoint(piece,0);edited.Id=piece.Id;
        var mapped=rule.WithVertexDeletionFor(piece,edited,0);
        Assert.NotNull(mapped);Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(mapped!.Deltas[0].X));
        Assert.Equal(1,edited.Notches!.Single().VertexIndex);Assert.Equal(edited.Points[1],edited.Notches[0].Position);
    }
    [Fact]
    public void StaleOrConflictingRulesAndEditedGeometryAreRejected()
    {
        var (piece,rule)=Fixture(true);var edited=PatternTools.DeletePoint(piece,1);edited.Id=piece.Id;
        var zero=BitConverter.Int64BitsToDouble(long.MinValue);
        rule.SourcePoints[0]=new Point2(zero,0);
        Assert.Null(rule.WithVertexDeletionFor(piece,edited,1));
        rule.SourcePoints[0]=piece.Points[0];rule.Deltas[^1]=new Point2(99,2);
        Assert.Null(rule.WithVertexDeletionFor(piece,edited,1));
        rule.Deltas[^1]=rule.Deltas[0];edited.Points[0]=new Point2(zero,0);
        Assert.Null(rule.WithVertexDeletionFor(piece,edited,1));
    }
}
