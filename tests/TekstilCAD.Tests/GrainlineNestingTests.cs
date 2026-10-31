using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class GrainlineNestingTests
{
    private static PatternPiece Piece(double x=100, double y=200, bool review=false) => new()
    {
        Name="Ön",
        Points=[new(x,y),new(x+20,y),new(x+20,y+10),new(x,y+10)],
        Grainline=new Grainline { Start=new(x+3,y+2),End=new(x+3,y+8),NeedsReview=review }
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShelfTranslationMovesBothEndsAndPreservesSource(bool review)
    {
        var source=Piece(review:review);var before=source.Clone();
        var layout=NestingEngine.Arrange([source],100,0,edgeMargin:5).Layout;
        var placed=Assert.Single(layout.Pieces);
        Assert.Equal(new Point2(5,5),placed.Points[0]);
        Assert.NotNull(placed.Grainline);
        Assert.Equal(new Point2(8,7),placed.Grainline!.Start);
        Assert.Equal(new Point2(8,13),placed.Grainline.End);
        Assert.Equal(review,placed.Grainline.NeedsReview);
        Assert.NotSame(source.Grainline,placed.Grainline);
        Assert.Equal(before.Points,source.Points);
        Assert.Equal(before.Grainline!.Start,source.Grainline!.Start);
        Assert.Equal(before.Grainline.End,source.Grainline.End);
        Assert.Equal(review,source.Grainline.NeedsReview);
    }

    [Fact]
    public void ProductionCopiesRetainOwnTranslatedLinesWithoutMutatingSource()
    {
        var source=Piece(review:true);
        source.Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=2 };
        var document=new ProjectDocument { Pieces=[source] };
        var result=ProductionNestingPlan.Arrange(document,[source.Id],100,5);
        Assert.Equal(2,result.Nesting.Layout.Pieces.Count);
        foreach(var placed in result.Nesting.Layout.Pieces)
        {
            Assert.NotEqual(source.Id,placed.Id);
            Assert.NotNull(placed.Grainline);
            Assert.Equal(new Point2(3,2),placed.Grainline!.Start-placed.Points[0]);
            Assert.Equal(new Point2(3,8),placed.Grainline.End-placed.Points[0]);
            Assert.True(placed.Grainline.NeedsReview);
        }
        Assert.Equal(new Point2(103,202),source.Grainline!.Start);
        Assert.Equal(new Point2(103,208),source.Grainline.End);
        Assert.Equal(2,source.Metadata.Quantity);
    }

    [Theory]
    [InlineData(double.NaN,0,1,1)]
    [InlineData(0,0,0,0)]
    [InlineData(100000001,0,1,1)]
    public void InvalidGrainlineIsRejectedWithSourceIdentity(double sx,double sy,double ex,double ey)
    {
        var piece=Piece();piece.Grainline=new Grainline { Start=new(sx,sy),End=new(ex,ey) };
        var before=piece.Points.ToArray();
        var error=Assert.Throws<ArgumentException>(()=>NestingEngine.Arrange([piece],100));
        Assert.True(NestingPieceError.TryGet(error,out var id,out _));Assert.Equal(piece.Id,id);
        Assert.Equal(before,piece.Points);
    }

    [Fact]
    public void NarrowLineCannotCollapseDuringLargePlacement()
    {
        var wide=new PatternPiece { Points=[new(0,0),new(90_000_000,0),new(90_000_000,100),new(0,100)] };
        var piece=Piece(0,0);
        piece.Grainline=new Grainline { Start=new(1,2),End=new(1.000000001,2) };
        var before=piece.Clone();
        var error=Assert.Throws<ArgumentException>(()=>NestingEngine.Arrange([wide,piece],100_000_000,0));
        Assert.True(NestingPieceError.TryGet(error,out var id,out _));Assert.Equal(piece.Id,id);
        Assert.Equal(before.Points,piece.Points);
        Assert.Equal(before.Grainline!.End,piece.Grainline.End);
    }

    [Fact]
    public void CancelledPlacementDoesNotMutateSource()
    {
        var source=Piece(review:true);var before=source.Clone();
        using var cts=new CancellationTokenSource();cts.Cancel();
        Assert.Throws<OperationCanceledException>(()=>NestingEngine.Arrange([source],100,0,double.PositiveInfinity,0,cts.Token));
        Assert.Equal(before.Points,source.Points);
        Assert.Equal(before.Grainline!.Start,source.Grainline!.Start);
        Assert.Equal(before.Grainline.End,source.Grainline.End);
        Assert.True(source.Grainline.NeedsReview);
    }
}
