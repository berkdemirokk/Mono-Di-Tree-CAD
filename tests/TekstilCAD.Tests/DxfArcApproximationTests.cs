using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;
public sealed class DxfArcApproximationTests
{
    [Theory]
    [InlineData(0,90,90)] [InlineData(350,10,20)] [InlineData(-90,90,180)] [InlineData(20,-70,270)]
    [InlineData(720,810,90)] [InlineData(270,269,359)]
    public void OpenArcHasExpectedEndpointsDirectionAndSampledChordError(double start,double end,double sweep)
    {
        const double radius=10,tolerance=0.01;var center=new Point2(20,-30);
        var result=DxfArcApproximation.Approximate(center,radius,start,end,1,tolerance,10_000);
        Assert.True(result.Points.Length>=2);double sum=0;
        for(var i=0;i<result.Points.Length;i++)
        {
            var p=result.Points[i];var dx=p.X-center.X;var dy=p.Y-center.Y;
            Assert.InRange(Math.Abs(Math.Sqrt(dx*dx+dy*dy)-radius),0,1e-7);
            if(i==0)continue;
            var q=result.Points[i-1];var qx=q.X-center.X;var qy=q.Y-center.Y;
            var delta=Math.Atan2(qx*dy-qy*dx,qx*dx+qy*dy);Assert.True(delta>0);sum+=delta;
            var mx=(p.X+q.X)/2-center.X;var my=(p.Y+q.Y)/2-center.Y;
            Assert.InRange(radius-Math.Sqrt(mx*mx+my*my),-1e-7,tolerance+1e-7);
        }
        Assert.InRange(Math.Abs(sum-sweep*Math.PI/180),0,1e-7);
        var first=result.Points[0];var last=result.Points[^1];
        Assert.InRange(Math.Abs(first.X-(center.X+radius*Math.Cos(start*Math.PI/180))),0,1e-7);
        Assert.InRange(Math.Abs(last.Y-(center.Y+radius*Math.Sin(end*Math.PI/180))),0,1e-7);
        Assert.False(Geometry.HasSelfIntersections(result.Points,false));
    }
    [Fact] public void RejectsAmbiguousDegenerateAndUnrepresentableInputs()
    {
        Assert.Throws<InvalidDataException>(()=>DxfArcApproximation.Approximate(new(0,0),1,0,360,1,0.1,100));
        Assert.Throws<ArgumentOutOfRangeException>(()=>DxfArcApproximation.Approximate(new(0,0),0,0,90,1,0.1,100));
        Assert.Throws<InvalidDataException>(()=>DxfArcApproximation.Approximate(new(1e16,0),0.1,0,90,1,0.01,100));
        Assert.Throws<InvalidDataException>(()=>DxfArcApproximation.Approximate(new(0,0),1000,0,270,1,1e-9,4));
    }
    [Fact] public void CancellationIsObserved()
    {
        using var cts=new CancellationTokenSource();cts.Cancel();
        Assert.Throws<OperationCanceledException>(()=>DxfArcApproximation.Approximate(new(0,0),1,0,90,1,0.1,100,cts.Token));
    }
}
