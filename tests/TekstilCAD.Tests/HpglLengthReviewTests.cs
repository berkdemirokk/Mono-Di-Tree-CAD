using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class HpglLengthReviewTests
{
    [Fact]
    public void CollinearEdgesCanChangeLengthWithoutChangingTurnClass()
    {
        var piece=new PatternPiece { Closed=false,Points=[new(.49,0),new(10.49,0),new(20.51,0)] };
        var output=HpglFormat.Prepare(new ProjectDocument { Pieces=[piece] },1);
        Assert.Equal(0,Assert.Single(output.TurnReviews).ChangedTurns);
        var result=HpglLengthReview.Analyze(output.GetPieceGeometry(piece.Id));
        Assert.Equal(2,result.EdgeCount);Assert.Equal(20.02,result.SourceLengthMm,8);Assert.Equal(21,result.EncodedLengthMm,8);
        Assert.Equal(1,result.WorstStartIndex);Assert.Equal(2,result.WorstEndIndex);Assert.Equal(.98,result.WorstSignedDifferenceMm,8);
    }
    [Fact]
    public void ClosedPathIncludesClosingEdgeAndSkipsRepeatedClosure()
    {
        var piece=new PatternPiece { Closed=true,Points=[new(0,0),new(10,0),new(10,10),new(0,0)] };
        var result=HpglLengthReview.Analyze(HpglFormat.Prepare(new ProjectDocument { Pieces=[piece] },40).GetPieceGeometry(piece.Id));
        Assert.Equal(3,result.EdgeCount);Assert.Equal(20+Math.Sqrt(200),result.SourceLengthMm,10);Assert.Equal(result.SourceLengthMm,result.EncodedLengthMm,10);
    }
    [Fact]
    public void CapturedOutputDoesNotChangeWhenSourceIsEdited()
    {
        var piece=new PatternPiece { Closed=false,Points=[new(0,0),new(10,0)] };
        var geometry=HpglFormat.Prepare(new ProjectDocument { Pieces=[piece] },40).GetPieceGeometry(piece.Id);
        piece.Points[1]=new(20,0);Assert.Equal(10,HpglLengthReview.Analyze(geometry).SourceLengthMm,10);
        using var cts=new CancellationTokenSource();cts.Cancel();Assert.Throws<OperationCanceledException>(()=>HpglLengthReview.Analyze(geometry,cts.Token));
    }
    [Fact]
    public void WorstClosingEdgeUsesLastAndFirstVertex()
    {
        var piece=new PatternPiece { Closed=true,Points=[new(.49,.49),new(10,0),new(10,10)] };
        var result=HpglLengthReview.Analyze(HpglFormat.Prepare(new ProjectDocument { Pieces=[piece] },1).GetPieceGeometry(piece.Id));
        Assert.Equal(2,result.WorstStartIndex);Assert.Equal(0,result.WorstEndIndex);Assert.True(result.WorstSignedDifferenceMm>0);
    }
    [Fact]
    public void EncodedDifferenceWidensBeforeSubtractingLargeCoordinates()
    {
        var piece=new PatternPiece { Closed=false,Points=[new(-50_000_000,0),new(50_000_000,0)] };
        var result=HpglLengthReview.Analyze(HpglFormat.Prepare(new ProjectDocument { Pieces=[piece] },40).GetPieceGeometry(piece.Id));
        Assert.Equal(100_000_000,result.SourceLengthMm,5);Assert.Equal(100_000_000,result.EncodedLengthMm,5);
    }
}
