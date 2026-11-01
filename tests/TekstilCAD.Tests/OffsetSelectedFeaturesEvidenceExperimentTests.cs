using System.Reflection;
using System.Runtime.ExceptionServices;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetSelectedFeaturesEvidenceExperimentTests
{
    private static (bool Ok, string Reason) Verify(GridPoint[] source, GridPoint[] candidate,
        (int, int, int, double, double)[] features, CancellationToken token = default)
    {
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetSelectedFeaturesEvidenceExperiment", true)!
            .GetMethod("TryVerify", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [source, candidate, 1_000_000L, features, null, token];
        try { return ((bool)method.Invoke(null, args)!, (string)args[4]!); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static (GridPoint[] Source, GridPoint[] Candidate) Square() =>
        ([new(0,0),new(100_000_000,0),new(100_000_000,100_000_000),new(0,100_000_000)],
         [new(-1_000_000,-1_000_000),new(101_000_000,-1_000_000),
          new(101_000_000,101_000_000),new(-1_000_000,101_000_000)]);

    [Theory] [InlineData(false)] [InlineData(true)]
    public void EverySelectedFeatureMustPassItsOwnTolerance(bool reverse)
    {
        var (source, candidate) = Square();
        if (reverse) { Array.Reverse(source); Array.Reverse(candidate); }
        var beforeSource = source.ToArray(); var beforeCandidate = candidate.ToArray();
        (int, int, int, double, double)[] features = [(0,1,2,10,10),(1,2,3,10,10)];
        var result = Verify(source, candidate, features);
        Assert.True(result.Ok, result.Reason); Assert.Contains("not certified", result.Reason);
        features[1] = (1,2,3,0,0);
        result = Verify(source, candidate, features);
        Assert.False(result.Ok); Assert.Contains("Feature 1", result.Reason);
        Assert.Equal(beforeSource, source); Assert.Equal(beforeCandidate, candidate);
    }

    [Fact]
    public void EmptyDuplicateAndInvalidFeatureListsAreRejected()
    {
        var (s,c) = Square();
        Assert.False(Verify(s,c,[]).Ok);
        Assert.False(Verify(s,c,[(0,1,2,10,10),(0,1,2,1,1)]).Ok);
        Assert.False(Verify(s,c,[(0,1,2,10,double.NaN)]).Ok);
        Assert.False(Verify(s,c,[(0,1,4,10,10)]).Ok);
    }

    [Fact]
    public void AlteredCandidateAndCancellationAreRejected()
    {
        var (s,c) = Square(); c[0] = c[0] with { X = c[0].X + 1 };
        Assert.False(Verify(s,c,[(0,1,2,10,10)]).Ok);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => Verify(s,c,[(0,1,2,10,10)],cancellation.Token));
    }
}
