using System.Text;
using TekstilCAD.Core;
using TekstilCAD.Core.Commands;

namespace TekstilCAD.Tests;

/// <summary>
/// Hostile input for <see cref="CommandPlanner"/>: broken documents, odd selections, locked and hidden layers, huge coordinates,
/// curves, many points and every command record the parser can produce. The planner must never throw, never change its input,
/// give the same answer twice, and answer with either a plan (one-line preview) or a refusal (non-empty message, code "R..").
/// The helpers marked internal are reused by <see cref="PlannerVectorTests"/>.
/// </summary>
public sealed class PlannerHostileTests
{
    internal static readonly Guid Epoch = new("5a1d2c3b-4e5f-4a6b-8c7d-9e0f1a2b3c4d");

    /// <summary>A command type the planner has never heard of.</summary>
    private sealed record MysteryCommand : Command;

    // ================================================================================================================
    // Shared helpers
    // ================================================================================================================

    /// <summary>One representative of every command record, every enum value and many hostile numbers and names (null entry included).</summary>
    internal static readonly IReadOnlyList<Command?> AllCommands = BuildCommands();

    private static List<Command?> BuildCommands()
    {
        var list = new List<Command?>();

        foreach (var (w, h) in new (double, double)[]
                 { (120, 80), (1, 1), (0.001, 0.001), (20000, 20000), (20000.5, 10), (0, 80), (-5, 80), (double.NaN, 80), (80, double.PositiveInfinity), (1e8, 1e8), (double.MaxValue, 1), (double.Epsilon, 5) })
            list.Add(new RectangleCommand(w, h));
        list.Add(new TrapezoidCommand(600, 400, 500));
        list.Add(new TrapezoidCommand(double.NaN, 400, 500));
        list.Add(new TrapezoidCommand(400, 600, 0));
        list.Add(new RectangleCommand(100, 100, true));
        list.Add(new RectangleCommand(100, double.NaN, true));
        list.Add(new RectangleCommand(0, 100, true));
        list.Add(new RectangleCommand(double.NaN, 100, true));

        foreach (var orientation in Enum.GetValues<LineOrientation>())
            foreach (var length in new[] { 150, 0, -1, double.NaN, double.PositiveInfinity, 20000, 20001, 1e8, 0.001, 0.0011 })
                list.Add(new LineCommand(length, orientation));
        list.Add(new LineCommand(100, (LineOrientation)7));

        foreach (var (right, down) in new (double, double)[]
                 { (25, 0), (0, -10), (0, 0), (20, -10), (-0.0, 0.0), (double.NaN, 0), (0, double.NaN), (double.PositiveInfinity, 0), (0, double.NegativeInfinity), (1e300, 0), (1e8, 1e8), (-1e8, -1e8), (1e-300, 0), (double.MaxValue, double.MinValue) })
            list.Add(new MoveByCommand(right, down));

        foreach (var (x, y) in new (double, double)[]
                 { (100, 50), (0, 0), (double.NaN, 0), (0, double.PositiveInfinity), (1e9, 0), (1e8, 1e8), (-1e8, -1e8), (-5, -5), (1e-300, 1e-300) })
            list.Add(new MoveToCommand(x, y));

        foreach (var direction in Enum.GetValues<RotationDirection>())
            foreach (var degrees in new[] { 90, 180, 270, 0, 45, -90, 360, 450, int.MaxValue, int.MinValue })
                list.Add(new RotateCommand(degrees, direction));
        list.Add(new RotateCommand(90, (RotationDirection)7));

        foreach (var axis in Enum.GetValues<MirrorAxis>()) list.Add(new MirrorCommand(axis));
        list.Add(new MirrorCommand((MirrorAxis)9));

        foreach (var (w, h) in new (double?, double?)[]
                 { (95, null), (null, 60), (95, 60), (null, null), (0, 10), (10, 0), (-1, null), (null, -1), (double.NaN, 5), (5, double.NaN), (1e9, 1e9), (20000, 20000), (0.001, null), (double.PositiveInfinity, null) })
            list.Add(new ResizeCommand(w, h));

        foreach (var name in new string?[]
                 { "Ön Beden", "", "   ", "\t", new string('x', 500), new string('x', 128), new string('x', 129), "a\nb\tc\0", "\uD800abc", "abc\uDC00", "😀 beden", null, "Ön beden", "Kol", "  padded  ", new string('ş', 10000) })
        {
            list.Add(new RenameCommand(name!));
            list.Add(new RenameProjectCommand(name!));
        }

        var targets = new[] { PieceTarget.Selected, PieceTarget.Next, PieceTarget.Previous, PieceTarget.None, (PieceTarget)99 };
        foreach (var target in targets)
        {
            list.Add(new DeleteCommand(target));
            list.Add(new SelectCommand(target));
        }
        foreach (var name in new string?[] { "kol", "ön beden", "Ön Beden", "", "  ", null, "zzz", "\0", "\uD800", new string('a', 10000), "😀", "ARKA", "beden" })
        {
            list.Add(new DeleteCommand(PieceTarget.Named, name));
            list.Add(new SelectCommand(PieceTarget.Named, name));
        }

        foreach (var steps in new[] { 1, 3, 10, 0, -4, 11, int.MaxValue, int.MinValue }) list.Add(new UndoCommand(steps));
        list.Add(new RedoCommand());

        foreach (var action in Enum.GetValues<ViewAction>().Append((ViewAction)999).Append((ViewAction)(-1))) list.Add(new ViewCommand(action));
        foreach (var tool in Enum.GetValues<DrawingTool>().Append((DrawingTool)99)) list.Add(new ToolCommand(tool));
        foreach (var action in Enum.GetValues<FinishDrawingAction>().Append((FinishDrawingAction)99)) list.Add(new FinishDrawingCommand(action));
        foreach (var action in Enum.GetValues<FileAction>().Append((FileAction)99)) list.Add(new FileCommand(action));

        foreach (var target in Enum.GetValues<HandoffTarget>().Append((HandoffTarget)999))
            foreach (var tab in new int?[] { null, 1, 2, 3, 99 })
                foreach (var value in new double?[] { null, 10, double.NaN, -1, 1e12 })
                    list.Add(new HandoffCommand(target, tab, value));

        list.Add(new HelpCommand());
        list.Add(new CancelPreviewCommand());
        list.Add(new MysteryCommand());
        list.Add(null);
        return list;
    }

    internal static PatternPiece Rect(string name, double x, double y, double width, double height) =>
        PieceOperationTestData.Rect(name, x, y, width, height);

    /// <summary>Three pieces (context B1 of the specification); "Ön beden" 480 x 620 first.</summary>
    internal static ProjectDocument Standard() => PieceOperationTestData.Doc(
        Rect("Ön beden", 20, 20, 480, 620),
        Rect("Arka beden", 520, 20, 480, 620),
        Rect("Kol", 1020, 20, 200, 300));

    private static PatternPiece Curved(string name)
    {
        var piece = Rect(name, 20, 20, 480, 620);
        piece.Curves =
        [
            new CurveSegment.Line(new Point2(20, 20), new Point2(500, 20)),
            new CurveSegment.Line(new Point2(500, 20), new Point2(500, 640)),
            new CurveSegment.Line(new Point2(500, 640), new Point2(20, 640)),
            new CurveSegment.Line(new Point2(20, 640), new Point2(20, 20)),
        ];
        return piece;
    }

    private static List<Point2> Circle(int count, double radius = 500)
    {
        var points = new List<Point2>(count);
        for (var i = 0; i < count; i++)
        {
            var angle = 2 * Math.PI * i / count;
            points.Add(new Point2(1000 + radius * Math.Cos(angle), 1000 + radius * Math.Sin(angle)));
        }
        return points;
    }

    private static DocumentView ViewOf(ProjectDocument document) =>
        new(document, Epoch) { UndoCount = 3, RedoCount = 1, LastUndoLabel = "25 mm sağa taşıma", FileName = "gomlek.tcad" };

    internal static string Describe(Command? command)
    {
        if (command is null) return "null";
        var text = command.ToString() ?? command.GetType().Name;
        return text.Length <= 160 ? text : text.Substring(0, 160) + "...";
    }

    /// <summary>Plans and turns any exception into a failure that names the command.</summary>
    internal static PlanResult PlanSafely(Command? command, DocumentView view, Selection? selection, bool unitAssumed, string context)
    {
        try
        {
            return CommandPlanner.Plan(command!, view, selection, unitAssumed);
        }
        catch (Exception ex)
        {
            throw new Xunit.Sdk.XunitException($"{context}: Plan threw {ex.GetType().Name} for {Describe(command)}: {ex.Message}");
        }
    }

    private static PlanResult PlanOf(Command command, ProjectDocument document, Selection? selection, DocumentView? view = null, bool unitAssumed = false) =>
        PlanSafely(command, view ?? ViewOf(document), selection, unitAssumed, "test");

    private static Plan AssertPlan(PlanResult result)
    {
        Assert.True(result.IsPlan, result.ToString());
        return result.Plan!;
    }

    private static void AssertRefusedWith(string code, PlanResult result)
    {
        Assert.False(result.IsPlan, $"expected {code} but got the plan: {result.Plan?.Preview}");
        Assert.Equal(code, result.Code);
    }

    private static bool HasBrace(string? text) => text is not null && (text.Contains('{') || text.Contains('}'));

    private static void AssertNoForbiddenWords(string text, string? userText, string context)
    {
        var haystack = TurkishText.Fold(text);
        var echoed = TurkishText.Fold(userText);
        foreach (var word in new[] { "yapay zeka", "asistan", "model" })
        {
            if (haystack.Contains(word, StringComparison.Ordinal) && !echoed.Contains(word, StringComparison.Ordinal))
                Assert.Fail($"{context}: the text contains the word '{word}': {text}");
        }
    }

    /// <summary>
    /// The contract every answer must meet: exactly one of plan and refusal; a plan has a non-empty one-line preview without
    /// placeholders and a stamp that is still current; a refusal has a code starting with "R", a non-empty message and no placeholders.
    /// <paramref name="userText"/> is what the user typed (words and braces that were echoed back are allowed).
    /// </summary>
    internal static void AssertSound(PlanResult? result, DocumentView? view, Selection? selection, string? userText, string context, bool requireFiniteFrame = true)
    {
        Assert.True(result is not null, $"{context}: the planner returned null");
        var hasPlan = result!.Plan is not null;
        var hasRefusal = result.Refusal is not null;
        Assert.True(hasPlan != hasRefusal, $"{context}: a result must carry exactly one of plan and refusal");
        Assert.Equal(hasPlan, result.IsPlan);
        var bracesAllowed = userText is not null && HasBrace(userText);

        if (hasRefusal)
        {
            var refusal = result.Refusal!;
            Assert.True(refusal.Code is { Length: > 1 } && refusal.Code[0] == 'R', $"{context}: the refusal code '{refusal.Code}' must start with R");
            Assert.Equal(refusal.Code, result.Code);
            Assert.True(!string.IsNullOrWhiteSpace(refusal.Message), $"{context}: refusal {refusal.Code} has an empty message");
            if (!bracesAllowed) Assert.True(!HasBrace(refusal.Message), $"{context}: refusal {refusal.Code} keeps a placeholder: {refusal.Message}");
            AssertNoForbiddenWords(refusal.Message, userText, context);
            Assert.NotNull(refusal.Buttons);
            foreach (var button in refusal.Buttons)
            {
                Assert.NotNull(button);
                Assert.NotNull(button.Label);
                if (!bracesAllowed) Assert.True(!HasBrace(button.Label) && !HasBrace(button.FillText), $"{context}: a button of {refusal.Code} keeps a placeholder");
            }
            return;
        }

        var plan = result.Plan!;
        Assert.NotNull(plan.Command);
        Assert.True(!string.IsNullOrWhiteSpace(plan.Preview), $"{context}: the preview is empty");
        Assert.True(!plan.Preview.Contains('\n') && !plan.Preview.Contains('\r'), $"{context}: the preview is not a single line: {plan.Preview}");
        if (!bracesAllowed) Assert.True(!HasBrace(plan.Preview), $"{context}: the preview keeps a placeholder: {plan.Preview}");
        AssertNoForbiddenWords(plan.Preview, userText, context);
        Assert.NotNull(plan.Caveats);
        foreach (var caveat in plan.Caveats) Assert.True(!string.IsNullOrWhiteSpace(caveat), $"{context}: a caveat is empty");
        Assert.NotNull(plan.Alternatives);
        foreach (var alternative in plan.Alternatives) Assert.True(!string.IsNullOrWhiteSpace(alternative.Label), $"{context}: an alternative has no label");
        Assert.NotNull(plan.ResultText);

        Assert.NotNull(plan.Ghost);
        foreach (var polygon in plan.Ghost.Polygons)
        {
            Assert.NotNull(polygon);
            foreach (var point in polygon.Points) Assert.True(!requireFiniteFrame || point.IsFinite, $"{context}: the frame has a non-finite point");
        }
        foreach (var label in plan.Ghost.Labels)
        {
            Assert.True(label.Anchor.IsFinite, $"{context}: a frame label has a non-finite anchor");
            Assert.True(!string.IsNullOrWhiteSpace(label.Text), $"{context}: a frame label is empty");
        }
        if (plan.Kind == PlanKind.DeletePiece && !plan.Ghost.IsEmpty) Assert.Equal(GhostStyle.Danger, plan.Ghost.Style);

        Assert.NotNull(plan.Stamp);
        if (view is not null) Assert.True(plan.Stamp.IsCurrent(view, selection), $"{context}: a fresh stamp is not current");

        var core = plan.Kind is PlanKind.AddTrapezoid or PlanKind.AddRectangle or PlanKind.AddLine or PlanKind.ResizePiece or PlanKind.RenamePiece or PlanKind.RenameProject or PlanKind.DeletePiece or PlanKind.DuplicatePiece;
        Assert.True(core == plan.AppliesInCore, $"{context}: {plan.Kind} AppliesInCore={plan.AppliesInCore}");
        var operation = plan.Kind is PlanKind.MovePiece or PlanKind.MovePieceTo or PlanKind.RotatePiece or PlanKind.MirrorPiece;
        Assert.True(operation == (plan.Operation is not null), $"{context}: {plan.Kind} has Operation={plan.Operation}");
        if (plan.Operation is not null) Assert.Equal(plan.TargetPieceId, (Guid?)plan.Operation.PieceId);
        Assert.True((plan.CreatedPieceId is not null) == plan.AddsPiece, $"{context}: CreatedPieceId and AddsPiece disagree");
        if (plan.NeedsSelectedPiece) Assert.True(plan.TargetPieceId is not null, $"{context}: NeedsSelectedPiece without a target piece");
        if (plan.AppliesInCore)
        {
            var edited = plan.EditedDocument();
            Assert.NotNull(edited);
            if (plan.CreatedPieceId is { } created) Assert.Contains(edited!.Pieces, p => p.Id == created);
        }
    }

    private static string GhostSignature(GhostScene ghost)
    {
        var sb = new StringBuilder();
        sb.Append(ghost.Style).Append('|');
        foreach (var polygon in ghost.Polygons)
        {
            sb.Append(polygon.Closed ? 'c' : 'o').Append(':');
            foreach (var p in polygon.Points)
                sb.Append(BitConverter.DoubleToInt64Bits(p.X).ToString("x")).Append(',').Append(BitConverter.DoubleToInt64Bits(p.Y).ToString("x")).Append(' ');
            sb.Append(';');
        }
        sb.Append('|');
        foreach (var label in ghost.Labels)
            sb.Append(label.Text).Append('@').Append(BitConverter.DoubleToInt64Bits(label.Anchor.X).ToString("x")).Append(',')
              .Append(BitConverter.DoubleToInt64Bits(label.Anchor.Y).ToString("x")).Append(label.Placement).Append(';');
        return sb.ToString();
    }

    /// <summary>Planning twice must give equal text, frame, digest, caveats and refusal (including the id of the piece a plan would create).</summary>
    internal static void AssertSameOutcome(PlanResult first, PlanResult second, string context)
    {
        Assert.Equal(first.IsPlan, second.IsPlan);
        if (!first.IsPlan)
        {
            Assert.Equal(first.Refusal!.Code, second.Refusal!.Code);
            Assert.Equal(first.Refusal.Message, second.Refusal.Message);
            Assert.Equal(first.Refusal.Buttons.Select(b => b.Label + "|" + b.FillText), second.Refusal.Buttons.Select(b => b.Label + "|" + b.FillText));
            return;
        }
        var p = first.Plan!;
        var q = second.Plan!;
        Assert.True(p.Kind == q.Kind && p.Class == q.Class, $"{context}: kind or class differ between two runs");
        Assert.True(p.Preview == q.Preview, $"{context}: the preview differs between two runs: '{p.Preview}' / '{q.Preview}'");
        Assert.Equal(p.ResultText, q.ResultText);
        Assert.Equal(p.ActionName, q.ActionName);
        Assert.Equal(p.TargetPieceId, q.TargetPieceId);
        Assert.Equal(p.CreatedPieceId, q.CreatedPieceId);
        Assert.Equal(p.Stamp.ContentDigest, q.Stamp.ContentDigest);
        Assert.Equal(p.Stamp.Scope, q.Stamp.Scope);
        Assert.Equal(p.Stamp.PieceId, q.Stamp.PieceId);
        Assert.Equal(p.Caveats, q.Caveats);
        Assert.Equal(p.Alternatives.Select(a => a.Label + "|" + a.FillText), q.Alternatives.Select(a => a.Label + "|" + a.FillText));
        Assert.True(GhostSignature(p.Ghost) == GhostSignature(q.Ghost), $"{context}: the frame differs between two runs");
        Assert.Equal(p.Operation, q.Operation);
    }

    private static ProjectDocument? TryClone(ProjectDocument document)
    {
        try { return document.Clone(); }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Plans every command (with and without "no unit written") against the document and checks: no exception, a sound answer,
    /// the same answer twice, and the document and the selection untouched after each command.
    /// </summary>
    internal static void Sweep(ProjectDocument document, Selection? selection, string context, DocumentView? view = null, bool requireFiniteFrame = true)
    {
        view ??= ViewOf(document);
        var snapshot = TryClone(document);
        var selectedBefore = selection?.PieceIds?.ToArray();
        foreach (var command in AllCommands)
        {
            foreach (var unitAssumed in new[] { false, true })
            {
                var where = $"{context} / {Describe(command)} / unitAssumed={unitAssumed}";
                var first = PlanSafely(command, view, selection, unitAssumed, context);
                AssertSound(first, view, selection, null, where, requireFiniteFrame);
                var second = PlanSafely(command, view, selection, unitAssumed, context);
                AssertSameOutcome(first, second, where);
            }
            if (snapshot is not null)
                Assert.True(DocumentSession.ContentEquals(snapshot, document), $"{context}: planning {Describe(command)} changed the document");
            if (selectedBefore is not null)
                Assert.True(selectedBefore.SequenceEqual(selection!.PieceIds.ToArray()), $"{context}: planning {Describe(command)} changed the selection");
        }
    }

    // ================================================================================================================
    // The command list itself
    // ================================================================================================================

    [Fact]
    public void EveryCommandRecordOfTheParserHasARepresentativeInTheSweep()
    {
        var known = AllCommands.Where(c => c is not null).Select(c => c!.GetType()).ToHashSet();
        var missing = typeof(Command).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(Command).IsAssignableFrom(t) && !known.Contains(t))
            .Select(t => t.Name).ToList();
        Assert.True(missing.Count == 0, "Add a representative to PlannerHostileTests.AllCommands for: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryKnownCommandRecordIsHandledByThePlannerAndNeverFallsThrough()
    {
        var doc = Standard();
        var selection = Selection.Of(doc.Pieces[0].Id);
        foreach (var command in AllCommands.Where(c => c is not null && c is not MysteryCommand))
        {
            var result = PlanOf(command!, doc, selection);
            if (result.Refusal is { } refusal)
                Assert.False(refusal.Message.Contains("Bu komut tanınmıyor", StringComparison.Ordinal), $"{Describe(command)} fell through the planner's switch");
        }
    }

    [Fact]
    public void TheSweepCoversEveryValueOfEveryCommandEnum()
    {
        var views = AllCommands.OfType<ViewCommand>().Select(c => c.Action).ToHashSet();
        foreach (var action in Enum.GetValues<ViewAction>()) Assert.Contains(action, views);
        var targets = AllCommands.OfType<HandoffCommand>().Select(c => c.Target).ToHashSet();
        foreach (var target in Enum.GetValues<HandoffTarget>()) Assert.Contains(target, targets);
        Assert.True(AllCommands.Count > 400, "the sweep should cover hundreds of commands");
    }

    // ================================================================================================================
    // Never throws, never changes the input, deterministic: hostile documents
    // ================================================================================================================

    [Fact]
    public void PlanningNeverChangesTheInputDocumentForAnyCommand()
    {
        var doc = Standard();
        doc.GradingRules.Add(new GradingRuleSet { SourcePieceId = doc.Pieces[0].Id, SourceClosed = true, SourcePoints = [.. doc.Pieces[0].Points], Deltas = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)] });
        Sweep(doc, Selection.Of(doc.Pieces[0].Id), "standard document, first piece selected");
        Sweep(doc, Selection.Of(doc.Pieces[2].Id), "standard document, last piece selected");
        Sweep(doc, Selection.None, "standard document, nothing selected");
    }

    [Fact]
    public void PlanningTwiceGivesEqualPreviewFramePolygonsAndStampDigest()
    {
        var doc = Standard();
        var view = ViewOf(doc);
        var selection = Selection.Of(doc.Pieces[1].Id);
        foreach (var command in AllCommands)
        {
            var first = PlanSafely(command, view, selection, true, "determinism");
            var second = PlanSafely(command, view, selection, true, "determinism");
            AssertSameOutcome(first, second, Describe(command));
        }
    }

    [Fact]
    public void EmptyDocumentNeverMakesThePlannerThrow()
    {
        Sweep(new ProjectDocument(), null, "empty document, null selection");
        Sweep(new ProjectDocument(), Selection.None, "empty document, empty selection");
        Sweep(new ProjectDocument(), Selection.Of(Guid.NewGuid()), "empty document, selection of an unknown piece");
    }

    [Fact]
    public void PieceWithNanCoordinateNeverMakesThePlannerThrow()
    {
        var bad = new PatternPiece { Name = "Bozuk", Points = [new(0, 0), new(double.NaN, 0), new(100, 100), new(0, 100)] };
        var doc = PieceOperationTestData.Doc(bad, Rect("Sağlam", 200, 0, 100, 50));
        Sweep(doc, Selection.Of(bad.Id), "NaN piece selected", null, false);
        Sweep(doc, Selection.None, "NaN piece beside a good one, nothing selected", null, false);
        Sweep(doc, Selection.Of(doc.Pieces[1].Id), "good piece selected beside a NaN piece", null, false);
    }

    [Fact]
    public void PieceWithInfiniteCoordinatesNeverMakesThePlannerThrow()
    {
        var bad = new PatternPiece { Name = "Sonsuz", Points = [new(0, 0), new(double.PositiveInfinity, 0), new(100, double.NegativeInfinity), new(0, 100)] };
        var doc = PieceOperationTestData.Doc(bad, Rect("Sağlam", 200, 0, 100, 50));
        Sweep(doc, Selection.Of(bad.Id), "infinite piece selected", null, false);
        Sweep(doc, Selection.Of(doc.Pieces[1].Id), "good piece selected beside an infinite piece", null, false);
    }

    [Fact]
    public void PieceWithCoordinatesBeyondTheWorkingAreaNeverMakesThePlannerThrow()
    {
        var bad = new PatternPiece { Name = "Taşkın", Points = [new(0, 0), new(1e9, 0), new(-1e9, 5), new(1e300, 1e300)] };
        var doc = PieceOperationTestData.Doc(bad, Rect("Sağlam", 200, 0, 100, 50));
        Sweep(doc, Selection.Of(bad.Id), "out-of-range piece selected");
        Sweep(doc, Selection.Of(doc.Pieces[1].Id), "good piece selected beside an out-of-range piece");
    }

    [Fact]
    public void PieceWithoutPointsNeverMakesThePlannerThrow()
    {
        var empty = new PatternPiece { Name = "Boş", Points = [] };
        var doc = PieceOperationTestData.Doc(empty, Rect("Sağlam", 200, 0, 100, 50));
        Sweep(doc, Selection.Of(empty.Id), "piece without points selected");
        Sweep(doc, Selection.None, "piece without points, nothing selected");
    }

    [Fact]
    public void PieceWithNullPointsListNeverMakesThePlannerThrow()
    {
        var broken = new PatternPiece { Name = "Noktasız", Points = null! };
        var doc = PieceOperationTestData.Doc(broken, Rect("Sağlam", 200, 0, 100, 50));
        // The document cannot even be cloned; Sweep skips the "unchanged" check but still checks every answer.
        Sweep(doc, Selection.Of(broken.Id), "piece with a null point list selected");
        Sweep(doc, Selection.Of(doc.Pieces[1].Id), "good piece selected beside a null point list");
    }

    [Fact]
    public void PieceWithNullNameNeverMakesThePlannerThrow()
    {
        var nameless = new PatternPiece { Name = null!, Points = [new(0, 0), new(100, 0), new(100, 50), new(0, 50)] };
        var doc = PieceOperationTestData.Doc(nameless, Rect("Kol", 200, 0, 100, 50));
        Sweep(doc, Selection.Of(nameless.Id), "nameless piece selected");
        Sweep(doc, Selection.None, "nameless piece, nothing selected");
    }

    [Fact]
    public void PiecesWithEmptyAndWhitespaceNamesNeverMakeThePlannerThrow()
    {
        var doc = PieceOperationTestData.Doc(Rect("", 0, 0, 100, 50), Rect("   ", 200, 0, 100, 50), Rect("\t\n", 400, 0, 100, 50));
        Sweep(doc, Selection.Of(doc.Pieces[0].Id), "empty name selected");
        Sweep(doc, Selection.None, "whitespace names, nothing selected");
    }

    [Fact]
    public void PiecesWithZeroAreaNeverMakeThePlannerThrow()
    {
        var dot = new PatternPiece { Name = "Nokta", Points = [new(5, 5), new(5, 5), new(5, 5), new(5, 5)] };
        var flat = new PatternPiece { Name = "Yassı", Points = [new(0, 0), new(100, 0), new(200, 0), new(300, 0)] };
        var thin = Rect("İnce", 0, 100, 100, 0);
        var doc = PieceOperationTestData.Doc(dot, flat, thin);
        Sweep(doc, Selection.Of(dot.Id), "single repeated point selected");
        Sweep(doc, Selection.Of(flat.Id), "collinear piece selected");
        Sweep(doc, Selection.Of(thin.Id), "zero-height rectangle selected");
    }

    [Fact]
    public void PieceWithASinglePointOrATwoPointClosedContourNeverMakesThePlannerThrow()
    {
        var single = new PatternPiece { Name = "Tek", Points = [new(5, 5)] };
        var twoClosed = new PatternPiece { Name = "İki", Closed = true, Points = [new(0, 0), new(10, 0)] };
        var openLine = PieceOperationTestData.Segment("Çizgi", 50, 50, 100);
        var doc = PieceOperationTestData.Doc(single, twoClosed, openLine);
        Sweep(doc, Selection.Of(single.Id), "one-point piece selected");
        Sweep(doc, Selection.Of(twoClosed.Id), "two-point closed piece selected");
        Sweep(doc, Selection.Of(openLine.Id), "open line selected");
    }

    [Fact]
    public void DuplicatePieceIdsNeverMakeThePlannerThrow()
    {
        var shared = Guid.NewGuid();
        var a = Rect("A", 0, 0, 100, 50);
        var b = Rect("B", 200, 0, 100, 50);
        var c = Rect("C", 400, 0, 100, 50);
        var d = Rect("D", 600, 0, 100, 50);
        a.Id = shared;
        b.Id = shared;
        c.Id = Guid.Empty;
        d.Id = Guid.Empty;
        var doc = PieceOperationTestData.Doc(a, b, c, d);
        Sweep(doc, Selection.Of(shared), "two pieces share the selected id");
        Sweep(doc, Selection.Of(Guid.Empty), "two pieces have the empty id and it is selected");
    }

    [Fact]
    public void DuplicatePieceNamesNeverMakeThePlannerThrow()
    {
        var doc = PieceOperationTestData.Doc(Rect("Kol", 0, 0, 100, 50), Rect("Kol", 200, 0, 100, 50), Rect("Kol", 400, 0, 100, 50), Rect("kol", 600, 0, 100, 50));
        Sweep(doc, Selection.Of(doc.Pieces[1].Id), "four pieces named Kol");
    }

    [Fact]
    public void SelectionOfAnIdThatIsNotInTheDocumentNeverMakesThePlannerThrow()
    {
        var doc = Standard();
        Sweep(doc, Selection.Of(Guid.NewGuid()), "unknown id");
        Sweep(doc, Selection.Of(Guid.Empty), "empty id");
    }

    [Fact]
    public void EmptySelectionListNeverMakesThePlannerThrow() =>
        Sweep(Standard(), new Selection(new List<Guid>()), "empty selection list");

    [Fact]
    public void NullSelectionNeverMakesThePlannerThrow() =>
        Sweep(Standard(), null, "null selection");

    [Fact]
    public void SelectionWithANullIdListNeverMakesThePlannerThrow() =>
        Sweep(Standard(), new Selection(null!), "selection whose id list is null");

    [Fact]
    public void SelectionOfTwoPiecesNeverMakesThePlannerThrow()
    {
        var doc = Standard();
        Sweep(doc, new Selection(new[] { doc.Pieces[0].Id, doc.Pieces[1].Id }), "two selected pieces");
        Sweep(doc, new Selection(new[] { doc.Pieces[0].Id, Guid.NewGuid() }), "one selected piece and one unknown id");
        Sweep(doc, new Selection(new[] { doc.Pieces[0].Id, doc.Pieces[1].Id, doc.Pieces[2].Id }), "all pieces selected");
    }

    [Fact]
    public void LockedLayerNeverMakesThePlannerThrow()
    {
        var locked = new Layer { Name = "Kilitli", Locked = true };
        var doc = PieceOperationTestData.WithLayers(Standard(), locked);
        doc.Pieces[0].LayerId = locked.Id;
        Sweep(doc, Selection.Of(doc.Pieces[0].Id), "locked piece selected");
        Sweep(doc, Selection.Of(doc.Pieces[1].Id), "unlocked piece selected beside a locked one");
        Sweep(doc, Selection.Of(doc.Pieces[0].Id), "new pieces go to the locked layer",
            new DocumentView(doc, Epoch) { UndoCount = 1, NewPieceLayerId = locked.Id });
    }

    [Fact]
    public void HiddenDefaultLayerNeverMakesThePlannerThrow()
    {
        var doc = Standard();
        doc.Layers = [new Layer { Id = Layer.DefaultId, Name = "Katman 1", Visible = false }];
        Sweep(doc, Selection.Of(doc.Pieces[0].Id), "default layer hidden");
    }

    [Fact]
    public void LayerListWithoutTheDefaultLayerNeverMakesThePlannerThrow()
    {
        var doc = Standard();
        doc.Layers = [new Layer { Name = "Yalnız" }];
        Sweep(doc, Selection.Of(doc.Pieces[0].Id), "no default layer in the list");
        doc.Layers = [];
        Sweep(doc, Selection.Of(doc.Pieces[0].Id), "empty layer list");
    }

    [Fact]
    public void HugeCoordinatesNeverMakeThePlannerThrow()
    {
        var far = PieceOperationTestData.Doc(Rect("Uzak", 1e8 - 100, 1e8 - 100, 100, 100), Rect("Yakın", 0, 0, 50, 50));
        Sweep(far, Selection.Of(far.Pieces[0].Id), "piece touching the working-area limit");
        Sweep(far, Selection.Of(far.Pieces[1].Id), "small piece beside one at the limit");

        var negative = PieceOperationTestData.Doc(Rect("Eksi", -1e8, -1e8, 100, 100));
        Sweep(negative, Selection.Of(negative.Pieces[0].Id), "piece at the negative limit");

        var beyond = PieceOperationTestData.Doc(Rect("Dışarıda", 1e8 + 1, 0, 100, 100));
        Sweep(beyond, Selection.Of(beyond.Pieces[0].Id), "piece just beyond the limit");

        var tiny = PieceOperationTestData.Doc(Rect("Minik", 1e7, 1e7, 0.002, 0.002));
        Sweep(tiny, Selection.Of(tiny.Pieces[0].Id), "tiny piece far from the origin");
    }

    [Fact]
    public void PieceWithCurvesNeverMakesThePlannerThrow()
    {
        var doc = PieceOperationTestData.Doc(Curved("Eğri"), Rect("Düz", 600, 20, 100, 100));
        Sweep(doc, Selection.Of(doc.Pieces[0].Id), "curved piece selected");
        Sweep(doc, Selection.None, "curved piece, nothing selected");
    }

    [Fact]
    public void PieceWithCurvesAndNotchesNeverMakesThePlannerThrow()
    {
        var piece = Curved("Eğri çentikli");
        piece.Notches = [new NotchMark { VertexIndex = 1, Position = new Point2(500, 20) }];
        var doc = PieceOperationTestData.Doc(piece, Rect("Düz", 600, 20, 100, 100));
        Sweep(doc, Selection.Of(piece.Id), "curved piece with notches selected");
    }

    [Fact]
    public void PieceWithGrainlineNotchesAndGradingRuleNeverMakesThePlannerThrow()
    {
        var piece = Rect("Çentikli", 20, 20, 480, 620);
        piece.Grainline = new Grainline { Start = new Point2(60, 60), End = new Point2(60, 500) };
        piece.Notches = [new NotchMark { VertexIndex = 0, Position = piece.Points[0] }, new NotchMark { VertexIndex = 2, Position = piece.Points[2] }];
        piece.Metadata = new PieceMetadata { Role = "ön", Quantity = 2 };
        var doc = PieceOperationTestData.Doc(piece, Rect("Düz", 600, 20, 100, 100));
        doc.GradingRules.Add(new GradingRuleSet { SourcePieceId = piece.Id, SourceClosed = true, SourcePoints = [.. piece.Points], Deltas = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)] });
        Sweep(doc, Selection.Of(piece.Id), "piece with grainline, notches and a grading rule");
    }

    [Fact]
    public void BrokenNotchesAndOrphanGradingRulesNeverMakeThePlannerThrow()
    {
        var badNotch = Rect("Kötü çentik", 20, 20, 100, 100);
        badNotch.Notches = [new NotchMark { VertexIndex = 99, Position = new Point2(1, 1) }, new NotchMark { VertexIndex = -3, Position = new Point2(double.NaN, 0) }];
        var badGrain = Rect("Kötü iplik", 300, 20, 100, 100);
        badGrain.Grainline = new Grainline { Start = new Point2(double.NaN, 0), End = new Point2(1e12, 1e12) };
        var doc = PieceOperationTestData.Doc(badNotch, badGrain);
        doc.GradingRules.Add(new GradingRuleSet { SourcePieceId = Guid.NewGuid(), SourceClosed = true, SourcePoints = [new(0, 0)], Deltas = [] });
        doc.GradingRules.Add(new GradingRuleSet { SourcePieceId = badNotch.Id, SourceClosed = false, SourcePoints = [], Deltas = [new(0, 0), new(1, 1)] });
        Sweep(doc, Selection.Of(badNotch.Id), "piece with out-of-range notch indexes");
        Sweep(doc, Selection.Of(badGrain.Id), "piece with a broken grainline");
    }

    [Fact]
    public void ManyPiecesWithManyPointsNeverMakeTheCommandsThatLookAtTheWholeDocumentThrow()
    {
        var pieces = Enumerable.Range(0, 60).Select(i => Rect("Parça " + (i + 1), (i % 10) * 120, (i / 10) * 120, 100, 100)).ToArray();
        var doc = PieceOperationTestData.Doc(pieces);
        Sweep(doc, Selection.Of(doc.Pieces[30].Id), "sixty pieces, one selected");
    }

    // ================================================================================================================
    // Never throws: window state and view values
    // ================================================================================================================

    [Fact]
    public void EveryCombinationOfWindowFlagsNeverMakesThePlannerThrow()
    {
        for (var mask = 0; mask < 32; mask++)
        {
            var doc = Standard();
            var view = new DocumentView(doc, Epoch)
            {
                DrawingInProgress = (mask & 1) != 0,
                DrawingPointCount = (mask & 1) != 0 ? 3 : 0,
                HasUnappliedDrafts = (mask & 2) != 0,
                RecoveryIncomplete = (mask & 4) != 0,
                FileOperationInProgress = (mask & 8) != 0,
                HasUnsavedChanges = (mask & 16) != 0,
                UndoCount = 2,
                RedoCount = 2,
                FileName = (mask & 16) != 0 ? "gomlek.tcad" : null,
            };
            Sweep(doc, Selection.Of(doc.Pieces[0].Id), $"window flags {mask}", view);
        }
    }

    [Fact]
    public void ExtremeViewCountsNeverMakeThePlannerThrow()
    {
        var doc = Standard();
        var selection = Selection.Of(doc.Pieces[0].Id);
        var views = new[]
        {
            new DocumentView(doc, Guid.Empty) { UndoCount = -5, RedoCount = -1, DrawingPointCount = -3, DrawingInProgress = true },
            new DocumentView(doc, Epoch) { UndoCount = int.MaxValue, RedoCount = int.MaxValue, DrawingPointCount = int.MaxValue, DrawingInProgress = true },
            new DocumentView(doc, Epoch) { UndoCount = int.MinValue, RedoCount = int.MinValue },
            new DocumentView(doc, Epoch) { UndoCount = 5, LastUndoLabel = "", FileName = "" },
            new DocumentView(doc, Epoch) { UndoCount = 5, LastUndoLabel = "   ", FileName = "   ", HasUnsavedChanges = true },
            new DocumentView(doc, Epoch) { UndoCount = 5, LastUndoLabel = new string('u', 5000), FileName = new string('f', 5000), HasUnsavedChanges = true },
            new DocumentView(doc, Epoch) { NewPieceLayerId = Guid.NewGuid() },
            new DocumentView(doc, Epoch) { NewPieceLayerId = Guid.Empty },
        };
        for (var i = 0; i < views.Length; i++) Sweep(doc, selection, $"extreme view {i}", views[i]);
    }

    [Fact]
    public void NullViewIsRefusedWithR28aByEveryEntryPoint()
    {
        AssertRefusedWith("R28a", CommandPlanner.Plan(new HelpCommand(), null!, Selection.None));
        AssertRefusedWith("R28a", CommandPlanner.Plan(ParseResult.Understood(new HelpCommand()), null!, Selection.None));
        AssertRefusedWith("R28a", CommandPlanner.PlanDuplicate(null!, Selection.None));
        AssertRefusedWith("R28a", CommandPlanner.Plan(new RectangleCommand(120, 80), null!, null));
    }

    [Fact]
    public void NullCommandIsRefusedWithACodeAndAMessage()
    {
        var doc = Standard();
        var result = CommandPlanner.Plan((Command)null!, ViewOf(doc), Selection.None);
        AssertSound(result, null, null, null, "null command");
        Assert.False(result.IsPlan);
    }

    [Fact]
    public void NullParseResultIsRefusedAsNotUnderstood()
    {
        var doc = Standard();
        var result = CommandPlanner.Plan((ParseResult)null!, ViewOf(doc), Selection.None);
        AssertRefusedWith("R01", result);
        AssertSound(result, null, null, null, "null parse result");
    }

    [Fact]
    public void UnknownCommandTypeIsRefusedWithR28a()
    {
        var doc = Standard();
        AssertRefusedWith("R28a", PlanOf(new MysteryCommand(), doc, Selection.Of(doc.Pieces[0].Id)));
    }

    [Fact]
    public void ParserRefusalsAndQuestionsPassThroughTheSameObject()
    {
        var doc = Standard();
        var view = ViewOf(doc);
        var refusal = Refusals.NoPiecesYet();
        var passed = CommandPlanner.Plan(ParseResult.Refused(refusal), view, Selection.None);
        Assert.False(passed.IsPlan);
        Assert.Same(refusal, passed.Refusal);

        var question = Refusals.UnitQuestionFor("12 x 8 dikdörtgen", "12 x 8 mm dikdörtgen", "12 x 8 cm dikdörtgen");
        var asked = CommandPlanner.Plan(ParseResult.Refused(question), view, Selection.None);
        Assert.Same(question, asked.Refusal);
        Assert.Equal("R06", asked.Code);
    }

    // ================================================================================================================
    // Selection rules
    // ================================================================================================================

    [Fact]
    public void SelectionOfAnUnknownPieceCountsAsNothingSelected()
    {
        var doc = Standard();
        foreach (var command in new Command[] { new MoveByCommand(25, 0), new RotateCommand(90, RotationDirection.Clockwise), new MirrorCommand(MirrorAxis.Vertical), new ResizeCommand(95, null), new RenameCommand("Yeni"), new DeleteCommand(PieceTarget.Selected) })
            AssertRefusedWith("R03", PlanOf(command, doc, Selection.Of(Guid.NewGuid())));
    }

    [Fact]
    public void EmptyNullAndIdlessSelectionsCountAsNothingSelected()
    {
        var doc = Standard();
        var command = new MoveByCommand(25, 0);
        AssertRefusedWith("R03", PlanOf(command, doc, new Selection(new List<Guid>())));
        AssertRefusedWith("R03", PlanOf(command, doc, null));
        AssertRefusedWith("R03", PlanOf(command, doc, new Selection(null!)));
        Assert.Equal(0, new Selection(null!).Count);
    }

    [Fact]
    public void NothingSelectedInAnEmptyProjectSaysThereAreNoPiecesYet()
    {
        var doc = new ProjectDocument();
        var result = PlanOf(new MoveByCommand(25, 0), doc, Selection.None);
        AssertRefusedWith("R03", result);
        Assert.Contains("Henüz parça yok", result.Refusal!.Message);
        AssertRefusedWith("R03", PlanOf(new DeleteCommand(PieceTarget.Selected), doc, null));
        AssertRefusedWith("R03", PlanOf(new SelectCommand(PieceTarget.Next), doc, null));
    }

    [Fact]
    public void NothingSelectedOffersTheFirstThreePieceNamesAsButtons()
    {
        var doc = PieceOperationTestData.Doc(Rect("A", 0, 0, 10, 10), Rect("B", 20, 0, 10, 10), Rect("C", 40, 0, 10, 10), Rect("D", 60, 0, 10, 10));
        var result = PlanOf(new MoveByCommand(25, 0), doc, Selection.None);
        AssertRefusedWith("R03", result);
        Assert.Equal(new[] { "A", "B", "C" }, result.Refusal!.Buttons.Select(b => b.Label).ToArray());
        Assert.Equal(new[] { "A seç", "B seç", "C seç" }, result.Refusal.Buttons.Select(b => b.FillText).ToArray());
    }

    [Fact]
    public void SeveralSelectedPiecesAreRefusedWithR14EvenWhenTheyAreUnknown()
    {
        var doc = Standard();
        var two = new Selection(new[] { doc.Pieces[0].Id, doc.Pieces[1].Id });
        var unknown = new Selection(new[] { Guid.NewGuid(), Guid.NewGuid() });
        foreach (var selection in new[] { two, unknown })
            foreach (var command in new Command[] { new MoveByCommand(25, 0), new MoveToCommand(10, 10), new RotateCommand(90, RotationDirection.Clockwise), new MirrorCommand(MirrorAxis.Horizontal), new ResizeCommand(95, 60), new RenameCommand("Yeni"), new DeleteCommand(PieceTarget.Selected), new ViewCommand(ViewAction.FitSelected) })
                AssertRefusedWith("R14", PlanOf(command, doc, selection));
    }

    [Fact]
    public void TheSameIdListedTwiceCountsAsOnePiece()
    {
        var doc = Standard();
        var id = doc.Pieces[1].Id;
        var plan = AssertPlan(PlanOf(new MoveByCommand(25, 0), doc, new Selection(new[] { id, id })));
        Assert.Equal(id, plan.TargetPieceId);
    }

    [Fact]
    public void SelectingNextWithoutAValidSelectionStartsAtTheFirstPieceAndPreviousAtTheLast()
    {
        var doc = Standard();
        foreach (var selection in new Selection?[] { null, Selection.None, Selection.Of(Guid.NewGuid()), new Selection(new[] { doc.Pieces[0].Id, doc.Pieces[1].Id }) })
        {
            Assert.Equal(doc.Pieces[0].Id, AssertPlan(PlanOf(new SelectCommand(PieceTarget.Next), doc, selection)).TargetPieceId);
            Assert.Equal(doc.Pieces[2].Id, AssertPlan(PlanOf(new SelectCommand(PieceTarget.Previous), doc, selection)).TargetPieceId);
        }
    }

    [Fact]
    public void SelectingNextAndPreviousWrapsAround()
    {
        var doc = Standard();
        Assert.Equal(doc.Pieces[1].Id, AssertPlan(PlanOf(new SelectCommand(PieceTarget.Next), doc, Selection.Of(doc.Pieces[0].Id))).TargetPieceId);
        Assert.Equal(doc.Pieces[0].Id, AssertPlan(PlanOf(new SelectCommand(PieceTarget.Next), doc, Selection.Of(doc.Pieces[2].Id))).TargetPieceId);
        Assert.Equal(doc.Pieces[2].Id, AssertPlan(PlanOf(new SelectCommand(PieceTarget.Previous), doc, Selection.Of(doc.Pieces[0].Id))).TargetPieceId);
        Assert.Equal(doc.Pieces[0].Id, AssertPlan(PlanOf(new SelectCommand(PieceTarget.Previous), doc, Selection.Of(doc.Pieces[1].Id))).TargetPieceId);
    }

    [Fact]
    public void SelectionAfterIsTheTargetForSelectingPlansAndEmptyForClearing()
    {
        var doc = Standard();
        var named = AssertPlan(PlanOf(new SelectCommand(PieceTarget.Named, "kol"), doc, null));
        Assert.Equal(doc.Pieces[2].Id, named.SelectionAfter);
        Assert.Equal(PlanClass.NoChange, named.Class);
        var cleared = AssertPlan(PlanOf(new SelectCommand(PieceTarget.None), doc, Selection.Of(doc.Pieces[0].Id)));
        Assert.Null(cleared.SelectionAfter);
        Assert.Null(cleared.TargetPieceId);
        var moved = AssertPlan(PlanOf(new MoveByCommand(5, 5), doc, Selection.Of(doc.Pieces[0].Id)));
        Assert.Null(moved.SelectionAfter);
    }

    // ================================================================================================================
    // Layers
    // ================================================================================================================

    [Fact]
    public void LockedLayerRefusesEveryEditWithR08ButStillAllowsSelecting()
    {
        var locked = new Layer { Name = "Kilitli", Locked = true };
        var doc = PieceOperationTestData.WithLayers(Standard(), locked);
        doc.Pieces[0].LayerId = locked.Id;
        var selection = Selection.Of(doc.Pieces[0].Id);
        foreach (var command in new Command[]
                 {
                     new MoveByCommand(25, 0), new MoveToCommand(10, 10), new RotateCommand(90, RotationDirection.Clockwise), new RotateCommand(180, RotationDirection.Clockwise),
                     new MirrorCommand(MirrorAxis.Vertical), new ResizeCommand(95, null), new RenameCommand("Yeni ad"), new DeleteCommand(PieceTarget.Selected),
                     new DeleteCommand(PieceTarget.Named, "ön beden"),
                 })
            AssertRefusedWith("R08", PlanOf(command, doc, selection));

        Assert.True(PlanOf(new SelectCommand(PieceTarget.Named, "ön beden"), doc, selection).IsPlan);
        Assert.True(PlanOf(new ViewCommand(ViewAction.FitSelected), doc, selection).IsPlan);
        Assert.True(PlanOf(new MoveByCommand(25, 0), doc, Selection.Of(doc.Pieces[1].Id)).IsPlan);
        Assert.True(PlanOf(new RectangleCommand(120, 80), doc, selection).IsPlan);
    }

    [Fact]
    public void NewShapesOnALockedTargetLayerAreRefusedWithR08()
    {
        var locked = new Layer { Name = "Kilitli", Locked = true };
        var doc = PieceOperationTestData.WithLayers(Standard(), locked);
        var view = new DocumentView(doc, Epoch) { UndoCount = 1, NewPieceLayerId = locked.Id };
        AssertRefusedWith("R08", PlanOf(new RectangleCommand(120, 80), doc, null, view));
        AssertRefusedWith("R08", PlanOf(new LineCommand(150, LineOrientation.Horizontal), doc, null, view));
        AssertRefusedWith("R08", PlanOf(new LineCommand(150, LineOrientation.Vertical), doc, null, view));
    }

    [Fact]
    public void HiddenDefaultLayerRefusesNewShapesWithR08()
    {
        var doc = Standard();
        doc.Layers = [new Layer { Id = Layer.DefaultId, Name = "Katman 1", Visible = false }];
        AssertRefusedWith("R08", PlanOf(new RectangleCommand(120, 80), doc, null));
        AssertRefusedWith("R08", PlanOf(new RectangleCommand(100, 100, true), doc, null));
        AssertRefusedWith("R08", PlanOf(new LineCommand(150, LineOrientation.Horizontal), doc, null));
        AssertRefusedWith("R08", PlanOf(new LineCommand(150, LineOrientation.Vertical), doc, null));
    }

    [Fact]
    public void LayerListWithoutTheDefaultLayerRefusesNewShapesWithR08ButLetsPiecesMove()
    {
        var doc = Standard();
        doc.Layers = [new Layer { Name = "Yalnız" }];
        AssertRefusedWith("R08", PlanOf(new RectangleCommand(120, 80), doc, null));
        Assert.True(PlanOf(new MoveByCommand(25, 0), doc, Selection.Of(doc.Pieces[0].Id)).IsPlan);
    }

    [Fact]
    public void UnknownNewPieceLayerIsRefusedWithR08()
    {
        var doc = Standard();
        var view = new DocumentView(doc, Epoch) { UndoCount = 1, NewPieceLayerId = Guid.NewGuid() };
        AssertRefusedWith("R08", PlanOf(new RectangleCommand(120, 80), doc, null, view));
    }

    [Fact]
    public void NewShapeGoesToTheLayerNamedByTheView()
    {
        var layer = new Layer { Name = "Ek katman" };
        var doc = PieceOperationTestData.WithLayers(Standard(), layer);
        var view = new DocumentView(doc, Epoch) { UndoCount = 1, NewPieceLayerId = layer.Id };
        var plan = AssertPlan(PlanOf(new RectangleCommand(120, 80), doc, null, view));
        var edited = plan.EditedDocument()!;
        Assert.Equal(layer.Id, edited.Pieces.Single(p => p.Id == plan.CreatedPieceId).LayerId);
    }

    // ================================================================================================================
    // Coordinates, curves, point counts
    // ================================================================================================================

    [Fact]
    public void NewShapeNextToAPieceAtTheWorkingAreaLimitIsRefusedWithR04()
    {
        var doc = PieceOperationTestData.Doc(Rect("Uzak", 1e8 - 100, 0, 100, 100));
        AssertRefusedWith("R04", PlanOf(new RectangleCommand(120, 80), doc, null));
        AssertRefusedWith("R04", PlanOf(new LineCommand(150, LineOrientation.Horizontal), doc, null));
    }

    [Fact]
    public void MovingIsRefusedWhenTheTargetLeavesTheWorkingArea()
    {
        var doc = PieceOperationTestData.Doc(Rect("Uzak", 1e8 - 100, 0, 100, 100));
        var selection = Selection.Of(doc.Pieces[0].Id);
        var tooFar = PlanOf(new MoveByCommand(1000, 0), doc, selection);
        Assert.False(tooFar.IsPlan, tooFar.ToString());
        AssertRefusedWith("R04", PlanOf(new MoveToCommand(1e9, 0), doc, selection));
        AssertSound(tooFar, ViewOf(doc), selection, null, "move beyond the limit");
    }

    [Fact]
    public void CurvedPieceIsRefusedForMovingRotatingAndMirroringWithR12AndForResizingWithR11()
    {
        var doc = PieceOperationTestData.Doc(Curved("Eğri"));
        var selection = Selection.Of(doc.Pieces[0].Id);
        AssertRefusedWith("R12", PlanOf(new MoveByCommand(25, 0), doc, selection));
        AssertRefusedWith("R12", PlanOf(new MoveToCommand(100, 100), doc, selection));
        AssertRefusedWith("R12", PlanOf(new RotateCommand(90, RotationDirection.Clockwise), doc, selection));
        AssertRefusedWith("R12", PlanOf(new RotateCommand(180, RotationDirection.CounterClockwise), doc, selection));
        AssertRefusedWith("R12", PlanOf(new MirrorCommand(MirrorAxis.Vertical), doc, selection));
        AssertRefusedWith("R12", PlanOf(new MirrorCommand(MirrorAxis.Horizontal), doc, selection));
        AssertRefusedWith("R11", PlanOf(new ResizeCommand(95, null), doc, selection));
    }

    [Fact]
    public void CurvedPieceCanStillBeRenamedDeletedSelectedAndDuplicated()
    {
        var doc = PieceOperationTestData.Doc(Curved("Eğri"));
        var selection = Selection.Of(doc.Pieces[0].Id);
        Assert.True(PlanOf(new RenameCommand("Yeni ad"), doc, selection).IsPlan);
        Assert.True(PlanOf(new DeleteCommand(PieceTarget.Selected), doc, selection).IsPlan);
        Assert.True(PlanOf(new SelectCommand(PieceTarget.Named, "eğri"), doc, null).IsPlan);
        // The copy re-samples the curves; whether this hand-made contour survives that is not the point, a clean answer is.
        AssertSound(CommandPlanner.PlanDuplicate(ViewOf(doc), selection), ViewOf(doc), selection, null, "duplicate of a curved piece");
    }

    [Fact]
    public void CurvedPieceWithNotchesIsRefusedForMovingAndDuplicating()
    {
        var piece = Curved("Eğri çentikli");
        piece.Notches = [new NotchMark { VertexIndex = 1, Position = new Point2(500, 20) }];
        var doc = PieceOperationTestData.Doc(piece);
        var selection = Selection.Of(piece.Id);
        AssertRefusedWith("R12", PlanOf(new MoveByCommand(25, 0), doc, selection));
        var duplicate = CommandPlanner.PlanDuplicate(ViewOf(doc), selection);
        Assert.False(duplicate.IsPlan, duplicate.ToString());
        AssertSound(duplicate, ViewOf(doc), selection, null, "duplicate of a curved piece with notches");
    }

    [Fact]
    public void RotatingOrMirroringMoreThan4096PointsIsRefusedWithR17()
    {
        foreach (var count in new[] { 4097, 5000 })
        {
            var piece = new PatternPiece { Name = "Daire", Points = Circle(count) };
            var doc = PieceOperationTestData.Doc(piece);
            var selection = Selection.Of(piece.Id);
            foreach (var degrees in new[] { 90, 180, 270 })
                foreach (var direction in Enum.GetValues<RotationDirection>())
                    AssertRefusedWith("R17", PlanOf(new RotateCommand(degrees, direction), doc, selection));
            AssertRefusedWith("R17", PlanOf(new MirrorCommand(MirrorAxis.Vertical), doc, selection));
            AssertRefusedWith("R17", PlanOf(new MirrorCommand(MirrorAxis.Horizontal), doc, selection));
        }
    }

    [Fact]
    public void ExactlyFourThousandNinetySixPointsAreNotRefusedWithR17()
    {
        var piece = new PatternPiece { Name = "Daire", Points = Circle(4096) };
        var doc = PieceOperationTestData.Doc(piece);
        var selection = Selection.Of(piece.Id);
        var rotate = PlanOf(new RotateCommand(90, RotationDirection.Clockwise), doc, selection);
        Assert.NotEqual("R17", rotate.Code);
        var mirror = PlanOf(new MirrorCommand(MirrorAxis.Vertical), doc, selection);
        Assert.NotEqual("R17", mirror.Code);
        AssertSound(rotate, ViewOf(doc), selection, null, "4096 points rotate");
        AssertSound(mirror, ViewOf(doc), selection, null, "4096 points mirror");
    }

    [Fact]
    public void PieceWithFiveThousandPointsIsPlannedSoundlyForTheOtherCommands()
    {
        var piece = new PatternPiece { Name = "Daire", Points = Circle(5000) };
        var doc = PieceOperationTestData.Doc(piece, Rect("Düz", 3000, 0, 100, 100));
        var view = ViewOf(doc);
        var selection = Selection.Of(piece.Id);
        AssertRefusedWith("R10", PlanOf(new ResizeCommand(95, null), doc, selection, view));
        foreach (var command in new Command[]
                 {
                     new MoveByCommand(25, 0), new MoveToCommand(10, 10), new RenameCommand("Yeni"), new DeleteCommand(PieceTarget.Selected),
                     new SelectCommand(PieceTarget.Next), new SelectCommand(PieceTarget.Named, "daire"), new ViewCommand(ViewAction.FitSelected),
                     new HandoffCommand(HandoffTarget.Duplicate), new RectangleCommand(120, 80), new LineCommand(150, LineOrientation.Vertical),
                 })
        {
            var result = PlanOf(command, doc, selection, view);
            AssertSound(result, view, selection, null, "5000 points / " + Describe(command));
        }
    }

    // ================================================================================================================
    // Specific behaviour of single commands in odd situations
    // ================================================================================================================

    [Fact]
    public void RotationDegreesAndDirectionsMapToTheRightTurnAndOperationId()
    {
        var doc = Standard();
        var selection = Selection.Of(doc.Pieces[0].Id);
        var expected = new (int Degrees, RotationDirection Direction, int Turn, string Id)[]
        {
            (90, RotationDirection.Clockwise, 90, "rotate-cw"),
            (90, RotationDirection.CounterClockwise, -90, "rotate-ccw"),
            (270, RotationDirection.Clockwise, -90, "rotate-ccw"),
            (270, RotationDirection.CounterClockwise, 90, "rotate-cw"),
            (180, RotationDirection.Clockwise, 180, "rotate-half"),
            (180, RotationDirection.CounterClockwise, 180, "rotate-half"),
        };
        foreach (var (degrees, direction, turn, id) in expected)
        {
            var plan = AssertPlan(PlanOf(new RotateCommand(degrees, direction), doc, selection));
            var operation = Assert.IsType<RotatePieceOperation>(plan.Operation);
            Assert.Equal(turn, operation.ClockwiseDegrees);
            Assert.Equal(id, operation.OperationId);
            Assert.Equal(doc.Pieces[0].Id, operation.PieceId);
            Assert.False(plan.AppliesInCore);
        }
    }

    [Fact]
    public void AnglesOtherThan90180And270AreRefusedWithR13()
    {
        var doc = Standard();
        var selection = Selection.Of(doc.Pieces[0].Id);
        foreach (var degrees in new[] { 45, 0, -90, 360, 450, int.MaxValue, int.MinValue })
            AssertRefusedWith("R13", PlanOf(new RotateCommand(degrees, RotationDirection.Clockwise), doc, selection));
    }

    [Fact]
    public void MirrorOperationsCarryTheAxisAndTheOperationId()
    {
        var doc = Standard();
        var selection = Selection.Of(doc.Pieces[0].Id);
        var vertical = Assert.IsType<MirrorPieceOperation>(AssertPlan(PlanOf(new MirrorCommand(MirrorAxis.Vertical), doc, selection)).Operation);
        Assert.Equal("mirror-vertical", vertical.OperationId);
        var horizontal = Assert.IsType<MirrorPieceOperation>(AssertPlan(PlanOf(new MirrorCommand(MirrorAxis.Horizontal), doc, selection)).Operation);
        Assert.Equal("mirror-horizontal", horizontal.OperationId);
    }

    [Fact]
    public void NonFiniteMoveDistancesAreRefusedWithR02()
    {
        var doc = Standard();
        var selection = Selection.Of(doc.Pieces[0].Id);
        AssertRefusedWith("R02", PlanOf(new MoveByCommand(double.NaN, 0), doc, selection));
        AssertRefusedWith("R02", PlanOf(new MoveByCommand(0, double.PositiveInfinity), doc, selection));
        AssertRefusedWith("R02", PlanOf(new MoveToCommand(double.NaN, 0), doc, selection));
        AssertRefusedWith("R02", PlanOf(new MoveToCommand(0, double.NegativeInfinity), doc, selection));
    }

    [Fact]
    public void ChangesThatChangeNothingAreRefusedWithR25()
    {
        var doc = Standard();
        var selection = Selection.Of(doc.Pieces[0].Id);
        AssertRefusedWith("R25", PlanOf(new MoveByCommand(0, 0), doc, selection));
        AssertRefusedWith("R25", PlanOf(new MoveToCommand(20, 20), doc, selection));
        AssertRefusedWith("R25", PlanOf(new ResizeCommand(480, null), doc, selection));
        AssertRefusedWith("R25", PlanOf(new ResizeCommand(480, 620), doc, selection));
        AssertRefusedWith("R25", PlanOf(new RenameCommand("Ön beden"), doc, selection));
    }

    [Fact]
    public void ResizingWithNoSideAtAllIsRefusedWithR02()
    {
        var doc = Standard();
        AssertRefusedWith("R02", PlanOf(new ResizeCommand(null, null), doc, Selection.Of(doc.Pieces[0].Id)));
    }

    [Fact]
    public void ResizeSidesOutsideTheAllowedRangeAreRefusedWithR04()
    {
        var doc = Standard();
        var selection = Selection.Of(doc.Pieces[0].Id);
        AssertRefusedWith("R04", PlanOf(new ResizeCommand(0, 10), doc, selection));
        AssertRefusedWith("R04", PlanOf(new ResizeCommand(null, 20001), doc, selection));
        AssertRefusedWith("R04", PlanOf(new ResizeCommand(1e9, 1e9), doc, selection));
        AssertSound(PlanOf(new ResizeCommand(double.NaN, 5), doc, selection), ViewOf(doc), selection, null, "NaN width");
    }

    [Fact]
    public void ResizingAPieceThatIsNotARectangleIsRefusedWithR10AndOneWithAGrainlineWithR11()
    {
        var triangle = new PatternPiece { Name = "Üçgen", Points = [new(0, 0), new(100, 0), new(50, 80)] };
        var withGrain = Rect("İplikli", 300, 0, 100, 100);
        withGrain.Grainline = new Grainline { Start = new Point2(320, 20), End = new Point2(320, 80) };
        var doc = PieceOperationTestData.Doc(triangle, withGrain);
        AssertRefusedWith("R10", PlanOf(new ResizeCommand(95, null), doc, Selection.Of(triangle.Id)));
        AssertRefusedWith("R11", PlanOf(new ResizeCommand(95, null), doc, Selection.Of(withGrain.Id)));
    }

    [Fact]
    public void ResizeEditsTheDocumentCopyOnlyWhenApplied()
    {
        var doc = Standard();
        var plan = AssertPlan(PlanOf(new ResizeCommand(95, 60), doc, Selection.Of(doc.Pieces[0].Id)));
        Assert.True(plan.AppliesInCore);
        var edited = plan.EditedDocument()!;
        var bounds = Geometry.Bounds(edited.Pieces[0].Points);
        Assert.Equal(95, bounds.Width, 9);
        Assert.Equal(60, bounds.Height, 9);
        Assert.Equal(480, Geometry.Bounds(doc.Pieces[0].Points).Width, 9);
    }

    [Fact]
    public void RenameRefusesEmptyBlankAndNullNamesWithR24()
    {
        var doc = Standard();
        var selection = Selection.Of(doc.Pieces[0].Id);
        foreach (var name in new string?[] { "", "   ", "\t", null })
            AssertRefusedWith("R24", PlanOf(new RenameCommand(name!), doc, selection));
    }

    [Fact]
    public void RenameRefusesTooLongControlCharacterAndLoneSurrogateNames()
    {
        var doc = Standard();
        var selection = Selection.Of(doc.Pieces[0].Id);
        foreach (var name in new[] { new string('x', 129), new string('x', 500), "a\nb", "a\tb", "a\0b", "\uD800abc", "abc\uDC00" })
        {
            var result = PlanOf(new RenameCommand(name), doc, selection);
            Assert.False(result.IsPlan, $"name of length {name.Length} was planned: {result}");
            AssertSound(result, ViewOf(doc), selection, null, "rename " + name.Length);
        }
    }

    [Fact]
    public void RenameAcceptsTheLongestNameAnEmojiAndTrimsPadding()
    {
        var doc = Standard();
        var selection = Selection.Of(doc.Pieces[0].Id);

        var longest = AssertPlan(PlanOf(new RenameCommand(new string('x', 128)), doc, selection));
        Assert.Equal(new string('x', 128), longest.EditedDocument()!.Pieces[0].Name);
        Assert.Contains(new string('x', 128), longest.Preview); // a new name is shown in full before it is accepted
        Assert.True(longest.Preview.Length < 300, "the preview of a 128 character new name stays one short sentence");

        var emoji = AssertPlan(PlanOf(new RenameCommand("😀 beden"), doc, selection));
        Assert.Equal("😀 beden", emoji.EditedDocument()!.Pieces[0].Name);

        var padded = AssertPlan(PlanOf(new RenameCommand("  Yeni ad  "), doc, selection));
        Assert.Equal("Yeni ad", padded.EditedDocument()!.Pieces[0].Name);
        Assert.Contains("‘Yeni ad’", padded.Preview);
    }

    [Fact]
    public void LookingUpAnUnknownOrEmptyNameIsRefusedWithR15a()
    {
        var doc = Standard();
        foreach (var name in new string?[] { "zzz", "", "  ", null, "\0" })
        {
            AssertRefusedWith("R15a", PlanOf(new SelectCommand(PieceTarget.Named, name), doc, null));
            AssertRefusedWith("R15a", PlanOf(new DeleteCommand(PieceTarget.Named, name), doc, null));
        }
        var notFound = PlanOf(new SelectCommand(PieceTarget.Named, "zzz"), doc, null);
        Assert.Equal(new[] { "Ön beden", "Arka beden", "Kol" }, notFound.Refusal!.Buttons.Select(b => b.Label).ToArray());
    }

    [Fact]
    public void LookingUpAnyNameInAnEmptyProjectIsRefusedWithR03()
    {
        var doc = new ProjectDocument();
        AssertRefusedWith("R03", PlanOf(new SelectCommand(PieceTarget.Named, "kol"), doc, null));
        AssertRefusedWith("R03", PlanOf(new DeleteCommand(PieceTarget.Named, "kol"), doc, null));
    }

    [Fact]
    public void FourPiecesWithTheSameNameAskWhichOneWithAtMostThreeButtons()
    {
        var doc = PieceOperationTestData.Doc(Rect("Kol", 0, 0, 100, 50), Rect("Kol", 200, 0, 100, 60), Rect("Kol", 400, 0, 100, 70), Rect("kol", 600, 0, 100, 80));
        foreach (var command in new Command[] { new SelectCommand(PieceTarget.Named, "kol"), new DeleteCommand(PieceTarget.Named, "KOL") })
        {
            var result = PlanOf(command, doc, null);
            AssertRefusedWith("R15b", result);
            Assert.Equal(3, result.Refusal!.Buttons.Count);
            Assert.All(result.Refusal.Buttons, b => Assert.EndsWith(command is SelectCommand ? " seç" : " sil", b.FillText));
        }
    }

    [Fact]
    public void AnExactNameWinsOverLongerNamesThatContainIt()
    {
        var doc = PieceOperationTestData.Doc(Rect("Ön beden", 0, 0, 100, 50), Rect("Ön beden 2", 200, 0, 100, 50));
        var plan = AssertPlan(PlanOf(new SelectCommand(PieceTarget.Named, "ön beden"), doc, null));
        Assert.Equal(doc.Pieces[0].Id, plan.TargetPieceId);
        var delete = AssertPlan(PlanOf(new DeleteCommand(PieceTarget.Named, "ON BEDEN"), doc, null));
        Assert.Equal(doc.Pieces[0].Id, delete.TargetPieceId);
        var longer = AssertPlan(PlanOf(new SelectCommand(PieceTarget.Named, "ön beden 2"), doc, null));
        Assert.Equal(doc.Pieces[1].Id, longer.TargetPieceId);
        AssertRefusedWith("R15b", PlanOf(new SelectCommand(PieceTarget.Named, "ön"), doc, null));
    }

    [Fact]
    public void NameLookupIgnoresCaseAndTurkishDiacritics()
    {
        var doc = Standard();
        foreach (var name in new[] { "ON BEDEN", "on beden", "Ön Beden", "ÖN BEDEN", "arka" })
            Assert.True(PlanOf(new SelectCommand(PieceTarget.Named, name), doc, null).IsPlan, name);
    }

    [Fact]
    public void PiecesWithNullNamesAreNeverFoundByNameAndNeverBreakTheLookup()
    {
        var nameless = new PatternPiece { Name = null!, Points = [new(0, 0), new(100, 0), new(100, 50), new(0, 50)] };
        var doc = PieceOperationTestData.Doc(nameless, Rect("Kol", 200, 0, 100, 50));
        var plan = AssertPlan(PlanOf(new SelectCommand(PieceTarget.Named, "kol"), doc, null));
        Assert.Equal(doc.Pieces[1].Id, plan.TargetPieceId);
        var next = AssertPlan(PlanOf(new SelectCommand(PieceTarget.Next), doc, null));
        Assert.Equal(nameless.Id, next.TargetPieceId);
        Assert.Contains("‘’", next.Preview);
    }

    [Fact]
    public void SelectingAPieceWithNonFinitePointsNeverGivesTheCanvasANonFiniteFrame()
    {
        var bad = new PatternPiece { Name = "Bozuk", Points = [new(0, 0), new(double.NaN, 0), new(100, 100)] };
        var doc = PieceOperationTestData.Doc(bad);
        foreach (var command in new Command[] { new SelectCommand(PieceTarget.Next), new SelectCommand(PieceTarget.Previous), new SelectCommand(PieceTarget.Named, "bozuk") })
            AssertSound(PlanOf(command, doc, null), ViewOf(doc), null, null, "select a piece with a NaN point");
    }

    [Fact]
    public void NothingSelectedDoesNotOfferButtonsForPiecesWithoutAName()
    {
        var doc = PieceOperationTestData.Doc(Rect("", 0, 0, 10, 10), Rect("   ", 20, 0, 10, 10), Rect("Kol", 40, 0, 10, 10));
        var result = PlanOf(new MoveByCommand(25, 0), doc, Selection.None);
        AssertRefusedWith("R03", result);
        Assert.All(result.Refusal!.Buttons, b => Assert.False(string.IsNullOrWhiteSpace(b.Label), "a button without a label"));
    }

    [Fact]
    public void RefusalTextNamingARealPieceStaysOnOneLine()
    {
        var doc = PieceOperationTestData.Doc(Rect("a\nb", 0, 0, 10, 10), Rect("Kol", 40, 0, 10, 10));
        var result = PlanOf(new SelectCommand(PieceTarget.Named, "zzz"), doc, null);
        AssertRefusedWith("R15a", result);
        Assert.False(result.Refusal!.Message.Contains('\n'), "the example in the refusal repeats a piece name with a line break");
    }

    [Fact]
    public void DeleteTargetsThatAreNotSelectedOrNamedAreRefusedAsNotUnderstood()
    {
        var doc = Standard();
        foreach (var target in new[] { PieceTarget.Next, PieceTarget.Previous, PieceTarget.None, (PieceTarget)99 })
            AssertRefusedWith("R01", PlanOf(new DeleteCommand(target), doc, Selection.Of(doc.Pieces[0].Id)));
    }

    [Fact]
    public void DeletingTheSelectedPieceIsDangerousShowsARedFrameAndNamesTheSize()
    {
        var doc = Standard();
        var plan = AssertPlan(PlanOf(new DeleteCommand(PieceTarget.Selected), doc, Selection.Of(doc.Pieces[0].Id)));
        Assert.Equal("‘Ön beden’ silinecek (480 × 620 mm). Geri alabilirsiniz.", plan.Preview);
        Assert.Equal(PlanClass.Dangerous, plan.Class);
        Assert.True(plan.RequiresConfirmation);
        Assert.True(plan.Danger);
        Assert.Equal(GhostStyle.Danger, plan.Ghost.Style);
        Assert.Single(plan.Ghost.Polygons);
        Assert.True(plan.NeedsSelectedPiece);
        Assert.Equal(2, plan.EditedDocument()!.Pieces.Count);
    }

    [Fact]
    public void DeletingANamedPieceDoesNotNeedASelectionAndMentionsItsGradingRule()
    {
        var doc = Standard();
        doc.GradingRules.Add(new GradingRuleSet { SourcePieceId = doc.Pieces[2].Id, SourceClosed = true, SourcePoints = [.. doc.Pieces[2].Points], Deltas = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)] });
        var plan = AssertPlan(PlanOf(new DeleteCommand(PieceTarget.Named, "kol"), doc, null));
        Assert.False(plan.NeedsSelectedPiece);
        Assert.Equal(doc.Pieces[2].Id, plan.TargetPieceId);
        Assert.Contains(plan.Caveats, c => c.Contains("1 serileme kuralı", StringComparison.Ordinal));
        Assert.Empty(plan.EditedDocument()!.GradingRules);
        Assert.Single(doc.GradingRules);
    }

    [Fact]
    public void DeletingAPieceWithoutPointsHasNoFrameAndDoesNotThrow()
    {
        var empty = new PatternPiece { Name = "Boş", Points = [] };
        var doc = PieceOperationTestData.Doc(empty);
        var result = PlanOf(new DeleteCommand(PieceTarget.Selected), doc, Selection.Of(empty.Id));
        // Deleting does not need points (Locate is told so), so this is a plan without a frame; the point is a clean answer.
        AssertSound(result, ViewOf(doc), Selection.Of(empty.Id), null, "delete a piece without points");
        var named = PlanOf(new DeleteCommand(PieceTarget.Named, "boş"), doc, null);
        AssertSound(named, ViewOf(doc), null, null, "delete a named piece without points");
        if (named.IsPlan) Assert.True(named.Plan!.Ghost.IsEmpty);
    }

    // ================================================================================================================
    // Unit assumed, previews of hostile names
    // ================================================================================================================

    [Fact]
    public void WhenNoUnitWasWrittenEveryLengthPreviewEndsWithTheMillimetreSentenceAndOffersACentimetreChip()
    {
        var empty = new ProjectDocument();
        var rect = AssertPlan(PlanOf(new RectangleCommand(120, 80), empty, null, null, unitAssumed: true));
        Assert.EndsWith("Birim yazmadınız; milimetre sayıldı.", rect.Preview);
        Assert.Contains(rect.Alternatives, a => a.Label == "120 x 80 cm dikdörtgen");
        var withUnit = AssertPlan(PlanOf(new RectangleCommand(120, 80), empty, null));
        Assert.DoesNotContain("Birim yazmadınız", withUnit.Preview);
        Assert.Empty(withUnit.Alternatives);

        var doc = Standard();
        var selection = Selection.Of(doc.Pieces[0].Id);
        foreach (var command in new Command[] { new MoveByCommand(25, 0), new MoveToCommand(100, 50), new ResizeCommand(95, null), new LineCommand(150, LineOrientation.Horizontal), new RectangleCommand(100, 100, true) })
        {
            Assert.EndsWith("Birim yazmadınız; milimetre sayıldı.", AssertPlan(PlanOf(command, doc, selection, null, unitAssumed: true)).Preview);
            Assert.DoesNotContain("Birim yazmadınız", AssertPlan(PlanOf(command, doc, selection)).Preview);
        }
        var move = AssertPlan(PlanOf(new MoveByCommand(25, 0), doc, selection, null, unitAssumed: true));
        Assert.Contains(move.Alternatives, a => a.Label == "25 cm sağa taşı");
    }

    [Fact]
    public void PreviewReplacesControlCharactersInPieceNamesAndStaysOnOneLine()
    {
        var piece = Rect("a\nb\r\tc\0d", 0, 0, 100, 50);
        var doc = PieceOperationTestData.Doc(piece, Rect("Kol", 200, 0, 100, 50));
        var selection = Selection.Of(piece.Id);
        foreach (var command in new Command[] { new DeleteCommand(PieceTarget.Selected), new MoveByCommand(5, 5), new RotateCommand(90, RotationDirection.Clockwise), new MirrorCommand(MirrorAxis.Vertical), new RenameCommand("Yeni") })
        {
            var plan = AssertPlan(PlanOf(command, doc, selection));
            Assert.False(plan.Preview.Contains('\n') || plan.Preview.Contains('\r') || plan.Preview.Contains('\0'), plan.Preview);
            Assert.Contains("a·b··c·d", plan.Preview);
        }
        var next = AssertPlan(PlanOf(new SelectCommand(PieceTarget.Next), doc, null));
        Assert.Equal(piece.Id, next.TargetPieceId);
        Assert.Contains("a·b··c·d", next.Preview);
        foreach (var command in new Command[] { new ViewCommand(ViewAction.FitSelected), new HandoffCommand(HandoffTarget.Grainline) })
        {
            var plan = AssertPlan(PlanOf(command, doc, selection));
            Assert.False(plan.Preview.Contains('\n') || plan.Preview.Contains('\r') || plan.Preview.Contains('\0'), plan.Preview);
        }
    }

    [Fact]
    public void PreviewShortensVeryLongPieceNames()
    {
        var piece = Rect(new string('ş', 5000), 0, 0, 100, 50);
        var doc = PieceOperationTestData.Doc(piece);
        var plan = AssertPlan(PlanOf(new DeleteCommand(PieceTarget.Selected), doc, Selection.Of(piece.Id)));
        Assert.True(plan.Preview.Length < 400, $"preview of {plan.Preview.Length} characters");
        Assert.Contains("…", plan.Preview);
    }

    [Fact]
    public void PreviewNeverCutsAnEmojiInTheMiddleOfItsSurrogatePair()
    {
        var piece = Rect(new string('a', 39) + "😀" + "bbb", 0, 0, 100, 50);
        var doc = PieceOperationTestData.Doc(piece);
        var plan = AssertPlan(PlanOf(new DeleteCommand(PieceTarget.Selected), doc, Selection.Of(piece.Id)));
        for (var i = 0; i < plan.Preview.Length; i++)
        {
            if (char.IsHighSurrogate(plan.Preview[i]))
            {
                Assert.True(i + 1 < plan.Preview.Length && char.IsLowSurrogate(plan.Preview[i + 1]), "lone high surrogate in the preview");
                i++;
            }
            else
            {
                Assert.False(char.IsLowSurrogate(plan.Preview[i]), "lone low surrogate in the preview");
            }
        }
    }

    [Fact]
    public void PreviewOfAnUndoStaysOnOneLineWhenTheUndoLabelHasLineBreaks()
    {
        var doc = Standard();
        var view = new DocumentView(doc, Epoch) { UndoCount = 2, LastUndoLabel = "birinci satır\nikinci satır\r\nüçüncü" };
        var plan = AssertPlan(PlanOf(new UndoCommand(1), doc, null, view));
        Assert.DoesNotContain('\n', plan.Preview);
        Assert.DoesNotContain('\r', plan.Preview);
    }

    [Fact]
    public void PreviewOfASaveStaysOnOneLineWhenTheFileNameHasLineBreaks()
    {
        var doc = Standard();
        var view = new DocumentView(doc, Epoch) { FileName = "bir\niki.tcad" };
        var plan = AssertPlan(PlanOf(new FileCommand(FileAction.Save), doc, null, view));
        Assert.DoesNotContain('\n', plan.Preview);
        Assert.DoesNotContain('\r', plan.Preview);
    }

    // ================================================================================================================
    // Guards, undo, tools, drawing, files, hand-offs
    // ================================================================================================================

    [Fact]
    public void ProjectGuardsAreAppliedInTheDocumentedOrder()
    {
        var doc = Standard();
        var rectangle = new RectangleCommand(120, 80);
        DocumentView View(bool recovery = false, bool file = false, bool drawing = false, bool drafts = false) => new(doc, Epoch)
        { RecoveryIncomplete = recovery, FileOperationInProgress = file, DrawingInProgress = drawing, HasUnappliedDrafts = drafts, UndoCount = 1 };

        AssertRefusedWith("R21", PlanOf(rectangle, doc, null, View(recovery: true)));
        AssertRefusedWith("R09", PlanOf(rectangle, doc, null, View(file: true)));
        AssertRefusedWith("R07", PlanOf(rectangle, doc, null, View(drawing: true)));
        AssertRefusedWith("R20", PlanOf(rectangle, doc, null, View(drafts: true)));
        AssertRefusedWith("R21", PlanOf(rectangle, doc, null, View(recovery: true, file: true, drawing: true, drafts: true)));
        AssertRefusedWith("R09", PlanOf(rectangle, doc, null, View(file: true, drawing: true, drafts: true)));
        AssertRefusedWith("R07", PlanOf(rectangle, doc, null, View(drawing: true, drafts: true)));

        var selection = Selection.Of(doc.Pieces[0].Id);
        foreach (var command in new Command[] { new MoveByCommand(25, 0), new RotateCommand(90, RotationDirection.Clockwise), new MirrorCommand(MirrorAxis.Vertical), new ResizeCommand(95, null), new RenameCommand("Yeni"), new DeleteCommand(PieceTarget.Selected), new DeleteCommand(PieceTarget.Named, "kol"), new LineCommand(150, LineOrientation.Vertical) })
        {
            AssertRefusedWith("R21", PlanOf(command, doc, selection, View(recovery: true)));
            AssertRefusedWith("R09", PlanOf(command, doc, selection, View(file: true)));
            AssertRefusedWith("R07", PlanOf(command, doc, selection, View(drawing: true)));
            AssertRefusedWith("R20", PlanOf(command, doc, selection, View(drafts: true)));
        }
    }

    [Fact]
    public void SelectingIsRefusedWhileADrawingOrARecoveryIsOpen()
    {
        var doc = Standard();
        var drawing = new DocumentView(doc, Epoch) { DrawingInProgress = true, DrawingPointCount = 2 };
        var recovery = new DocumentView(doc, Epoch) { RecoveryIncomplete = true };
        foreach (var command in new Command[] { new SelectCommand(PieceTarget.Named, "kol"), new SelectCommand(PieceTarget.Next), new SelectCommand(PieceTarget.None) })
        {
            AssertRefusedWith("R07", PlanOf(command, doc, null, drawing));
            AssertRefusedWith("R21", PlanOf(command, doc, null, recovery));
        }
    }

    [Fact]
    public void UndoAndRedoNeedSomethingToUndoOrRedoAndNoDrawingInProgress()
    {
        var doc = Standard();
        var empty = new DocumentView(doc, Epoch) { UndoCount = 0, RedoCount = 0 };
        AssertRefusedWith("R18", PlanOf(new UndoCommand(1), doc, null, empty));
        AssertRefusedWith("R18", PlanOf(new UndoCommand(5), doc, null, empty));
        AssertRefusedWith("R18", PlanOf(new RedoCommand(), doc, null, empty));
        var negative = new DocumentView(doc, Epoch) { UndoCount = -3, RedoCount = -3 };
        AssertRefusedWith("R18", PlanOf(new UndoCommand(1), doc, null, negative));
        AssertRefusedWith("R18", PlanOf(new RedoCommand(), doc, null, negative));
        var drawing = new DocumentView(doc, Epoch) { UndoCount = 3, RedoCount = 3, DrawingInProgress = true };
        AssertRefusedWith("R07", PlanOf(new UndoCommand(1), doc, null, drawing));
        AssertRefusedWith("R07", PlanOf(new RedoCommand(), doc, null, drawing));
    }

    [Fact]
    public void UndoStepsAreClampedToWhatIsAvailableAndToTen()
    {
        var doc = Standard();
        var view = new DocumentView(doc, Epoch) { UndoCount = 4 };
        var many = AssertPlan(PlanOf(new UndoCommand(50), doc, null, view));
        Assert.Equal("Son 4 işlem geri alınacak.", many.Preview);
        Assert.Equal(new UndoCommand(4), many.Command);
        var capped = AssertPlan(PlanOf(new UndoCommand(int.MaxValue), doc, null, new DocumentView(doc, Epoch) { UndoCount = 100 }));
        Assert.Equal("Son 10 işlem geri alınacak.", capped.Preview);
        foreach (var steps in new[] { 0, -4, int.MinValue })
            Assert.Equal("Son işlem geri alınacak.", AssertPlan(PlanOf(new UndoCommand(steps), doc, null, view)).Preview);
    }

    [Fact]
    public void UndoPreviewNamesTheLastStepOnlyWhenThereIsAUsableLabel()
    {
        var doc = Standard();
        var labelled = new DocumentView(doc, Epoch) { UndoCount = 2, LastUndoLabel = "  25 mm sağa taşıma  " };
        Assert.Equal("Son işlem geri alınacak: 25 mm sağa taşıma.", AssertPlan(PlanOf(new UndoCommand(1), doc, null, labelled)).Preview);
        foreach (var label in new string?[] { null, "", "   " })
        {
            var view = new DocumentView(doc, Epoch) { UndoCount = 2, LastUndoLabel = label };
            Assert.Equal("Son işlem geri alınacak.", AssertPlan(PlanOf(new UndoCommand(1), doc, null, view)).Preview);
        }
        Assert.Equal("Son 2 işlem geri alınacak.", AssertPlan(PlanOf(new UndoCommand(2), doc, null, labelled)).Preview);
    }

    [Fact]
    public void ToolsAreRefusedWhileADrawingIsInProgress()
    {
        var doc = Standard();
        var drawing = new DocumentView(doc, Epoch) { DrawingInProgress = true, DrawingPointCount = 2 };
        foreach (var tool in Enum.GetValues<DrawingTool>())
        {
            AssertRefusedWith("R07", PlanOf(new ToolCommand(tool), doc, null, drawing));
            Assert.True(PlanOf(new ToolCommand(tool), doc, null).IsPlan);
        }
    }

    [Fact]
    public void FinishingADrawingNeedsADrawingAndEnoughPoints()
    {
        var doc = Standard();
        var none = new DocumentView(doc, Epoch);
        foreach (var action in Enum.GetValues<FinishDrawingAction>())
            AssertRefusedWith("R28a", PlanOf(new FinishDrawingCommand(action), doc, null, none));

        View2("close with two points", FinishDrawingAction.Close, 2, false);
        View2("close with three points", FinishDrawingAction.Close, 3, true);
        View2("finish open with one point", FinishDrawingAction.FinishOpen, 1, false);
        View2("finish open with two points", FinishDrawingAction.FinishOpen, 2, true);
        View2("cancel with no points", FinishDrawingAction.Cancel, 0, true);
        View2("remove the last point", FinishDrawingAction.RemoveLastPoint, 0, true);

        void View2(string label, FinishDrawingAction action, int points, bool planned)
        {
            var view = new DocumentView(doc, Epoch) { DrawingInProgress = true, DrawingPointCount = points };
            var result = PlanOf(new FinishDrawingCommand(action), doc, null, view);
            Assert.True(result.IsPlan == planned, $"{label}: {result}");
            AssertSound(result, view, null, null, label);
        }
    }

    [Fact]
    public void CancellingADrawingIsDangerousAndFinishingOneChangesTheProject()
    {
        var doc = Standard();
        var view = new DocumentView(doc, Epoch) { DrawingInProgress = true, DrawingPointCount = 4 };
        Assert.Equal(PlanClass.Dangerous, AssertPlan(PlanOf(new FinishDrawingCommand(FinishDrawingAction.Cancel), doc, null, view)).Class);
        Assert.Equal(PlanClass.Geometry, AssertPlan(PlanOf(new FinishDrawingCommand(FinishDrawingAction.Close), doc, null, view)).Class);
        Assert.Equal(PlanClass.NoChange, AssertPlan(PlanOf(new FinishDrawingCommand(FinishDrawingAction.RemoveLastPoint), doc, null, view)).Class);
    }

    [Fact]
    public void ViewCommandsNeverChangeTheProjectAndFitSelectedNeedsOnePiece()
    {
        var doc = Standard();
        foreach (var action in Enum.GetValues<ViewAction>().Where(a => a != ViewAction.FitSelected))
        {
            var plan = AssertPlan(PlanOf(new ViewCommand(action), doc, null));
            Assert.Equal(PlanClass.NoChange, plan.Class);
            Assert.False(plan.AppliesInCore);
            Assert.Null(plan.Operation);
        }
        AssertRefusedWith("R03", PlanOf(new ViewCommand(ViewAction.FitSelected), doc, null));
        AssertRefusedWith("R14", PlanOf(new ViewCommand(ViewAction.FitSelected), doc, new Selection(new[] { doc.Pieces[0].Id, doc.Pieces[1].Id })));
        Assert.True(PlanOf(new ViewCommand(ViewAction.FitSelected), doc, Selection.Of(doc.Pieces[2].Id)).IsPlan);
    }

    [Fact]
    public void DetailedViewIsRefusedWhileADrawingDraftsOrAFileOperationBlockIt()
    {
        var doc = Standard();
        var command = new ViewCommand(ViewAction.DetailedView);
        AssertRefusedWith("R07", PlanOf(command, doc, null, new DocumentView(doc, Epoch) { DrawingInProgress = true }));
        AssertRefusedWith("R20", PlanOf(command, doc, null, new DocumentView(doc, Epoch) { HasUnappliedDrafts = true }));
        AssertRefusedWith("R09", PlanOf(command, doc, null, new DocumentView(doc, Epoch) { FileOperationInProgress = true }));
        Assert.True(PlanOf(command, doc, null).IsPlan);
    }

    [Fact]
    public void FileCommandsAreRefusedWhileAFileOperationRunsAndOpenOrNewAreDangerousWithUnsavedChanges()
    {
        var doc = Standard();
        var busy = new DocumentView(doc, Epoch) { FileOperationInProgress = true };
        foreach (var action in Enum.GetValues<FileAction>())
            AssertRefusedWith("R09", PlanOf(new FileCommand(action), doc, null, busy));

        var dirty = new DocumentView(doc, Epoch) { HasUnsavedChanges = true };
        Assert.Equal(PlanClass.Dangerous, AssertPlan(PlanOf(new FileCommand(FileAction.Open), doc, null, dirty)).Class);
        Assert.Equal(PlanClass.Dangerous, AssertPlan(PlanOf(new FileCommand(FileAction.New), doc, null, dirty)).Class);
        Assert.Equal(PlanClass.Window, AssertPlan(PlanOf(new FileCommand(FileAction.Open), doc, null)).Class);
        Assert.Equal(PlanClass.Window, AssertPlan(PlanOf(new FileCommand(FileAction.Save), doc, null, dirty)).Class);
    }

    [Fact]
    public void SaveNamesTheOpenFileOnlyWhenThereIsAUsableFileName()
    {
        var doc = Standard();
        Assert.Equal("Proje kaydedilecek: gomlek.tcad", AssertPlan(PlanOf(new FileCommand(FileAction.Save), doc, null, new DocumentView(doc, Epoch) { FileName = "  gomlek.tcad  " })).Preview);
        foreach (var name in new string?[] { null, "", "   " })
            Assert.Equal("Proje kaydedilecek; dosya adı sorulacak.", AssertPlan(PlanOf(new FileCommand(FileAction.Save), doc, null, new DocumentView(doc, Epoch) { FileName = name })).Preview);
    }

    [Fact]
    public void HandoffsThatNeedAPieceAreRefusedWithoutOneAndPlannedWithOne()
    {
        var doc = Standard();
        var selection = Selection.Of(doc.Pieces[0].Id);
        foreach (var target in new[] { HandoffTarget.Duplicate, HandoffTarget.ExportSelectedDxf, HandoffTarget.ExportSelectedA4, HandoffTarget.Grainline, HandoffTarget.PieceMetadata, HandoffTarget.MirrorCopy })
        {
            AssertRefusedWith("R03", PlanOf(new HandoffCommand(target), doc, null));
            AssertRefusedWith("R14", PlanOf(new HandoffCommand(target), doc, new Selection(new[] { doc.Pieces[0].Id, doc.Pieces[1].Id })));
            var plan = AssertPlan(PlanOf(new HandoffCommand(target), doc, selection));
            Assert.Equal(PlanKind.Window, plan.Kind);
            Assert.Equal(doc.Pieces[0].Id, plan.TargetPieceId);
            Assert.True(plan.NeedsSelectedPiece);
        }
    }

    [Fact]
    public void HandoffsThatNeedNoPieceAreAlwaysPlanned()
    {
        var empty = new ProjectDocument();
        foreach (var target in new[] { HandoffTarget.ImportDxf, HandoffTarget.Export, HandoffTarget.MachineCenter, HandoffTarget.Layers, HandoffTarget.About, HandoffTarget.DesignRequest, HandoffTarget.RecoveryScan, HandoffTarget.CoordinateDrawing })
        {
            var plan = AssertPlan(PlanOf(new HandoffCommand(target), empty, null));
            Assert.Null(plan.TargetPieceId);
            Assert.False(plan.NeedsSelectedPiece);
        }
        Assert.Equal(PlanClass.Dangerous, AssertPlan(PlanOf(new HandoffCommand(HandoffTarget.Export), empty, null)).Class);
        Assert.Equal(PlanClass.NoChange, AssertPlan(PlanOf(new HandoffCommand(HandoffTarget.Layers), empty, null)).Class);
    }

    [Fact]
    public void TextileToolsTabsNeedTheRightThingsAndShowTheirValue()
    {
        var doc = Standard();
        var empty = new ProjectDocument();
        var selection = Selection.Of(doc.Pieces[0].Id);

        // Tab 3 (marker) needs pieces in the project, not a selection; the others need a selected piece.
        AssertRefusedWith("R03", PlanOf(new HandoffCommand(HandoffTarget.TextileTools, 3), empty, null));
        Assert.True(PlanOf(new HandoffCommand(HandoffTarget.TextileTools, 3), doc, null).IsPlan);
        foreach (var tab in new int?[] { 1, 2, null, 99 })
            AssertRefusedWith("R03", PlanOf(new HandoffCommand(HandoffTarget.TextileTools, tab), doc, null));

        Assert.Contains("10 mm (1 cm)", AssertPlan(PlanOf(new HandoffCommand(HandoffTarget.TextileTools, 2, 10), doc, selection)).Preview);
        Assert.Contains("1500 mm (150 cm)", AssertPlan(PlanOf(new HandoffCommand(HandoffTarget.TextileTools, 3, 1500), doc, null)).Preview);
        foreach (var bad in new double?[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var seam = AssertPlan(PlanOf(new HandoffCommand(HandoffTarget.TextileTools, 2, bad), doc, selection));
            var marker = AssertPlan(PlanOf(new HandoffCommand(HandoffTarget.TextileTools, 3, bad), doc, null));
            Assert.DoesNotContain("?", seam.Preview);
            Assert.DoesNotContain("?", marker.Preview);
            Assert.DoesNotContain("mm (", seam.Preview);
        }
    }

    [Fact]
    public void MovingSeveralPiecesNeedsAtLeastTwoPieces()
    {
        var one = PieceOperationTestData.Doc(Rect("A", 0, 0, 10, 10));
        var result = PlanOf(new HandoffCommand(HandoffTarget.GroupMove), one, null);
        Assert.False(result.IsPlan, result.ToString());
        AssertSound(result, ViewOf(one), null, null, "group move with one piece");
        Assert.True(PlanOf(new HandoffCommand(HandoffTarget.GroupMove), Standard(), null).IsPlan);
    }

    [Fact]
    public void HelpAndCancelPreviewNeverTouchTheProject()
    {
        var doc = Standard();
        foreach (var command in new Command[] { new HelpCommand(), new CancelPreviewCommand() })
        {
            var plan = AssertPlan(PlanOf(command, doc, null, new DocumentView(doc, Epoch) { DrawingInProgress = true, RecoveryIncomplete = true, FileOperationInProgress = true }));
            Assert.Equal(PlanClass.NoChange, plan.Class);
            Assert.False(plan.AppliesInCore);
            Assert.Equal(StampScope.None, plan.Stamp.Scope);
        }
        Assert.Equal("Önizleme kapatılacak. Hiçbir şey değişmedi.", AssertPlan(PlanOf(new CancelPreviewCommand(), doc, null)).Preview);
    }

    // ================================================================================================================
    // Duplicate entry point
    // ================================================================================================================

    [Fact]
    public void PlanDuplicateCopiesTheSelectedPieceAndTheCopyCanBeAppliedToAnEqualDocument()
    {
        var doc = Standard();
        var selection = Selection.Of(doc.Pieces[0].Id);
        var view = ViewOf(doc);
        var plan = AssertPlan(CommandPlanner.PlanDuplicate(view, selection));
        Assert.Equal(PlanKind.DuplicatePiece, plan.Kind);
        Assert.True(plan.AddsPiece);
        Assert.True(plan.AppliesInCore);
        Assert.Contains("Ön beden kopya", plan.Preview);
        AssertSound(CommandPlanner.PlanDuplicate(view, selection), view, selection, null, "duplicate");

        var clone = doc.Clone();
        plan.Apply(clone);
        Assert.Equal(4, clone.Pieces.Count);
        Assert.Equal(3, doc.Pieces.Count);
        Assert.Equal("Ön beden kopya", clone.Pieces.Single(p => p.Id == plan.CreatedPieceId).Name);
    }

    [Fact]
    public void PlanDuplicateRefusesBadOffsetsAndBadSelectionsWithoutThrowing()
    {
        var doc = Standard();
        var view = ViewOf(doc);
        var selection = Selection.Of(doc.Pieces[0].Id);
        foreach (var (x, y) in new (double, double)[] { (double.NaN, 0), (0, double.PositiveInfinity), (1e300, 0), (1e9, 1e9), (0, 0), (-20, -20) })
            AssertSound(CommandPlanner.PlanDuplicate(view, selection, x, y), view, selection, null, $"duplicate offset {x}, {y}");
        AssertRefusedWith("R03", CommandPlanner.PlanDuplicate(view, Selection.None));
        AssertRefusedWith("R03", CommandPlanner.PlanDuplicate(view, null));
        AssertRefusedWith("R14", CommandPlanner.PlanDuplicate(view, new Selection(new[] { doc.Pieces[0].Id, doc.Pieces[1].Id })));
        AssertRefusedWith("R09", CommandPlanner.PlanDuplicate(new DocumentView(doc, Epoch) { FileOperationInProgress = true }, selection));

        var locked = new Layer { Name = "Kilitli", Locked = true };
        var lockedDoc = PieceOperationTestData.WithLayers(Standard(), locked);
        lockedDoc.Pieces[0].LayerId = locked.Id;
        AssertRefusedWith("R08", CommandPlanner.PlanDuplicate(ViewOf(lockedDoc), Selection.Of(lockedDoc.Pieces[0].Id)));
    }

    // ================================================================================================================
    // Plans and stamps: applying and going stale
    // ================================================================================================================

    [Fact]
    public void RectanglePlanAppliesToAnEqualCopyAndRefusesAChangedDocument()
    {
        var doc = Standard();
        var plan = AssertPlan(PlanOf(new RectangleCommand(120, 80), doc, null));
        Assert.True(plan.AddsPiece);
        Assert.NotNull(plan.CreatedPieceId);

        var changed = doc.Clone();
        changed.Pieces[0].Name = "Başka ad";
        Assert.ThrowsAny<InvalidOperationException>(() => plan.Apply(changed));

        var equal = doc.Clone();
        plan.Apply(equal);
        Assert.Equal(4, equal.Pieces.Count);
        Assert.Equal(3, doc.Pieces.Count);
        Assert.Contains(equal.Pieces, p => p.Id == plan.CreatedPieceId);
    }

    [Fact]
    public void PlansThatTheDesktopAppliesRefuseToBeAppliedInCore()
    {
        var doc = Standard();
        var selection = Selection.Of(doc.Pieces[0].Id);
        foreach (var command in new Command[] { new MoveByCommand(25, 0), new MoveToCommand(100, 100), new RotateCommand(90, RotationDirection.Clockwise), new MirrorCommand(MirrorAxis.Vertical), new SelectCommand(PieceTarget.Next), new UndoCommand(1) })
        {
            var plan = AssertPlan(PlanOf(command, doc, selection));
            Assert.False(plan.AppliesInCore);
            Assert.Null(plan.EditedDocument());
            Assert.ThrowsAny<InvalidOperationException>(() => plan.Apply(doc.Clone()));
        }
    }

    [Fact]
    public void StampDetectsEveryKindOfChangeAfterPlanning()
    {
        var doc = Standard();
        var selection = Selection.Of(doc.Pieces[0].Id);
        var view = ViewOf(doc);
        var plan = AssertPlan(PlanOf(new MoveByCommand(25, 0), doc, selection, view));
        var stamp = plan.Stamp;
        Assert.Equal(StampState.Current, stamp.Check(view, selection));

        Assert.Equal(StampState.EpochChanged, stamp.Check(new DocumentView(doc, Guid.NewGuid()) { UndoCount = 3 }, selection));
        Assert.Equal(StampState.DifferentDocument, stamp.Check(new DocumentView(doc.Clone(), Epoch) { UndoCount = 3 }, selection));
        Assert.Equal(StampState.SelectionChanged, stamp.Check(view, Selection.Of(doc.Pieces[1].Id)));
        Assert.Equal(StampState.SelectionChanged, stamp.Check(view, Selection.None));

        doc.Pieces[0].Name = "Değişti";
        Assert.Equal(StampState.ContentChanged, stamp.Check(view, selection));
        doc.Pieces[0].Name = "Ön beden";
        Assert.Equal(StampState.Current, stamp.Check(view, selection));

        var other = doc.Pieces[1];
        other.Name = "Başka parçanın adı";
        Assert.Equal(StampState.Current, stamp.Check(view, selection)); // a one-piece stamp ignores other pieces

        var removed = doc.Pieces[0];
        doc.Pieces.Remove(removed);
        Assert.Equal(StampState.PieceMissing, stamp.Check(view, selection));
    }

    [Fact]
    public void StampOfAWholeDocumentPlanNoticesAChangeToAnyPiece()
    {
        var doc = Standard();
        var view = ViewOf(doc);
        var plan = AssertPlan(PlanOf(new RectangleCommand(120, 80), doc, null, view));
        Assert.Equal(StampScope.Document, plan.Stamp.Scope);
        Assert.True(plan.Stamp.IsCurrent(view, null));
        doc.Pieces[2].Points[0] = new Point2(1, 1);
        Assert.False(plan.Stamp.IsCurrent(view, null));
        doc.Pieces[2].Points[0] = new Point2(1020, 20);
        Assert.True(plan.Stamp.IsCurrent(view, null));
        doc.Layers = [.. Layer.Defaults(), new Layer { Name = "Yeni" }];
        Assert.False(plan.Stamp.IsCurrent(view, null));
    }
}
