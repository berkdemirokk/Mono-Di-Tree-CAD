namespace TekstilCAD.Core;

public static partial class PieceOperations
{
    /// <summary>
    /// Moves a piece by (dx, dy) millimetres; +X is right, +Y is down. Point order, closure, notch marks, grainline and a
    /// matching grading rule move with it. Zero movement succeeds without a document change. Pieces with analytic curves
    /// move too (control points shift and the preview points are regenerated); pieces with both curves and notch marks are refused.
    /// </summary>
    public static PieceOperationResult MoveBy(ProjectDocument document, Guid pieceId, double dx, double dy)
    {
        var refusal = Locate(document, pieceId, requireUnlocked: true, out var index, out var piece);
        if (refusal is not null) return refusal;
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) return InvalidNumber("Taşıma miktarı");
        return Guard(piece, () => MoveCore(document, index, piece, dx, dy, MoveSummary(piece, dx, dy)));
    }

    /// <summary>
    /// Moves a piece so that the top-left corner of its bounding box is at (x, y), as the inspector's X and Y fields do.
    /// The reached position is checked against the request (eight ULP); otherwise the move is refused.
    /// </summary>
    public static PieceOperationResult MoveTo(ProjectDocument document, Guid pieceId, double x, double y)
    {
        var refusal = Locate(document, pieceId, requireUnlocked: true, out var index, out var piece);
        if (refusal is not null) return refusal;
        if (!double.IsFinite(x) || !double.IsFinite(y)) return InvalidNumber("Konum (X ve Y)");
        if (Math.Abs(x) > Geometry.MaxCoordinate || Math.Abs(y) > Geometry.MaxCoordinate)
            return Fail(PieceOperationError.OutOfRange, OutOfRangeText);
        return Guard(piece, () =>
        {
            var bounds = Geometry.Bounds(piece.Points);
            var dx = x - bounds.MinX;
            var dy = y - bounds.MinY;
            var summary = dx == 0 && dy == 0
                ? $"{Quote(piece.Name)} zaten orada; taşıma gerekmedi."
                : $"{Quote(piece.Name)} sol üst köşesi X {Mm(x)}, Y {Mm(y)} konumuna taşındı.";
            var result = MoveCore(document, index, piece, dx, dy, summary);
            if (!result.Succeeded || (dx == 0 && dy == 0)) return result;
            var actual = Geometry.Bounds(result.Piece!.Points);
            return TargetMatches(actual.MinX, x, bounds.MinX) && TargetMatches(actual.MinY, y, bounds.MinY)
                ? result
                : Fail(PieceOperationError.Precision,
                    $"{Quote(piece.Name)} için istenen konum sayısal hassasiyetle yeterince temsil edilemiyor; hiçbir şey değişmedi. Daha küçük koordinatlar girin.");
        });
    }

    private static string MoveSummary(PatternPiece piece, double dx, double dy)
    {
        if (dx == 0 && dy == 0) return $"{Quote(piece.Name)} zaten orada; taşıma gerekmedi.";
        var parts = new List<string>(2);
        if (dx != 0) parts.Add($"{Mm(Math.Abs(dx))} {(dx > 0 ? "sağa" : "sola")}");
        if (dy != 0) parts.Add($"{Mm(Math.Abs(dy))} {(dy > 0 ? "aşağı" : "yukarı")}");
        return $"{Quote(piece.Name)} {string.Join(", ", parts)} taşındı.";
    }

    private static PieceOperationResult MoveCore(ProjectDocument document, int index, PatternPiece piece, double dx, double dy, string summary)
    {
        var moved = Translated(piece, dx, dy);
        if (moved is null) return CurvesWithNotches(piece);
        var warnings = new List<string>();
        GradingRuleSet? newRule = null;
        var rule = document.GradingRules.FirstOrDefault(r => r.SourcePieceId == piece.Id);
        if (rule is not null && (dx != 0 || dy != 0))
        {
            newRule = piece.Curves is null ? rule.TranslatedFor(piece, moved.Points, dx, dy) : null;
            if (newRule is null) warnings.Add(StaleRuleWarning);
        }
        return Replace(document, index, moved, newRule, summary, warnings);
    }

    private static PieceOperationResult CurvesWithNotches(PatternPiece piece) => Fail(PieceOperationError.Unsupported,
        $"{Quote(piece.Name)} hem yay/eğri hem çentik işareti içeriyor; işaretlerin yerinde kalacağı garanti edilemediği için taşıma veya çoğaltma uygulanmadı.");

    /// <summary>A detached translated copy (same id), or null for a piece with both curves and notch marks.</summary>
    private static PatternPiece? Translated(PatternPiece piece, double dx, double dy)
    {
        var copy = piece.Clone();
        if (dx == 0 && dy == 0) return copy;
        if (piece.Curves is not null)
        {
            if (piece.Notches is not null) return null;
            var shifted = piece.Curves.Select(segment => Shift(segment, dx, dy)).ToList();
            copy.Curves = shifted;
            copy.Points = ParametricContour.Sample(shifted);
            foreach (var point in copy.Points) Geometry.Validate(point);
            if (copy.Points.Count == piece.Points.Count) TranslationPrecision.Validate(piece.Points, copy.Points);
            else throw new InvalidDataException("Eğri örneklemesi taşıma sonrası değişti.");
            copy.Grainline = piece.Grainline is null ? null : GrainlineMath.Translate(piece.Grainline, null, null, dx, dy);
            return copy;
        }
        var (points, notches) = MarkedPieceTranslation.Translate(piece, dx, dy);
        copy.Points = points;
        copy.Notches = notches;
        copy.Grainline = piece.Grainline is null ? null : GrainlineMath.Translate(piece.Grainline, piece.Points[0], points[0], dx, dy);
        return copy;
    }

    private static CurveSegment Shift(CurveSegment segment, double dx, double dy)
    {
        Point2 S(Point2 p)
        {
            var q = new Point2(dx == 0 ? p.X : p.X + dx, dy == 0 ? p.Y : p.Y + dy);
            Geometry.Validate(q);
            return q;
        }
        return segment switch
        {
            CurveSegment.Line l => new CurveSegment.Line(S(l.Start), S(l.End)),
            CurveSegment.CubicBezier b => new CurveSegment.CubicBezier(S(b.P0), S(b.P1), S(b.P2), S(b.P3)),
            CurveSegment.Arc a => a with { Center = S(a.Center) },
            _ => throw new InvalidDataException("Bilinmeyen eğri türü.")
        };
    }

    /// <summary>
    /// The strict form of <see cref="Resize"/> for the command box: only a closed, four-point, axis-parallel rectangle without
    /// grainline, notch marks, grading rule or curves is resized (refusals R10 and R11 of the command design); everything else
    /// is refused with those texts and nothing changes. The rest of the behaviour is that of <see cref="Resize"/>.
    /// </summary>
    public static PieceOperationResult ResizeRectangle(ProjectDocument document, Guid pieceId, double? width, double? height)
    {
        var refusal = Locate(document, pieceId, requireUnlocked: true, out _, out var piece);
        if (refusal is not null) return refusal;
        if (piece.Curves is not null || piece.Grainline is not null || piece.Notches is not null
            || document.GradingRules.Any(r => r.SourcePieceId == pieceId))
            return Fail(PieceOperationError.Unsupported, ExtrasText);
        if (!IsAxisParallelRectangle(piece)) return Fail(PieceOperationError.Unsupported, NotRectangleText);
        return Resize(document, pieceId, width, height);
    }

    private static bool IsAxisParallelRectangle(PatternPiece piece)
    {
        if (!piece.Closed || piece.Points.Count != 4) return false;
        for (var i = 0; i < 4; i++)
        {
            var a = piece.Points[i]; var b = piece.Points[(i + 1) % 4];
            if ((a.X == b.X) == (a.Y == b.Y)) return false; // exactly one coordinate must stay equal along an edge
        }
        return piece.Points[0].X != piece.Points[2].X && piece.Points[0].Y != piece.Points[2].Y;
    }

    /// <summary>
    /// Scales the bounding box about its top-left corner to the new size, keeping the top-left corner where it is. Each
    /// coordinate becomes <c>min + (c − min) · factor</c>, and points on the far edge land exactly on <c>min + new size</c>, so a
    /// rectangle gets exactly the requested sides. Pass null to keep a dimension. Point order, closure and precision are kept; notch
    /// marks follow their vertices, the grainline is scaled with the piece and flagged for review, and a grading rule is left
    /// untouched with a warning because it no longer matches. A dimension that is zero now (a horizontal line's height) cannot be changed.
    /// Pieces with analytic curves are refused. A valid contour must stay valid.
    /// </summary>
    public static PieceOperationResult Resize(ProjectDocument document, Guid pieceId, double? width, double? height)
    {
        var refusal = Locate(document, pieceId, requireUnlocked: true, out var index, out var piece);
        if (refusal is not null) return refusal;
        if (width is null && height is null)
            return Fail(PieceOperationError.InvalidValue, "Genişlik veya yükseklikten en az birini belirtin.");
        if (width is { } w && CheckSide(w, "Genişlik") is { } badWidth) return badWidth;
        if (height is { } h && CheckSide(h, "Yükseklik") is { } badHeight) return badHeight;
        if (piece.Curves is not null) return CurvesUnsupported(piece, "yeniden boyutlandırma");
        return Guard(piece, () =>
        {
            var b = Geometry.Bounds(piece.Points);
            if (width is not null && b.Width <= 0)
                return Fail(PieceOperationError.InvalidValue, $"{Quote(piece.Name)} parçasının genişliği 0; genişlik değiştirilemez. Yalnızca yüksekliği belirtin.");
            if (height is not null && b.Height <= 0)
                return Fail(PieceOperationError.InvalidValue, $"{Quote(piece.Name)} parçasının yüksekliği 0; yükseklik değiştirilemez. Yalnızca genişliği belirtin.");
            var newWidth = width ?? b.Width;
            var newHeight = height ?? b.Height;
            var scaleX = width is null ? 1 : newWidth / b.Width;
            var scaleY = height is null ? 1 : newHeight / b.Height;
            if (!double.IsFinite(scaleX) || !double.IsFinite(scaleY)) return Fail(PieceOperationError.OutOfRange, OutOfRangeText);

            double MapX(double x) => width is null || scaleX == 1 ? x : x == b.MaxX ? b.MinX + newWidth : b.MinX + (x - b.MinX) * scaleX;
            double MapY(double y) => height is null || scaleY == 1 ? y : y == b.MaxY ? b.MinY + newHeight : b.MinY + (y - b.MinY) * scaleY;
            Point2 Map(Point2 p) => new(MapX(p.X), MapY(p.Y));

            var points = piece.Points.Select(Map).ToList();
            if (points.Any(p => !InRange(p))) return Fail(PieceOperationError.OutOfRange, OutOfRangeText);
            var actual = Geometry.Bounds(points);
            if ((width is not null && !SpanMatches(newWidth, actual.Width)) || (height is not null && !SpanMatches(newHeight, actual.Height)))
                return Fail(PieceOperationError.Precision,
                    $"{Quote(piece.Name)} için istenen ölçü sayısal hassasiyetle temsil edilemiyor; hiçbir şey değişmedi. Parçayı orijine yaklaştırın veya daha küçük bir ölçü girin.");
            if (!StaysValid(piece, points))
                return Fail(PieceOperationError.InvalidShape,
                    $"Yeniden boyutlandırma {Quote(piece.Name)} konturunu geçersiz yapardı (noktalar üst üste biniyor); hiçbir şey değişmedi. Daha büyük bir ölçü girin.");

            var changed = !points.SequenceEqual(piece.Points);
            // Marks must match their vertices before they are re-anchored (also rejects a stray vertex index).
            if (piece.Notches is not null) _ = MarkedPieceTranslation.Translate(piece, 0, 0);
            var resized = piece.Clone();
            resized.Points = points;
            resized.Notches = piece.Notches?.Select(m => new NotchMark { VertexIndex = m.VertexIndex, Position = points[m.VertexIndex] }).ToList();
            var warnings = new List<string>();
            if (changed && piece.Grainline is { } grain)
            {
                var start = Map(grain.Start); var end = Map(grain.End);
                if (!InRange(start) || !InRange(end) || start == end)
                    return Fail(PieceOperationError.Precision, $"{Quote(piece.Name)} iplik yönü yeni ölçüyle güvenle hesaplanamadı; hiçbir şey değişmedi.");
                resized.Grainline = new Grainline { Start = start, End = end, NeedsReview = true };
                warnings.Add("İplik yönü parçayla birlikte ölçeklendi ve gözden geçirilmek üzere işaretlendi.");
            }
            if (changed && document.GradingRules.Any(r => r.SourcePieceId == piece.Id)) warnings.Add(StaleRuleWarning);
            var summary = changed
                ? $"{Quote(piece.Name)} yeniden boyutlandırıldı: {Mm(actual.Width)} × {Mm(actual.Height)} (sol üst köşe sabit)."
                : $"Ölçü zaten {Mm(actual.Width)[..^3]} × {Mm(actual.Height)}. Değişiklik yok.";
            return Replace(document, index, resized, null, summary, warnings);
        });
    }

    // A contour that was valid must stay valid. Contours that were not (drafts) or are too large to check are not judged here.
    private static bool StaysValid(PatternPiece source, IReadOnlyList<Point2> result)
    {
        try
        {
            if (source.Closed)
            {
                if (source.Points.Count > Geometry.MaxValidatedContourVertices) return true;
                try { Geometry.ValidateContour(source.Points); } catch (ArgumentException) { return true; }
                Geometry.ValidateContour(result);
            }
            else
            {
                if (source.Points.Count > LargeOpenPathValidator.MaximumStoredPoints) return true;
                try { LargeOpenPathValidator.Validate(source.Points); } catch (ArgumentException) { return true; }
                LargeOpenPathValidator.Validate(result);
            }
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    /// <summary>
    /// Rotates by a multiple of 90° about the centre of the bounding box. Positive is clockwise on screen (the Windows "90° sağa"
    /// button), negative counter-clockwise; 270 equals −90 and 360 changes nothing. Any other angle is refused. Notch marks, grainline and a
    /// matching grading rule rotate with the piece. Pieces with analytic curves are refused.
    /// </summary>
    public static PieceOperationResult Rotate(ProjectDocument document, Guid pieceId, double degrees)
    {
        var refusal = Locate(document, pieceId, requireUnlocked: true, out var index, out var piece);
        if (refusal is not null) return refusal;
        if (!double.IsFinite(degrees) || degrees % 90 != 0)
            return Fail(PieceOperationError.InvalidValue, AngleText);
        if (piece.Curves is not null) return CurvesUnsupported(piece, "döndürme");
        var turn = degrees % 360;
        if (turn < 0) turn += 360;
        // 270 is expressed as −90 so that the grading transform names match the Windows buttons.
        var signed = turn == 270 ? -90 : turn;
        var kind = signed switch { 90 => OrthogonalGradingTransform.RotateLeft, -90 => OrthogonalGradingTransform.RotateRight, _ => OrthogonalGradingTransform.RotateHalf };
        var summary = signed switch
        {
            0 => $"{Quote(piece.Name)} tam turla döndürüldü; değişiklik gerekmedi.",
            90 => $"{Quote(piece.Name)} 90° saat yönünde döndürüldü.",
            -90 => $"{Quote(piece.Name)} 90° saat yönünün tersine döndürüldü.",
            _ => $"{Quote(piece.Name)} 180° döndürüldü."
        };
        return Guard(piece, () => RigidCore(document, index, piece, signed == 0 ? null : kind,
            (points, center) => Geometry.Rotate(points, signed, center), summary));
    }

    /// <summary>
    /// Flips the piece across the horizontal axis through the centre of its bounding box (top and bottom swap): the Windows
    /// "Yatay aynala" button. See <see cref="MirrorVertical"/> for the left-right flip.
    /// </summary>
    public static PieceOperationResult MirrorHorizontal(ProjectDocument document, Guid pieceId)
        => Mirror(document, pieceId, verticalAxis: false, OrthogonalGradingTransform.MirrorHorizontal, "yatay eksende (alt-üst) aynalandı");

    /// <summary>
    /// Flips the piece across the vertical axis through the centre of its bounding box (left and right swap): the Windows
    /// "Aynala" button. See <see cref="MirrorHorizontal"/> for the top-bottom flip.
    /// </summary>
    public static PieceOperationResult MirrorVertical(ProjectDocument document, Guid pieceId)
        => Mirror(document, pieceId, verticalAxis: true, OrthogonalGradingTransform.MirrorVertical, "dikey eksende (sağ-sol) aynalandı");

    private static PieceOperationResult Mirror(ProjectDocument document, Guid pieceId, bool verticalAxis, OrthogonalGradingTransform kind, string what)
    {
        var refusal = Locate(document, pieceId, requireUnlocked: true, out var index, out var piece);
        if (refusal is not null) return refusal;
        if (piece.Curves is not null) return CurvesUnsupported(piece, "aynalama");
        return Guard(piece, () => RigidCore(document, index, piece, kind,
            (points, center) => Geometry.Mirror(points, verticalAxis, verticalAxis ? center.X : center.Y), $"{Quote(piece.Name)} {what}."));
    }

    // Shared path of rotation and mirroring: transform about the bounding-box centre, re-anchor notch marks by index,
    // carry the grainline with its contour anchor and the grading rule through the matching orthogonal transform.
    private static PieceOperationResult RigidCore(ProjectDocument document, int index, PatternPiece piece, OrthogonalGradingTransform? kind,
        Func<IReadOnlyList<Point2>, Point2, List<Point2>> transform, string summary)
    {
        if (kind is null) return Replace(document, index, piece.Clone(), null, summary, []);
        var center = Geometry.Bounds(piece.Points).Center;
        var result = transform(piece.Points, center);
        if (result.Count != piece.Points.Count)
            return Fail(PieceOperationError.Precision, $"{Quote(piece.Name)} dönüşümde köşe sayısı değişti; hiçbir şey değişmedi.");
        var transformed = piece.Clone();
        transformed.Points = result;
        if (piece.Notches is not null)
        {
            // Marks must match their vertices before and after, exactly as the Windows transform requires.
            _ = MarkedPieceTranslation.Translate(piece, 0, 0);
            transformed.Notches = piece.Notches.Select(m => new NotchMark { VertexIndex = m.VertexIndex, Position = result[m.VertexIndex] }).ToList();
        }
        if (piece.Grainline is not null)
            transformed.Grainline = GrainlineMath.Transform(piece.Grainline, piece.Points[0], result[0], points => transform(points, center));
        var warnings = new List<string>();
        GradingRuleSet? newRule = null;
        var rule = document.GradingRules.FirstOrDefault(r => r.SourcePieceId == piece.Id);
        if (rule is not null && !result.SequenceEqual(piece.Points))
        {
            newRule = rule.OrthogonalFor(piece, result, kind.Value);
            if (newRule is null) warnings.Add(StaleRuleWarning);
            else warnings.Add("Serileme artışları parçayla birlikte dönüştürüldü; bedenleri kontrol edin.");
        }
        return Replace(document, index, transformed, newRule, summary, warnings);
    }
}

/// <summary>Grainline helpers that move or rotate the two ends together with the contour anchor, so a rounding that would separate them is refused.</summary>
internal static class GrainlineMath
{
    internal static Grainline Translate(Grainline grain, Point2? anchor, Point2? placedAnchor, double dx, double dy)
    {
        Validate(grain);
        if (anchor is { } a)
        {
            var moved = Geometry.Translate([a, grain.Start, grain.End], dx, dy);
            if (moved[0] != placedAnchor) throw new ArgumentException("İplik yönü ve kontur aynı konuma güvenle taşınamadı.");
            return Build(grain, moved[1], moved[2]);
        }
        var ends = Geometry.Translate([grain.Start, grain.End], dx, dy);
        return Build(grain, ends[0], ends[1]);
    }

    internal static Grainline Transform(Grainline grain, Point2 anchor, Point2 placedAnchor, Func<IReadOnlyList<Point2>, List<Point2>> transform)
    {
        Validate(grain);
        var source = new[] { anchor, grain.Start, grain.End };
        var moved = transform(source);
        if (moved.Count != 3 || moved[0] != placedAnchor)
            throw new ArgumentException("İplik yönü ve kontur aynı dönüşümle güvenle hesaplanamadı.");
        var result = Build(grain, moved[1], moved[2]);
        // Rotation and reflection are rigid: lengths and distances to the anchor must survive within a strict relative bound.
        for (var i = 0; i < 3; i++)
            for (var j = i + 1; j < 3; j++)
            {
                var before = Geometry.Distance(source[i], source[j]);
                var after = Geometry.Distance(moved[i], moved[j]);
                if (before == 0 ? after != 0 : Math.Abs(after - before) > before * 1e-9)
                    throw new ArgumentException("Dönüşüm sırasında iplik yönü sayısal hassasiyetle korunamıyor.");
            }
        return result;
    }

    private static Grainline Build(Grainline source, Point2 start, Point2 end)
    {
        var result = new Grainline { Start = start, End = end, NeedsReview = source.NeedsReview };
        Validate(result);
        return result;
    }

    private static void Validate(Grainline grain)
    {
        Geometry.Validate(grain.Start); Geometry.Validate(grain.End);
        if (grain.Start == grain.End) throw new ArgumentException("İplik yönünün iki ucu farklı olmalıdır.");
    }
}
