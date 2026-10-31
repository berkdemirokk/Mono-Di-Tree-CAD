using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class ClosingEdgeCollapseTests
{
    [Theory]
    [InlineData(0)] [InlineData(3)]
    public void MovingAnImplicitClosingEndpointOntoOtherIsRejected(int index)
    {
        var source=new PatternPiece { Closed=true,Points=[new(0,0),new(10,0),new(10,10),new(0,10)] };
        var before=source.Clone();var position=index==0 ? source.Points[^1] : source.Points[0];
        var error=Assert.Throws<ArgumentException>(()=>PatternTools.MovePoint(source,index,position));
        Assert.Contains("kapanış kenarını",error.Message);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
    }
    [Theory]
    [InlineData(0)] [InlineData(4)]
    public void ExistingExplicitClosingAliasStillMovesAsOneVertex(int index)
    {
        var source=new PatternPiece { Closed=true,Points=[new(0,0),new(10,0),new(10,10),new(0,10),new(0,0)] };
        var result=PatternTools.MovePoint(source,index,new Point2(-1,-1));
        Assert.Equal(new Point2(-1,-1),result.Points[0]);Assert.Equal(result.Points[0],result.Points[^1]);
    }
    [Fact]
    public void NormalUnclosedEndpointMoveRemainsAvailable()
    {
        var source=new PatternPiece { Closed=true,Points=[new(0,0),new(10,0),new(10,10),new(0,10)] };
        Assert.Equal(new Point2(-1,0),PatternTools.MovePoint(source,0,new Point2(-1,0)).Points[0]);
    }

    [Fact]
    public void InsertingFirstVertexOnImplicitClosingEdgeIsRejectedWithoutChangingSource()
    {
        var source=new PatternPiece { Closed=true,Points=[new(0,0),new(10,0),new(10,10),new(0,10)] };
        var before=source.Clone();
        Assert.Throws<ArgumentException>(()=>PatternTools.InsertPoint(source,3,source.Points[0]));
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
        var result=PatternTools.InsertPoint(source,3,new Point2(0,5));
        Assert.Equal(5,result.Points.Count);
        Assert.Equal(new Point2(0,5),result.Points[^1]);
        Assert.Equal(4,source.Points.Count);
    }
}
