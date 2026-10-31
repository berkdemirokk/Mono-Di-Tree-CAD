using System.Windows;
using System.Windows.Media;

namespace TekstilCAD.Desktop;

/// <summary>Shared visual tokens for the native desktop shell and tool windows.</summary>
public static class StudioTheme
{
    public static Brush Background { get; } = Solid("#101923");
    public static Brush Surface { get; } = Solid("#16222F");
    public static Brush Raised { get; } = Solid("#1D2C3C");
    public static Brush Border { get; } = Solid("#304254");
    public static Brush Accent { get; } = Solid("#49D8BB");
    public static Brush Text { get; } = Solid("#EDF4FA");
    public static Brush Muted { get; } = Solid("#9BADBE");
    public static Brush Warning { get; } = Solid("#F5C377");
    public static Brush Danger { get; } = Solid("#FF9999");

    private static Brush Solid(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
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
