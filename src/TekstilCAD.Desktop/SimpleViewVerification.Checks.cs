using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TekstilCAD.Core;
using static TekstilCAD.Desktop.DesktopVerification;

namespace TekstilCAD.Desktop;

internal static partial class SimpleViewVerification
{
    // Intermediate renders go next to the report so that CI uploads them for review.
    internal static string ReportPath = "";
    private static string Temp(string name) => Path.ChangeExtension(ReportPath, ".simple-view-tmp-" + name + ".png");

    private static void VerifyRoundTrip(List<string> checks) => WithFresh(fresh =>
    {
        var root = (FrameworkElement)fresh.Content;
        Render(fresh, Temp("roundtrip-classic"), 1440, 900);
        var before = Capture(root);
        Simple(fresh, true);
        var changed = F<FrameworkElement>(fresh, "commandAreaPanel").Visibility == Visibility.Collapsed && fresh.IsSimpleView;
        Render(fresh, Temp("roundtrip-simple"), 1024, 640);
        Simple(fresh, false);
        Render(fresh, Temp("roundtrip-classic"), 1440, 900);
        var after = Capture(root);
        var canvas = F<CadCanvas>(fresh, "canvas");
        Check(changed && !fresh.IsSimpleView && ReferenceEquals(fresh.Content, root) && SameFingerprint(before, after) && canvas.EmptyStateTitle is null && canvas.EmptyStateHint is null
            && InteractiveRoundTrip(),
            "Switching to the simple view and back restores every touched property and the same control instances", checks);
    });

    // The interactive path: the switch button is mounted, a piece is selected and the view is switched twice.
    private static bool InteractiveRoundTrip() { var ok = false; WithFresh(fresh =>
    {
        fresh.EnableViewSwitching();
        var root = (FrameworkElement)fresh.Content;
        fresh.Select(fresh.Document.Pieces[0].Id);
        Render(fresh, Temp("roundtrip-interactive"), 1440, 900);
        var before = Capture(root);
        var canvas = F<CadCanvas>(fresh, "canvas");
        var same = true;
        for (var cycle = 0; cycle < 2; cycle++)
        {
            Simple(fresh, true); Render(fresh, Temp("roundtrip-interactive"), 1024, 640);
            Simple(fresh, false); Render(fresh, Temp("roundtrip-interactive"), 1440, 900);
            same &= SameFingerprint(before, Capture(root)) && canvas.EmptyStateTitle is null && canvas.EmptyStateHint is null && !fresh.IsSimpleView;
        }
        ok = same;
    }); return ok; }

    private static void VerifyHeaderNotices(string reportPath, List<string> checks) => WithFresh(fresh =>
    {
        Simple(fresh, true);
        var complete = true;
        foreach (var (width, height) in new[] { (1024, 640), (1440, 900) })
        {
            Render(fresh, Temp($"header-{width}x{height}"), width, height);
            var root = (FrameworkElement)fresh.Content;
            var state = F<TextBlock>(fresh, "saveState"); var notice = F<TextBlock>(fresh, "sourceHistoryNotice"); var details = F<Button>(fresh, "sourceHistoryDetails");
            var noticeBounds = BoundsInRoot(notice, root); var detailsBounds = BoundsInRoot(details, root);
            complete &= state.Visibility == Visibility.Visible && FullyVisible(state, root) && FullyVisible(notice, root) && FullyVisible(details, root)
                && notice.ActualHeight + notice.Margin.Top + notice.Margin.Bottom >= notice.DesiredSize.Height - .5
                && Equals(details.Content, "Kaynak ve aktarım notları") && AutomationProperties.GetName(details) == "DXF kaynak ve aktarım notlarını incele"
                && detailsBounds.Left >= noticeBounds.Right - .5 && notice.Focusable
                && notice.Text.StartsWith("Kaynak ve önceki aktarım uyarıları projede saklanmaz.", StringComparison.Ordinal);
        }
        Check(complete, "Simple view keeps the save state, the source history notice and its button visible and unclipped at 1024x640 and at 1440x900", checks);

        // An empty new project shows the sentence only after it was opened or edited.
        fresh.LoadDocument(new ProjectDocument());
        var hidden = F<TextBlock>(fresh, "sourceHistoryNotice").Visibility == Visibility.Collapsed && F<Button>(fresh, "sourceHistoryDetails").Visibility == Visibility.Collapsed;
        fresh.LoadDocument(new ProjectDocument { Name = "Açılan proje", Pieces = [new PatternPiece { Name = "Parça", Points = [new(0, 0), new(100, 0), new(100, 50), new(0, 50)] }] });
        var shown = F<TextBlock>(fresh, "sourceHistoryNotice").Visibility == Visibility.Visible && F<Button>(fresh, "sourceHistoryDetails").Visibility == Visibility.Visible;
        Simple(fresh, false);
        var classic = F<TextBlock>(fresh, "sourceHistoryNotice").Visibility == Visibility.Visible;
        Check(hidden && shown && classic, "Simple view shows the source history notice of an empty new project only after the project was opened or edited", checks);
    });

    private static void VerifyFooter(List<string> checks) => WithFresh(fresh =>
    {
        Simple(fresh, true);
        var ok = true;
        foreach (var (width, height) in new[] { (1024, 640), (1440, 900) })
        {
            Render(fresh, Temp($"footer-{width}x{height}"), width, height);
            var root = (FrameworkElement)fresh.Content;
            var footer = F<FrameworkElement>(fresh, "footerPanel");
            var units = F<TextBlock>(fresh, "unitsText");
            var note = Descendants<TextBlock>(footer).Single(t => t.Text == "Bu çizim üretim veya kesim onayı değildir");
            ok &= units.Text == "Ölçü birimi: milimetre (mm) · Fareyle çizerken 1 mm adımlarla" && FullyVisible(units, root) && FullyVisible(note, root)
                && units.Focusable && note.Focusable && units.FontSize >= 12 && note.FontSize >= 12
                && units.ActualHeight >= units.DesiredSize.Height - .5 && note.ActualHeight >= note.DesiredSize.Height - .5;
        }
        F<CheckBox>(fresh, "snap").IsChecked = false;
        Invoke(fresh, "Refresh");
        ok &= F<TextBlock>(fresh, "unitsText").Text == "Ölçü birimi: milimetre (mm) · Fareyle çizerken serbest";
        Check(ok, "Simple view shows the unit text and the production note in the footer", checks);
    });

    private static void VerifyBusyHeader(List<string> checks) => WithFresh(fresh =>
    {
        Simple(fresh, true);
        MakeBusy(fresh, true);
        var ok = true;
        foreach (var (width, height) in new[] { (1024, 640), (1440, 900) })
        {
            Render(fresh, Temp($"notices-{width}x{height}"), width, height);
            ok &= NoticesComplete(fresh);
        }
        Check(ok, "Simple view keeps every conditional header notice complete beside its action when set", checks);
    });

    private static void VerifyWarningsUnderCard(List<string> checks) => WithFresh(fresh =>
    {
        Simple(fresh, true);
        fresh.Select(fresh.Document.Pieces[0].Id);
        var grading = F<TextBlock>(fresh, "gradingGeometryNotice"); var limits = F<TextBlock>(fresh, "pieceLimits"); var conflict = F<TextBox>(fresh, "propertyDraftConflict");
        void Show(bool g, bool l, bool c)
        {
            grading.Text = g ? "Kayıtlı serileme kuralı eski geometriye ait. Tekstil araçlarında artışları gözden geçirin." : ""; grading.Visibility = g ? Visibility.Visible : Visibility.Collapsed;
            limits.Text = l ? "2500 nokta: Pastal parça başına 2000 noktayla sınırlıdır." : ""; limits.Visibility = l ? Visibility.Visible : Visibility.Collapsed;
            conflict.Text = c ? "Taslak çakışması: ad ve konum taslağı yeni değerlerle uyuşmuyor." : ""; conflict.Visibility = c ? Visibility.Visible : Visibility.Collapsed;
        }
        var reasons = new List<string>();
        bool Under(string state, int width, int height, Action show, params FrameworkElement[] warnings)
        {
            // Render refreshes the window, which resets these warnings to their real (hidden) state; set them again
            // and lay the window out once more without refreshing.
            Render(fresh, Temp($"warnings-{state}-{width}x{height}"), width, height);
            show();
            var root = (FrameworkElement)fresh.Content;
            root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
            var card = Descendants<FrameworkElement>(root).Single(e => AutomationProperties.GetName(e) == "Sıradaki adım ve seçili parça");
            var cardBounds = BoundsInRoot(card, root); var ok = true;
            foreach (var w in warnings)
            {
                var bounds = BoundsInRoot(w, root);
                var visible = FullyVisible(w, root) && (w is not TextBlock t || t.ActualHeight >= t.DesiredSize.Height - .5) && (w is not TextBox box || box.ExtentHeight <= box.ViewportHeight + .5);
                var below = bounds.Top >= cardBounds.Bottom - .5;
                if (!visible || !below) { ok = false; reasons.Add($"{state} {width}x{height} {w.GetType().Name}: fullyVisible={visible} below={below} bounds={bounds} visible={VisibleBoundsInRoot(w, root)} cardBottom={cardBounds.Bottom:0} viewport={Viewport(w, root)} desired={w.DesiredSize} vis={w.Visibility} wrap={(w as TextBlock)?.TextWrapping}"); }
            }
            return ok;
        }
        // Each warning alone at the smallest window; all three together where the side column is taller.
        var one = Under("grading", 1024, 640, () => Show(true, false, false), grading);
        one &= Under("limits", 1024, 640, () => Show(false, true, false), limits);
        one &= Under("conflict", 1024, 640, () => Show(false, false, true), conflict);
        var all = Under("all", 1440, 900, () => Show(true, true, true), grading, limits, conflict);
        Check(one && all, "Simple view keeps the grading, piece limit and draft conflict warnings visible under the piece card" + (one && all ? "" : " [" + string.Join("; ", reasons) + "]"), checks);
    });

    private static string Viewport(FrameworkElement element, FrameworkElement root)
    {
        for (DependencyObject? parent = System.Windows.Media.VisualTreeHelper.GetParent(element); parent is not null; parent = System.Windows.Media.VisualTreeHelper.GetParent(parent))
            if (parent is ScrollViewer viewer) return BoundsInRoot(viewer, root) + " scrollable=" + viewer.ScrollableHeight.ToString("0");
        return "none";
    }

    private static void VerifyFloors(string reportPath, List<string> checks)
    {
        foreach (var busy in new[] { false, true })
            foreach (var (width, height, floor, busyFloor) in new[] { (1024, 640, Floor1024, BusyFloor1024), (1440, 900, Floor1440, BusyFloor1440) })
                WithFresh(fresh =>
                {
                    Simple(fresh, true);
                    if (busy) MakeBusy(fresh, true);
                    Render(fresh, Temp($"floor-{busy}-{width}x{height}"), width, height);
                    var canvas = F<CadCanvas>(fresh, "canvas");
                    checks.Add($"[measured] simple view canvas height {canvas.ActualHeight:0.#} px ({(busy ? "every notice busy" : "clean")}) at {width}x{height}");
                    if (busy) CheckFloor(canvas.ActualHeight, busyFloor, $"Canvas keeps at least {busyFloor} px of height in the simple view with every header notice busy at {width}x{height}", checks);
                    else CheckFloor(canvas.ActualHeight, floor, $"Canvas keeps at least {floor} px of height in the simple view at {width}x{height}", checks);
                });
    }

    private static void VerifyClassicFloorsAfterSwitch(List<string> checks)
    {
        // The classic floors (DesktopVerification.VerifyCompactWorkspace): 340 and 600 busy, 411 and 671 clean.
        var results = new List<(string State, int Width, double Floor, double Height)>();
        foreach (var busy in new[] { false, true })
            foreach (var (width, height, floor) in busy ? new[] { (1024, 640, 340d), (1440, 900, 600d) } : new[] { (1024, 640, 411d), (1440, 900, 671d) })
                WithFresh(fresh =>
                {
                    if (busy) MakeBusy(fresh, false);
                    Render(fresh, Temp("classic-before"), width, height);
                    var canvas = F<CadCanvas>(fresh, "canvas"); var original = canvas.ActualHeight;
                    Simple(fresh, true); Render(fresh, Temp("classic-on"), width, height);
                    Simple(fresh, false); Render(fresh, Temp("classic-after"), width, height);
                    results.Add((busy ? "busy" : "clean", width, floor, canvas.ActualHeight));
                    checks.Add($"[measured] classic canvas height {original:0.#} px before and {canvas.ActualHeight:0.#} px after the switch ({(busy ? "busy" : "clean")}) at {width}x{height}");
                });
        var pass = results.All(r => r.Height >= r.Floor);
        Check(pass, "Classic view keeps its canvas floors after switching to the simple view and back"
            + (pass ? "" : " [" + string.Join(", ", results.Select(r => $"{r.State} {r.Width}: {r.Height:0.#} < {r.Floor}")) + "]"), checks);
    }

    private static void VerifyControls(List<string> checks) => WithFresh(fresh =>
    {
        Simple(fresh, true);
        var offenders = new List<string>(); var toolsOk = true;
        foreach (var (width, height) in new[] { (1024, 640), (1440, 900) })
        {
            Render(fresh, Temp($"controls-{width}x{height}"), width, height);
            var root = (FrameworkElement)fresh.Content;
            foreach (var button in Descendants<Button>(root).Where(b => !VisibleBoundsInRoot(b, root).IsEmpty))
            {
                var label = button.Content as string ?? AutomationProperties.GetName(button);
                var named = !string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)) || !string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(button)) || !string.IsNullOrWhiteSpace(button.Content as string);
                var primary = AutomationProperties.GetName(button) is "Proje aç" or "Projeyi kaydet";
                if (!named) offenders.Add($"{width}: unnamed button");
                else if (!button.Focusable && button.IsEnabled) offenders.Add($"{width}: not focusable {label}");
                else if (button.ActualHeight < (primary ? 44 : 32) - .5) offenders.Add($"{width}: {label} {button.ActualHeight:0.#} px");
            }
            foreach (var element in Descendants<FrameworkElement>(root).Where(e => e is TextBlock || e is Button || e is TextBox || e is CheckBox).Where(e => !VisibleBoundsInRoot(e, root).IsEmpty))
            {
                var size = element is TextBlock t ? t.FontSize : ((Control)element).FontSize;
                if (size < 12 - .01) offenders.Add($"{width}: {size} px {element.GetType().Name} {(element as TextBlock)?.Text ?? AutomationProperties.GetName(element)}");
            }
            var tools = F<Dictionary<string, Button>>(fresh, "toolButtons").Values;
            toolsOk &= tools.Count == 6 && tools.All(tool => tool.ActualHeight >= 32 && FullyVisible(tool, root));
        }
        Check(offenders.Count == 0, "Every simple view button has an accessible name, is focusable, is at least 32 px high; primary actions at least 44 px"
            + (offenders.Count == 0 ? "" : " [" + string.Join("; ", offenders.Take(6)) + "]"), checks);
        Check(toolsOk, "Six drawing tools stay visible without scrolling and at least 32 px high in the simple view", checks);
    });

    private static List<Point2> Drawing(CadCanvas canvas) => (List<Point2>)typeof(CadCanvas).GetField("drawing", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(canvas)!;

    private static void VerifyRefusals(List<string> checks) => WithFresh(fresh =>
    {
        var canvas = F<CadCanvas>(fresh, "canvas"); var root = (FrameworkElement)fresh.Content;
        // An unapplied draft refuses the switch. The baseline is taken once the selection has refreshed the window.
        fresh.Select(fresh.Document.Pieces[0].Id);
        Invoke(fresh, "Refresh");
        var before = Capture(root);
        F<TextBox>(fresh, "name").Text = "Başka bir ad";
        var draftRefused = !fresh.TrySetSimpleView(true, out var draftMessage) && draftMessage is not null && !fresh.IsSimpleView;
        (typeof(MainWindow).GetField("propertyDrafts", Private)!.GetValue(fresh) as System.Collections.IDictionary)!.Clear();
        Invoke(fresh, "Refresh");
        // A pending drawing refuses it too.
        Drawing(canvas).Add(new Point2(10, 10));
        var drawingRefused = !fresh.TrySetSimpleView(true, out var drawingMessage) && drawingMessage is not null && !fresh.IsSimpleView;
        canvas.CancelDrawing();
        var unchanged = SameFingerprint(before, Capture(root));
        // And leaving the simple view is refused while a drawing is pending, leaving the view as it was.
        Simple(fresh, true);
        Drawing(canvas).Add(new Point2(10, 10));
        var leaveRefused = !fresh.TrySetSimpleView(false, out _) && fresh.IsSimpleView;
        canvas.CancelDrawing();
        Check(draftRefused && drawingRefused && unchanged && leaveRefused, "Switching is refused with unapplied drafts or a pending drawing and leaves the view unchanged", checks);
    });

    private static void VerifyCommandThroughSimpleView(List<string> checks) => WithFresh(fresh =>
    {
        Simple(fresh, true);
        var count = fresh.Document.Pieces.Count;
        var edit = new CommandEdit("Dikdörtgen çizildi: 120 × 80 mm.", "Dikdörtgen",
            document => document.Pieces.Add(new PatternPiece { Name = "Parça 3", Points = [new(300, 0), new(420, 0), new(420, 80), new(300, 80)] }),
            NeedsSelection: false, AddsPiece: true);
        var outcome = fresh.ApplyCommand(edit);
        Render(fresh, Temp("command"), 1440, 900);
        var state = F<TextBlock>(fresh, "saveState"); var save = Descendants<Button>((FrameworkElement)fresh.Content).Single(b => AutomationProperties.GetName(b) == "Projeyi kaydet");
        Check(outcome.Changed && fresh.Document.Pieces.Count == count + 1 && state.Text.StartsWith("●", StringComparison.Ordinal)
            && ReferenceEquals(save.Background, StudioTheme.PrimaryFill) && fresh.IsSimpleView,
            "Box command applies through the simple view and header save state shows the unsaved marker", checks);
    });

    private static void VerifyRenders(string reportPath, List<string> checks)
    {
        var files = new List<string>();
        WithFresh(fresh =>
        {
            Simple(fresh, true);
            foreach (var (name, width, height, busy) in new[] { ("", 1440, 900, false), ("", 1024, 640, false), ("-busy", 1024, 640, true) })
            {
                if (busy) MakeBusy(fresh, true);
                var path = Path.ChangeExtension(reportPath, $".simple-view{name}-{width}x{height}.png");
                Render(fresh, path, width, height); files.Add(path);
            }
        });
        Check(files.Count == 3 && files.All(f => File.Exists(f) && new FileInfo(f).Length > 2000),
            "Simple view window renders .simple-view-1440x900.png, .simple-view-1024x640.png, .simple-view-busy-1024x640.png", checks);
    }

    private static void VerifyNoWindowVisible(MainWindow studio, List<string> checks)
        => Check(!studio.IsVisible && Created.Count > 0 && Created.All(window => !window.IsVisible), "No window becomes visible", checks);
}
