using TekstilCAD.Core;
namespace TekstilCAD.Tests;
public class GeometryTests
{
    private static readonly Point2[] Square = [new(0,0), new(100,0), new(100,100), new(0,100)];
    [Fact] public void DistanceUsesMillimeters() => Assert.Equal(5, Geometry.Distance(new(0,0), new(3,4)));
    [Fact] public void AreaHasOrientation() { Assert.Equal(10000, Geometry.SignedArea(Square)); Assert.Equal(-10000, Geometry.SignedArea(Square.Reverse().ToArray())); }
    [Fact] public void EmptyBoundsRejected() => Assert.Throws<ArgumentException>(()=>Geometry.Bounds([]));
    [Theory] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    public void NonfiniteCoordinatesRejected(double value) => Assert.Throws<ArgumentOutOfRangeException>(()=>Geometry.Distance(new(value,0),default));
    [Fact] public void TranslationDoesNotMutateSource() { var moved=Geometry.Translate(Square,2,-3); Assert.Equal(new Point2(2,-3),moved[0]); Assert.Equal(new Point2(0,0),Square[0]); }
    [Fact] public void RotationAboutPivot() { var p=Geometry.Rotate([new(11,10)],90,new(10,10))[0]; Assert.Equal(10,p.X,10); Assert.Equal(11,p.Y,10); }
    [Fact] public void MirrorTwiceRestoresCoordinates() => Assert.Equal(Square,Geometry.Mirror(Geometry.Mirror(Square,true,25),true,25));
    [Theory] [InlineData(50,50,true)] [InlineData(0,50,true)] [InlineData(100,100,true)] [InlineData(101,50,false)]
    public void ContainmentIncludesBoundary(double x,double y,bool expected) => Assert.Equal(expected,Geometry.PointInPolygon(new(x,y),Square));
    [Fact] public void SquareIsSimple() => Assert.False(Geometry.HasSelfIntersections(Square));
    [Fact] public void ExplicitClosingVertexAccepted() => Assert.False(Geometry.HasSelfIntersections([..Square,Square[0]]));
    [Fact] public void BowtieRejected() => Assert.True(Geometry.HasSelfIntersections([new(0,0),new(100,100),new(0,100),new(100,0)]));
    [Fact] public void BacktrackingRejected() => Assert.True(Geometry.HasSelfIntersections([new(0,0),new(10,0),new(5,0),new(5,10)]));
    [Fact] public void BezierEndpointsAndMidpoint() { var p=Geometry.SampleCubicBezier(new(0,0),new(0,10),new(10,10),new(10,0),2); Assert.Equal(3,p.Count); Assert.Equal(new Point2(0,0),p[0]); Assert.Equal(new Point2(5,7.5),p[1]); Assert.Equal(new Point2(10,0),p[2]); }
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(100001)] public void InvalidBezierResolutionRejected(int count) => Assert.Throws<ArgumentOutOfRangeException>(()=>Geometry.SampleCubicBezier(default,default,default,default,count));
}
