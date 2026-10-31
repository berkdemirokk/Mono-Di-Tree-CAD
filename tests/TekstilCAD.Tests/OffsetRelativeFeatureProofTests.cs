using System.Reflection;
using System.Runtime.ExceptionServices;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Explicit local depth ranges are not a proof of complete feature preservation.</summary>
public sealed class OffsetRelativeFeatureProofTests
{
    private readonly record struct Target(int Index, (long N, long D) SourceLow, (long N, long D) SourceHigh,
        (long N, long D) CandidateLow, (long N, long D) CandidateHigh);

    private static bool Validate(IReadOnlyList<GridPoint> source, IReadOnlyList<GridPoint> candidate,
        IReadOnlyList<Target> targets, CancellationToken cancellation = default)
    {
        var assembly = typeof(PatternTools).Assembly;
        var rangeType = assembly.GetType("TekstilCAD.Patterns.OffsetDepthRange", true)!;
        var targetType = assembly.GetType("TekstilCAD.Patterns.OffsetRelativeFeatureTarget", true)!;
        var proofType = assembly.GetType("TekstilCAD.Patterns.OffsetRelativeFeatureProof", true)!;
        object Range((long N,long D) lo, (long N,long D) hi) =>
            Activator.CreateInstance(rangeType, lo.N, lo.D, hi.N, hi.D)!;
        var targetArray = Array.CreateInstance(targetType, targets.Count);
        for (var i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            targetArray.SetValue(Activator.CreateInstance(targetType, t.Index,
                Range(t.SourceLow,t.SourceHigh), Range(t.CandidateLow,t.CandidateHigh)), i);
        }
        object?[] args = [source, candidate, targetArray, null, cancellation];
        try { return (bool)proofType.GetMethod("TryValidate", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static GridPoint[] Notch(long bottom = 16, long origin = 0) =>
    [new(origin,origin), new(origin+20,origin), new(origin+20,origin+20),
     new(origin+15,origin+20), new(origin+15,origin+bottom), new(origin+12,origin+bottom),
     new(origin+12,origin+20), new(origin,origin+20)];

    private static Target Exact(int i, long n, long d) => new(i, (n,d), (n,d), (n,d), (n,d));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactRationalBoundaryIsAcceptedInEitherWinding(bool reverse)
    {
        var source = Notch(); var candidate = Notch(); var index = 4;
        if (reverse) { Array.Reverse(source); Array.Reverse(candidate); index = source.Length - 1 - index; }
        var beforeSource=source.ToArray(); var beforeCandidate=candidate.ToArray();
        // Chord = 5; cross = 12, so perpendicular depth is exactly 12/5.
        Assert.True(Validate(source, candidate, [Exact(index,12,5)]));
        Assert.Equal(beforeSource,source); Assert.Equal(beforeCandidate,candidate);
    }

    [Fact]
    public void IndependentTargetRangesAcceptExpectedChangeRatherThanAssumeEqualDepth()
    {
        var source=Notch(); var candidate=Notch(18);
        // Candidate depth is 6/sqrt(13), which lies between 3/2 and 5/3.
        Assert.True(Validate(source,candidate,[new(4,(12,5),(12,5),(3,2),(5,3))]));
        Assert.False(Validate(source,candidate,[new(4,(12,5),(12,5),(5,3),(7,4))]));
    }

    [Fact]
    public void ExplicitTargetRejectsLargeLossThatIndependentMinimumWouldAccept()
    {
        var source = new[] { new GridPoint(0,0),new(100,0),new(100,100),new(80,100),
            new(80,70),new(65,70),new(65,100),new(0,100) };
        var candidate = source.ToArray(); candidate[4]=new(80,97); candidate[5]=new(65,97);
        // Source is 450/sqrt(1125) > 13; candidate is 45/sqrt(234) < 3.
        var target=new Target(4,(13,1),(14,1),(13,1),(14,1));
        Assert.False(Validate(source,candidate,[target]));
    }

    [Fact]
    public void OnePassingFeatureCannotMaskAnotherOutsideItsOwnRange()
    {
        var source=new[] { new GridPoint(0,0),new(100,0),new(100,100),new(80,100),new(80,70),
            new(65,70),new(65,100),new(45,100),new(45,90),new(30,90),new(30,100),new(0,100) };
        var candidate=source.ToArray(); candidate[8]=new(45,99); candidate[9]=new(30,99);
        var first=new Target(4,(13,1),(14,1),(13,1),(14,1));
        // Second source depth is 150/sqrt(325), between 8 and 9.
        var second=new Target(8,(8,1),(9,1),(8,1),(9,1));
        Assert.False(Validate(source,candidate,[first,second]));
    }

    [Theory]
    [InlineData(9000000000000000000L)]
    [InlineData(-9000000000000000000L)]
    public void LargeOriginDoesNotChangeExactLocalMeasurement(long origin)
    {
        var source=Notch(origin:origin);
        Assert.True(Validate(source,source.ToArray(),[Exact(4,12,5)]));
    }

    [Fact]
    public void InvalidRangesIndicesAndCancellationCannotBeApproved()
    {
        var contour=Notch();
        Assert.False(Validate(contour,contour,[Exact(-1,12,5)]));
        Assert.False(Validate(contour,contour,[new(4,(0,1),(12,5),(1,1),(3,1))]));
        Assert.False(Validate(contour,contour,[new(4,(3,1),(2,1),(1,1),(3,1))]));
        using var cancellation=new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>Validate(contour,contour,[Exact(4,12,5)],cancellation.Token));
    }

    [Fact]
    public void OversizedContourIsNotAccepted()
    {
        var tooMany=Enumerable.Range(0,1025).Select(i=>new GridPoint(i,i*i)).ToArray();
        Assert.False(Validate(tooMany,tooMany,[Exact(4,12,5)]));
    }
}
