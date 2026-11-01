using System.Reflection;
using System.Runtime.ExceptionServices;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetIdealTopologyExperimentTests
{
    private static (bool Ok, string Reason) Verify(GridPoint[] source, GridPoint[] candidate, long delta, CancellationToken token = default)
    {
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetIdealTopologyExperiment", true)!
            .GetMethod("TryVerify", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [source, candidate, delta, null, token];
        try { return ((bool)method.Invoke(null, args)!, (string)args[3]!); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void SquareHasSufficientClearance(bool reverse)
    {
        GridPoint[] source = [new(0,0),new(1000,0),new(1000,1000),new(0,1000)];
        GridPoint[] candidate = [new(-10,-10),new(1010,-10),new(1010,1010),new(-10,1010)];
        if (reverse) { Array.Reverse(source); Array.Reverse(candidate); }
        var result = Verify(source, candidate, 10);
        Assert.True(result.Ok, result.Reason); Assert.Contains("not certified", result.Reason);
    }

    [Fact]
    public void NarrowNotchDoesNotPassTheSeparationMargin()
    {
        GridPoint[] source = [new(0,0),new(1000,0),new(1000,1000),new(503,1000),
            new(503,500),new(497,500),new(497,1000),new(0,1000)];
        GridPoint[] candidate = [new(-1,-1),new(1001,-1),new(1001,1001),new(502,1001),
            new(502,501),new(498,501),new(498,1001),new(-1,1001)];
        var result = Verify(source, candidate, 1);
        Assert.False(result.Ok);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void LobedIdealPathHasSufficientClearance(bool reverse)
    {
        const long grid = 1_000_000;
        var source = Enumerable.Range(0, 512).Select(i =>
        {
            double a = Math.Tau * i / 512, r = 1000 + 200 * Math.Cos(3 * a);
            return new GridPoint((long)Math.Round(r * Math.Cos(a) * grid, MidpointRounding.AwayFromZero),
                (long)Math.Round(r * Math.Sin(a) * grid, MidpointRounding.AwayFromZero));
        }).ToArray();
        if (reverse) Array.Reverse(source);
        var before = source.ToArray();
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof", true)!
            .GetMethod("TryCreate", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] args = [source, grid, null, null, CancellationToken.None];
        Assert.True((bool)method.Invoke(null, args)!, (string?)args[3]);
        var candidate = Assert.IsAssignableFrom<IReadOnlyList<GridPoint>>(args[2]).ToArray();
        var result = Verify(source, candidate, grid);
        Assert.True(result.Ok, result.Reason);
        Assert.Equal(before, source);
    }

    [Fact]
    public void CancelledVerificationThrows()
    {
        GridPoint[] square = [new(0,0),new(1000,0),new(1000,1000),new(0,1000)];
        using var c = new CancellationTokenSource(); c.Cancel();
        Assert.Throws<OperationCanceledException>(() => Verify(square, square, 1, c.Token));
    }
}
