using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TekstilCAD.Core.Theme;

namespace TekstilCAD.Desktop;

/// <summary>
/// Headless checks of the colour-token infrastructure: the tokens are shared unfrozen brushes, the dark palette equals the
/// colours the desktop had before themes, a palette switch recolours styles and templates in place and returns exactly,
/// and no colour literal is left in App.xaml or CadCanvas. Every check leaves the default dark palette applied.
/// </summary>
internal static class ThemeVerification
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    // The colours the desktop had before themes existed (see ThemePaletteCompletenessTests for the unit-test copy).
    private static readonly (string Token, string Hex)[] LegacyDark =
    [
        ("Background", "#101923"), ("Surface", "#16222F"), ("Raised", "#1D2C3C"), ("Border", "#304254"), ("Accent", "#49D8BB"),
        ("Text", "#EDF4FA"), ("Muted", "#9BADBE"), ("Warning", "#F5C377"), ("Danger", "#FF9999"), ("Selection", "#235C59"),
        ("HoverFill", "#2A3D50"), ("PressedFill", "#244D4A"), ("PrimaryFill", "#176F65"), ("PrimaryBorder", "#3CB29F"),
        ("BorderHover", "#637C91"), ("SelectionBorder", "#41877F"), ("TextSelection", "#3E8D83"), ("RowAlt", "#192737"),
        ("GridLine", "#253546"), ("ScrollThumb", "#526779"), ("ScrollThumbHover", "#7B98AB"),
        ("Canvas", "#0D1827"), ("CanvasGrid", "#203047"), ("CanvasAxis", "#43617B"), ("Piece", "#4DE1C1"), ("PieceFill", "#163D40"),
        ("PieceSelected", "#FFCB70"), ("PieceSelectedFill", "#304947"), ("PieceLabel", "#B3D2D5"), ("Notch", "#FF8FAF"),
        ("GrainOk", "#65CFFF"), ("GrainReview", "#FFC468"), ("DrawingPreview", "#FFCB70"),
    ];

    /// <summary>A palette that differs from Dark in every token, used to prove that styles and templates follow a switch.</summary>
    private static readonly ThemePalette Probe = new("Probe", ThemePalette.Tokens.ToDictionary(token => token, token => Invert(ThemePalette.Dark[token])));

    internal static void Run(MainWindow studio, string reportPath, List<string> checks)
    {
        try
        {
            VerifyTokens(checks);
            VerifyDrawingKeepsTokensUnfrozen(studio, checks);
            VerifyPaletteSwitch(studio, checks);
            VerifySourceLiterals(reportPath, checks);
        }
        finally { StudioTheme.Use(ThemePalette.Dark); }
        Check(ReferenceEquals(StudioTheme.Current, ThemePalette.Dark) && StudioTheme.Matches(ThemePalette.Dark), "Theme verification leaves the default dark palette applied", checks);
    }

    /// <summary>Called at the end of the whole run: later checks inspect drawings, which must not freeze or recolour the tokens.</summary>
    internal static void RunAtEnd(List<string> checks)
    {
        Check(ThemePalette.Tokens.All(token => !StudioTheme.TokenBrush(token).IsFrozen) && StudioTheme.Matches(ThemePalette.Dark),
            "Theme tokens are still unfrozen and dark after every other check has run", checks);
    }

    private static void Check(bool condition, string name, List<string> checks)
    {
        if (!condition) throw new InvalidOperationException("Verification failed: " + name);
        checks.Add(name);
    }

    private static void VerifyTokens(List<string> checks)
    {
        var frozen = ThemePalette.Tokens.Where(token => StudioTheme.TokenBrush(token).IsFrozen).ToArray();
        Check(frozen.Length == 0, "Theme brushes are shared unfrozen instances: " + string.Join(", ", frozen), checks);
        var resources = Application.Current.Resources;
        var unregistered = ThemePalette.Tokens.Where(token => !ReferenceEquals(resources["Studio." + token], StudioTheme.TokenBrush(token))).ToArray();
        Check(unregistered.Length == 0, "Application resources Studio tokens are the same instances as StudioTheme: " + string.Join(", ", unregistered), checks);
        var wrong = LegacyDark.Where(row => StudioTheme.TokenBrush(row.Token).Color != StudioTheme.ParseColor(row.Hex)).Select(row => row.Token).ToArray();
        Check(wrong.Length == 0 && ReferenceEquals(StudioTheme.Current, ThemePalette.Dark), "Dark palette keeps the original hex value of every existing token: " + string.Join(", ", wrong), checks);
        Check(StudioTheme.Matches(ThemePalette.Dark) && ThemePalette.Tokens.All(token => StudioTheme.TokenBrush(token).Color.A == 255), "The default palette is dark and every token is an opaque colour", checks);
        var freezable = ThemePalette.Tokens.Where(token => StudioTheme.TokenBrush(token).CanFreeze).ToArray();
        Check(freezable.Length == 0, "Theme brushes cannot be frozen, so no WPF operation can end live switching: " + string.Join(", ", freezable), checks);
    }

    private static void VerifyDrawingKeepsTokensUnfrozen(MainWindow studio, List<string> checks)
    {
        // VisualTreeHelper.GetDrawing freezes the brushes a visual drew with; the canvas must therefore draw with copies.
        var canvas = new CadCanvas(studio);
        canvas.Measure(new Size(300, 200)); canvas.Arrange(new Rect(0, 0, 300, 200)); canvas.UpdateLayout();
        _ = VisualTreeHelper.GetDrawing(canvas);
        Check(ThemePalette.Tokens.All(token => !StudioTheme.TokenBrush(token).IsFrozen),
            "Reading the drawing of the canvas leaves the theme tokens unfrozen", checks);
    }

    private static void VerifyPaletteSwitch(MainWindow studio, List<string> checks)
    {
        var instances = ThemePalette.Tokens.ToDictionary(token => token, token => StudioTheme.TokenBrush(token));
        var before = ThemePalette.Tokens.ToDictionary(token => token, token => instances[token].Color);

        var box = new TextBox(); var button = new Button { Content = "Deneme", Width = 200 }; var list = new ListBox { Height = 60 };
        var grid = new DataGrid { Height = 60, AutoGenerateColumns = true }; var check = new CheckBox { Content = "Seçenek" };
        var combo = new ComboBox { Width = 200 }; var tab = new TabControl { Height = 40 };
        var panel = new StackPanel { Margin = new Thickness(10) };
        foreach (var element in new UIElement[] { box, button, list, grid, check, combo, tab }) panel.Children.Add(element);
        var canvas = new CadCanvas(studio) { Width = 120, Height = 60, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(canvas);
        var window = new Window { Content = panel, Width = 300, Height = 640 };
        StudioTheme.Apply(window);
        var darkPixels = RenderPixels(window, 300, 640);
        Check(ColorOf(button.Background) == StudioTheme.TokenBrush("Raised").Color && ColorOf(box.Background) == StudioTheme.TokenBrush("Input").Color
            && ColorOf(list.Background) == StudioTheme.TokenBrush("Surface").Color && ColorOf(grid.Background) == StudioTheme.TokenBrush("Background").Color,
            "Implicit styles resolve their brushes from the shared tokens", checks);
        Check(ReferenceEquals(button.Background, StudioTheme.Raised) && ReferenceEquals(box.Background, StudioTheme.Input) && ReferenceEquals(list.BorderBrush, StudioTheme.BorderStrong)
            && ReferenceEquals(window.Background, StudioTheme.Background) && ReferenceEquals(window.Foreground, StudioTheme.Text),
            "Styles and windows hold the shared brush instances themselves", checks);

        StudioTheme.Use(Probe);
        Check(StudioTheme.Matches(Probe) && ReferenceEquals(StudioTheme.Current, Probe), "Using another palette gives every token its value", checks);
        Check(ThemePalette.Tokens.All(token => ReferenceEquals(StudioTheme.TokenBrush(token), instances[token]) && !instances[token].IsFrozen),
            "Palette switch keeps the same unfrozen brush instances", checks);
        var probe = ThemePalette.Tokens.ToDictionary(token => token, token => StudioTheme.ParseColor(Probe[token]));
        Check(ColorOf(button.Background) == probe["Raised"] && ColorOf(box.Background) == probe["Input"] && ColorOf(list.Background) == probe["Surface"]
            && ColorOf(grid.Background) == probe["Background"] && ColorOf(grid.AlternatingRowBackground) == probe["RowAlt"] && ColorOf(window.Background) == probe["Background"],
            "Recolouring after the first window changes Button, TextBox, ListBox and DataGrid style values", checks);
        var probePixels = RenderPixels(window, 300, 640);
        Check(PixelAt(probePixels, button, 6, 0.5) == probe["Raised"] && PixelAt(probePixels, box, 0.5, 0.5) == probe["Input"] && PixelAt(probePixels, list, 0.5, 0.5) == probe["Surface"]
            && PixelAt(probePixels, check, 0, 0.5) != PixelAt(darkPixels, check, 0, 0.5),
            "Recolouring after the first window repaints Button, TextBox, ListBox and CheckBox templates", checks);
        var canvasBackgrounds = new[] { (3, 0.5), (0.2, 0.9), (0.9, 0.2), (0.6, 0.55) };
        Check(canvasBackgrounds.Any(point => PixelAt(probePixels, canvas, point.Item1, point.Item2) == probe["Canvas"]) && canvasBackgrounds.Any(point => PixelAt(darkPixels, canvas, point.Item1, point.Item2) == StudioTheme.ParseColor(ThemePalette.Dark["Canvas"])),
            "Custom drawing on the canvas follows a palette switch", checks);
        Check(PixelAt(probePixels, button, 6, 0.5) != PixelAt(darkPixels, button, 6, 0.5) && probePixels.Data.SequenceEqual(darkPixels.Data) == false,
            "The rendered window differs after the palette switch", checks);

        StudioTheme.Use(ThemePalette.Dark);
        var restored = ThemePalette.Tokens.Where(token => StudioTheme.TokenBrush(token).Color != before[token] || !ReferenceEquals(StudioTheme.TokenBrush(token), instances[token])).ToArray();
        Check(restored.Length == 0 && StudioTheme.Matches(ThemePalette.Dark), "Palette switch keeps brush identity and returns to the dark values exactly: " + string.Join(", ", restored), checks);
        var backPixels = RenderPixels(window, 300, 640);
        Check(backPixels.Data.SequenceEqual(darkPixels.Data), "The window renders pixel for pixel as before after the switch back to dark", checks);
        window.Close();
    }

    private static void VerifySourceLiterals(string reportPath, List<string> checks)
    {
        string root;
        try { root = (string)typeof(DesktopVerification).GetMethod("RepositoryRoot", PrivateStatic)!.Invoke(null, [reportPath])!; }
        catch (Exception) { checks.Add("Colour literal scan skipped: the source tree was not found"); return; }
        var directory = Path.Combine(root, "src", "TekstilCAD.Desktop");
        if (!File.Exists(Path.Combine(directory, "App.xaml"))) { checks.Add("Colour literal scan skipped: the source tree was not found"); return; }
        var xaml = File.ReadAllText(Path.Combine(directory, "App.xaml"));
        Check(!Regex.IsMatch(xaml, "#[0-9A-Fa-f]{6,8}\\b|<SolidColorBrush") && !xaml.Contains("{StaticResource Studio.Background}", StringComparison.Ordinal),
            "App.xaml contains no colour literal and reads the Studio brushes through DynamicResource", checks);
        var canvas = File.ReadAllText(Path.Combine(directory, "CadCanvas.cs"));
        Check(!Regex.IsMatch(canvas, "\"#[0-9A-Fa-f]{3,8}\"|BrushConverter"), "CadCanvas draws only with theme tokens", checks);
        Check(!File.ReadAllText(Path.Combine(directory, "MainWindow.cs")).Contains("ThemeSettings", StringComparison.Ordinal),
            "The MainWindow constructor path never reads a saved theme preference", checks);
    }

    private sealed record Pixels(byte[] Data, int Width, FrameworkElement Root);

    private static Pixels RenderPixels(Window window, int width, int height)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        content.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var backing = new DrawingVisual();
        using (var dc = backing.RenderOpen()) dc.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        bitmap.Render(backing); bitmap.Render(content);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return new Pixels(pixels, width, content);
    }

    /// <summary>The colour of the pixel at a relative position inside an element; x is an offset in pixels when 1 or more, otherwise a fraction.</summary>
    private static Color PixelAt(Pixels image, FrameworkElement element, double x, double y)
    {
        var bounds = element.TransformToAncestor(image.Root).TransformBounds(new Rect(element.RenderSize));
        var px = (int)Math.Round(bounds.X + (x >= 1 ? x : bounds.Width * x));
        var py = (int)Math.Round(bounds.Y + bounds.Height * y);
        var index = (py * image.Width + px) * 4;
        return Color.FromRgb(image.Data[index + 2], image.Data[index + 1], image.Data[index]);
    }

    private static Color ColorOf(Brush brush) => brush is SolidColorBrush solid ? solid.Color : Colors.Transparent;

    private static string Invert(string hex)
    {
        var color = StudioTheme.ParseColor(hex);
        return $"#{255 - color.R:X2}{255 - color.G:X2}{255 - color.B:X2}";
    }
}
