using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class ExperimentalExplicitFeatureGateTests
{
    private static PatternPiece Lobed()
    {
        var m=typeof(SmoothAllowanceRegressionTests).GetMethod("Radial",BindingFlags.NonPublic|BindingFlags.Static)!;
        return (PatternPiece)m.Invoke(null,[512,true])!;
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingFeatureContractCannotPromoteEitherOriginalLobe(bool reverse)
    {
        var p=Lobed();if(reverse)p.Points.Reverse();
        Assert.Throws<OffsetLocalDetailUnverifiedException>(()=>PatternTools.Offset(p,1));
        var r=ExperimentalExplicitFeatureGate.Evaluate(p,1,[],[]);
        Assert.False(r.SelectedChecksPassed);Assert.False(r.AllFeaturesCertified);Assert.False(r.ProductionApproved);
        Assert.Contains("unknown",r.Reason);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedVWidthAndDepthUseSeparateExplicitBudgets(bool reverse)
    {
        var p=PatternTools.InsertNotch(Lobed(),146,.5,6,.5);int a=147,w=148,b=149;
        if(reverse){p.Points.Reverse();(a,w,b)=(p.Points.Count-1-b,p.Points.Count-1-w,p.Points.Count-1-a);}
        ExperimentalExplicitFeatureGate.V Feature(double width,double depth)=>new(a,w,b,width,depth);
        var strictWidth=ExperimentalExplicitFeatureGate.Evaluate(p,1,[Feature(.1,2)],[]);
        var strictDepth=ExperimentalExplicitFeatureGate.Evaluate(p,1,[Feature(2,.01)],[]);
        var tolerant=ExperimentalExplicitFeatureGate.Evaluate(p,1,[Feature(.2,.02)],[]);
        Assert.False(strictWidth.SelectedChecksPassed);Assert.Contains("width",strictWidth.Reason);
        Assert.False(strictDepth.SelectedChecksPassed);Assert.Contains("depth",strictDepth.Reason);
        Assert.True(tolerant.SelectedChecksPassed,tolerant.Reason);Assert.Equal(1,tolerant.CheckedFeatures);
        Assert.False(tolerant.AllFeaturesCertified);Assert.False(tolerant.ProductionApproved);
    }
    [Fact]
    public void ExactChannelDistanceRejectsIntersectionAndHonorsBoundary()
    {
        Point2 a=new(0,0),b=new(0,10),c=new(5,2),d=new(5,8);
        Assert.True(ExperimentalExplicitFeatureGate.SegmentsAtLeast(a,b,c,d,5));
        Assert.False(ExperimentalExplicitFeatureGate.SegmentsAtLeast(a,b,c,d,Math.BitIncrement(5)));
        Assert.False(ExperimentalExplicitFeatureGate.SegmentsAtLeast(a,b,new(-1,5),new(1,5),.001));
        Assert.False(ExperimentalExplicitFeatureGate.SegmentsAtLeast(a,a,c,d,1));
        Assert.False(ExperimentalExplicitFeatureGate.SegmentsAtLeast(a,b,c,d,double.NaN));
        double origin=50_000_000;
        Assert.True(ExperimentalExplicitFeatureGate.SegmentsAtLeast(new(origin,0),new(origin,10),new(origin+5,2),new(origin+5,8),5));
    }
    [Fact]
    public void SelectedEdgePairIsCheckedOnTheActualIndexedCandidate()
    {
        var p=Lobed();
        var possible=ExperimentalExplicitFeatureGate.Evaluate(p,1,[],[new(0,256,1)]);
        var impossible=ExperimentalExplicitFeatureGate.Evaluate(p,1,[],[new(0,256,20_000)]);
        Assert.True(possible.SelectedChecksPassed,possible.Reason);Assert.False(possible.ProductionApproved);
        Assert.False(impossible.SelectedChecksPassed);Assert.Contains("minimum",impossible.Reason);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedChannelAndOneGridNotchNeverPassAnExplicitContract(bool reverse)
    {
        var notch=new PatternPiece{Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(50.000002,100),
            new(50.000002,99.999999),new(50,99.999999),new(50,100),new(0,100)]};
        var channel=new PatternPiece{Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(52,100),new(52,20),new(48,20),new(48,100),new(0,100)]};
        if(reverse){notch.Points.Reverse();channel.Points.Reverse();}
        var n=ExperimentalExplicitFeatureGate.Evaluate(notch,1,[new(0,1,2,1,1)],[]);
        var c=ExperimentalExplicitFeatureGate.Evaluate(channel,5,[],[new(3,5,.001)]);
        Assert.False(n.SelectedChecksPassed);Assert.False(c.SelectedChecksPassed);
        Assert.False(n.ProductionApproved);Assert.False(c.ProductionApproved);
    }
    [Fact]
    public void BadFeatureIdentityAndDuplicatedChannelsAreRejected()
    {
        var p=Lobed();
        Assert.False(ExperimentalExplicitFeatureGate.Evaluate(p,1,[new(0,2,1,1,1)],[]).SelectedChecksPassed);
        Assert.False(ExperimentalExplicitFeatureGate.Evaluate(p,1,[],[new(0,1,1)]).SelectedChecksPassed);
        Assert.False(ExperimentalExplicitFeatureGate.Evaluate(p,1,[],[new(1,3,1),new(3,1,1)]).SelectedChecksPassed);
        Assert.False(ExperimentalExplicitFeatureGate.Evaluate(p,1,[new(0,1,2,double.NaN,1)],[]).SelectedChecksPassed);
    }
}
