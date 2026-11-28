namespace TekstilCAD.Core;

/// <summary>Construction and geometry for standard sewn darts, whose legs are internal sewing marks.</summary>
public static class DartTools
{
    /// <summary>
    /// Adds an internal sewn dart to a closed, straight-edge piece. The fabric contour is preserved.
    /// Fractions locate the two leg ends along the selected edge; depth is measured perpendicular
    /// into the piece. For example, <c>AddDart(piece, 0, 0.4, 0.6, 20)</c> adds a centered dart on
    /// edge zero with a 20 mm intake and 20 mm depth.
    /// </summary>
    public static PatternPiece AddDart(PatternPiece piece, int edgeIndex,
        double startFraction, double endFraction, double depthMm,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(piece);
        cancellationToken.ThrowIfCancellationRequested();
        if (!piece.Closed) throw new ArgumentException("Pens için kapalı kontur gereklidir.", nameof(piece));
        if (piece.Curves is not null) throw new ArgumentException("Önce parametrik konturu açıkça poliline dönüştürün.", nameof(piece));
        if (piece.Points is null || piece.Points.Count < 3 || piece.Points.Count > Geometry.MaxValidatedContourVertices)
            throw new ArgumentException("Kontur pens işlemi için geçerli nokta sınırında değil.", nameof(piece));
        if (piece.Name is null || piece.Name.Length > 1024) throw new ArgumentException("Kaynak parça adı geçersiz veya çok uzun.", nameof(piece));
        var points = piece.Points.ToList();
        if (points.Count > 3 && points[0] == points[^1]) points.RemoveAt(points.Count - 1);
        if (points.Count < 3 || points.Count >= Geometry.MaxValidatedContourVertices)
            throw new ArgumentException("Kontur pens işlemi için geçerli nokta sınırında değil.", nameof(piece));
        if (edgeIndex < 0 || edgeIndex >= points.Count) throw new ArgumentOutOfRangeException(nameof(edgeIndex));
        if (!double.IsFinite(startFraction) || startFraction <= 0 || startFraction >= 1) throw new ArgumentOutOfRangeException(nameof(startFraction));
        if (!double.IsFinite(endFraction) || endFraction <= 0 || endFraction >= 1 || endFraction <= startFraction)
            throw new ArgumentOutOfRangeException(nameof(endFraction));
        if (!double.IsFinite(depthMm) || depthMm <= 0) throw new ArgumentOutOfRangeException(nameof(depthMm));
        if (piece.Darts is { Count: >= 64 }) throw new ArgumentException("Parçada en fazla 64 pens işareti bulunabilir.", nameof(piece));
        if (piece.Darts?.Any(dart => dart.EdgeIndex == edgeIndex) == true)
            throw new ArgumentException("Bu kenarda zaten bir pens var.", nameof(edgeIndex));

        Geometry.ValidateContour(points, cancellationToken);
        var area = Geometry.SignedArea(points, cancellationToken);
        if (area == 0) throw new ArgumentException("Konturun alanı sıfırdır.", nameof(piece));
        var a = points[edgeIndex]; var b = points[(edgeIndex + 1) % points.Count];
        var edge = b - a; var length = Geometry.Distance(a, b);
        if (length <= Geometry.Epsilon) throw new ArgumentException("Seçilen kenar çok kısa.", nameof(edgeIndex));
        var mouthStart = a + edge * startFraction; var mouthEnd = a + edge * endFraction;
        var inward = new Point2(-edge.Y / length, edge.X / length) * Math.Sign(area);
        var apex = a + edge * ((startFraction + endFraction) / 2) + inward * depthMm;
        Geometry.Validate(mouthStart); Geometry.Validate(mouthEnd); Geometry.Validate(apex);
        if (!Geometry.PointInPolygon(apex, points)) throw new ArgumentException("Pens ucu konturun içinde değil.", nameof(depthMm));
        for (var i = 1; i < 16; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var t = i / 16d;
            if (!Geometry.PointInPolygon(mouthStart + (apex - mouthStart) * t, points) ||
                !Geometry.PointInPolygon(apex + (mouthEnd - apex) * t, points))
                throw new ArgumentException("Pens kolu kontur dışına çıkıyor; işlem uygulanmadı.", nameof(depthMm));
        }

        var result = piece.Clone();
        result.Points = [.. piece.Points];
        result.Darts ??= [];
        result.Darts.Add(new DartMark { EdgeIndex = edgeIndex, StartFraction = startFraction, EndFraction = endFraction, DepthMm = depthMm });
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    /// <summary>Returns the two internal sewing legs of a dart from its current piece contour.</summary>
    public static (Point2 Start, Point2 Apex, Point2 End) GetLegs(PatternPiece piece, DartMark dart)
    {
        ArgumentNullException.ThrowIfNull(piece); ArgumentNullException.ThrowIfNull(dart);
        if (!piece.Closed || piece.Curves is not null || piece.Points is null)
            throw new ArgumentException("Pens tanımı geçerli bir düz kapalı konturla eşleşmiyor.", nameof(dart));
        var points=piece.Points.Count>3 && piece.Points[0]==piece.Points[^1] ? piece.Points.Take(piece.Points.Count-1).ToList() : piece.Points;
        if(dart.EdgeIndex<0 || dart.EdgeIndex>=points.Count)throw new ArgumentException("Pens tanımı geçerli bir kontur kenarıyla eşleşmiyor.",nameof(dart));
        var a = points[dart.EdgeIndex]; var b = points[(dart.EdgeIndex + 1) % points.Count];
        var edge = b - a; var length = Geometry.Distance(a, b); var area = Geometry.SignedArea(points);
        if (length <= Geometry.Epsilon || area == 0 || !double.IsFinite(dart.StartFraction) || !double.IsFinite(dart.EndFraction) ||
            dart.StartFraction <= 0 || dart.EndFraction >= 1 || dart.EndFraction <= dart.StartFraction ||
            !double.IsFinite(dart.DepthMm) || dart.DepthMm <= 0)
            throw new ArgumentException("Pens ölçüleri geçersiz.", nameof(dart));
        var start = a + edge * dart.StartFraction; var end = a + edge * dart.EndFraction;
        var inward = new Point2(-edge.Y / length, edge.X / length) * Math.Sign(area);
        var apex = a + edge * ((dart.StartFraction + dart.EndFraction) / 2) + inward * dart.DepthMm;
        Geometry.Validate(start); Geometry.Validate(end); Geometry.Validate(apex);
        return (start, apex, end);
    }
}
