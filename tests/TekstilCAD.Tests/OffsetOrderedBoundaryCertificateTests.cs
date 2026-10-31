using System.Reflection;
using Clipper2Lib;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

public sealed class OffsetOrderedBoundaryCertificateTests(ITestOutputHelper log)
{
    private static bool Verify(IReadOnlyList<GridPoint> reference, IReadOnlyList<GridPoint> actual, long tolerance)
    {
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetOrderedBoundaryCertificate", true)!
            .GetMethod("TryVerify", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [reference, actual, tolerance, null, CancellationToken.None];
        return (bool)method.Invoke(null, args)!;
    }

    [Fact]
    public void RejectsSkippedCornerAndReversedTrace()
    {
        GridPoint[] square = [new(0,0), new(100,0), new(100,100), new(0,100)];
        Assert.True(Verify(square, square, 0));
        Assert.False(Verify(square, [square[0], square[1], square[3]], 100));
        Assert.False(Verify(square, square.Reverse().ToArray(), 100));
        Assert.False(Verify(square, [square[0], new(50,50), square[1], square[2], square[3]], 49));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReportsWholeBoundaryBoundForLobedForwardPath(bool reverse)
    {
        const long grid = 1_000_000;
        var source = Enumerable.Range(0, 512).Select(i => {
            double angle = Math.Tau * i / 512, radius = 1000 + 200 * Math.Cos(3 * angle);
            return new GridPoint((long)Math.Round(radius * Math.Cos(angle) * grid, MidpointRounding.AwayFromZero),
                (long)Math.Round(radius * Math.Sin(angle) * grid, MidpointRounding.AwayFromZero));
        }).ToArray();
        if (reverse) Array.Reverse(source);
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof", true)!
            .GetMethod("TryCreate", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [source, grid, null, null, CancellationToken.None];
        Assert.True((bool)method.Invoke(null, args)!, (string?)args[3]);
        var candidate = Assert.IsAssignableFrom<IReadOnlyList<GridPoint>>(args[2]);
        var path = new Path64(source.Select(p => new Point64(p.X, p.Y)));
        var result = Assert.Single(Clipper.InflatePaths(new Paths64 { path }, grid, JoinType.Miter, EndType.Polygon, 2));
        var actual = result.Select(p => new GridPoint(p.X, p.Y)).ToArray();
        // Report a distance only when order and complete continuous coverage hold.
        long[] thresholds = [8, 100, 1000, 10000, 100000, 1000000, 10000000];
        var bound = thresholds.FirstOrDefault(t => Verify(candidate, actual, t));
        log.WriteLine($"reverse={reverse}; certified_bound_grid={bound}; zero means no listed threshold certified");
    }
}
