using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

/// <summary>Sampling limits affect examples only, never the reported counts.</summary>
public sealed class HpglReviewSampleBudgetTests
{
    private static PatternPiece Zigzag(int count)=>new()
    {
        Name="Yoğun dönüş",Closed=false,
        Points=Enumerable.Range(0,count).Select(i=>new Point2(i,i%2==0?0:.01)).ToList()
    };

    [Fact]
    public void LaterNotchCountsRemainVisibleAfterEarlierPiecesExhaustSampleBudget()
    {
        var pieces=Enumerable.Range(0,10).Select(_=>Zigzag(12)).ToList();
        var notch=new PatternPiece { Name="Son parça",Closed=true,
            Points=[new(0,0),new(10,0),new(10,10),new(6,10),new(5,9.99),new(4,10),new(0,10)] };
        pieces.Add(notch);
        var prepared=HpglFormat.Prepare(new ProjectDocument { Pieces=pieces },40);
        Assert.Equal(11,prepared.TurnReviews.Count);
        Assert.All(prepared.TurnReviews.Take(10),r=> { Assert.Equal(10,r.ChangedTurns);Assert.Equal(10,r.Samples.Count);Assert.False(r.SamplesTruncated); });
        Assert.Equal(100,prepared.TurnReviews.Sum(r=>r.Samples.Count));
        var last=prepared.TurnReviews[^1];
        Assert.Equal(notch.Id,last.PieceId);Assert.Equal(3,last.FlattenedTurns);Assert.Equal(3,last.ChangedTurns);
        Assert.Empty(last.Samples);Assert.True(last.SamplesTruncated);
        Assert.Equal(103,prepared.TurnReviews.Sum(r=>r.ChangedTurns));
    }

    [Fact]
    public void ZeroAndMaximumExampleLimitsPreserveFullCounts()
    {
        var piece=Zigzag(104);
        var zero=HpglTurnReview.Analyze(piece,40,0);
        var maximum=HpglTurnReview.Analyze(piece,40,HpglTurnReview.MaximumSamples);
        Assert.Equal(102,zero.ChangedTurns);Assert.Equal(zero.ChangedTurns,maximum.ChangedTurns);
        Assert.Empty(zero.Samples);Assert.True(zero.SamplesTruncated);
        Assert.Equal(100,maximum.Samples.Count);Assert.True(maximum.SamplesTruncated);
        Assert.Throws<ArgumentOutOfRangeException>(()=>HpglTurnReview.Analyze(piece,40,HpglTurnReview.MaximumSamples+1));
    }

    [Fact]
    public void ZeroSampleBudgetDoesNotClaimTruncationWhenNothingChanged()
    {
        var straight=new PatternPiece { Closed=false,Points=[new(0,0),new(1,0),new(2,0)] };
        var report=HpglTurnReview.Analyze(straight,40,0);
        Assert.Equal(0,report.ChangedTurns);Assert.Empty(report.Samples);Assert.False(report.SamplesTruncated);
    }
}
