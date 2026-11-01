using System.Reflection;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetSelectedFeatureEvidenceExperimentTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public void BoundaryAndExplicitFeatureTolerancesAreBothRequired(bool reverse)
    {
        const long grid = 1_000_000;
        GridPoint[] source = [new(0,0), new(100*grid,0), new(100*grid,100*grid), new(0,100*grid)];
        GridPoint[] candidate = [new(-grid,-grid), new(101*grid,-grid), new(101*grid,101*grid), new(-grid,101*grid)];
        if (reverse) { Array.Reverse(source); Array.Reverse(candidate); }
        var before = source.ToArray();
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetSelectedFeatureEvidenceExperiment", true)!
            .GetMethod("TryVerify", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [source, candidate, grid, 0, 1, 2, 10.0, 10.0, null, CancellationToken.None];
        Assert.True((bool)method.Invoke(null, args)!, (string?)args[8]);
        Assert.Contains("not certified", (string)args[8]!);
        args = [source, candidate, grid, 0, 1, 2, 0.0, 0.0, null, CancellationToken.None];
        Assert.False((bool)method.Invoke(null, args)!);
        Assert.Contains("changed", (string)args[8]!);
        candidate[0] = candidate[0] with { X = candidate[0].X + 1 };
        args = [source, candidate, grid, 0, 1, 2, 10.0, 10.0, null, CancellationToken.None];
        Assert.False((bool)method.Invoke(null, args)!);
        Assert.Contains("differs", (string)args[8]!);
        Assert.Equal(before, source);
    }

    [Fact]
    public void RepeatedSelectionAndInvalidToleranceAreRejected()
    {
        GridPoint[] source = [new(0,0), new(100,0), new(100,100), new(0,100)];
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetSelectedFeatureEvidenceExperiment", true)!
            .GetMethod("TryVerify", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] repeated = [source, source, 10L, 0, 0, 2, 0.0, 0.0, null, CancellationToken.None];
        Assert.False((bool)method.Invoke(null, repeated)!);
        object?[] invalid = [source, source, 10L, 0, 1, 2, double.NaN, 0.0, null, CancellationToken.None];
        Assert.False((bool)method.Invoke(null, invalid)!);
    }
}
