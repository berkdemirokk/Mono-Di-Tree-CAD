using Clipper2Lib;
using TekstilCAD.Core;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

/// <summary>Fixture-specific measurements, not a topology or production safety certificate.</summary>
public sealed class OffsetCornerDifferenceInvestigationTests(ITestOutputHelper output)
{
    private const double Scale = 1_000_000;
    private static double Dot(Point2 a, Point2 b) => a.X * b.X + a.Y * b.Y;
    private static double Cross(Point2 a, Point2 b) => a.X * b.Y - a.Y * b.X;
    private static double Length(Point2 p) => Math.Sqrt(Dot(p, p));

    // Signed projection and signed perpendicular distance to an independently
    // shifted source edge. Subtraction is performed before dot/cross products.
    private static (double AlongMm, double PerpendicularMm) Project(Point2 p, Point2 start, Point2 end, int orientation)
    {
        var edge = end - start;
        var length = Length(edge);
        var unit = edge * (1 / length);
        var normal = new Point2(orientation * unit.Y, -orientation * unit.X);
        var relative = p - (start + normal);
        return (Dot(relative, unit), Cross(unit, relative));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuantizedLobedCornerReportsMiterAndAdjacentShiftedEdges(bool reverse)
    {
        var source = Enumerable.Range(0, 512).Select(i =>
        {
            var angle = Math.Tau * i / 512;
            var radius = 1000 + 200 * Math.Cos(3 * angle);
            return new Point2(Math.Round(radius * Math.Cos(angle) * Scale, MidpointRounding.AwayFromZero) / Scale,
                Math.Round(radius * Math.Sin(angle) * Scale, MidpointRounding.AwayFromZero) / Scale);
        }).ToList();
        if (reverse) source.Reverse();
        var path = new Path64(source.Select(p => new Point64(
            checked((long)Math.Round(p.X * Scale, MidpointRounding.AwayFromZero)),
            checked((long)Math.Round(p.Y * Scale, MidpointRounding.AwayFromZero)))));
        var actual = Assert.Single(Clipper.InflatePaths(new Paths64 { path }, Scale, JoinType.Miter, EndType.Polygon, 2.0))
            .Select(p => new Point2(p.X / Scale, p.Y / Scale)).ToList();
        Assert.Equal(515, actual.Count);
        var orientation = Math.Sign(Geometry.SignedArea(source));

        // Bisector of independently constructed outward unit normals gives the
        // infinite-line miter; this is not derived from Clipper's output.
        var miters = new List<Point2>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            var a = source[(i + source.Count - 1) % source.Count];
            var b = source[i]; var c = source[(i + 1) % source.Count];
            var u = b - a; var v = c - b;
            var n1 = new Point2(orientation * u.Y / Length(u), -orientation * u.X / Length(u));
            var n2 = new Point2(orientation * v.Y / Length(v), -orientation * v.X / Length(v));
            var denominator = 1 + Dot(n1, n2);
            Assert.True(denominator > 1e-9);
            miters.Add(b + (n1 + n2) * (1 / denominator));
        }

        var shortEdges = Enumerable.Range(0, actual.Count)
            .Where(i => Geometry.Distance(actual[i], actual[(i + 1) % actual.Count]) < .001).ToArray();
        Assert.NotEmpty(shortEdges);
        output.WriteLine($"reverse={reverse}; source={source.Count}; output={actual.Count}; shortEdges={shortEdges.Length}");
        foreach (var i in shortEdges)
        {
            var p = actual[i]; var q = actual[(i + 1) % actual.Count];
            var midpoint = p + (q - p) * .5;
            var corner = Enumerable.Range(0, miters.Count).MinBy(j => Geometry.Distance(midpoint, miters[j]));
            var prev = source[(corner + source.Count - 1) % source.Count];
            var pivot = source[corner]; var next = source[(corner + 1) % source.Count];
            var miter = miters[corner];
            var pPrev = Project(p, prev, pivot, orientation); var pNext = Project(p, pivot, next, orientation);
            var qPrev = Project(q, prev, pivot, orientation); var qNext = Project(q, pivot, next, orientation);
            Assert.True(new[] { pPrev.AlongMm, pPrev.PerpendicularMm, pNext.AlongMm, pNext.PerpendicularMm,
                qPrev.AlongMm, qPrev.PerpendicularMm, qNext.AlongMm, qNext.PerpendicularMm }.All(double.IsFinite));
            output.WriteLine($"edge={i}; sourceCorner={corner}; source=({pivot.X:G17},{pivot.Y:G17}); miter=({miter.X:G17},{miter.Y:G17})");
            output.WriteLine($"  p=({p.X:G17},{p.Y:G17}) miterDistance={Geometry.Distance(p,miter):G9} mm; previous=(along {pPrev.AlongMm:G9}, perp {pPrev.PerpendicularMm:G9}) mm; next=(along {pNext.AlongMm:G9}, perp {pNext.PerpendicularMm:G9}) mm");
            output.WriteLine($"  q=({q.X:G17},{q.Y:G17}) miterDistance={Geometry.Distance(q,miter):G9} mm; previous=(along {qPrev.AlongMm:G9}, perp {qPrev.PerpendicularMm:G9}) mm; next=(along {qNext.AlongMm:G9}, perp {qNext.PerpendicularMm:G9}) mm; length={Geometry.Distance(p,q):G9} mm");
        }
    }
}
