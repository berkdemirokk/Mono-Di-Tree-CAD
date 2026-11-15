using System.Windows;
using System.Windows.Media;

namespace TekstilCAD.Desktop;

/// <summary>The two complete palettes. Dark is the default and reproduces the original colours exactly.</summary>
public enum ThemeKind { Dark, Light }

/// <summary>
/// Shared visual tokens for the native desktop shell and tool windows.
/// Every token is ONE shared, unfrozen <see cref="SolidColorBrush"/>; <see cref="Use"/> changes the colour of
/// those instances, so every control that holds a token repaints at once, in windows that are already open as well
/// as in windows opened later. Nothing reads a palette at construction time, which keeps the constructor of
/// <c>MainWindow</c> (and therefore every headless check) independent of the saved preference.
/// The same instances are published to <c>Application.Resources</c> under the keys <c>Studio.&lt;Token&gt;</c>
/// (<see cref="RegisterResources"/>), which App.xaml reads with <c>DynamicResource</c>.
/// The brushes are bound to the UI thread; do not use them from background threads.
/// </summary>
public static class StudioTheme
{
    // Token table: name, dark value, light value. Dark values marked (=) are the literals that existed before themes.
    // Dark values of tokens that no control used before are tentative (nothing renders them yet).
    private static readonly (string Name, string Dark, string Light)[] Table =
    [
        // Surfaces and text (all existed before).
        ("Background", "#101923", "#F3F6F9"),
        ("Surface", "#16222F", "#FFFFFF"),
        ("Raised", "#1D2C3C", "#E9EEF3"),
        ("Border", "#304254", "#D3DBE3"),
        ("Accent", "#49D8BB", "#0B645C"),
        ("Text", "#EDF4FA", "#16222E"),
        ("Muted", "#9BADBE", "#4D5D6E"),
        ("Warning", "#F5C377", "#85470A"),
        ("Danger", "#FF9999", "#B42318"),
        ("Selection", "#235C59", "#D4EDE9"),
        // Control states that used to be literals in App.xaml (=).
        ("BorderStrong", "#304254", "#6F8093"),
        ("BorderHover", "#637C91", "#5B6C7F"),
        ("Input", "#101923", "#FFFFFF"),
        ("OnAccent", "#101923", "#FFFFFF"),
        ("OnPrimary", "#EDF4FA", "#FFFFFF"),
        ("PrimaryFill", "#176F65", "#0F766E"),
        ("PrimaryBorder", "#3CB29F", "#0B5F58"),
        ("SelectionBorder", "#41877F", "#2F8F85"),
        ("TextSelection", "#3E8D83", "#9AD6CE"),
        ("HoverFill", "#2A3D50", "#DDE5EC"),
        ("PressedFill", "#244D4A", "#CCD7E1"),
        ("RowAlt", "#192737", "#F8FAFC"),
        ("GridLine", "#253546", "#E1E7ED"),
        ("ScrollbarThumb", "#526779", "#76879A"),
        ("ScrollbarThumbHover", "#7B98AB", "#5B6C7F"),
        ("TooltipBg", "#1D2C3C", "#1F2D3A"),
        ("TooltipText", "#EDF4FA", "#FFFFFF"),
        // Tokens for the planned simple view; no control uses them yet (dark values are proposals).
        ("Placeholder", "#8A9DB0", "#5A6A7B"),
        ("AccentHover", "#6FE6CF", "#0B5F58"),
        ("AccentPressed", "#2FBFA3", "#084C47"),
        ("AccentSoft", "#1B3B3A", "#DDF3F0"),
        ("Success", "#7FDDA5", "#166534"),
        ("SuccessSoft", "#15332A", "#E1F5E7"),
        ("WarningSoft", "#3A3022", "#FFF1CF"),
        ("DangerSoft", "#3C2327", "#FDE7E4"),
        ("Focus", "#49D8BB", "#0B645C"),
        // Drawing canvas (dark values are the literals CadCanvas used to contain (=)).
        ("Canvas", "#0D1827", "#FAFBFC"),
        ("CanvasGrid", "#203047", "#E3E8EE"),
        ("CanvasAxis", "#43617B", "#B4C0CC"),
        ("Piece", "#4DE1C1", "#0F766E"),
        ("PieceFill", "#163D40", "#DFF2EF"),
        ("PieceSelected", "#FFCB70", "#B45309"),
        ("PieceSelectedFill", "#304947", "#FFEECB"),
        ("PieceHover", "#7AF0D6", "#0B5F58"),
        ("PieceHoverFill", "#1D4A4D", "#CBE9E4"),
        ("PieceLabel", "#B3D2D5", "#24424A"),
        ("PieceLabelSelected", "#FFCB70", "#7A3A00"),
        ("Notch", "#FF8FAF", "#BE185D"),
        ("GrainOk", "#65CFFF", "#0369A1"),
        ("GrainReview", "#FFC468", "#B45309"),
        ("DrawingPreview", "#FFCB70", "#B45309"),
        // Drawing surfaces inside tool windows (dark values are the named colours they used before (=)).
        ("PreviewSource", "#00BFFF", "#0369A1"),
        ("PreviewPlot", "#FFA500", "#B45309"),
        ("PreviewMarker", "#FFD700", "#7A3A00"),
        ("PreviewLabel", "#FFFFFF", "#16222E"),
        ("PreviewOutline", "#696969", "#4D5D6E"),
        ("PreviewGuide", "#DAA520", "#8A5A00"),
        ("PreviewPiece", "#48D1CC", "#0F766E"),
        ("PreviewRing", "#FFFF00", "#A16207"),
        ("PreviewFrame", "#708090", "#5B6C7F"),
        ("PreviewLine", "#DC143C", "#B42318"),
        ("PreviewStart", "#4169E1", "#1D4ED8"),
        ("PreviewEnd", "#006400", "#166534"),
        ("IllustrationFill", "#9EC8C0", "#8FC7BE"),
        ("IllustrationStroke", "#D1E8E2", "#0F766E"),
        ("IllustrationDetail", "#356260", "#24424A"),
    ];

    private static readonly Dictionary<string, SolidColorBrush> Shared = CreateBrushes();

    public static Brush Background { get; } = Shared[nameof(Background)];
    public static Brush Surface { get; } = Shared[nameof(Surface)];
    public static Brush Raised { get; } = Shared[nameof(Raised)];
    public static Brush Border { get; } = Shared[nameof(Border)];
    public static Brush Accent { get; } = Shared[nameof(Accent)];
    public static Brush Text { get; } = Shared[nameof(Text)];
    public static Brush Muted { get; } = Shared[nameof(Muted)];
    public static Brush Warning { get; } = Shared[nameof(Warning)];
    public static Brush Danger { get; } = Shared[nameof(Danger)];
    public static Brush Selection { get; } = Shared[nameof(Selection)];
    public static Brush BorderStrong { get; } = Shared[nameof(BorderStrong)];
    public static Brush BorderHover { get; } = Shared[nameof(BorderHover)];
    public static Brush Input { get; } = Shared[nameof(Input)];
    public static Brush OnAccent { get; } = Shared[nameof(OnAccent)];
    public static Brush OnPrimary { get; } = Shared[nameof(OnPrimary)];
    public static Brush PrimaryFill { get; } = Shared[nameof(PrimaryFill)];
    public static Brush PrimaryBorder { get; } = Shared[nameof(PrimaryBorder)];
    public static Brush SelectionBorder { get; } = Shared[nameof(SelectionBorder)];
    public static Brush TextSelection { get; } = Shared[nameof(TextSelection)];
    public static Brush HoverFill { get; } = Shared[nameof(HoverFill)];
    public static Brush PressedFill { get; } = Shared[nameof(PressedFill)];
    public static Brush RowAlt { get; } = Shared[nameof(RowAlt)];
    public static Brush GridLine { get; } = Shared[nameof(GridLine)];
    public static Brush ScrollbarThumb { get; } = Shared[nameof(ScrollbarThumb)];
    public static Brush ScrollbarThumbHover { get; } = Shared[nameof(ScrollbarThumbHover)];
    public static Brush TooltipBg { get; } = Shared[nameof(TooltipBg)];
    public static Brush TooltipText { get; } = Shared[nameof(TooltipText)];
    public static Brush Placeholder { get; } = Shared[nameof(Placeholder)];
    public static Brush AccentHover { get; } = Shared[nameof(AccentHover)];
    public static Brush AccentPressed { get; } = Shared[nameof(AccentPressed)];
    public static Brush AccentSoft { get; } = Shared[nameof(AccentSoft)];
    public static Brush Success { get; } = Shared[nameof(Success)];
    public static Brush SuccessSoft { get; } = Shared[nameof(SuccessSoft)];
    public static Brush WarningSoft { get; } = Shared[nameof(WarningSoft)];
    public static Brush DangerSoft { get; } = Shared[nameof(DangerSoft)];
    public static Brush Focus { get; } = Shared[nameof(Focus)];
    public static Brush Canvas { get; } = Shared[nameof(Canvas)];
    public static Brush CanvasGrid { get; } = Shared[nameof(CanvasGrid)];
    public static Brush CanvasAxis { get; } = Shared[nameof(CanvasAxis)];
    public static Brush Piece { get; } = Shared[nameof(Piece)];
    public static Brush PieceFill { get; } = Shared[nameof(PieceFill)];
    public static Brush PieceSelected { get; } = Shared[nameof(PieceSelected)];
    public static Brush PieceSelectedFill { get; } = Shared[nameof(PieceSelectedFill)];
    public static Brush PieceHover { get; } = Shared[nameof(PieceHover)];
    public static Brush PieceHoverFill { get; } = Shared[nameof(PieceHoverFill)];
    public static Brush PieceLabel { get; } = Shared[nameof(PieceLabel)];
    public static Brush PieceLabelSelected { get; } = Shared[nameof(PieceLabelSelected)];
    public static Brush Notch { get; } = Shared[nameof(Notch)];
    public static Brush GrainOk { get; } = Shared[nameof(GrainOk)];
    public static Brush GrainReview { get; } = Shared[nameof(GrainReview)];
    public static Brush DrawingPreview { get; } = Shared[nameof(DrawingPreview)];

    public static Brush PreviewSource { get; } = Shared[nameof(PreviewSource)];
    public static Brush PreviewPlot { get; } = Shared[nameof(PreviewPlot)];
    public static Brush PreviewMarker { get; } = Shared[nameof(PreviewMarker)];
    public static Brush PreviewLabel { get; } = Shared[nameof(PreviewLabel)];
    public static Brush PreviewOutline { get; } = Shared[nameof(PreviewOutline)];
    public static Brush PreviewGuide { get; } = Shared[nameof(PreviewGuide)];
    public static Brush PreviewPiece { get; } = Shared[nameof(PreviewPiece)];
    public static Brush PreviewRing { get; } = Shared[nameof(PreviewRing)];
    public static Brush PreviewFrame { get; } = Shared[nameof(PreviewFrame)];
    public static Brush IllustrationFill { get; } = Shared[nameof(IllustrationFill)];
    public static Brush IllustrationStroke { get; } = Shared[nameof(IllustrationStroke)];
    public static Brush IllustrationDetail { get; } = Shared[nameof(IllustrationDetail)];

    public static Brush PreviewLine { get; } = Shared[nameof(PreviewLine)];
    public static Brush PreviewStart { get; } = Shared[nameof(PreviewStart)];
    public static Brush PreviewEnd { get; } = Shared[nameof(PreviewEnd)];

    /// <summary>The palette currently applied. Dark until <see cref="Use"/> is called.</summary>
    public static ThemeKind Current { get; private set; } = ThemeKind.Dark;

    /// <summary>Raised on the calling thread after every <see cref="Use"/>; canvases invalidate themselves from it.</summary>
    public static event Action? Changed;

    /// <summary>Token names in table order (used by the verification of both palettes).</summary>
    internal static IReadOnlyList<string> TokenNames { get; } = Table.Select(row => row.Name).ToArray();

    /// <summary>The colour a token has in a palette, independent of what is currently applied.</summary>
    internal static Color PaletteColor(string token, ThemeKind kind)
    {
        foreach (var row in Table)
            if (row.Name == token) return ParseColor(kind == ThemeKind.Dark ? row.Dark : row.Light);
        throw new ArgumentException("Unknown theme token: " + token, nameof(token));
    }

    /// <summary>
    /// A frozen copy of a token's current colour for drawing code (<c>OnRender</c>). Retained drawings are frozen by
    /// <c>VisualTreeHelper.GetDrawing</c>, which would freeze a shared token brush and end live switching, so drawing code
    /// never hands the token itself to a <c>DrawingContext</c>; <see cref="Use"/> repaints every open window instead.
    /// </summary>
    public static Brush Paint(Brush token)
    {
        if (token is not SolidColorBrush solid) return token;
        var copy = new SolidColorBrush(solid.Color);
        copy.Freeze();
        return copy;
    }

    /// <summary>True when every token brush currently holds the value of a palette.</summary>
    internal static bool PaletteMatches(ThemeKind kind) => Table.All(row => Shared[row.Name].Color == ParseColor(kind == ThemeKind.Dark ? row.Dark : row.Light));

    /// <summary>The shared brush of a token.</summary>
    internal static SolidColorBrush TokenBrush(string token) => Shared[token];

    /// <summary>Switches every token to a palette. Open windows repaint immediately; call on the UI thread.</summary>
    public static void Use(ThemeKind kind)
    {
        foreach (var row in Table) Shared[row.Name].Color = ParseColor(kind == ThemeKind.Dark ? row.Dark : row.Light);
        Current = kind;
        // Windows repaint their controls through the shared brushes; custom drawing (OnRender) uses frozen copies and must be redrawn.
        if (Application.Current is { } application)
            foreach (Window window in application.Windows) Redraw(window);
        Changed?.Invoke();
    }

    /// <summary>Publishes the shared brushes as <c>Studio.&lt;Token&gt;</c> resources for the DynamicResource references in App.xaml.</summary>
    internal static void RegisterResources(ResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        foreach (var row in Table) resources["Studio." + row.Name] = Shared[row.Name];
    }

    private static void Redraw(DependencyObject element)
    {
        if (element is UIElement ui) ui.InvalidateVisual();
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) Redraw(VisualTreeHelper.GetChild(element, i));
    }

    private static Dictionary<string, SolidColorBrush> CreateBrushes()
    {
        var brushes = new Dictionary<string, SolidColorBrush>(Table.Length);
        foreach (var row in Table) brushes[row.Name] = new SolidColorBrush(ParseColor(row.Dark));
        return brushes;
    }

    private static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>Apply window-level tokens. App.xaml supplies implicit control styles.</summary>
    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Background = Background;
        window.Foreground = Text;
        window.FontFamily = new FontFamily("Segoe UI");
        window.FontSize = 13;
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        var area=SystemParameters.WorkArea;
        var owner=window.Owner;
        var width=owner is null?area.Width:Math.Min(area.Width,owner.ActualWidth>0?owner.ActualWidth:double.IsFinite(owner.Width)?owner.Width:area.Width);
        var height=owner is null?area.Height:Math.Min(area.Height,owner.ActualHeight>0?owner.ActualHeight:double.IsFinite(owner.Height)?owner.Height:area.Height);
        if(double.IsFinite(window.Width))window.Width=Math.Min(window.Width,Math.Max(window.MinWidth,width-24));
        if(double.IsFinite(window.Height))window.Height=Math.Min(window.Height,Math.Max(window.MinHeight,height-24));
    }
}
