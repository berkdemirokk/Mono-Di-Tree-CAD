using Clipper2Lib;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

/// <summary>Fixture-specific measurement, not a certificate for offset geometry.</summary>
public sealed class OffsetReverseMismatchInvestigationTests(ITestOutputHelper log)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmoothLobedFixtureIdentifiesInverseMismatchIndependentlyOfProductionGuard(bool reverse)
    {
        const double scale = 1_000_000;
        var path = new Path64(Enumerable.Range(0, 512).Select(i =>
        {
            double angle = Math.Tau * i / 512, radius = 1000 + 200 * Math.Cos(3 * angle);
            return new Point64((long)Math.Round(radius * Math.Cos(angle) * scale, MidpointRounding.AwayFromZero),
                (long)Math.Round(radius * Math.Sin(angle) * scale, MidpointRounding.AwayFromZero));
        }));
        if (reverse) path.Reverse();
        var forward = Assert.Single(Clipper.InflatePaths(new Paths64 { path }, scale, JoinType.Miter, EndType.Polygon, 2));
        var recovered = Assert.Single(Clipper.InflatePaths(new Paths64 { forward }, -scale, JoinType.Miter, EndType.Polygon, 2));
        var sourceToRecovered = LargestSampleDistance(path, recovered);
        var recoveredToSource = LargestSampleDistance(recovered, path);
        log.WriteLine($"reverse={reverse}; source->recovered={sourceToRecovered:G17} grid units; recovered->source={recoveredToSource:G17} grid units; forward vertices={forward.Count}; recovered vertices={recovered.Count}");
        // Only the source-to-recovered direction is a safety property. The reverse
        // direction also measures Clipper's extra miter vertices in the
        // reconstruction (516 vs 512 here), which are not lost source features.
        Assert.True(sourceToRecovered <= 8);
        Assert.True(recoveredToSource > 8);
        var offset = PatternTools.Offset(new PatternPiece
        {
            Closed = true,
            Points = path.Select(p => new Point2(p.X / scale, p.Y / scale)).ToList()
        }, 1);
        Assert.True(offset.Points.Count >= 512);
    }

    private static double LargestSampleDistance(Path64 source, Path64 target)
    {
        double maximum = 0;
        for (var i = 0; i < source.Count; i++)
        {
            var a = source[i]; var b = source[(i + 1) % source.Count];
            maximum = Math.Max(maximum, NearestDistance(0, 0, a, target));
            maximum = Math.Max(maximum, NearestDistance((b.X - a.X) / 2.0, (b.Y - a.Y) / 2.0, a, target));
        }
        return maximum;
    }

    private static double NearestDistance(double x, double y, Point64 origin, Path64 target)
    {
        double best = double.PositiveInfinity;
        for (var i = 0; i < target.Count; i++)
        {
            var a = target[i]; var b = target[(i + 1) % target.Count];
            double ax = a.X - origin.X, ay = a.Y - origin.Y;
            double dx = b.X - a.X, dy = b.Y - a.Y, lengthSquared = dx * dx + dy * dy;
            var t = lengthSquared == 0 ? 0 : Math.Clamp(((x - ax) * dx + (y - ay) * dy) / lengthSquared, 0, 1);
            best = Math.Min(best, Math.Sqrt(Math.Pow(x - ax - t * dx, 2) + Math.Pow(y - ay - t * dy, 2)));
        }
        return best;
    }
}
