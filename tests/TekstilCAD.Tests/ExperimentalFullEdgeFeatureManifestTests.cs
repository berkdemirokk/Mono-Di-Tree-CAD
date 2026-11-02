using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Tests.ExperimentalFullEdgeFeatureManifest;

namespace TekstilCAD.Tests;

public sealed class ExperimentalFullEdgeFeatureManifestTests
{
    private static PatternPiece Lobed()
    {
        var m=typeof(SmoothAllowanceRegressionTests).GetMethod("Radial",BindingFlags.NonPublic|BindingFlags.Static)!;
        return (PatternPiece)m.Invoke(null,[512,true])!;
    }
    private static Manifest Create(PatternPiece p,V[]? vs=null,Channel[]? channels=null)
    {
        vs??=[];channels??=[];int n=p.Points.Count;
        var labels=Enumerable.Range(0,n).Select(_=>new List<string>()).ToArray();
        foreach(var v in vs)for(int k=0;k<(v.End-v.Start+n)%n;k++)labels[(v.Start+k)%n].Add(v.Id);
        foreach(var c in channels){labels[c.FirstEdge].Add(c.Id);labels[c.SecondEdge].Add(c.Id);}
        var edges=Enumerable.Range(0,n).Select(i=>new Edge(i,true,labels[i])).ToArray();
        return new(p.Id,ExperimentalPhysicalNotchValidation.Fingerprint(p),"test-reviewer",edges,vs,channels,.00001);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealLobeWithoutFeatureDefinitionsFailsClosed(bool reverse)
    {
        var p=Lobed();if(reverse)p.Points.Reverse();var before=p.Points.ToArray();
        Assert.Throws<OffsetLocalDetailUnverifiedException>(()=>PatternTools.Offset(p,1));
        Assert.False(Evaluate(p,1,null).Evaluated);
        var allReviewedButEmpty=Evaluate(p,1,Create(p));
        Assert.False(allReviewedButEmpty.Evaluated);Assert.Empty(allReviewedButEmpty.Preview);
        Assert.Contains("absence cannot be independently established",allReviewedButEmpty.Reason);
        Assert.False(allReviewedButEmpty.SemanticCompletenessCertified);Assert.False(allReviewedButEmpty.ProductionApproved);
        Assert.Equal(before,p.Points);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeclaredVRequiresSourceCoverageAndExplicitDimensionalBudgets(bool reverse)
    {
        var p=PatternTools.InsertNotch(Lobed(),146,.5,6,.5);int a=147,w=148,b=149;
        if(reverse){p.Points.Reverse();(a,w,b)=(p.Points.Count-1-b,p.Points.Count-1-w,p.Points.Count-1-a);}
        var v=new V("V1",a,w,b,.2,.02);var valid=Create(p,[v]);
        var ok=Evaluate(p,1,valid);Assert.True(ok.Evaluated,ok.Reason);Assert.Equal(p.Points.Count,ok.ReviewedEdges);
        Assert.Equal(1,ok.DeclaredFeatures);Assert.False(ok.SemanticCompletenessCertified);Assert.False(ok.ProductionApproved);
        var strict=Create(p,[v with {WidthToleranceMm=.1}]);Assert.False(Evaluate(p,1,strict).Evaluated);
        var strictDepth=Create(p,[v with {DepthToleranceMm=.01}]);Assert.False(Evaluate(p,1,strictDepth).Evaluated);
        var altered=valid.Edges.ToArray();altered[a]=altered[a] with {FeatureIds=Array.Empty<string>()};
        Assert.False(Evaluate(p,1,valid with {Edges=altered}).Evaluated);
    }
    [Fact]
    public void MissingUnreviewedDuplicateStaleAndInvalidDeclarationsFailBeforeCandidate()
    {
        var p=Lobed();var v=new V("f",0,1,2,1,1);var valid=Create(p,[v]);
        Assert.False(Evaluate(p,1,valid with {Edges=valid.Edges.Skip(1).ToArray()}).Evaluated);
        var unreviewed=valid.Edges.ToArray();unreviewed[0]=unreviewed[0] with {Reviewed=false};
        Assert.False(Evaluate(p,1,valid with {Edges=unreviewed}).Evaluated);
        var duplicate=valid.Edges.ToArray();duplicate[1]=duplicate[0];Assert.False(Evaluate(p,1,valid with {Edges=duplicate}).Evaluated);
        Assert.False(Evaluate(p,1,valid with {Reviewer=""}).Evaluated);
        Assert.False(Evaluate(p,1,valid with {MaxMiterDeviationMm=double.NaN}).Evaluated);
        Assert.False(Evaluate(p,1,valid with {SnapshotHash="old"}).Evaluated);
        Assert.False(Evaluate(p,1,valid with {Notches=[v,v]}).Evaluated);
        var changed=p.Clone();changed.Points[0]=new Point2(changed.Points[0].X+.001,changed.Points[0].Y);
        Assert.False(Evaluate(changed,1,valid).Evaluated);
    }
    [Fact]
    public void ReviewDeclarationCannotDetectAnOmittedPhysicalFeature()
    {
        var p=PatternTools.InsertNotch(Lobed(),146,.5,6,.5);
        // Reviewer declares another mathematically valid triple while omitting
        // the actual V at 147..149. Structural coverage can still be complete.
        var result=Evaluate(p,1,Create(p,[new V("other",0,1,2,2,2)]));
        Assert.True(result.Evaluated,result.Reason);Assert.Equal(p.Points.Count,result.ReviewedEdges);
        Assert.False(result.SemanticCompletenessCertified);Assert.False(result.ProductionApproved);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LostGridNotchAndClosedChannelRetainExistingRejection(bool reverse)
    {
        var notch=new PatternPiece{Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(50.000002,100),
            new(50.000002,99.999999),new(50,99.999999),new(50,100),new(0,100)]};
        var channel=new PatternPiece{Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(52,100),new(52,20),new(48,20),new(48,100),new(0,100)]};
        if(reverse){notch.Points.Reverse();channel.Points.Reverse();}
        var n=Evaluate(notch,1,Create(notch,[new V("v",0,1,2,1,1)]));
        var c=Evaluate(channel,5,Create(channel,channels:[new Channel("c",3,5,.001)]));
        Assert.False(n.Evaluated);Assert.False(c.Evaluated);Assert.Empty(n.Preview);Assert.Empty(c.Preview);
    }
}
