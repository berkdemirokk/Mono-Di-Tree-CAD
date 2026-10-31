using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class HpglPreparedPieceReviewTests
{
    private static PatternPiece Piece(int index)
    {
        double x=index*20;
        return new PatternPiece { Name=$"Parça {index}",Points=[new(x,0),new(x+10,0),new(x+10,10),
            new(x+6,10),new(x+5,9.99),new(x+4,10),new(x,10)] };
    }

    [Fact]
    public void PieceBeyondGlobalSampleBudgetCanBeReviewedFromCapturedSource()
    {
        var project=new ProjectDocument { Pieces=Enumerable.Range(0,101).Select(Piece).ToList() };
        var last=project.Pieces[^1];var id=last.Id;
        var prepared=HpglFormat.Prepare(project,40);
        var initial=prepared.TurnReviews.Single(r=>r.PieceId==id);
        Assert.True(initial.ChangedTurns>0);
        Assert.Empty(initial.Samples);
        Assert.True(initial.SamplesTruncated);
        var originalText=prepared.Text;
        last.Points[4]=new Point2(2005,8);
        last.Name="Değişti";
        var result=prepared.ReviewPiece(id,10);
        Assert.Equal(initial.ChangedTurns,result.ChangedTurns);
        Assert.Equal(initial.FlattenedTurns,result.FlattenedTurns);
        Assert.NotEmpty(result.Samples);
        Assert.Equal(originalText,prepared.Text);
    }

    [Fact]
    public void ReviewEnforcesSampleLimitAndSupportsCancellation()
    {
        var piece=Piece(0);var prepared=HpglFormat.Prepare(new ProjectDocument { Pieces=[piece] },40);
        var zero=prepared.ReviewPiece(piece.Id,0);
        Assert.True(zero.ChangedTurns>0);
        Assert.Empty(zero.Samples);
        Assert.True(zero.SamplesTruncated);
        Assert.Throws<ArgumentOutOfRangeException>(()=>prepared.ReviewPiece(piece.Id,101));
        Assert.Throws<KeyNotFoundException>(()=>prepared.ReviewPiece(Guid.NewGuid()));
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>prepared.ReviewPiece(piece.Id,20,cancellation.Token));
    }

    [Fact]
    public async Task ConcurrentReviewsUseTheSamePrivateSnapshot()
    {
        var piece=Piece(0);var prepared=HpglFormat.Prepare(new ProjectDocument { Pieces=[piece] },40);
        var results=await Task.WhenAll(Enumerable.Range(0,4).Select(_=>Task.Run(()=>prepared.ReviewPiece(piece.Id,20))));
        Assert.All(results,r=>Assert.Equal(results[0].ChangedTurns,r.ChangedTurns));
        Assert.All(results,r=>Assert.Equal(results[0].Samples,r.Samples));
    }
}
