using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

// Research-only composition. No candidate PatternPiece and no production gate.
internal sealed class IndexedMiterEvidenceSnapshot
{
    private readonly Guid id;
    private readonly Point2[] points;
    private IndexedMiterEvidenceSnapshot(PatternPiece source) { id=source.Id;points=source.Points.ToArray(); }
    internal static IndexedMiterEvidenceSnapshot Capture(PatternPiece source)
    {
        if(!source.Closed || source.Points.Count is <3 or >707 || source.Notches is not null || source.PhysicalFeatures is not null)
            throw new ArgumentException("Unsupported source or marked contour");
        Geometry.ValidateContour(source.Points);return new(source);
    }
    private bool Matches(PatternPiece source) => source.Closed && source.Id==id && source.Notches is null && source.PhysicalFeatures is null &&
        points.Length==source.Points.Count && points.Zip(source.Points).All(pair=>BitConverter.DoubleToInt64Bits(pair.First.X)==BitConverter.DoubleToInt64Bits(pair.Second.X) && BitConverter.DoubleToInt64Bits(pair.First.Y)==BitConverter.DoubleToInt64Bits(pair.Second.Y));
    internal (bool Passed,string Reason) Verify(PatternPiece source,double allowance,double bound)
    {
        if(!Matches(source))return (false,"Source identity or contour changed");
        var privateCopy=source.Clone();
        var candidate=ExperimentalExactGridRoundingTests.Evaluate(privateCopy,allowance);
        if(!candidate.Ok)return (false,"Candidate evidence unresolved: "+candidate.Reason);
        var c=candidate.Candidate.Select(p=>new Point2(p.X/1_000_000d,p.Y/1_000_000d)).ToArray();
        if(c.Length!=points.Length)return (false,"Index count changed");
        try { Geometry.ValidateContour(c); }
        catch(ArgumentException) { return (false,"Candidate topology rejected"); }
        if(Math.Sign(Geometry.SignedArea(points))!=Math.Sign(Geometry.SignedArea(c)))return (false,"Orientation changed");
        if(!ExperimentalSourceMiterIntervalBound.TryVerifyRequested(points,c,allowance,bound,out _,out var reason))return (false,"Interval unresolved: "+reason);
        if(!Matches(source))return (false,"Source changed during evaluation");
        return (true,"Indexed segment interval, winding and simple contour checked; physical features remain unverified; production rejected");
    }
}

public sealed class IndexedMiterEvidenceCompositionTests
{
    private static PatternPiece Lobed()=> (PatternPiece)typeof(SmoothAllowanceRegressionTests).GetMethod("Radial",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[512,true])!;
    [Theory][InlineData(false)][InlineData(true)]
    public void BothWindingsBindSourceToIndexedContinuousEvidenceButNeverAuthorizeProduction(bool reverse)
    {
        var source=Lobed();if(reverse)source.Points.Reverse();var snapshot=IndexedMiterEvidenceSnapshot.Capture(source);
        var proof=snapshot.Verify(source,1,.001);Assert.True(proof.Passed,proof.Reason);Assert.Contains("production rejected",proof.Reason);
        var denied=ExperimentalOffsetFallbackPrototype.TryOffset(source,1);Assert.Null(denied.Piece);Assert.NotNull(denied.OriginalFailure);
        var other=source.Clone();other.Id=Guid.NewGuid();Assert.False(snapshot.Verify(other,1,.001).Passed);
        other=source.Clone();other.Points[0]=new Point2(Math.BitIncrement(other.Points[0].X),other.Points[0].Y);Assert.False(snapshot.Verify(other,1,.001).Passed);
        other=source.Clone();other.Points.Reverse();Assert.False(snapshot.Verify(other,1,.001).Passed);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void ShallowUnmarkedVCanPassGeometricEvidenceButDoesNotProduceApprovedPiece(bool reverse)
    {
        var source=PatternTools.InsertNotch(Lobed(),146,.5,6,.05);if(reverse)source.Points.Reverse();
        var snapshot=IndexedMiterEvidenceSnapshot.Capture(source);var evidence=snapshot.Verify(source,1,.001);
        Assert.True(evidence.Passed,evidence.Reason);Assert.Contains("physical features remain unverified",evidence.Reason);
        var denied=ExperimentalOffsetFallbackPrototype.TryOffset(source,1);Assert.Null(denied.Piece);Assert.NotNull(denied.OriginalFailure);
    }
    [Fact] public void MarkedAndClosedNarrowChannelDoNotProduceEvidence()
    {
        var marked=Lobed();marked.PhysicalFeatures=PhysicalContourFeatures.Create(marked,[]);
        Assert.Throws<ArgumentException>(()=>IndexedMiterEvidenceSnapshot.Capture(marked));
        var channel=new PatternPiece { Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(52,100),new(52,20),new(48,20),new(48,100),new(0,100)] };
        var snapshot=IndexedMiterEvidenceSnapshot.Capture(channel);Assert.False(snapshot.Verify(channel,5,.001).Passed);
    }
}
