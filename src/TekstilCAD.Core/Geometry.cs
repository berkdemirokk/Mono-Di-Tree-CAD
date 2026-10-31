using System.Numerics;

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
        var origin = polygon[0]; double sum = 0, magnitude = 0;
        for (var i = 1; i < polygon.Count - 1; i++)
        {
            var a = polygon[i] - origin; var b = polygon[i + 1] - origin;
            var left = a.X * b.Y; var right = a.Y * b.X;
            sum += left - right;
            magnitude += Math.Abs(left) + Math.Abs(right);
        }
        // Near cancellation can erase a real area or reverse its orientation.
        // Include accumulation error for long contours as well as errors in
        // subtraction and multiplication. Double.Epsilon is the smallest
        // subnormal, not the rounding unit of IEEE double precision.
        var roundingBound = Math.Max(1e-12, (polygon.Count + 8d) * 2.220446049250313e-16);
        if (magnitude > 0 && Math.Abs(sum) <= magnitude * roundingBound)
            return ExactSignedArea(polygon);
        return FiniteResult(sum / 2);
    }

    private static double ExactSignedArea(IReadOnlyList<Point2> polygon)
    {
        // Every finite double is an integer multiple of 2^-1074.
        static BigInteger Scaled(double value)
        {
            var bits = BitConverter.DoubleToInt64Bits(value);
            var exponent = (int)((bits >> 52) & 0x7ff);
            var fraction = (ulong)bits & 0x000f_ffff_ffff_ffffUL;
            var mantissa = exponent == 0 ? fraction : fraction | (1UL << 52);
            var integer = new BigInteger(mantissa) << (exponent == 0 ? 0 : exponent - 1);
            return bits < 0 ? -integer : integer;
        }

        var ox = Scaled(polygon[0].X); var oy = Scaled(polygon[0].Y);
        BigInteger sum = 0;
        var ax = Scaled(polygon[1].X) - ox; var ay = Scaled(polygon[1].Y) - oy;
        for (int i = 2; i < polygon.Count; i++)
        {
            var bx = Scaled(polygon[i].X) - ox; var by = Scaled(polygon[i].Y) - oy;
            sum += ax * by - ay * bx;
            ax = bx; ay = by;
        }
        if (sum.IsZero) return 0;
        // Reduce before conversion, retaining extra bits for rounding.
        int shift = (int)Math.Max(0, BigInteger.Abs(sum).GetBitLength() - 60);
        return FiniteResult(Math.ScaleB((double)(sum >> shift), shift - 2149));
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
        var angle = degrees % 360;
        // A complete turn is an identity even when subtracting a distant pivot
        // would round away small coordinates. Return a new list as usual.
        if (angle == 0) return new List<Point2>(points);
        // Exact quarter-turn coefficients avoid injecting trig approximation
        // errors into the much more common orthogonal transformations.
        var (c, s) = angle switch
        {
            90 or -270 => (0d, 1d),
            180 or -180 => (-1d, 0d),
            270 or -90 => (0d, -1d),
            _ => (Math.Cos(angle * Math.PI / 180), Math.Sin(angle * Math.PI / 180))
        };
        var rotated = Map(points, p => new(origin.X + (p.X - origin.X) * c - (p.Y - origin.Y) * s,
            origin.Y + (p.X - origin.X) * s + (p.Y - origin.Y) * c));
        if (angle is 90 or -270 or 180 or -180 or 270 or -90)
        {
            // A quadrant rotation about a pivot differs from the exact rotation
            // about zero only by a translation. Check that rounding did not
            // destroy the represented edges or axis spans during that shift.
            var aroundZero = Map(points, p => new(p.X * c - p.Y * s, p.X * s + p.Y * c));
            try { TranslationPrecision.Validate(aroundZero, rotated); }
            catch (ArgumentException ex)
            { throw new ArgumentException("Döndürme sırasında parça şekli sayısal hassasiyetle korunamıyor. Daha küçük koordinatlar kullanın.", nameof(points), ex); }
            ValidateOrthogonalRotationPositions(points, rotated, origin, (int)c, (int)s);
        }
        return rotated;
    }

    // Compare with the exact dyadic result, including its absolute position.
    // Vector checks alone cannot detect an equal displacement of every point.
    private static void ValidateOrthogonalRotationPositions(IReadOnlyList<Point2> points,
        IReadOnlyList<Point2> rotated, Point2 origin, int c, int s)
    {
        static BigInteger Scaled(double value)
        {
            var bits = BitConverter.DoubleToInt64Bits(value);
            var exponent = (int)((bits >> 52) & 0x7ff);
            var fraction = (ulong)bits & 0x000f_ffff_ffff_ffffUL;
            var mantissa = exponent == 0 ? fraction : fraction | (1UL << 52);
            var integer = new BigInteger(mantissa) << (exponent == 0 ? 0 : exponent - 1);
            return bits < 0 ? -integer : integer;
        }

        var ox = Scaled(origin.X); var oy = Scaled(origin.Y);
        for (var i = 0; i < points.Count; i++)
        {
            var x = Scaled(points[i].X); var y = Scaled(points[i].Y);
            var expectedX = ox + (x - ox) * c - (y - oy) * s;
            var expectedY = oy + (x - ox) * s + (y - oy) * c;
            var errorX = BigInteger.Abs(Scaled(rotated[i].X) - expectedX);
            var errorY = BigInteger.Abs(Scaled(rotated[i].Y) - expectedY);
            if (errorX * 1_000_000_000 > BigInteger.Abs(expectedX)
                || errorY * 1_000_000_000 > BigInteger.Abs(expectedY))
                throw new ArgumentException("Döndürme sırasında nokta konumu sayısal hassasiyetle korunamıyor. Daha küçük koordinatlar kullanın.", nameof(points));
        }
    }

    /// <summary>Mirror across x=axisCoordinate when verticalAxis is true; otherwise y=axisCoordinate.</summary>
    public static List<Point2> Mirror(IReadOnlyList<Point2> points, bool verticalAxis, double axisCoordinate = 0)
    {
        Validate(points);
        if (!double.IsFinite(axisCoordinate) || Math.Abs(axisCoordinate) > MaxCoordinate) throw new ArgumentOutOfRangeException(nameof(axisCoordinate));
        var mirrored = Map(points, p => verticalAxis ? new(2 * axisCoordinate - p.X, p.Y) : new(p.X, 2 * axisCoordinate - p.Y));
        // Reflection is a sign change followed by a translation. Verify that
        // rounding at a distant axis did not collapse or distort the shape.
        var reflected = points.Select(p => verticalAxis ? new Point2(-p.X, p.Y) : new Point2(p.X, -p.Y)).ToList();
        try { TranslationPrecision.Validate(reflected, mirrored); }
        catch (ArgumentException ex)
        { throw new ArgumentException("Aynalama sırasında parça şekli sayısal hassasiyetle korunamıyor. Daha küçük koordinatlar kullanın.", nameof(points), ex); }
        return mirrored;
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
                    if (AdjacentEdgesOverlap(first, common, second)) return true;
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

    // Preserve the existing conservative tolerance for near-overlap, and also
    // catch exact overlaps too short for its dot-product threshold. Comparing
    // coordinates avoids a rounded dot product in the exact check; its
    // determinant uses the exact values represented by binary64.
    internal static bool AdjacentEdgesOverlap(Point2 first, Point2 common, Point2 second)
    {
        if (Orientation(first, common, second) == 0 && Dot(first - common, second - common) > Epsilon)
            return true;
        var sameDirection = first.X != common.X
            ? (first.X > common.X) == (second.X > common.X) && second.X != common.X
            : first.Y != common.Y && second.Y != common.Y && (first.Y > common.Y) == (second.Y > common.Y);
        if (!sameDirection) return false;

        static BigInteger ScaledInteger(double value)
        {
            var bits = BitConverter.DoubleToInt64Bits(value);
            var exponent = (int)((bits >> 52) & 0x7ff);
            var fraction = (ulong)bits & 0x000f_ffff_ffff_ffffUL;
            var mantissa = exponent == 0 ? fraction : fraction | (1UL << 52);
            var result = new BigInteger(mantissa) << (exponent == 0 ? 0 : exponent - 1);
            return bits < 0 ? -result : result;
        }

        var ax = ScaledInteger(first.X) - ScaledInteger(common.X);
        var ay = ScaledInteger(first.Y) - ScaledInteger(common.Y);
        var bx = ScaledInteger(second.X) - ScaledInteger(common.X);
        var by = ScaledInteger(second.Y) - ScaledInteger(common.Y);
        return ax * by == ay * bx;
    }
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
