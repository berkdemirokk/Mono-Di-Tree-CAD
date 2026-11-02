using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetLaminarFeatureEvidenceTests
{
    private static (bool Ok,string Reason) Verify(GridPoint[] source,GridPoint[] candidate,
        (int Start,int Witness,int End,double WidthMm,double DepthMm)[] features)
    {
        var method=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetLaminarFeatureEvidence",true)!
            .GetMethod("TryVerify",BindingFlags.Static|BindingFlags.NonPublic)!;
        object?[] args=[source,candidate,1_000_000L,features,null,CancellationToken.None];
        return ((bool)method.Invoke(null,args)!,(string)args[4]!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedNestedSpansAreUniqueAndDirectional(bool reverse)
    {
        const long u=1_000_000;
        GridPoint[] source=[new(0,0),new(100*u,0),new(100*u,100*u),new(0,100*u)];
        GridPoint[] candidate=[new(-u,-u),new(101*u,-u),new(101*u,101*u),new(-u,101*u)];
        if(reverse){Array.Reverse(source);Array.Reverse(candidate);}
        (int,int,int,double,double) Transform(int a,int w,int b)=>reverse?(3-b,3-w,3-a,10,10):(a,w,b,10,10);
        var parent=Transform(0,1,3);var child=Transform(1,2,3);
        var result=Verify(source,candidate,[parent,child]);
        Assert.True(result.Ok,result.Reason);Assert.Contains("not certified",result.Reason);
        Assert.False(Verify(source,candidate,[Transform(0,1,2),Transform(1,2,3)]).Ok);
        Assert.False(Verify(source,candidate,[Transform(0,1,2),Transform(0,1,3)]).Ok);
        Assert.False(Verify(source,candidate,[Transform(0,3,2)]).Ok);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OuterDepthCanPassWhileInnerDepthLossIsIndependentlyRejected(bool reverse)
    {
        Point2[] source=[new(0,0),new(100,0),new(100,100),new(80,100),new(80,60),
            new(65,60),new(65,50),new(55,50),new(55,60),new(20,60),new(20,100),new(0,100)];
        var candidate=source.ToArray();candidate[6]=candidate[6] with {Y=59};candidate[7]=candidate[7] with {Y=59};
        if(reverse){Array.Reverse(source);Array.Reverse(candidate);}
        var method=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetUserFeatureToleranceExperiment",true)!
            .GetMethod("TryVerify",BindingFlags.Static|BindingFlags.NonPublic)!;
        bool Measure(int a,int w,int b)
        {
            if(reverse)(a,w,b)=(source.Length-1-b,source.Length-1-w,source.Length-1-a);
            object?[] args=[source,candidate,a,w,b,0d,0d,null,CancellationToken.None];
            return (bool)method.Invoke(null,args)!;
        }
        Assert.True(Measure(3,4,10));
        Assert.False(Measure(5,6,8));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultiEdgeNestedNotchChecksBothSelectedWidths(bool reverse)
    {
        const long u=1_000_000;
        GridPoint[] source=[new(0,0),new(120*u,0),new(120*u,120*u),new(100*u,120*u),
            new(100*u,80*u),new(80*u,80*u),new(80*u,60*u),new(60*u,60*u),
            new(60*u,80*u),new(20*u,80*u),new(20*u,120*u),new(0,120*u)];
        GridPoint[] candidate=[new(-u,-u),new(121*u,-u),new(121*u,121*u),new(99*u,121*u),
            new(99*u,81*u),new(79*u,81*u),new(79*u,61*u),new(61*u,61*u),
            new(61*u,81*u),new(21*u,81*u),new(21*u,121*u),new(-u,121*u)];
        if(reverse){Array.Reverse(source);Array.Reverse(candidate);}
        (int,int,int,double,double) Feature(int a,int w,int b,double width)=>reverse
            ?(source.Length-1-b,source.Length-1-w,source.Length-1-a,width,1)
            :(a,w,b,width,1);
        var result=Verify(source,candidate,[Feature(3,4,10,2),Feature(5,6,8,2)]);
        Assert.True(result.Ok,result.Reason);
        Assert.False(Verify(source,candidate,[Feature(3,4,10,2),Feature(5,6,8,Math.BitDecrement(2))]).Ok);
        candidate[reverse?source.Length-1-6:6]=new(0,0);
        Assert.False(Verify(source,candidate,[Feature(3,4,10,2),Feature(5,6,8,2)]).Ok);
    }
}
