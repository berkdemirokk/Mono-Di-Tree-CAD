using System.Reflection;
using Clipper2Lib;
using TekstilCAD.Patterns;
using Xunit.Abstractions;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Numerical fixture measurements; not a feature or production certificate.</summary>
public sealed class OffsetRefinedBudgetInvestigationTests(ITestOutputHelper log)
{
    private const long U=1_000_000;
    private static GridPoint[] Lobed(bool reverse)
    {
        var s=Enumerable.Range(0,512).Select(i=>
        {
            double a=Math.Tau*i/512,r=1000+200*Math.Cos(3*a);
            return new GridPoint((long)Math.Round(r*Math.Cos(a)*U,MidpointRounding.AwayFromZero),
                (long)Math.Round(r*Math.Sin(a)*U,MidpointRounding.AwayFromZero));
        }).ToArray();if(reverse)Array.Reverse(s);return s;
    }
    private static GridPoint[] Ideal(GridPoint[] s)
    {
        var m=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof",true)!
            .GetMethod("TryCreate",BindingFlags.Static|BindingFlags.NonPublic)!;
        object?[] args=[s,U,null,null,CancellationToken.None];Assert.True((bool)m.Invoke(null,args)!,(string?)args[3]);
        return ((IReadOnlyList<GridPoint>)args[2]!).ToArray();
    }
    private static GridPoint[] Refined(GridPoint[] s,long delta=U)
    {
        var origin=s[0];var path=new Path64(s.Select(p=>new Point64(checked((p.X-origin.X)*10),checked((p.Y-origin.Y)*10))));
        var output=Assert.Single(Clipper.InflatePaths(new Paths64{path},10*delta,JoinType.Miter,EndType.Polygon,2));
        static long Round(long x)=>x>=0?(x+5)/10:-((-x+5)/10);
        return output.Select(p=>new GridPoint(checked(Round(p.X)+origin.X),checked(Round(p.Y)+origin.Y))).ToArray();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeasureEveryIndexedLobedDifference(bool reverse)
    {
        var s=Lobed(reverse);var ideal=Ideal(s);var actual=Refined(s);Assert.Equal(ideal.Length,actual.Length);
        int start=Enumerable.Range(0,actual.Length).MinBy(i=>
            Math.Pow((double)actual[i].X-ideal[0].X,2)+Math.Pow((double)actual[i].Y-ideal[0].Y,2));
        var distances=Enumerable.Range(0,s.Length).Select(i=>
        {
            var p=ideal[i];var q=actual[(start+i)%actual.Length];
            return Math.Sqrt(Math.Pow((double)p.X-q.X,2)+Math.Pow((double)p.Y-q.Y,2));
        }).Order().ToArray();
        log.WriteLine($"reverse={reverse} vertices={s.Length} exact={distances.Count(d=>d==0)} <=1grid={distances.Count(d=>d<=1)} <=sqrt2grid={distances.Count(d=>d<=Math.Sqrt(2))} max_grid={distances[^1]:F9} max_mm={distances[^1]/U:G12} p50_mm={distances[distances.Length/2]/U:G12} p95_mm={distances[(int)(distances.Length*.95)]/U:G12}");
        Assert.InRange(distances[^1],124,125);Assert.Contains(distances,d=>d>0);
        var mapped=Enumerable.Range(0,s.Length).Select(i=>actual[(start+i)%actual.Length]).ToArray();
        Assert.False(Verify(s,[mapped],U,124).Ok);
        var check=Verify(s,[mapped],U,125);
        log.WriteLine($"budget=125 grid accepted={check.Ok}; reason={check.Reason}");
        Assert.True(check.Ok,check.Reason);Assert.Contains("not certified",check.Reason);
        (mapped[10],mapped[300])=(mapped[300],mapped[10]);
        Assert.False(Verify(s,[mapped],U,125).Ok);
    }
    private static (bool Ok,string Reason) Verify(GridPoint[] source,IReadOnlyList<GridPoint>[] candidates,long delta,long budget)
    {
        var m=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetBudgetEvidenceExperiment",true)!
            .GetMethod("TryVerify",BindingFlags.Static|BindingFlags.NonPublic)!;
        object?[] args=[source,candidates,delta,budget,126L,1_000_000L,1_000L,1L,null,CancellationToken.None];
        bool ok=(bool)m.Invoke(null,args)!;return (ok,(string)args[8]!);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameBudgetPipelineRejectsLostFeaturesAndInvalidContours(bool reverse)
    {
        GridPoint[] notch=[new(0,0),new(100*U,0),new(100*U,100*U),new(50*U+2,100*U),
            new(50*U+2,100*U-1),new(50*U,100*U-1),new(50*U,100*U),new(0,100*U)];
        GridPoint[] channel=[new(0,0),new(100*U,0),new(100*U,100*U),new(52*U,100*U),
            new(52*U,20*U),new(48*U,20*U),new(48*U,100*U),new(0,100*U)];
        GridPoint[] crossed=[new(0,0),new(100*U,100*U),new(0,100*U),new(100*U,0)];
        GridPoint[] large=[new(long.MinValue,0),new(long.MaxValue,0),new(long.MaxValue,100),new(long.MinValue,100)];
        GridPoint[] square=[new(0,0),new(100*U,0),new(100*U,100*U),new(0,100*U)];
        if(reverse){Array.Reverse(notch);Array.Reverse(channel);Array.Reverse(crossed);Array.Reverse(large);Array.Reverse(square);}
        var notchCandidate=Refined(notch);var channelCandidate=Refined(channel,5*U);
        Assert.Equal(4,notchCandidate.Length);Assert.Equal(4,channelCandidate.Length);
        var lost=Verify(notch,[notchCandidate],U,125);var closed=Verify(channel,[channelCandidate],5*U,125);
        var self=Verify(crossed,[crossed],U,125);var extreme=Verify(large,[large],U,125);
        var multi=Verify(square,[square,square],U,125);
        log.WriteLine($"reverse={reverse}; notch={lost.Reason}; channel={closed.Reason}; self={self.Reason}; extreme={extreme.Reason}; multi={multi.Reason}");
        Assert.False(lost.Ok);Assert.False(closed.Ok);Assert.False(self.Ok);Assert.False(extreme.Ok);Assert.False(multi.Ok);
    }
}
