using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class OffsetShadowBoundaryTests
{
    [Fact]
    public void OutOfRangeCandidateReturnsDiagnosticWithoutChangingSource()
    {
        List<Point2> points = [new(99_999_990, 0), new(100_000_000, 0), new(100_000_000, 10), new(99_999_990, 10)];
        var before = points.ToArray();
        var result = OffsetShadowDiagnostic.Analyze(points, 10);
        Assert.False(result.CandidateConstructed);
        Assert.Contains("coordinate range", result.Reason);
        Assert.Equal(before, points);
    }

    [Fact]
    public void CollinearAdjacentEdgesAreConservativelyRejected()
    {
        List<Point2> points = [new(0, 0), new(5, 0), new(10, 0), new(10, 10), new(0, 10)];
        var result = OffsetShadowDiagnostic.Analyze(points, 1);
        Assert.False(result.CandidateConstructed);
        Assert.Contains("parallel", result.Reason);
    }
}
