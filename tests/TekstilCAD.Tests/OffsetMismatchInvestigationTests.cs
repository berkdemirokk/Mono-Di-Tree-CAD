using Clipper2Lib;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

/// <summary>Numerical investigation only: sampled edges do not certify an offset.</summary>
public sealed class OffsetMismatchInvestigationTests
{
    private readonly ITestOutputHelper output;
    public OffsetMismatchInvestigationTests(ITestOutputHelper output) => this.output = output;

    private static List<Point2> Lobed(bool reverse)
    {
        var points = Enumerable.Range(0, 512).Select(i =>
        {
            var angle = Math.Tau * i / 512;
            var radius = 1000 + 200 * Math.Cos(3 * angle);
            return new Point2(radius * Math.Cos(angle), radius * Math.Sin(angle));
        }).ToList();
        if (reverse) points.Reverse();
        return points;
    }

    // Independent intersection of adjacent unit-normal shifted edges. This candidate
    // is for measuring this fixture only, not a general-purpose offset implementation.
    private static List<Point2> Candidate(IReadOnlyList<Point2> points, double distance)
    {
        var orientation = Math.Sign(Geometry.SignedArea(points));
        var candidate = new List<Point2>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[(i + points.Count - 1) % points.Count];
            var b = points[i];
            var c = points[(i + 1) % points.Count];
            var u = b - a; var v = c - b;
            var ul = Geometry.Distance(a, b); var vl = Geometry.Distance(b, c);
            var n1 = new Point2(orientation * u.Y / ul, -orientation * u.X / ul);
            var n2 = new Point2(orientation * v.Y / vl, -orientation * v.X / vl);
            var denominator = 1 + n1.X * n2.X + n1.Y * n2.Y;
            candidate.Add(b + (n1 + n2) * (distance / denominator));
        }
        return candidate;
    }

    private static List<Point2> ClipperOffset(List<Point2> points)
    {
        var path = new PathD(points.Select(p => new PointD(p.X, p.Y)));
        var result = Clipper.InflatePaths(new PathsD { path }, 1, JoinType.Miter, EndType.Polygon, 2, 6);
        return Assert.Single(result).Select(p => new Point2(p.x, p.y)).ToList();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmoothLobedContourReportsSampledShortEdgeMeasurements(bool reverse)
    {
        var points = Lobed(reverse);
        var candidate = Candidate(points, 1);
        var actual = ClipperOffset(points);
        var report = OffsetMismatchReport.Analyze(candidate, actual, .000001, .1);
        Assert.Equal(512, report.SourceEdgeCount);
        Assert.Equal(actual.Count, report.OutputEdgeCount);
        Assert.All(report.Mismatches, edge =>
        {
            Assert.InRange(edge.OutputEdgeIndex, 0, actual.Count - 1);
            Assert.InRange(edge.NearestSourceEdgeIndex, 0, candidate.Count - 1);
            Assert.InRange(edge.LengthMm, 0, .1);
            Assert.True(double.IsFinite(edge.MidpointDistanceMm));
        });
        output.WriteLine($"Smooth reverse={reverse}; output edges={actual.Count}; short sampled mismatches={report.Mismatches.Count}");
        foreach (var edge in report.Mismatches.Take(20))
            output.WriteLine($"  edge={edge.OutputEdgeIndex}, source={edge.NearestSourceEdgeIndex}, length={edge.LengthMm:G9} mm, midpoint={edge.MidpointDistanceMm:G9} mm");
    }

    [Theory]
    [InlineData(0d, false)]
    [InlineData(0d, true)]
    [InlineData(50000000d, false)]
    [InlineData(50000000d, true)]
    public void SmallNotchExceedingSamplingToleranceProducesLocalMeasurements(double origin, bool reverse)
    {
        List<Point2> candidate = [new(origin - 1, origin - 1), new(origin + 101, origin - 1),
            new(origin + 101, origin + 101), new(origin - 1, origin + 101)];
        List<Point2> notched = [new(origin - 1, origin - 1), new(origin + 101, origin - 1),
            new(origin + 101, origin + 101), new(origin + 50.0005, origin + 101),
            new(origin + 50.0005, origin + 100.995), new(origin + 49.9995, origin + 100.995),
            new(origin + 49.9995, origin + 101), new(origin - 1, origin + 101)];
        if (reverse) { candidate.Reverse(); notched.Reverse(); }
        var report = OffsetMismatchReport.Analyze(candidate, notched, .001, .01);
        Assert.NotEmpty(report.Mismatches);
        Assert.All(report.Mismatches, edge => Assert.InRange(edge.LengthMm, 0, .01));
        Assert.Contains(report.Mismatches, edge => Math.Max(edge.StartDistanceMm, edge.EndDistanceMm) > .004);
        output.WriteLine($"Small notch origin={origin:G}, reverse={reverse}; sampled short mismatches={report.Mismatches.Count}");
    }
}
