using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class EdgeMidpointTests
{
    [Fact]
    public void SplitsOpenEdgeWithoutChangingSourceOrGeometry()
    {
        var source=new PatternPiece { Closed=false,Points=[new(0,0),new(8,4),new(10,4)] };
        var before=source.Points.ToArray();var result=PatternTools.InsertEdgeMidpoint(source,0);
        Assert.Equal(new[] { new Point2(0,0),new(4,2),new(8,4),new(10,4) },result.Points);
        Assert.Equal(before,source.Points);Assert.False(result.Closed);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ClosingEdgeAndExplicitClosingPointAreHandled(bool repeated)
    {
        var source=new PatternPiece { Closed=true,Points=[new(0,0),new(10,0),new(10,10),new(0,10)] };
        if(repeated)source.Points.Add(source.Points[0]);
        var closing=PatternTools.InsertEdgeMidpoint(source,3);
        Assert.Equal(new Point2(0,5),closing.Points[4]);
        if(repeated)
        {
            Assert.Equal(source.Points[0],closing.Points[^1]);
            var firstEdge=PatternTools.InsertEdgeMidpoint(source,4);
            Assert.Equal(new Point2(5,0),firstEdge.Points[1]);
        }
    }
    [Fact]
    public void PreservesRepresentableSubnormalMidpoint()
    {
        var source=new PatternPiece { Closed=false,Points=[new(0,double.Epsilon),new(10,5*double.Epsilon)] };
        var before=source.Points.ToArray();
        var result=PatternTools.InsertEdgeMidpoint(source,0);
        Assert.Equal(new Point2(5,3*double.Epsilon),result.Points[1]);
        Assert.Equal(before,source.Points);
    }
    [Fact]
    public void RejectsMissingEdgeAndUnrepresentableMidpoint()
    {
        var source=new PatternPiece { Closed=false,Points=[new(1,0),new(Math.BitIncrement(1),0)] };
        Assert.Throws<ArgumentException>(()=>PatternTools.InsertEdgeMidpoint(source,1));
        Assert.Throws<ArgumentException>(()=>PatternTools.InsertEdgeMidpoint(source,0));
        Assert.Equal(2,source.Points.Count);
    }
    [Fact]
    public void CancellationDoesNotChangeSource()
    {
        var source=new PatternPiece { Closed=false,Points=[new(0,0),new(10,0)] };
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>PatternTools.InsertEdgeMidpoint(source,0,cancellation.Token));
        Assert.Equal(2,source.Points.Count);
    }
}
