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
        new("C17", "adı kol olsun", "plain"),
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
        new("C49", "dışa aktar", "piece", "Handoff.Export", "Export", true),
        new("C50", "seçili parçayı dxf yap", "piece", "Handoff.ExportSelectedDxf", "ExportSelectedDxf", true),
        new("C51", "a4 çıktı", "piece", "Handoff.ExportSelectedA4", "ExportSelectedA4", true),
        new("C52", "1 cm dikiş payı", "piece", "Handoff.TextileTools", null, true),
        new("C53", "bir beden büyüt", "piece", "Handoff.TextileTools", null, true),
        new("C54", "pastal yap", "piece", "Handoff.TextileTools", null, true),
        new("C55", "makine dosyası hazırla", "empty", "Handoff.MachineCenter", "OpenMachineCenter", true),
        new("C56", "iplik yönü", "piece", "Handoff.Grainline", "EditGrainline", true),
        new("C57", "parça bilgileri", "piece", "Handoff.PieceMetadata", "EditPieceMetadata", true),
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

    private static void VerifyCommandCoverage(List<string> checks)
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
}
