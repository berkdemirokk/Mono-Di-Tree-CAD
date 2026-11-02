using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

public sealed class ExperimentalSourceMiterIntervalBoundTests(ITestOutputHelper log)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnalyticSquareHasContinuousTwoWayBound(bool reverse)
    {
        Point2[] s=[new(0,0),new(100,0),new(100,100),new(0,100)];
        Point2[] c=[new(-1,-1),new(101,-1),new(101,101),new(-1,101)];
        if(reverse){Array.Reverse(s);Array.Reverse(c);}
        Assert.True(ExperimentalSourceMiterIntervalBound.TryVerify(s,c,1_000_000,.000001,out var upper,out var reason),reason);
        Assert.InRange(upper,0,.000001);
        c[0]=new Point2(c[0].X+.001,c[0].Y);
        Assert.False(ExperimentalSourceMiterIntervalBound.TryVerify(s,c,1_000_000,.0001,out _,out _));
    }
    [Theory]
    [InlineData(false,0d)]
    [InlineData(true,0d)]
    [InlineData(false,50000000d)]
    [InlineData(true,50000000d)]
    [InlineData(false,99998000d)]
    [InlineData(true,99998000d)]
    public void LobedSourceBinary64AndGridCandidateHaveAnExplicitIntervalBound(bool reverse,double origin)
    {
        var m=typeof(SmoothAllowanceRegressionTests).GetMethod("Radial",BindingFlags.NonPublic|BindingFlags.Static)!;
        var p=(PatternPiece)m.Invoke(null,[512,true])!;
        p.Points=p.Points.Select(q=>new Point2(q.X+origin,q.Y+origin)).ToList();if(reverse)p.Points.Reverse();
        var result=ExperimentalExactGridRoundingTests.Evaluate(p,1);Assert.True(result.Ok,result.Reason);
        var c=result.Candidate.Select(q=>new Point2(q.X/1_000_000d,q.Y/1_000_000d)).ToArray();
        for(int i=0;i<c.Length;i++)
        {
            Assert.Equal(result.Candidate[i].X,(long)Math.Round(c[i].X*1_000_000,MidpointRounding.AwayFromZero));
            Assert.Equal(result.Candidate[i].Y,(long)Math.Round(c[i].Y*1_000_000,MidpointRounding.AwayFromZero));
        }
        var ok=ExperimentalSourceMiterIntervalBound.TryVerify(p.Points,c,1_000_000,.001,out var bound,out var reason);
        log.WriteLine($"origin={origin}; reverse={reverse}; success={ok}; bound mm={bound:G17}; reason={reason}");
        Assert.True(ok,reason);Assert.InRange(bound,0,.001);Assert.Contains("feature",reason);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NotchedLobedContourCanMeetGeometryBoundWithoutCertifyingFeatureDimensions(bool reverse)
    {
        var m=typeof(SmoothAllowanceRegressionTests).GetMethod("Radial",BindingFlags.NonPublic|BindingFlags.Static)!;
        var p=PatternTools.InsertNotch((PatternPiece)m.Invoke(null,[512,true])!,146,.5,6,.5);
        if(reverse)p.Points.Reverse();var result=ExperimentalExactGridRoundingTests.Evaluate(p,1);Assert.True(result.Ok,result.Reason);
        var c=result.Candidate.Select(q=>new Point2(q.X/1_000_000d,q.Y/1_000_000d)).ToArray();
        Assert.True(ExperimentalSourceMiterIntervalBound.TryVerify(p.Points,c,1_000_000,.001,out _,out var reason),reason);
        Assert.Contains("feature",reason);
    }
    [Fact]
    public void NearBacktrackTinyEdgeNonfiniteBoundsAndCancellationFailClosed()
    {
        Point2[] nearReverse=[new(-1,0),new(0,0),new(-1,1e-12),new(-2,1)];
        Assert.False(ExperimentalSourceMiterIntervalBound.TryVerify(nearReverse,nearReverse,1_000_000,100,out _,out var backtrack));
        Assert.Contains("denominator",backtrack);
        Point2[] tiny=[new(0,0),new(1e-35,0),new(1,1),new(0,1)];
        Assert.False(ExperimentalSourceMiterIntervalBound.TryVerify(tiny,tiny,1,1,out _,out var length));
        Assert.Contains("length",length);
        Point2[] bad=[new(0,0),new(double.NaN,0),new(1,1)];
        Assert.False(ExperimentalSourceMiterIntervalBound.TryVerify(bad,bad,1,1,out _,out _));
        Point2[] huge=[new(0,0),new(100_000_001,0),new(0,1)];
        Assert.False(ExperimentalSourceMiterIntervalBound.TryVerify(huge,huge,1,1,out _,out _));
        Assert.False(ExperimentalSourceMiterIntervalBound.TryVerify(nearReverse,nearReverse,long.MaxValue,double.NaN,out _,out _));
        Point2[] square=[new(0,0),new(100,0),new(100,100),new(0,100)];
        Assert.False(ExperimentalSourceMiterIntervalBound.TryVerify(square,square,long.MaxValue,.001,out _,out _));
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        Assert.Throws<OperationCanceledException>(()=>ExperimentalSourceMiterIntervalBound.TryVerify(tiny,tiny,1,1,out _,out _,cancel.Token));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollapsedNotchAndChannelNeverProduceCandidateForBound(bool reverse)
    {
        var notch=new PatternPiece{Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(50.000002,100),
            new(50.000002,99.999999),new(50,99.999999),new(50,100),new(0,100)]};
        var channel=new PatternPiece{Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(52,100),
            new(52,20),new(48,20),new(48,100),new(0,100)]};
        if(reverse){notch.Points.Reverse();channel.Points.Reverse();}
        Assert.False(ExperimentalExactGridRoundingTests.Evaluate(notch,1).Ok);
        Assert.False(ExperimentalExactGridRoundingTests.Evaluate(channel,5).Ok);
    }
    [Fact]
    public void HalfGridBinaryCoordinatesAreComparedAsStoredNotAsDecimalIntent()
    {
        Point2[] s=[new(.0078125,0),new(100.0078125,0),new(100.0078125,100),new(.0078125,100)];
        Point2[] c=[new(-.9921875,-1),new(101.0078125,-1),new(101.0078125,101),new(-.9921875,101)];
        Assert.True(ExperimentalSourceMiterIntervalBound.TryVerify(s,c,1_000_000,.000001,out _,out var reason),reason);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RationalThreeFourFiveNormalsMatchIndependentAnalyticMiter(bool reverse)
    {
        Point2[] s=[new(0,0),new(30,40),new(-10,70),new(-40,30)];
        Point2[] c=[new(1,-7),new(37,41),new(-11,77),new(-47,29)];
        if(reverse){Array.Reverse(s);Array.Reverse(c);}
        Assert.True(ExperimentalSourceMiterIntervalBound.TryVerify(s,c,5_000_000,.000001,out var bound,out var reason),reason);
        Assert.InRange(bound,0,.000001);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequestedHalfGridAllowanceIsNotConfusedWithEffectiveGridAllowance(bool reverse)
    {
        const double requested=1.0000005;
        Point2[] s=[new(0,0),new(100,0),new(100,100),new(0,100)];
        Point2[] c=[new(-1.000001,-1.000001),new(101.000001,-1.000001),
            new(101.000001,101.000001),new(-1.000001,101.000001)];
        if(reverse){Array.Reverse(s);Array.Reverse(c);}
        Assert.True(ExperimentalSourceMiterIntervalBound.TryVerify(s,c,1_000_001,.000000001,out var effective,out var effectiveReason),effectiveReason);
        Assert.False(ExperimentalSourceMiterIntervalBound.TryVerifyRequested(s,c,requested,.0000007,out _,out _));
        Assert.True(ExperimentalSourceMiterIntervalBound.TryVerifyRequested(s,c,requested,.00000072,out var requestedBound,out var reason),reason);
        Assert.True(requestedBound>effective);Assert.InRange(requestedBound,.0000007,.00000072);
        log.WriteLine($"reverse={reverse} effective bound={effective:G17} mm, requested bound={requestedBound:G17} mm");
    }
    [Theory]
    [InlineData(false,0d)]
    [InlineData(true,0d)]
    [InlineData(false,50000000d)]
    [InlineData(true,50000000d)]
    public void ActualLobedCandidateIsBoundedAgainstRequestedBinary64Allowance(bool reverse,double origin)
    {
        var m=typeof(SmoothAllowanceRegressionTests).GetMethod("Radial",BindingFlags.NonPublic|BindingFlags.Static)!;
        var p=(PatternPiece)m.Invoke(null,[512,true])!;
        p.Points=p.Points.Select(q=>new Point2(q.X+origin,q.Y+origin)).ToList();if(reverse)p.Points.Reverse();
        const double requested=1.0000005;
        var result=ExperimentalExactGridRoundingTests.Evaluate(p,requested);Assert.True(result.Ok,result.Reason);
        var c=result.Candidate.Select(q=>new Point2(q.X/1_000_000d,q.Y/1_000_000d)).ToArray();
        Assert.True(ExperimentalSourceMiterIntervalBound.TryVerifyRequested(p.Points,c,requested,.00001,out var bound,out var reason),reason);
        log.WriteLine($"origin={origin}; reverse={reverse}; requested={requested:G17}; upper={bound:G17} mm");
        Assert.InRange(bound,0,.00001);
    }
    [Fact]
    public void AcuteCornerAmplifiesRequestedAllowanceRounding()
    {
        const double requested=1.0000005,effective=1.000001;
        Point2[] s=[new(0,0),new(100,0),new(0,10)];
        var c=new Point2[s.Length];
        for(int i=0;i<s.Length;i++)
        {
            var a=s[(i+s.Length-1)%s.Length];var b=s[i];var d=s[(i+1)%s.Length];
            var u=b-a;var v=d-b;double lu=Geometry.Distance(a,b),lv=Geometry.Distance(b,d);
            var n1=new Point2(u.Y/lu,-u.X/lu);var n2=new Point2(v.Y/lv,-v.X/lv);
            double scale=effective/(1+n1.X*n2.X+n1.Y*n2.Y);
            c[i]=b+(n1+n2)*scale;
        }
        Assert.False(ExperimentalSourceMiterIntervalBound.TryVerifyRequested(s,c,requested,.000002,out _,out _));
        Assert.True(ExperimentalSourceMiterIntervalBound.TryVerifyRequested(s,c,requested,.0001,out var bound,out var reason),reason);
        Assert.InRange(bound,.000009,.00002);
    }
}
