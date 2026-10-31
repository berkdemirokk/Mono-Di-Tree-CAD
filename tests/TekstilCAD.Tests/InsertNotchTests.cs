using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class InsertNotchTests
{
    [Fact]
    public void CutsInwardAndPreservesSourceAndGrainline()
    {
        var p = new PatternPiece { Points=[new(0,0),new(20,0),new(20,10),new(0,10)], Grainline=new() { Start=new(2,2),End=new(2,8) } };
        var result=PatternTools.InsertNotch(p,0,0.5,2);
        Assert.Equal(new[] { new Point2(0,0),new(8,0),new(10,2),new(12,0),new(20,0),new(20,10),new(0,10) },result.Points);
        Assert.Equal(4,p.Points.Count);Assert.True(result.Grainline!.NeedsReview);Assert.False(p.Grainline!.NeedsReview);
    }
    [Fact]
    public void ClockwiseAndExplicitClosingEdgePointInward()
    {
        var p=new PatternPiece { Points=[new(0,0),new(0,10),new(20,10),new(20,0),new(0,0)] };
        var result=PatternTools.InsertNotch(p,3,0.5,2);
        Assert.Equal(new Point2(10,2),result.Points[5]);Assert.Equal(result.Points[0],result.Points[^1]);
    }
    [Fact]
    public void InvalidNotchDoesNotChangeOriginal()
    {
        var p=new PatternPiece { Points=[new(0,0),new(20,0),new(20,4),new(0,4)] };
        Assert.Throws<ArgumentException>(()=>PatternTools.InsertNotch(p,0,0.5,5));
        Assert.Throws<ArgumentException>(()=>PatternTools.InsertNotch(p,0,0.5,10));
        Assert.Equal(4,p.Points.Count);
    }
    [Fact]
    public void OpenPathsAndCancellationAreRejected()
    {
        var p=new PatternPiece { Closed=false,Points=[new(0,0),new(20,0)] };
        Assert.Throws<ArgumentException>(()=>PatternTools.InsertNotch(p,0,0.5,2));
        using var c=new CancellationTokenSource();c.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>PatternTools.InsertNotch(p,0,0.5,2,c.Token));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothWindingsRemoveExactTriangleArea(bool reverse)
    {
        var points=new List<Point2> { new(0,0),new(20,0),new(20,10),new(0,10) };
        if(reverse) points.Reverse();
        var p=new PatternPiece { Points=points };
        var edited=PatternTools.InsertNotch(p,0,0.5,2);
        Assert.Equal(Math.Abs(Geometry.SignedArea(points))-4,Math.Abs(Geometry.SignedArea(edited.Points)),8);
    }
    [Fact]
    public void ExplicitDuplicateFirstVertexIdentifiesSameOutgoingEdge()
    {
        var p=new PatternPiece { Points=[new(0,0),new(20,0),new(20,10),new(0,10),new(0,0)] };
        Assert.Equal(PatternTools.InsertNotch(p,0,.5,2).Points,PatternTools.InsertNotch(p,4,.5,2).Points);
    }
    [Fact]
    public void ConcaveEdgeCutsInwardAndCrossingOtherBoundaryIsRejected()
    {
        var p=new PatternPiece { Points=[new(0,0),new(20,0),new(20,20),new(12,20),new(12,5),new(8,5),new(8,20),new(0,20)] };
        var cut=PatternTools.InsertNotch(p,4,.5,1);
        Assert.Equal(new Point2(10,4),cut.Points[6]);
        Assert.Equal(Math.Abs(Geometry.SignedArea(p.Points))-1,Math.Abs(Geometry.SignedArea(cut.Points)),8);
        Assert.Throws<ArgumentException>(()=>PatternTools.InsertNotch(p,0,.5,6));
        Assert.Equal(8,p.Points.Count);
    }
    [Theory]
    [InlineData(0,1)] [InlineData(1,1)] [InlineData(.1,3)] [InlineData(.5,0)]
    public void RejectsOutsideEdgeOrNonpositiveDepth(double fraction,double depth)
    {
        var p=new PatternPiece { Points=[new(0,0),new(20,0),new(20,10),new(0,10)] };
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.InsertNotch(p,0,fraction,depth));
    }
}
