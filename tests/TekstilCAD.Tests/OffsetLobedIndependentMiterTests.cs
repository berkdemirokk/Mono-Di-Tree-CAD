using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

// This checks the experimental candidate, not the production Clipper boundary.
public sealed class OffsetLobedIndependentMiterTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 173)]
    [InlineData(true, 0)]
    [InlineData(true, 173)]
    public void EveryCandidateCornerMatchesIndependentShiftedLineIntersection(bool reverse, int start)
    {
        const double scale = 1_000_000;
        var source = Enumerable.Range(0, 512).Select(i =>
        {
            double angle = Math.Tau * i / 512, radius = 1000 + 200 * Math.Cos(3 * angle);
            return new GridPoint((long)Math.Round(radius * Math.Cos(angle) * scale, MidpointRounding.AwayFromZero),
                (long)Math.Round(radius * Math.Sin(angle) * scale, MidpointRounding.AwayFromZero));
        }).ToArray();
        if (reverse) Array.Reverse(source);
        source = source.Skip(start).Concat(source.Take(start)).ToArray();
        var before = source.ToArray();
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateAssessment", true)!
            .GetMethod("TryAssess", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [source, 1_000_000L, null, null, CancellationToken.None];
        Assert.True((bool)method.Invoke(null, args)!, (string?)args[3]);
        var actual = Assert.IsAssignableFrom<IReadOnlyList<Point2>>(args[2]);
        Assert.Equal(source.Length, actual.Count);
        var grid = source.Select(p => new Point2(p.X / scale, p.Y / scale)).ToArray();
        int orientation = Math.Sign(Geometry.SignedArea(grid));
        for (int i = 0; i < grid.Length; i++)
        {
            var a = grid[(i + grid.Length - 1) % grid.Length];
            var b = grid[i]; var c = grid[(i + 1) % grid.Length];
            var u = b - a; var v = c - b;
            double lu = Math.Sqrt(u.X * u.X + u.Y * u.Y), lv = Math.Sqrt(v.X * v.X + v.Y * v.Y);
            var q = b + new Point2(orientation * u.Y / lu, -orientation * u.X / lu);
            var r = b + new Point2(orientation * v.Y / lv, -orientation * v.X / lv);
            var expected = q + u * (Cross(r - q, v) / Cross(u, v));
            // The experimental candidate rounds each coordinate to the grid;
            // this bound also allows binary64 evaluation error in this oracle.
            Assert.InRange(Geometry.Distance(expected, actual[i]), 0, 0.000002);
        }
        Assert.Equal(before, source);
    }

    private static double Cross(Point2 a, Point2 b) => a.X * b.Y - a.Y * b.X;
}
