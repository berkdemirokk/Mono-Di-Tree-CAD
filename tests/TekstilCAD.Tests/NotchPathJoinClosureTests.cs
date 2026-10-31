using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;
namespace TekstilCAD.Tests;
public class NotchPathJoinClosureTests
{
    private static PatternPiece Open(params Point2[] points)=>new() { Closed=false,Points=[..points] };
    private static void Mark(PatternPiece piece,int index)=>piece.Notches=[new NotchMark { VertexIndex=index,Position=piece.Points[index] }];
    [Fact]
    public void ReversedSourcesAndBridgeRetainBothMarkersIndependently()
    {
        var a=Open(new(1,0),new(0,0));var b=Open(new(2,0),new(1.1,0));Mark(a,0);Mark(b,1);
        var originalA=a.Clone();var originalB=b.Clone();var joined=OpenPathJoin.Join(a,b,.11);
        Assert.Equal(new[] { 1,2 },joined.Piece.Notches!.Select(n=>n.VertexIndex));
        Assert.Equal(new[] { new Point2(1,0),new Point2(1.1,0) },joined.Piece.Notches.Select(n=>n.Position));
        Assert.Equal(a.Id,joined.FirstSourceId);Assert.Equal(b.Id,joined.SecondSourceId);
        Assert.NotSame(a.Notches![0],joined.Piece.Notches[0]);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[a,b] },new ProjectDocument { Pieces=[originalA,originalB] }));
        ProjectStorage.Validate(new ProjectDocument { Pieces=[joined.Piece] });
    }
    [Fact]
    public void CommonEndpointAllowsSingleMarkButRejectsTwoMarks()
    {
        var a=Open(new(0,0),new(1,0));var b=Open(new(1,0),new(2,0));Mark(a,1);
        var result=OpenPathJoin.Join(a,b,0);Assert.Equal(1,result.Piece.Notches!.Single().VertexIndex);
        Mark(b,0);Assert.Throws<ArgumentException>(()=>OpenPathJoin.Join(a,b,0));
        Assert.Single(a.Notches!);Assert.Single(b.Notches!);
    }
    [Fact]
    public void ClosureCanonicalizesLastMarkOnRepeatedEndpointAndRejectsCollision()
    {
        var p=Open(new(0,0),new(10,0),new(10,10),new(0,0));Mark(p,3);
        var closed=OpenPathClosure.Close(p,0);Assert.Equal(0,closed.Piece.Notches!.Single().VertexIndex);
        Assert.Equal(new Point2(0,0),closed.Piece.Notches[0].Position);
        ProjectStorage.Validate(new ProjectDocument { Pieces=[closed.Piece] });
        p.Notches!.Add(new NotchMark { VertexIndex=0,Position=new(0,0) });
        Assert.Throws<ArgumentException>(()=>OpenPathClosure.Close(p,0));
    }
    [Fact]
    public void DistinctClosingEndpointsRetainDistinctMarksAndSourceMetadataStillBlocks()
    {
        var p=Open(new(0,0),new(10,0),new(10,10),new(0,10));Mark(p,0);
        p.Notches!.Add(new NotchMark { VertexIndex=3,Position=new(0,10) });
        var result=OpenPathClosure.Close(p,10);Assert.Equal(new[] { 0,3 },result.Piece.Notches!.Select(n=>n.VertexIndex));
        p.Metadata=new PieceMetadata { Size="M" };Assert.Throws<ArgumentException>(()=>OpenPathClosure.Close(p,10));
        Assert.Equal("M",p.Metadata.Size);Assert.Equal(2,p.Notches.Count);
    }
    [Fact]
    public void StaleOrDuplicateSourceMarkersAreRejected()
    {
        var p=Open(new(0,0),new(10,0),new(10,10));Mark(p,1);p.Notches![0].Position=new(9,0);
        Assert.Throws<ArgumentException>(()=>OpenPathClosure.Close(p,20));
        p.Notches[0].Position=p.Points[1];p.Notches.Add(p.Notches[0].Clone());
        Assert.Throws<ArgumentException>(()=>OpenPathClosure.Close(p,20));
    }
}
