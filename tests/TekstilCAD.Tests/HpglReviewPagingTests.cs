using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class HpglReviewPagingTests
{
    private static PatternPiece Zigzag(bool closed=false)
    {
        var p=new PatternPiece { Name="Zikzak",Closed=closed,
            Points=Enumerable.Range(0,29).Select(i=>new Point2(i,i%2==0 ? 0 : .01)).ToList() };
        if(closed) { p.Points.Add(new Point2(28,5));p.Points.Add(new Point2(0,5));p.Points.Add(p.Points[0]); }
        return p;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PagesMatchFullSnapshotReviewInSourceOrder(bool closed)
    {
        var piece=Zigzag(closed);
        var prepared=HpglFormat.Prepare(new ProjectDocument { Pieces=[piece] },40);
        var expected=prepared.ReviewPiece(piece.Id,100);
        Assert.True(expected.ChangedTurns>10);
        var collected=new List<HpglTurnReview.Sample>();int offset=0;int pages=0;
        do
        {
            var page=prepared.ReviewPiecePage(piece.Id,offset,5);
            Assert.Equal(offset,page.Offset);
            Assert.Equal(expected.ChangedTurns,page.Report.ChangedTurns);
            Assert.InRange(page.Report.Samples.Count,1,5);
            collected.AddRange(page.Report.Samples);pages++;
            if(page.NextOffset is not { } next)break;
            Assert.True(next>offset);offset=next;
        } while(pages<100);
        Assert.Equal(expected.ChangedTurns,collected.Count);
        Assert.Equal(expected.Samples,collected);
        Assert.Equal(collected.OrderBy(s=>s.SourceVertexIndex),collected);
    }

    [Fact]
    public void EndOutOfRangeAndBadArgumentsAreBounded()
    {
        var p=Zigzag();var prepared=HpglFormat.Prepare(new ProjectDocument { Pieces=[p] },40);
        var count=prepared.ReviewPiece(p.Id,0).ChangedTurns;
        var atEnd=prepared.ReviewPiecePage(p.Id,count,10);
        Assert.Empty(atEnd.Report.Samples);Assert.Null(atEnd.NextOffset);
        var huge=prepared.ReviewPiecePage(p.Id,int.MaxValue,10);
        Assert.Empty(huge.Report.Samples);Assert.Null(huge.NextOffset);
        Assert.Throws<ArgumentOutOfRangeException>(()=>prepared.ReviewPiecePage(p.Id,-1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>prepared.ReviewPiecePage(p.Id,0,0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>prepared.ReviewPiecePage(p.Id,0,101));
        Assert.Throws<KeyNotFoundException>(()=>prepared.ReviewPiecePage(Guid.NewGuid()));
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>prepared.ReviewPiecePage(p.Id,0,10,cancellation.Token));
    }

    [Fact]
    public void LaterPagesUseFrozenCoordinatesAndUnits()
    {
        var p=Zigzag();var prepared=HpglFormat.Prepare(new ProjectDocument { Pieces=[p] },40);
        var before=prepared.ReviewPiecePage(p.Id,5,5);
        p.Points.Clear();p.Name="Değişti";
        var after=prepared.ReviewPiecePage(p.Id,5,5);
        Assert.Equal(before.Report.Samples,after.Report.Samples);
        Assert.Equal(before.Report.ChangedTurns,after.Report.ChangedTurns);
        Assert.Equal(40,after.Report.UnitsPerMillimeter);
        Assert.Equal(before.NextOffset,after.NextOffset);
    }
}
