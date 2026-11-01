using TekstilCAD.Core;
using TekstilCAD.Patterns;
namespace TekstilCAD.Tests;
public class ContourPathExtractionTests
{
    private static PatternPiece Source(bool repeated) {
        var p=new PatternPiece { Points=[new(0,0),new(10,0),new(10,10),new(0,10)],
            Metadata=new PieceMetadata { Quantity=2 },Grainline=new Grainline { Start=new(2,2),End=new(2,8) },
            Notches=[new NotchMark { VertexIndex=0,Position=new(0,0) },new NotchMark { VertexIndex=2,Position=new(10,10) }] };
        if(repeated)p.Points.Add(p.Points[0]);return p;
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void WrapPathPreservesOnlyIncludedMarksAndDoesNotMutateSource(bool repeated)
    {
        var source=Source(repeated);var before=source.Clone();var result=ContourPathExtraction.Extract(source,3,1);
        Assert.True(result.CrossesClosure);Assert.Equal(new[] { new Point2(0,10),new Point2(0,0),new Point2(10,0) },result.Piece.Points);
        Assert.Equal(1,result.Piece.Notches!.Single().VertexIndex);Assert.Equal(1,result.OmittedMarks);Assert.False(result.Piece.Closed);
        Assert.Null(result.Piece.Metadata);Assert.Null(result.Piece.Grainline);Assert.NotSame(source.Notches![0],result.Piece.Notches[0]);
        result.Piece.Notches[0].Position=new(99,99);Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[source] },new ProjectDocument { Pieces=[before] }));
    }
    [Fact]
    public void NormalizesClosureAliasAndRejectsAmbiguousOrBitDifferentClosure()
    {
        var source=Source(true);var result=ContourPathExtraction.Extract(source,4,2);
        Assert.Equal(0,result.StartIndex);Assert.Equal(2,result.EndIndex);Assert.False(result.CrossesClosure);
        Assert.Equal(new[] { 0,2 },result.Piece.Notches!.Select(mark=>mark.VertexIndex));
        Assert.Throws<ArgumentException>(()=>ContourPathExtraction.Extract(source,0,4));
        source.Points[^1]=new Point2(BitConverter.Int64BitsToDouble(long.MinValue),0);
        Assert.Throws<ArgumentException>(()=>ContourPathExtraction.Extract(source,0,2));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>ContourPathExtraction.Extract(source,0,2,cancelled.Token));
    }
    [Fact]
    public void RejectsInvalidContoursIndicesAndUnmatchedMarkers()
    {
        var source=Source(false);Assert.Throws<ArgumentOutOfRangeException>(()=>ContourPathExtraction.Extract(source,-1,2));
        source.Notches![0].Position=new Point2(1,0);Assert.Throws<ArgumentException>(()=>ContourPathExtraction.Extract(source,0,2));
        source=Source(false);source.Points=[new(0,0),new(10,10),new(0,10),new(10,0)];source.Notches=null;
        Assert.Throws<ArgumentException>(()=>ContourPathExtraction.Extract(source,0,2));
    }
}
