using Clipper2Lib;
using TekstilCAD.Patterns;
using Xunit.Abstractions;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

// Research only: this deliberately simplistic cleanup is not a safe production transform.
public sealed class OffsetMicroCleanupExperimentTests(ITestOutputHelper output)
{
    private static Path64 Clean(Path64 input)
    {
        var p = new Path64(input);
        for (int i = p.Count - 1; i >= 0 && p.Count > 3; i--)
        {
            var a = p[i]; var b = p[(i + 1) % p.Count];
            var x = (double)(a.X - b.X); var y = (double)(a.Y - b.Y);
            if (x * x + y * y <= 4) p.RemoveAt(i);
        }
        return p;
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ShortEdgeCleanupChangesLobedInverseButIsNotSafetyEvidence(bool reverse)
    {
        const long grid = 1_000_000;
        var path = new Path64(Enumerable.Range(0, 512).Select(i => {
            var a = Math.Tau * i / 512; var r = 1000 + 200 * Math.Cos(3 * a);
            return new Point64((long)Math.Round(r * Math.Cos(a) * grid), (long)Math.Round(r * Math.Sin(a) * grid));
        }));
        if (reverse) path.Reverse();
        var forward = Assert.Single(Clipper.InflatePaths(new Paths64 { path }, grid, JoinType.Miter, EndType.Polygon, 2));
        var cleaned = Clean(forward);
        var inverse = Assert.Single(Clipper.InflatePaths(new Paths64 { cleaned }, -grid, JoinType.Miter, EndType.Polygon, 2));
        GridPoint[] source = path.Select(p => new GridPoint(p.X, p.Y)).ToArray();
        GridPoint[] recovered = inverse.Select(p => new GridPoint(p.X, p.Y)).ToArray();
        var worst = OffsetReverseMiterDiagnostic.Analyze(source, recovered, 8).WorstFindings[0].DistanceGrid;
        output.WriteLine($"reverse={reverse}, removed={forward.Count-cleaned.Count}, sampled worst grid={worst:G17}");
        Assert.True(cleaned.Count < forward.Count);
        Assert.InRange(worst, 0, 8);
    }

    [Fact]
    public void ShortEdgeCleanupCanEraseARealOneGridNotch()
    {
        Path64 source = [new(0,0), new(1000,0), new(1000,1000), new(502,1000), new(502,999), new(500,999), new(500,1000), new(0,1000)];
        var cleaned = Clean(source);
        Assert.True(cleaned.Count < source.Count);
        // A real notch corner was removed solely due to its edge length.
        Assert.DoesNotContain(new Point64(500,999), cleaned);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ValidationOnlyCleanupDoesNotDetectLostOneGridNotch(bool reverse)
    {
        const long delta = 1_000_000;
        Path64 source = [new(0,0), new(100_000_000,0), new(100_000_000,100_000_000),
            new(50_000_002,100_000_000), new(50_000_002,99_999_999),
            new(50_000_000,99_999_999), new(50_000_000,100_000_000), new(0,100_000_000)];
        if (reverse) source.Reverse();
        var forward = Assert.Single(Clipper.InflatePaths(new Paths64 { source }, delta, JoinType.Miter, EndType.Polygon, 2));
        var unchanged = forward.ToArray();
        var verificationCopy = Clean(forward);
        Assert.Equal(unchanged, forward);
        var inverse = Assert.Single(Clipper.InflatePaths(new Paths64 { verificationCopy }, -delta, JoinType.Miter, EndType.Polygon, 2));
        var report = OffsetReverseMiterDiagnostic.Analyze(
            source.Select(p => new GridPoint(p.X,p.Y)).ToArray(),
            inverse.Select(p => new GridPoint(p.X,p.Y)).ToArray(), 8);
        output.WriteLine($"reverse={reverse}, removed={forward.Count-verificationCopy.Count}, sampled worst grid={report.WorstFindings[0].DistanceGrid:G17}");
        Assert.InRange(report.WorstFindings[0].DistanceGrid, 0, 8);
        Assert.DoesNotContain(inverse, p => p.X == 50_000_000 && p.Y == 99_999_999);
    }
}


