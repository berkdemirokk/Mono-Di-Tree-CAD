using System.Reflection;
using TekstilCAD.Patterns;
using Xunit.Abstractions;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetRefinedGridExperimentTests(ITestOutputHelper log)
{
    private const long U=1_000_000;
    private static (bool Ok,IReadOnlyList<GridPoint> Candidate,string Reason) Create(GridPoint[] source,long delta=U)
    {
        var m=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetRefinedGridExperiment",true)!
            .GetMethod("TryCreate",BindingFlags.NonPublic|BindingFlags.Static)!;
        object?[] args=[source,delta,null,null,CancellationToken.None];
        bool ok=(bool)m.Invoke(null,args)!;return (ok,(IReadOnlyList<GridPoint>)args[2]!, (string)args[3]!);
    }
    [Theory]
    [InlineData(false,0L)]
    [InlineData(true,0L)]
    [InlineData(false,50000000000000L)]
    [InlineData(true,50000000000000L)]
    public void SimpleSquareChecksTranslationAndIndexedResult(bool reverse,long origin)
    {
        GridPoint[] s=[new(origin,origin),new(origin+100*U,origin),new(origin+100*U,origin+100*U),new(origin,origin+100*U)];
        if(reverse)Array.Reverse(s);var before=s.ToArray();var r=Create(s);
        log.WriteLine($"square origin={origin}, reverse={reverse}, accepted={r.Ok}, reason={r.Reason}");
        Assert.True(r.Ok,r.Reason);Assert.Equal(4,r.Candidate.Count);Assert.Equal(before,s);
        Assert.Contains("not certified",r.Reason);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LobedRoundTripAndSmallFeatureAreNotAssumedSafe(bool reverse)
    {
        var lobed=Enumerable.Range(0,512).Select(i=>
        {
            double a=Math.Tau*i/512,r=1000+200*Math.Cos(3*a);
            return new GridPoint((long)Math.Round(r*Math.Cos(a)*U,MidpointRounding.AwayFromZero),
                (long)Math.Round(r*Math.Sin(a)*U,MidpointRounding.AwayFromZero));
        }).ToArray();
        GridPoint[] notch=[new(0,0),new(100*U,0),new(100*U,100*U),new(50*U+2,100*U),
            new(50*U+2,100*U-1),new(50*U,100*U-1),new(50*U,100*U),new(0,100*U)];
        GridPoint[] channel=[new(0,0),new(100*U,0),new(100*U,100*U),new(52*U,100*U),
            new(52*U,20*U),new(48*U,20*U),new(48*U,100*U),new(0,100*U)];
        if(reverse){Array.Reverse(lobed);Array.Reverse(notch);Array.Reverse(channel);}
        var l=Create(lobed);var n=Create(notch);var c=Create(channel,5*U);
        log.WriteLine($"lobed reverse={reverse} accepted={l.Ok} reason={l.Reason}; notch={n.Reason}; channel={c.Reason}");
        Assert.False(l.Ok);Assert.Empty(l.Candidate);
        Assert.False(n.Ok);Assert.Empty(n.Candidate);
        Assert.False(c.Ok);Assert.Empty(c.Candidate);
    }
    [Fact]
    public void CoordinateAndAllowanceBudgetsFailClosed()
    {
        GridPoint[] huge=[new(long.MinValue,0),new(long.MaxValue,0),new(long.MaxValue,100),new(long.MinValue,100)];
        GridPoint[] nearLimit=[new(long.MaxValue-100,0),new(long.MaxValue-1,0),new(long.MaxValue-1,99),new(long.MaxValue-100,99)];
        Assert.False(Create(huge).Ok);Assert.False(Create(nearLimit).Ok);
        Assert.False(Create([new(0,0),new(100,0),new(100,100),new(0,100)],long.MaxValue).Ok);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MinimizedInflectionIsStillSubjectToExactBoundaryProof(bool reverse)
    {
        GridPoint[] source=[new(607106781,607106781),new(596042581,610854176),
            new(585115305,614565823),new(569055029,567215348),new(591046505,559756306)];
        if(reverse)Array.Reverse(source);var result=Create(source);
        log.WriteLine($"minimized reverse={reverse} accepted={result.Ok}; reason={result.Reason}");
        Assert.False(result.Ok);Assert.Empty(result.Candidate);
        Assert.Contains("Exact indexed boundary",result.Reason);
    }
}
