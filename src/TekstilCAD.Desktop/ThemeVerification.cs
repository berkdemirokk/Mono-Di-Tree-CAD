using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Desktop;

/// <summary>
/// Headless checks of the colour theme: palette contrast, token wiring, live switching, the saved preference,
/// the absence of stray colour literals and light renders of the main window and the main tool windows.
/// Every check leaves the application in the default Dark theme.
/// </summary>
internal static class ThemeVerification
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    // Text and graphics pairs that must reach WCAG 2 contrast in the light palette: 4.5 for text, 3 for controls and graphics.
    // A pair is (foreground token, background token, minimum ratio). The dark palette is the historical one and is not remediated.
    private static readonly (string Foreground, string Background, double Minimum)[] LightPairs = BuildPairs();

    private static (string, string, double)[] BuildPairs()
    {
        var pairs = new List<(string, string, double)>();
        void Text(string fg, params string[] backgrounds) { foreach (var bg in backgrounds) pairs.Add((fg, bg, 4.5)); }
        void Graphic(string fg, params string[] backgrounds) { foreach (var bg in backgrounds) pairs.Add((fg, bg, 3.0)); }
        Text("Text", "Background", "Surface", "Raised", "HoverFill", "PressedFill", "AccentSoft", "Selection", "TextSelection", "SuccessSoft", "WarningSoft", "DangerSoft", "RowAlt", "Input");
        Text("Muted", "Background", "Surface", "Raised", "Input", "AccentSoft", "Canvas", "Selection", "HoverFill", "PressedFill", "RowAlt");
        Text("Placeholder", "Input", "Surface");
        Text("Accent", "Background", "Surface", "Raised", "AccentSoft", "Input", "Canvas");
        Text("OnAccent", "Accent", "AccentHover", "AccentPressed", "Danger");
        Text("OnPrimary", "PrimaryFill");
        Text("Success", "Surface", "Background", "SuccessSoft");
        Text("Warning", "Surface", "Background", "Raised", "WarningSoft");
        Text("Danger", "Surface", "Background", "Raised", "DangerSoft", "Input");
        Text("TooltipText", "TooltipBg");
        Text("PieceLabel", "Canvas", "PieceFill", "PieceHoverFill");
        Text("PieceLabelSelected", "Canvas", "PieceSelectedFill");
        Text("Notch", "Canvas");      // the canvas also prints marker captions in these colours
        Text("GrainOk", "Canvas");
        Text("GrainReview", "Canvas");
        Text("PreviewLabel", "Raised");
        Graphic("DrawingPreview", "Canvas");
        Graphic("BorderStrong", "Surface", "Background", "Raised", "Input");
        Graphic("BorderHover", "Surface", "Background");
        Graphic("Focus", "Surface", "Background", "Canvas", "Input");
        Graphic("SelectionBorder", "Surface");
        Graphic("PrimaryBorder", "Surface");
        Graphic("Piece", "Canvas", "PieceFill");
        Graphic("PieceSelected", "Canvas", "PieceSelectedFill");
        Graphic("PieceHover", "Canvas", "PieceHoverFill");
        Graphic("ScrollbarThumb", "Background", "Surface");
        Graphic("ScrollbarThumbHover", "Background");
        Graphic("PreviewSource", "Raised");
        Graphic("PreviewPlot", "Raised", "Background");
        Graphic("PreviewMarker", "Raised");
        Graphic("PreviewOutline", "Raised");
        Graphic("PreviewLine", "Raised");
        Graphic("PreviewStart", "Raised");
        Graphic("PreviewEnd", "Raised");
        Graphic("PreviewGuide", "Background");
        Graphic("PreviewPiece", "Background");
        Graphic("PreviewRing", "Background");
        Graphic("PreviewFrame", "Background");
        Graphic("IllustrationStroke", "Surface");
        Graphic("IllustrationDetail", "IllustrationFill");
        return pairs.ToArray();
    }

    // The colours the dark theme had before themes existed; the default palette must reproduce them exactly.
    private static readonly (string Token, string Hex)[] LegacyDark =
    [
        ("Background", "#101923"), ("Surface", "#16222F"), ("Raised", "#1D2C3C"), ("Border", "#304254"), ("Accent", "#49D8BB"),
        ("Text", "#EDF4FA"), ("Muted", "#9BADBE"), ("Warning", "#F5C377"), ("Danger", "#FF9999"), ("Selection", "#235C59"),
        ("HoverFill", "#2A3D50"), ("PressedFill", "#244D4A"), ("PrimaryFill", "#176F65"), ("PrimaryBorder", "#3CB29F"),
        ("BorderHover", "#637C91"), ("SelectionBorder", "#41877F"), ("TextSelection", "#3E8D83"), ("RowAlt", "#192737"),
        ("GridLine", "#253546"), ("ScrollbarThumb", "#526779"), ("ScrollbarThumbHover", "#7B98AB"),
        ("Canvas", "#0D1827"), ("CanvasGrid", "#203047"), ("CanvasAxis", "#43617B"), ("Piece", "#4DE1C1"), ("PieceFill", "#163D40"),
        ("PieceSelected", "#FFCB70"), ("PieceSelectedFill", "#304947"), ("PieceLabel", "#B3D2D5"), ("Notch", "#FF8FAF"),
        ("GrainOk", "#65CFFF"), ("GrainReview", "#FFC468"), ("DrawingPreview", "#FFCB70"),
        ("PreviewSource", "#00BFFF"), ("PreviewPlot", "#FFA500"), ("PreviewMarker", "#FFD700"), ("PreviewGuide", "#DAA520"),
        ("PreviewPiece", "#48D1CC"), ("PreviewRing", "#FFFF00"), ("PreviewFrame", "#708090"), ("PreviewOutline", "#696969"),
    ];

    // Files that still contain colours on purpose: self-contained drawing surfaces that paint their own background.
    private static readonly string[] LiteralAllowList =
    [
        "StudioTheme.cs", "DesktopVerification.cs", "GroupMoveVerification.cs", "ThemeVerification.cs",
        "BulkGradePreview.cs", "PointMeasurePreview.cs", "OffsetTriplePreview.cs", "OffsetUnverifiedPreviewWindow.cs",
        "OpenPathClosureWindow.cs", "OpenPathJoinWindow.cs", "SeamLengthComparisonWindow.cs",
    ];

    internal static void Run(MainWindow studio, string work, string reportPath, List<string> checks)
    {
        try
        {
            VerifyTokens(checks);
            VerifyContrast(checks);
            VerifyPreference(work, checks);
            VerifyLiveSwitching(studio, checks);
            VerifySourceLiterals(reportPath, checks);
            VerifyLightRenders(studio, work, reportPath, checks);
        }
        finally { StudioTheme.Use(ThemeKind.Dark); }
        Check(StudioTheme.Current == ThemeKind.Dark && StudioTheme.PaletteMatches(ThemeKind.Dark), "Theme verification leaves the default dark palette applied", checks);
    }

    private static void Check(bool condition, string name, List<string> checks)
    {
        if (!condition) throw new InvalidOperationException("Verification failed: " + name);
        checks.Add(name);
    }

    private static void VerifyTokens(List<string> checks)
    {
        Check(StudioTheme.Current == ThemeKind.Dark && StudioTheme.PaletteMatches(ThemeKind.Dark), "Dark is the default palette and every token has its dark value", checks);
        var wrong = LegacyDark.Where(row => StudioTheme.PaletteColor(row.Token, ThemeKind.Dark) != (Color)ColorConverter.ConvertFromString(row.Hex)).Select(row => row.Token).ToArray();
        Check(wrong.Length == 0, "The dark palette reproduces the colours the desktop had before themes: " + string.Join(", ", wrong), checks);
        Check(StudioTheme.TokenNames.All(name => !StudioTheme.TokenBrush(name).IsFrozen && StudioTheme.TokenBrush(name).CanFreeze),
            "Every theme token is one shared brush that stays unfrozen so it can change colour in place", checks);
        var resources = Application.Current.Resources;
        var unregistered = StudioTheme.TokenNames.Where(name => !ReferenceEquals(resources["Studio." + name], StudioTheme.TokenBrush(name))).ToArray();
        Check(unregistered.Length == 0, "App resources expose the shared token brushes for the DynamicResource styles: " + string.Join(", ", unregistered), checks);
        var sameLight = StudioTheme.TokenNames.Where(name => StudioTheme.PaletteColor(name, ThemeKind.Light) == StudioTheme.PaletteColor(name, ThemeKind.Dark)).ToArray();
        // Tokens whose value legitimately matches in both palettes (a dark border that equals a control border, etc.) are few.
        Check(sameLight.Length <= 4, "The light palette differs from the dark one for nearly every token (same: " + string.Join(", ", sameLight) + ")", checks);
    }

    private static void VerifyContrast(List<string> checks)
    {
        var failures = new List<string>();
        foreach (var (foreground, background, minimum) in LightPairs)
        {
            var ratio = ContrastRatio(StudioTheme.PaletteColor(foreground, ThemeKind.Light), StudioTheme.PaletteColor(background, ThemeKind.Light));
            if (ratio < minimum) failures.Add($"{foreground} on {background} {ratio:0.00} < {minimum}");
        }
        Check(failures.Count == 0, $"Light palette meets WCAG contrast for all {LightPairs.Length} text and control pairs" + (failures.Count == 0 ? "" : ": " + string.Join("; ", failures)), checks);
        var darkTextPairs = new[] { ("Text", "Background"), ("Text", "Surface"), ("Muted", "Surface"), ("Accent", "Surface"), ("Warning", "Surface"), ("Danger", "Surface") };
        var darkFailures = darkTextPairs.Where(pair => ContrastRatio(StudioTheme.PaletteColor(pair.Item1, ThemeKind.Dark), StudioTheme.PaletteColor(pair.Item2, ThemeKind.Dark)) < 4.5).ToArray();
        Check(darkFailures.Length == 0, "Dark palette keeps its main text pairs above 4.5:1", checks);
    }

    private static void VerifyPreference(string work, List<string> checks)
    {
        var directory = Path.Combine(work, "theme-settings");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ThemeSettings.FileName);
        Check(ThemeSettings.Load(path) == ThemeKind.Dark, "A missing theme file means the dark theme", checks);
        Check(ThemeSettings.Save(ThemeKind.Light, path) && ThemeSettings.Load(path) == ThemeKind.Light, "The light theme choice is saved and read back", checks);
        Check(ThemeSettings.Save(ThemeKind.Dark, path) && ThemeSettings.Load(path) == ThemeKind.Dark, "The dark theme choice is saved and read back", checks);
        File.WriteAllText(path, "{ not json");
        Check(ThemeSettings.Load(path) == ThemeKind.Dark, "A corrupt theme file is ignored", checks);
        File.WriteAllText(path, "{\"Theme\":\"Neon\"}");
        Check(ThemeSettings.Load(path) == ThemeKind.Dark, "An unknown theme name is ignored", checks);
        File.WriteAllText(path, new string(' ', 10000) + "{\"Theme\":\"Light\"}");
        Check(ThemeSettings.Load(path) == ThemeKind.Dark, "An oversized theme file is ignored", checks);
        Check(ThemeSettings.Save(ThemeKind.Light, path) && Directory.GetFiles(directory).Length == 1, "Saving the theme replaces the file without leaving temporary files", checks);
        Check(!ThemeSettings.Save(ThemeKind.Light, Path.Combine(path, "inside-a-file", ThemeSettings.FileName)), "A theme file that cannot be written is reported, not thrown", checks);
    }

    private static void VerifyLiveSwitching(MainWindow studio, List<string> checks)
    {
        var before = StudioTheme.TokenNames.ToDictionary(name => name, name => StudioTheme.TokenBrush(name).Color);
        var instances = StudioTheme.TokenNames.ToDictionary(name => name, name => (object)StudioTheme.TokenBrush(name));
        var about = new AboutWindow(studio);
        try
        {
            var box = about.DataPath;
            var button = about.OpenData;
            _ = (FrameworkElement)about.Content;
            Render(about, Path.Combine(Path.GetTempPath(), "tekstilcad-theme-live-dark.png"), 580, 600, save: false);
            Check(ColorOf(box.Background) == StudioTheme.PaletteColor("Input", ThemeKind.Dark) && ColorOf(button.Background) == StudioTheme.PaletteColor("Raised", ThemeKind.Dark)
                && ColorOf(about.Background) == StudioTheme.PaletteColor("Background", ThemeKind.Dark), "An open window starts in the dark palette", checks);

            StudioTheme.Use(ThemeKind.Light);
            Check(StudioTheme.Current == ThemeKind.Light && StudioTheme.PaletteMatches(ThemeKind.Light), "Using the light theme gives every token its light value", checks);
            Check(ColorOf(box.Background) == StudioTheme.PaletteColor("Input", ThemeKind.Light) && ColorOf(button.Background) == StudioTheme.PaletteColor("Raised", ThemeKind.Light)
                && ColorOf(about.Background) == StudioTheme.PaletteColor("Background", ThemeKind.Light) && ColorOf(about.Foreground) == StudioTheme.PaletteColor("Text", ThemeKind.Light),
                "A window that is already open takes the new theme without being rebuilt (styles, window and code-built brushes)", checks);
            Check(ColorOf(about.VersionText.Foreground) == StudioTheme.PaletteColor("Text", ThemeKind.Light) && about.Status.Foreground == StudioTheme.Muted,
                "Code-built controls keep the shared token brush so they follow a switch", checks);

            about.ThemeToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(StudioTheme.Current == ThemeKind.Dark && Equals(about.ThemeToggle.Content, "Görünüm: Koyu") && ThemeSettings.Load() == ThemeKind.Dark,
                "The About window toggle returns to dark and saves the choice", checks);
            about.ThemeToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(StudioTheme.Current == ThemeKind.Light && Equals(about.ThemeToggle.Content, "Görünüm: Açık") && ThemeSettings.Load() == ThemeKind.Light
                && about.Status.Text.Contains("Açık görünüm", StringComparison.Ordinal), "The About window toggle switches to light, saves it and announces it", checks);
            Check(AutomationProperties.GetHelpText(about.ThemeToggle).Length > 0 && about.ThemeToggle.Focusable && AutomationProperties.GetName(about.ThemeToggle) == "Görünüm: Açık",
                "The theme toggle is keyboard-focusable and named for assistive technology", checks);

            StudioTheme.Use(ThemeKind.Dark);
            ThemeSettings.Save(ThemeKind.Dark);
            var restored = StudioTheme.TokenNames.Where(name => StudioTheme.TokenBrush(name).Color != before[name] || !ReferenceEquals(StudioTheme.TokenBrush(name), instances[name])).ToArray();
            Check(restored.Length == 0, "Switching back to dark restores identical brush values on the same brush instances: " + string.Join(", ", restored), checks);
            Check(ColorOf(box.Background) == StudioTheme.PaletteColor("Input", ThemeKind.Dark) && ColorOf(about.Background) == before["Background"], "A window that was open during both switches is dark again", checks);
        }
        finally { StudioTheme.Use(ThemeKind.Dark); about.Close(); }
    }

    private static void VerifySourceLiterals(string reportPath, List<string> checks)
    {
        string root;
        try { root = (string)typeof(DesktopVerification).GetMethod("RepositoryRoot", PrivateStatic)!.Invoke(null, [reportPath])!; }
        catch (Exception) { checks.Add("Colour literal scan skipped: the source tree was not found"); return; }
        var directory = Path.Combine(root, "src", "TekstilCAD.Desktop");
        if (!Directory.Exists(directory)) { checks.Add("Colour literal scan skipped: the source tree was not found"); return; }
        var literal = new Regex("\"#[0-9A-Fa-f]{3,8}\"|Color\\.From(Rgb|Argb|ScRgb)\\b|new SolidColorBrush\\b|\\bBrushes\\.(?!Transparent\\b)[A-Z]|\\bColors\\.[A-Z]|BrushConverter", RegexOptions.CultureInvariant);
        var offenders = new List<string>();
        var scanned = 0;
        foreach (var file in Directory.GetFiles(directory, "*.cs"))
        {
            if (LiteralAllowList.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)) continue;
            scanned++;
            var count = literal.Matches(File.ReadAllText(file)).Count;
            if (count > 0) offenders.Add(Path.GetFileName(file) + " (" + count + ")");
        }
        var xaml = File.ReadAllText(Path.Combine(directory, "App.xaml"));
        if (Regex.IsMatch(xaml, "#[0-9A-Fa-f]{6,8}\\b|<SolidColorBrush")) offenders.Add("App.xaml");
        Check(scanned > 30 && offenders.Count == 0, $"No colour literal remains in {scanned} converted source files or App.xaml: " + string.Join(", ", offenders), checks);
        Check(!File.ReadAllText(Path.Combine(directory, "MainWindow.cs")).Contains("ThemeSettings", StringComparison.Ordinal),
            "The MainWindow constructor path never reads the saved theme preference", checks);
    }

    private static void VerifyLightRenders(MainWindow studio, string work, string reportPath, List<string> checks)
    {
        StudioTheme.Use(ThemeKind.Light);
        var queue = new SimulationQueue(Path.Combine(work, "theme-spool"));
        var windows = new List<(string Name, Window Window)>
        {
            ("main", studio),
            ("textile-tools", new TextileToolsWindow(studio)),
            ("machine-center", new MachineCenterWindow(studio, queue)),
            ("about", new AboutWindow(studio)),
            ("design-request", new DesignRequestWindow(studio)),
            ("production-nesting", new ProductionNestingWindow(studio, studio)),
        };
        var report = new StringBuilder();
        var below = 0;
        try
        {
            foreach (var (name, window) in windows)
            {
                foreach (var (width, height) in new[] { (1024, 640), (1440, 900) })
                {
                    var path = Path.ChangeExtension(reportPath, $".light-{name}-{width}x{height}.png");
                    var luminance = Render(window, path, width, height, save: true);
                    Check(luminance > 0.5, $"The {name} window renders as a light surface in the light theme at {width}x{height} (mean luminance {luminance:0.00})", checks);
                }
                below += SweepText(name, window, report);
            }
            File.WriteAllText(Path.ChangeExtension(reportPath, ".light-text-sweep.txt"), report.ToString());
            // Informational for now: the sweep lists text whose own foreground and nearest opaque background are below 4.5:1.
            checks.Add($"Light text sweep over the main and tool windows lists {below} text blocks below 4.5:1 in the light-text-sweep file");
        }
        finally
        {
            StudioTheme.Use(ThemeKind.Dark);
            foreach (var (_, window) in windows) if (!ReferenceEquals(window, studio)) window.Close();
        }
    }

    /// <summary>Lists visible text blocks whose solid foreground is below 4.5:1 against the nearest opaque background above them.</summary>
    private static int SweepText(string name, Window window, StringBuilder report)
    {
        var count = 0;
        var root = (FrameworkElement)window.Content;
        foreach (var text in Descendants<TextBlock>(root))
        {
            if (string.IsNullOrWhiteSpace(text.Text) || !Shown(text) || text.Foreground is not SolidColorBrush foreground || foreground.Color.A != 255) continue;
            var background = NearestBackground(text, window);
            var ratio = ContrastRatio(foreground.Color, background);
            if (ratio >= 4.5) continue;
            count++;
            report.AppendLine($"{name}\t{ratio:0.00}\tfg {foreground.Color}\tbg {background}\t{text.Text.Replace('\n', ' ')[..Math.Min(60, text.Text.Length)]}");
        }
        return count;
    }

    private static bool Shown(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement ui && (ui.Visibility != Visibility.Visible || ui.Opacity < 0.5)) return false;
        return true;
    }

    private static Color NearestBackground(DependencyObject start, Window window)
    {
        for (var current = VisualTreeHelper.GetParent(start); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            Brush? brush = current switch { Border border => border.Background, Panel panel => panel.Background, Control control => control.Background, _ => null };
            if (brush is SolidColorBrush solid && solid.Color.A == 255 && solid.Opacity >= 1) return solid.Color;
        }
        return ColorOf(window.Background);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    /// <summary>Lays the window out at a size, optionally saves the PNG through the shared renderer, and returns the mean relative luminance of the image.</summary>
    private static double Render(Window window, string path, int width, int height, bool save)
    {
        if (save) typeof(DesktopVerification).GetMethod("Render", PrivateStatic)!.Invoke(null, [window, path, width, height]);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var backing = new DrawingVisual();
        using (var dc = backing.RenderOpen()) dc.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        bitmap.Render(backing); bitmap.Render(content);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        double sum = 0; var samples = 0;
        for (var i = 0; i < pixels.Length; i += 16) { sum += Luminance(pixels[i + 2], pixels[i + 1], pixels[i]); samples++; }
        return sum / Math.Max(1, samples);
    }

    private static Color ColorOf(Brush brush) => brush is SolidColorBrush solid ? solid.Color : Colors.Transparent;

    private static double Luminance(byte r, byte g, byte b)
    {
        static double Channel(byte value) { var c = value / 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    internal static double ContrastRatio(Color first, Color second)
    {
        var a = Luminance(first.R, first.G, first.B); var b = Luminance(second.R, second.G, second.B);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
