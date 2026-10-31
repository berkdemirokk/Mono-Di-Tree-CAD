using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public class NestingTests
{
    private static PatternPiece Rectangle(double width, double height, double x = 0, double y = 0) =>
        new() { Name = "Test", Points = [new(x,y), new(x+width,y), new(x+width,y+height), new(x,y+height)] };

    [Fact]
    public void TwoPiecesFitSameShelfWithKnownUtilization()
    {
        var result = NestingEngine.Arrange([Rectangle(40,30), Rectangle(40,30)], 100, 5);
        Assert.Equal(30, result.UsedLength);
        Assert.Equal(80, result.UtilizationPercent, 8);
        Assert.Equal(20, result.WastePercent, 8);
        var second = Geometry.Bounds(result.Layout.Pieces[1].Points);
        Assert.Equal(45, second.MinX);
        Assert.Equal(0, second.MinY);
    }

    [Fact]
    public void NewShelfIncludesGapAndHonorsFabricWidth()
    {
        var result = NestingEngine.Arrange([Rectangle(40,30), Rectangle(40,30), Rectangle(60,20)], 100, 5);
        Assert.Equal(55, result.UsedLength);
        var last = Geometry.Bounds(result.Layout.Pieces[2].Points);
        Assert.Equal(0, last.MinX);
        Assert.Equal(35, last.MinY);
        foreach (var piece in result.Layout.Pieces)
        {
            var bounds = Geometry.Bounds(piece.Points);
            Assert.InRange(bounds.MinX, 0, 100);
            Assert.InRange(bounds.MaxX, 0, 100);
        }
    }

    [Fact]
    public void PlacementIsDeterministicAndLeavesSourceUnchanged()
    {
        PatternPiece[] source = [Rectangle(60,10,-20,50), Rectangle(30,40,100,-100)];
        var originals = source.Select(p=>p.Clone()).ToList();
        var first = NestingEngine.Arrange(source, 100);
        var second = NestingEngine.Arrange(source, 100);
        for (int i=0; i<first.Layout.Pieces.Count; i++)
        {
            Assert.Equal(first.Layout.Pieces[i].Id,second.Layout.Pieces[i].Id);
            Assert.Equal(first.Layout.Pieces[i].Points,second.Layout.Pieces[i].Points);
            Assert.Equal(originals[i].Points,source[i].Points);
        }
        Assert.Equal(source[1].Id,first.Layout.Pieces[0].Id); // tallest first
    }

    [Fact]
    public void BoundingBoxesAreSeparatedByRequestedGap()
    {
        var result = NestingEngine.Arrange([Rectangle(40,30), Rectangle(40,30), Rectangle(60,20),Rectangle(35,10)],100,7);
        var boxes = result.Layout.Pieces.Select(p=>Geometry.Bounds(p.Points)).ToArray();
        for(var i=0;i<boxes.Length;i++) for(var j=i+1;j<boxes.Length;j++)
        {
            var a=boxes[i]; var b=boxes[j];
            Assert.True(a.MaxX+7<=b.MinX || b.MaxX+7<=a.MinX || a.MaxY+7<=b.MinY || b.MaxY+7<=a.MinY);
        }
    }

    [Fact]
    public void RejectsPieceWiderThanFabricWithoutRotatingIt()
    {
        Assert.Throws<ArgumentException>(()=>NestingEngine.Arrange([Rectangle(110,20)],100));
    }

    [Fact]
    public void RejectsLengthOverflowWithoutMutatingPieces()
    {
        var piece=Rectangle(70,30); var original=piece.Clone();
        Assert.Throws<InvalidOperationException>(()=>NestingEngine.Arrange([piece,Rectangle(70,30)],100,5,60));
        Assert.Equal(original.Points,piece.Points);
    }

    [Theory]
    [InlineData(0,5)]
    [InlineData(-1,5)]
    [InlineData(100,-1)]
    [InlineData(double.NaN,5)]
    [InlineData(100,double.PositiveInfinity)]
    public void RejectsInvalidDimensions(double width,double gap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>NestingEngine.Arrange([Rectangle(20,20)],width,gap));
    }

    [Fact]
    public void RejectsInvalidProductionContours()
    {
        var open=Rectangle(20,20);open.Closed=false;
        Assert.Throws<ArgumentException>(()=>NestingEngine.Arrange([open],100));
        var crossed=new PatternPiece { Points=[new(0,0),new(20,20),new(0,20),new(20,0)] };
        Assert.Throws<ArgumentException>(()=>NestingEngine.Arrange([crossed],100));
        var flat=new PatternPiece { Points=[new(0,0),new(10,0),new(20,0)] };
        Assert.Throws<ArgumentException>(()=>NestingEngine.Arrange([flat],100));
    }

    [Fact]
    public void EmptyJobConsumesNoFabric()
    {
        var result=NestingEngine.Arrange([],100);
        Assert.Empty(result.Layout.Pieces);
        Assert.Equal(0,result.UsedLength);
        Assert.Equal(0,result.UtilizationPercent);
        Assert.Equal(0,result.WastePercent);
    }
}
