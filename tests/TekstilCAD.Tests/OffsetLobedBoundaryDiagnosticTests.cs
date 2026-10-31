using System.Numerics;
using System.Reflection;
using Clipper2Lib;
using TekstilCAD.Patterns;
using Xunit.Abstractions;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

// Diagnostic observations only: distances between vertices and opposite edges
// are not a bound on either complete boundary or a proof of feature retention.
public sealed class OffsetLobedBoundaryDiagnosticTests(ITestOutputHelper output)
{
    private const long Grid = 1_000_000;
    private readonly record struct Match(int Edge, BigInteger Numerator, BigInteger Denominator, string Region)
    {
        internal double Distance => Math.Sqrt((double)Numerator / (double)Denominator);
    }

    private static Match Nearest(GridPoint p, IReadOnlyList<GridPoint> path)
    {
        Match? best = null;
        for (var i = 0; i < path.Count; i++)
        {
            var a = path[i]; var b = path[(i + 1) % path.Count];
            BigInteger dx = (BigInteger)b.X - a.X, dy = (BigInteger)b.Y - a.Y;
            BigInteger px = (BigInteger)p.X - a.X, py = (BigInteger)p.Y - a.Y;
            var len = dx * dx + dy * dy;
            if (len.IsZero) throw new InvalidOperationException("Degenerate comparison edge.");
            var dot = px * dx + py * dy;
            BigInteger n, d; string region;
            if (dot <= 0) { n = px * px + py * py; d = 1; region = "start"; }
            else if (dot >= len)
            {
                var ex = (BigInteger)p.X - b.X; var ey = (BigInteger)p.Y - b.Y;
                n = ex * ex + ey * ey; d = 1; region = "end";
            }
            else { var cross = px * dy - py * dx; n = cross * cross; d = len; region = "interior"; }
            if (best is null || n * best.Value.Denominator < best.Value.Numerator * d)
                best = new Match(i, n, d, region);
        }
        return best ?? throw new InvalidOperationException("Empty path.");
    }

    private static IReadOnlyList<GridPoint> Candidate(IReadOnlyList<GridPoint> source)
    {
        var type = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof", true)!;
        var method = type.GetMethod("TryCreate", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] args = [source, Grid, null, null, CancellationToken.None];
        Assert.True((bool)method.Invoke(null, args)!, (string?)args[3]);
        return Assert.IsAssignableFrom<IReadOnlyList<GridPoint>>(args[2]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReportBidirectionalVertexToEdgeDistanceAndSharedNearestEdges(bool reverse)
    {
        var source = Enumerable.Range(0, 512).Select(i =>
        {
            var angle = 2 * Math.PI * i / 512;
            var radius = 1000 + 200 * Math.Cos(3 * angle);
            return new GridPoint(checked((long)Math.Round(radius * Math.Cos(angle) * Grid, MidpointRounding.AwayFromZero)),
                checked((long)Math.Round(radius * Math.Sin(angle) * Grid, MidpointRounding.AwayFromZero)));
        }).ToArray();
        if (reverse) Array.Reverse(source);
        var candidate = Candidate(source);
        var actual = Assert.Single(Clipper.InflatePaths(new Paths64 { new Path64(source.Select(p => new Point64(p.X, p.Y))) },
            Grid, JoinType.Miter, EndType.Polygon, 2.0)).Select(p => new GridPoint(p.X, p.Y)).ToArray();
        var actualMatches = actual.Select(p => Nearest(p, candidate)).ToArray();
        var candidateMatches = candidate.Select(p => Nearest(p, actual)).ToArray();
        output.WriteLine($"reverse={reverse} candidate={candidate.Count} actual={actual.Length} extra_vertices={actual.Length - candidate.Count}");
        output.WriteLine($"max_actual_vertex_to_candidate_edge_grid={actualMatches.Max(x => x.Distance):R}; max_candidate_vertex_to_actual_edge_grid={candidateMatches.Max(x => x.Distance):R}");
        foreach (var item in actualMatches.Select((match, index) => (match, index)).OrderByDescending(x => x.match.Distance).Take(8))
            output.WriteLine($"actual_vertex={item.index} point={actual[item.index]} nearest_candidate_edge={item.match.Edge} region={item.match.Region} distance_grid={item.match.Distance:R} exact_squared={item.match.Numerator}/{item.match.Denominator}");
        foreach (var group in actualMatches.Select((match, index) => (match, index)).GroupBy(x => x.match.Edge).Where(g => g.Count() > 1))
            output.WriteLine($"shared_candidate_edge={group.Key} actual_vertices={string.Join(",", group.Select(x => x.index))}; regions={string.Join(",", group.Select(x => x.match.Region))}");
        Assert.Equal(source.Length, candidate.Count);
        Assert.All(actualMatches, x => Assert.True(x.Denominator > 0));
    }
}
