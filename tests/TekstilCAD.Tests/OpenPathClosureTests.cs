using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class OpenPathClosureTests
{
    private static PatternPiece Open(params Point2[] points)=>new() { Name="Kaynak",Closed=false,Points=[..points] };
    [Fact]
    public void ClosesWithoutMovingEndpointsAndKeepsSource()
    {
        var source=Open(new(0,0),new(10,0),new(10,10),new(0,10)); var original=source.Clone();
        var result=OpenPathClosure.Close(source,10);
        Assert.True(result.Piece.Closed); Assert.Equal(original.Points,result.Piece.Points);
        Assert.Equal(original.Points,source.Points); Assert.False(source.Closed);
        Assert.Equal(source.Id,result.SourceId); Assert.NotEqual(source.Id,result.Piece.Id); Assert.Equal(10,result.ClosingLength);
    }
    [Fact]
    public void ExplicitlyRepeatedEndpointIsPreserved()
    {
        var source=Open(new(0,0),new(10,0),new(10,10),new(0,0));
        var result=OpenPathClosure.Close(source,0);
        Assert.Equal(source.Points,result.Piece.Points); Assert.Equal(0,result.ClosingLength);
    }
    [Fact]
    public void RejectsGapCrossingAndDegenerateContour()
    {
        var square=Open(new(0,0),new(10,0),new(10,10),new(0,10));
        Assert.Throws<ArgumentException>(()=>OpenPathClosure.Close(square,9.9));
        Assert.Throws<ArgumentException>(()=>OpenPathClosure.Close(Open(new(0,0),new(10,10),new(0,10),new(10,0)),20));
        Assert.Throws<ArgumentException>(()=>OpenPathClosure.Close(Open(new(0,0),new(1,0),new(2,0)),2));
    }
    [Fact]
    public void RejectsUnresolvedInformationInvalidLimitsAndCancellation()
    {
        var p=Open(new(0,0),new(1,0),new(0,1));
        p.Metadata=new PieceMetadata { Size="M" }; Assert.Throws<ArgumentException>(()=>OpenPathClosure.Close(p,2));
        p.Metadata=null; p.Grainline=new Grainline(); Assert.Throws<ArgumentException>(()=>OpenPathClosure.Close(p,2));
        p.Grainline=null; Assert.Throws<ArgumentOutOfRangeException>(()=>OpenPathClosure.Close(p,double.NaN));
        using var c=new CancellationTokenSource(); c.Cancel(); Assert.ThrowsAny<OperationCanceledException>(()=>OpenPathClosure.Close(p,2,c.Token));
        p.Points=Enumerable.Repeat(new Point2(0,0),10001).ToList(); Assert.Throws<ArgumentException>(()=>OpenPathClosure.Close(p,2));
    }
}
