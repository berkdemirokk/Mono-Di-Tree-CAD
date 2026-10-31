namespace TekstilCAD.Core;

/// <summary>
/// Experimental pairwise contour validator with a conservative broad phase. Not used
/// by the editor or a production safety certificate. Input must remain stable while
/// the private snapshot is copied. Worst-case work remains quadratic.
/// </summary>
public static class LargeContourBroadphaseDiagnostic
{
    public sealed record Statistics(long PairCount, long BroadphaseRejected, long PredicateCalls);

    public static Statistics Validate(IReadOnlyList<Point2> points, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        cancellationToken.ThrowIfCancellationRequested();
        var storedCount = points.Count;
        if (storedCount > LargeContourValidator.MaximumStoredPoints)
            throw new ArgumentException("Contour validation exceeds the stored point limit.", nameof(points));
        var snapshot = new Point2[storedCount];
        for (var i = 0; i < storedCount; i++)
        {
            if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            snapshot[i] = points[i];
            Geometry.Validate(snapshot[i]);
        }
        var count = storedCount;
        if (count > 1 && snapshot[0] == snapshot[^1]) count--;
        if (count < 3) throw new ArgumentException("A closed contour needs at least three distinct vertices.", nameof(points));
        var unique = new HashSet<Point2>();
        for (var i = 0; i < count; i++)
            if (!unique.Add(snapshot[i])) throw new ArgumentException("A closed contour contains repeated vertices.", nameof(points));
        cancellationToken.ThrowIfCancellationRequested();
        if (Math.Abs(Geometry.SignedArea(snapshot)) <= Geometry.Epsilon)
            throw new ArgumentException("A closed contour must have nonzero area.", nameof(points));

        long pairs = 0, rejected = 0, predicates = 0;
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var a = snapshot[i]; var b = snapshot[(i + 1) % count];
            if (Geometry.Distance(a, b) <= Geometry.Epsilon)
                throw new ArgumentException("A closed contour must not cross or overlap itself.", nameof(points));
            for (var j = i + 1; j < count; j++)
            {
                if ((pairs++ & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                var c = snapshot[j]; var d = snapshot[(j + 1) % count];
                if (j == i + 1 || i == 0 && j == count - 1)
                {
                    var common = j == i + 1 ? b : a;
                    var first = j == i + 1 ? a : b;
                    var second = j == i + 1 ? d : c;
                    if (Geometry.Orientation(first, common, second) == 0 && Geometry.Dot(first - common, second - common) > Geometry.Epsilon)
                        throw new ArgumentException("A closed contour must not cross or overlap itself.", nameof(points));
                    continue;
                }
                // For a safe skip, the boxes must be separated in *both* of the
                // padded comparisons used by OnSegment. This prevents every
                // endpoint-on-segment case in the existing predicate, including
                // around a representable-value boundary. We still compute its
                // first two orientation values using the same arithmetic: if
                // they are opposite, the proper-crossing branch might be true
                // because of floating-point cancellation and we fall back.
                if (Geometry.CanSkipDisjointSegments(a, b, c, d))
                { rejected++; continue; }
                predicates++;
                if (Geometry.SegmentsIntersect(a, b, c, d))
                    throw new ArgumentException("A closed contour must not cross or overlap itself.", nameof(points));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new Statistics(pairs, rejected, predicates);
    }

}
