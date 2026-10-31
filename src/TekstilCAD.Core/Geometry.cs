namespace TekstilCAD.Core;

/// <summary>Independent double precision geometry in millimetres, with boundary-inclusive predicates.</summary>
public static class Geometry
{
    public const double Epsilon = 1e-8;
    /// <summary>Supported world-coordinate magnitude in mm (100 km per axis).</summary>
    public const double MaxCoordinate = 100_000_000;
    /// <summary>Bound for the quadratic production-contour validation pass.</summary>
    public const int MaxValidatedContourVertices = 4096;

    public static void Validate(Point2 point)
    {
        if (!point.IsFinite || Math.Abs(point.X) > MaxCoordinate || Math.Abs(point.Y) > MaxCoordinate)
            throw new ArgumentOutOfRangeException(nameof(point), "Coordinates must be finite and within ±100,000,000 mm.");
    }

    private static void Validate(IReadOnlyList<Point2> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        foreach (var point in points) Validate(point);
    }

    public static double Distance(Point2 a, Point2 b)
    {
        Validate(a); Validate(b);
        var dx = Math.Abs(a.X - b.X); var dy = Math.Abs(a.Y - b.Y);
        var max = Math.Max(dx, dy);
        if (max == 0) return 0;
        return FiniteResult(max * Math.Sqrt(Math.Pow(dx / max, 2) + Math.Pow(dy / max, 2)));
    }

    public static double SignedArea(IReadOnlyList<Point2> polygon)
    {
        Validate(polygon);
        if (polygon.Count < 3) return 0;
        // Translate to the first vertex to reduce cancellation for far-from-origin contours.
        var origin = polygon[0]; double sum = 0;
        for (var i = 1; i < polygon.Count - 1; i++) sum += Cross(polygon[i] - origin, polygon[i + 1] - origin);
        return FiniteResult(sum / 2);
    }

    public static Bounds2 Bounds(IReadOnlyList<Point2> points)
    {
        Validate(points);
        if (points.Count == 0) throw new ArgumentException("At least one point is required.", nameof(points));
        return new(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
    }

    public static List<Point2> Translate(IReadOnlyList<Point2> points, double dx, double dy)
    {
        Validate(points);
        // Displacements may span the whole supported world (200,000,000 mm).
        // The resulting coordinates, rather than the delta, have world limits.
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) throw new ArgumentOutOfRangeException(nameof(dx), "Taşıma miktarı sonlu olmalıdır.");
        var moved = Map(points, p => new(p.X + dx, p.Y + dy));
        TranslationPrecision.Validate(points, moved);
        return moved;
    }

    public static List<Point2> Rotate(IReadOnlyList<Point2> points, double degrees, Point2 origin = default)
    {
        Validate(points); Validate(origin);
        if (!double.IsFinite(degrees)) throw new ArgumentOutOfRangeException(nameof(degrees));
        var radians = (degrees % 360) * Math.PI / 180;
        var c = Math.Cos(radians); var s = Math.Sin(radians);
        return Map(points, p => new(origin.X + (p.X - origin.X) * c - (p.Y - origin.Y) * s,
            origin.Y + (p.X - origin.X) * s + (p.Y - origin.Y) * c));
    }

    /// <summary>Mirror across x=axisCoordinate when verticalAxis is true; otherwise y=axisCoordinate.</summary>
    public static List<Point2> Mirror(IReadOnlyList<Point2> points, bool verticalAxis, double axisCoordinate = 0)
    {
        Validate(points);
        if (!double.IsFinite(axisCoordinate) || Math.Abs(axisCoordinate) > MaxCoordinate) throw new ArgumentOutOfRangeException(nameof(axisCoordinate));
        return Map(points, p => verticalAxis ? new(2 * axisCoordinate - p.X, p.Y) : new(p.X, 2 * axisCoordinate - p.Y));
    }

    public static bool PointInPolygon(Point2 point, IReadOnlyList<Point2> polygon)
    {
        Validate(point); Validate(polygon);
        if (polygon.Count < 3) return false;
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[j]; var b = polygon[i];
            if (OnSegment(point, a, b)) return true;
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    public static bool SegmentsIntersect(Point2 a, Point2 b, Point2 c, Point2 d)
    {
        Validate(a); Validate(b); Validate(c); Validate(d);
        var o1 = Orientation(a, b, c); var o2 = Orientation(a, b, d);
        var o3 = Orientation(c, d, a); var o4 = Orientation(c, d, b);
        if (o1 * o2 < 0 && o3 * o4 < 0) return true;
        return o1 == 0 && OnSegment(c, a, b) || o2 == 0 && OnSegment(d, a, b)
            || o3 == 0 && OnSegment(a, c, d) || o4 == 0 && OnSegment(b, c, d);
    }

    public static bool HasSelfIntersections(IReadOnlyList<Point2> points, bool closed = true)
        => HasSelfIntersections(points, closed, CancellationToken.None);

    /// <summary>Checks the same contour predicates while observing cancellation during the quadratic scan.</summary>
    public static bool HasSelfIntersections(IReadOnlyList<Point2> points, bool closed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(points);
        var count = points.Count;
        // Accept the common explicit duplicate closing vertex.
        if (closed && count > 1 && points[0] == points[^1]) count--;
        var edges = closed ? count : count - 1;
        for (var i = 0; i < edges; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var a = points[i]; var b = points[(i + 1) % count];
            if (Distance(a, b) <= Epsilon) return true;
            for (var j = i + 1; j < edges; j++)
            {
                if ((j & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                var c = points[j]; var d = points[(j + 1) % count];
                if (j == i + 1 || closed && i == 0 && j == edges - 1)
                {
                    // Adjacent edges may share only their endpoint, never double back.
                    var common = j == i + 1 ? b : a;
                    var first = j == i + 1 ? a : b;
                    var second = j == i + 1 ? d : c;
                    if (Orientation(first, common, second) == 0 && Dot(first - common, second - common) > Epsilon) return true;
                    continue;
                }
                if (SegmentsIntersect(a, b, c, d)) return true;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    /// <summary>
    /// Validates a simple, closed production contour. An explicit final closing vertex is accepted.
    /// Open drawing entities should not use this method. Larger contours require a future spatial-index validator.
    /// </summary>
    public static void ValidateContour(IReadOnlyList<Point2> points)
        => ValidateContour(points, CancellationToken.None);

    /// <summary>Validates the same contour predicates with cooperative cancellation during the intersection scan.</summary>
    public static void ValidateContour(IReadOnlyList<Point2> points, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(points);
        cancellationToken.ThrowIfCancellationRequested();
        var count = points.Count;
        if (count > 1 && points[0] == points[^1]) count--;
        if (count < 3) throw new ArgumentException("A closed contour needs at least three distinct vertices.", nameof(points));
        if (count > MaxValidatedContourVertices)
            throw new ArgumentException($"Contour validation supports at most {MaxValidatedContourVertices} vertices.", nameof(points));
        cancellationToken.ThrowIfCancellationRequested();
        Validate(points);
        cancellationToken.ThrowIfCancellationRequested();
        var distinctCount = points.Take(count).Distinct().Count();
        cancellationToken.ThrowIfCancellationRequested();
        if (distinctCount != count)
            throw new ArgumentException("A closed contour contains repeated vertices.", nameof(points));
        cancellationToken.ThrowIfCancellationRequested();
        var area = SignedArea(points);
        cancellationToken.ThrowIfCancellationRequested();
        if (Math.Abs(area) <= Epsilon)
            throw new ArgumentException("A closed contour must have nonzero area.", nameof(points));
        var intersects = HasSelfIntersections(points, true, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (intersects)
            throw new ArgumentException("A closed contour must not cross or overlap itself.", nameof(points));
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static double FiniteResult(double result) => double.IsFinite(result) ? result
        : throw new ArithmeticException("Geometry calculation exceeded the supported numeric range.");

    public static List<Point2> SampleCubicBezier(Point2 p0, Point2 p1, Point2 p2, Point2 p3, int segments = 32)
    {
        Validate(p0); Validate(p1); Validate(p2); Validate(p3);
        if (segments < 1 || segments > 100_000) throw new ArgumentOutOfRangeException(nameof(segments));
        var result = new List<Point2>(segments + 1);
        for (var i = 0; i <= segments; i++)
        {
            var t = (double)i / segments; var u = 1 - t;
            var p = p0 * (u * u * u) + p1 * (3 * u * u * t) + p2 * (3 * u * t * t) + p3 * (t * t * t);
            Validate(p); result.Add(p);
        }
        return result;
    }

    private static List<Point2> Map(IReadOnlyList<Point2> points, Func<Point2, Point2> transform)
    {
        var result = new List<Point2>(points.Count);
        foreach (var point in points) { var value = transform(point); Validate(value); result.Add(value); }
        return result;
    }

    private static double Cross(Point2 a, Point2 b) => a.X * b.Y - a.Y * b.X;
    internal static double Dot(Point2 a, Point2 b) => a.X * b.X + a.Y * b.Y;
    internal static int Orientation(Point2 a, Point2 b, Point2 c)
    {
        var cross = Cross(b - a, c - a);
        var tolerance = Epsilon * Math.Max(1, Distance(a, b));
        return cross > tolerance ? 1 : cross < -tolerance ? -1 : 0;
    }

    // Only skip an existing SegmentsIntersect call when its endpoint-on-segment
    // branches are excluded in both padded comparison directions AND the first
    // two orientation signs exclude its proper-crossing branch. If either
    // condition is uncertain, use SegmentsIntersect unchanged.
    internal static bool CanSkipDisjointSegments(Point2 a, Point2 b, Point2 c, Point2 d)
    {
        static bool Separated(double a, double b, double c, double d)
        {
            var lo1 = Math.Min(a, b); var hi1 = Math.Max(a, b);
            var lo2 = Math.Min(c, d); var hi2 = Math.Max(c, d);
            return (lo2 > hi1 + Epsilon && hi1 < lo2 - Epsilon)
                || (lo1 > hi2 + Epsilon && hi2 < lo1 - Epsilon);
        }
        if (!Separated(a.X, b.X, c.X, d.X) && !Separated(a.Y, b.Y, c.Y, d.Y)) return false;
        return Orientation(a, b, c) * Orientation(a, b, d) >= 0;
    }

    private static bool OnSegment(Point2 p, Point2 a, Point2 b) => Orientation(a, b, p) == 0
        && p.X >= Math.Min(a.X, b.X) - Epsilon && p.X <= Math.Max(a.X, b.X) + Epsilon
        && p.Y >= Math.Min(a.Y, b.Y) - Epsilon && p.Y <= Math.Max(a.Y, b.Y) + Epsilon;
}
