using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class ExperimentalFiniteSegmentBoundTests
{
    private const long U=1_000_000;
    private static GridPoint[] Ideal(GridPoint[] source,long delta)
    {
        var method=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof",true)!
            .GetMethod("TryCreate",BindingFlags.NonPublic|BindingFlags.Static)!;
        object?[] args=[source,delta,null,null,CancellationToken.None];Assert.True((bool)method.Invoke(null,args)!,(string?)args[3]);
        return ((IReadOnlyList<GridPoint>)args[2]!).ToArray();
    }
    [Fact]
    public void WholeSegmentsAreBoundedNotJustMatchingEndpoints()
    {
        GridPoint[] line=[new(0,0),new(100,0)];
        GridPoint[] detour=[new(0,0),new(0,100),new(100,100),new(100,0)];
        Assert.Contains(line[0],detour);Assert.Contains(line[1],detour);
        Assert.False(ExperimentalFiniteSegmentBound.Verify(line,detour,false,1,16,out _));
        Assert.True(ExperimentalFiniteSegmentBound.Verify(line,detour,false,100,16,out var reason),reason);
    }
    [Fact]
    public void NearestSegmentCanChangeAtAJunctionButProofMustCoverWholeParts()
    {
        GridPoint[] longEdge=[new(0,0),new(100,0)];
        GridPoint[] split=[new(0,1),new(50,1),new(100,1)];
        Assert.False(ExperimentalFiniteSegmentBound.Verify(longEdge,split,false,1,1,out _));
        Assert.True(ExperimentalFiniteSegmentBound.Verify(longEdge,split,false,1,2,out var reason),reason);
        Assert.False(ExperimentalFiniteSegmentBound.Verify(longEdge,split,false,0,2,out _));
    }
    [Fact]
    public void ClosedPathWraparoundAndInvalidLengthsAreChecked()
    {
        GridPoint[] square=[new(0,0),new(100,0),new(100,100),new(0,100)];
        GridPoint[] rotated=[new(100,100),new(0,100),new(0,0),new(100,0)];
        Assert.True(ExperimentalFiniteSegmentBound.Verify(square,rotated,true,0,1,out var reason),reason);
        GridPoint[] broken=[new(0,0),new(100,0),new(100,100),new(20,100)];
        Assert.False(ExperimentalFiniteSegmentBound.Verify(square,broken,true,1,16,out _));
        GridPoint[] zero=[new(0,0),new(0,0),new(1,0)];
        Assert.False(ExperimentalFiniteSegmentBound.Verify(zero,square,true,5,1,out _));
    }
    [Fact]
    public void ZeroSetDistanceDoesNotProveSingleTraversalOrTopology()
    {
        GridPoint[] square=[new(0,0),new(100,0),new(100,100),new(0,100)];
        var twice=square.Concat(square).ToArray();
        Assert.True(ExperimentalFiniteSegmentBound.Verify(square,twice,true,0,1,out var reason),reason);
        Assert.Contains("topology",reason);
        Assert.Throws<ArgumentException>(()=>Geometry.ValidateContour(twice.Select(p=>new Point2(p.X,p.Y)).ToArray()));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualLobedGridIdealAndIndexedBoundaryHaveTwoGridFiniteBound(bool reverse)
    {
        var m=typeof(SmoothAllowanceRegressionTests).GetMethod("Radial",BindingFlags.NonPublic|BindingFlags.Static)!;
        var p=(PatternPiece)m.Invoke(null,[512,true])!;if(reverse)p.Points.Reverse();
        var s=p.Points.Select(x=>new GridPoint((long)Math.Round(x.X*U,MidpointRounding.AwayFromZero),
            (long)Math.Round(x.Y*U,MidpointRounding.AwayFromZero))).ToArray();
        var candidate=Ideal(s,U);
        Assert.True(ExperimentalFiniteSegmentBound.VerifyIdealGridMiter(s,candidate,U,2,1,out var reason),reason);
        Assert.Contains("source binary64",reason);
        Assert.False(ExperimentalFiniteSegmentBound.VerifyIdealGridMiter(s,candidate,U,1,1,out _));
        candidate[10]=new GridPoint(candidate[10].X+100,candidate[10].Y);
        Assert.False(ExperimentalFiniteSegmentBound.VerifyIdealGridMiter(s,candidate,U,2,1,out _));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LostOneGridNotchAndClosingChannelCannotProduceIndexedIdealEvidence(bool reverse)
    {
        GridPoint[] notch=[new(0,0),new(100*U,0),new(100*U,100*U),new(50*U+2,100*U),
            new(50*U+2,100*U-1),new(50*U,100*U-1),new(50*U,100*U),new(0,100*U)];
        GridPoint[] channel=[new(0,0),new(100*U,0),new(100*U,100*U),new(52*U,100*U),
            new(52*U,20*U),new(48*U,20*U),new(48*U,100*U),new(0,100*U)];
        GridPoint[] flat=[new(-U,-U),new(101*U,-U),new(101*U,101*U),new(-U,101*U)];
        if(reverse){Array.Reverse(notch);Array.Reverse(channel);Array.Reverse(flat);}
        Assert.False(ExperimentalFiniteSegmentBound.VerifyIdealGridMiter(notch,flat,U,2,1,out _));
        Assert.False(ExperimentalFiniteSegmentBound.VerifyIdealGridMiter(channel,flat,5*U,2,1,out _));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FiniteBoundAlsoMeasuresNotchedLobeButDoesNotCertifyNotchDimensions(bool reverse)
    {
        var m=typeof(SmoothAllowanceRegressionTests).GetMethod("Radial",BindingFlags.NonPublic|BindingFlags.Static)!;
        var p=PatternTools.InsertNotch((PatternPiece)m.Invoke(null,[512,true])!,146,.5,6,.5);
        if(reverse)p.Points.Reverse();
        var grid=p.Points.Select(x=>new GridPoint((long)Math.Round(x.X*U,MidpointRounding.AwayFromZero),
            (long)Math.Round(x.Y*U,MidpointRounding.AwayFromZero))).ToArray();
        var candidate=Ideal(grid,U);
        Assert.True(ExperimentalFiniteSegmentBound.VerifyIdealGridMiter(grid,candidate,U,2,1,out var reason),reason);
        Assert.Contains("features",reason);
    }
    [Fact]
    public void WorkBudgetAndCancellationDoNotApprove()
    {
        var input=Enumerable.Range(0,1024).Select(i=>new GridPoint(i,i*i)).ToArray();
        Assert.False(ExperimentalFiniteSegmentBound.Verify(input,input,true,0,2,out _));
        using var c=new CancellationTokenSource();c.Cancel();
        Assert.Throws<OperationCanceledException>(()=>ExperimentalFiniteSegmentBound.Verify(input,input,true,0,1,out _,c.Token));
    }
}
