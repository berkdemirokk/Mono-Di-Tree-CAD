using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public class CurveSegmentTests
{
    private static Point2 P(double x, double y) => new(x, y);

    // Trigonometric endpoints (pi/2, 2*pi) are only approximately representable.
    private static void AssertPointNear(Point2 expected, Point2 actual, double tolerance = 1e-9)
    {
        Assert.InRange(Math.Abs(actual.X - expected.X), 0, tolerance);
        Assert.InRange(Math.Abs(actual.Y - expected.Y), 0, tolerance);
    }

    // Measure the actual Bézier-to-polyline error, not the arbitrary chord length.
    private static double DistanceToPolyline(Point2 p, IReadOnlyList<Point2> points)
    {
        var best = double.PositiveInfinity;
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var lengthSquared = dx * dx + dy * dy;
            var t = lengthSquared == 0 ? 0 : Math.Clamp(
                ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
            var ex = p.X - a.X - t * dx;
            var ey = p.Y - a.Y - t * dy;
            best = Math.Min(best, Math.Sqrt(ex * ex + ey * ey));
        }
        return best;
    }

    [Fact]
    public void LinePointAtEndpointsAndMidpoint()
    {
        var seg = new CurveSegment.Line(P(0, 0), P(10, 0));
        Assert.Equal(P(0, 0), seg.PointAt(0));
        Assert.Equal(P(10, 0), seg.PointAt(1));
        Assert.Equal(P(5, 0), seg.PointAt(0.5));
        Assert.Equal(P(2, 0), seg.PointAt(0.2));
        Assert.Equal(10, seg.Length, 12);
        Assert.Equal(10, CurveSampler.TotalLength([seg]), 12);
    }

    [Fact]
    public void CubicBezierPointAtEndpointsAndMidpoint()
    {
        var seg = new CurveSegment.CubicBezier(P(0, 0), P(0, 10), P(10, 10), P(10, 0));
        Assert.Equal(P(0, 0), seg.PointAt(0));
        Assert.Equal(P(10, 0), seg.PointAt(1));
        // t=0.5 -> (5, 7.5) by de Casteljau.
        Assert.Equal(P(5, 7.5), seg.PointAt(0.5));
        // Length exceeds the straight chord from P0 to P3.
        Assert.True(seg.Length > Geometry.Distance(P(0, 0), P(10, 0)));
        Assert.True(seg.Length > 0);
    }

    [Fact]
    public void CubicBezierClampedParametersStayInRange()
    {
        var seg = new CurveSegment.CubicBezier(P(0, 0), P(0, 10), P(10, 10), P(10, 0));
        Assert.Equal(P(0, 0), seg.PointAt(-100));
        Assert.Equal(P(10, 0), seg.PointAt(100));
    }

    [Fact]
    public void ArcFullCircleLengthAndEndpoints()
    {
        var seg = new CurveSegment.Arc(P(0, 0), 100, 0, 2 * Math.PI);
        Assert.Equal(P(100, 0), seg.PointAt(0));
        AssertPointNear(P(100, 0), seg.PointAt(1));
        // Full circle length = 2 * pi * r.
        Assert.Equal(2 * Math.PI * 100, seg.Length, 10);
        Assert.Equal(2 * Math.PI * 100, CurveSampler.TotalLength([seg]), 10);
    }

    [Fact]
    public void ArcQuarterCirclePointAtQuarter()
    {
        // 90° arc from (r,0) to (0,r) CCW.
        var seg = new CurveSegment.Arc(P(0, 0), 100, 0, Math.PI / 2);
        Assert.Equal(P(100, 0), seg.PointAt(0));
        AssertPointNear(P(0, 100), seg.PointAt(1));
        // t=0.5 -> 45°.
        var mid = seg.PointAt(0.5);
        Assert.Equal(70.71, mid.X, 2);
        Assert.Equal(70.71, mid.Y, 2);
    }

    [Fact]
    public void ArcNegativeSweepGoesClockwise()
    {
        var seg = new CurveSegment.Arc(P(0, 0), 100, 0, -Math.PI / 2);
        AssertPointNear(P(0, -100), seg.PointAt(1));
        Assert.Equal(Math.PI / 2 * 100, seg.Length, 10);
    }

    [Fact]
    public void SampleLineProducesTwoPoints()
    {
        var seg = new CurveSegment.Line(P(0, 0), P(10, 0));
        var points = CurveSampler.Sample([seg], 0.1);
        Assert.Equal([P(0, 0), P(10, 0)], points);
    }

    [Fact]
    public void SampleBezierMatchesEndpointsAndIsSmooth()
    {
        var seg = new CurveSegment.CubicBezier(P(0, 0), P(0, 10), P(10, 10), P(10, 0));
        var points = CurveSampler.Sample([seg], 0.1);
        Assert.Equal(P(0, 0), points[0]);
        Assert.Equal(P(10, 0), points[^1]);
        Assert.True(points.Count >= 2);
        // The sampling contract is a maximum chord deviation, not a maximum
        // segment length. Check independent analytic points against the polyline.
        for (var i = 0; i <= 1_000; i++)
        {
            var actual = seg.PointAt(i / 1_000d);
            var deviation = DistanceToPolyline(actual, points);
            Assert.True(deviation <= 0.100001,
                $"Curve deviates {deviation} mm at parameter {i}/1000.");
        }
    }

    [Fact]
    public void SampleArcApproximatesCircle()
    {
        var seg = new CurveSegment.Arc(P(0, 0), 100, 0, 2 * Math.PI);
        var points = CurveSampler.Sample([seg], 0.1);
        Assert.Equal(P(100, 0), points[0]);
        AssertPointNear(P(100, 0), points[^1]);
        // All samples lie close to the radius circle.
        foreach (var p in points)
            Assert.True(Math.Abs(Geometry.Distance(P(0, 0), p) - 100) < 0.5, $"Point {p} off circle");
    }

    [Fact]
    public void SampleConsecutiveSegmentsShareEndpointOnce()
    {
        var segs = new CurveSegment[]
        {
            new CurveSegment.Line(P(0, 0), P(10, 0)),
            new CurveSegment.Line(P(10, 0), P(10, 10)),
            new CurveSegment.Line(P(10, 10), P(0, 10))
        };
        var points = CurveSampler.Sample(segs, 0.1);
        // Square perimeter: shared corners emitted once.
        Assert.Equal(P(0, 0), points[0]);
        Assert.Equal(P(10, 0), points[1]);
        Assert.Equal(P(10, 10), points[2]);
        Assert.Equal(P(0, 10), points[3]);
        Assert.Equal(4, points.Count);
    }

    [Fact]
    public void InvalidToleranceRejected()
    {
        var seg = new CurveSegment.Line(P(0, 0), P(10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CurveSampler.Sample([seg], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CurveSampler.Sample([seg], -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CurveSampler.Sample([seg], double.NaN));
        Assert.Throws<ArgumentNullException>(() => CurveSampler.Sample(null!, 0.1));
    }

    [Fact]
    public void ArcTinyToleranceIsRejectedInsteadOfSilentlyFlattening()
    {
        var arc = new CurveSegment.Arc(P(0, 0), 100, 0, Math.PI / 2);
        Assert.Throws<InvalidDataException>(() => CurveSampler.Sample([arc], 1e-30));
    }

    [Fact]
    public void ArcExcessiveSweepIsRejectedBeforeAllocation()
    {
        var arc = new CurveSegment.Arc(P(0, 0), 100, 0, 1_000_000_000);
        Assert.Throws<InvalidDataException>(() => CurveSampler.Sample([arc], 0.01));
    }

    [Fact]
    public void ArcNonFiniteAnglesAreRejected()
    {
        var arc = new CurveSegment.Arc(P(0, 0), 100, double.NaN, Math.PI);
        Assert.Throws<ArgumentOutOfRangeException>(() => CurveSampler.Sample([arc], 0.1));
    }

    [Fact]
    public void InvalidLineAndBezierCoordinatesCannotBeSampled()
    {
        var line = new CurveSegment.Line(P(double.NaN, 0), P(10, 0));
        var bezier = new CurveSegment.CubicBezier(P(0, 0), P(0, 1e12), P(10, 0), P(10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => CurveSampler.Sample([line], 0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CurveSampler.Sample([bezier], 0.1));
    }

    [Fact]
    public void ArcRadiusMustBePositive()
    {
        var seg = new CurveSegment.Arc(P(0, 0), 0, 0, Math.PI);
        Assert.Throws<ArgumentOutOfRangeException>(() => CurveSampler.Sample([seg], 0.1));
    }
}
