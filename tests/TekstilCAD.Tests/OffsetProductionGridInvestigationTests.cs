using Clipper2Lib;
using TekstilCAD.Core;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

/// <summary>Numerical measurements only; vertex and midpoint sampling is not a preservation proof.</summary>
public sealed class OffsetProductionGridInvestigationTests
{
    private const double Scale = 1_000_000;
    private readonly ITestOutputHelper output;
    public OffsetProductionGridInvestigationTests(ITestOutputHelper output) => this.output = output;

    private static Path64 Quantize(IEnumerable<Point2> points) => new(points.Select(p =>
        new Point64(checked((long)Math.Round(p.X * Scale, MidpointRounding.AwayFromZero)),
                    checked((long)Math.Round(p.Y * Scale, MidpointRounding.AwayFromZero)))));

    // Coordinates are subtracted as integers before conversion, as in the production local guard.
    private static double MaximumSampleDistance(Path64 from, Path64 to)
    {
        double maximum = 0;
        for (var i = 0; i < from.Count; i++)
        {
            var a = from[i]; var b = from[(i + 1) % from.Count];
            foreach (var t in new[] { 0d, .5d })
            {
                double x = (b.X - a.X) * t, y = (b.Y - a.Y) * t;
                var best = double.PositiveInfinity;
                for (var j = 0; j < to.Count; j++)
                {
                    var c = to[j]; var d = to[(j + 1) % to.Count];
                    double cx = c.X - a.X, cy = c.Y - a.Y, dx = d.X - c.X, dy = d.Y - c.Y;
                    var length = dx * dx + dy * dy;
                    var q = length == 0 ? 0 : Math.Clamp(((x - cx) * dx + (y - cy) * dy) / length, 0, 1);
                    var ex = x - (cx + q * dx); var ey = y - (cy + q * dy);
                    best = Math.Min(best, Math.Sqrt(ex * ex + ey * ey));
                }
                maximum = Math.Max(maximum, best);
            }
        }
        return maximum;
    }

    private void Measure(string label, Path64 source, long delta)
    {
        var forward = Assert.Single(Clipper.InflatePaths(new Paths64 { source }, delta, JoinType.Miter, EndType.Polygon, 2.0));
        var reverse = Assert.Single(Clipper.InflatePaths(new Paths64 { forward }, -delta, JoinType.Miter, EndType.Polygon, 2.0));
        var sourceToForward = MaximumSampleDistance(source, forward);
        var forwardToSource = MaximumSampleDistance(forward, source);
        var sourceToReverse = MaximumSampleDistance(source, reverse);
        var reverseToSource = MaximumSampleDistance(reverse, source);
        Assert.True(double.IsFinite(sourceToForward) && double.IsFinite(forwardToSource)
            && double.IsFinite(sourceToReverse) && double.IsFinite(reverseToSource));
        output.WriteLine($"{label}: source={source.Count}, forward={forward.Count}, reverse={reverse.Count}, source→forward={sourceToForward:G9} grid, forward→source={forwardToSource:G9} grid, source→reverse={sourceToReverse:G9} grid, reverse→source={reverseToSource:G9} grid");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionGridLobedForwardAndReverseMeasurements(bool reverse)
    {
        var points = Enumerable.Range(0, 512).Select(i =>
        {
            var a = Math.Tau * i / 512; var r = 1000 + 200 * Math.Cos(3 * a);
            return new Point2(r * Math.Cos(a), r * Math.Sin(a));
        }).ToList();
        if (reverse) points.Reverse();
        Measure($"lobed reverse={reverse}", Quantize(points), 1_000_000);
    }

    [Theory]
    [InlineData(1L, 0L, false)]
    [InlineData(1L, 50_000_000_000_000L, true)]
    [InlineData(5000L, 0L, true)]
    [InlineData(5000L, 50_000_000_000_000L, false)]
    public void ProductionGridNotchForwardAndReverseMeasurements(long depth, long origin, bool reverse)
    {
        // The tiny recess sits on the upper edge; all coordinates below are grid units.
        Point64 P(long x, long y) => new(origin + x, origin + y);
        var width = 2 * depth;
        var source = new Path64 { P(0,0), P(100_000_000,0), P(100_000_000,100_000_000),
            P(50_000_000 + width,100_000_000), P(50_000_000 + width,100_000_000 - depth),
            P(50_000_000,100_000_000 - depth), P(50_000_000,100_000_000), P(0,100_000_000) };
        if (reverse) source.Reverse();
        Measure($"notch depth={depth} grid origin={origin} reverse={reverse}", source, 1_000_000);
    }
}
