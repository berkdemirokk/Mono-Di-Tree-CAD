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
        var touching=Prepare(Line(0,0,1,0),Line(1.01,0,2,0)).Interactions!;
        Assert.Equal(HpglPieceInteraction.State.Found,touching.Status);
    }

    [Fact]
    public void ContainmentAndDisjointPathsAreDistinguished()
    {
        Assert.Equal(HpglPieceInteraction.State.Found,Prepare(Box(0,0,20,20),Box(3,3,5,5)).Interactions!.Status);
        Assert.Equal(HpglPieceInteraction.State.Found,Prepare(Line(3,3,5,5),Box(0,0,20,20)).Interactions!.Status);
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
