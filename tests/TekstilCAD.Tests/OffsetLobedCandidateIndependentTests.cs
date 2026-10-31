using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Fixture-specific independent oracle for the indexed experimental candidate.
/// This does not prove feature dimensions or authorize a production offset.</summary>
public sealed class OffsetLobedCandidateIndependentTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 173)]
    [InlineData(true, 0)]
    [InlineData(true, 173)]
    public void EveryCandidateCornerMatchesIndependentOffsetLines(bool reverse, int start)
    {
        const long scale = 1_000_000;
        var original = Enumerable.Range(0, 512).Select(i =>
        {
            double a = Math.Tau * i / 512, r = 1000 + 200 * Math.Cos(3 * a);
            return new Point2(r * Math.Cos(a), r * Math.Sin(a));
        }).ToArray();
        if (reverse) Array.Reverse(original);
        original = original.Skip(start).Concat(original.Take(start)).ToArray();
        var source = original.Select(p => new GridPoint(
            (long)Math.Round(p.X * scale, MidpointRounding.AwayFromZero),
            (long)Math.Round(p.Y * scale, MidpointRounding.AwayFromZero))).ToArray();
        var before = source.ToArray();
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof", true)!
            .GetMethod("TryCreate", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [source, scale, null, null, CancellationToken.None];
        Assert.True((bool)method.Invoke(null, args)!, (string?)args[3]);
        var candidate = Assert.IsAssignableFrom<IReadOnlyList<GridPoint>>(args[2]);
        Assert.Equal(source.Length, candidate.Count);
        var p = source.Select(q => new Point2(q.X / (double)scale, q.Y / (double)scale)).ToArray();
        int orientation = Math.Sign(Geometry.SignedArea(p));
        Assert.NotEqual(0, orientation);
        for (int i = 0; i < p.Length; i++)
        {
            var a = p[(i + p.Length - 1) % p.Length]; var b = p[i]; var c = p[(i + 1) % p.Length];
            var u = b - a; var v = c - b;
            double lu = Math.Sqrt(u.X * u.X + u.Y * u.Y), lv = Math.Sqrt(v.X * v.X + v.Y * v.Y);
            var left = b + new Point2(orientation * u.Y / lu, -orientation * u.X / lu);
            var right = b + new Point2(orientation * v.Y / lv, -orientation * v.X / lv);
            double denominator = Cross(u, v);
            Assert.NotEqual(0, denominator);
            var expected = left + u * (Cross(right - left, v) / denominator);
            var actual = new Point2(candidate[i].X / (double)scale, candidate[i].Y / (double)scale);
            double dx = actual.X - expected.X, dy = actual.Y - expected.Y;
            Assert.True(Math.Sqrt(dx * dx + dy * dy) <= .00001,
                $"Corner {i}: expected ({expected.X:R}, {expected.Y:R}), actual ({actual.X:R}, {actual.Y:R}).");
        }
        Assert.Equal(before, source);
    }

    private static double Cross(Point2 a, Point2 b) => a.X * b.Y - a.Y * b.X;
}
