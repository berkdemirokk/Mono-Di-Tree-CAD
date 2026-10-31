using TekstilCAD.Core;

namespace TekstilCAD.Patterns;

/// <summary>
/// Bounded diagnostic sampling of two contours. This report does not certify matching,
/// topology, offset correctness, or suitability for production.
/// </summary>
public static class OffsetMismatchReport
{
    // Pairwise sampling is deliberately bounded independently of the general geometry limit.
    public const int MaximumVertices = 2048;

    public sealed record EdgeMismatch(
        int OutputEdgeIndex, double LengthMm,
        int NearestSourceEdgeIndex, int NearestSourceVertexIndex,
        double StartDistanceMm, double MidpointDistanceMm, double EndDistanceMm,
        Point2 PreviousOutputDirection, Point2 OutputDirection, Point2 NextOutputDirection,
        Point2 NearestSourceEdgeDirection);

    public sealed record Report(int SourceEdgeCount, int OutputEdgeCount, double ToleranceMm,
        double MaximumReportedEdgeLengthMm, IReadOnlyList<EdgeMismatch> Mismatches);

    /// <summary>
    /// Reports output edges no longer than the specified length when any of their
    /// endpoints or midpoint lies farther than tolerance from every source segment.
    /// Indices follow input cyclic order after removing an optional closing point.
    /// Nearest source indices are determined at the output edge midpoint. Ties use
    /// the first index. Sampling cannot prove that an entire edge is covered.
    /// </summary>
    public static Report Analyze(IReadOnlyList<Point2> source, IReadOnlyList<Point2> output,
        double toleranceMm, double maximumReportedEdgeLengthMm)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);
        if (!double.IsFinite(toleranceMm) || toleranceMm <= 0 || toleranceMm > 1)
            throw new ArgumentOutOfRangeException(nameof(toleranceMm));
        if (!double.IsFinite(maximumReportedEdgeLengthMm) || maximumReportedEdgeLengthMm <= 0 || maximumReportedEdgeLengthMm > Geometry.MaxCoordinate)
            throw new ArgumentOutOfRangeException(nameof(maximumReportedEdgeLengthMm));

        var a = Contour(source, nameof(source));
        var b = Contour(output, nameof(output));
        var mismatches = new List<EdgeMismatch>();
        for (var i = 0; i < b.Count; i++)
        {
            var start = b[i]; var end = b[(i + 1) % b.Count];
            var length = Geometry.Distance(start, end);
            if (length > maximumReportedEdgeLengthMm) continue;
            var midpoint = new Point2(start.X + (end.X - start.X) / 2, start.Y + (end.Y - start.Y) / 2);
            var startDistance = NearestEdge(a, start).Distance;
            var nearest = NearestEdge(a, midpoint);
            var endDistance = NearestEdge(a, end).Distance;
            if (startDistance <= toleranceMm && nearest.Distance <= toleranceMm && endDistance <= toleranceMm) continue;

            var nearestVertex = 0;
            var vertexDistance = double.PositiveInfinity;
            for (var j = 0; j < a.Count; j++)
            {
                var distance = Geometry.Distance(a[j], midpoint);
                if (distance < vertexDistance) { vertexDistance = distance; nearestVertex = j; }
            }
            mismatches.Add(new(i, length, nearest.Index, nearestVertex, startDistance, nearest.Distance, endDistance,
                Direction(b[(i + b.Count - 1) % b.Count], start), Direction(start, end),
                Direction(end, b[(i + 2) % b.Count]), Direction(a[nearest.Index], a[(nearest.Index + 1) % a.Count])));
        }
        return new(a.Count, b.Count, toleranceMm, maximumReportedEdgeLengthMm, mismatches.AsReadOnly());
    }

    private static List<Point2> Contour(IReadOnlyList<Point2> points, string parameter)
    {
        if (points.Count > MaximumVertices + 1) throw new ArgumentException("Diagnostic contour exceeds the vertex limit.", parameter);
        var copy = points.ToList();
        if (copy.Count > 1 && copy[0] == copy[^1]) copy.RemoveAt(copy.Count - 1);
        if (copy.Count > MaximumVertices) throw new ArgumentException("Diagnostic contour exceeds the vertex limit.", parameter);
        Geometry.ValidateContour(copy);
        for (var i = 0; i < copy.Count; i++)
        {
            var dx = copy[(i + 1) % copy.Count].X - copy[i].X;
            var dy = copy[(i + 1) % copy.Count].Y - copy[i].Y;
            var squared = dx * dx + dy * dy;
            if (!double.IsFinite(squared) || squared == 0)
                throw new ArgumentException("Diagnostic edge is too short for numerical analysis.", parameter);
        }
        return copy;
    }

    private static Point2 Direction(Point2 a, Point2 b)
    {
        var length = Geometry.Distance(a, b);
        return new((b.X - a.X) / length, (b.Y - a.Y) / length);
    }

    private static (int Index, double Distance) NearestEdge(IReadOnlyList<Point2> contour, Point2 point)
    {
        var bestIndex = 0; var bestSquared = double.PositiveInfinity;
        for (var i = 0; i < contour.Count; i++)
        {
            var a = contour[i]; var b = contour[(i + 1) % contour.Count];
            // Subtract before projection to limit cancellation at large coordinate origins.
            var dx = b.X - a.X; var dy = b.Y - a.Y;
            var px = point.X - a.X; var py = point.Y - a.Y;
            var t = Math.Clamp((px * dx + py * dy) / (dx * dx + dy * dy), 0, 1);
            var ex = px - t * dx; var ey = py - t * dy;
            var squared = ex * ex + ey * ey;
            if (squared < bestSquared) { bestSquared = squared; bestIndex = i; }
        }
        return (bestIndex, Math.Sqrt(bestSquared));
    }
}
