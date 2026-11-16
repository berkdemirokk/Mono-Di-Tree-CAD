using Clipper2Lib;
using TekstilCAD.Core;

namespace TekstilCAD.Nesting;

/// <summary>One placed copy. Points are the placed contour in millimetres.</summary>
public sealed record TrueShapePlacement(Guid PieceId, bool Rotated180, double OffsetX, double OffsetY, IReadOnlyList<Point2> Points);

/// <summary>Detached true-shape layout. It is a heuristic result, not an optimum and not a production approval.</summary>
public sealed record TrueShapeNestingResult(IReadOnlyList<TrueShapePlacement> Placements, double UsedLength, double UtilizationPercent);

/// <summary>The bounded search gave up; a larger step or a smaller job may succeed.</summary>
public sealed class TrueShapeSearchLimitException(string message) : ArgumentException(message);

/// <summary>
/// Experimental bottom-left placement that tests the real contours instead of
/// their bounding boxes, so concave pieces can interlock. X runs across the
/// fabric width and Y along its length, as in <see cref="NestingEngine"/>.
/// Contours are snapped to a 0.001 mm grid; candidate positions are searched
/// on a regular step, so tighter layouts between steps are not found. Only
/// 0° and, when allowed, 180° are tried. Grainlines, notch marks and grading
/// rules are not carried to the result. Not verified on factory patterns.
/// </summary>
public static class TrueShapeNesting
{
    public const int MaximumPieces = 500;
    public const int MaximumVerticesPerPiece = 2_000;
    public const double MaximumDimension = 1_000_000;
    public const long MaximumCollisionTests = 2_000_000;
    private const double Scale = 1000;

    private sealed record Shape(Path64 Contour, Path64 Inflated, long Width, long Height, long InflatedMinX, long InflatedMinY, long InflatedMaxX, long InflatedMaxY);
    private sealed record Placed(Path64 Contour, long MinX, long MinY, long MaxX, long MaxY);

    public static TrueShapeNestingResult Arrange(IReadOnlyList<PatternPiece> pieces, double fabricWidth, double gap = 5,
        double edgeMargin = 0, bool allowRotate180 = false, double step = 5, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(pieces);
        if (!double.IsFinite(fabricWidth) || fabricWidth <= 0 || fabricWidth > MaximumDimension) throw new ArgumentOutOfRangeException(nameof(fabricWidth));
        if (!double.IsFinite(gap) || gap < 0 || gap > MaximumDimension) throw new ArgumentOutOfRangeException(nameof(gap));
        if (!double.IsFinite(edgeMargin) || edgeMargin < 0 || edgeMargin * 2 >= fabricWidth) throw new ArgumentOutOfRangeException(nameof(edgeMargin));
        if (!double.IsFinite(step) || step < 0.1 || step > MaximumDimension) throw new ArgumentOutOfRangeException(nameof(step));
        if (pieces.Count == 0 || pieces.Count > MaximumPieces) throw new ArgumentException("Pastal parça sayısı 1 ile 500 arasında olmalıdır.", nameof(pieces));

        long gapGrid = (long)Math.Round(gap * Scale), stepGrid = (long)Math.Round(step * Scale);
        long minX = (long)Math.Ceiling(edgeMargin * Scale), maxX = (long)Math.Floor((fabricWidth - edgeMargin) * Scale);
        var ids = new HashSet<Guid>();
        var entries = new List<(Guid Id, Shape[] Orientations, double Area, int Index)>(pieces.Count);
        double totalArea = 0;
        for (var index = 0; index < pieces.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var piece = pieces[index] ?? throw new ArgumentException("Geçersiz parça.", nameof(pieces));
            if (piece.Id == Guid.Empty || !ids.Add(piece.Id)) throw new ArgumentException("Her pastal parçasının kimliği benzersiz olmalıdır.", nameof(pieces));
            if (piece.Points is null || !piece.Closed || piece.Points.Count < 3 || piece.Points.Count > MaximumVerticesPerPiece)
                throw NestingPieceError.Create(piece.Id, "Pastal için kapalı, 3–2000 noktalı konturlar gereklidir.", nameof(pieces));
            var points = piece.Points.ToArray();
            if (points.Any(p => !p.IsFinite || Math.Abs(p.X) > MaximumDimension || Math.Abs(p.Y) > MaximumDimension))
                throw NestingPieceError.Create(piece.Id, "Koordinatlar çalışma sınırı dışında.", nameof(pieces));
            if (Geometry.HasSelfIntersections(points, true, cancellationToken))
                throw NestingPieceError.Create(piece.Id, "Kontur kendisiyle kesişmemelidir.", nameof(pieces));
            var orientations = new List<Shape> { CreateShape(points, false, gapGrid) };
            if (allowRotate180) orientations.Add(CreateShape(points, true, gapGrid));
            var area = Math.Abs(Clipper.Area(orientations[0].Contour)) / (Scale * Scale);
            if (!(area > 0)) throw NestingPieceError.Create(piece.Id, "Kontur sıfırdan büyük alan taşımalıdır.", nameof(pieces));
            if (orientations[0].Width > maxX - minX) throw NestingPieceError.Create(piece.Id, "Parça kumaş enine sığmıyor.", nameof(fabricWidth));
            totalArea += area;
            entries.Add((piece.Id, orientations.ToArray(), area, index));
        }

        var placed = new List<Placed>(entries.Count);
        var placements = new TrueShapePlacement?[entries.Count];
        long usedLength = 0, tests = 0;
        foreach (var entry in entries.OrderByDescending(e => e.Area).ThenBy(e => e.Index))
        {
            (long X, long Y, int Orientation)? best = null;
            for (var orientation = 0; orientation < entry.Orientations.Length; orientation++)
            {
                var shape = entry.Orientations[orientation];
                var found = FindBottomLeft(shape, placed, minX, maxX, stepGrid, usedLength, gapGrid, ref tests, cancellationToken);
                if (best is null || found.Y + shape.Height < best.Value.Y + entry.Orientations[best.Value.Orientation].Height)
                    best = (found.X, found.Y, orientation);
            }
            var chosen = entry.Orientations[best!.Value.Orientation];
            var contour = Translate(chosen.Contour, best.Value.X, best.Value.Y);
            placed.Add(new(contour, best.Value.X, best.Value.Y, best.Value.X + chosen.Width, best.Value.Y + chosen.Height));
            usedLength = Math.Max(usedLength, best.Value.Y + chosen.Height);
            placements[entry.Index] = new(entry.Id, best.Value.Orientation == 1, best.Value.X / Scale, best.Value.Y / Scale,
                contour.Select(p => new Point2(p.X / Scale, p.Y / Scale)).ToArray());
        }

        // Independent final check on the placed contours themselves.
        for (var i = 0; i < placed.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (placed[i].MinX < minX || placed[i].MaxX > maxX || placed[i].MinY < 0)
                throw new InvalidOperationException("Yerleşim kumaş sınırını aştı; sonuç kullanılmadı.");
            for (var j = i + 1; j < placed.Count; j++)
                if (BoxesOverlap(placed[i], placed[j].MinX, placed[j].MinY, placed[j].MaxX, placed[j].MaxY) && Overlaps(placed[i].Contour, placed[j].Contour))
                    throw new InvalidOperationException("Yerleşimde çakışma bulundu; sonuç kullanılmadı.");
        }
        var length = usedLength / Scale;
        return new(placements.Select(p => p!).ToArray(), length, length > 0 ? totalArea / (fabricWidth * length) * 100 : 0);
    }

    private static (long X, long Y) FindBottomLeft(Shape shape, List<Placed> placed, long minX, long maxX, long step, long usedLength,
        long gap, ref long tests, CancellationToken cancellationToken)
    {
        // Above everything already placed (plus the gap) the left edge is always free.
        var fallbackY = placed.Count == 0 ? 0 : usedLength + gap;
        for (long y = 0; y < fallbackY; y += step)
        {
            for (var x = minX; x + shape.Width <= maxX; x += step)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsFree(shape, x, y, placed, ref tests)) return (x, y);
            }
        }
        return (minX, fallbackY);
    }

    private static bool IsFree(Shape shape, long x, long y, List<Placed> placed, ref long tests)
    {
        Path64? moved = null;
        foreach (var other in placed)
        {
            if (!BoxesOverlap(other, shape.InflatedMinX + x, shape.InflatedMinY + y, shape.InflatedMaxX + x, shape.InflatedMaxY + y)) continue;
            if (++tests > MaximumCollisionTests)
                throw new TrueShapeSearchLimitException("Pastal arama sınırı aşıldı; adımı büyütün veya daha küçük iş grupları kullanın.");
            moved ??= Translate(shape.Inflated, x, y);
            if (Overlaps(moved, other.Contour)) return false;
        }
        return true;
    }

    private static bool BoxesOverlap(Placed a, long minX, long minY, long maxX, long maxY) =>
        a.MinX < maxX && minX < a.MaxX && a.MinY < maxY && minY < a.MaxY;

    private static bool Overlaps(Path64 a, Path64 b)
    {
        var common = Clipper.Intersect(new Paths64 { a }, new Paths64 { b }, FillRule.NonZero);
        foreach (var path in common) if (Math.Abs(Clipper.Area(path)) > 0) return true;
        return false;
    }

    private static Path64 Translate(Path64 path, long dx, long dy)
    {
        var moved = new Path64(path.Count);
        foreach (var p in path) moved.Add(new Point64(p.X + dx, p.Y + dy));
        return moved;
    }

    private static Shape CreateShape(Point2[] points, bool rotate, long gap)
    {
        var grid = new (long X, long Y)[points.Length];
        long minX = long.MaxValue, minY = long.MaxValue, maxX = long.MinValue, maxY = long.MinValue;
        for (var i = 0; i < points.Length; i++)
        {
            long x = (long)Math.Round(points[i].X * Scale), y = (long)Math.Round(points[i].Y * Scale);
            if (rotate) { x = -x; y = -y; }
            grid[i] = (x, y);
            minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
        }
        var contour = new Path64(points.Length);
        foreach (var (x, y) in grid) contour.Add(new Point64(x - minX, y - minY));
        var inflated = contour;
        long inflatedMinX = 0, inflatedMinY = 0, inflatedMaxX = maxX - minX, inflatedMaxY = maxY - minY;
        if (gap > 0)
        {
            var result = Clipper.InflatePaths(new Paths64 { contour }, gap, JoinType.Miter, EndType.Polygon, 2.0);
            if (result.Count != 1) throw new ArgumentException("Parça aralığı tek bir dış kontur oluşturmadı; pastal hesaplanmadı.");
            inflated = result[0];
            inflatedMinX = inflated.Min(p => p.X); inflatedMinY = inflated.Min(p => p.Y);
            inflatedMaxX = inflated.Max(p => p.X); inflatedMaxY = inflated.Max(p => p.Y);
        }
        return new(contour, inflated, maxX - minX, maxY - minY, inflatedMinX, inflatedMinY, inflatedMaxX, inflatedMaxY);
    }
}
