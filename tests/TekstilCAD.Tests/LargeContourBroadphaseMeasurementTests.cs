using System.Diagnostics;
using TekstilCAD.Core;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

/// <summary>Comparative measurements, not performance thresholds or a safety proof.</summary>
public sealed class LargeContourBroadphaseMeasurementTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(4097)]
    [InlineData(10000)]
    public void ValidRingReportsFullAndDiagnosticWorkWithoutChangingInput(int count)
    {
        var points = Enumerable.Range(0, count).Select(i =>
        {
            var angle = Math.Tau * i / count;
            return new Point2(1000 * Math.Cos(angle), 1000 * Math.Sin(angle));
        }).ToList();
        var before = points.ToArray();

        var fullTimer = Stopwatch.StartNew();
        var fullError = Record.Exception(() => LargeContourValidator.Validate(points));
        fullTimer.Stop();

        LargeContourBroadphaseDiagnostic.Statistics? stats = null;
        var diagnosticTimer = Stopwatch.StartNew();
        var diagnosticError = Record.Exception(() => { stats = LargeContourBroadphaseDiagnostic.Validate(points); });
        diagnosticTimer.Stop();

        Assert.Null(fullError);
        Assert.Null(diagnosticError);
        Assert.NotNull(stats);
        Assert.Equal(before, points);
        output.WriteLine("count={0}, full_ms={1:F3}, diagnostic_ms={2:F3}, pairs={3}, filtered={4}, predicate_calls={5}",
            count, fullTimer.Elapsed.TotalMilliseconds, diagnosticTimer.Elapsed.TotalMilliseconds,
            stats!.PairCount, stats.BroadphaseRejected, stats.PredicateCalls);
    }
}
