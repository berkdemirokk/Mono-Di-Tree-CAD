using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using TekstilCAD.Core.Commands;

namespace TekstilCAD.Desktop;

// The "Tüm araçlar" page of the simple view (FINAL_SPEC g.2, FINAL_PLAN PR 9). It is an overlay over the rail and the canvas
// (ColumnSpan 2 of the workspace grid); the side column stays open, so the piece card and its warnings stay visible.
// The entries come from CommandCatalogue (Core). Entries are new buttons created each time the page opens and removed again
// when it closes, so the classic tree never holds a second control with a content string the headless verification looks up.
public partial class MainWindow
{
    private AllToolsSheet? allToolsSheet;

    /// <summary>True while the "Tüm araçlar" page covers the rail and the canvas.</summary>
    internal bool IsAllToolsSheetOpen => allToolsSheet is { IsOpen: true };

    /// <summary>Opens the page (simple view only). Returns false when it cannot open.</summary>
    internal bool OpenAllToolsSheet()
    {
        if (!IsSimpleView) return false;
        allToolsSheet ??= new AllToolsSheet(this);
        allToolsSheet.Open();
        return allToolsSheet.IsOpen;
    }

    internal void CloseAllToolsSheet() => allToolsSheet?.Close();

    internal void ToggleAllToolsSheet()
    {
        if (IsAllToolsSheetOpen) CloseAllToolsSheet(); else OpenAllToolsSheet();
    }

    /// <summary>Test access: the live classic controls whose content string or automation name equals a candidate.</summary>
    internal List<FrameworkElement> FindClassicControls(params string?[] candidates) => AllToolsSheet.FindClassic(this, candidates);

    /// <summary>Test access: number of entry columns the open page shows (1 for a search result list).</summary>
    internal int AllToolsColumnCount => allToolsSheet?.ColumnCount ?? 0;

    /// <summary>Test access: the page itself while it is open.</summary>
    internal FrameworkElement? AllToolsSheetRoot => allToolsSheet?.Root;

    private sealed class AllToolsSheet
    {
        private readonly MainWindow w;
        private Border? root;
        private TextBox? search;
        private TextBlock? emptyResult, resultCount;
        private string spoken = "";
        private readonly List<(UIElement Element, KeyboardNavigationMode Mode)> blocked = [];
        private Grid? body;
        private ScrollViewer? scroller;
        private readonly Dictionary<string, Button> rows = [];
        private int columns;

        internal AllToolsSheet(MainWindow window) => w = window;

        internal int ColumnCount => body is null ? 0 : body.ColumnDefinitions.Count == 0 ? 1 : body.ColumnDefinitions.Count;
        internal bool IsOpen => root is not null && w.workspaceGrid.Children.Contains(root);
        internal FrameworkElement? Root => IsOpen ? root : null;

        // ---------------------------------------------------------------- finding the real controls

        /// <summary>
        /// The live classic controls an entry stands for: the control whose content string or automation name equals one of
        /// the candidates, searched in the header, the (hidden in the simple view) command strip and the workspace. The page
        /// itself and the simple view's own copies (command region, side card) are not part of that search.
        /// </summary>
        internal static List<FrameworkElement> FindClassic(MainWindow w, params string?[] candidates)
        {
            var wanted = candidates.Where(c => !string.IsNullOrEmpty(c)).Select(c => c!).ToHashSet(StringComparer.Ordinal);
            var found = new List<FrameworkElement>();
            // The six drawing tools: their content is a layout and their automation name changes in the simple view.
            foreach (var name in wanted)
                if (w.toolButtons.TryGetValue(name, out var tool)) found.Add(tool);
            if (found.Count > 0) return found;
            void Walk(DependencyObject parent)
            {
                foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
                {
                    if (child is FrameworkElement { Tag: SheetTag }) continue;
                    if (child is FrameworkElement element)
                    {
                        var byContent = element is ContentControl { Content: string content } && wanted.Contains(content);
                        var byName = AutomationProperties.GetName(element) is { Length: > 0 } name && wanted.Contains(name);
                        if (byContent || byName) found.Add(element);
                    }
                    Walk(child);
                }
            }
            Walk(w.headerGrid); Walk(w.commandAreaPanel); Walk(w.workspaceGrid);
            return found;
        }

        private const string SheetTag = "all-tools-sheet";

        // ---------------------------------------------------------------- build

        private void Build()
        {
            rows.Clear();
            search = new TextBox { MinHeight = 36, Padding = new Thickness(8, 4, 8, 4), FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center, MaxWidth = 420 };
            AutomationProperties.SetName(search, "Araç ara");
            search.ToolTip = "Ad, açıklama ya da bildiğiniz bir sözle arayın: örneğin kopyala, ayna, ölçü";
            search.TextChanged += (_, _) => Arrange();
            var title = new TextBlock { Text = "Tüm araçlar", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = StudioTheme.Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
            var hint = new TextBlock { Text = "Araç ara", FontSize = 14, Foreground = StudioTheme.Muted, Margin = new Thickness(0, 0, 0, 2) };
            // Left-aligned with a width of its own, so the label sits directly above the field's left edge.
            var searchBox = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Width = 360, Children = { hint, search } };
            var close = new Button { Content = "Kapat", MinHeight = 36, MinWidth = 84, Padding = new Thickness(14, 4, 14, 4), FontSize = 14, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 0, 0), ToolTip = "Sayfayı kapat · Esc" };
            AutomationProperties.SetName(close, "Tüm araçlar sayfasını kapat");
            close.Click += (_, _) => Close();
            var top = new Grid();
            top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            Grid.SetColumn(searchBox, 1); Grid.SetColumn(close, 2);
            top.Children.Add(title); top.Children.Add(searchBox); top.Children.Add(close);

            // The window's own capacity notice, repeated word for word while it is shown (the page covers the canvas top).
            var notice = new TextBlock { FontSize = 12, Foreground = StudioTheme.Warning, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Focusable = true };
            notice.SetBinding(TextBlock.TextProperty, new Binding(nameof(TextBlock.Text)) { Source = w.historyCapacityNotice, Mode = BindingMode.OneWay });
            notice.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(UIElement.Visibility)) { Source = w.historyCapacityNotice, Mode = BindingMode.OneWay });
            AutomationProperties.SetName(notice, "İşlem geçmişi kapasite uyarısı");

            // Both texts are polite live regions: the count while there are hits, the "no result" sentence when there are none.
            emptyResult = new TextBlock { Text = "Bu sözle eşleşen araç bulunamadı. Başka bir söz deneyin; örneğin: kopyala, ayna, kaydet.", FontSize = 14, Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 14, 0, 0), Visibility = Visibility.Collapsed, Focusable = true };
            AutomationProperties.SetLiveSetting(emptyResult, AutomationLiveSetting.Polite);
            resultCount = new TextBlock { FontSize = 14, Foreground = StudioTheme.Muted, Margin = new Thickness(2, 10, 0, 0), Visibility = Visibility.Collapsed, Focusable = true };
            AutomationProperties.SetLiveSetting(resultCount, AutomationLiveSetting.Polite);

            foreach (var entry in CommandCatalogue.Entries) rows[entry.Id] = BuildRow(entry);

            body = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = body, Focusable = false, Margin = new Thickness(0, 4, 0, 0) };
            var layout = new Grid();
            layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(notice, 1); Grid.SetRow(emptyResult, 2); Grid.SetRow(resultCount, 2); Grid.SetRow(scroller, 3);
            layout.Children.Add(top); layout.Children.Add(notice); layout.Children.Add(emptyResult); layout.Children.Add(resultCount); layout.Children.Add(scroller);

            root = new Border { Background = StudioTheme.Background, BorderBrush = StudioTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12, 8, 8), Child = layout, Tag = SheetTag };
            AutomationProperties.SetName(root, "Tüm araçlar");
            root.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
            root.SizeChanged += (_, e) => { var wanted = e.NewSize.Width >= 900 ? 3 : 2; if (wanted != columns) { columns = wanted; Arrange(); } };
            Grid.SetColumn(root, 0); Grid.SetColumnSpan(root, 2); System.Windows.Controls.Panel.SetZIndex(root, 50);
        }

        private Button BuildRow(CommandCatalogueEntry entry)
        {
            var description = entry.Kind == CommandCatalogueKind.Detailed ? entry.Description + " Ayrıntılı görünümde açılır." : entry.Description;
            var button = new Button
            {
                MinHeight = 44, Margin = new Thickness(0, 0, 8, 6), Padding = new Thickness(12, 6, 12, 6), Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Tag = entry,
                Content = new StackPanel { Children =
                {
                    new TextBlock { Text = entry.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = description, FontSize = 12, Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap },
                } },
            };
            AutomationProperties.SetName(button, entry.Title);
            AutomationProperties.SetHelpText(button, description);
            switch (entry.Kind)
            {
                case CommandCatalogueKind.Fill:
                    button.ToolTip = "Komut kutusuna şunu yazar: " + entry.Examples[0] + "\nSiz Enter'a basana kadar hiçbir şey değişmez.";
                    button.Click += (_, _) => Fill(entry);
                    break;
                case CommandCatalogueKind.Open:
                    BindToReal(button, entry);
                    break;
                default:
                    button.ToolTip = "Ayrıntılı görünüme geçer ve ilgili denetime gider: " + entry.DetailedControl;
                    button.Click += (_, _) => GoToDetailed(entry);
                    break;
            }
            return button;
        }

        // D: the entry mirrors the real button's enabled state and raises that button's click, so its guards and its error
        // handling (AddButton's wrapper) run exactly as for a mouse click; no handler is copied.
        private void BindToReal(Button button, CommandCatalogueEntry entry)
        {
            var real = FindClassic(w, entry.ClassicButtonLabel, entry.ClassicAccessibleName).OfType<Button>().FirstOrDefault();
            if (real is null) { button.IsEnabled = false; button.ToolTip = "Bu işlev şu anda bulunamadı."; return; }
            button.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(UIElement.IsEnabled)) { Source = real, Mode = BindingMode.OneWay });
            button.ToolTip = real.ToolTip as string is { Length: > 0 } tip ? tip : entry.Description;
            button.Click += (_, _) =>
            {
                if (!real.IsEnabled) { w.SetStatus(entry.Title + " şu anda kullanılamıyor."); return; }
                Close();
                real.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            };
        }

        private void Fill(CommandCatalogueEntry entry)
        {
            Close();
            w.simpleView?.FillCommandBox(entry.Examples[0]);
            w.SetStatus("Komut kutusuna örnek yazıldı: " + entry.Examples[0] + " · Değiştirip Enter'a basın.");
        }

        private void GoToDetailed(CommandCatalogueEntry entry)
        {
            if (!w.TrySetSimpleView(false, out var refusal)) { w.SetStatus(refusal!); return; }
            Close();
            var target = FindClassic(w, entry.DetailedControl, entry.ClassicButtonLabel).FirstOrDefault();
            if (target is null) { w.SetStatus(entry.DetailedControl + " bulunamadı."); return; }
            target.BringIntoView();
            target.Focus();
            w.SetStatus("Ayrıntılı görünüm açık · " + entry.Title);
        }

        // ---------------------------------------------------------------- arrange

        // Arrange also runs on width changes; the same result is not said twice.
        private void Say(TextBlock live, string text)
        {
            if (spoken == text) return;
            spoken = text;
            AutomationProperties.SetName(live, text);
            if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
                UIElementAutomationPeer.FromElement(live)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        private void Arrange()
        {
            if (body is null || search is null) return;
            foreach (var row in rows.Values) (row.Parent as System.Windows.Controls.Panel)?.Children.Remove(row);
            body.Children.Clear(); body.ColumnDefinitions.Clear();
            var query = search.Text.Trim();
            if (query.Length > 0)
            {
                // Ranked list, best match first, one column.
                var hits = CommandCatalogue.Search(query);
                emptyResult!.Visibility = hits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (hits.Count == 0) { resultCount!.Visibility = Visibility.Collapsed; Say(emptyResult, "Sonuç yok. " + emptyResult.Text); }
                else { resultCount!.Text = hits.Count + " sonuç"; resultCount.Visibility = Visibility.Visible; Say(resultCount, resultCount.Text); }
                var list = new StackPanel { MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
                foreach (var entry in hits) list.Children.Add(rows[entry.Id]);
                body.Children.Add(list);
                return;
            }
            emptyResult!.Visibility = Visibility.Collapsed; resultCount!.Visibility = Visibility.Collapsed; spoken = "";
            var count = Math.Max(2, columns);
            for (var i = 0; i < count; i++) body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var panels = Enumerable.Range(0, count).Select(_ => new StackPanel { Margin = new Thickness(0, 0, 4, 0) }).ToArray();
            for (var i = 0; i < count; i++) { Grid.SetColumn(panels[i], i); body.Children.Add(panels[i]); }
            var total = (double)(CommandCatalogue.Entries.Length + 2 * CommandCatalogue.Groups.Length);
            double used = 0; var column = 0;
            foreach (var group in CommandCatalogue.Groups)
            {
                var entries = CommandCatalogue.EntriesIn(group);
                if (column < count - 1 && used >= (column + 1) * total / count) column++;
                var caption = new TextBlock { Text = group.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = StudioTheme.Accent, Margin = new Thickness(2, 14, 0, 6), Focusable = true };
                AutomationProperties.SetName(caption, group.Title);
                panels[column].Children.Add(caption);
                foreach (var entry in entries) panels[column].Children.Add(rows[entry.Id]);
                used += entries.Count + 2;
            }
        }

        // ---------------------------------------------------------------- open / close

        internal void Open()
        {
            if (IsOpen) return;
            Build();
            columns = 0;
            w.workspaceGrid.Children.Add(root!);
            // The page hides the rail and the canvas; Tab and the arrow keys must not wander into them.
            foreach (UIElement under in new UIElement[] { w.railScroller, w.canvasShellBorder })
            {
                blocked.Add((under, KeyboardNavigation.GetTabNavigation(under)));
                KeyboardNavigation.SetTabNavigation(under, KeyboardNavigationMode.None);
                KeyboardNavigation.SetDirectionalNavigation(under, KeyboardNavigationMode.None);
            }
            Arrange();
            w.Dispatcher.BeginInvoke(() => search?.Focus(), System.Windows.Threading.DispatcherPriority.Input);
            w.SetStatus("Tüm araçlar açık · Aramak için yazın, kapatmak için Esc.");
        }

        internal void Close()
        {
            if (root is null || !w.workspaceGrid.Children.Contains(root)) return;
            var hadFocus = root.IsKeyboardFocusWithin;
            w.workspaceGrid.Children.Remove(root);
            foreach (var (under, mode) in blocked) { KeyboardNavigation.SetTabNavigation(under, mode); KeyboardNavigation.SetDirectionalNavigation(under, mode); }
            blocked.Clear();
            foreach (var row in rows.Values) (row.Parent as System.Windows.Controls.Panel)?.Children.Remove(row);
            rows.Clear();
            root = null; body = null; scroller = null; search = null;
            if (hadFocus) w.simpleView?.FocusAllToolsButton();
        }
    }
}
