using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public class CurveSegmentTests
{
    private static Point2 P(double x, double y) => new(x, y);

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
        Assert.Equal(P(100, 0), seg.PointAt(1));
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
        Assert.Equal(P(0, 100), seg.PointAt(1));
        // t=0.5 -> 45°.
        var mid = seg.PointAt(0.5);
        Assert.Equal(70.71, mid.X, 2);
        Assert.Equal(70.71, mid.Y, 2);
    }

    [Fact]
    public void ArcNegativeSweepGoesClockwise()
    {
        var seg = new CurveSegment.Arc(P(0, 0), 100, 0, -Math.PI / 2);
        Assert.Equal(P(0, -100), seg.PointAt(1));
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
        // Every segment between consecutive samples should be short (flatness bound).
        for (var i = 1; i < points.Count; i++)
            Assert.True(Geometry.Distance(points[i - 1], points[i]) < 1, $"Sample step too long at {i}");
    }

    [Fact]
    public void SampleArcApproximatesCircle()
    {
        var seg = new CurveSegment.Arc(P(0, 0), 100, 0, 2 * Math.PI);
        var points = CurveSampler.Sample([seg], 0.1);
        Assert.Equal(P(100, 0), points[0]);
        Assert.Equal(P(100, 0), points[^1]);
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
    public void ArcRadiusMustBePositive()
    {
        var seg = new CurveSegment.Arc(P(0, 0), 0, 0, Math.PI);
        Assert.Throws<ArgumentOutOfRangeException>(() => CurveSampler.Sample([seg], 0.1));
    }
}
