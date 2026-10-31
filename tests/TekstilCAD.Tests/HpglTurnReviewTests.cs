using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class HpglTurnReviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FlattenedNotchIsReportedInBothDirections(bool reverse)
    {
        var points=new List<Point2> { new(0,0),new(10,0),new(10,10),new(6,10),new(5,9.99),new(4,10),new(0,10) };
        if(reverse)points.Reverse();
        var before=points.ToArray();var piece=new PatternPiece { Points=points };
        var report=HpglTurnReview.Analyze(piece,40);
        Assert.Equal(piece.Id,report.PieceId);Assert.True(report.Closed);
        Assert.True(report.FlattenedTurns>0);
        Assert.Contains(report.Samples,s=>s.SourceVertexIndex==(reverse ? 2 : 4) && s.SourceSign!=0 && s.PlotSign==0);
        Assert.Equal(before,points);
    }

    [Fact]
    public void OpenLineIncludesOnlyInteriorTurnsAndIdentifiesReversal()
    {
        var piece=new PatternPiece { Closed=false,Points=[new(.49,.49),new(1.49,.51),new(1.51,1.49)] };
        var report=HpglTurnReview.Analyze(piece,1);
        Assert.Equal(1,report.ExaminedTurns);Assert.Equal(1,report.ReversedTurns);
        Assert.Equal(new HpglTurnReview.Sample(1,1,-1),Assert.Single(report.Samples));
    }

    [Fact]
    public void ExistingDuplicatesAndExplicitClosureAreNotExtraTurns()
    {
        var piece=new PatternPiece { Closed=true,Points=[new(0,0),new(0,0),new(10,0),new(10,10),new(0,10),new(0,0)] };
        var report=HpglTurnReview.Analyze(piece,40);
        Assert.Equal(4,report.ExaminedTurns);Assert.Equal(0,report.ChangedTurns);
    }

    [Fact]
    public void SamplesAreBoundedWithoutLosingTotalCounts()
    {
        var piece=new PatternPiece { Closed=false,Points=[new(.375,.75),new(.875,1.75),new(1.375,2.75),new(1.875,3.75)] };
        var full=HpglTurnReview.Analyze(piece,1,100);var limited=HpglTurnReview.Analyze(piece,1,0);
        Assert.True(full.ChangedTurns>0);Assert.Equal(full.ChangedTurns,limited.ChangedTurns);
        Assert.Empty(limited.Samples);Assert.True(limited.SamplesTruncated);
        Assert.Equal(full.ChangedTurns,full.FlattenedTurns+full.IntroducedTurns+full.ReversedTurns);
    }

    [Fact]
    public void BoundsAndPreCancellationAreExplicit()
    {
        var piece=new PatternPiece { Closed=false,Points=[new(0,0),new(1,1)] };
        Assert.Throws<ArgumentOutOfRangeException>(()=>HpglTurnReview.Analyze(piece,1,101));
        Assert.Throws<ArgumentOutOfRangeException>(()=>HpglTurnReview.Analyze(piece,0));
        var huge=new PatternPiece { Closed=false,Points=Enumerable.Repeat(new Point2(0,0),ProjectStorage.MaximumPointsPerPiece+1).ToList() };
        Assert.Throws<ArgumentException>(()=>HpglTurnReview.Analyze(huge,1));
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        Assert.Throws<OperationCanceledException>(()=>HpglTurnReview.Analyze(piece,1,cancellationToken:cancel.Token));
    }
}
