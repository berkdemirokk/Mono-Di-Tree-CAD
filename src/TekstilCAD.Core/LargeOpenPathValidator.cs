namespace TekstilCAD.Core;

/// <summary>
/// Cancellable validation of an open path with at most 10,000 stored points.
/// Edge intersection semantics match <see cref="Geometry.HasSelfIntersections"/>.
/// Work is bounded but quadratic in the worst case; desktop large-path completion runs it off the UI thread.
/// </summary>
public static class LargeOpenPathValidator
{
    public const int MaximumStoredPoints = 10_000;

    /// <summary>
    /// Validates a private snapshot; callers must not mutate the input while it is
    /// being copied. Cancellation is checked at least once every 1,024 edge pairs.
    /// Open paths do not treat coincident first and last points as an implicit closure.
    /// </summary>
    public static void Validate(IReadOnlyList<Point2> points, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        cancellationToken.ThrowIfCancellationRequested();
        var count = points.Count;
        if (count < 2 || count > MaximumStoredPoints)
            throw new ArgumentException($"An open path requires 2 to {MaximumStoredPoints} stored points.", nameof(points));

        var snapshot = new Point2[count];
        for (var i = 0; i < count; i++)
        {
            if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            snapshot[i] = points[i];
            Geometry.Validate(snapshot[i]);
        }

        var edges = count - 1;
        long comparisons = 0;
        for (var i = 0; i < edges; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var a = snapshot[i]; var b = snapshot[i + 1];
            if (Geometry.Distance(a, b) <= Geometry.Epsilon)
                throw new ArgumentException("An open path must not contain a zero-length edge, crossing or overlap.", nameof(points));

            for (var j = i + 1; j < edges; j++)
            {
                if ((comparisons++ & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                var c = snapshot[j]; var d = snapshot[j + 1];
                if (j == i + 1)
                {
                    if (Geometry.AdjacentEdgesOverlap(a, b, d))
                        throw new ArgumentException("An open path must not contain a zero-length edge, crossing or overlap.", nameof(points));
                }
                else if (!Geometry.CanSkipDisjointSegments(a, b, c, d) && Geometry.SegmentsIntersect(a, b, c, d))
                    throw new ArgumentException("An open path must not contain a zero-length edge, crossing or overlap.", nameof(points));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
}
