using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class HpglPieceInteractionTests
{
    private static PatternPiece Line(double x1,double y1,double x2,double y2)=>new() { Name="çizgi",Closed=false,Points=[new(x1,y1),new(x2,y2)] };
    private static PatternPiece Box(double left,double top,double right,double bottom)=>new() { Name="kutu",Closed=true,Points=[new(left,top),new(right,top),new(right,bottom),new(left,bottom)] };
    private static HpglPreparedOutput Prepare(params PatternPiece[] pieces)=>HpglFormat.Prepare(new ProjectDocument { Pieces=pieces.ToList() },40);

    [Fact]
    public void CrossingAndQuantizedTouchIdentifyCapturedPieces()
    {
        var a=Line(0,0,10,0);var b=Line(5,-2,5,2);
        var result=Prepare(a,b).Interactions!;
        Assert.Equal(HpglPieceInteraction.State.Found,result.Status);
        Assert.Equal(a.Id,result.FirstPiece);Assert.Equal(b.Id,result.SecondPiece);
        Assert.Equal(HpglPieceInteraction.Finding.Boundary,result.Kind);Assert.Equal(0,result.FirstEdge);Assert.Equal(0,result.SecondEdge);
        Assert.True(result.SourceEdgesTouch);
        var touching=Prepare(Line(0,0,1,0),Line(1.01,0,2,0)).Interactions!;
        Assert.Equal(HpglPieceInteraction.State.Found,touching.Status);Assert.False(touching.SourceEdgesTouch);
    }

    [Fact]
    public void ReportedOutputEdgePairIsComparedExactlyAgainstItsSourceEdges()
    {
        var separated=Prepare(Line(0,0,10,0),Line(0,0.01,10,0.01)).Interactions!;
        Assert.Equal(HpglPieceInteraction.Finding.Boundary,separated.Kind);Assert.False(separated.SourceEdgesTouch);
        var touching=Prepare(Line(0,0,10,0),Line(10,0,10,5)).Interactions!;
        Assert.True(touching.SourceEdgesTouch);
        var overlap=Prepare(Line(0,0,10,0),Line(5,0,15,0)).Interactions!;
        Assert.True(overlap.SourceEdgesTouch);
        var tinyGap=Prepare(Line(0,0,10,0),Line(0,double.Epsilon,10,double.Epsilon)).Interactions!;
        Assert.False(tinyGap.SourceEdgesTouch);
        var containment=Prepare(Box(0,0,20,20),Box(3,3,5,5)).Interactions!;
        Assert.Null(containment.SourceEdgesTouch);
    }

    [Fact]
    public void ContainmentAndDisjointPathsAreDistinguished()
    {
        Assert.Equal(HpglPieceInteraction.Finding.SecondInsideFirst,Prepare(Box(0,0,20,20),Box(3,3,5,5)).Interactions!.Kind);
        Assert.Equal(HpglPieceInteraction.Finding.FirstInsideSecond,Prepare(Line(3,3,5,5),Box(0,0,20,20)).Interactions!.Kind);
        Assert.Equal(HpglPieceInteraction.State.Clear,Prepare(Box(0,0,2,2),Box(3,3,5,5)).Interactions!.Status);
        Assert.Equal(HpglPieceInteraction.State.Clear,Prepare(Line(0,0,10,10),Line(0,5,2,5)).Interactions!.Status);
    }

    [Fact]
    public void BudgetExhaustionIsNotReportedAsClearAndCancellationIsObserved()
    {
        var a=Line(0,0,10,0);var b=Line(5,-2,5,2);var output=Prepare(a,b);
        var pieces=new[] { output.GetPieceGeometry(a.Id),output.GetPieceGeometry(b.Id) };
        Assert.Equal(HpglPieceInteraction.State.Incomplete,HpglPieceInteraction.Analyze(pieces,1).Status);
        using var cts=new CancellationTokenSource();cts.Cancel();
        Assert.Throws<OperationCanceledException>(()=>HpglPieceInteraction.Analyze(pieces,cancellationToken:cts.Token));
    }
}
