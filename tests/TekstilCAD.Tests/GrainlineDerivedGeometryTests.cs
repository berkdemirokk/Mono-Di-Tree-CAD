using TekstilCAD.Core;
using TekstilCAD.Grading;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class GrainlineDerivedGeometryTests
{
    private static PatternPiece Source() => new()
    {
        Name="Ön", Closed=true, Points=[new(0,0),new(20,0),new(20,20),new(0,20)],
        Grainline=new Grainline { Start=new(5,4),End=new(5,16),NeedsReview=false }
    };

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void OffsetCopiesGrainlineAndRequiresReview(double amount)
    {
        var source=Source();var before=source.Clone();
        var result=PatternTools.Offset(source,amount);
        Assert.NotEqual(source.Id,result.Id); Assert.Equal(source.Closed,result.Closed);
        Assert.NotSame(source.Points,result.Points); Assert.NotNull(result.Grainline);
        Assert.NotSame(source.Grainline,result.Grainline);
        Assert.Equal(source.Grainline!.Start,result.Grainline!.Start);
        Assert.Equal(source.Grainline.End,result.Grainline.End);
        Assert.True(result.Grainline.NeedsReview);
        result.Grainline.Start=new(9,9); result.Points[0]=new(123,123);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
    }

    [Fact]
    public void ConvexOffsetCopiesGrainlineAndRequiresReview()
    {
        var source=Source();var before=source.Clone();
        var result=PatternTools.OffsetConvex(source,1);
        Assert.NotEqual(source.Id,result.Id);Assert.NotNull(result.Grainline);Assert.NotSame(source.Grainline,result.Grainline);
        Assert.Equal(source.Grainline!.Start,result.Grainline!.Start);Assert.Equal(source.Grainline.End,result.Grainline.End);
        Assert.True(result.Grainline.NeedsReview);result.Grainline.End=new(7,7);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
    }

    [Theory]
    [InlineData(-1, false)] [InlineData(0, false)] [InlineData(1, true)]
    public void GradingCopiesGrainlineAndRequiresReview(double steps, bool priorReview)
    {
        var source=Source();source.Grainline!.NeedsReview=priorReview;var before=source.Clone();
        Point2[] increments=[new(-1,-1),new(1,-1),new(1,1),new(-1,1)];
        var result=GradingEngine.Generate(source,increments,steps,"L");
        Assert.NotEqual(source.Id,result.Id);Assert.NotSame(source.Points,result.Points);
        Assert.NotNull(result.Grainline);Assert.NotSame(source.Grainline,result.Grainline);
        Assert.Equal(before.Grainline!.Start,result.Grainline!.Start);
        Assert.Equal(before.Grainline.End,result.Grainline.End);Assert.True(result.Grainline.NeedsReview);
        result.Grainline.Start=new(9,9);result.Points[0]=new(123,123);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
    }

    [Fact]
    public void GradingBatchProducesIndependentGrainlinesAndAllowsEmptyBatch()
    {
        var source=Source();var before=source.Clone();var increments=source.Points.Select(_=>new Point2(0,0)).ToArray();
        var results=GradingEngine.GenerateSizes(source,increments,[new("S",-1),new("M",0),new("L",1)]);
        Assert.Equal(3,results.Count);
        foreach(var piece in results)
        {
            Assert.NotNull(piece.Grainline);Assert.NotSame(source.Grainline,piece.Grainline);
            Assert.Equal(before.Grainline!.Start,piece.Grainline!.Start);
            Assert.Equal(before.Grainline.End,piece.Grainline.End);Assert.True(piece.Grainline.NeedsReview);
        }
        Assert.NotSame(results[0].Grainline,results[1].Grainline);
        results[0].Grainline!.End=new(8,8);
        Assert.Equal(before.Grainline!.End,results[1].Grainline!.End);
        Assert.Empty(GradingEngine.GenerateSizes(source,increments,Array.Empty<GradeSize>()));
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
    }

    [Fact]
    public void GradingRejectsInvalidGrainlineWithoutModifyingSource()
    {
        foreach(var line in new[] { new Grainline { Start=new(double.NaN,0),End=new(1,1) },
            new Grainline { Start=new(0,0),End=new(100_000_001,0) },
            new Grainline { Start=new(2,3),End=new(2,3) } })
        {
            var source=Source();source.Grainline=line;var before=source.Clone();
            var increments=source.Points.Select(_=>new Point2(0,0)).ToArray();
            Assert.ThrowsAny<ArgumentException>(()=>GradingEngine.Generate(source,increments,1,"L"));
            Assert.ThrowsAny<ArgumentException>(()=>GradingEngine.GenerateSizes(source,increments,[new("L",1)]));
            Assert.ThrowsAny<ArgumentException>(()=>GradingEngine.GenerateSizes(source,increments,Array.Empty<GradeSize>()));
            Assert.Equal(before.Points,source.Points);Assert.Equal(before.Grainline!.Start,source.Grainline!.Start);
            Assert.Equal(before.Grainline.End,source.Grainline.End);Assert.Equal(before.Grainline.NeedsReview,source.Grainline.NeedsReview);
        }
    }

    [Theory]
    [InlineData("insert")] [InlineData("delete")] [InlineData("move")]
    public void ContourEditCopiesGrainlineAndMarksRelationshipForReview(string operation)
    {
        var source=Source();var original=source.Clone();
        var edited=operation switch
        {
            "insert"=>PatternTools.InsertPoint(source,0,new Point2(10,0)),
            "delete"=>PatternTools.DeletePoint(source,1),
            _=>PatternTools.MovePoint(source,1,new Point2(20,1))
        };
        Assert.NotNull(edited.Grainline);Assert.NotSame(source.Grainline,edited.Grainline);
        Assert.Equal(source.Grainline!.Start,edited.Grainline!.Start);Assert.Equal(source.Grainline.End,edited.Grainline.End);
        Assert.True(edited.Grainline.NeedsReview);Assert.False(source.Grainline.NeedsReview);
        edited.Grainline.Start=new(7,7);Assert.Equal(new Point2(5,4),source.Grainline.Start);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[original] },new ProjectDocument { Pieces=[source] }));
    }

    [Fact]
    public void NoOpPointMoveDoesNotCreateReviewRequirement()
    {
        var source=Source();var edited=PatternTools.MovePoint(source,1,source.Points[1]);
        Assert.NotNull(edited.Grainline);Assert.NotSame(source.Grainline,edited.Grainline);
        Assert.False(edited.Grainline!.NeedsReview);
        source.Grainline!.NeedsReview=true;
        Assert.True(PatternTools.MovePoint(source,1,source.Points[1]).Grainline!.NeedsReview);
    }
}
