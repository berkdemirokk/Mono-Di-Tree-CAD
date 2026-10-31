using System.Diagnostics;
using TekstilCAD.Core;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

/// <summary>Measurements of selected inputs, not timing thresholds or safety proofs.</summary>
public sealed class LargeBroadphaseWorstCaseMeasurementTests(ITestOutputHelper output)
{
    [Fact]
    public void ValidAlternatingRadiusContourReportsOverlappingBoundsWork()
    {
        // Each pair of successive long radial edges occupies an angular sector.
        // Their axis-aligned boxes overlap considerably even though the contour
        // is simple. This is not asserted to be the mathematical worst case.
        const int count = 1200;
        var points = Enumerable.Range(0, count).Select(i =>
        {
            var angle = Math.Tau * i / count;
            var radius = i % 2 == 0 ? 1000.0 : 1.0;
            return new Point2(radius * Math.Cos(angle), radius * Math.Sin(angle));
        }).ToList();
        var before = points.ToArray();

        var full = Stopwatch.StartNew();
        var fullError = Record.Exception(() => LargeContourValidator.Validate(points));
        full.Stop();
        LargeContourBroadphaseDiagnostic.Statistics? statistics = null;
        var filtered = Stopwatch.StartNew();
        var filteredError = Record.Exception(() => statistics = LargeContourBroadphaseDiagnostic.Validate(points));
        filtered.Stop();

        Assert.Null(fullError);
        Assert.Null(filteredError);
        Assert.NotNull(statistics);
        Assert.Equal(before, points);
        Assert.Equal((long)count * (count - 1) / 2, statistics!.PairCount);
        Assert.True(statistics.PredicateCalls > 0, "This sample should exercise overlapping candidate boxes.");
        output.WriteLine("alternating_radius count={0}, full_ms={1:F3}, diagnostic_ms={2:F3}, pairs={3}, filtered={4}, predicate_calls={5}",
            count, full.Elapsed.TotalMilliseconds, filtered.Elapsed.TotalMilliseconds, statistics.PairCount,
            statistics.BroadphaseRejected, statistics.PredicateCalls);
    }

    [Fact]
    public void EarlyCrossingIsRejectedByBothAndReportsElapsedTime()
    {
        // Edges 0 and 2 cross; the extra vertex gives the contour nonzero signed area.
        List<Point2> points = [new(0, 0), new(100, 100), new(0, 100), new(100, 0), new(110, -20)];
        var before = points.ToArray();
        var full = Stopwatch.StartNew();
        var fullError = Record.Exception(() => LargeContourValidator.Validate(points));
        full.Stop();
        var filtered = Stopwatch.StartNew();
        var filteredError = Record.Exception(() => LargeContourBroadphaseDiagnostic.Validate(points));
        filtered.Stop();

        Assert.IsType<ArgumentException>(fullError);
        Assert.IsType<ArgumentException>(filteredError);
        Assert.Equal(before, points);
        // Statistics are only returned on success; no partial-work count is claimed.
        output.WriteLine("early_crossing count={0}, full_ms={1:F3}, diagnostic_ms={2:F3}; both rejected; partial counts unavailable",
            points.Count, full.Elapsed.TotalMilliseconds, filtered.Elapsed.TotalMilliseconds);
    }
}
