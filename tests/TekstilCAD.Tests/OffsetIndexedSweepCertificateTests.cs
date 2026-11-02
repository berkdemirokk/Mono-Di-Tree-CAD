using System.Diagnostics;
using System.Reflection;
using TekstilCAD.Patterns;
using Xunit.Abstractions;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Experimental indexed boundary evidence; feature preservation is outside this test.</summary>
public sealed class OffsetIndexedSweepCertificateTests(ITestOutputHelper output)
{
    private static (bool Ok,IReadOnlyList<GridPoint> Candidate,string Reason) Verify(GridPoint[] source,long delta)
    {
        var method=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetIndexedSweepCertificate",true)!
            .GetMethod("TryCreate",BindingFlags.Static|BindingFlags.NonPublic)!;
        object?[] args=[source,delta,null,null,CancellationToken.None];
        bool ok=(bool)method.Invoke(null,args)!;
        return (ok,(IReadOnlyList<GridPoint>)args[2]!, (string)args[3]!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FourAnalyticFixturesHaveCertifiedIndexedBoundaries(bool reverse)
    {
        const long u=1_000_000;
        static GridPoint T(long x,long y)=>new(3*x-4*y,4*x+3*y);
        GridPoint[][] fixtures=[
            [new(0,0),new(100*u,0),new(100*u,100*u),new(0,100*u)],
            [new(0,0),new(120*u,0),new(120*u,80*u),new(0,80*u)],
            [T(0,0),T(10*u,0),T(10*u,10*u),T(0,10*u)],
            [new(0,0),new(100*u,0),new(100*u,100*u),new(70*u,100*u),
             new(70*u,60*u),new(30*u,60*u),new(30*u,100*u),new(0,100*u)] ];
        int count=0;
        foreach(var input in fixtures)
        {
            if(reverse)Array.Reverse(input);
            var before=input.ToArray(); var timer=Stopwatch.StartNew(); var result=Verify(input,u);
            output.WriteLine($"fixture={count}; reverse={reverse}; vertices={input.Length}; accepted={result.Ok}; ms={timer.Elapsed.TotalMilliseconds:F3}; reason={result.Reason}");
            Assert.True(result.Ok,result.Reason); Assert.Equal(input.Length,result.Candidate.Count);
            Assert.Equal(before,input); count++;
        }
        Assert.Equal(4,count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LobedBoundaryReportsEveryStageWithoutAuthorizingFeatures(bool reverse)
    {
        const long u=1_000_000;
        var source=Enumerable.Range(0,512).Select(i=>
        {
            double a=Math.Tau*i/512, r=1000+200*Math.Cos(3*a);
            return new GridPoint((long)Math.Round(r*Math.Cos(a)*u,MidpointRounding.AwayFromZero),
                (long)Math.Round(r*Math.Sin(a)*u,MidpointRounding.AwayFromZero));
        }).ToArray();
        if(reverse)Array.Reverse(source);
        var before=source.ToArray(); var timer=Stopwatch.StartNew(); var result=Verify(source,u);
        output.WriteLine($"lobed reverse={reverse}; vertices={source.Length}; accepted={result.Ok}; ms={timer.Elapsed.TotalMilliseconds:F3}; reason={result.Reason}");
        Assert.True(result.Ok,result.Reason); Assert.Equal(source.Length,result.Candidate.Count);
        Assert.Contains("not certified",result.Reason); Assert.Equal(before,source);
    }

    [Fact]
    public void LostOneGridFeatureAndClosedChannelFailClosed()
    {
        const long u=1_000_000;
        GridPoint[] notch=[new(0,0),new(100*u,0),new(100*u,100*u),new(50*u+2,100*u),
            new(50*u+2,100*u-1),new(50*u,100*u-1),new(50*u,100*u),new(0,100*u)];
        GridPoint[] channel=[new(0,0),new(100*u,0),new(100*u,100*u),new(52*u,100*u),
            new(52*u,20*u),new(48*u,20*u),new(48*u,100*u),new(0,100*u)];
        var a=Verify(notch,u);var b=Verify(channel,5*u);
        Assert.False(a.Ok);Assert.Empty(a.Candidate);
        Assert.False(b.Ok);Assert.Empty(b.Candidate);
    }

    [Fact]
    public void BudgetAndCancellationCannotReturnACandidate()
    {
        var large=Enumerable.Range(0,708).Select(i=>new GridPoint(i,(long)i*i)).ToArray();
        var result=Verify(large,100);
        Assert.False(result.Ok);Assert.Empty(result.Candidate);
        var method=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetIndexedSweepCertificate",true)!
            .GetMethod("TryCreate",BindingFlags.Static|BindingFlags.NonPublic)!;
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        GridPoint[] square=[new(0,0),new(1000,0),new(1000,1000),new(0,1000)];
        var ex=Assert.Throws<TargetInvocationException>(()=>method.Invoke(null,
            [square,10L,null,null,cancellation.Token]));
        Assert.IsAssignableFrom<OperationCanceledException>(ex.InnerException);
    }

    [Fact]
    public void ReturnedCandidateCannotBeMutatedThroughListInterface()
    {
        GridPoint[] source=[new(0,0),new(1000,0),new(1000,1000),new(0,1000)];
        var result=Verify(source,10);
        Assert.True(result.Ok,result.Reason);
        var list=Assert.IsAssignableFrom<IList<GridPoint>>(result.Candidate);
        Assert.Throws<NotSupportedException>(()=>list[0]=new GridPoint(1,1));
    }
}
