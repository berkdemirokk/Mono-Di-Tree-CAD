using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TekstilCAD.Core.Commands;
using static TekstilCAD.Desktop.DesktopVerification;

namespace TekstilCAD.Desktop;

/// <summary>Headless checks of the "Tüm araçlar" page (PR 9). Same fresh, never shown windows as the other simple view checks.</summary>
internal static partial class SimpleViewVerification
{
    private static IEnumerable<T> Logical<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var nested in Logical<T>(child)) yield return nested;
        }
    }

    private static List<(CommandCatalogueEntry Entry, Button Row)> SheetRows(MainWindow window)
        => window.AllToolsSheetRoot is { } sheet ? Logical<Button>(sheet).Where(b => b.Tag is CommandCatalogueEntry).Select(b => ((CommandCatalogueEntry)b.Tag, b)).ToList() : [];

    private static void VerifyAllTools(string reportPath, List<string> checks)
    {
        VerifyAllToolsCoverage(checks);
        VerifyAllToolsClick(checks);
        VerifyAllToolsEnabledState(checks);
        VerifyAllToolsKeepsSideColumn(checks);
        VerifyAllToolsSearch(checks);
        VerifyAllToolsRenders(reportPath, checks);
    }

    // Every classic button belongs to exactly one entry (or the header list), and the page can reach each one.
    private static void VerifyAllToolsCoverage(List<string> checks) => WithFresh(fresh =>
    {
        var buttons = Logical<Button>((FrameworkElement)fresh.Content).ToList();
        var problems = new List<string>();
        foreach (var button in buttons)
        {
            var candidates = new[] { button.Content as string, AutomationProperties.GetName(button) }.Where(c => !string.IsNullOrEmpty(c)).ToHashSet(StringComparer.Ordinal);
            var owners = CommandCatalogue.Entries.Count(e => e.ClassicButtonLabel is { } label && candidates.Contains(label))
                + CommandCatalogue.HeaderButtons.Count(h => candidates.Contains(h.ClassicButtonLabel));
            if (owners != 1) problems.Add($"{(button.Content as string) ?? AutomationProperties.GetName(button)}: {owners}");
        }
        foreach (var entry in CommandCatalogue.Entries.Where(e => e.ClassicButtonLabel is not null))
        {
            var found = fresh.FindClassicControls(entry.ClassicButtonLabel, entry.ClassicAccessibleName).OfType<Button>().Count();
            if (found != 1) problems.Add($"entry {entry.Id}: {found} real buttons");
        }
        foreach (var entry in CommandCatalogue.Entries.Where(e => e.Kind == CommandCatalogueKind.Detailed))
            if (fresh.FindClassicControls(entry.DetailedControl, entry.ClassicButtonLabel).Count == 0) problems.Add($"entry {entry.Id}: control '{entry.DetailedControl}' not found");
        Check(buttons.Count == 68 && problems.Count == 0,
            "Every classic button appears in exactly one All tools entry or in the header list (68 buttons)"
            + (buttons.Count == 68 && problems.Count == 0 ? "" : $" [{buttons.Count} buttons; {string.Join("; ", problems.Take(8))}]"), checks);
    });

    // An entry raises the click of the real button and nothing else runs; a disabled real button blocks it.
    private static void VerifyAllToolsClick(List<string> checks) => WithFresh(fresh =>
    {
        Simple(fresh, true);
        fresh.Select(fresh.Document.Pieces[0].Id);
        Invoke(fresh, "Refresh");
        var counts = new Dictionary<string, int>();
        foreach (var entry in CommandCatalogue.Entries.Where(e => e.Kind == CommandCatalogueKind.Open))
        {
            var real = fresh.FindClassicControls(entry.ClassicButtonLabel, entry.ClassicAccessibleName).OfType<Button>().First();
            counts[entry.Id] = 0;
            var id = entry.Id;
            real.Click += (_, _) => counts[id]++;
        }
        Check(fresh.OpenAllToolsSheet() && fresh.IsAllToolsSheetOpen, "All tools page opens in the simple view", checks);
        var rows = SheetRows(fresh);
        var fit = rows.Single(r => r.Entry.Id == "view.fit-all").Row;
        fit.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var onlyFit = counts.Where(c => c.Value > 0).Select(c => c.Key + "=" + c.Value).ToList();
        var closed = !fresh.IsAllToolsSheetOpen;
        // A different entry: the one for undo, whose real button is disabled when nothing can be undone.
        fresh.OpenAllToolsSheet();
        var rowsAgain = SheetRows(fresh);
        var undoReal = fresh.FindClassicControls("↶", "Geri al").OfType<Button>().First();
        var undoRow = rowsAgain.Single(r => r.Entry.Id == "view.undo").Row;
        var undoWasEnabled = undoReal.IsEnabled;
        var before = counts["view.undo"];
        undoRow.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var undoClicks = counts["view.undo"] - before;
        Check(onlyFit.SequenceEqual(["view.fit-all=1"]) && closed && (undoWasEnabled ? undoClicks == 1 : undoClicks == 0),
            "All tools entry raises the click of the real button and runs no other handler"
            + (onlyFit.SequenceEqual(["view.fit-all=1"]) && closed ? "" : $" [{string.Join(", ", onlyFit)}; closed={closed}]"), checks);
        fresh.CloseAllToolsSheet();
    });

    // The entry follows the real button's enabled state, also while the page stays open and the selection changes.
    private static void VerifyAllToolsEnabledState(List<string> checks) => WithFresh(fresh =>
    {
        Simple(fresh, true);
        fresh.Select(null);
        Invoke(fresh, "Refresh");
        fresh.OpenAllToolsSheet();
        string Mismatches()
        {
            var bad = new List<string>();
            foreach (var (entry, row) in SheetRows(fresh).Where(r => r.Entry.Kind == CommandCatalogueKind.Open))
            {
                var real = fresh.FindClassicControls(entry.ClassicButtonLabel, entry.ClassicAccessibleName).OfType<Button>().First();
                if (row.IsEnabled != real.IsEnabled) bad.Add(entry.Id);
            }
            return string.Join(",", bad);
        }
        var rows = () => SheetRows(fresh).ToDictionary(r => r.Entry.Id, r => r.Row);
        var noneSelected = rows()["piece.duplicate"].IsEnabled == false && rows()["view.fit-all"].IsEnabled && Mismatches() == "";
        fresh.Select(fresh.Document.Pieces[0].Id);
        Invoke(fresh, "Refresh");
        var selected = rows()["piece.duplicate"].IsEnabled && Mismatches() == "";
        Check(noneSelected && selected, "All tools entry mirrors the enabled state of the real button" + (noneSelected && selected ? "" : $" [{Mismatches()}]"), checks);
        fresh.CloseAllToolsSheet();
    });

    // The page covers the rail and the canvas only; the side column keeps its piece card and its warnings.
    private static void VerifyAllToolsKeepsSideColumn(List<string> checks) => WithFresh(fresh =>
    {
        Simple(fresh, true);
        fresh.Select(fresh.Document.Pieces[0].Id);
        var grading = F<TextBlock>(fresh, "gradingGeometryNotice"); var limits = F<TextBlock>(fresh, "pieceLimits"); var conflict = F<TextBox>(fresh, "propertyDraftConflict");
        var notice = F<TextBlock>(fresh, "historyCapacityNotice");
        var reasons = new List<string>();
        // Each warning alone at the smallest window; all three together where the side column is taller (as the warnings-under-card check).
        var scenarios = new (int Width, int Height, bool G, bool L, bool C)[] { (1024, 640, true, false, false), (1024, 640, false, true, false), (1024, 640, false, false, true), (1440, 900, true, true, true) };
        foreach (var (width, height, showGrading, showLimits, showConflict) in scenarios)
        {
            fresh.OpenAllToolsSheet();
            Render(fresh, Path.ChangeExtension(ReportPath, $".simple-view-tmp-alltools-side-{width}x{height}.png"), width, height);
            grading.Text = showGrading ? "Kayıtlı serileme kuralı eski geometriye ait. Tekstil araçlarında artışları gözden geçirin." : ""; grading.Visibility = showGrading ? Visibility.Visible : Visibility.Collapsed;
            limits.Text = showLimits ? "2500 nokta: Pastal parça başına 2000 noktayla sınırlıdır." : ""; limits.Visibility = showLimits ? Visibility.Visible : Visibility.Collapsed;
            conflict.Text = showConflict ? "Taslak çakışması: ad ve konum taslağı yeni değerlerle uyuşmuyor." : ""; conflict.Visibility = showConflict ? Visibility.Visible : Visibility.Collapsed;
            notice.Text = "İşlem geçmişi kapasitesi doldu; en eski adımlar silinir."; notice.Visibility = Visibility.Visible;
            var root = (FrameworkElement)fresh.Content;
            root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
            var sheet = fresh.AllToolsSheetRoot!;
            var sheetBounds = BoundsInRoot(sheet, root);
            var card = Descendants<FrameworkElement>(root).Single(e => AutomationProperties.GetName(e) == "Sıradaki adım ve seçili parça");
            var cardBounds = BoundsInRoot(card, root);
            if (!FullyVisible(card, root) || sheetBounds.Right > cardBounds.Left + .5) reasons.Add($"{width}: card visible={FullyVisible(card, root)} sheet.Right={sheetBounds.Right:0} card.Left={cardBounds.Left:0}");
            foreach (var warning in new FrameworkElement[] { grading, limits, conflict }.Where(x => x.Visibility == Visibility.Visible))
                if (!FullyVisible(warning, root) || BoundsInRoot(warning, root).Left < sheetBounds.Right - .5) reasons.Add($"{width}: {warning.GetType().Name} hidden or under the page");
            // The window's capacity notice is repeated at the top of the page, word for word.
            var repeated = Logical<TextBlock>(sheet).FirstOrDefault(t => AutomationProperties.GetName(t) == "İşlem geçmişi kapasite uyarısı");
            if (repeated is null || repeated.Text != notice.Text || repeated.Visibility != Visibility.Visible) reasons.Add($"{width}: capacity notice not repeated");
            // The page spans the rail and the canvas columns (0 and 1) and nothing else.
            var workspaceGrid = F<Grid>(fresh, "workspaceGrid");
            if (Grid.GetColumn(sheet) != 0 || Grid.GetColumnSpan(sheet) != 2 || !workspaceGrid.Children.Contains(sheet)) reasons.Add($"{width}: wrong columns");
            fresh.CloseAllToolsSheet();
            notice.Visibility = Visibility.Collapsed; grading.Visibility = Visibility.Collapsed; limits.Visibility = Visibility.Collapsed; conflict.Visibility = Visibility.Collapsed;
        }
        Check(reasons.Count == 0, "All tools sheet leaves the piece card and its warnings visible" + (reasons.Count == 0 ? "" : " [" + string.Join("; ", reasons) + "]"), checks);
    });

    // Search by title, synonym, folded Turkish letters; the best match comes first and others are not listed.
    private static void VerifyAllToolsSearch(List<string> checks) => WithFresh(fresh =>
    {
        Simple(fresh, true);
        fresh.OpenAllToolsSheet();
        var box = Logical<TextBox>(fresh.AllToolsSheetRoot!).Single(t => AutomationProperties.GetName(t) == "Araç ara");
        var bad = new List<string>();
        foreach (var (typed, expected) in new[] { ("ayna", "piece.mirror"), ("çoğalt", "piece.duplicate"), ("COGALT", "piece.duplicate"), ("ölçü", "piece.resize"), ("kaydet", "start.save") })
        {
            box.Text = typed;
            var listed = SheetRows(fresh).Select(r => r.Entry.Id).ToList();
            if (listed.Count == 0 || listed[0] != expected || listed.Count >= CommandCatalogue.Entries.Length) bad.Add($"{typed} → {string.Join(",", listed.Take(3))} ({listed.Count})");
        }
        box.Text = "zzzzqqq";
        var none = SheetRows(fresh).Count == 0;
        box.Text = "";
        var all = SheetRows(fresh).Count == CommandCatalogue.Entries.Length;
        Check(bad.Count == 0 && none && all, "All tools search finds an entry by a synonym" + (bad.Count == 0 && none && all ? "" : $" [{string.Join("; ", bad)}; empty={none}; cleared={all}]"), checks);
        fresh.CloseAllToolsSheet();
    });

    private static void VerifyAllToolsRenders(string reportPath, List<string> checks)
    {
        var files = new List<string>(); var problems = new List<string>();
        WithFresh(fresh =>
        {
            Simple(fresh, true);
            fresh.Select(fresh.Document.Pieces[0].Id);
            var root = (FrameworkElement)fresh.Content;
            var workspace = F<Grid>(fresh, "workspaceGrid"); var childrenBefore = workspace.Children.Count;
            foreach (var (name, width, height, query, columns) in new[] { ("", 1440, 900, "", 3), ("", 1024, 640, "", 2), ("-search", 1024, 640, "ayna", 1) })
            {
                fresh.OpenAllToolsSheet();
                var box = Logical<TextBox>(fresh.AllToolsSheetRoot!).Single(t => AutomationProperties.GetName(t) == "Araç ara");
                box.Text = query;
                var path = Path.ChangeExtension(reportPath, $".all-tools{name}-{width}x{height}.png");
                Render(fresh, path, width, height); files.Add(path);
                var sheet = fresh.AllToolsSheetRoot!;
                var shown = fresh.AllToolsColumnCount;
                var rowCount = SheetRows(fresh).Count;
                var tooNarrow = SheetRows(fresh).Where(r => r.Row.ActualWidth < 150).Select(r => r.Entry.Id).ToList();
                if (shown != columns) problems.Add($"{width}x{height}{name}: {shown} columns instead of {columns}");
                if (query.Length == 0 && rowCount != CommandCatalogue.Entries.Length) problems.Add($"{width}x{height}: {rowCount} entry buttons");
                if (tooNarrow.Count > 0) problems.Add($"{width}x{height}{name}: too narrow {string.Join(",", tooNarrow.Take(3))}");
                var actual = BoundsInRoot(sheet, root);
                if (actual.Width < 300 || actual.Height < 300) problems.Add($"{width}x{height}{name}: page {actual.Width:0}x{actual.Height:0}");
                fresh.CloseAllToolsSheet();
            }
            if (workspace.Children.Count != childrenBefore) problems.Add("closing the page changed the workspace");
        });
        Check(files.Count == 3 && files.All(f => File.Exists(f) && new FileInfo(f).Length > 2000) && problems.Count == 0,
            "All tools page renders .all-tools-1440x900.png (3 columns), .all-tools-1024x640.png (2 columns), .all-tools-search-1024x640.png and closing restores the workspace"
            + (problems.Count == 0 ? "" : " [" + string.Join("; ", problems.Take(6)) + "]"), checks);
    }
}
