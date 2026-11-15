namespace TekstilCAD.Core;

/// <summary>
/// Bounded, deterministic polyline projection of an editable analytic open path.
/// The source segments are authoritative; Points are only a cached display
/// approximation. Editing Points without updating/detaching Curves is invalid.
/// </summary>
public static class ParametricContour
{
    public const double PreviewToleranceMm = 0.1;
    public const int MaximumSegments = 2_000;

    public static List<Point2> Sample(IReadOnlyList<CurveSegment> curves)
    {
        ArgumentNullException.ThrowIfNull(curves);
        if (curves.Count is < 1 or > MaximumSegments)
            throw new InvalidDataException("Parametrik segment sayısı sınır dışında.");

        for (var i = 0; i < curves.Count; i++)
        {
            if (curves[i] is null)
                throw new InvalidDataException("Boş parametrik segment kabul edilemez.");
            if (i > 0)
            {
                // This is a *single* continuous contour, never a set of
                // disconnected spans silently joined by a straight line.
                var end = curves[i - 1].PointAt(1);
                var start = curves[i].PointAt(0);
                if (!SameBits(end.X, start.X) || !SameBits(end.Y, start.Y))
                    throw new InvalidDataException("Komşu parametrik segment uçları kesintisiz bağlanmalı.");
            }
        }
        var sampled = CurveSampler.Sample(curves, PreviewToleranceMm);
        if (sampled.Count < 2)
            throw new InvalidDataException("Parametrik açık kontur en az iki nokta üretmeli.");
        return sampled;
    }

    /// <summary>Generate preview points directly from control points.</summary>
    public static void Regenerate(PatternPiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if (piece.Closed || piece.Curves is null)
            throw new InvalidDataException("Bu işlem yalnız açık parametrik konturlar için geçerli.");
        piece.Points = Sample(piece.Curves);
    }

    /// <summary>Explicitly convert to a fixed polyline before ordinary vertex editing.</summary>
    public static void Detach(PatternPiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        piece.Curves = null;
    }

    public static bool MatchesPreview(PatternPiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if (piece.Curves is null || piece.Closed || piece.Points is null) return false;
        var expected = Sample(piece.Curves);
        if (expected.Count != piece.Points.Count) return false;
        for (var i = 0; i < expected.Count; i++)
            if (!SameBits(expected[i].X, piece.Points[i].X) ||
                !SameBits(expected[i].Y, piece.Points[i].Y)) return false;
        return true;
    }

    private static bool SameBits(double a, double b) =>
        BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
}
