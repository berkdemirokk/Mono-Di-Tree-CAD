using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class OpenPathMarkedClosureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosureCopiesMarkersAndLeavesSourceUnchanged(bool repeated)
    {
        var points=new List<Point2> { new(0,0),new(20,0),new(20,10),new(0,10) };
        if(repeated)points.Add(points[0]);
        var source=new PatternPiece { Closed=false,Points=points,
            Notches=[new NotchMark { VertexIndex=0,Position=points[0] },new NotchMark { VertexIndex=1,Position=points[1] }] };
        var before=source.Clone();
        var result=OpenPathClosure.Close(source,10).Piece;
        Assert.True(result.Closed);Assert.Equal(source.Points,result.Points);
        Assert.Equal(new[] { 0,1 },result.Notches!.Select(mark=>mark.VertexIndex));
        Assert.All(result.Notches!,mark=>Assert.Equal(result.Points[mark.VertexIndex],mark.Position));
        Assert.NotSame(source.Notches,result.Notches);Assert.NotSame(source.Notches![0],result.Notches![0]);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
    }

    [Fact]
    public void MarkOnRepeatedFinalOpenEndpointIsRejectedWithoutChangingSource()
    {
        var points=new List<Point2> { new(0,0),new(20,0),new(20,10),new(0,10),new(0,0) };
        var source=new PatternPiece { Closed=false,Points=points,Notches=[new NotchMark { VertexIndex=4,Position=points[4] }] };
        var before=source.Clone();
        Assert.Throws<ArgumentException>(()=>OpenPathClosure.Close(source,0));
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
    }
}
