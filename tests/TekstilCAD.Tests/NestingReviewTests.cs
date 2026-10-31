using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public class NestingReviewTests
{
    private static PatternPiece Rectangle(double x=0) => new() { Points=[new(x,0),new(x+10,0),new(x+10,10),new(x,10)] };

    [Fact]
    public void EmptyIdentityCannotProduceUnsavableLayout()
    {
        var p=Rectangle(); p.Id=Guid.Empty;
        Assert.Throws<ArgumentException>(()=>NestingEngine.Arrange([p],100));
    }
    [Fact]
    public void DuplicateIdentityRejectedWithoutSourceMutation()
    {
        var p=Rectangle(); var duplicate=p.Clone();
        Assert.Throws<ArgumentException>(()=>NestingEngine.Arrange([p,duplicate],100));
        Assert.Equal(new Point2(0,0),p.Points[0]);
    }
    [Theory]
    [InlineData(null)]
    [InlineData(1025)]
    public void UnsavableNamesRejected(int? length)
    {
        var p=Rectangle();p.Name=length is null ? null! : new string('a',length.Value);
        Assert.Throws<ArgumentException>(()=>NestingEngine.Arrange([p],100));
    }
    [Fact]
    public void BatchWorkRejectedBeforeContourTraversal()
    {
        var pieces=Enumerable.Range(0,51).Select(_=>new PatternPiece {
            Points=Enumerable.Range(0,1000).Select(i=>new Point2(i,i%2)).ToList()
        }).ToList();
        var error=Assert.Throws<ArgumentException>(()=>NestingEngine.Arrange(pieces,10000));
        Assert.Contains("kaynak sınırı",error.Message);
    }
    [Fact]
    public void FarNegativeSourceCanBePlacedAtPositiveShelfCoordinate()
    {
        var source=Rectangle(-Geometry.MaxCoordinate);
        var result=NestingEngine.Arrange([Rectangle(),source],100,5);
        Assert.Equal(15,Geometry.Bounds(result.Layout.Pieces[1].Points).MinX);
        Assert.Equal(-Geometry.MaxCoordinate,source.Points[0].X);
    }
    [Fact]
    public void ExactFabricWidthAndLengthAreAccepted()
    {
        var result=NestingEngine.Arrange([Rectangle()],10,0,10);
        Assert.Equal(100,result.UtilizationPercent);
        Assert.Equal(0,result.WastePercent);
    }
}
