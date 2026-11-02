using System.Reflection;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetAutomaticOrthogonalFeaturesExperimentTests
{
    private const long U=1_000_000;
    private static (bool Ok,int Count,string Reason) Verify(GridPoint[] source,GridPoint[] candidate,
        double width=2,double depth=1)
    {
        var method=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetAutomaticOrthogonalFeaturesExperiment",true)!
            .GetMethod("TryVerify",BindingFlags.Static|BindingFlags.NonPublic)!;
        object?[] args=[source,candidate,U,width,depth,0,null,CancellationToken.None];
        bool ok=(bool)method.Invoke(null,args)!;
        return (ok,(int)args[5]!, (string)args[6]!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FindsNestedRecessesAndRejectsCandidateOrToleranceMismatch(bool reverse)
    {
        GridPoint[] source=[new(0,0),new(120*U,0),new(120*U,120*U),new(100*U,120*U),
            new(100*U,80*U),new(80*U,80*U),new(80*U,60*U),new(60*U,60*U),
            new(60*U,80*U),new(20*U,80*U),new(20*U,120*U),new(0,120*U)];
        GridPoint[] candidate=[new(-U,-U),new(121*U,-U),new(121*U,121*U),new(99*U,121*U),
            new(99*U,81*U),new(79*U,81*U),new(79*U,61*U),new(61*U,61*U),
            new(61*U,81*U),new(21*U,81*U),new(21*U,121*U),new(-U,121*U)];
        if(reverse){Array.Reverse(source);Array.Reverse(candidate);}
        var result=Verify(source,candidate);Assert.True(result.Ok,result.Reason);Assert.Equal(2,result.Count);
        Assert.False(Verify(source,candidate,Math.BitDecrement(2)).Ok);
        candidate[reverse?source.Length-1-6:6]=new(0,0);
        Assert.False(Verify(source,candidate).Ok);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NarrowChannelAndUnmatchedRecessAreNotSilentlyAccepted(bool reverse)
    {
        GridPoint[] source=[new(0,0),new(100*U,0),new(100*U,100*U),new(52*U,100*U),
            new(52*U,20*U),new(48*U,20*U),new(48*U,100*U),new(0,100*U)];
        GridPoint[] collapsed=[new(-U,-U),new(101*U,-U),new(101*U,101*U),new(50*U,101*U),
            new(50*U,21*U),new(50*U,21*U),new(50*U,101*U),new(-U,101*U)];
        if(reverse){Array.Reverse(source);Array.Reverse(collapsed);}
        Assert.False(Verify(source,collapsed).Ok);
        source[reverse?source.Length-1-5:5]=new(48*U,19*U);
        Assert.False(Verify(source,collapsed).Ok);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lobed512ContoursAreExplicitlyUnsupported(bool reverse)
    {
        var source=Enumerable.Range(0,512).Select(i=>
        {
            double angle=2*Math.PI*i/512, radius=1000+200*Math.Cos(3*angle);
            return new GridPoint((long)Math.Round(radius*Math.Cos(angle)*U),(long)Math.Round(radius*Math.Sin(angle)*U));
        }).ToArray();
        if(reverse)Array.Reverse(source);
        var result=Verify(source,source);
        Assert.False(result.Ok);Assert.Contains("Non-orthogonal",result.Reason);
    }
}
