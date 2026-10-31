using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class ReverseOpenPathTests
{
    [Fact]
    public void ReversesOnlyOrderAndDoesNotMutateSource()
    {
        var original=new PatternPiece { Closed=false,Points=[new(0,0),new(3,4),new(7,4)] };
        var result=PatternTools.ReverseOpenPath(original);
        Assert.Equal(new[] { new Point2(7,4),new(3,4),new(0,0) },result.Points);
        Assert.Equal(new[] { new Point2(0,0),new(3,4),new(7,4) },original.Points);
        Assert.False(result.Closed);Assert.NotSame(original.Points,result.Points);
    }
    [Fact]
    public void RejectsClosedAndSelfIntersectingPaths()
    {
        var closed=new PatternPiece { Points=[new(0,0),new(10,0),new(0,10)] };
        Assert.Throws<ArgumentException>(()=>PatternTools.ReverseOpenPath(closed));
        var crossing=new PatternPiece { Closed=false,Points=[new(0,0),new(10,10),new(0,10),new(10,0)] };
        Assert.Throws<ArgumentException>(()=>PatternTools.ReverseOpenPath(crossing));
    }
    [Fact]
    public void CancelledOperationDoesNotChangeSource()
    {
        var p=new PatternPiece { Closed=false,Points=[new(0,0),new(10,0)] };
        using var c=new CancellationTokenSource();c.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>PatternTools.ReverseOpenPath(p,c.Token));Assert.Equal(new Point2(0,0),p.Points[0]);
    }
}
