using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Integration evidence on isolated candidates, never a production approval.</summary>
public sealed class OffsetIntegratedCandidateEvidenceTests
{
    private static PatternPiece Lobed()
    {
        var method=typeof(SmoothAllowanceRegressionTests).GetMethod("Radial",BindingFlags.NonPublic|BindingFlags.Static)!;
        return (PatternPiece)method.Invoke(null,[512,true])!;
    }
    private static GridPoint[] ExactGrid(IReadOnlyList<Point2> source)=>source.Select(p=>
    {
        Assert.True(ExperimentalExactGridRounding.TryRound(p.X,out var x));
        Assert.True(ExperimentalExactGridRounding.TryRound(p.Y,out var y));return new GridPoint(x,y);
    }).ToArray();
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealLobedFailureHasBoundedCandidateButNoFeatureAuthorization(bool reverse)
    {
        var p=Lobed();if(reverse)p.Points.Reverse();var before=p.Points.ToArray();
        Assert.Throws<OffsetLocalDetailUnverifiedException>(()=>PatternTools.Offset(p,1));
        var proof=ExperimentalExactGridRoundingTests.Evaluate(p,1);Assert.True(proof.Ok,proof.Reason);
        var candidate=proof.Candidate.Select(q=>new Point2(q.X/1_000_000d,q.Y/1_000_000d)).ToArray();
        Assert.True(ExperimentalSourceMiterIntervalBound.TryVerifyRequested(before,candidate,1,.00001,out var bound,out var reason),reason);
        Assert.InRange(bound,0,.00001);Geometry.ValidateContour(candidate);
        var grid=ExactGrid(before);
        Assert.True(ExperimentalFiniteSegmentBound.VerifyIdealGridMiter(grid,proof.Candidate.ToArray(),1_000_000,2,1,out reason),reason);
        Assert.False(ExperimentalExplicitFeatureGate.Evaluate(p,1,[],[]).ProductionApproved);
        var fallback=ExperimentalOffsetFallbackPrototype.TryOffset(p,1);
        Assert.Null(fallback.Piece);Assert.True(fallback.NumericalChecksPassed,fallback.Reason);
        Assert.Equal(before,p.Points);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundedSimpleLobeWithVStillNeedsUserFeatureRequirements(bool reverse)
    {
        var p=PatternTools.InsertNotch(Lobed(),146,.5,6,.5);int a=147,w=148,b=149;
        if(reverse){p.Points.Reverse();(a,w,b)=(p.Points.Count-1-b,p.Points.Count-1-w,p.Points.Count-1-a);}
        Assert.Throws<OffsetLocalDetailUnverifiedException>(()=>PatternTools.Offset(p,1));
        var proof=ExperimentalExactGridRoundingTests.Evaluate(p,1);Assert.True(proof.Ok,proof.Reason);
        var candidate=proof.Candidate.Select(q=>new Point2(q.X/1_000_000d,q.Y/1_000_000d)).ToArray();
        Assert.True(ExperimentalSourceMiterIntervalBound.TryVerifyRequested(p.Points,candidate,1,.00001,out _,out var reason),reason);
        Geometry.ValidateContour(candidate);
        var limited=ExperimentalExplicitFeatureGate.Evaluate(p,1,[new(a,w,b,.1,.01)],[]);
        Assert.False(limited.SelectedChecksPassed);Assert.False(limited.ProductionApproved);
        var permissive=ExperimentalExplicitFeatureGate.Evaluate(p,1,[new(a,w,b,.2,.02)],[]);
        Assert.True(permissive.SelectedChecksPassed,permissive.Reason);Assert.False(permissive.AllFeaturesCertified);
        Assert.False(permissive.ProductionApproved);
    }
}
