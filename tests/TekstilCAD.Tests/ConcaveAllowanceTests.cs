using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class ConcaveAllowanceTests
{
    private static PatternPiece LShape() => new() { Name="L", Points=[new(0,0),new(100,0),new(100,40),new(40,40),new(40,100),new(0,100)] };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcaveLHasExpectedOutwardGeometryInBothDirections(bool clockwise)
    {
        var source=LShape(); if(clockwise)source.Points.Reverse();
        var original=source.Points.ToArray(); var id=source.Id;
        var result=PatternTools.Offset(source,5);
        var bounds=Geometry.Bounds(result.Points);
        Assert.Equal(-5,bounds.MinX,5); Assert.Equal(-5,bounds.MinY,5);
        Assert.Equal(105,bounds.MaxX,5); Assert.Equal(105,bounds.MaxY,5);
        Assert.Equal(8500,Math.Abs(Geometry.SignedArea(result.Points)),4);
        Assert.False(Geometry.HasSelfIntersections(result.Points));
        Assert.Equal(original,source.Points); Assert.Equal(id,source.Id);
        Assert.NotEqual(id,result.Id); Assert.NotSame(source.Points,result.Points);
    }

    [Fact]
    public void ZeroAllowancePreservesExactPrecisionAndIsIndependent()
    {
        var source=LShape(); source.Points[0]=new(0.00000012345,0.00000023456);
        var copy=PatternTools.Offset(source,0);
        Assert.Equal(source.Points,copy.Points); Assert.NotSame(source.Points,copy.Points);
        copy.Points[0]=new(2,2); Assert.NotEqual(source.Points[0],copy.Points[0]);
    }

    [Theory]
    [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidAllowanceRejected(double value) => Assert.ThrowsAny<ArgumentException>(()=>PatternTools.Offset(LShape(),value));

    [Fact]
    public void OpenAndSelfIntersectingContoursRejectedWithoutMutatingInput()
    {
        var open=LShape();open.Closed=false; var old=open.Points.ToArray();
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.Offset(open,5)); Assert.Equal(old,open.Points);
        var bow=new PatternPiece { Points=[new(0,0),new(100,100),new(0,100),new(100,0)] };
        var before=bow.Points.ToArray(); Assert.ThrowsAny<ArgumentException>(()=>PatternTools.Offset(bow,5)); Assert.Equal(before,bow.Points);
    }

    [Fact]
    public void SubGridAllowanceRejectedInsteadOfSilentlyDisappearing()
        => Assert.ThrowsAny<ArgumentException>(()=>PatternTools.Offset(LShape(),0.0000001));

    [Fact]
    public void NearlyCoincidentVerticesCannotSilentlyCollapse()
    {
        var piece=new PatternPiece { Points=[new(0,0),new(0.0000002,0),new(100,0),new(100,100),new(0,100)] };
        var before=piece.Points.ToArray();
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.Offset(piece,2)); Assert.Equal(before,piece.Points);
    }
    [Fact]
    public void ZeroAllowanceRetainsExplicitClosureExactly()
    {
        var piece=LShape();piece.Points.Add(piece.Points[0]);
        var result=PatternTools.Offset(piece,0);
        Assert.Equal(piece.Points,result.Points);
        Assert.NotSame(piece.Points,result.Points);
    }

    [Fact]
    public void NarrowRecessThatWouldCloseMustNotBeSilentlyRemoved()
    {
        var piece=new PatternPiece {Points=[new(0,0),new(100,0),new(100,100),new(52,100),new(52,20),new(48,20),new(48,100),new(0,100)]};
        var original=piece.Points.ToArray();
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.Offset(piece,5));
        Assert.Equal(original,piece.Points);
    }

    [Fact]
    public void SharpMiterMustNotBeSilentlyCutOff()
    {
        var piece=new PatternPiece {Points=[new(0,0),new(100,0),new(1,10)]};
        var original=piece.Points.ToArray();
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.Offset(piece,5));
        Assert.Equal(original,piece.Points);
    }
}
