using TekstilCAD.Core;
using TekstilCAD.Patterns;
namespace TekstilCAD.Tests;
public class OpenPathExplicitJoinTests
{
    [Theory]
    [InlineData(false,false,false)] [InlineData(false,false,true)] [InlineData(false,true,false)] [InlineData(false,true,true)]
    [InlineData(true,false,false)] [InlineData(true,false,true)] [InlineData(true,true,false)] [InlineData(true,true,true)]
    public void SelectedEndpointsPreserveOriginalDirectionsMarksAndBridge(bool reverseFirst,bool reverseSecond,bool shared)
    {
        var a=new List<Point2> { new(0,0),new(10,0) };var b=new List<Point2> { new(shared ? 10 : 11,0),new(20,5) };
        if(reverseFirst)a.Reverse();if(reverseSecond)b.Reverse();
        var first=new PatternPiece { Closed=false,Points=a,Notches=[new NotchMark { VertexIndex=reverseFirst ? 1 : 0,Position=new(0,0) }] };
        var second=new PatternPiece { Closed=false,Points=b,Notches=[new NotchMark { VertexIndex=reverseSecond ? 0 : 1,Position=new(20,5) }] };
        var before=new ProjectDocument { Pieces=[first.Clone(),second.Clone()] };
        var joined=OpenPathJoin.JoinSelected(first,second,reverseFirst ? 0 : 1,reverseSecond ? 1 : 0,shared ? 0 : 1);
        Assert.Equal(reverseFirst ? 0 : 1,joined.FirstEndpointIndex);Assert.Equal(reverseSecond ? 1 : 0,joined.SecondEndpointIndex);
        Assert.Equal(new[] { 0,joined.Piece.Points.Count-1 },joined.Piece.Notches!.Select(m=>m.VertexIndex));
        Assert.All(joined.Piece.Notches!,m=>Assert.Equal(joined.Piece.Points[m.VertexIndex],m.Position));
        Assert.True(DocumentSession.ContentEquals(before,new ProjectDocument { Pieces=[first,second] }));
    }
    [Fact]
    public void ExplicitChoiceResolvesSeveralCandidatesWithoutGuessing()
    {
        var a=new PatternPiece { Closed=false,Points=[new(0,0),new(10,0)] };
        var b=new PatternPiece { Closed=false,Points=[new(0,1),new(10,1)] };
        Assert.Throws<ArgumentException>(()=>OpenPathJoin.Join(a,b,11));
        var result=OpenPathJoin.JoinSelected(a,b,1,0,11);
        Assert.Equal(new[] { new Point2(0,0),new Point2(10,0),new Point2(0,1),new Point2(10,1) },result.Piece.Points);
        Assert.Throws<ArgumentException>(()=>OpenPathJoin.JoinSelected(a,b,1,0,0));
        Assert.Throws<ArgumentException>(()=>OpenPathJoin.JoinSelected(a,a,1,0,11));
    }
    [Fact]
    public void SharedBitMismatchAndTwoMarkersAreRejected()
    {
        var z=BitConverter.Int64BitsToDouble(long.MinValue);
        var a=new PatternPiece { Closed=false,Points=[new(0,0),new(10,z)] };
        var b=new PatternPiece { Closed=false,Points=[new(10,0),new(20,5)] };
        Assert.Throws<ArgumentException>(()=>OpenPathJoin.JoinSelected(a,b,1,0,0));
        b.Points[0]=a.Points[1];a.Notches=[new NotchMark { VertexIndex=1,Position=a.Points[1] }];b.Notches=[new NotchMark { VertexIndex=0,Position=b.Points[0] }];
        Assert.Throws<ArgumentException>(()=>OpenPathJoin.JoinSelected(a,b,1,0,0));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>OpenPathJoin.JoinSelected(a,b,1,0,0,cancelled.Token));
    }
    [Fact]
    public void RejectsCombinedPathBeyondStoredPointLimit()
    {
        var a=new PatternPiece { Closed=false,Points=Enumerable.Range(0,5001).Select(i=>new Point2(i,0)).ToList() };
        var b=new PatternPiece { Closed=false,Points=Enumerable.Range(5000,5001).Select(i=>new Point2(i,0)).ToList() };
        Assert.Throws<ArgumentException>(()=>OpenPathJoin.JoinSelected(a,b,a.Points.Count-1,0,0));
        Assert.Equal(5001,a.Points.Count);Assert.Equal(5001,b.Points.Count);
    }
}
