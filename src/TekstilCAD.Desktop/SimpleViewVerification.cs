using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using TekstilCAD.Core;
using TekstilCAD.Core.Theme;
using static TekstilCAD.Desktop.DesktopVerification;

namespace TekstilCAD.Desktop;

/// <summary>
/// Headless checks of the simple view (PR 7). Every scenario builds its own fresh window that is never shown, so the
/// long-lived verification window and its order-dependent state stay untouched; the light palette is applied only inside
/// the try block and the dark palette is restored afterwards.
/// </summary>
internal static partial class SimpleViewVerification
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    // Simple view canvas floors. Written from the first CI render minus 10 px; never lowered just to pass.
    // First CI render: clean 377 px at 1024x640 and 637 px at 1440x900 (floors 367 and 627); busy 243 px at 1024x640 and 503 px at 1440x900 with a heavier busy set;
    // the busy floors are provisional until measured with the classic busy set (CI).
    private const double Floor1024 = 367;
    private const double Floor1440 = 627;
    private const double BusyFloor1024 = 250;
    private const double BusyFloor1440 = 500;

    internal static void Run(MainWindow studio, string reportPath, List<string> checks)
    {
        StudioTheme.Use(ThemePalette.Light);
        ReportPath = reportPath;
        try
        {
            VerifyConstructorDefault(checks);
            VerifyRoundTrip(checks);
            VerifyHeaderNotices(reportPath, checks);
            VerifyFooter(checks);
            VerifyBusyHeader(checks);
            VerifyWarningsUnderCard(checks);
            VerifyFloors(reportPath, checks);
            VerifyClassicFloorsAfterSwitch(checks);
            VerifyControls(checks);
            VerifyRefusals(checks);
            VerifyCommandThroughSimpleView(checks);
            VerifyRenders(reportPath, checks);
            VerifyNoWindowVisible(studio, checks);
        }
        finally { StudioTheme.Use(ThemePalette.Dark); }
    }

    // ---------------------------------------------------------------- helpers

    private static readonly List<MainWindow> Created = [];

    private static T F<T>(MainWindow window, string name) where T : class => (T)typeof(MainWindow).GetField(name, Private)!.GetValue(window)!;

    private static void WithFresh(Action<MainWindow> body)
    {
        var fresh = new MainWindow(); StopTimer(fresh); Created.Add(fresh); var closed = false; fresh.Closed += (_, _) => closed = true;
        try { _ = new WindowInteropHelper(fresh).EnsureHandle(); body(fresh); }
        finally
        {
            // Unsaved test edits must not open the discard prompt when the window closes.
            typeof(MainWindow).GetField("dirty", Private)!.SetValue(fresh, false);
            var drafts = typeof(MainWindow).GetField("propertyDrafts", Private)!.GetValue(fresh) as System.Collections.IDictionary; drafts?.Clear();
            F<CadCanvas>(fresh, "canvas").CancelDrawing();
            StopTimer(fresh); fresh.Close(); PumpUntil(() => closed);
        }
    }

    private static void Simple(MainWindow window, bool on)
    {
        if (!window.TrySetSimpleView(on, out var refusal)) throw new InvalidOperationException("Verification failed: the view switch was refused: " + refusal);
    }

    private static void Notice(MainWindow window, string text, string noticeField, params string[] actionFields)
    {
        var notice = F<FrameworkElement>(window, noticeField);
        if (notice is TextBlock block) block.Text = text; else if (notice is TextBox box) box.Text = text;
        notice.Visibility = Visibility.Visible;
        foreach (var action in actionFields) F<Button>(window, action).Visibility = Visibility.Visible;
    }

    // The classic "busy" composition (DesktopVerification.VerifyCompactWorkspace): unsaved work, a DXF notice with its actions, a recovery scan notice
    // with its open action, the machine message. "every" adds the recovery warning, the scan error details, the export and the pastal notices.
    private static void MakeBusy(MainWindow window, bool every)
    {
        typeof(MainWindow).GetField("dirty", Private)!.SetValue(window, true);
        Notice(window, "DXF hazır. Proje henüz değiştirilmedi; önizlemeyi açarak ölçüleri kontrol edin.", "dxfNotice", "dxfCancel", "dxfReview");
        Notice(window, "Kurtarma taraması başka kayıtlar buldu.", "recoveryScanNotice", "recoveryOpen");
        Notice(window, "Makine simülatörü hazırlanıyor; fiziksel makine bağlantısı yok.", "machineQueueMessage");
        if (every)
        {
            Notice(window, "Otomatik kurtarma kaydı bulundu; çalışmanızı kontrol edin.", "recoveryNotice", "recoveryDetails");
            Notice(window, "Kurtarma taraması başka kayıtlar buldu.", "recoveryScanNotice", "recoveryScanDetails", "recoveryOpen");
            Notice(window, "Dışa aktarma sürüyor; dosya yalnız inceleme içindir.", "exportNotice", "exportCancel");
            Notice(window, "Pastal geçmişi üretim onayı değildir.", "nestingHistoryNotice", "nestingHistoryDetails");
        }
        Invoke(window, "Refresh");
    }

    private static readonly (string Text, string[] Actions)[] Notices =
    [
        ("saveState", []), ("sourceHistoryNotice", ["sourceHistoryDetails"]), ("nestingHistoryNotice", ["nestingHistoryDetails"]),
        ("exportNotice", ["exportCancel"]), ("dxfNotice", ["dxfCancel", "dxfReview"]), ("recoveryNotice", ["recoveryDetails"]),
        ("recoveryScanNotice", ["recoveryScanDetails", "recoveryOpen"]), ("machineQueueMessage", []),
    ];

    private static bool NoticesComplete(MainWindow window)
    {
        var root = (FrameworkElement)window.Content; var complete = true;
        foreach (var (name, actions) in Notices)
        {
            var text = F<FrameworkElement>(window, name); var shown = text.Visibility == Visibility.Visible;
            if (shown) complete &= FullyVisible(text, root) && text.ActualHeight + text.Margin.Top + text.Margin.Bottom >= text.DesiredSize.Height - .5
                && (text is not TextBox box || box.ExtentHeight <= box.ViewportHeight + .5);
            foreach (var action in actions.Select(a => F<FrameworkElement>(window, a)).Where(a => a.Visibility == Visibility.Visible))
            {
                var bounds = BoundsInRoot(action, root);
                complete &= FullyVisible(action, root) && bounds.Height >= 26;
                if (shown) { var beside = BoundsInRoot(text, root); complete &= bounds.Left >= beside.Right - .5 && bounds.Top < beside.Bottom && bounds.Bottom > beside.Top; }
            }
        }
        return complete;
    }

    private static void CheckFloor(double actual, double floor, string name, List<string> checks)
        => Check(actual >= floor, actual >= floor ? name : $"{name} [measured {actual:0.#} px]", checks);

    // Logical-tree fingerprint: type, names, geometry properties, texts and brush references of every element.
    private static List<object?> Capture(DependencyObject root)
    {
        var list = new List<object?>();
        void Visit(DependencyObject element)
        {
            list.Add(element); list.Add(element.GetType());
            if (element is FrameworkElement fe)
            {
                list.Add(AutomationProperties.GetName(fe)); list.Add(fe.Visibility); list.Add(fe.Margin); list.Add(fe.Width); list.Add(fe.Height);
                list.Add(fe.MinWidth); list.Add(fe.MinHeight); list.Add(fe.ToolTip as string); list.Add(fe.Focusable); list.Add(fe.LayoutTransform);
                list.Add(DockPanel.GetDock(fe)); list.Add(AutomationProperties.GetLiveSetting(fe));
            }
            if (element is FrameworkElement shape)
            {
                list.Add(shape.MaxWidth); list.Add(shape.MaxHeight); list.Add(shape.Opacity); list.Add(shape.HorizontalAlignment); list.Add(shape.VerticalAlignment);
                list.Add(AutomationProperties.GetHelpText(shape)); list.Add(shape.ToolTip);
            }
            if (element is Border border) { list.Add(border.Padding); list.Add(border.Background); list.Add(border.CornerRadius); }
            if (element is Control control) { list.Add(control.FontSize); list.Add(control.FontWeight); list.Add(control.Background); list.Add(control.Foreground); list.Add(control.Padding); }
            if (element is TextBlock text) { list.Add(text.FontSize); list.Add(text.FontWeight); list.Add(text.Text); list.Add(text.Foreground); list.Add(text.TextWrapping); list.Add(text.Padding); }
            if (element is ContentControl { Content: string content }) list.Add(content);
            if (element is Grid grid)
            {
                foreach (var column in grid.ColumnDefinitions) { list.Add(column); list.Add(column.Width); list.Add(column.MinWidth); }
                foreach (var row in grid.RowDefinitions) { list.Add(row); list.Add(row.Height); }
            }
            if (element is ItemsControl) return;
            foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>()) Visit(child);
        }
        Visit(root);
        return list;
    }

    private static bool SameFingerprint(List<object?> a, List<object?> b)
        => a.Count == b.Count && a.Zip(b).All(pair => object.Equals(pair.First, pair.Second));

    // ---------------------------------------------------------------- the 15 checks

    private static void VerifyConstructorDefault(List<string> checks) => WithFresh(fresh =>
    {
        var root = (FrameworkElement)fresh.Content;
        var simpleNames = new[] { "Komut kutusu", "Komut çubuğu", "Sıradaki adım ve seçili parça", "Projeyi kaydet", "Proje aç", "Ayrıntılı görünüme geç", "Basit görünüme geç" };
        var simpleTexts = new[] { "Fareyle çiz", "Bu çizim üretim veya kesim onayı değildir", "Ayrıntılı görünüm", "Basit görünüm", "Tüm araçlar" };
        var found = Descendants<FrameworkElement>(root).Any(e => simpleNames.Contains(AutomationProperties.GetName(e))
            || e is TextBlock { Text: var t } && simpleTexts.Contains(t) || e is ContentControl { Content: string c } && simpleTexts.Contains(c));
        var commandArea = F<FrameworkElement>(fresh, "commandAreaPanel"); var tagline = F<FrameworkElement>(fresh, "taglineText");
        Check(!fresh.IsSimpleView && !found && commandArea.Visibility == Visibility.Visible && tagline.Visibility == Visibility.Visible
            && F<CadCanvas>(fresh, "canvas").EmptyStateTitle is null && F<Grid>(fresh, "workspaceGrid").ColumnDefinitions.Select(c => c.Width.Value).SequenceEqual(new[] { 146d, 1d, 270d }),
            "Constructor default stays the detailed layout and contains no simple view control", checks);
    });
}
