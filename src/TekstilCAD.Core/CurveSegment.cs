namespace TekstilCAD.Core;

/// <summary>A single parametric contour segment. Curves keep their exact
/// mathematical representation instead of being baked into a polyline.</summary>
public abstract record CurveSegment
{
    /// <summary>Parametric length of the segment in millimetres.</summary>
    public abstract double Length { get; }

    /// <summary>Point on the segment at parameter t in [0, 1]. Out-of-range
    /// values are clamped.</summary>
    public abstract Point2 PointAt(double t);

    /// <summary>A straight line segment.</summary>
    public sealed record Line(Point2 Start, Point2 End) : CurveSegment
    {
        public override double Length => Geometry.Distance(Start, End);
        public override Point2 PointAt(double t)
        {
            t = Clamp(t);
            return Start + (End - Start) * t;
        }
    }

    /// <summary>A cubic Bézier segment defined by two endpoints and two
    /// control points.</summary>
    public sealed record CubicBezier(Point2 P0, Point2 P1, Point2 P2, Point2 P3) : CurveSegment
    {
        public override double Length => LengthFlattened(P0, P1, P2, P3, BezierFlatnessTolerance);
        public override Point2 PointAt(double t)
        {
            t = Clamp(t);
            var u = 1 - t;
            return P0 * (u * u * u) + P1 * (3 * u * u * t) + P2 * (3 * u * t * t) + P3 * (t * t * t);
        }
    }

    /// <summary>A circular arc segment defined by centre, radius, start angle
    /// and sweep angle (radians, counter-clockwise positive).</summary>
    public sealed record Arc(Point2 Center, double Radius, double StartAngle, double SweepAngle) : CurveSegment
    {
        public override double Length => Math.Abs(Radius * SweepAngle);
        public override Point2 PointAt(double t)
        {
            t = Clamp(t);
            var angle = StartAngle + SweepAngle * t;
            return new Point2(Center.X + Radius * Math.Cos(angle), Center.Y + Radius * Math.Sin(angle));
        }
    }

    private const double BezierFlatnessTolerance = 0.25; // millimetres

    private static double Clamp(double t) => Math.Clamp(t, 0.0, 1.0);

    /// <summary>Approximate chord length of a cubic Bézier by recursive
    /// de Casteljau subdivision until the control polygon is flat.</summary>
    private static double LengthFlattened(Point2 p0, Point2 p1, Point2 p2, Point2 p3, double tolerance)
    {
        var length = 0.0;
        FlattenLength(p0, p1, p2, p3, tolerance, ref length);
        return length;
    }

    private static void FlattenLength(Point2 a, Point2 b, Point2 c, Point2 d, double tolerance, ref double length)
    {
        var flatness = Math.Max(DistancePointToSegment(b, a, d), DistancePointToSegment(c, a, d));
        var span = Math.Max(Geometry.Distance(a, b), Math.Max(Geometry.Distance(b, c), Geometry.Distance(c, d)));
        if (flatness <= tolerance || span <= Geometry.Epsilon)
        {
            length += Geometry.Distance(a, d);
            return;
        }
        var ab = (a + b) * 0.5;
        var bc = (b + c) * 0.5;
        var cd = (c + d) * 0.5;
        var abbc = (ab + bc) * 0.5;
        var bccd = (bc + cd) * 0.5;
        var abbccd = (abbc + bccd) * 0.5;
        FlattenLength(a, ab, abbc, abbccd, tolerance, ref length);
        FlattenLength(abbccd, bccd, cd, d, tolerance, ref length);
    }

    internal static double DistancePointToSegment(Point2 p, Point2 a, Point2 b)
    {
        var ab = b - a;
        var ab2 = Geometry.Dot(ab, ab);
        if (ab2 <= Geometry.Epsilon) return Geometry.Distance(p, a);
        var t = Geometry.Dot(p - a, ab) / ab2;
        t = Clamp(t);
        var closest = a + ab * t;
        return Geometry.Distance(p, closest);
    }
}

/// <summary>Sampling and measurement helpers for parametric curve segments.</summary>
public static class CurveSampler
{
    /// <summary>Sample every segment into one continuous polyline whose chord
    /// deviation from each parametric curve stays within <paramref name="toleranceMm"/>.</summary>
    public static List<Point2> Sample(IReadOnlyList<CurveSegment> segments, double toleranceMm)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (!double.IsFinite(toleranceMm) || toleranceMm <= 0)
            throw new ArgumentOutOfRangeException(nameof(toleranceMm));
        var result = new List<Point2>();
        for (var i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            var points = SampleSegment(seg, toleranceMm);
            if (points.Count == 0) continue;
            // Consecutive segments share an endpoint: emit it only once.
            if (i > 0 && result.Count > 0 && points[0] == result[^1])
                points.RemoveAt(0);
            result.AddRange(points);
        }
        return result;
    }

    /// <summary>Total parametric length of all segments in millimetres.</summary>
    public static double TotalLength(IReadOnlyList<CurveSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var sum = 0.0;
        foreach (var seg in segments) sum += seg.Length;
        return sum;
    }

    private static List<Point2> SampleSegment(CurveSegment seg, double tolerance)
    {
        return seg switch
        {
            CurveSegment.Line line => new List<Point2> { line.Start, line.End },
            CurveSegment.CubicBezier bez => SampleBezier(bez, tolerance),
            CurveSegment.Arc arc => SampleArc(arc, tolerance),
            _ => throw new ArgumentOutOfRangeException(nameof(seg), "Desteklenmeyen eğri segmenti.")
        };
    }

    private static List<Point2> SampleBezier(CurveSegment.CubicBezier bez, double tolerance)
    {
        var result = new List<Point2> { bez.P0 };
        Flatten(result, bez.P0, bez.P1, bez.P2, bez.P3, tolerance);
        return result;
    }

    private static List<Point2> SampleArc(CurveSegment.Arc arc, double tolerance)
    {
        if (!double.IsFinite(arc.Radius) || arc.Radius <= 0)
            throw new ArgumentOutOfRangeException(nameof(arc), "Yay yarıçapı pozitif ve sınırlı olmalıdır.");
        var sweepAbs = Math.Abs(arc.SweepAngle);
        if (sweepAbs <= Geometry.Epsilon) return new List<Point2> { arc.PointAt(0) };
        // Maximum sub-sweep whose chord sagitta stays within tolerance:
        // sagitta = r * (1 - cos(delta/2)) <= tolerance.
        var ratio = tolerance / Math.Abs(arc.Radius);
        if (ratio >= 1.0) ratio = 0.999;
        var delta = 2.0 * Math.Acos(1.0 - ratio);
        var count = Math.Max(1, (int)Math.Ceiling(sweepAbs / delta));
        var result = new List<Point2>(count + 1);
        for (var k = 0; k <= count; k++) result.Add(arc.PointAt((double)k / count));
        return result;
    }

    private static void Flatten(List<Point2> result, Point2 a, Point2 b, Point2 c, Point2 d, double tolerance)
    {
        var flatness = Math.Max(CurveSegment.DistancePointToSegment(b, a, d), CurveSegment.DistancePointToSegment(c, a, d));
        var span = Math.Max(Geometry.Distance(a, b), Math.Max(Geometry.Distance(b, c), Geometry.Distance(c, d)));
        if (flatness <= tolerance || span <= Geometry.Epsilon)
        {
            result.Add(d);
            return;
        }
        var ab = (a + b) * 0.5;
        var bc = (b + c) * 0.5;
        var cd = (c + d) * 0.5;
        var abbc = (ab + bc) * 0.5;
        var bccd = (bc + cd) * 0.5;
        var abbccd = (abbc + bccd) * 0.5;
        Flatten(result, a, ab, abbc, abbccd, tolerance);
        Flatten(result, abbccd, bccd, cd, d, tolerance);
    }
}
