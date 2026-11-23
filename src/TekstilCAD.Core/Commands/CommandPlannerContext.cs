using System.Security.Cryptography;
using System.Text;
using static TekstilCAD.Core.Commands.CommandPlannerText;

namespace TekstilCAD.Core.Commands;

/// <summary>
/// One planning call: the view, the selection and whether the unit was assumed. Everything here is read-only with respect to the
/// document. The families of commands live in the other partial files (drawing, edit, other).
/// </summary>
internal sealed partial class PlannerContext
{
    /// <summary>The rotation, mirror and resize operations refuse beyond this (the main window opens its own window above it, R17).</summary>
    internal const int MaximumTransformPoints = 4096;

    private readonly DocumentView view;
    private readonly Selection selection;
    private readonly bool unitAssumed;

    internal PlannerContext(DocumentView view, Selection selection, bool unitAssumed)
    {
        this.view = view;
        this.selection = selection;
        this.unitAssumed = unitAssumed;
    }

    private ProjectDocument Doc => view.Document;

    internal PlanResult PlanCommand(Command command) => command switch
    {
        RectangleCommand c => PlanRectangle(c),
        LineCommand c => PlanLine(c),
        MoveByCommand c => PlanMoveBy(c),
        MoveToCommand c => PlanMoveTo(c),
        RotateCommand c => PlanRotate(c),
        MirrorCommand c => PlanMirror(c),
        ResizeCommand c => PlanResize(c),
        RenameProjectCommand c => PlanProjectRename(c),
        RenameCommand c => PlanRename(c),
        DeleteCommand c => PlanDelete(c),
        SelectCommand c => PlanSelect(c),
        UndoCommand c => PlanUndo(c),
        RedoCommand c => PlanRedo(c),
        ViewCommand c => PlanView(c),
        ToolCommand c => PlanTool(c),
        FinishDrawingCommand c => PlanFinishDrawing(c),
        FileCommand c => PlanFile(c),
        HandoffCommand c => PlanHandoff(c),
        HelpCommand c => Of(Window(PlanKind.Help, PlanClass.NoChange, c, "Örnek listesi açılacak.")),
        CancelPreviewCommand c => Of(Window(PlanKind.CancelPreview, PlanClass.NoChange, c, "Önizleme kapatılacak. Hiçbir şey değişmedi.")),
        null => Refuse(Refusals.Unrecognized(string.Empty)),
        _ => Refuse(Refusals.Failed("Bu komut tanınmıyor")),
    };

    // ---- small helpers ------------------------------------------------------------------------------------------------

    private static PlanResult Of(Plan plan) => PlanResult.Of(plan);

    private static PlanResult Refuse(Refusal refusal) => PlanResult.Refused(refusal);

    /// <summary>A plain "this cannot be done now" sentence (no R-code of the design fits); shown as R28a.</summary>
    private static Refusal Condition(string sentence) => new(Refusals.ExecutionFailed, sentence + ".", Array.Empty<RefusalButton>());

    private static Refusal Custom(string code, string message, params RefusalButton[] buttons) => new(code, message, buttons);

    /// <summary>A plan without a target piece and without content in its stamp (view, tool, help, window and file commands).</summary>
    private Plan Window(PlanKind kind, PlanClass planClass, Command command, string preview, string actionName = "", string resultText = "") =>
        new(kind, planClass, command, preview, PieceStamp.Capture(view, selection, null, StampScope.None))
        {
            ActionName = actionName,
            ResultText = resultText,
        };

    private string WithUnitNote(string preview) => unitAssumed ? preview + " " + UnitAssumedSentence : preview;

    /// <summary>Guards every command that changes the project meets before anything else (the order of the executor's ladder).</summary>
    private Refusal? ProjectGuards()
    {
        if (view.RecoveryIncomplete) return Refusals.RecoveryInProgress();
        if (view.FileOperationInProgress) return Refusals.Busy();
        if (view.DrawingInProgress) return Refusals.DrawingPending();
        if (view.HasUnappliedDrafts) return Refusals.AreaDraftsPending();
        return null;
    }

    /// <summary>The single selected piece, or the R03/R14 refusal.</summary>
    private bool TrySelected(out PatternPiece piece, out PlanResult refusal)
    {
        piece = null!;
        refusal = null!;
        var ids = selection.PieceIds ?? Array.Empty<Guid>();
        if (ids.Distinct().Count() > 1)
        {
            refusal = Refuse(Refusals.SeveralSelected());
            return false;
        }
        var found = ids.Count == 0 ? null : Doc.Pieces.FirstOrDefault(p => p.Id == ids[0]);
        if (found is null)
        {
            refusal = Refuse(NothingSelected());
            return false;
        }
        piece = found;
        return true;
    }

    /// <summary>R03: no pieces at all, or pieces but none selected (the buttons select the first three by name).</summary>
    private Refusal NothingSelected() => Doc.Pieces.Count == 0
        ? Refusals.NoPiecesYet()
        : Refusals.NoPieceSelected(Doc.Pieces.Select(p => OneLine(p.Name, 40)).Where(n => n.Length > 0).Take(3));

    /// <summary>The selected piece for an edit: guards, then selection, then the layer lock.</summary>
    private bool TryEditTarget(out PatternPiece piece, out PlanResult refusal)
    {
        piece = null!;
        refusal = null!;
        if (ProjectGuards() is { } guard) { refusal = Refuse(guard); return false; }
        if (!TrySelected(out piece, out refusal)) return false;
        if (LayerTools.IsPieceLocked(Doc, piece)) { refusal = Refuse(Refusals.Locked()); return false; }
        return true;
    }

    private static Bounds2 BoundsOf(PatternPiece piece) => Geometry.Bounds(piece.Points);

    /// <summary>
    /// Gives the piece a new id derived from the stamp digest and the command numbers instead of the random one the operation drew,
    /// so planning the same command on the same content twice gives the same plan. The id is unique in the document.
    /// </summary>
    private static Guid Reid(ProjectDocument edited, Guid oldId, string seed)
    {
        var piece = edited.Pieces.First(p => p.Id == oldId);
        for (var salt = 0; ; salt++)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed + "|" + salt));
            var id = new Guid(hash.AsSpan(0, 16));
            if (id == Guid.Empty || edited.Pieces.Any(p => p.Id == id)) continue;
            piece.Id = id;
            return id;
        }
    }

    /// <summary>The bounding box, or null when the piece has no usable points (a command that does not need it must not fail on it).</summary>
    private static Bounds2? SafeBounds(PatternPiece piece)
    {
        if (piece.Points is not { Count: > 0 }) return null;
        try { return Geometry.Bounds(piece.Points); }
        catch (ArgumentException) { return null; }
    }

    /// <summary>Maps a refusal of a core operation to its R-code. Texts of the operations are already complete Turkish sentences.</summary>
    private static Refusal MapFailure(PieceOperationResult result)
    {
        var text = result.Error ?? "";
        if (text == PieceOperations.SideRangeText) return Refusals.OutOfRangeMeasure();
        if (text == PieceOperations.AngleText) return Refusals.AngleNotSupported();
        if (text == PieceOperations.EmptyNameText) return Refusals.EmptyNameRefusal();
        if (text == PieceOperations.NotRectangleText) return Refusals.NotARectangle();
        if (text == PieceOperations.ExtrasText) return Refusals.HasExtraData();
        return result.ErrorCode switch
        {
            PieceOperationError.LayerLocked => text.StartsWith("Parça kilitli", StringComparison.Ordinal)
                ? Refusals.Locked()
                : Custom(Refusals.LockedLayer, text, new RefusalButton("Ayrıntılı görünüm")),
            PieceOperationError.OutOfRange => Custom(Refusals.OutOfRange, text),
            PieceOperationError.LayerUnavailable => Custom(Refusals.LockedLayer, text, new RefusalButton("Ayrıntılı görünüm")),
            _ => Custom(Refusals.ExecutionFailed, text),
        };
    }

    // ---- ghost helpers ------------------------------------------------------------------------------------------------

    private static GhostScene Scene(GhostStyle style, IEnumerable<GhostPolygon> polygons, IEnumerable<GhostLabel> labels) =>
        new(style, polygons.ToArray(), labels.ToArray());

    private static GhostPolygon Outline(PatternPiece piece) => new(piece.Points.ToArray(), piece.Closed);

    private static Point2 Mid(Point2 a, Point2 b) => new(a.X / 2 + b.X / 2, a.Y / 2 + b.Y / 2);

    /// <summary>Width and height labels on the top and right edge of a box.</summary>
    private static IEnumerable<GhostLabel> SizeLabels(Bounds2 b)
    {
        yield return new GhostLabel(new Point2(b.MinX / 2 + b.MaxX / 2, b.MinY), Mm(b.Width), GhostLabelPlacement.Above);
        yield return new GhostLabel(new Point2(b.MaxX, b.MinY / 2 + b.MaxY / 2), Mm(b.Height), GhostLabelPlacement.Right);
    }

    // ---- what travels with a piece --------------------------------------------------------------------------------------

    /// <summary>"iplik yönü ve serileme kuralı parçayla birlikte taşınır." for the parts the piece really has; null when it has none.</summary>
    private string? TravelsWith(PatternPiece piece, string verb, bool ruleTravels)
    {
        var items = new List<string>(3);
        if (piece.Grainline is not null) items.Add("iplik yönü");
        if (piece.Notches is { Count: > 0 }) items.Add("çentik işaretleri");
        if (ruleTravels && Doc.GradingRules.Any(r => r.SourcePieceId == piece.Id)) items.Add("serileme kuralı");
        if (items.Count == 0) return null;
        var joined = items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + " ve " + items[^1];
        return $"{joined[0].ToString().ToUpper(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))}{joined[1..]} parçayla birlikte {verb}.";
    }

    private List<string> CaveatsFor(PatternPiece piece, string verb, PieceOperationResult result)
    {
        var list = new List<string>();
        var ruleTravels = !result.Warnings.Any(w => w.StartsWith("Kayıtlı serileme kuralı", StringComparison.Ordinal));
        if (TravelsWith(piece, verb, ruleTravels) is { } travels) list.Add(travels);
        foreach (var warning in result.Warnings)
            if (!string.IsNullOrWhiteSpace(warning) && !list.Contains(warning)) list.Add(warning);
        return list;
    }
}
