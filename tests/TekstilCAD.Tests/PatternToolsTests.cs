using TekstilCAD.Core;
using TekstilCAD.Patterns;
namespace TekstilCAD.Tests;
public class PatternToolsTests
{
    private static PatternPiece Square() => new() { Points = [new(0,0),new(100,0),new(100,100),new(0,100)] };
    [Fact] public void AllowancePreservesSourceAndExpandsSquare()
    {
        var source=Square(); var expanded=PatternTools.OffsetConvex(source,10); var bounds=Geometry.Bounds(expanded.Points);
        Assert.Equal(120,bounds.Width,8); Assert.Equal(120,bounds.Height,8); Assert.Equal(-10,bounds.MinX,8);
        Assert.Equal(14400,Math.Abs(Geometry.SignedArea(expanded.Points)),8); Assert.NotEqual(source.Id,expanded.Id); Assert.Equal(100,Geometry.Bounds(source.Points).Width);
    }
    [Fact] public void ClockwiseContourAlsoOffsetsOutward()
    {
        var square=Square(); square.Points.Reverse(); var expanded=PatternTools.OffsetConvex(square,10);
        Assert.Equal(120,Geometry.Bounds(expanded.Points).Width,8); Assert.True(Geometry.SignedArea(expanded.Points)<0);
    }
    [Theory] [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidAllowanceRejected(double value) => Assert.Throws<ArgumentOutOfRangeException>(()=>PatternTools.OffsetConvex(Square(),value));
    [Fact] public void ConcaveContourRejected()
    {
        var piece=new PatternPiece {Points=[new(0,0),new(100,0),new(50,50),new(100,100),new(0,100)]};
        Assert.Throws<ArgumentException>(()=>PatternTools.OffsetConvex(piece,10));
    }
    [Fact] public void OpenContourRejected() {var p=Square();p.Closed=false;Assert.Throws<ArgumentException>(()=>PatternTools.OffsetConvex(p,5));}
    [Fact] public void ZeroAllowanceCreatesIndependentCopy() {var p=Square();var r=PatternTools.OffsetConvex(p,0);Assert.Equal(p.Points,r.Points);Assert.NotSame(p.Points,r.Points);}
    [Fact] public void NotchHasExpectedCenterDepthAndWidth()
    {
        var p=PatternTools.NotchPoints(new(0,0),new(100,0),.5,5);
        Assert.Equal(new Point2(45,0),p[0]);Assert.Equal(new Point2(50,5),p[1]);Assert.Equal(new Point2(55,0),p[2]);
    }
    [Fact] public void NotchCannotEscapeSegment() => Assert.Throws<ArgumentException>(()=>PatternTools.NotchPoints(new(0,0),new(10,0),.1,2));
    [Fact] public void InsertAndDeletePointPreserveArea()
    {
        var p=Square();var inserted=PatternTools.InsertPoint(p,0,new(50,0));Assert.Equal(5,inserted.Points.Count);
        Assert.Equal(10000,Geometry.SignedArea(inserted.Points));var deleted=PatternTools.DeletePoint(inserted,1);Assert.Equal(p.Points,deleted.Points);
    }
    [Fact] public void DeleteCannotCollapseTriangle()
    {
        var p=new PatternPiece{Points=[new(0,0),new(100,0),new(0,100)]};Assert.Throws<ArgumentException>(()=>PatternTools.DeletePoint(p,1));
    }
    [Fact] public void ArcSamplesStayOnCircleAndIncludeEndpoints()
    {
        var points=PatternTools.SampleArc(new(5,5),10,0,90,16);Assert.Equal(17,points.Count);
        Assert.Equal(15,points[0].X,8);Assert.Equal(15,points[^1].Y,8);
        Assert.All(points,p=>Assert.Equal(10,Geometry.Distance(new(5,5),p),8));
    }
}
