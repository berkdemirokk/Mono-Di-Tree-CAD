using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
namespace TekstilCAD.Tests;

public class OffsetUserFeatureToleranceExperimentTests
{
    private static bool Verify(Point2[] source,Point2[] candidate,double width,double depth,out string reason)
    {
        var method=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetUserFeatureToleranceExperiment",true)!
            .GetMethod("TryVerify",BindingFlags.Static|BindingFlags.NonPublic)!;
        object?[] args=[source,candidate,3,4,5,width,depth,null,CancellationToken.None];
        bool ok=(bool)method.Invoke(null,args)!;reason=(string)args[7]!;return ok;
    }
    private static Point2[] Source()=>[new(0,0),new(100,0),new(100,100),new(60,100),
        new(50,90),new(40,100),new(0,100)];
    [Fact]
    public void UserToleranceRejectsNearlyLostTenMillimeterNotch()
    {
        var source=Source();var candidate=(Point2[])source.Clone();candidate[4]=new(50,99.999999);
        Assert.False(Verify(source,candidate,0,0.001,out var reason));
        Assert.Contains("depth",reason);
        Assert.True(Verify(source,candidate,0,10,out _));
    }
    [Fact]
    public void WidthToleranceControlsMouthChange()
    {
        var source=Source();var candidate=(Point2[])source.Clone();
        candidate[3]=new(59,100);candidate[5]=new(41,100);
        Assert.False(Verify(source,candidate,1.99,0,out var reason));
        Assert.Contains("width",reason);
        Assert.True(Verify(source,candidate,2,0,out _));
    }
    [Fact]
    public void ExactZeroAndBoundaryToleranceHaveNoRoundingSlack()
    {
        var source=Source();var candidate=(Point2[])source.Clone();
        Assert.True(Verify(source,candidate,0,0,out _));
        candidate[4]=new(50,91);
        Assert.False(Verify(source,candidate,0,Math.BitDecrement(1.0),out _));
        Assert.True(Verify(source,candidate,0,1,out _));
    }
    [Fact]
    public void FlatOrReversedNotchRejectedEvenWithGenerousTolerances()
    {
        var source=Source();var candidate=(Point2[])source.Clone();
        candidate[4]=new(50,100);Assert.False(Verify(source,candidate,100,100,out _));
        candidate[4]=new(50,101);Assert.False(Verify(source,candidate,100,100,out _));
    }
    [Fact]
    public void LargeOriginDoesNotHideChange()
    {
        var source=Source().Select(p=>new Point2(p.X+90_000_000,p.Y+90_000_000)).ToArray();
        var candidate=(Point2[])source.Clone();candidate[4]=new(candidate[4].X,candidate[4].Y+1);
        Assert.False(Verify(source,candidate,0,0.5,out _));
        Assert.True(Verify(source,candidate,0,1,out _));
    }
}

