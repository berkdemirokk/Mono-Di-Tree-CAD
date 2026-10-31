using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class NotchSupplementaryEditTests
{
    [Fact]
    public void ReversingOpenPathKeepsEachMarkerAtItsOriginalLocation()
    {
        var p=new PatternPiece { Closed=false,Points=[new(0,0),new(10,0),new(10,10),new(20,10)],
            Notches=[new NotchMark { VertexIndex=0,Position=new(0,0) },new NotchMark { VertexIndex=2,Position=new(10,10) }] };
        var before=p.Clone();var result=PatternTools.ReverseOpenPath(p);
        Assert.Equal(new[] { 3,1 },result.Notches!.Select(n=>n.VertexIndex));
        Assert.Equal(new[] { new Point2(0,0),new Point2(10,10) },result.Notches.Select(n=>n.Position));
        Assert.NotSame(p.Notches,result.Notches);Assert.NotSame(p.Notches![0],result.Notches[0]);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[p] }));
    }
    [Theory]
    [InlineData(false,0,2)] [InlineData(true,0,2)] [InlineData(true,4,2)] [InlineData(false,3,1)] [InlineData(true,3,1)]
    public void MidpointReindexesMarkersForNormalAndClosingEdges(bool repeated,int after,int expected)
    {
        var p=new PatternPiece { Points=[new(0,0),new(10,0),new(10,10),new(0,10)],
            Notches=[new NotchMark { VertexIndex=1,Position=new(10,0) }] };
        if(repeated)p.Points.Add(p.Points[0]);
        var original=p.Clone();var result=PatternTools.InsertEdgeMidpoint(p,after);
        Assert.Equal(expected,result.Notches![0].VertexIndex);Assert.Equal(new Point2(10,0),result.Notches[0].Position);
        Assert.Equal(result.Points[expected],result.Notches[0].Position);
        if(repeated)Assert.Equal(result.Points[0],result.Points[^1]);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[original] },new ProjectDocument { Pieces=[p] }));
    }
    [Fact]
    public void InvalidMarkersMissingEdgeAndPrecisionLossAreRejected()
    {
        var p=new PatternPiece { Closed=false,Points=[new(0,0),new(10,0),new(10,10)],Notches=[new NotchMark { VertexIndex=1,Position=new(9,0) }] };
        Assert.Throws<ArgumentException>(()=>PatternTools.ReverseOpenPath(p));Assert.Throws<ArgumentException>(()=>PatternTools.InsertEdgeMidpoint(p,0));
        p.Notches![0].Position=p.Points[1];Assert.Throws<ArgumentException>(()=>PatternTools.InsertEdgeMidpoint(p,2));
        p.Points=[new(1,0),new(Math.BitIncrement(1),0)];p.Notches=[new NotchMark { VertexIndex=0,Position=p.Points[0] }];
        Assert.Throws<ArgumentException>(()=>PatternTools.InsertEdgeMidpoint(p,0));
    }
    [Fact]
    public void CancellationLeavesMarkersUntouched()
    {
        var p=new PatternPiece { Closed=false,Points=[new(0,0),new(10,0)],Notches=[new NotchMark { VertexIndex=1,Position=new(10,0) }] };
        using var c=new CancellationTokenSource();c.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>PatternTools.ReverseOpenPath(p,c.Token));
        Assert.ThrowsAny<OperationCanceledException>(()=>PatternTools.InsertEdgeMidpoint(p,0,c.Token));Assert.Equal(1,p.Notches![0].VertexIndex);
    }
}
