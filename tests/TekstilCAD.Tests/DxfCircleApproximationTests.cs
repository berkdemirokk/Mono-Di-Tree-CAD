using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfCircleApproximationTests
{
    [Theory]
    [InlineData(0,0,10,1,0.01)]
    [InlineData(25,-31,3,25.4,0.1)]
    [InlineData(10000,20000,100,1,0.1)]
    public void SamplesFullCircleInOrderAndWithinNominalChordTarget(double x,double y,double radius,double scale,double tolerance)
    {
        var result=DxfCircleApproximation.Approximate(new(x,y),radius,scale,tolerance,10_000);
        Assert.True(result.Points.Length>=4);
        Assert.NotEqual(result.Points[0],result.Points[^1]);
        var center=new Point2(x*scale,y*scale);var scaledRadius=radius*scale;
        var sum=0d;
        for(var i=0;i<result.Points.Length;i++)
        {
            var a=result.Points[i];var b=result.Points[(i+1)%result.Points.Length];
            var ax=a.X-center.X;var ay=a.Y-center.Y;var bx=b.X-center.X;var by=b.Y-center.Y;
            Assert.InRange(Math.Abs(Math.Sqrt(ax*ax+ay*ay)-scaledRadius),0,1e-7);
            var delta=Math.Atan2(ax*by-ay*bx,ax*bx+ay*by);
            Assert.True(delta>0);sum+=delta;
            var mx=(a.X+b.X)/2-center.X;var my=(a.Y+b.Y)/2-center.Y;
            Assert.InRange(scaledRadius-Math.Sqrt(mx*mx+my*my),-1e-7,tolerance+1e-7);
        }
        Assert.InRange(Math.Abs(sum-Math.Tau),0,1e-7);
        LargeContourValidator.Validate(result.Points);
    }

    [Fact]
    public void RejectsDegenerateUnrepresentableAndOverBudgetCircles()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>DxfCircleApproximation.Approximate(new(0,0),0,1,0.1,100));
        Assert.Throws<ArgumentOutOfRangeException>(()=>DxfCircleApproximation.Approximate(new(double.NaN,0),1,1,0.1,100));
        Assert.Throws<InvalidDataException>(()=>DxfCircleApproximation.Approximate(new(1e16,0),0.1,1,0.01,100));
        Assert.Throws<InvalidDataException>(()=>DxfCircleApproximation.Approximate(new(0,0),1000,1,1e-9,4));
    }

    [Fact]
    public void CancellationIsObservedBeforeSampling()
    {
        using var cts=new CancellationTokenSource();cts.Cancel();
        Assert.Throws<OperationCanceledException>(()=>DxfCircleApproximation.Approximate(new(0,0),1,1,0.1,100,cts.Token));
    }
}
