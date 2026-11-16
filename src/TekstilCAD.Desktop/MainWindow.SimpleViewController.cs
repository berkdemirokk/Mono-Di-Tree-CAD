using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

public partial class MainWindow
{
    /// <summary>
    /// Adds the calm, light simple layer to the already built window and removes it again. Every property it touches goes
    /// through <see cref="ViewRecorder"/>; every control is the same instance as in the detailed layout. Controls that exist
    /// only in the simple view (command bar region, side card, rail captions, footer note) are created on the first attach
    /// and are children of the window only while the simple view is on.
    /// </summary>
    private sealed partial class SimpleViewController
    {
        private readonly MainWindow w;
        private readonly ViewRecorder recorder = new();
        private ICommandBarHost? commandBar;
        private Border? commandRegion;
        private StackPanel? noticeStrip;
        private SideCardView? card;
        private Button? switchButton, themeButton, openButton, saveButton, allToolsButton, openCancelProxy;
        private TextBlock? railCaption, railHint, footerNote, scaleWarning;
        private bool switchMounted, switchPermanent, built;
        private int textScale = 100;
        internal int TextScale => textScale;
        private readonly Dictionary<string, UIElement> toolIcons = [];

        internal SimpleViewController(MainWindow window) => w = window;

        internal bool IsAttached { get; private set; }

        /// <summary>True once the "Tüm araçlar" sheet exists (AllToolsSheet.cs); while false its button stays hidden.</summary>
        internal bool AllToolsSheetAvailable { get; set; } = true;

        private static readonly (string Tool, string Label, string Typed)[] ToolTexts =
        [
            ("Seç", "Seç", "seç aracı"), ("Çizgi", "Çizgi", "150 çizgi"), ("Çoklu çizgi", "Nokta nokta çiz", "çoklu çizgi aracı"),
            ("Dikdörtgen", "Dikdörtgen", "120 x 80 dikdörtgen"), ("Bézier", "Eğri", "eğri aracı"), ("Yay", "Yay", "yay aracı"),
        ];

        private static readonly Dictionary<string, string> IconPaths = new()
        {
            ["Seç"] = "M5,3 L5,16 L8.5,12.8 L11,18 L13,17 L10.6,12 L15,12 Z",
            ["Çizgi"] = "M4,16 L16,4",
            ["Çoklu çizgi"] = "M3,15 L8,6 L13,12 L17,4",
            ["Dikdörtgen"] = "M4,5 L16,5 L16,15 L4,15 Z",
            ["Bézier"] = "M3,15 C6,1 12,19 17,5",
            ["Yay"] = "M4,16 A 11,11 0 0 1 16,16",
        };

        // ---------------------------------------------------------------- build (once)

        private void Build()
        {
            if (built) return;
            var bar = CommandBarFactory(w);
            commandBar = bar;
            // Region under the header: [command box + help row] [Aç] [Kaydet] [Tüm araçlar].
            var row = new Grid { MaxWidth = 1100 };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.Children.Add(commandBar.Element);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 4, 0, 0) };
            openButton = Primary("Aç", "Proje aç", "Proje aç · Ctrl+O", 64, () => RaiseClick("Aç…"));
            saveButton = Primary("Kaydet", "Projeyi kaydet", "Kaydet · Ctrl+S", 92, () => RaiseClick("Kaydet"));
            allToolsButton = Primary("Tüm araçlar", "Tüm araçlar", "Programın bütün işlevleri", 136, w.ToggleAllToolsSheet);
            allToolsButton.Visibility = Visibility.Collapsed;
            openCancelProxy = Primary("Açmayı iptal et", "Açmayı iptal et", "Okuma sonucunu kullanma; mevcut çalışma korunur.", 120, () => RaiseClick("Açmayı iptal et"));
            openCancelProxy.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(UIElement.Visibility)) { Source = w.projectOpenCancel });
            buttons.Children.Add(openButton); buttons.Children.Add(saveButton); buttons.Children.Add(allToolsButton); buttons.Children.Add(openCancelProxy);
            Grid.SetColumn(buttons, 1); row.Children.Add(buttons);
            commandRegion = new Border { Margin = new Thickness(16, 6, 16, 4), Child = row };
            // The warnings of the header sit here, under the box, so the box keeps its place however many warnings there are.
            noticeStrip = new StackPanel { Margin = new Thickness(16, 0, 16, 2), MaxWidth = 1100 };
            DockPanel.SetDock(noticeStrip, Dock.Top);
            AutomationProperties.SetName(commandRegion, "Komut çubuğu");
            DockPanel.SetDock(commandRegion, Dock.Top);
            // Above the workspace, so a preview card that grows past the region is drawn and clicked over the canvas.
            System.Windows.Controls.Panel.SetZIndex(commandRegion, 1);

            switchButton = new Button { Content = "Ayrıntılı görünüm", MinHeight = 40, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(10, 0, 0, 0), FontSize = 14, Cursor = Cursors.Hand };
            AutomationProperties.SetName(switchButton, "Ayrıntılı görünüme geç");
            switchButton.ToolTip = "Yoğun, ayrıntılı ekran · F9";
            DockPanel.SetDock(switchButton, Dock.Right);
            switchButton.Click += (_, _) => w.ToggleSimpleView();

            // Same code path as the switch in the About window; the label names the theme a click would give.
            themeButton = new Button { MinHeight = 40, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(8, 0, 0, 0), FontSize = 14, Cursor = Cursors.Hand };
            DockPanel.SetDock(themeButton, Dock.Left);
            themeButton.Click += (_, _) => w.SetStatus(InterfaceSettings.ToggleTheme());

            railCaption = new TextBlock { Text = "Fareyle çiz", FontSize = 14, Foreground = StudioTheme.Muted, Margin = new Thickness(4, 4, 0, 4) };
            railHint = new TextBlock { Text = "Çizerken: Enter kapatır · Esc vazgeçer", FontSize = 14, Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(7, 8, 0, 0), Focusable = true };
            AutomationProperties.SetName(railHint, "Çizerken: Enter konturu kapatır, Esc çizimden vazgeçer");

            footerNote = new TextBlock { Text = "Bu çizim üretim veya kesim onayı değildir", FontSize = 13, Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap, MaxWidth = 280, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Focusable = true };
            AutomationProperties.SetName(footerNote, "Bu çizim üretim veya kesim onayı değildir");
            DockPanel.SetDock(footerNote, Dock.Right);
            scaleWarning = new TextBlock { Text = "Yazı büyüdükçe çalışma alanı küçülür.", FontSize = 13, Foreground = StudioTheme.Warning, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Focusable = true, TextWrapping = TextWrapping.Wrap, MaxWidth = 240 };
            AutomationProperties.SetName(scaleWarning, "Yazı büyüdükçe çalışma alanı küçülür.");
            DockPanel.SetDock(scaleWarning, Dock.Right);

            card = BuildCard();
            built = true;
        }

        private Button Primary(string text, string name, string tip, double minWidth, Action action)
        {
            var button = new Button { Content = text, Height = 44, MinWidth = minWidth, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0), FontSize = 14, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand, ToolTip = tip };
            AutomationProperties.SetName(button, name);
            button.Click += (_, _) => action();
            return button;
        }

        // Raises the click of the real button, so its guards and error handling run exactly as for a mouse click.
        private void RaiseClick(string content)
        {
            var real = FindButton(w.commandAreaPanel, content);
            if (real is { IsEnabled: true }) real.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            else w.SetStatus(content + " şu anda kullanılamıyor.");
        }

        private static Button? FindButton(DependencyObject root, string content)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                if (child is Button button && Equals(button.Content, content)) return button;
                if (FindButton(child, content) is { } found) return found;
            }
            return null;
        }





        // ---------------------------------------------------------------- attach / detach

        internal void Attach()
        {
            if (IsAttached) return;
            Build();
            IsAttached = true;
            try { AttachCore(); }
            catch
            {
                // A failed attach must not leave a half-built layer behind.
                IsAttached = false;
                try { recorder.Restore(); } catch (Exception) { /* the original failure is the one to report */ }
                throw;
            }
        }

        private void AttachCore()
        {
            var r = recorder;
            r.Set(w.unitsText, AutomationProperties.NameProperty, "");

            // Header: same instances, calmer.
            r.Set(w.headerGrid, FrameworkElement.MarginProperty, new Thickness(16, 4, 16, 2));
            r.Set(w.taglineText, UIElement.VisibilityProperty, Visibility.Collapsed);
            r.Set(w.aboutButton, FrameworkElement.MinHeightProperty, 40d);
            r.Set(w.aboutButton, Control.FontSizeProperty, 14d);
            // Hakkında first, then the view switch, both flush with the brand at the left edge.
            r.Set(w.aboutButton, DockPanel.DockProperty, Dock.Left);
            r.Set(w.aboutButton, FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            MountSwitch(r);
            r.Set(switchButton!, DockPanel.DockProperty, Dock.Left);
            r.Set(switchButton!, FrameworkElement.MarginProperty, new Thickness(0));
            r.Add(w.brandLinePanel, themeButton!, w.brandLinePanel.Children.IndexOf(switchButton!) + 1);
            Action updateTheme = UpdateThemeButton;
            r.Do(() => { StudioTheme.Changed += updateTheme; UpdateThemeButton(); }, () => StudioTheme.Changed -= updateTheme);
            r.Set(w.sourceHistoryDetails, ContentControl.ContentProperty, "Kaynak ve aktarım notları");
            if (w.modeChip.Child is TextBlock modeText)
            {
                r.Set(modeText, TextBlock.TextProperty, "Yerel çalışma  ·  mm");
                r.Set(modeText, TextBlock.FontSizeProperty, 13d);
            }
            foreach (var button in Descendants<Button>(w.projectPanel))
            {
                r.Set(button, FrameworkElement.MinHeightProperty, 40d);
                r.Set(button, Control.FontSizeProperty, 14d);
            }
            LiftToThirteen(w.projectPanel); LiftToThirteen(w.headerGrid); LiftToThirteen(w.canvasHeaderPanel); LiftToThirteen(w.toolsPanel); LiftToThirteen(w.footerPanel);
            MoveNoticesBelowBox(r);

            // Command bar region replaces the command strip.
            r.Set(w.commandAreaPanel, UIElement.VisibilityProperty, Visibility.Collapsed);
            r.Add(w.rootPanel, commandRegion!, 1);
            r.Add(w.rootPanel, noticeStrip!, 2);

            // Workspace columns.
            r.Set(w.workspaceGrid.ColumnDefinitions[0], ColumnDefinition.WidthProperty, new GridLength(176));
            r.Set(w.workspaceGrid.ColumnDefinitions[2], ColumnDefinition.WidthProperty, new GridLength(InspectorWidth(w.rootPanel.ActualWidth)));
            SizeChangedEventHandler resized = (_, e) => w.workspaceGrid.ColumnDefinitions[2].Width = new GridLength(InspectorWidth(e.NewSize.Width));
            r.Do(() => w.rootPanel.SizeChanged += resized, () => w.rootPanel.SizeChanged -= resized);

            // Tab order: header, command bar, workspace; the footer stops come last.
            r.Set(w.headerGrid, KeyboardNavigation.TabIndexProperty, 0);
            r.Set(commandRegion!, KeyboardNavigation.TabIndexProperty, 1);
            r.Set(w.workspaceGrid, KeyboardNavigation.TabIndexProperty, 2);

            AttachRail(r);
            AttachCanvasHeader(r);
            AttachInspector(r);
            AttachFooter(r);

            r.Do(() => { w.canvas.EmptyStateTitle = "Henüz parça yok."; w.canvas.EmptyStateHint = "Yukarıdaki kutuya yazın: 120 x 80 dikdörtgen"; },
                () => { w.canvas.EmptyStateTitle = null; w.canvas.EmptyStateHint = null; });
            if (textScale != 100) ApplyScale(r);
            w.canvasTool.Text = ToolHeading(w.ActiveTool);
            OnRefreshed();
        }

        // Every notice row of the header (everything after the spacer and the title row) moves under the command box and
        // comes back in place on detach, so the box does not drop when warnings appear.
        private void MoveNoticesBelowBox(ViewRecorder r)
        {
            var rows = w.projectPanel.Children.Cast<UIElement>().Skip(2).ToList();
            r.Do(() =>
            {
                foreach (var row in rows) { w.projectPanel.Children.Remove(row); noticeStrip!.Children.Add(row); }
            }, () =>
            {
                foreach (var row in rows) { noticeStrip!.Children.Remove(row); w.projectPanel.Children.Add(row); }
            });
        }

        internal void Detach()
        {
            if (!IsAttached) return;
            w.CloseAllToolsSheet();
            commandBar?.ClearPreview();
            IsAttached = false;
            try { recorder.Restore(); }
            finally
            {
                // These two texts are rewritten by the window itself while the simple view is on, so they are recomputed
                // instead of restored from a possibly outdated recording.
                w.canvasTool.Text = "KALIP ALANI   /   " + w.ActiveTool.ToUpper(CultureInfo.GetCultureInfo("tr-TR"));
                w.unitsText.Text = w.snap.IsChecked == true ? "MILIMETRE   /   1 mm yakalama" : "MILIMETRE   /   Serbest koordinat";
                // In the interactive detailed layout the switch button stays where the tagline was.
                if (switchPermanent) MountSwitchPermanently();
            }
        }

        private void UpdateThemeButton()
        {
            var name = InterfaceSettings.IsLight ? "Koyu tema" : "Açık tema";
            themeButton!.Content = name;
            AutomationProperties.SetName(themeButton, name + "ya geç");
            themeButton.ToolTip = "Koyu ve açık görünüm arasında geçer; seçiminiz saklanır";
        }

        private static double InspectorWidth(double windowWidth) => windowWidth >= 1200 || windowWidth == 0 ? 320 : 300;

        private void MountSwitch(ViewRecorder r)
        {
            if (switchMounted)
            {
                r.Set(switchButton!, ContentControl.ContentProperty, "Ayrıntılı görünüm");
                r.Set(switchButton!, AutomationProperties.NameProperty, "Ayrıntılı görünüme geç");
                r.Set(switchButton!, FrameworkElement.ToolTipProperty, "Yoğun, ayrıntılı ekran · F9");
                return;
            }
            var index = w.brandLinePanel.Children.IndexOf(w.taglineText);
            r.Add(w.brandLinePanel, switchButton!, index < 0 ? -1 : index);
            switchMounted = true;
            r.Do(() => { }, () => switchMounted = false);
        }

        /// <summary>Interactive start: the switch button replaces the tagline in the detailed layout too.</summary>
        internal void MountSwitchPermanently()
        {
            Build();
            switchPermanent = true;
            if (IsAttached || switchMounted) return;
            switchButton!.Content = "Basit görünüm";
            AutomationProperties.SetName(switchButton, "Basit görünüme geç");
            switchButton.ToolTip = "Sade, açık ekran · F9";
            var index = w.brandLinePanel.Children.IndexOf(w.taglineText);
            w.brandLinePanel.Children.Insert(index < 0 ? w.brandLinePanel.Children.Count : index, switchButton);
            w.taglineText.Visibility = Visibility.Collapsed;
            switchMounted = true;
        }

        private void AttachRail(ViewRecorder r)
        {
            var kept = w.toolButtons.Values.ToHashSet();
            foreach (UIElement child in w.toolsPanel.Children.Cast<UIElement>().ToList())
            {
                if (child is Button button && kept.Contains(button)) continue;
                if (ReferenceEquals(child, w.navigationPanel)) continue;
                r.Set(child, UIElement.VisibilityProperty, Visibility.Collapsed);
            }
            // Only zoom in and zoom out stay beside the tools; panning has the wheel and the middle button.
            var navigation = w.navigationPanel.Children.Cast<UIElement>().ToList();
            for (var i = 2; i < navigation.Count; i++) r.Set(navigation[i], UIElement.VisibilityProperty, Visibility.Collapsed);
            foreach (var button in navigation.Take(2).OfType<Button>())
            {
                r.Set(button, FrameworkElement.MinHeightProperty, 40d);
                r.Set(button, FrameworkElement.MinWidthProperty, 40d);
            }
            r.Add(w.toolsPanel, railCaption!, 0);
            r.Add(w.toolsPanel, railHint!);
            foreach (var (tool, label, typed) in ToolTexts)
            {
                var button = w.toolButtons[tool];
                r.Set(button, FrameworkElement.MinHeightProperty, 40d);
                r.Set(button, AutomationProperties.NameProperty, label);
                if (button.Content is Grid item && item.Children.Count >= 3)
                {
                    var glyph = (TextBlock)item.Children[0]; var text = (TextBlock)item.Children[1]; var key = (TextBlock)item.Children[2];
                    r.Set(glyph, UIElement.VisibilityProperty, Visibility.Collapsed);
                    r.Set(text, TextBlock.TextProperty, label);
                    r.Set(text, TextBlock.FontSizeProperty, 14d);
                    r.Set(key, TextBlock.FontSizeProperty, 13d);
                    r.Set(key, UIElement.OpacityProperty, 1.0);
                    r.Add(item, DrawnIcon(tool, item));
                }
                var hint = button.ToolTip as string ?? tool;
                r.Set(button, FrameworkElement.ToolTipProperty, hint + "\nYazarak: " + typed);
            }
        }

        private static UIElement DrawnIcon(string tool, Grid item)
        {
            var path = new Path
            {
                Data = System.Windows.Media.Geometry.Parse(IconPaths[tool]), StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Width = 20, Height = 20, Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
            };
            // The label's colour (set by the button template for hover and press as well), so icon and text always agree.
            path.SetBinding(Shape.StrokeProperty, new Binding { Path = new PropertyPath("(TextElement.Foreground)"), Source = item });
            return path;
        }

        private void AttachCanvasHeader(ViewRecorder r)
        {
            r.Set(w.undoCommand!, ContentControl.ContentProperty, "↶ Geri al");
            r.Set(w.redoCommand!, ContentControl.ContentProperty, "↷ Yinele");
            foreach (var button in new[] { w.undoCommand!, w.redoCommand!, w.fitCommand! })
            {
                r.Set(button, FrameworkElement.MinHeightProperty, 40d);
                r.Set(button, Control.FontSizeProperty, 14d);
                r.Set(button, Control.PaddingProperty, new Thickness(12, 4, 12, 4));
            }
            if (w.canvasView.Parent is UIElement chip) r.Set(chip, UIElement.VisibilityProperty, Visibility.Collapsed);
            r.Set(w.canvasTool, TextBlock.FontSizeProperty, 13d);
            r.Set(w.historyCapacityNotice, TextBlock.FontSizeProperty, 13d);
        }

        private void AttachInspector(ViewRecorder r)
        {
            var keep = new HashSet<UIElement> { w.gradingGeometryNotice, w.pieceLimits, w.propertyDraftConflict, w.grainlineStatus };
            foreach (UIElement child in w.inspectorPanel.Children.Cast<UIElement>().ToList())
            {
                if (keep.Contains(child)) continue;
                r.Set(child, UIElement.VisibilityProperty, Visibility.Collapsed);
            }
            r.Add(w.inspectorPanel, card!, 0);
            r.Add(w.inspectorPanel, card!.FirstStepsPanel);
            foreach (var warning in new TextBlock[] { w.gradingGeometryNotice, w.pieceLimits, w.grainlineStatus })
                r.Set(warning, TextBlock.FontSizeProperty, 13d);
            r.Set(w.propertyDraftConflict, Control.FontSizeProperty, 13d);
            r.Set(w.grainlineStatus, UIElement.VisibilityProperty, Visibility.Collapsed);
            r.Set(w.sourceHistoryNotice, UIElement.VisibilityProperty, Visibility.Visible);
            r.Set(w.sourceHistoryDetails, UIElement.VisibilityProperty, Visibility.Visible);
        }

        private void AttachFooter(ViewRecorder r)
        {
            r.Set(w.statusBarBorder, Border.PaddingProperty, new Thickness(18, 4, 18, 4));
            r.Set(w.statusViewer, FrameworkElement.MaxHeightProperty, 36d);
            r.Set(w.status, TextBlock.FontSizeProperty, 13d);
            // The cursor position rewrites the status on every mouse move; only meaningful messages are live in this view.
            r.Set(w.status, AutomationProperties.LiveSettingProperty, AutomationLiveSetting.Off);
            r.Set(w.unitsText, TextBlock.FontSizeProperty, 13d);
            r.Set(w.unitsText, TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            r.Set(w.unitsText, FrameworkElement.MaxWidthProperty, 330d);
            r.Set(w.unitsText, UIElement.FocusableProperty, true);
            r.Add(w.footerPanel, footerNote!, 1);
        }

        private void ApplyScale(ViewRecorder r)
        {
            var scale = textScale / 100.0;
            r.Set(w.rootPanel, FrameworkElement.LayoutTransformProperty, new ScaleTransform(scale, scale));
            if (textScale > 115) r.Add(w.footerPanel, scaleWarning!, 2);
        }

        /// <summary>Text size in percent (100, 115, 130); takes effect now when the simple view is on, else at the next attach.</summary>
        internal void SetTextScale(int percent)
        {
            textScale = percent is 115 or 130 ? percent : 100;
            if (!IsAttached) return;
            Detach();
            Attach();
        }

        // Nothing below 13 px in this view: lifts the smaller text sizes of existing controls.
        private void LiftToThirteen(DependencyObject root)
        {
            foreach (var element in Descendants<DependencyObject>(root))
            {
                if (element is TextBlock text && text.FontSize < 13) recorder.Set(text, TextBlock.FontSizeProperty, 13d);
                else if (element is Control control && control.FontSize < 13) recorder.Set(control, Control.FontSizeProperty, 13d);
            }
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                if (child is T match) yield return match;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }

        // ---------------------------------------------------------------- state

        internal string ToolHeading(string tool) => "Kalıp alanı · " + (ToolTexts.FirstOrDefault(t => t.Tool == tool).Label ?? tool);

        internal void FocusSwitch() => switchButton?.Focus();

        /// <summary>Fills the command box with text (never applies it) and moves the focus there.</summary>
        internal void FillCommandBox(string text)
        {
            if (!IsAttached) return;
            commandBar!.SetText(text);
            commandBar.FocusBox();
        }

        internal void FocusAllToolsButton() => allToolsButton?.Focus();

        internal bool FocusCommandBar()
        {
            if (!IsAttached) return false;
            commandBar!.FocusBox();
            return true;
        }

        internal void OnRefreshed()
        {
            Observe();
            if (!IsAttached) return;
            var selected = w.Selected is { Points.Count: > 0 } piece ? piece : null;
            var pieceCount = w.Document.Pieces.Count;

            // The source-history sentence and its button mean nothing to a project that has no import history.
            var visible = w.Document.ImportProvenance is not null;
            w.sourceHistoryNotice.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            w.sourceHistoryDetails.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            // The "no grainline" line is noise; the unverified-production sentence only exists when there is a grainline.
            w.grainlineStatus.Visibility = selected?.Grainline is not null ? Visibility.Visible : Visibility.Collapsed;

            w.unitsText.Text = w.snap.IsChecked == true
                ? "Ölçü birimi: milimetre (mm) · Fareyle çizerken 1 mm adımlarla"
                : "Ölçü birimi: milimetre (mm) · Fareyle çizerken serbest";
            AutomationProperties.SetName(w.unitsText, w.unitsText.Text);

            var unsaved = w.HasUnsavedWork;
            // Filled while there is unsaved work, otherwise the same plain look as Aç.
            if (unsaved)
            {
                saveButton!.Background = StudioTheme.PrimaryFill; saveButton.Foreground = StudioTheme.OnPrimary; saveButton.BorderBrush = StudioTheme.PrimaryBorder;
            }
            else
            {
                saveButton!.ClearValue(Control.BackgroundProperty); saveButton.ClearValue(Control.ForegroundProperty); saveButton.ClearValue(Control.BorderBrushProperty);
            }
            allToolsButton!.Visibility = AllToolsSheetAvailable ? Visibility.Visible : Visibility.Collapsed;

            UpdateCard(selected, pieceCount, unsaved);
            // The start tasks of the side card offer the same actions as the suggestion chips.
            commandBar!.ShowSuggestions(!card!.StartTasksVisible);
            commandBar.OnRefreshed();
        }

    }
}
