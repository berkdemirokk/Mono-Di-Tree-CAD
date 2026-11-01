using System.Reflection;
using System.Runtime.ExceptionServices;
using TekstilCAD.Patterns;
using Xunit.Abstractions;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetLinearTopologyExperimentTests(ITestOutputHelper output)
{
    private static (bool Ok, string Reason) Verify(GridPoint[] source, GridPoint[] candidate, CancellationToken token = default)
    {
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetLinearTopologyExperiment", true)!
            .GetMethod("TryVerify", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [source, candidate, null, token];
        try { return ((bool)method.Invoke(null, args)!, (string)args[2]!); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpandingSquareIsCertifiedForBothOrientations(bool reverse)
    {
        GridPoint[] source = [new(0, 0), new(100, 0), new(100, 100), new(0, 100)];
        GridPoint[] target = [new(-10, -10), new(110, -10), new(110, 110), new(-10, 110)];
        if (reverse) { Array.Reverse(source); Array.Reverse(target); }
        var result = Verify(source, target);
        Assert.True(result.Ok, result.Reason);
        Assert.Contains("not certified", result.Reason);
    }

    [Fact]
    public void SimpleEndpointsCanCollapseAtMidpoint()
    {
        GridPoint[] source = [new(-10, -10), new(10, -10), new(10, 10), new(-10, 10)];
        GridPoint[] target = [new(10, 10), new(-10, 10), new(-10, -10), new(10, -10)];
        Assert.False(Verify(source, target).Ok);
    }

    [Fact]
    public void CrossedContourAndCancellationCannotBeCertified()
    {
        GridPoint[] source = [new(0, 0), new(100, 0), new(100, 100), new(0, 100)];
        GridPoint[] target = [new(0, 0), new(100, 100), new(100, 0), new(0, 100)];
        Assert.False(Verify(source, target).Ok);
        using var c = new CancellationTokenSource(); c.Cancel();
        Assert.Throws<OperationCanceledException>(() => Verify(source, target, c.Token));
    }

    [Fact]
    public void NonAdjacentCrossingWithNonzeroTurnsFailsPairCheck()
    {
        // A pentagram has no collinear adjacent triples, so a turn-only test
        // cannot establish that its non-adjacent edges are disjoint.
        GridPoint[] star = [new(0, 10), new(6, -8), new(-10, 3), new(10, 3), new(-6, -8)];
        var result = Verify(star, star);
        Assert.False(result.Ok);
        Assert.Contains("Edges", result.Reason);
    }

    [Fact]
    public void ExactPredicatesHandleCoordinateDifferencesLargerThanLong()
    {
        // Subtraction must be performed after promotion to BigInteger.
        GridPoint[] square = [new(long.MinValue, long.MinValue), new(long.MaxValue, long.MinValue),
            new(long.MaxValue, long.MaxValue), new(long.MinValue, long.MaxValue)];
        var before = square.ToArray();
        var result = Verify(square, square);
        Assert.True(result.Ok, result.Reason);
        Assert.Equal(before, square);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LobedCandidateReportsAnExplicitConservativeResult(bool reverse)
    {
        const long grid = 1_000_000;
        var source = Enumerable.Range(0, 512).Select(i =>
        {
            double angle = Math.Tau * i / 512, radius = 1000 + 200 * Math.Cos(3 * angle);
            return new GridPoint((long)Math.Round(radius * Math.Cos(angle) * grid, MidpointRounding.AwayFromZero),
                (long)Math.Round(radius * Math.Sin(angle) * grid, MidpointRounding.AwayFromZero));
        }).ToArray();
        if (reverse) Array.Reverse(source);
        var before = source.ToArray();
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof", true)!
            .GetMethod("TryCreate", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [source, grid, null, null, CancellationToken.None];
        Assert.True((bool)method.Invoke(null, args)!, (string?)args[3]);
        var candidate = Assert.IsAssignableFrom<IReadOnlyList<GridPoint>>(args[2]).ToArray();
        var copy = candidate.ToArray();
        var result = Verify(source, candidate);
        output.WriteLine($"reverse={reverse}; accepted={result.Ok}; reason={result.Reason}");
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        Assert.Equal(before, source); Assert.Equal(copy, candidate);
    }
}
