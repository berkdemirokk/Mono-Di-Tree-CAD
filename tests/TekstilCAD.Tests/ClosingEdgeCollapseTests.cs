using TekstilCAD.Core;
using TekstilCAD.Patterns;
namespace TekstilCAD.Tests;
public class ClosingEdgeCollapseTests
{
    [Theory]
    [InlineData(0)] [InlineData(3)]
    public void MovingAnImplicitClosingEndpointOntoOtherIsRejectedEvenWhenMarked(int index)
    {
        var source=new PatternPiece { Closed=true,Points=[new(0,0),new(10,0),new(10,10),new(0,10)],
            Notches=[new NotchMark { VertexIndex=3,Position=new(0,10) }] };
        var before=source.Clone();var position=index==0 ? source.Points[^1] : source.Points[0];
        var error=Assert.Throws<ArgumentException>(()=>PatternTools.MovePoint(source,index,position));
        Assert.Contains("kapanış kenarını",error.Message);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
    }
    [Fact]
    public void ExistingExplicitClosingAliasStillMovesAsOneVertex()
    {
        var source=new PatternPiece { Closed=true,Points=[new(0,0),new(10,0),new(10,10),new(0,10),new(0,0)],
            Notches=[new NotchMark { VertexIndex=0,Position=new(0,0) }] };
        var result=PatternTools.MovePoint(source,4,new Point2(-1,-1));
        Assert.Equal(result.Points[0],result.Points[^1]);Assert.Equal(result.Points[0],result.Notches![0].Position);
    }
}
