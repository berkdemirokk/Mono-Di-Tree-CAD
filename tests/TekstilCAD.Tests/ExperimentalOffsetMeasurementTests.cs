using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

public sealed class ExperimentalOffsetMeasurementTests(ITestOutputHelper log)
{
    private static PatternPiece Lobed()
    {
        var m=typeof(SmoothAllowanceRegressionTests).GetMethod("Radial",BindingFlags.NonPublic|BindingFlags.Static)!;
        return (PatternPiece)m.Invoke(null,[512,true])!;
    }
    private static ExperimentalOffsetMeasurement.Result Evaluate(PatternPiece p,double band,
        params ExperimentalOffsetMeasurement.Feature[] features)=>
        ExperimentalOffsetMeasurement.Evaluate(p,1,.00001,.00002,.00002,band,features);
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LobedLineBandIsExplicitWhileFeatureStatusRemainsUnreviewed(bool reverse)
    {
        var source=Lobed();if(reverse)source.Points.Reverse();var before=source.Points.ToArray();
        Assert.Throws<OffsetLocalDetailUnverifiedException>(()=>PatternTools.Offset(source,1));
        var result=Evaluate(source,.00001);
        log.WriteLine($"reverse={reverse}; evaluated={result.Evaluated}; max measured line deviation≈{result.ObservedApproximateMaxLineDeviationMm:G17} mm; {result.Reason}");
        Assert.True(result.Evaluated,result.Reason);Assert.Equal(512,result.Preview.Count);Assert.Equal(0,result.SelectedFeatures);
        Assert.True(result.NeedsReview);Assert.False(result.ProductionApproved);Assert.InRange(result.ObservedApproximateMaxLineDeviationMm,0,.00001);
        Assert.False(Evaluate(source,.000000000001).Evaluated);Assert.Equal(before,source.Points);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitSelectedNotchTolerancesAreReportedButDoNotCertifyOtherFeatures(bool reverse)
    {
        var p=PatternTools.InsertNotch(Lobed(),146,.5,6,.5);int a=147,w=148,b=149;
        if(reverse){p.Points.Reverse();(a,w,b)=(p.Points.Count-1-b,p.Points.Count-1-w,p.Points.Count-1-a);}
        var strict=new ExperimentalOffsetMeasurement.Feature(a,w,b,.1,.01);
        var permissive=new ExperimentalOffsetMeasurement.Feature(a,w,b,2,2);
        var failed=Evaluate(p,.00001,strict);var passed=Evaluate(p,.00001,permissive);
        Assert.True(failed.Evaluated,failed.Reason);Assert.Equal(1,failed.FailedFeatures);Assert.Equal(1,failed.SelectedFeatures);
        Assert.True(passed.Evaluated,passed.Reason);Assert.Equal(0,passed.FailedFeatures);
        Assert.True(failed.NeedsReview);Assert.True(passed.NeedsReview);Assert.False(passed.ProductionApproved);
        Assert.Contains("feature discovery",passed.Reason);
    }
    [Fact]
    public void MissingBudgetsSmallNotchesMarkedPiecesAndClosedChannelsAreNotPromoted()
    {
        var p=Lobed();Assert.False(Evaluate(p,double.NaN).Evaluated);
        p.Notches=[new NotchMark{VertexIndex=0,Position=p.Points[0]}];Assert.False(Evaluate(p,.00001).Evaluated);
        var notch=new PatternPiece{Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(50.000002,100),
            new(50.000002,99.999999),new(50,99.999999),new(50,100),new(0,100)]};
        Assert.False(Evaluate(notch,.00001).Evaluated);
        var channel=new PatternPiece{Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(52,100),new(52,20),new(48,20),new(48,100),new(0,100)]};
        var r=ExperimentalOffsetMeasurement.Evaluate(channel,5,.00001,.00002,.00002,.00001,[]);
        Assert.False(r.Evaluated);Assert.Empty(r.Preview);Assert.False(r.ProductionApproved);
    }
    [Fact]
    public void LegacyQuantizationMismatchIsNotHiddenAtLargeOrigin()
    {
        var source=Lobed();source.Points=source.Points.Select(p=>new Point2(p.X+50_000_000,p.Y+50_000_000)).ToList();
        var result=Evaluate(source,.00001);Assert.False(result.Evaluated);Assert.Empty(result.Preview);
        Assert.Contains("not the exact nearest",result.Reason);
    }
}
