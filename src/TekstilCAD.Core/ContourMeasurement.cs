namespace TekstilCAD.Core;

/// <summary>Direction in stored vertex order for a closed contour.</summary>
public enum ContourDirection { Forward, Backward }

/// <summary>A path between stored vertices. Direction is required only for closed contours.</summary>
public sealed record ContourPath(IReadOnlyList<Point2> Points, bool Closed, int StartIndex, int EndIndex, ContourDirection? Direction = null, bool FullLoop = false);

/// <summary>Lengths in millimetres; SignedDifference is second minus first.</summary>
public sealed record SeamEdgeComparison(double FirstLength, double SecondLength, double SignedDifference, double AbsoluteDifference);

/// <summary>Deviation from the requested second-minus-first ease, in millimetres.</summary>
public sealed record SeamEaseAssessment(double ExpectedDifference, double Tolerance, double Deviation, bool WithinTolerance);

/// <summary>Measurements of the stored straight segments of a contour, in millimetres.</summary>
public static class ContourMeasurement
{
    /// <summary>Maximum stored vertices accepted for a single interactive comparison path.</summary>
    public const int MaxComparisonVertices = 10_000;

    /// <summary>
    /// Measures stored straight segments, not a parametrically exact curve.
    /// For closed contours an explicit repeated final vertex aliases vertex zero;
    /// identical endpoints mean zero length, not a full lap. Open paths use the
    /// unique path between indices and do not accept a direction.
    /// The caller must not mutate points while this method reads them.
    /// </summary>
    public static double MeasurePath(ContourPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(path.Points);
        var points = path.Points;
        if (points.Count == 0 || points.Count > MaxComparisonVertices)
            throw new ArgumentOutOfRangeException(nameof(path), "Kontur nokta sayısı ölçüm sınırının dışında.");
        if (path.StartIndex < 0 || path.StartIndex >= points.Count || path.EndIndex < 0 || path.EndIndex >= points.Count)
            throw new ArgumentOutOfRangeException(nameof(path), "Kontur noktası geçerli değil.");
        if (path.Closed ? path.Direction is not (ContourDirection.Forward or ContourDirection.Backward) : path.Direction is not null)
            throw new ArgumentException("Kapalı kontur için yön seçin; açık konturda yön kullanmayın.", nameof(path));
        foreach (var point in points) Geometry.Validate(point);
        int count = points.Count;
        if (path.Closed && count > 1 && points[0] == points[count - 1]) count--;
        int start = path.StartIndex == count ? 0 : path.StartIndex;
        int end = path.EndIndex == count ? 0 : path.EndIndex;
        if (path.FullLoop && (!path.Closed || start != end))
            throw new ArgumentException("Tam tur için kapalı konturda aynı başlangıç ve bitiş noktasını seçin.", nameof(path));
        if (start == end && !path.FullLoop) return 0;
        int step = path.Closed ? path.Direction == ContourDirection.Forward ? 1 : -1 : end > start ? 1 : -1;
        double sum = 0, correction = 0;
        int current = start;
        do
        {
            int next = current + step;
            if (path.Closed) next = (next + count) % count;
            double length = Geometry.Distance(points[current], points[next]);
            double adjusted = length - correction;
            double updated = sum + adjusted;
            correction = (updated - sum) - adjusted;
            sum = updated;
            if (!double.IsFinite(sum)) throw new ArgumentOutOfRangeException(nameof(path), "Kontur uzunluğu hesaplanamadı.");
            current = next;
        } while (current != end);
        return sum;
    }

    public static SeamEdgeComparison ComparePaths(ContourPath first, ContourPath second)
    {
        double a = MeasurePath(first), b = MeasurePath(second);
        double difference = b - a;
        if (!double.IsFinite(difference)) throw new ArgumentOutOfRangeException(nameof(second));
        return new(a, b, difference, Math.Abs(difference));
    }

    /// <summary>Compares a signed target difference with the measured difference and a nonnegative tolerance.</summary>
    public static SeamEaseAssessment AssessEase(SeamEdgeComparison comparison, double expectedDifference, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        if (!double.IsFinite(comparison.SignedDifference)) throw new ArgumentOutOfRangeException(nameof(comparison));
        if (!double.IsFinite(expectedDifference)) throw new ArgumentOutOfRangeException(nameof(expectedDifference));
        if (!double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        var deviation = comparison.SignedDifference - expectedDifference;
        if (!double.IsFinite(deviation)) throw new ArgumentOutOfRangeException(nameof(expectedDifference));
        return new(expectedDifference, tolerance, deviation, Math.Abs(deviation) <= tolerance);
    }

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
