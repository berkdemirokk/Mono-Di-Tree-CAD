namespace TekstilCAD.Core;

/// <summary>
/// Experimental cancellable validation of a single closed contour with at most
/// 10,000 stored points. This uses the same predicates as <see cref="Geometry"/>
/// and considers every edge pair; a conservative disjoint-pair check avoids some
/// full predicates, but worst-case work remains quadratic.
/// </summary>
public static class LargeContourValidator
{
    private readonly record struct EdgeBounds(double MinX, double MaxX, double MinY, double MaxY);

    // Match Geometry.Orientation's arithmetic and tolerance exactly. The edge
    // tolerance is invariant over the inner loop, so calculate it only once.
    private static int OrientationWithTolerance(Point2 a, Point2 b, Point2 p, double tolerance)
    {
        var edge = b - a; var relative = p - a;
        var cross = edge.X * relative.Y - edge.Y * relative.X;
        return cross > tolerance ? 1 : cross < -tolerance ? -1 : 0;
    }

    private static bool Separated(double lo1, double hi1, double lo2, double hi2) =>
        (lo2 > hi1 + Geometry.Epsilon && hi1 < lo2 - Geometry.Epsilon) ||
        (lo1 > hi2 + Geometry.Epsilon && hi2 < lo1 - Geometry.Epsilon);
    // The project format limits stored points, including a repeated closing point.
    public const int MaximumStoredPoints = 10_000;

    /// <summary>
    /// Validates a private snapshot. A caller must not mutate the input during
    /// snapshot creation; cancellation is observed at least every 1,024 pairs.
    /// An explicit repeated closing point is treated as a single vertex.
    /// </summary>
    public static void Validate(IReadOnlyList<Point2> points, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        cancellationToken.ThrowIfCancellationRequested();
        var storedCount = points.Count;
        if (storedCount > MaximumStoredPoints)
            throw new ArgumentException($"Contour validation supports at most {MaximumStoredPoints} stored points.", nameof(points));
        var snapshot = new Point2[storedCount];
        for (var i = 0; i < storedCount; i++)
        {
            if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            snapshot[i] = points[i];
        }
        var count = storedCount;
        if (count > 1 && snapshot[0] == snapshot[^1]) count--;
        if (count < 3) throw new ArgumentException("A closed contour needs at least three distinct vertices.", nameof(points));
        for (var i = 0; i < storedCount; i++)
        {
            if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            Geometry.Validate(snapshot[i]);
        }
        var unique = new HashSet<Point2>();
        for (var i = 0; i < count; i++)
            if (!unique.Add(snapshot[i])) throw new ArgumentException("A closed contour contains repeated vertices.", nameof(points));
        cancellationToken.ThrowIfCancellationRequested();
        if (Math.Abs(Geometry.SignedArea(snapshot)) <= Geometry.Epsilon)
            throw new ArgumentException("A closed contour must have nonzero area.", nameof(points));

        var bounds = new EdgeBounds[count];
        for (var i = 0; i < count; i++)
        {
            if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            var a = snapshot[i]; var b = snapshot[(i + 1) % count];
            bounds[i] = new(Math.Min(a.X, b.X), Math.Max(a.X, b.X),
                Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y));
        }
        long comparisons = 0;
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var a = snapshot[i]; var b = snapshot[(i + 1) % count];
            var distance = Geometry.Distance(a, b);
            if (distance <= Geometry.Epsilon)
                throw new ArgumentException("A closed contour must not cross or overlap itself.", nameof(points));
            var tolerance = Geometry.Epsilon * Math.Max(1, distance);
            var box = bounds[i];
            for (var j = i + 1; j < count; j++)
            {
                if ((comparisons++ & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                var c = snapshot[j]; var d = snapshot[(j + 1) % count];
                if (j == i + 1 || i == 0 && j == count - 1)
                {
                    var common = j == i + 1 ? b : a;
                    var first = j == i + 1 ? a : b;
                    var second = j == i + 1 ? d : c;
                    if (Geometry.AdjacentEdgesOverlap(first, common, second))
                        throw new ArgumentException("A closed contour must not cross or overlap itself.", nameof(points));
                }
                else
                {
                    var other = bounds[j];
                    // Preserve both padded comparisons and the orientation gate:
                    // disjoint boxes alone are not safe under floating-point arithmetic.
                    var separated = Separated(box.MinX, box.MaxX, other.MinX, other.MaxX) ||
                        Separated(box.MinY, box.MaxY, other.MinY, other.MaxY);
                    if (separated && OrientationWithTolerance(a, b, c, tolerance) *
                        OrientationWithTolerance(a, b, d, tolerance) >= 0) continue;
                    if (Geometry.SegmentsIntersect(a, b, c, d))
                        throw new ArgumentException("A closed contour must not cross or overlap itself.", nameof(points));
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

}
