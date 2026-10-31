using System.Reflection;
using Clipper2Lib;
using TekstilCAD.Patterns;
using Xunit.Abstractions;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public class OffsetPairedBoundaryDiagnosticTests(ITestOutputHelper output)
{
    private const long Grid=1_000_000;
    private static GridPoint[] Candidate(GridPoint[] source,long delta)
    {
        var type=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof",true)!;
        object?[] args=[source,delta,null,null,CancellationToken.None];
        Assert.True((bool)type.GetMethod("TryCreate",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,args)!, (string?)args[3]);
        return Assert.IsAssignableFrom<IReadOnlyList<GridPoint>>(args[2]).ToArray();
    }
    private static GridPoint[] Rectangle()=>[new(0,0),new(100*Grid,0),new(100*Grid,100*Grid),new(0,100*Grid)];

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ExactCorrespondenceAcceptsCyclicShiftAndBothSourceWindings(bool reverse)
    {
        var source=Rectangle();if(reverse)Array.Reverse(source);
        var expected=Candidate(source,Grid);var shifted=expected.Skip(2).Concat(expected.Take(2)).ToArray();
        var report=OffsetPairedBoundaryDiagnostic.Compare(source,Grid,shifted,0);
        Assert.True(report.CandidateConstructed,report.Reason);Assert.True(report.Matched,report.Reason);
        Assert.False(OffsetPairedBoundaryDiagnostic.Compare(source,Grid,shifted.Reverse().ToArray(),0).Matched);
    }
    [Fact]
    public void AddedOneGridNotchAndCrossedEdgesAreRejected()
    {
        var source=Rectangle();var candidate=Candidate(source,Grid);
        var notch=candidate.ToList();var a=notch[0];var b=notch[1];
        notch.Insert(1,new GridPoint((a.X+b.X)/2,(a.Y+b.Y)/2+1));
        Assert.False(OffsetPairedBoundaryDiagnostic.Compare(source,Grid,notch,64).Matched);
        var crossing=candidate.ToArray();(crossing[1],crossing[2])=(crossing[2],crossing[1]);
        Assert.False(OffsetPairedBoundaryDiagnostic.Compare(source,Grid,crossing,1_000*Grid).Matched);
        var changed=candidate.ToArray();changed[0]=new GridPoint(changed[0].X+1,changed[0].Y);
        Assert.False(OffsetPairedBoundaryDiagnostic.Compare(source,Grid,changed,0).Matched);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualLobedForwardResultIsObservedWithoutClaimingAcceptance(bool reverse)
    {
        var source=Enumerable.Range(0,512).Select(i=>
        {
            double angle=Math.Tau*i/512,r=1000+200*Math.Cos(3*angle);
            return new GridPoint((long)Math.Round(r*Math.Cos(angle)*Grid,MidpointRounding.AwayFromZero),
                (long)Math.Round(r*Math.Sin(angle)*Grid,MidpointRounding.AwayFromZero));
        }).ToArray();
        if(reverse)Array.Reverse(source);var original=source.ToArray();
        var paths=Clipper.InflatePaths(new Paths64 { new Path64(source.Select(p=>new Point64(p.X,p.Y))) },Grid,JoinType.Miter,EndType.Polygon,2.0);
        var actual=Assert.Single(paths).Select(p=>new GridPoint(p.X,p.Y)).ToArray();
        var report=OffsetPairedBoundaryDiagnostic.Compare(source,Grid,actual,8);
        output.WriteLine($"reverse={reverse}; candidate={report.CandidateCount}; actual={report.OutputCount}; matched={report.Matched}; {report.Reason}");
        Assert.True(report.CandidateConstructed,report.Reason);
        Assert.Equal(original,source);
        // Matching is observational: the supplied tolerance is not a validated manufacturing limit.
    }
    [Fact]
    public void CancellationDoesNotMutateInputs()
    {
        var source=Rectangle();var actual=Candidate(source,Grid);var original=source.ToArray();var expected=actual.ToArray();
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        Assert.Throws<OperationCanceledException>(()=>OffsetPairedBoundaryDiagnostic.Compare(source,Grid,actual,0,cancel.Token));
        Assert.Equal(original,source);Assert.Equal(expected,actual);
    }
}
