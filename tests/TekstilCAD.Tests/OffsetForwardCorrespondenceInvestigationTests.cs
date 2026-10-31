using System.Reflection;
using Clipper2Lib;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

/// <summary>Exploratory vertex correspondence only. Nearest-vertex assignments
/// do not bound edges, prove an offset, or authorize a production fallback.</summary>
public sealed class OffsetForwardCorrespondenceInvestigationTests(ITestOutputHelper log)
{
    private static (int[] Indices, double MaxDistance, bool Ordered, bool Complete) Map(
        IReadOnlyList<GridPoint> candidate, IReadOnlyList<GridPoint> actual)
    {
        var indices = new int[actual.Count];
        double maxSquared = 0;
        for (int i = 0; i < actual.Count; i++)
        {
            var best = double.PositiveInfinity;
            for (int j = 0; j < candidate.Count; j++)
            {
                double dx = (double)actual[i].X - candidate[j].X;
                double dy = (double)actual[i].Y - candidate[j].Y;
                double distance = dx * dx + dy * dy;
                if (distance < best) { best = distance; indices[i] = j; }
            }
            maxSquared = Math.Max(maxSquared, best);
        }
        int steps = 0; bool ordered = true;
        for (int i = 0; i < indices.Length; i++)
        {
            int step = (indices[(i + 1) % indices.Length] - indices[i] + candidate.Count) % candidate.Count;
            if (step > 1) ordered = false;
            steps += step;
        }
        return (indices, Math.Sqrt(maxSquared), ordered && steps == candidate.Count,
            indices.Distinct().Count() == candidate.Count);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualForwardPathHasReportedIndexedCorrespondence(bool reverse)
    {
        const long grid = 1_000_000;
        var source = Enumerable.Range(0, 512).Select(i =>
        {
            var angle = Math.Tau * i / 512; var radius = 1000 + 200 * Math.Cos(3 * angle);
            return new GridPoint((long)Math.Round(radius * Math.Cos(angle) * grid, MidpointRounding.AwayFromZero),
                (long)Math.Round(radius * Math.Sin(angle) * grid, MidpointRounding.AwayFromZero));
        }).ToArray();
        if (reverse) Array.Reverse(source);
        var before = source.ToArray();
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof", true)!
            .GetMethod("TryCreate", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [source, grid, null, null, CancellationToken.None];
        Assert.True((bool)method.Invoke(null, args)!, (string?)args[3]);
        var candidate = Assert.IsAssignableFrom<IReadOnlyList<GridPoint>>(args[2]);
        var path = new Path64(source.Select(p => new Point64(p.X, p.Y)));
        var forward = Assert.Single(Clipper.InflatePaths(new Paths64 { path }, grid, JoinType.Miter, EndType.Polygon, 2));
        Assert.Equal(515, forward.Count);
        var mapped = Map(candidate, forward.Select(p => new GridPoint(p.X, p.Y)).ToArray());
        log.WriteLine($"reverse={reverse}; candidate={candidate.Count}; forward={forward.Count}; ordered={mapped.Ordered}; complete={mapped.Complete}; maximum_vertex_distance_grid={mapped.MaxDistance:G17}; assignments={string.Join(",", mapped.Indices)}");
        Assert.Equal(before, source);
    }

    [Fact]
    public void MissingCornerAndReversedTraceFailOrderOrCompleteness()
    {
        GridPoint[] reference = [new(0,0),new(100,0),new(100,100),new(0,100)];
        GridPoint[] missing = [new(0,0),new(100,0),new(0,100)];
        var m = Map(reference, missing);
        Assert.False(m.Complete);
        var r = Map(reference, reference.Reverse().ToArray());
        Assert.False(r.Ordered);
    }

    [Fact]
    public void VertexMappingAloneCannotDetectADeepEdgeNotch()
    {
        GridPoint[] reference = [new(0,0),new(100,0),new(100,100),new(0,100)];
        GridPoint[] notched = [new(0,0),new(49,40),new(51,40),new(100,0),new(100,100),new(0,100)];
        // Even an ordered and complete assignment with finite distances says
        // nothing about whether the intervening boundary retains a feature.
        var result = Map(reference, notched);
        Assert.True(result.Ordered);
        Assert.True(result.Complete);
        Assert.True(result.MaxDistance > 0);
    }
}
