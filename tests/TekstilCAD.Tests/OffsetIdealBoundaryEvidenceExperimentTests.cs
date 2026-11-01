using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetIdealBoundaryEvidenceExperimentTests
{
    [Fact]
    public void MillimeterTopologyBudgetRejects708VerticesBeforeGeometryChecks()
    {
        var grid = new GridPoint[708];
        var millimeters = new Point2[708];
        var proof = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetMillimeterProof", true)!
            .GetMethod("TryValidate", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [grid, grid, millimeters, millimeters, null, CancellationToken.None];
        Assert.False((bool)proof.Invoke(null, args)!);
        Assert.Contains("pair budget", (string)args[4]!);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void LobedCandidatePassesCombinedEvidenceButChangedCornerFails(bool reverse)
    {
        const long grid = 1_000_000;
        var source = Enumerable.Range(0, 512).Select(i =>
        {
            double a = Math.Tau * i / 512, r = 1000 + 200 * Math.Cos(3 * a);
            return new GridPoint((long)Math.Round(r * Math.Cos(a) * grid, MidpointRounding.AwayFromZero),
                (long)Math.Round(r * Math.Sin(a) * grid, MidpointRounding.AwayFromZero));
        }).ToArray();
        if (reverse) Array.Reverse(source);
        var proof = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof", true)!
            .GetMethod("TryCreate", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] create = [source, grid, null, null, CancellationToken.None];
        Assert.True((bool)proof.Invoke(null, create)!, (string?)create[3]);
        var candidate = Assert.IsAssignableFrom<IReadOnlyList<GridPoint>>(create[2]).ToArray();
        var before = source.ToArray();
        var verifier = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetIdealBoundaryEvidenceExperiment", true)!
            .GetMethod("TryVerify", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [source, candidate, grid, null, CancellationToken.None];
        Assert.True((bool)verifier.Invoke(null, args)!, (string?)args[3]);
        Assert.Contains("not certified", (string)args[3]!);
        candidate[0] = candidate[0] with { X = candidate[0].X + 1 };
        args = [source, candidate, grid, null, CancellationToken.None];
        Assert.False((bool)verifier.Invoke(null, args)!);
        Assert.Contains("differs", (string)args[3]!);
        Assert.Equal(before, source);
    }
}
