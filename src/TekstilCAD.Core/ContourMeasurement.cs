namespace TekstilCAD.Core;

/// <summary>Measurements of the stored straight segments of a contour, in millimetres.</summary>
public static class ContourMeasurement
{
    /// <summary>
    /// Returns the total length of a closed contour, including the segment from
    /// the last stored vertex to the first. An explicitly repeated closing
    /// vertex is permitted and contributes a zero-length closing segment.
    /// </summary>
    public static double ClosedPerimeter(IReadOnlyList<Point2> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0) return 0;
        double sum = 0, correction = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var length = Geometry.Distance(points[i], points[(i + 1) % points.Count]);
            var adjusted = length - correction;
            var updated = sum + adjusted;
            correction = (updated - sum) - adjusted;
            sum = updated;
        }
        return sum;
    }
}
