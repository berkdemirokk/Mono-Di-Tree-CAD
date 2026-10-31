using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class GrainlineOffsetTests
{
    private static PatternPiece Shape(bool concave = false, bool needsReview = false) => new()
    {
        Name="Ön", Closed=true,
        Points=concave ? [new(0,0),new(100,0),new(100,100),new(60,100),new(60,40),new(40,40),new(40,100),new(0,100)]
                       : [new(0,0),new(100,0),new(100,100),new(0,100)],
        Grainline=new Grainline { Start=new(10,10),End=new(10,30),NeedsReview=needsReview },
        Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=2 }
    };

    [Theory]
    [InlineData(false,false)] [InlineData(false,true)] [InlineData(true,false)] [InlineData(true,true)]
    public void OffsetKeepsGrainlineIndependentlyAndAlwaysRequiresReview(bool concave,bool priorReview)
    {
        var source=Shape(concave,priorReview);var before=source.Clone();
        var result=PatternTools.Offset(source,1);
        Assert.NotEqual(source.Id,result.Id);Assert.NotSame(source.Points,result.Points);
        Assert.NotEqual(source.Name,result.Name);Assert.Null(result.Metadata);
        Assert.NotNull(result.Grainline);Assert.NotSame(source.Grainline,result.Grainline);
        Assert.Equal(before.Grainline!.Start,result.Grainline!.Start);
        Assert.Equal(before.Grainline.End,result.Grainline.End);Assert.True(result.Grainline.NeedsReview);
        result.Grainline.Start=new(20,20);result.Points[0]=new(200,200);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void ConvexOffsetRetainsLineWithReviewRegardlessOfAmount(double amount)
    {
        var source=Shape(needsReview:true);var before=source.Clone();
        var result=PatternTools.OffsetConvex(source,amount);
        Assert.NotEqual(source.Id,result.Id);Assert.NotSame(source.Points,result.Points);
        Assert.NotSame(source.Grainline,result.Grainline);Assert.NotNull(result.Grainline);
        Assert.Equal(before.Grainline!.Start,result.Grainline!.Start);Assert.Equal(before.Grainline.End,result.Grainline.End);
        Assert.True(result.Grainline.NeedsReview);Assert.Null(result.Metadata);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
    }

    [Fact]
    public void InvalidGrainlineIsRejectedBeforeAnyResultForBothOffsetAlgorithms()
    {
        foreach(var invalid in new[] {
            new Grainline { Start=new(double.NaN,0),End=new(1,1) },
            new Grainline { Start=new(0,0),End=new(100_000_001,0) },
            new Grainline { Start=new(2,3),End=new(2,3) } })
        {
            var source=Shape();source.Grainline=invalid;var before=source.Clone();
            Assert.ThrowsAny<ArgumentException>(()=>PatternTools.Offset(source,1));
            Assert.ThrowsAny<ArgumentException>(()=>PatternTools.OffsetConvex(source,1));
            Assert.Equal(before.Points,source.Points);Assert.Equal(before.Grainline!.Start,source.Grainline.Start);
            Assert.Equal(before.Grainline.End,source.Grainline.End);Assert.Equal(before.Grainline.NeedsReview,source.Grainline.NeedsReview);
        }
    }

    [Fact]
    public void FailedOrCancelledOffsetDoesNotAlterOriginalOrItsGrainline()
    {
        var source=Shape();var before=source.Clone();
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>PatternTools.Offset(source,1,cancelled.Token));
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.Offset(source,-1));
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
    }

    [Fact]
    public void GrainlineFreeSourceStillProducesGrainlineFreeOffset()
    {
        var source=Shape();source.Grainline=null;var before=source.Clone();
        Assert.Null(PatternTools.Offset(source,1).Grainline);
        Assert.Null(PatternTools.OffsetConvex(source,1).Grainline);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
    }
}
