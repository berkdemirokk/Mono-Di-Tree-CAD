using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TekstilCAD.Core;
using TekstilCAD.Core.Guide;
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

    private static ProjectDocument Imported() => new()
    {
        Name = "İçe aktarılan proje", Pieces = [new PatternPiece { Name = "Parça", Points = [new(0, 0), new(100, 0), new(100, 50), new(0, 50)] }],
        ImportProvenance = new DxfImportProvenance { SourceFileName = "kaynak.dxf", SourceSha256 = new string('a', 64), SourceByteLength = 100, MillimetersPerUnit = 1 },
    };

    private static void VerifyHeaderNotices(string reportPath, List<string> checks) => WithFresh(fresh =>
    {
        // The source notice and its button only exist for a project with import history.
        fresh.LoadDocument(Imported());
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
                && notice.Text.StartsWith("DXF kaynak ve aktarım notları projede saklanıyor.", StringComparison.Ordinal);
        }
        Check(complete, "Simple view keeps the save state, the source history notice and its button visible and unclipped at 1024x640 and at 1440x900 for an imported project", checks);

        // A project without import history shows neither the sentence nor its button, whether new or opened; an import brings them.
        bool Shown() => F<TextBlock>(fresh, "sourceHistoryNotice").Visibility == Visibility.Visible && F<Button>(fresh, "sourceHistoryDetails").Visibility == Visibility.Visible;
        bool Hidden() => F<TextBlock>(fresh, "sourceHistoryNotice").Visibility == Visibility.Collapsed && F<Button>(fresh, "sourceHistoryDetails").Visibility == Visibility.Collapsed;
        fresh.LoadDocument(new ProjectDocument());
        var hiddenNew = Hidden();
        fresh.LoadDocument(new ProjectDocument { Name = "Açılan proje", Pieces = Imported().Pieces });
        var hiddenOpened = Hidden();
        fresh.LoadDocument(Imported());
        var shown = Shown();
        Simple(fresh, false);
        var classic = F<TextBlock>(fresh, "sourceHistoryNotice").Visibility == Visibility.Visible;
        Check(hiddenNew && hiddenOpened && shown && classic, "Simple view hides the source history notice and its button until the project has import history; the detailed layout always shows them", checks);
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
            ok &= units.Text == "Ölçü birimi: milimetre (mm) · Fareyle çizerken 1 mm'lik adımlarla" && FullyVisible(units, root) && FullyVisible(note, root)
                && units.Focusable && note.Focusable && units.FontSize >= 12 && note.FontSize >= 12
                && units.ActualHeight >= units.DesiredSize.Height - .5 && note.ActualHeight >= note.DesiredSize.Height - .5;
        }
        F<CheckBox>(fresh, "snap").IsChecked = false;
        Invoke(fresh, "Refresh");
        ok &= F<TextBlock>(fresh, "unitsText").Text == "Ölçü birimi: milimetre (mm) · Fareyle çizerken adım sınırı yok";
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
            SetNotices(fresh, true, width, height);
            ok &= NoticesComplete(fresh);
        }
        Check(ok, "Simple view keeps every conditional header notice complete beside its action when set", checks);
    });

    // The warnings sit under the command box, so the box and its buttons stay where they are however many warnings show.
    private static void VerifyBoxStaysPut(List<string> checks) => WithFresh(fresh =>
    {
        fresh.LoadDocument(Imported());
        Simple(fresh, true);
        var view = InlineBox(fresh)!; var root = (FrameworkElement)fresh.Content;
        Rect Place() => BoundsInRoot(view.Box, root);
        var clean = new List<Rect>(); var busy = new List<Rect>();
        var sizes = new[] { (1024, 640), (1440, 900) };
        foreach (var (width, height) in sizes) { Render(fresh, Temp($"box-clean-{width}x{height}"), width, height); clean.Add(Place()); }
        MakeBusy(fresh, true);
        foreach (var (width, height) in sizes) { Render(fresh, Temp($"box-busy-{width}x{height}"), width, height); busy.Add(Place()); }
        var same = clean.Zip(busy).All(pair => Math.Abs(pair.First.Top - pair.Second.Top) < .5 && Math.Abs(pair.First.Left - pair.Second.Left) < .5 && Math.Abs(pair.First.Height - pair.Second.Height) < .5);
        SetNotices(fresh, true, 1440, 900);
        Check(same && NoticesComplete(fresh), "Simple view command box keeps its position when every header warning is shown, and the warnings stay complete"
            + (same ? "" : $" [clean={string.Join("/", clean)} busy={string.Join("/", busy)}]"), checks);
    });

    // Two or more warnings are one banner line under the box (the first warning and how many there are); the toggle opens them all with
    // their buttons. A single warning is shown as it is.
    private static void VerifyNoticeBanner(List<string> checks)
    {
        var ok = true; var reasons = new List<string>();
        WithFresh(fresh =>
    {
        Simple(fresh, true);
        MakeBusy(fresh, true);
        foreach (var (width, height, floor) in new[] { (1024, 640, BusyFloor1024), (1440, 900, BusyFloor1440) })
        {
            Render(fresh, Temp($"banner-{width}x{height}"), width, height);
            SetNotices(fresh, false, width, height);
            var root = (FrameworkElement)fresh.Content; var strip = NoticeStrip(fresh); var toggle = NoticeToggle(fresh)!;
            var summary = Descendants<TextBlock>(strip).First(t => t.Text.Contains(" uyarı: ", StringComparison.Ordinal));
            var oneLine = strip.ActualHeight <= 44 && summary.ActualHeight <= 24;
            var reachable = FullyVisible(toggle, root) && toggle.Focusable && toggle.ActualHeight >= 40;
            var canvas = F<CadCanvas>(fresh, "canvas").ActualHeight >= floor;
            var examples = Descendants<Button>(root).First(b => AutomationProperties.GetName(b) == "Örnekleri göster");
            var clear = BoundsInRoot(examples, root).Bottom <= BoundsInRoot(strip, root).Top + .5;
            SetNotices(fresh, true, width, height);
            var complete = NoticesComplete(fresh);
            SetNotices(fresh, false, width, height);
            if (!(oneLine && reachable && canvas && complete && clear)) reasons.Add($"{width}x{height} clear={clear} strip={strip.ActualHeight:0} summary={summary.ActualHeight:0} reachable={reachable} canvas={canvas} complete={complete}");
            ok &= oneLine && reachable && canvas && complete && clear;
        }
        });
        // One warning only: no banner, the row shows as it is.
        WithFresh(single =>
        {
            single.LoadDocument(Imported());
            Simple(single, true);
            Render(single, Temp("banner-single"), 1024, 640);
            var root = (FrameworkElement)single.Content;
            var alone = NoticeToggle(single) is null || ((FrameworkElement)NoticeToggle(single)!.Parent).Visibility == Visibility.Collapsed;
            alone &= FullyVisible(F<TextBlock>(single, "sourceHistoryNotice"), root);
            if (!alone) reasons.Add("single notice is hidden behind a banner");
            ok &= alone;
        });
        Check(ok, "Simple view collapses two or more header warnings to one banner line with a reachable toggle that shows them all, and shows a single warning as it is"
            + (ok ? "" : $" [{string.Join("; ", reasons)}]"), checks);
    }

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
                // DesiredSize is stale after the manual re-layout above, so only the visible bounds (and an unscrolled text box) count.
                var visible = FullyVisible(w, root) && (w is not TextBox box || box.ExtentHeight <= box.ViewportHeight + .5);
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
                    if (busy) MakeBusy(fresh, false);
                    Render(fresh, Temp($"floor-{busy}-{width}x{height}"), width, height);
                    var canvas = F<CadCanvas>(fresh, "canvas");
                    checks.Add($"[measured] simple view canvas height {canvas.ActualHeight:0.#} px ({(busy ? "busy" : "clean")}) at {width}x{height}");
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
                    // The clean figures equal VerifyCompactWorkspace's own (421 and 681), so its floors apply as they are. The busy
                    // state here is built from shorter notice texts than that check leaves behind and measures a little lower, so the
                    // classic busy floors (340 and 600) stay with VerifyCompactWorkspace and this check requires no loss at all.
                    results.Add((busy ? "busy" : "clean", width, busy ? original - .5 : floor, canvas.ActualHeight));
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
        double minFont = double.MaxValue, minTarget = double.MaxValue;
        foreach (var (width, height) in new[] { (1024, 640), (1440, 900) })
        {
            Render(fresh, Temp($"controls-{width}x{height}"), width, height);
            var root = (FrameworkElement)fresh.Content;
            foreach (var button in Descendants<Button>(root).Where(b => !VisibleBoundsInRoot(b, root).IsEmpty))
            {
                var label = button.Content as string ?? AutomationProperties.GetName(button);
                var named = !string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)) || !string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(button)) || !string.IsNullOrWhiteSpace(button.Content as string);
                var primary = AutomationProperties.GetName(button) is "Proje aç" or "Projeyi kaydet";
                minTarget = Math.Min(minTarget, button.ActualHeight);
                if (!named) offenders.Add($"{width}: unnamed button");
                else if (!button.Focusable && button.IsEnabled) offenders.Add($"{width}: not focusable {label}");
                else if (button.ActualHeight < (primary ? 44 : 40) - .5) offenders.Add($"{width}: {label} {button.ActualHeight:0.#} px");
            }
            foreach (var element in Descendants<FrameworkElement>(root).Where(e => e is TextBlock || e is Button || e is TextBox || e is CheckBox).Where(e => !VisibleBoundsInRoot(e, root).IsEmpty))
            {
                var size = element is TextBlock t ? t.FontSize : ((Control)element).FontSize;
                minFont = Math.Min(minFont, size);
                if (size < 13 - .01) offenders.Add($"{width}: {size} px {element.GetType().Name} {(element as TextBlock)?.Text ?? AutomationProperties.GetName(element)}");
            }
            var tools = F<Dictionary<string, Button>>(fresh, "toolButtons").Values;
            toolsOk &= tools.Count == 6 && tools.All(tool => tool.ActualHeight >= 40 - .5 && FullyVisible(tool, root));
        }
        checks.Add($"[measured] simple view smallest visible font {minFont:0.#} px, smallest visible button height {minTarget:0.#} px");
        Check(offenders.Count == 0, "Every simple view button has an accessible name, is focusable and at least 40 px high (primary actions 44 px), and no visible text is below 13 px"
            + (offenders.Count == 0 ? "" : " [" + string.Join("; ", offenders.Take(6)) + "]"), checks);
        Check(toolsOk, "Six drawing tools stay visible without scrolling and at least 40 px high in the simple view", checks);
    });

    // Body text of the rail and the side card is at least 14 px; at the smallest window the rail (hint included) and the
    // start of the side card still fit without scrolling.
    private static void VerifyReadability(List<string> checks) => WithFresh(fresh =>
    {
        fresh.SetGuideStateForVerification([], FirstStepsTracker.Fresh, true);
        fresh.LoadDocument(new ProjectDocument());
        Simple(fresh, true);
        var reasons = new List<string>();
        foreach (var (width, height) in new[] { (1024, 640), (1440, 900) })
        {
            Render(fresh, Temp($"readability-{width}x{height}"), width, height);
            var root = (FrameworkElement)fresh.Content; var card = fresh.SideCard!;
            var body = Descendants<TextBlock>(card).Concat(Descendants<TextBlock>(root).Where(t => t.Text == "Fareyle çiz" || t.Text.StartsWith("Çizerken:", StringComparison.Ordinal)))
                .Where(t => !VisibleBoundsInRoot(t, root).IsEmpty && t.Text.Length > 0).ToList();
            foreach (var text in body.Where(t => t.FontSize < 14 - .01)) reasons.Add($"{width}: {text.FontSize} px {text.Text}");
            var hint = body.Single(t => t.Text.StartsWith("Çizerken:", StringComparison.Ordinal));
            if (!FullyVisible(hint, root)) reasons.Add($"{width}: rail hint cut {BoundsInRoot(hint, root)} / {VisibleBoundsInRoot(hint, root)}");
            foreach (var tool in F<Dictionary<string, Button>>(fresh, "toolButtons").Values.Where(b => !FullyVisible(b, root))) reasons.Add($"{width}: tool {AutomationProperties.GetName(tool)} cut");
            if (!card.TaskButtons.All(b => FullyVisible(b, root))) reasons.Add($"{width}: start task cut");
        }
        Check(reasons.Count == 0, "Simple view rail and side card text is at least 14 px and the rail and start tasks stay fully visible at 1024x640 and 1440x900"
            + (reasons.Count == 0 ? "" : " [" + string.Join("; ", reasons.Take(6)) + "]"), checks);
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
                if (busy) MakeBusy(fresh, false);
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
