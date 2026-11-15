using System.Windows;
using System.Windows.Media;
using TekstilCAD.Core.Theme;

namespace TekstilCAD.Desktop;

/// <summary>
/// Shared visual tokens for the native desktop shell and tool windows.
/// Every token is ONE shared, unfrozen <see cref="SolidColorBrush"/>; <see cref="Use"/> changes the colour of those
/// instances in place, so every control that holds a token repaints at once, in windows that are already open as well as in
/// windows opened later, and reference comparisons such as <c>ReferenceEquals(x.Foreground, StudioTheme.Warning)</c> stay valid.
/// The same instances are published to <c>Application.Resources</c> as <c>Studio.&lt;Token&gt;</c> (<see cref="RegisterResources"/>),
/// which App.xaml reads with <c>DynamicResource</c>. The default palette is Dark; nothing reads a saved preference here.
/// The brushes belong to the UI thread; background threads must not touch them.
/// Never hand a token brush itself to a <c>DrawingContext</c> or to a <c>Setter</c> value: WPF freezes such values
/// (<c>VisualTreeHelper.GetDrawing</c>, sealing a style), and a frozen token can no longer change colour. Use <see cref="Paint"/> in
/// <c>OnRender</c> and <c>DynamicResource</c> references in code-built styles.
/// </summary>
public static class StudioTheme
{
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
    public static Brush Placeholder { get; } = Shared[nameof(Placeholder)];
    public static Brush OnAccent { get; } = Shared[nameof(OnAccent)];
    public static Brush AccentHover { get; } = Shared[nameof(AccentHover)];
    public static Brush AccentPressed { get; } = Shared[nameof(AccentPressed)];
    public static Brush AccentSoft { get; } = Shared[nameof(AccentSoft)];
    public static Brush PrimaryFill { get; } = Shared[nameof(PrimaryFill)];
    public static Brush PrimaryBorder { get; } = Shared[nameof(PrimaryBorder)];
    public static Brush Success { get; } = Shared[nameof(Success)];
    public static Brush SuccessSoft { get; } = Shared[nameof(SuccessSoft)];
    public static Brush WarningSoft { get; } = Shared[nameof(WarningSoft)];
    public static Brush DangerSoft { get; } = Shared[nameof(DangerSoft)];
    public static Brush SelectionBorder { get; } = Shared[nameof(SelectionBorder)];
    public static Brush TextSelection { get; } = Shared[nameof(TextSelection)];
    public static Brush HoverFill { get; } = Shared[nameof(HoverFill)];
    public static Brush PressedFill { get; } = Shared[nameof(PressedFill)];
    public static Brush RowAlt { get; } = Shared[nameof(RowAlt)];
    public static Brush GridLine { get; } = Shared[nameof(GridLine)];
    public static Brush ScrollThumb { get; } = Shared[nameof(ScrollThumb)];
    public static Brush ScrollThumbHover { get; } = Shared[nameof(ScrollThumbHover)];
    public static Brush TooltipBg { get; } = Shared[nameof(TooltipBg)];
    public static Brush TooltipText { get; } = Shared[nameof(TooltipText)];
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
    public static Brush Ghost { get; } = Shared[nameof(Ghost)];
    public static Brush GhostFill { get; } = Shared[nameof(GhostFill)];
    public static Brush OnPrimary { get; } = Shared[nameof(OnPrimary)];

    /// <summary>The palette currently applied. Dark until <see cref="Use"/> is called.</summary>
    public static ThemePalette Current { get; private set; } = ThemePalette.Dark;

    /// <summary>Raised on the calling thread after every <see cref="Use"/>.</summary>
    public static event Action? Changed;

    /// <summary>The shared brush of a token.</summary>
    internal static SolidColorBrush TokenBrush(string token) => Shared[token];

    /// <summary>Switches every token to a palette, in place. Open windows repaint immediately; call on the UI thread.</summary>
    public static void Use(ThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        foreach (var token in ThemePalette.Tokens) Shared[token].Color = ParseColor(palette[token]);
        Current = palette;
        // Controls repaint through the shared brushes; custom drawing (OnRender) uses frozen copies and must be redrawn.
        if (Application.Current is { } application)
            foreach (Window window in application.Windows) Redraw(window);
        Changed?.Invoke();
    }

    /// <summary>True when every token brush currently holds the colour of the palette.</summary>
    internal static bool Matches(ThemePalette palette) =>
        ThemePalette.Tokens.All(token => Shared[token].Color == ParseColor(palette[token]));

    /// <summary>Publishes the shared brushes as <c>Studio.&lt;Token&gt;</c> resources for the DynamicResource references in App.xaml.</summary>
    internal static void RegisterResources(ResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        foreach (var token in ThemePalette.Tokens) resources["Studio." + token] = Shared[token];
    }

    /// <summary>A frozen copy of a token's current colour, for <c>OnRender</c> drawing code (see the class summary).</summary>
    public static Brush Paint(Brush token)
    {
        if (token is not SolidColorBrush solid) return token;
        var copy = new SolidColorBrush(solid.Color);
        copy.Freeze();
        return copy;
    }

    internal static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static void Redraw(DependencyObject element)
    {
        if (element is UIElement ui) ui.InvalidateVisual();
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) Redraw(VisualTreeHelper.GetChild(element, i));
    }

    private static Dictionary<string, SolidColorBrush> CreateBrushes()
    {
        var brushes = new Dictionary<string, SolidColorBrush>(ThemePalette.Tokens.Count);
        foreach (var token in ThemePalette.Tokens) brushes[token] = new SolidColorBrush(ParseColor(ThemePalette.Dark[token]));
        return brushes;
    }

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
