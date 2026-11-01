using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class OpenPathMarkedJoinTests
{
    [Theory]
    [InlineData(false,false,false)]
    [InlineData(false,true,true)]
    [InlineData(true,false,true)]
    [InlineData(true,true,false)]
    public void JoinMapsMarksForBothDirectionsAndBridgeModes(bool reverseA,bool reverseB,bool shared)
    {
        var a=new List<Point2> { new(0,0),new(10,0) };
        var b=new List<Point2> { new(shared?10:11,0),new(20,5) };
        if(reverseA)a.Reverse();if(reverseB)b.Reverse();
        var first=new PatternPiece { Closed=false,Points=a,Notches=[new NotchMark { VertexIndex=reverseA?1:0,Position=new(0,0) }] };
        var second=new PatternPiece { Closed=false,Points=b,Notches=[new NotchMark { VertexIndex=reverseB?0:1,Position=new(20,5) }] };
        var original=new ProjectDocument { Pieces=[first.Clone(),second.Clone()] };
        var result=OpenPathJoin.Join(first,second,shared?0:1).Piece;
        Assert.Equal(new[] { 0,result.Points.Count-1 },result.Notches!.Select(mark=>mark.VertexIndex));
        Assert.All(result.Notches!,mark=>Assert.Equal(result.Points[mark.VertexIndex],mark.Position));
        Assert.NotSame(first.Notches![0],result.Notches![0]);Assert.NotSame(second.Notches![0],result.Notches![1]);
        Assert.True(DocumentSession.ContentEquals(original,new ProjectDocument { Pieces=[first,second] }));
    }

    [Fact]
    public void SharedEndpointCannotCollapseTwoMarksOrChangeMarkerCoordinateBits()
    {
        var minusZero=BitConverter.Int64BitsToDouble(long.MinValue);
        var first=new PatternPiece { Closed=false,Points=[new(0,minusZero),new(10,minusZero)],Notches=[new NotchMark { VertexIndex=1,Position=new(10,minusZero) }] };
        var second=new PatternPiece { Closed=false,Points=[new(10,minusZero),new(20,5)],Notches=[new NotchMark { VertexIndex=0,Position=new(10,minusZero) }] };
        var before=new ProjectDocument { Pieces=[first.Clone(),second.Clone()] };
        Assert.Throws<ArgumentException>(()=>OpenPathJoin.Join(first,second,0));
        Assert.True(DocumentSession.ContentEquals(before,new ProjectDocument { Pieces=[first,second] }));
        first.Notches=null;second.Points[0]=new(10,0);second.Notches![0].Position=second.Points[0];
        Assert.Throws<ArgumentException>(()=>OpenPathJoin.Join(first,second,0));
    }
}
