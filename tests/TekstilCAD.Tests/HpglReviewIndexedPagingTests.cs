using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class HpglReviewIndexedPagingTests
{
    private static PatternPiece Zigzag(bool closed)
    {
        var piece=new PatternPiece { Name="Kaynak",Closed=closed,
            Points=Enumerable.Range(0,180).Select(i=>new Point2(i,i%2==0?0:.01)).ToList() };
        if(closed) { piece.Points.Add(new(179,5));piece.Points.Add(new(0,5));piece.Points.Add(piece.Points[0]); }
        return piece;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryIndexedPageMatchesIndependentFullRescan(bool closed)
    {
        var piece=Zigzag(closed);var prepared=HpglFormat.Prepare(new ProjectDocument { Pieces=[piece] },40);
        for(var offset=0;offset<220;offset+=7)
        {
            var direct=HpglTurnReview.AnalyzePage(piece,40,offset,7);
            var indexed=prepared.ReviewPiecePage(piece.Id,offset,7);
            Assert.Equal(direct.Offset,indexed.Offset);Assert.Equal(direct.NextOffset,indexed.NextOffset);
            Assert.Equal(direct.Report.ExaminedTurns,indexed.Report.ExaminedTurns);
            Assert.Equal(direct.Report.ChangedTurns,indexed.Report.ChangedTurns);
            Assert.Equal(direct.Report.FlattenedTurns,indexed.Report.FlattenedTurns);
            Assert.Equal(direct.Report.IntroducedTurns,indexed.Report.IntroducedTurns);
            Assert.Equal(direct.Report.ReversedTurns,indexed.Report.ReversedTurns);
            Assert.Equal(direct.Report.SamplesTruncated,indexed.Report.SamplesTruncated);
            Assert.Equal(direct.Report.Samples,indexed.Report.Samples);
        }
    }

    [Fact]
    public async Task PagesAreImmutableAfterSourceMutationAndSafeForConcurrentReaders()
    {
        var piece=Zigzag(false);var prepared=HpglFormat.Prepare(new ProjectDocument { Pieces=[piece] },40);
        var expected=prepared.ReviewPiecePage(piece.Id,20,25);
        var results=await Task.WhenAll(Enumerable.Range(0,6).Select(_=>Task.Run(()=>prepared.ReviewPiecePage(piece.Id,20,25))));
        piece.Points.Clear();
        Assert.All(results,r=>Assert.Equal(expected.Report.Samples,r.Report.Samples));
        Assert.Equal(expected.Report.Samples,prepared.ReviewPiecePage(piece.Id,20,25).Report.Samples);
        var list=Assert.IsAssignableFrom<IList<HpglTurnReview.Sample>>(expected.Report.Samples);
        Assert.Throws<NotSupportedException>(()=>list.Add(new HpglTurnReview.Sample(0,0,0)));
    }

    [Fact]
    public void CancellationAndOffsetBoundariesRemainIntact()
    {
        var piece=Zigzag(false);var prepared=HpglFormat.Prepare(new ProjectDocument { Pieces=[piece] },40);
        var past=prepared.ReviewPiecePage(piece.Id,int.MaxValue,100);
        Assert.Empty(past.Report.Samples);Assert.Null(past.NextOffset);
        Assert.Throws<ArgumentOutOfRangeException>(()=>prepared.ReviewPiecePage(piece.Id,-1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>prepared.ReviewPiecePage(piece.Id,0,101));
        using var source=new CancellationTokenSource();source.Cancel();
        Assert.Throws<OperationCanceledException>(()=>prepared.ReviewPiecePage(piece.Id,0,20,source.Token));
    }
}
