namespace TekstilCAD.Core;

public static partial class PieceOperations
{
    /// <summary>
    /// Adds a closed rectangle (four points, clockwise on screen: top-left, top-right, bottom-right, bottom-left)
    /// named "Parça N". <paramref name="origin"/> is the top-left corner. Without it the piece is placed to the right of the
    /// rightmost existing piece with a <see cref="DefaultGapMm"/> gap and the same top, so it never overlaps another piece's
    /// bounding box; the first piece goes to (0, 0). The piece goes to <paramref name="layerId"/> (default layer when null),
    /// which must be visible and unlocked.
    /// </summary>
    public static PieceOperationResult AddRectangle(ProjectDocument document, double width, double height, Point2? origin = null, Guid? layerId = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (CheckSide(width, "Genişlik") is { } badWidth) return badWidth;
        if (CheckSide(height, "Yükseklik") is { } badHeight) return badHeight;
        return AddShape(document, width, height, origin, layerId, closed: true);
    }

    /// <summary>
    /// Adds an open, horizontal two-point line from the origin to the right, named "Parça N". Placement and layer rules
    /// are those of <see cref="AddRectangle"/>; <paramref name="origin"/> is the left end.
    /// </summary>
    public static PieceOperationResult AddLine(ProjectDocument document, double length, Point2? origin = null, Guid? layerId = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (CheckSide(length, "Uzunluk") is { } bad) return bad;
        return AddShape(document, length, 0, origin, layerId, closed: false);
    }

    private static PieceOperationResult? CheckSide(double value, string label)
    {
        if (!double.IsFinite(value)) return InvalidNumber(label);
        if (value <= MinimumSideMm)
            return Fail(PieceOperationError.InvalidValue,
                $"{label} {Mm(MinimumSideMm)} değerinden büyük olmalı; girilen: {Mm(value)}. Daha büyük bir ölçü girin.");
        if (value > 2 * Geometry.MaxCoordinate)
            return Fail(PieceOperationError.OutOfRange, $"{label} çalışma alanına sığmıyor: {Mm(value)}. {OutOfRangeText}");
        return null;
    }

    private static PieceOperationResult AddShape(ProjectDocument document, double width, double height, Point2? origin, Guid? layerId, bool closed)
    {
        if (document.Pieces.Count >= MaximumPieces)
            return Fail(PieceOperationError.LimitExceeded,
                $"Projede en çok {MaximumPieces.ToString("N0", Tr)} parça olabilir; yeni parça eklenmedi. Önce bazı parçaları silin.");
        var newPoints = closed ? 4 : 2;
        if (TotalPoints(document) + newPoints > MaximumTotalPoints)
            return Fail(PieceOperationError.LimitExceeded,
                $"Projede en çok {MaximumTotalPoints.ToString("N0", Tr)} nokta olabilir; yeni parça eklenmedi. Önce bazı parçaları silin.");

        var target = layerId ?? Layer.DefaultId;
        var layer = LayerTools.Find(document, target);
        if (layer is null)
            return Fail(PieceOperationError.LayerUnavailable, "Hedef katman bulunamadı; parça eklenmedi. Var olan bir katman seçin.");
        if (layer.Locked)
            return Fail(PieceOperationError.LayerLocked, $"“{layer.Name}” katmanı kilitli; parça eklenmedi. Önce kilidi açın ya da başka bir katman seçin.");
        if (!layer.Visible)
            return Fail(PieceOperationError.LayerUnavailable, $"“{layer.Name}” katmanı gizli; parça eklenmedi. Önce katmanı görünür yapın ya da başka bir katman seçin.");

        Point2 at;
        if (origin is { } explicitOrigin)
        {
            if (!explicitOrigin.IsFinite) return InvalidNumber("Konum (X ve Y)");
            if (!InRange(explicitOrigin)) return Fail(PieceOperationError.OutOfRange, OutOfRangeText);
            at = explicitOrigin;
        }
        else
        {
            if (!TryDefaultOrigin(document, out at))
                return Fail(PieceOperationError.InvalidShape, "Mevcut parçaların konumu okunamadı; yeni parça yerleştirilemedi. Konumu kendiniz belirtin.");
            if (document.Pieces.Count > 0 && (!InRange(at) || at.X + width > Geometry.MaxCoordinate))
                return Fail(PieceOperationError.OutOfRange,
                    "Mevcut parçaların sağında yeni parça için yer kalmadı (çalışma alanı sınırı ±100.000.000 mm). Konumu kendiniz belirtin.");
        }

        var right = at.X + width;
        var bottom = at.Y + height;
        List<Point2> points = closed
            ? [at, new(right, at.Y), new(right, bottom), new(at.X, bottom)]
            : [at, new(right, at.Y)];
        if (points.Any(p => !InRange(p))) return Fail(PieceOperationError.OutOfRange, OutOfRangeText);

        var name = NextPieceName(document);
        // The represented sides must still be the requested ones; at huge coordinates a tiny side can round away.
        if (!SpanMatches(width, right - at.X) || (closed && !SpanMatches(height, bottom - at.Y)))
            return Fail(PieceOperationError.Precision,
                $"Bu konumda {Mm(width)}{(closed ? " × " + Mm(height) : "")} ölçüsü sayısal hassasiyetle temsil edilemiyor; parça eklenmedi. Orijine daha yakın bir konum girin.");
        try
        {
            if (closed) Geometry.ValidateContour(points); else LargeOpenPathValidator.Validate(points);
        }
        catch (ArgumentException ex)
        {
            return Fail(PieceOperationError.InvalidShape,
                $"Bu ölçüyle geçerli bir {(closed ? "dikdörtgen" : "çizgi")} oluşturulamadı; parça eklenmedi. Daha büyük bir ölçü girin.", ex.Message);
        }

        var piece = new PatternPiece { Id = NewId(document), Name = name, LayerId = target, Points = points, Closed = closed };
        var edited = document.Clone();
        edited.Pieces.Add(piece);
        var summary = closed
            ? $"{Quote(name)} dikdörtgeni eklendi: {Mm(width)} × {Mm(height)}, sol üst köşe X {Mm(at.X)}, Y {Mm(at.Y)}."
            : $"{Quote(name)} çizgisi eklendi: {Mm(width)}, sol uç X {Mm(at.X)}, Y {Mm(at.Y)}.";
        return PieceOperationResult.Success(document, edited, piece.Id, summary);
    }

    private static Guid NewId(ProjectDocument document)
    {
        while (true)
        {
            var id = Guid.NewGuid();
            if (id != Guid.Empty && document.Pieces.All(p => p.Id != id)) return id;
        }
    }

    /// <summary>Right of the rightmost piece (ties: the highest one) plus the gap, same top; (0, 0) for an empty document.</summary>
    private static bool TryDefaultOrigin(ProjectDocument document, out Point2 origin)
    {
        origin = new Point2(0, 0);
        var found = false;
        double bestMaxX = 0, bestMinY = 0;
        foreach (var piece in document.Pieces)
        {
            if (!TryBounds(piece, out var b)) return false;
            if (!found || b.MaxX > bestMaxX || (b.MaxX == bestMaxX && b.MinY < bestMinY))
            { found = true; bestMaxX = b.MaxX; bestMinY = b.MinY; }
        }
        if (found) origin = new Point2(bestMaxX + DefaultGapMm, bestMinY);
        return true;
    }

    /// <summary>
    /// Copies a piece with a new id, moved by the given offset (default 20 mm right and 20 mm down) and named "&lt;ad&gt; kopya".
    /// The copy stays on the source's layer and keeps its points, closure, production info, grainline and notch marks.
    /// A grading rule is not copied (a warning says so). Pieces with analytic curves are copied too; the curves move with the copy.
    /// </summary>
    public static PieceOperationResult Duplicate(ProjectDocument document, Guid pieceId,
        double offsetX = DefaultDuplicateOffsetMm, double offsetY = DefaultDuplicateOffsetMm)
    {
        var refusal = Locate(document, pieceId, requireUnlocked: true, out _, out var piece);
        if (refusal is not null) return refusal;
        if (!double.IsFinite(offsetX) || !double.IsFinite(offsetY)) return InvalidNumber("Kaydırma miktarı");
        if (document.Pieces.Count >= MaximumPieces)
            return Fail(PieceOperationError.LimitExceeded,
                $"Projede en çok {MaximumPieces.ToString("N0", Tr)} parça olabilir; parça çoğaltılmadı. Önce bazı parçaları silin.");
        if (TotalPoints(document) + piece.Points.Count > MaximumTotalPoints)
            return Fail(PieceOperationError.LimitExceeded,
                $"Projede en çok {MaximumTotalPoints.ToString("N0", Tr)} nokta olabilir; parça çoğaltılmadı. Önce bazı parçaları silin.");
        return Guard(piece, () =>
        {
            var copy = Translated(piece, offsetX, offsetY);
            if (copy is null) return CurvesWithNotches(piece);
            copy.Id = NewId(document);
            copy.Name = CopyName(piece.Name);
            var warnings = new List<string>();
            if (document.GradingRules.Any(r => r.SourcePieceId == piece.Id)) warnings.Add("Serileme kuralı kopyalanmadı; kopya için gerekirse yeniden tanımlayın.");
            if (piece.Metadata is not null) warnings.Add("Üretim bilgisi kopyalandı; adedi kontrol edin.");
            var edited = document.Clone();
            edited.Pieces.Add(copy);
            return PieceOperationResult.Success(document, edited, copy.Id,
                $"{Quote(piece.Name)} çoğaltıldı: {Quote(copy.Name)}, {OffsetText(offsetX, offsetY)}.", warnings);
        });
    }

    private static string OffsetText(double dx, double dy)
    {
        if (dx == 0 && dy == 0) return "aynı konumda";
        var parts = new List<string>(2);
        if (dx != 0) parts.Add($"{Mm(Math.Abs(dx))} {(dx > 0 ? "sağa" : "sola")}");
        if (dy != 0) parts.Add($"{Mm(Math.Abs(dy))} {(dy > 0 ? "aşağı" : "yukarı")}");
        return string.Join(", ", parts) + " kaydırıldı";
    }

    private static string CopyName(string name)
    {
        const string suffix = " kopya";
        var length = Math.Min(name.Length, MaximumStoredNameLength - suffix.Length);
        if (length < name.Length && length > 0 && char.IsHighSurrogate(name[length - 1])) length--;
        return name[..length] + suffix;
    }

    /// <summary>
    /// Removes the piece and every grading rule that belongs to it, in one edit. A warning reports the removed rules.
    /// Nothing is asked here; the caller decides whether to confirm first. Undo restores both.
    /// </summary>
    public static PieceOperationResult Delete(ProjectDocument document, Guid pieceId)
    {
        var refusal = Locate(document, pieceId, requireUnlocked: true, out var index, out var piece, requirePoints: false);
        if (refusal is not null) return refusal;
        var edited = document.Clone();
        edited.Pieces.RemoveAt(index);
        var removedRules = edited.GradingRules.RemoveAll(r => r.SourcePieceId == pieceId);
        var warnings = new List<string>();
        if (removedRules > 0) warnings.Add($"Parçaya bağlı {removedRules} serileme kuralı da silindi.");
        return PieceOperationResult.Success(document, edited, null, $"{Quote(piece.Name)} silindi.", warnings);
    }

    /// <summary>
    /// Renames a piece. The name is trimmed and must be 1–<see cref="MaximumNameLength"/> UTF-16 units, free of control characters
    /// and unpaired surrogates. Names need not be unique; the id is the identity.
    /// </summary>
    public static PieceOperationResult Rename(ProjectDocument document, Guid pieceId, string? name)
    {
        var refusal = Locate(document, pieceId, requireUnlocked: true, out var index, out var piece, requirePoints: false);
        if (refusal is not null) return refusal;
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
            return Fail(PieceOperationError.InvalidValue, "Ad boş olamaz. 1–128 karakterlik bir ad yazın.");
        if (trimmed.Length > MaximumNameLength)
            return Fail(PieceOperationError.InvalidValue,
                $"Ad en çok {MaximumNameLength} karakter olabilir (girilen: {trimmed.Length}). Daha kısa bir ad yazın.");
        if (trimmed.Any(char.IsControl))
            return Fail(PieceOperationError.InvalidValue, "Ad satır sonu veya denetim karakteri içeremez. Düz bir ad yazın.");
        if (!IsWellFormed(trimmed))
            return Fail(PieceOperationError.InvalidValue, "Ad geçersiz bir Unicode karakteri içeriyor. Başka bir ad yazın.");
        if (string.Equals(trimmed, piece.Name, StringComparison.Ordinal))
            return Replace(document, index, piece.Clone(), null, $"{Quote(piece.Name)} zaten bu adı taşıyor.", []);
        var renamed = piece.Clone();
        renamed.Name = trimmed;
        return Replace(document, index, renamed, null, $"{Quote(piece.Name)} parçasının adı {Quote(trimmed)} yapıldı.", []);
    }

    private static bool IsWellFormed(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i])) { if (++i >= value.Length || !char.IsLowSurrogate(value[i])) return false; }
            else if (char.IsLowSurrogate(value[i])) return false;
        }
        return true;
    }
}
