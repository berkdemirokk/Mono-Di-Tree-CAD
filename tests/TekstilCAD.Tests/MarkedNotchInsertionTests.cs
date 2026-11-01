using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class MarkedNotchInsertionTests
{
    [Theory]
    [InlineData(false,0,6)]
    [InlineData(false,3,3)]
    [InlineData(true,0,6)]
    [InlineData(true,3,3)]
    [InlineData(true,4,6)]
    public void PhysicalNotchPreservesMarkedVerticesAndClosure(bool repeated,int edge,int expectedLastIndex)
    {
        var minusZero=BitConverter.Int64BitsToDouble(long.MinValue);
        var points=new List<Point2> { new(minusZero,minusZero),new(20,0),new(20,20),new(0,20) };
        if(repeated)points.Add(points[0]);
        var source=new PatternPiece { Points=points,Notches=[new NotchMark { VertexIndex=0,Position=points[0] },new NotchMark { VertexIndex=3,Position=points[3] }] };
        var before=source.Clone();var result=PatternTools.InsertNotch(source,edge,.5,4,2);
        Assert.Equal(source.Points.Count+3,result.Points.Count);
        Assert.Equal(new[] { 0,expectedLastIndex },result.Notches!.Select(mark=>mark.VertexIndex));
        Assert.All(result.Notches!,mark=>Assert.Equal(result.Points[mark.VertexIndex],mark.Position));
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(result.Notches![0].Position.X));
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(result.Points[0].X));
        if(repeated)Assert.Equal(result.Points[0],result.Points[^1]);
        Assert.NotSame(source.Notches![0],result.Notches[0]);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
    }

    [Fact]
    public void InvalidMarkAndCancellationDoNotModifySource()
    {
        var source=new PatternPiece { Points=[new(0,0),new(20,0),new(20,20),new(0,20)],
            Notches=[new NotchMark { VertexIndex=1,Position=new(21,0) }] };
        var before=source.Clone();Assert.ThrowsAny<ArgumentException>(()=>PatternTools.InsertNotch(source,0,.5,4,2));
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
        source.Notches![0].Position=source.Points[1];using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>PatternTools.InsertNotch(source,0,.5,4,2,cancelled.Token));
    }
}
