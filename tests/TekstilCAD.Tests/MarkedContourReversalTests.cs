using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class MarkedContourReversalTests
{
    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public void ReversePreservesMarkedPositionsAndIsAnInvolution(bool closed,bool repeated)
    {
        var points=new List<Point2> { new(0,0),new(20,0),new(20,10),new(0,10) };
        if(repeated)points.Add(points[0]);
        var source=new PatternPiece { Closed=closed,Points=points,
            Notches=[new NotchMark { VertexIndex=0,Position=points[0] },new NotchMark { VertexIndex=1,Position=points[1] }] };
        var before=source.Clone();
        var reversed=closed ? PatternTools.ReverseClosedContour(source) : PatternTools.ReverseOpenPath(source);
        Assert.Equal(closed ? new[] { 0,3 } : new[] { 3,2 },reversed.Notches!.Select(mark=>mark.VertexIndex));
        Assert.All(reversed.Notches!,mark=>Assert.Equal(mark.Position,reversed.Points[mark.VertexIndex]));
        Assert.NotSame(source.Notches,reversed.Notches);
        var restored=closed ? PatternTools.ReverseClosedContour(reversed) : PatternTools.ReverseOpenPath(reversed);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
        Assert.Equal(before.Points,restored.Points);
        Assert.Equal(before.Notches!.Select(mark=>(mark.VertexIndex,mark.Position)),restored.Notches!.Select(mark=>(mark.VertexIndex,mark.Position)));
    }

    [Fact]
    public void InvalidMarkAndCancellationDoNotChangeSource()
    {
        var source=new PatternPiece { Closed=false,Points=[new(0,0),new(10,0)],
            Notches=[new NotchMark { VertexIndex=1,Position=new(11,0) }] };
        var before=source.Clone();
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.ReverseOpenPath(source));
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
        source.Notches![0].Position=source.Points[1];using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>PatternTools.ReverseOpenPath(source,cancelled.Token));
    }
}
