using static TekstilCAD.Core.Commands.CommandPlannerText;

namespace TekstilCAD.Core.Commands;

internal sealed partial class PlannerContext
{
    // The Desktop applies moves, rotations and mirrors with its own routines; Core checks them with PieceOperations so that
    // the preview numbers are the ones the edit produces. Curved pieces are refused here (R12): the Desktop move routine
    // cannot keep curve data, and a project that loses it fails validation.
    private PlanResult PlanMoveBy(MoveByCommand c)
    {
        string summary;
        if (!TryEditTarget(out var piece, out var refusal)) return refusal;
        if (piece.Curves is not null) return Refuse(Refusals.HasCurves());
        var dx = c.RightMm;
        var dy = c.DownMm;
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) return Refuse(Refusals.NumberMissingFor(Refusals.MoveExample));
        var before = BoundsOf(piece);
        if (Math.Round(Math.Abs(dx), 6) == 0 && Math.Round(Math.Abs(dy), 6) == 0) return Refuse(Custom(Refusals.NoChange, $"Parça zaten burada: {Position(before.MinX, before.MinY)}. Değişiklik yok."));
        var result = PieceOperations.MoveBy(Doc, piece.Id, dx, dy);
        if (!result.Succeeded) return Refuse(MapFailure(result));
        var moved = result.Piece!;
        var after = BoundsOf(moved);

        var cmParts = new List<string>(2);
        var mmOnly = new List<string>(2);
        if (Math.Round(Math.Abs(dx), 6) != 0) { cmParts.Add($"{N(Math.Abs(dx) / 10)} cm {(dx > 0 ? "sağa" : "sola")}"); mmOnly.Add($"{Mm(Math.Abs(dx))} {(dx > 0 ? "sağa" : "sola")}"); }
        if (Math.Round(Math.Abs(dy), 6) != 0) { cmParts.Add($"{N(Math.Abs(dy) / 10)} cm {(dy > 0 ? "aşağı" : "yukarı")}"); mmOnly.Add($"{Mm(Math.Abs(dy))} {(dy > 0 ? "aşağı" : "yukarı")}"); }
        summary = string.Join(" ve ", mmOnly);
        // One axis: "25 mm sağa taşınacak (2,5 cm)"; two axes: "20 mm sağa ve 10 mm yukarı taşınacak (2 cm sağa ve 1 cm yukarı)".
        var cmText = mmOnly.Count == 1 ? $"({N(Math.Max(Math.Abs(dx), Math.Abs(dy)) / 10)} cm)" : $"({string.Join(" ve ", cmParts)})";
        var preview = WithUnitNote(
            $"{Quote(piece.Name)} {summary} taşınacak {cmText}. Sol üst köşe {Position(after.MinX, after.MinY)} olacak.");

        var alternatives = new List<RefusalButton>();
        if (unitAssumed)
        {
            var cm = new List<string>(2);
            if (Math.Round(Math.Abs(dx), 6) != 0) cm.Add($"{N(Math.Abs(dx))} cm {(dx > 0 ? "sağa" : "sola")}");
            if (Math.Round(Math.Abs(dy), 6) != 0) cm.Add($"{N(Math.Abs(dy))} cm {(dy > 0 ? "aşağı" : "yukarı")}");
            alternatives.Add(new RefusalButton(string.Join(" ", cm) + (cm.Count == 1 ? " taşı" : "")));
        }
        var plan = new Plan(PlanKind.MovePiece, PlanClass.Geometry, c, preview,
            PieceStamp.Capture(view, selection, piece.Id, StampScope.Piece))
        {
            Ghost = Scene(GhostStyle.Preview, [Outline(moved)], [new GhostLabel(new Point2(after.MinX / 2 + after.MaxX / 2, after.MinY), summary, GhostLabelPlacement.Above)]),
            Caveats = CaveatsFor(piece, "taşınır", result),
            Alternatives = alternatives,
            ResultText = $"{Quote(piece.Name)} {summary} taşındı.",
            ActionName = "Taşıma",
            TargetPieceId = piece.Id,
            NeedsSelectedPiece = true,
            Operation = new MovePieceOperation(piece.Id, dx, dy),
        };
        return Of(plan);
    }

    private PlanResult PlanMoveTo(MoveToCommand c)
    {
        if (!TryEditTarget(out var piece, out var refusal)) return refusal;
        if (piece.Curves is not null) return Refuse(Refusals.HasCurves());
        if (!double.IsFinite(c.XMm) || !double.IsFinite(c.YMm)) return Refuse(Refusals.NumberMissingFor(Refusals.MoveExample));
        var before = BoundsOf(piece);
        var result = PieceOperations.MoveTo(Doc, piece.Id, c.XMm, c.YMm);
        if (!result.Succeeded) return Refuse(MapFailure(result));
        if (!result.ChangesDocument) return Refuse(Custom(Refusals.NoChange, $"Parça zaten burada: {Position(before.MinX, before.MinY)}. Değişiklik yok."));
        var moved = result.Piece!;
        var after = BoundsOf(moved);
        var preview = WithUnitNote(
            $"{Quote(piece.Name)} sol üst köşesi soldan {MmCm(c.XMm)}, yukarıdan {MmCm(c.YMm)} olacak şekilde taşınacak.");
        var plan = new Plan(PlanKind.MovePieceTo, PlanClass.Geometry, c, preview,
            PieceStamp.Capture(view, selection, piece.Id, StampScope.Piece))
        {
            Ghost = Scene(GhostStyle.Preview, [Outline(moved)],
                [new GhostLabel(new Point2(after.MinX, after.MinY), $"soldan {N(c.XMm)}, yukarıdan {N(c.YMm)} mm", GhostLabelPlacement.Above)]),
            Caveats = CaveatsFor(piece, "taşınır", result),
            ResultText = $"{Quote(piece.Name)} taşındı: sol üst köşe soldan {Mm(c.XMm)}, yukarıdan {Mm(c.YMm)}.",
            ActionName = "Taşıma",
            TargetPieceId = piece.Id,
            NeedsSelectedPiece = true,
            Operation = new MovePieceToOperation(piece.Id, c.XMm, c.YMm, c.XMm - before.MinX, c.YMm - before.MinY),
        };
        return Of(plan);
    }

    private PlanResult PlanRotate(RotateCommand c)
    {
        if (!TryEditTarget(out var piece, out var refusal)) return refusal;
        if (c.Degrees is not (90 or 180 or 270)) return Refuse(Refusals.AngleNotSupported());
        if (piece.Curves is not null) return Refuse(Refusals.HasCurves());
        if (piece.Points.Count > MaximumTransformPoints) return Refuse(Refusals.TooManyPointsRefusal());
        var clockwise = c.Direction == RotationDirection.Clockwise;
        var turn = c.Degrees == 180 ? 180 : (c.Degrees == 90) == clockwise ? 90 : -90; // clockwise degrees: 90, -90 or 180
        var result = PieceOperations.Rotate(Doc, piece.Id, turn);
        if (!result.Succeeded) return Refuse(MapFailure(result));
        if (!result.ChangesDocument) return Refuse(Custom(Refusals.NoChange, "Parça zaten böyle duruyor. Değişiklik yok."));
        var rotated = result.Piece!;
        var before = BoundsOf(piece);
        var after = BoundsOf(rotated);
        var direction = c.Degrees == 180 ? "" : clockwise ? "saat yönünde " : "saat yönünün tersine ";
        var sizeText = SameSize(before, after)
            ? "Parçanın ölçüsü değişmez."
            : $"Ölçüsü {SizeMm(before.Width, before.Height)}'den {SizeMm(after.Width, after.Height)}'ye değişecek.";
        var preview = $"{Quote(piece.Name)} {direction}{c.Degrees}° döndürülecek (orta noktası yerinde kalır). {sizeText}";
        var operationId = turn == 180 ? "rotate-half" : turn == 90 ? "rotate-cw" : "rotate-ccw";
        var alternatives = new List<RefusalButton>();
        if (c.Degrees == 90 && clockwise)
        {
            alternatives.Add(new RefusalButton("90 sola döndür"));
            alternatives.Add(new RefusalButton("180 derece"));
        }
        var plan = new Plan(PlanKind.RotatePiece, PlanClass.Geometry, c, preview,
            PieceStamp.Capture(view, selection, piece.Id, StampScope.Piece))
        {
            Ghost = Scene(GhostStyle.Preview, [Outline(rotated)], [new GhostLabel(after.Center, $"{c.Degrees}°{(c.Degrees == 180 ? "" : clockwise ? " saat yönünde" : " saat yönünün tersine")}", GhostLabelPlacement.Center)]),
            Caveats = CaveatsFor(piece, "döner", result),
            Alternatives = alternatives,
            ResultText = $"{Quote(piece.Name)} {direction}{c.Degrees}° döndürüldü.",
            ActionName = "Döndürme",
            TargetPieceId = piece.Id,
            NeedsSelectedPiece = true,
            Operation = new RotatePieceOperation(piece.Id, turn, operationId),
        };
        return Of(plan);
    }

    private static bool SameSize(Bounds2 a, Bounds2 b) =>
        Math.Abs(a.Width - b.Width) <= 1e-9 * Math.Max(1, Math.Max(a.Width, b.Width))
        && Math.Abs(a.Height - b.Height) <= 1e-9 * Math.Max(1, Math.Max(a.Height, b.Height));

    private PlanResult PlanMirror(MirrorCommand c)
    {
        if (!TryEditTarget(out var piece, out var refusal)) return refusal;
        if (piece.Curves is not null) return Refuse(Refusals.HasCurves());
        if (piece.Points.Count > MaximumTransformPoints) return Refuse(Refusals.TooManyPointsRefusal());
        var vertical = c.Axis == MirrorAxis.Vertical;
        var result = vertical ? PieceOperations.MirrorVertical(Doc, piece.Id) : PieceOperations.MirrorHorizontal(Doc, piece.Id);
        if (!result.Succeeded) return Refuse(MapFailure(result));
        if (!result.ChangesDocument) return Refuse(Custom(Refusals.NoChange, "Parça zaten simetrik; aynalamak bir şeyi değiştirmez. Değişiklik yok."));
        var mirrored = result.Piece!;
        var way = vertical ? "sağ-sol" : "alt-üst";
        var line = vertical ? "dikey" : "yatay";
        var preview = $"{Quote(piece.Name)} {way} yönünde aynalanacak (ortasından geçen {line} çizgiye göre).";
        var plan = new Plan(PlanKind.MirrorPiece, PlanClass.Geometry, c, preview,
            PieceStamp.Capture(view, selection, piece.Id, StampScope.Piece))
        {
            Ghost = Scene(GhostStyle.Preview, [Outline(mirrored)], Array.Empty<GhostLabel>()),
            Caveats = CaveatsFor(piece, "aynalanır", result),
            ResultText = $"{Quote(piece.Name)} {way} yönünde aynalandı.",
            ActionName = "Aynalama",
            TargetPieceId = piece.Id,
            NeedsSelectedPiece = true,
            Operation = new MirrorPieceOperation(piece.Id, c.Axis, vertical ? "mirror-vertical" : "mirror-horizontal"),
        };
        return Of(plan);
    }

    private PlanResult PlanResize(ResizeCommand c)
    {
        if (!TryEditTarget(out var piece, out var refusal)) return refusal;
        if (c.WidthMm is null && c.HeightMm is null) return Refuse(Refusals.NumberMissingFor("genişliği 95 yap"));
        // R11 before R10, as PieceOperations.ResizeRectangle checks them (extra data first, then the shape).
        var hasRule = Doc.GradingRules.Any(r => r.SourcePieceId == piece.Id);
        if (piece.Curves is not null || piece.Grainline is not null || piece.Notches is not null || piece.Darts is not null || hasRule)
            return Refuse(Refusals.HasExtraData());
        var before = BoundsOf(piece);
        var result = PieceOperations.ResizeRectangle(Doc, piece.Id, c.WidthMm, c.HeightMm);
        // Preserve the existing rectangle path and all its guards. Expand only the specific
        // unsupported-shape case to validated, closed, unmarked linear polygons.
        var polygonResize = !result.Succeeded && result.Error == PieceOperations.NotRectangleText;
        if (polygonResize)
        {
            if (!piece.Closed || piece.Points.Count > Geometry.MaxValidatedContourVertices)
                return Refuse(Custom(Refusals.NotRectangle, "Yalnızca doğrulanabilir kapalı çokgenlerin ölçüsü değiştirilebilir."));
            if (piece.Metadata is not null)
                return Refuse(Custom(Refusals.ExtraData, "Bu parça üretim bilgisi taşıyor; ölçü değiştirme işlemi uygulanmadı."));
            try { Geometry.ValidateContour(piece.Points); }
            catch (ArgumentException)
            {
                return Refuse(Custom(Refusals.NotRectangle, "Kontur geçersiz; ölçü değiştirme işlemi uygulanmadı."));
            }
            result = PieceOperations.Resize(Doc, piece.Id, c.WidthMm, c.HeightMm);
        }
        if (!result.Succeeded) return Refuse(MapFailure(result));
        if (!result.ChangesDocument) return Refuse(Refusals.NothingChanges(SizeMm(before.Width, before.Height)));
        var resized = result.Piece!;
        var after = BoundsOf(resized);
        var name = Quote(piece.Name);
        string preview, summary;
        if (c.WidthMm is { } w && c.HeightMm is null)
        {
            preview = $"{name} genişliği {Mm(before.Width)}'den {Mm(after.Width)}'ye ({N(after.Width / 10)} cm) değişecek. Yükseklik {Mm(before.Height)} aynı kalır; sol kenar yerinde durur.";
            summary = $"genişliği {Mm(before.Width)}'den {Mm(after.Width)}'ye";
        }
        else if (c.HeightMm is { } && c.WidthMm is null)
        {
            preview = $"{name} yüksekliği {Mm(before.Height)}'den {Mm(after.Height)}'ye ({N(after.Height / 10)} cm) değişecek. Genişlik {Mm(before.Width)} aynı kalır; üst kenar yerinde durur.";
            summary = $"yüksekliği {Mm(before.Height)}'den {Mm(after.Height)}'ye";
        }
        else
        {
            preview = $"{name} ölçüsü {SizeMm(before.Width, before.Height)}'den {N(after.Width)} × {N(after.Height)} mm'ye ({N(after.Width / 10)} × {N(after.Height / 10)} cm) değişecek. Sol üst köşe yerinde durur.";
            summary = $"ölçüsü {SizeMm(before.Width, before.Height)}'den {SizeMm(after.Width, after.Height)}'ye";
        }
        var alternatives = new List<RefusalButton>();
        if (unitAssumed)
        {
            if (c.WidthMm is { } cw && c.HeightMm is { } ch) alternatives.Add(new RefusalButton($"{N(cw)} x {N(ch)} cm yap"));
            else if (c.WidthMm is { } onlyW) alternatives.Add(new RefusalButton($"genişliği {N(onlyW)} cm yap"));
            else if (c.HeightMm is { } onlyH) alternatives.Add(new RefusalButton($"yüksekliği {N(onlyH)} cm yap"));
        }
        if (polygonResize)
            preview += " Ölçüler konturun sınır kutusuna aittir; bu işlem beden serileme değildir.";
        var labels = new List<GhostLabel>();
        if (c.WidthMm is not null || c.HeightMm is not null) labels.AddRange(SizeLabels(after));
        var plan = new Plan(PlanKind.ResizePiece, PlanClass.Geometry, c, WithUnitNote(preview),
            PieceStamp.Capture(view, selection, piece.Id, StampScope.Document), Doc.Clone(), result.Document)
        {
            Ghost = Scene(GhostStyle.Preview, [Outline(resized)], labels),
            Caveats = result.Warnings.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray(),
            Alternatives = alternatives,
            ResultText = $"{name} {summary} değişti.",
            ActionName = "Ölçü değiştirme",
            TargetPieceId = piece.Id,
            NeedsSelectedPiece = true,
        };
        return Of(plan);
    }

    private PlanResult PlanProjectRename(RenameProjectCommand c)
    {
        if (ProjectGuards() is { } guard) return Refuse(guard);
        var name = (c.Name ?? "").Trim();
        if (name.Length == 0) return Refuse(Refusals.EmptyNameRefusal());
        if (name.Length > CommandParser.MaxNameLength || name.Any(char.IsControl) || !PieceOperations.IsWellFormed(name))
            return Refuse(Custom(Refusals.EmptyName, $"Tek satırda, en fazla {CommandParser.MaxNameLength} karakterlik geçerli bir ad yazın."));
        if (name == Doc.Name)
            return Refuse(Custom(Refusals.NoChange, $"Projenin adı zaten {QuoteFull(name)}."));
        var edited = Doc.Clone();
        edited.Name = name;
        return Of(new Plan(PlanKind.RenameProject, PlanClass.Geometry, c,
            $"Projenin adı {QuoteFull(name)} olacak.",
            PieceStamp.Capture(view, selection, null, StampScope.Document), Doc.Clone(), edited)
        {
            ResultText = $"Projenin adı {QuoteFull(name)} oldu.",
            ActionName = "Projeyi adlandırma",
        });
    }

    private PlanResult PlanRename(RenameCommand c)
    {
        if (!TryEditTarget(out var piece, out var refusal)) return refusal;
        var newName = (c.Name ?? "").Trim();
        if (newName.Length == 0) return Refuse(Refusals.EmptyNameRefusal());
        if (string.Equals(newName, piece.Name, StringComparison.Ordinal))
            return Refuse(Custom(Refusals.NoChange, $"Ad zaten {QuoteFull(newName)}. Değişiklik yok."));
        var result = PieceOperations.Rename(Doc, piece.Id, newName);
        if (!result.Succeeded) return Refuse(MapFailure(result));
        var renamed = result.Piece!;
        var plan = new Plan(PlanKind.RenamePiece, PlanClass.Geometry, c,
            $"{Quote(piece.Name)} adı {QuoteFull(renamed.Name)} olacak.",
            PieceStamp.Capture(view, selection, piece.Id, StampScope.Document), Doc.Clone(), result.Document)
        {
            ResultText = $"{Quote(piece.Name)} adı {QuoteFull(renamed.Name)} oldu.",
            ActionName = "Yeniden adlandırma",
            TargetPieceId = piece.Id,
            NeedsSelectedPiece = true,
        };
        return Of(plan);
    }

    private PlanResult PlanDelete(DeleteCommand c)
    {
        PatternPiece piece;
        var named = c.Target == PieceTarget.Named;
        if (named)
        {
            if (ProjectGuards() is { } guard) return Refuse(guard);
            if (!TryResolve(c.Name, "sil", out var match, out var resolved)) return resolved;
            piece = Doc.Pieces[match!.Index];
            if (LayerTools.IsPieceLocked(Doc, piece)) return Refuse(Refusals.Locked());
        }
        else if (c.Target == PieceTarget.Selected)
        {
            if (!TryEditTarget(out piece, out var refusal)) return refusal;
        }
        else return Refuse(Refusals.Unrecognized(string.Empty));
        return DeletePlan(c, piece, named);
    }

    private PlanResult DeletePlan(Command command, PatternPiece piece, bool named)
    {
        var result = PieceOperations.Delete(Doc, piece.Id);
        if (!result.Succeeded) return Refuse(MapFailure(result));
        var safe = SafeBounds(piece);
        var caveats = new List<string>();
        var rules = Doc.GradingRules.Count(r => r.SourcePieceId == piece.Id);
        if (rules > 0) caveats.Add($"Parçaya bağlı {rules} serileme kuralı da silinir.");
        var ghost = safe is { } bounds
            ? Scene(GhostStyle.Danger, [Outline(piece)], [new GhostLabel(bounds.Center, "silinecek", GhostLabelPlacement.Center)])
            : GhostScene.Empty;
        var plan = new Plan(PlanKind.DeletePiece, PlanClass.Dangerous, command,
            safe is { } b ? $"{Quote(piece.Name)} silinecek ({SizeMm(b.Width, b.Height)}). Geri alabilirsiniz." : $"{Quote(piece.Name)} silinecek. Geri alabilirsiniz.",
            PieceStamp.Capture(view, selection, piece.Id, StampScope.Document), Doc.Clone(), result.Document)
        {
            Ghost = ghost,
            Caveats = caveats,
            ResultText = $"{Quote(piece.Name)} silindi.",
            ActionName = "Silme",
            TargetPieceId = piece.Id,
            NeedsSelectedPiece = !named,
        };
        return Of(plan);
    }

    internal PlanResult PlanDuplicate(double offsetX, double offsetY)
    {
        if (!TryEditTarget(out var piece, out var refusal)) return refusal;
        var result = PieceOperations.Duplicate(Doc, piece.Id, offsetX, offsetY);
        if (!result.Succeeded) return Refuse(MapFailure(result));
        var edited = result.Document!.Clone();
        var stamp = PieceStamp.Capture(view, selection, piece.Id, StampScope.Document);
        var newId = Reid(edited, result.PieceId!.Value, $"{stamp.ContentDigest}|dup|{Inv(offsetX)}|{Inv(offsetY)}");
        var copy = edited.Pieces.First(p => p.Id == newId);
        var where = new List<string>(2);
        if (offsetX != 0) where.Add($"{MmCm(Math.Abs(offsetX))} {(offsetX > 0 ? "sağa" : "sola")}");
        if (offsetY != 0) where.Add($"{MmCm(Math.Abs(offsetY))} {(offsetY > 0 ? "aşağı" : "yukarı")}");
        var shift = where.Count == 0 ? "aynı konumda" : string.Join(" ve ", where) + " kaydırılarak";
        var plan = new Plan(PlanKind.DuplicatePiece, PlanClass.Geometry, new HandoffCommand(HandoffTarget.Duplicate),
            $"{Quote(piece.Name)} çoğaltılacak: {Quote(copy.Name)} adlı kopya {shift} eklenecek.",
            stamp, Doc.Clone(), edited)
        {
            Ghost = Scene(GhostStyle.Preview, [Outline(copy)], Array.Empty<GhostLabel>()),
            Caveats = result.Warnings.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray(),
            ResultText = $"{Quote(piece.Name)} çoğaltıldı: {Quote(copy.Name)}.",
            ActionName = "Çoğaltma",
            TargetPieceId = piece.Id,
            CreatedPieceId = newId,
            NeedsSelectedPiece = true,
            AddsPiece = true,
        };
        return Of(plan);
    }

    // ---- selecting by name or order ----------------------------------------------------------------------------------------

    private PlanResult PlanSelect(SelectCommand c)
    {
        if (view.RecoveryIncomplete) return Refuse(Refusals.RecoveryInProgress());
        if (view.DrawingInProgress) return Refuse(Refusals.DrawingPending());
        if (c.Target == PieceTarget.None)
        {
            var clear = new Plan(PlanKind.SelectPiece, PlanClass.NoChange, c, "Seçim bırakılacak. Parçalar değişmez.",
                PieceStamp.Capture(view, selection, null, StampScope.None))
            {
                ResultText = "Seçim bırakıldı.",
                ActionName = "Seçim",
            };
            return Of(clear);
        }
        if (Doc.Pieces.Count == 0) return Refuse(Refusals.NoPiecesYet());
        PatternPiece piece;
        switch (c.Target)
        {
            case PieceTarget.Named:
                {
                    if (!TryResolve(c.Name, "seç", out var match, out var refusal)) return refusal;
                    piece = Doc.Pieces[match!.Index];
                    break;
                }
            case PieceTarget.Next:
            case PieceTarget.Previous:
                {
                    var current = SelectedIndex();
                    var count = Doc.Pieces.Count;
                    var index = c.Target == PieceTarget.Next
                        ? (current < 0 ? 0 : (current + 1) % count)
                        : (current < 0 ? count - 1 : (current - 1 + count) % count);
                    piece = Doc.Pieces[index];
                    break;
                }
            default:
                {
                    if (!TrySelected(out piece, out var refusal)) return refusal;
                    break;
                }
        }
        var hasOutline = SafeBounds(piece) is not null;
        var plan = new Plan(PlanKind.SelectPiece, PlanClass.NoChange, c, $"{Quote(piece.Name)} seçilecek.",
            PieceStamp.Capture(view, selection, piece.Id, c.Target is PieceTarget.Next or PieceTarget.Previous ? StampScope.Document : StampScope.Piece))
        {
            ResultText = $"{Quote(piece.Name)} seçildi.",
            ActionName = "Seçim",
            TargetPieceId = piece.Id,
            Ghost = hasOutline ? Scene(GhostStyle.Preview, [Outline(piece)], Array.Empty<GhostLabel>()) : GhostScene.Empty,
        };
        return Of(plan);
    }

    private int SelectedIndex()
    {
        var ids = selection.PieceIds;
        if (ids is null || ids.Count != 1) return -1;
        return Doc.Pieces.FindIndex(p => p.Id == ids[0]);
    }

    /// <summary>
    /// Finds exactly one piece by the typed name (accent-folded, case-insensitive substring); an exact match wins over longer
    /// names that contain it ("ön beden" beside "Ön beden 2"). Zero matches is R15a, several is R15b (a choice with buttons).
    /// </summary>
    private bool TryResolve(string? typed, string verb, out PieceMatch? match, out PlanResult refusal)
    {
        match = null;
        refusal = null!;
        if (Doc.Pieces.Count == 0) { refusal = Refuse(Refusals.NoPiecesYet()); return false; }
        var query = (typed ?? "").Trim();
        var found = PieceOperations.FindPiecesByNameFolded(Doc, query);
        if (found.Count > 1)
        {
            var folded = PieceNameSearch.Fold(query);
            var exact = found.Where(m => PieceNameSearch.Fold(m.Name.Trim()) == folded).ToList();
            if (exact.Count >= 1) found = exact;
        }
        if (found.Count == 1) { match = found[0]; return true; }
        var names = Doc.Pieces.Select(p => OneLine(p.Name, 40)).Where(n => n.Length > 0).Take(3).ToList();
        if (found.Count == 0)
        {
            var typedShown = TurkishText.ContainsForbiddenWord(query) ? "…" : query;
            var examples = names.Where(n => !TurkishText.ContainsForbiddenWord(n)).ToList();
            refusal = Refuse(Refusals.NameNotFoundFor(typedShown, examples, verb));
            return false;
        }
        var buttons = found.Take(3).Select(m =>
        {
            var piece = Doc.Pieces[m.Index];
            var size = SafeBounds(piece) is { } sb ? " · " + SizeMm(sb.Width, sb.Height) : "";
            return new RefusalButton(m.Name + size, m.Name + " " + verb);
        }).ToArray();
        var sameNames = found.Select(m => PieceNameSearch.Fold(m.Name.Trim())).Distinct().Count() < found.Count;
        var hint = sameNames ? " Aynı adlı parçalar var; birini fareyle seçin." : "";
        refusal = Refuse(new Refusal(Refusals.NameAmbiguous,
            Refusals.TemplateNameAmbiguous.Replace("{n}", found.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) + hint, buttons));
        return false;
    }
}
