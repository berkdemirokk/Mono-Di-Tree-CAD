using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Candidate feasibility gate only; acceptance does not authorize production.</summary>
public sealed class OffsetLobedCandidateAcceptanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LobedCandidateMustBeConstructibleAndExpandBounds(bool reverse)
    {
        const double scale = 1_000_000;
        var original = Enumerable.Range(0, 512).Select(i =>
        {
            var a = Math.Tau * i / 512;
            var radius = 1000 + 200 * Math.Cos(3 * a);
            return new GridPoint((long)Math.Round(radius * Math.Cos(a) * scale, MidpointRounding.AwayFromZero),
                (long)Math.Round(radius * Math.Sin(a) * scale, MidpointRounding.AwayFromZero));
        }).ToArray();
        if (reverse) Array.Reverse(original);
        var before = original.ToArray();
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateAssessment", true)!
            .GetMethod("TryAssess", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [original, 1_000_000L, null, null, CancellationToken.None];
        Assert.True((bool)method.Invoke(null, args)!, (string?)args[3]);
        var result = Assert.IsAssignableFrom<IReadOnlyList<Point2>>(args[2]);
        var sourceBounds = Geometry.Bounds(original.Select(p => new Point2(p.X / scale, p.Y / scale)).ToArray());
        var resultBounds = Geometry.Bounds(result);
        Assert.InRange(sourceBounds.MinX - resultBounds.MinX, .999, 1.01);
        Assert.InRange(sourceBounds.MinY - resultBounds.MinY, .999, 1.01);
        Assert.InRange(resultBounds.MaxX - sourceBounds.MaxX, .999, 1.01);
        Assert.InRange(resultBounds.MaxY - sourceBounds.MaxY, .999, 1.01);
        Assert.Equal(original.Length, result.Count);
        Assert.Equal(before, original);
    }
}
