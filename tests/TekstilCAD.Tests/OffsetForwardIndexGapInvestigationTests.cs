using System.Numerics;
using System.Reflection;
using Clipper2Lib;
using TekstilCAD.Patterns;
using Xunit.Abstractions;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Measurement only: nearest vertices do not establish segment correspondence.</summary>
public sealed class OffsetForwardIndexGapInvestigationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MeasureActualForwardGridAgainstIndependentIndexedCandidate(bool reverse)
    {
        const long grid = 1_000_000;
        var source = Enumerable.Range(0,512).Select(i => {
            double a = Math.Tau * i / 512, r = 1000 + 200 * Math.Cos(3 * a);
            return new GridPoint((long)Math.Round(r * Math.Cos(a) * grid, MidpointRounding.AwayFromZero),
                (long)Math.Round(r * Math.Sin(a) * grid, MidpointRounding.AwayFromZero));
        }).ToArray();
        if (reverse) Array.Reverse(source);
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof", true)!
            .GetMethod("TryCreate", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] args = [source, grid, null, null, CancellationToken.None];
        Assert.True((bool)method.Invoke(null, args)!, (string?)args[3]);
        var candidate = Assert.IsAssignableFrom<IReadOnlyList<GridPoint>>(args[2]);
        var actual = Assert.Single(Clipper.InflatePaths(new Paths64 { new Path64(source.Select(p => new Point64(p.X,p.Y))) },
            grid, JoinType.Miter, EndType.Polygon, 2));
        Assert.Equal(512, candidate.Count); Assert.Equal(515, actual.Count);
        static BigInteger DistanceSquared(GridPoint p, Point64 q) {
            BigInteger x = (BigInteger)p.X - q.X, y = (BigInteger)p.Y - q.Y;
            return x * x + y * y;
        }
        var nearest = candidate.Select(p => actual.Select(q => DistanceSquared(p,q)).Min()).ToArray();
        int exact = nearest.Count(x => x.IsZero);
        BigInteger maximum = nearest.Max();
        output.WriteLine($"reverse={reverse}; candidate=512; forward=515; exactNearest={exact}; maxNearestSquaredGrid={maximum}");
        foreach (var i in Enumerable.Range(0,candidate.Count).Where(i => nearest[i] != 0))
            output.WriteLine($"candidateIndex={i}; nearestSquaredGrid={nearest[i]}");
        // Nearest-vertex distances cannot certify a cyclic alignment, preserve a
        // recess, or establish a quantization bound. No production gate uses this.
        Assert.All(nearest, distance => Assert.True(distance >= 0));
    }
}
