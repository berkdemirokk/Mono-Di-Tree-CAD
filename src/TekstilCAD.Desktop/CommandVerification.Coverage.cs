using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TekstilCAD.Core;
using TekstilCAD.Core.Commands;
using TekstilCAD.Core.Theme;
using TekstilCAD.FileFormats;
using static TekstilCAD.Desktop.DesktopVerification;

namespace TekstilCAD.Desktop;

// Every command of FINAL_COMMANDS.md (C01-C66) is typed into a fresh window, planned and applied end to end. A command that opens a
// dialog is applied with the hand-off seam: the executor hands over the key and the guarded method it would run (the same method the
// button runs), so no window ever shows. A command the executor cannot run must be on RefusedByDesign with a reason; a command that
// is on neither list (a new planner command, hand-off target or view action) fails the completeness check below.
internal static partial class CommandVerification
{
    /// <param name="Id">The command number in FINAL_COMMANDS.md (a row without one is a phrase the specification lists under another command).</param>
    /// <param name="Fixture">The project and window state the phrase needs.</param>
    /// <param name="Seam">The key the executor must hand to the seam; null when the command runs without a dialog.</param>
    /// <param name="Method">The guarded method the hand-off must run (null when the hand-off passes arguments).</param>
    /// <param name="After">Extra check of the real effect.</param>
    private sealed record CoverageRow(string Id, string Text, string Fixture, string? Seam = null, string? Method = null, bool Confirm = false,
        Func<MainWindow, CommandOutcome, bool>? After = null);

    /// <summary>Commands the executor refuses on purpose, with the reason. Empty: every command has an existing method to run.</summary>
    private static readonly Dictionary<string, string> RefusedByDesign = new();

    private static readonly CoverageRow[] CoverageRows =
    [
        new("C01", "120 x 80 dikdörtgen", "empty"),
        new("-", "yamuk alt 60 cm üst 40 cm yükseklik 50 cm", "empty",
            After: (window, _) => window.Document.Pieces.Single().Points.SequenceEqual(
                new Point2[] { new(120,20),new(520,20),new(620,520),new(20,520) })),
        new("C02", "kare 100", "empty"),
        new("C03", "150 çizgi", "empty"),
        new("C04", "80 dikey çizgi", "empty"),
        new("C05", "25 sağa taşı", "piece"),
        new("C06", "20 sağa 10 yukarı taşı", "piece"),
        new("C07", "x 100 y 50 konumuna taşı", "piece"),
        new("C08", "döndür", "piece"),
        new("C09", "90 sağa döndür", "piece"),
        new("C10", "sola döndür", "piece"),
        new("C11", "yarım tur", "piece"),
        new("C12", "aynala", "piece"),
        new("C13", "alt üst aynala", "piece"),
        new("C14", "genişliği 95 yap", "plain"),
        new("C15", "boyu 60 yap", "plain"),
        new("C16", "95 x 60 yap", "plain"),
        new("-", "1 kenara 10 mm ağızlı 5 mm derinlikte pens ekle", "piece",
            After: (window, _) => window.Document.Pieces.Single().Darts is { Count: 1 } &&
                window.Document.Pieces.Single().Points.SequenceEqual(new Point2[] { new(0,0),new(20,0),new(20,10),new(0,10) })),
        new("C17", "adı kol olsun", "plain"),
        new("-", "projenin adı Yaz Koleksiyonu olsun", "empty",
            After: (window, _) => window.Document.Name == "Yaz Koleksiyonu"),
        new("C18", "çoğalt", "piece", "Handoff.Duplicate", "DuplicateSelectedPiece", true),
        new("C19", "sil", "piece", Confirm: true),
        new("C20", "kolu sil", "two", Confirm: true),
        new("C21", "kolu seç", "two"),
        new("C22", "sonraki parçayı seç", "two"),
        new("C23", "seçimi bırak", "two"),
        new("C24", "geri al", "undo"),
        new("C25", "3 adım geri", "undo"),
        new("C26", "yinele", "redo"),
        new("C27", "tümünü göster", "piece"),
        new("C28", "seçiliyi göster", "piece"),
        new("C29", "yakınlaştır", "piece"),
        new("C30", "uzaklaştır", "piece"),
        new("C31", "görünümü sola kaydır", "piece"),
        new("C31", "görünümü sağa kaydır", "piece"),
        new("C31", "görünümü yukarı kaydır", "piece"),
        new("C31", "görünümü aşağı kaydır", "piece"),
        new("C32", "seç aracı", "piece"),
        new("C33", "çizgi aracı", "piece"),
        new("C34", "nokta nokta çiz", "piece"),
        new("C35", "dikdörtgen aracı", "piece"),
        new("C36", "eğri aracı", "piece"),
        new("C37", "yay aracı", "piece"),
        new("C38", "çizimi kapat", "drawing"),
        new("C39", "çizimi açık bırak", "drawing"),
        new("C40", "çizimi iptal et", "drawing", Confirm: true),
        new("C41", "son noktayı sil", "drawing"),
        new("C42", "koordinat gir", "tool", "Handoff.CoordinateDrawing", "OpenCoordinateDrawing", true),
        new("C43", "kaydet", "piece", "File.Save", "Save", true),
        new("C44", "farklı kaydet", "piece", "File.SaveAs", "SaveAs", true),
        new("C45", "yedeğini al", "piece", "File.SaveCopy", "SaveNewCopy", true),
        new("C46", "aç", "piece", "File.Open", "Open", true),
        new("C47", "yeni", "piece", "File.New", "NewDocument", true),
        new("C48", "dxf al", "empty", "Handoff.ImportDxf", "ImportDxf", true),
        new("C49-A4", "yazdır", "piece", "Handoff.ExportProjectA4", "ExportProjectA4", true),
        new("C49", "dışa aktar", "piece", "Handoff.Export", "Export", true),
        new("C49-PLT", "plotterda bastır", "piece", "Handoff.ExportHpgl", "ExportPlotter", true),
        new("C50", "seçili parçayı dxf yap", "piece", "Handoff.ExportSelectedDxf", "ExportSelectedDxf", true),
        new("C51", "a4 çıktı", "piece", "Handoff.ExportSelectedA4", "ExportSelectedA4", true),
        new("C52", "1 cm dikiş payı", "piece", "Handoff.TextileTools", null, true),
        new("C53", "bir beden büyüt", "piece", "Handoff.TextileTools", null, true),
        new("C54", "pastal yap", "piece", "Handoff.TextileTools", null, true),
        new("C55", "makine dosyası hazırla", "empty", "Handoff.MachineCenter", "OpenMachineCenter", true),
        new("C56", "iplik yönü", "piece", "Handoff.Grainline", "EditGrainline", true),
        new("C57", "parça bilgileri", "piece", "Handoff.PieceMetadata", "EditPieceMetadata", true),
        new("-", "eğri noktalarını düzenle", "analytic", "Handoff.ParametricCurveEdit", "EditParametricContour", true),
        new("C58", "karşı parçasını çıkar", "piece", "Handoff.MirrorCopy", "OpenMirrorCopy", true),
        new("C59", "parçaları taşı", "two", "Handoff.GroupMove", "OpenGroupMove", true),
        new("C60", "yeni katman", "simple", After: (window, _) => !window.IsSimpleView),
        new("C61", "yardım", "empty", After: (_, outcome) => outcome.OpensExamples && outcome.Message.Length > 0),
        new("C62", "hakkında", "empty", "Handoff.About", "OpenAbout", true),
        new("C63", "ayrıntılı görünüm", "simple", After: (window, _) => !window.IsSimpleView),
        new("C64", "kurtarma tara", "empty", "Handoff.RecoveryScan", "RescanRecovery", true),
        new("C65", "elbise isteği yaz", "empty", "Handoff.DesignRequest", "OpenDesignRequest", true),
        new("C66", "koyu tema", "empty", After: (_, _) => ReferenceEquals(StudioTheme.Current, ThemePalette.Dark) && SavedPreferences().Theme == "Dark"),
        new("C66", "açık tema", "empty", After: (_, _) => ReferenceEquals(StudioTheme.Current, ThemePalette.Light) && SavedPreferences().Theme == "Light"),
        new("C66", "yazıyı büyüt", "simple", After: (_, outcome) => SavedPreferences().TextScale == 115 && outcome.Message.Contains("115")),
        new("C66", "yazıyı küçült", "simple115", After: (_, outcome) => SavedPreferences().TextScale == 100 && outcome.Message.Contains("100")),
        new("-", "tüm araçlar", "simple", After: (window, _) => window.IsAllToolsSheetOpen),
        new("-", "vazgeç", "empty", After: (_, outcome) => outcome.Message.Length > 0),
    ];

    private static UiPreferences SavedPreferences() => UiPreferences.Load(AppPaths.UiPreferencesPath);

    private static string CoverageKey(Command command) => command switch
    {
        ViewCommand view => "View." + view.Action,
        HandoffCommand hand => "Handoff." + hand.Target,
        FileCommand file => "File." + file.Action,
        ToolCommand tool => "Tool." + tool.Tool,
        FinishDrawingCommand finish => "Finish." + finish.Action,
        _ => command.GetType().Name,
    };

    private static void PrepareCoverageWindow(MainWindow window, string fixture)
    {
        PatternPiece Plain(string name, double left) => new() { Name = name, Points = [new(left, 10), new(left + 100, 10), new(left + 100, 90), new(left, 90)] };
        switch (fixture)
        {
            case "empty": window.LoadDocument(new ProjectDocument()); break;
            case "piece" or "undo" or "redo" or "tool" or "drawing" or "simple" or "simple115":
                var piece = MarkedPiece();
                window.LoadDocument(new ProjectDocument { Pieces = [piece], GradingRules = [RuleFor(piece)] });
                window.Select(piece.Id);
                break;
            case "plain":
                var rectangle = Plain("Düz", 10);
                window.LoadDocument(new ProjectDocument { Pieces = [rectangle] });
                window.Select(rectangle.Id);
                break;
            case "two":
                var first = Plain("Ön beden", 10);
                window.LoadDocument(new ProjectDocument { Pieces = [first, Plain("Kol", 200)] });
                window.Select(first.Id);
                break;
            case "analytic":
                var bezier = new CurveSegment.CubicBezier(new(0, 0), new(0, 30), new(100, 30), new(100, 0));
                var curves = new CurveSegment[] { bezier };
                var analytic = new PatternPiece { Name = "Eğri", Closed = false, Curves = [.. curves], Points = ParametricContour.Sample(curves) };
                window.LoadDocument(new ProjectDocument { Pieces = [analytic] });
                window.Select(analytic.Id);
                break;
        }
        Layout(window);
        if (fixture is "undo" or "redo")
        {
            for (var i = 0; i < 3; i++) window.ApplyCommand(PlanOf(window, "25 sağa taşı"));
            if (fixture == "redo") window.ApplyCommand(PlanOf(window, "geri al"));
        }
        if (fixture == "tool") window.SetTool("Çizgi");
        if (fixture == "drawing")
        {
            window.SetTool("Çoklu çizgi");
            Field<CadCanvas>(window, "canvas").RestorePendingDrawing(new RecoveryPendingDrawing("Çoklu çizgi", [new Point2(0, 0), new Point2(50, 0), new Point2(50, 50)]));
        }
        if (fixture is "simple" or "simple115")
        {
            if (!window.TrySetSimpleView(true, out _)) throw new InvalidOperationException("The simple view did not open.");
            if (fixture == "simple115") window.ApplySimpleTextScale(115);
        }
    }

    /// <summary>Runs one row in a fresh window; returns what is wrong, or null.</summary>
    private static string? RunCoverageRow(CoverageRow row, HashSet<string> reached)
    {
        string? problem = null;
        Scenario(window =>
        {
            PrepareCoverageWindow(window, row.Fixture);
            (string Key, Action Run)? handed = null;
            window.HandoffForVerification = (key, run) => handed = (key, run);
            var preview = window.PreviewCommand(row.Text, true);
            if (preview.Plan is not { } plan) { problem = $"{row.Id} “{row.Text}” has no plan: {preview.Text}"; return; }
            reached.Add(CoverageKey(plan.Command));
            var outcome = window.ApplyCommand(plan, row.Confirm || plan.RequiresConfirmation);
            if (outcome.Result is not (CommandResult.Applied or CommandResult.Done)) { problem = $"{row.Id} “{row.Text}” was {outcome.Result} ({outcome.Guard}): {outcome.Message}"; return; }
            if (row.Seam is null ? handed is not null : handed?.Key != row.Seam) { problem = $"{row.Id} “{row.Text}” reached {handed?.Key ?? "no dialog method"}, expected {row.Seam ?? "none"}"; return; }
            if (row.Method is not null && handed?.Run.Method.Name != row.Method) { problem = $"{row.Id} “{row.Text}” would run {handed?.Run.Method.Name}, expected {row.Method}"; return; }
            if (row.After is { } after && !after(window, outcome)) problem = $"{row.Id} “{row.Text}” did not have its effect (message: {outcome.Message})";
        });
        return problem;
    }

    // The light/dark button in the simple view header uses the same helper as the About window and saves the choice.
    private static void VerifyThemeButton(List<string> checks)
    {
        var preferencesPath = AppPaths.UiPreferencesPath;
        var savedBytes = File.Exists(preferencesPath) ? File.ReadAllBytes(preferencesPath) : null;
        try
        {
            Scenario(window =>
            {
                static Button? Find(MainWindow window) => Descendants<Button>((FrameworkElement)window.Content)
                    .SingleOrDefault(b => AutomationProperties.GetName(b) is "Koyu temaya geç" or "Açık temaya geç");
                Check(Find(window) is null, "The theme button does not exist in the detailed layout", checks);
                Check(window.TrySetSimpleView(true, out _), "The simple view opens for the theme button check", checks);
                Layout(window);
                var button = Find(window);
                var about = Field<Button>(window, "aboutButton");
                Check(button is not null && Equals(button.Content, "Açık tema") && ReferenceEquals(button.Parent, about.Parent) && button.IsVisible == about.IsVisible,
                    "The simple view header shows a theme button next to the other header buttons, labelled with the theme a click gives", checks);
                button!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(ReferenceEquals(StudioTheme.Current, ThemePalette.Light) && Equals(button.Content, "Koyu tema") && SavedPreferences().Theme == "Light",
                    "The theme button switches to the light palette, relabels itself and saves the choice", checks);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(ReferenceEquals(StudioTheme.Current, ThemePalette.Dark) && Equals(button.Content, "Açık tema") && SavedPreferences().Theme == "Dark",
                    "The theme button switches back to the dark palette and saves it", checks);
                var about2 = new AboutWindow(window);
                about2.SwitchTheme();
                Check(ReferenceEquals(StudioTheme.Current, ThemePalette.Light) && Equals(button.Content, "Koyu tema") && SavedPreferences().Theme == "Light",
                    "The About window's theme switch and the header button go through the same code and keep each other's label current", checks);
                Check(window.TrySetSimpleView(false, out _) && Find(window) is null, "The theme button leaves with the simple view", checks);
            });
        }
        finally
        {
            StudioTheme.Use(ThemePalette.Dark);
            if (savedBytes is not null) File.WriteAllBytes(preferencesPath, savedBytes); else if (File.Exists(preferencesPath)) File.Delete(preferencesPath);
        }
    }

    private static void VerifyCommandCoverage(List<string> checks, string? reportPath)
    {
        windowsStable = true;
        var preferencesPath = AppPaths.UiPreferencesPath;
        var savedBytes = File.Exists(preferencesPath) ? File.ReadAllBytes(preferencesPath) : null;
        var problems = new List<string>();
        var reached = new HashSet<string>();
        try
        {
            foreach (var row in CoverageRows)
                if (RunCoverageRow(row, reached) is { } problem) problems.Add(problem);
        }
        finally
        {
            // Rows change the colours and the text size; verification expects the dark palette and its own preference file.
            StudioTheme.Use(ThemePalette.Dark);
            if (savedBytes is not null) File.WriteAllBytes(preferencesPath, savedBytes); else if (File.Exists(preferencesPath)) File.Delete(preferencesPath);
        }
        Check(problems.Count == 0, "Every typed command of FINAL_COMMANDS.md runs end to end in a fresh window: " + string.Join(" | ", problems), checks);
        Check(windowsStable, "Applying the commands opened no window and no message box", checks);

        var ids = CoverageRows.Select(row => row.Id).Where(id => id != "-").ToHashSet();
        var missingIds = Enumerable.Range(1, 66).Select(number => $"C{number:00}").Where(id => !ids.Contains(id) && !RefusedByDesign.ContainsKey(id)).ToList();
        Check(missingIds.Count == 0, "Every command id C01-C66 is executed or on the refused list; missing: " + string.Join(", ", missingIds), checks);

        // A new command kind, hand-off target, view action, file action, tool or drawing action needs a row (or a justified refusal).
        var expected = typeof(Command).Assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(Command)) && !type.IsAbstract)
            .SelectMany(type => type switch
            {
                _ when type == typeof(ViewCommand) => Enum.GetValues<ViewAction>().Select(value => "View." + value),
                _ when type == typeof(HandoffCommand) => Enum.GetValues<HandoffTarget>().Select(value => "Handoff." + value),
                _ when type == typeof(FileCommand) => Enum.GetValues<FileAction>().Select(value => "File." + value),
                _ when type == typeof(ToolCommand) => Enum.GetValues<DrawingTool>().Select(value => "Tool." + value),
                _ when type == typeof(FinishDrawingCommand) => Enum.GetValues<FinishDrawingAction>().Select(value => "Finish." + value),
                _ => [type.Name],
            }).ToList();
        var uncovered = expected.Where(key => !reached.Contains(key) && !RefusedByDesign.ContainsKey(key)).ToList();
        Check(uncovered.Count == 0, "Every planner command kind has an executed row or a justified refusal; uncovered: " + string.Join(", ", uncovered), checks);
        Check(RefusedByDesign.Keys.All(key => !reached.Contains(key)), "No command is both executed and on the refused list", checks);
        VerifyParametricCurveEdit(checks, reportPath);

        // Stage 3.4: acceptance-first for non-rectangular, unmarked closed polygons.
        // Open the window's own command box, not a detached test control.
        Scenario(window =>
        {
            window.LoadDocument(new ProjectDocument());
            window.CommandBoxHostForVerification = () => new RecordingHost();
            var opened = window.HandleCommandBoxShortcut(System.Windows.Input.ModifierKeys.Control, System.Windows.Input.Key.K);
            var view = window.CommandBoxView;
            Check(opened && view is not null, "Polygon resize: Ctrl+K opens the main window command box", checks);
            if (!opened || view is null) return;
            view.AutoPreview = false;
            Type(view, "yamuk alt 60 cm üst 40 cm yükseklik 50 cm");
            Enter(view);
            Enter(view);
            var session = SessionOf(window);
            Point2[] original = [new(120,20), new(520,20), new(620,520), new(20,520)];
            var created = window.Document.Pieces.Count == 1 &&
                window.Document.Pieces[0].Closed &&
                window.Document.Pieces[0].Points.SequenceEqual(original) &&
                window.Selected?.Id == window.Document.Pieces[0].Id;
            Check(created, "Polygon resize: the typed trapezoid creates the expected closed contour", checks);
            if (!created) return;
            var id = window.Document.Pieces[0].Id;
            var before = window.Document.Clone();
            var undoCount = session.UndoCount;
            Type(view, "30 x 25 cm yap");
            Enter(view);
            var previewed = view.State == CommandBarState.Preview &&
                DocumentSession.ContentEquals(before, window.Document) && session.UndoCount == undoCount;
            Check(previewed, "Polygon resize: a typed explicit-unit resize previews without changing the source", checks);
            if (!previewed) return;
            Enter(view);
            Point2[] expected = [new(70,20), new(270,20), new(320,270), new(20,270)];
            var applied = window.Document.Pieces.Count == 1 &&
                window.Document.Pieces[0].Id == id && window.Document.Pieces[0].Closed &&
                window.Document.Pieces[0].Points.SequenceEqual(expected) &&
                window.Selected?.Id == id && session.UndoCount == undoCount + 1;
            Check(applied, "Polygon resize: 300 x 250 mm contour keeps its anchor, identity and point order in one undo step", checks);
            if (!applied) return;
            var resized = window.Document.Clone();
            view.SetText("");
            var undone = view.HandleKey(System.Windows.Input.Key.Z, System.Windows.Input.ModifierKeys.Control);
            Check(undone && DocumentSession.ContentEquals(before, window.Document) && session.UndoCount == undoCount,
                "Polygon resize: Ctrl+Z restores the exact original document", checks);
            var redone = view.HandleKey(System.Windows.Input.Key.Y, System.Windows.Input.ModifierKeys.Control);
            Check(redone && DocumentSession.ContentEquals(resized, window.Document) && session.UndoCount == undoCount + 1,
                "Polygon resize: Ctrl+Y restores the exact resized document", checks);
        });

        // Stage 3.4 discoverability: the real command box must suggest a supported edit for a selected polygon.
        Scenario(window =>
        {
            var polygon = new PatternPiece { Name = "Yamuk", Points = [new(120,20),new(520,20),new(620,520),new(20,520)] };
            window.LoadDocument(new ProjectDocument { Pieces = [polygon] });
            window.Select(polygon.Id);
            window.CommandBoxHostForVerification = () => new RecordingHost();
            window.OpenCommandBox();
            var view = window.CommandBoxView!;
            view.AutoPreview = false;
            var before = window.Document.Clone();
            var session = SessionOf(window);
            var undoCount = session.UndoCount;
            view.RefreshChips();
            var chip = view.ChipButtons.SingleOrDefault(button => Equals(button.Tag, "genişliği 95 yap"));
            Check(chip is not null && chip.Focusable && chip.IsEnabled &&
                !string.IsNullOrWhiteSpace(AutomationProperties.GetName(chip)),
                "Polygon resize UI: the main command box offers an accessible resize suggestion for a valid selected polygon", checks);
            if (chip is null) return;
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(view.State == CommandBarState.Preview &&
                view.ApplyButton.Visibility == Visibility.Visible && view.CancelButton.Visibility == Visibility.Visible &&
                DocumentSession.ContentEquals(before, window.Document) && session.UndoCount == undoCount,
                "Polygon resize UI: clicking the suggestion opens a cancellable preview without editing the source", checks);
            view.HandleKey(System.Windows.Input.Key.Escape, System.Windows.Input.ModifierKeys.None);
            Check(DocumentSession.ContentEquals(before, window.Document) && session.UndoCount == undoCount,
                "Polygon resize UI: Escape leaves the document and history unchanged", checks);
        });

        // The suggestion must not imply an unsupported edit on marked, locked or open contours.
        foreach (var unsupported in new[] { "metadata", "grainline", "locked", "open" })
            Scenario(window =>
            {
                var polygon = new PatternPiece { Name = "Yamuk", Points = [new(120,20),new(520,20),new(620,520),new(20,520)] };
                var doc = new ProjectDocument { Pieces = [polygon] };
                if (unsupported == "metadata") polygon.Metadata = new PieceMetadata { Size = "38" };
                if (unsupported == "grainline") polygon.Grainline = new Grainline { Start = new(200,100), End = new(200,300) };
                if (unsupported == "open") polygon.Closed = false;
                if (unsupported == "locked") doc.Layers = [new Layer { Id = Layer.DefaultId, Name = "Kilitli", Locked = true }];
                window.LoadDocument(doc);
                window.Select(polygon.Id);
                window.CommandBoxHostForVerification = () => new RecordingHost();
                window.OpenCommandBox();
                var view = window.CommandBoxView!;
                view.AutoPreview = false;
                view.RefreshChips();
                Check(!view.ChipButtons.Any(button => Equals(button.Tag, "genişliği 95 yap")),
                    "Polygon resize UI: no misleading resize suggestion for " + unsupported, checks);
            });

        // "çoğalt" end to end through the existing duplicate seam: the real method runs and stops at its own choice step.
        Scenario(window =>
        {
            PrepareCoverageWindow(window, "piece");
            var asked = false;
            window.ChooseDuplicateForVerification = _ => { asked = true; return null; };
            var before = window.Document.Clone();
            var outcome = window.ApplyCommand(PlanOf(window, "çoğalt"));
            Check(asked && outcome.Result == CommandResult.Done && DocumentSession.ContentEquals(before, window.Document) && SessionOf(window).UndoCount == 0,
                "The “çoğalt” command reaches DuplicateSelectedPiece and its own choice step, and a cancelled choice changes nothing", checks);
        });
        // "yardım" through the box: Enter shows the preview, a second Enter answers with a visible result and the example list.
        Scenario(window =>
        {
            window.LoadDocument(new ProjectDocument());
            var view = NewBox(window);
            Type(view, "yardım");
            Enter(view);
            Enter(view);
            Check(view.State == CommandBarState.Result && view.ExamplesOpen && view.RowText.Contains("Örnek listesi"),
                "The “yardım” command shows a result line and opens the example list in the command box", checks);
            Check(window.PreviewCommand("?", true).Plan?.Kind == PlanKind.Help, "A lone question mark is the help command", checks);
        });
    }

    private static void VerifyParametricCurveEdit(List<string> checks, string? reportPath)
    {
        var root = reportPath is null ? Path.Combine(Environment.CurrentDirectory, "work", "verify") : Path.GetDirectoryName(reportPath)!;
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "parametric-curve-edit-" + Guid.NewGuid().ToString("N") + ".tcad");
        try
        {
            Scenario(window =>
            {
                var bezier = new CurveSegment.CubicBezier(new(0, 0), new(0, 30), new(100, 30), new(100, 0));
                var curves = new CurveSegment[] { bezier };
                var piece = new PatternPiece { Name = "Eğri", Closed = false, Curves = [.. curves], Points = ParametricContour.Sample(curves) };
                window.LoadDocument(new ProjectDocument { Pieces = [piece] });
                window.Select(piece.Id);
                window.CommandBoxHostForVerification = () => new RecordingHost();
                var opened = window.HandleCommandBoxShortcut(System.Windows.Input.ModifierKeys.Control, System.Windows.Input.Key.K);
                var view = window.CommandBoxView;
                Check(opened && view is not null, "Curve edit: Ctrl+K opens the main window command box", checks);
                if (!opened || view is null) return;
                view.AutoPreview = false;
                var session = SessionOf(window);
                session.MarkSaved();
                var before = window.Document.Clone();
                var undoCount = session.UndoCount;
                var editorOpened = false;
                window.ParametricCurveEditorForVerification = editor =>
                {
                    editorOpened = editor.Title.Contains("Eğri", StringComparison.Ordinal);
                    editor.SetTextForVerification(0, "P1 Y", "40");
                    editor.ApplyForVerification();
                };
                Type(view, "eğri noktalarını düzenle");
                Enter(view);
                var planned = view.State == CommandBarState.Preview && DocumentSession.ContentEquals(before, window.Document) && session.UndoCount == undoCount;
                Check(planned, "Curve edit: the Turkish command previews without changing the analytic source", checks);
                if (!planned) return;
                Enter(view);
                var editedPiece = window.Document.Pieces.Single();
                var editedCurve = AssertCurveEditBezier(editedPiece);
                var edited = window.Document.Clone();
                var applied = editorOpened && editedPiece.Id == piece.Id && editedCurve == (bezier with { P1 = new Point2(0, 40) }) &&
                    ParametricContour.MatchesPreview(editedPiece) && session.UndoCount == undoCount + 1;
                Check(applied, "Curve edit: the existing editor changes a Bézier handle numerically as one undo step", checks);
                if (!applied) return;

                ProjectStorage.Save(edited, path);
                var reopened = ProjectStorage.Load(path);
                var roundTrip = !ReferenceEquals(edited, reopened) && DocumentSession.ContentEquals(edited, reopened) &&
                    AssertCurveEditBezier(reopened.Pieces.Single()) == editedCurve;
                Check(roundTrip, "Curve edit: saved and reopened analytic controls match the edited document", checks);

                view.SetText("");
                var undone = view.HandleKey(System.Windows.Input.Key.Z, System.Windows.Input.ModifierKeys.Control) &&
                    DocumentSession.ContentEquals(before, window.Document) && session.UndoCount == undoCount;
                Check(undone, "Curve edit: Ctrl+Z restores the exact original analytic controls", checks);
                var redone = view.HandleKey(System.Windows.Input.Key.Y, System.Windows.Input.ModifierKeys.Control) &&
                    DocumentSession.ContentEquals(edited, window.Document) && session.UndoCount == undoCount + 1;
                Check(redone, "Curve edit: Ctrl+Y restores the exact edited controls", checks);
            });

            foreach (var fixture in new[] { "straight", "locked" })
            {
                Scenario(window =>
                {
                    CurveSegment[] curves = fixture == "locked"
                        ? [new CurveSegment.CubicBezier(new(0, 0), new(0, 30), new(100, 30), new(100, 0))]
                        : [new CurveSegment.Line(new(0, 0), new(100, 0))];
                    var piece = new PatternPiece { Name = "Doğru", Closed = false, Curves = [.. curves], Points = ParametricContour.Sample(curves) };
                    var document = new ProjectDocument { Pieces = [piece] };
                    if (fixture == "locked") document.Layers = [new Layer { Id = Layer.DefaultId, Name = "Kilitli", Locked = true }];
                    window.LoadDocument(document);
                    window.Select(piece.Id);
                    window.CommandBoxHostForVerification = () => new RecordingHost();
                    window.OpenCommandBox();
                    var view = window.CommandBoxView!;
                    view.AutoPreview = false;
                    var before = window.Document.Clone();
                    var session = SessionOf(window);
                    var undoCount = session.UndoCount;
                    var selectedBefore = window.Selected?.Id;
                    var editorOpened = false;
                    window.ParametricCurveEditorForVerification = _ => editorOpened = true;
                    Type(view, "eğri noktalarını düzenle");
                    Enter(view);
                    Check(view.State == CommandBarState.Refusal && !editorOpened &&
                        DocumentSession.ContentEquals(before, window.Document) && session.UndoCount == undoCount &&
                        window.Selected?.Id == selectedBefore,
                        $"Curve edit: {fixture} contour is refused without opening the editor or changing selection, geometry or history", checks);
                });
            }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static CurveSegment.CubicBezier AssertCurveEditBezier(PatternPiece piece) =>
        piece.Curves is { Count: 1 } curves && curves[0] is CurveSegment.CubicBezier bezier
            ? bezier
            : throw new InvalidDataException("Expected one analytic cubic Bézier segment.");
}
