using System.Reflection;
using System.Runtime.ExceptionServices;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetIdealPathBoundExperimentTests
{
    private static (bool Ok, string Reason) Verify(GridPoint[] source, GridPoint[] candidate, long delta, CancellationToken token = default)
    {
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetIdealPathBoundExperiment", true)!
            .GetMethod("TryVerify", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] args = [source, candidate, delta, null, token];
        try { return ((bool)method.Invoke(null, args)!, (string)args[3]!); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ExactSquareMiterSatisfiesBound(bool reverse)
    {
        GridPoint[] source = [new(0,0),new(100,0),new(100,100),new(0,100)];
        GridPoint[] candidate = [new(-10,-10),new(110,-10),new(110,110),new(-10,110)];
        if (reverse) { Array.Reverse(source); Array.Reverse(candidate); }
        var result = Verify(source, candidate, 10);
        Assert.True(result.Ok, result.Reason);
        Assert.Contains("not certified", result.Reason);
        candidate[0] = candidate[0] with { X = candidate[0].X + 2 };
        Assert.False(Verify(source, candidate, 10).Ok);
    }

    [Fact]
    public void InvalidGeometryAndCancellationAreRejected()
    {
        GridPoint[] line = [new(0,0),new(10,0),new(20,0)];
        Assert.False(Verify(line, line, 1).Ok);
        Assert.False(Verify(line, line, 0).Ok);
        using var c = new CancellationTokenSource(); c.Cancel();
        Assert.Throws<OperationCanceledException>(() => Verify(line, line, 1, c.Token));
    }

    [Theory]
    [InlineData(false, 0)] [InlineData(false, 173)]
    [InlineData(true, 0)] [InlineData(true, 173)]
    public void LobedRoundedCandidateStaysWithinOneGridUnitPerAxis(bool reverse, int start)
    {
        const long grid = 1_000_000;
        var source = Enumerable.Range(0, 512).Select(i =>
        {
            double angle = Math.Tau * i / 512, radius = 1000 + 200 * Math.Cos(3 * angle);
            return new GridPoint((long)Math.Round(radius * Math.Cos(angle) * grid, MidpointRounding.AwayFromZero),
                (long)Math.Round(radius * Math.Sin(angle) * grid, MidpointRounding.AwayFromZero));
        }).ToArray();
        if (reverse) Array.Reverse(source);
        source = source.Skip(start).Concat(source.Take(start)).ToArray();
        var before = source.ToArray();
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof", true)!
            .GetMethod("TryCreate", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [source, grid, null, null, CancellationToken.None];
        Assert.True((bool)method.Invoke(null, args)!, (string?)args[3]);
        var candidate = Assert.IsAssignableFrom<IReadOnlyList<GridPoint>>(args[2]).ToArray();
        var candidateBefore = candidate.ToArray();
        var result = Verify(source, candidate, grid);
        Assert.True(result.Ok, result.Reason);
        Assert.Equal(before, source); Assert.Equal(candidateBefore, candidate);
    }
}
