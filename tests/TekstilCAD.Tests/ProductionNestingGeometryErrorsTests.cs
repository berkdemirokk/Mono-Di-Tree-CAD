using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class ProductionNestingGeometryErrorsTests
{
    private static PatternPiece Piece() => new()
    {
        Name="Aynı\u202E",
        Closed=true,
        Points=[new(0,0),new(20,0),new(20,20),new(0,20)],
        Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=2 }
    };

    [Theory]
    [InlineData("open")]
    [InlineData("few")]
    [InlineData("many")]
    [InlineData("nonfinite")]
    [InlineData("selfintersect")]
    [InlineData("zeroarea")]
    public void PieceGeometryErrorsIdentifyOriginalSourceAfterCopyExpansion(string condition)
    {
        var good=Piece();var bad=Piece();
        switch(condition)
        {
            case "open": bad.Closed=false; break;
            case "few": bad.Points=[new(0,0),new(1,0)]; break;
            case "many": bad.Points=Enumerable.Range(0,2001).Select(i=>new Point2(i,Math.Sin(i))).ToList(); break;
            case "nonfinite": bad.Points[1]=new(double.NaN,0); break;
            case "selfintersect": bad.Points=[new(0,0),new(10,10),new(0,10),new(10,0)]; break;
            case "zeroarea": bad.Points=[new(0,0),new(1,0),new(2,0)]; break;
            default: throw new ArgumentOutOfRangeException(nameof(condition));
        }
        var source=new ProjectDocument { Pieces=[good,bad] };var before=source.Clone();
        var error=Assert.Throws<ArgumentException>(()=>ProductionNestingPlan.Arrange(source,[good.Id,bad.Id],100));
        Assert.True(NestingPieceError.TryGet(error,out var sourceId,out var reason));
        Assert.Equal(bad.Id,sourceId);Assert.NotEqual(good.Id,sourceId);
        Assert.False(string.IsNullOrWhiteSpace(reason));Assert.DoesNotContain('\u202E',error.Message);
        Assert.True(DocumentSession.ContentEquals(before,source));
        Assert.Equal(2,bad.Metadata!.Quantity);
    }

    [Fact]
    public void DirectMotorIdentifiesInvalidGeometryWithoutRenamingSource()
    {
        var good=Piece();var bad=Piece();bad.Points[2]=new(double.PositiveInfinity,20);
        var source=new ProjectDocument { Pieces=[good,bad] };var before=source.Clone();
        var error=Assert.Throws<ArgumentException>(()=>NestingEngine.Arrange([good,bad],100));
        Assert.True(NestingPieceError.TryGet(error,out var sourceId,out _));
        Assert.Equal(bad.Id,sourceId);Assert.Equal("Aynı\u202E",bad.Name);
        Assert.True(DocumentSession.ContentEquals(before,source));
    }

    [Fact]
    public void PreCancelledGeometryRequestIsCancellationNotPieceFailure()
    {
        var bad=Piece();bad.Closed=false;
        var source=new ProjectDocument { Pieces=[bad] };var before=source.Clone();
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>ProductionNestingPlan.Arrange(source,[bad.Id],100,5,double.PositiveInfinity,0,cancellation.Token));
        Assert.True(DocumentSession.ContentEquals(before,source));
    }
    [Fact]
    public void DuplicateIdsAreRejectedBeforeAmbiguousPieceGeometryError()
    {
        var id=Guid.NewGuid();
        var first=new PatternPiece { Id=id,Name="açık",Closed=false,Points=[new(0,0),new(10,0)] };
        var second=new PatternPiece { Id=id,Name="başka",Points=[new(0,0),new(20,0),new(0,20)] };
        var error=Assert.Throws<ArgumentException>(()=>NestingEngine.Arrange([first,second],100));
        Assert.False(NestingPieceError.TryGet(error,out _,out _));
        Assert.Contains("benzersiz",error.Message);
        Assert.False(first.Closed);Assert.Equal(id,second.Id);
    }
}
