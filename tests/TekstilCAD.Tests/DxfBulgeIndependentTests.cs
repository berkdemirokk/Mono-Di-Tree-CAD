using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfBulgeIndependentTests
{
    // Oracle starts with a known circle and sweep; it does not recover the
    // center or radius from the production bulge algorithm.
    [Theory]
    [InlineData(90, 1.0, 0.001)]
    [InlineData(-90, 10.0, 0.1)]
    [InlineData(180, 25.4, 0.001)]
    [InlineData(-180, 1.0, 0.1)]
    [InlineData(270, 1.0, 0.001)]
    [InlineData(-270, 10.0, 0.1)]
    public void KnownCircleSweepHasCorrectDirectionAndChordError(double degrees,double scale,double tolerance)
    {
        const double radius=10;
        var sweep=degrees*Math.PI/180;
        var start=new Point2(radius,0);
        var end=new Point2(radius*Math.Cos(sweep),radius*Math.Sin(sweep));
        var result=DxfBulgeApproximation.Approximate([new(start,Math.Tan(sweep/4)),new(end,0)],false,scale,tolerance,10_000);
        Assert.Equal(start*scale,result.Points[0]);Assert.Equal(end*scale,result.Points[^1]);
        AssertCirclePolyline(result.Points.ToArray(),new Point2(0,0),radius*scale,sweep,tolerance,1e-8);
    }

    [Fact]
    public void ClosedFinalEdgeIsSampledAndReturnsImplicitlyToFirstPoint()
    {
        var start=new Point2(10,0);var second=new Point2(-10,0);var final=new Point2(0,10);
        var result=DxfBulgeApproximation.Approximate([new(start,0),new(second,0),new(final,-Math.Tan(Math.PI/8))],true,1,0.001,10_000);
        Assert.Equal(start,result.Points[0]);Assert.Equal(second,result.Points[1]);Assert.Equal(final,result.Points[2]);
        Assert.NotEqual(start,result.Points[^1]);
        var closing=result.Points.Skip(2).Append(start).ToArray();
        AssertCirclePolyline(closing,new Point2(0,0),10,-Math.PI/2,0.001,1e-8);
    }

    [Fact]
    public void SmallRadiusAtLargeOffsetRemainsWithinIndependentChordBound()
    {
        const double offset=10_000_000,radius=0.1,tolerance=0.001;
        var center=new Point2(offset,offset);
        var start=new Point2(offset+radius,offset);var end=new Point2(offset,offset+radius);
        var result=DxfBulgeApproximation.Approximate([new(start,Math.Tan(Math.PI/8)),new(end,0)],false,1,tolerance,10_000);
        AssertCirclePolyline(result.Points.ToArray(),center,radius,Math.PI/2,tolerance,1e-7);
    }

    [Fact]
    public void TenThousandPointBudgetAcceptsStraightBoundaryAndRejectsOversampledArc()
    {
        var straight=Enumerable.Range(0,10_000).Select(i=>new DxfBulgeVertex(new Point2(i,0),0)).ToArray();
        var result=DxfBulgeApproximation.Approximate(straight,false,1,0.1,10_000);
        Assert.Equal(10_000,result.Points.Length);Assert.Equal(0,result.AddedVertices);
        var oversized=new[] { new DxfBulgeVertex(new Point2(10_000,0),1),new DxfBulgeVertex(new Point2(-10_000,0),0) };
        Assert.Throws<InvalidDataException>(()=>DxfBulgeApproximation.Approximate(oversized,false,1,0.00001,10_000));
    }

    private static void AssertCirclePolyline(IReadOnlyList<Point2> points,Point2 center,double radius,double sweep,double tolerance,double epsilon)
    {
        Assert.True(points.Count>=2);
        var previous=Math.Atan2(points[0].Y-center.Y,points[0].X-center.X);
        var accumulated=0.0;
        for(var i=0;i<points.Count;i++)
        {
            var x=points[i].X-center.X;var y=points[i].Y-center.Y;
            Assert.InRange(Math.Abs(Math.Sqrt(x*x+y*y)-radius),0,epsilon);
            if(i==0)continue;
            var angle=Math.Atan2(y,x);var delta=angle-previous;
            while(delta<=-Math.PI)delta+=2*Math.PI;
            while(delta>Math.PI)delta-=2*Math.PI;
            Assert.True(sweep>0 ? delta>0 : delta<0);
            accumulated+=delta;previous=angle;
            var a=points[i-1];var b=points[i];
            var dx=b.X-a.X;var dy=b.Y-a.Y;var chord=Math.Sqrt(dx*dx+dy*dy);
            // For each subarc shorter than pi, the farthest arc point from its
            // chord is at the angular midpoint; compute its actual distance to
            // that chord's midpoint using the known circle center.
            var middleAngle=angle-delta/2;
            var arcX=center.X+radius*Math.Cos(middleAngle);var arcY=center.Y+radius*Math.Sin(middleAngle);
            var mx=a.X/2+b.X/2;var my=a.Y/2+b.Y/2;
            var deviation=Math.Sqrt((arcX-mx)*(arcX-mx)+(arcY-my)*(arcY-my));
            Assert.True(chord<=2*radius+epsilon);
            Assert.True(deviation<=tolerance+epsilon,$"Chord deviation {deviation} exceeds {tolerance}.");
        }
        Assert.InRange(Math.Abs(accumulated-sweep),0,1e-6);
    }
}
