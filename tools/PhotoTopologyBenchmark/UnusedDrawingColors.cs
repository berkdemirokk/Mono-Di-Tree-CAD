using System.Windows.Media;

namespace TekstilCAD.Desktop;

// The linked canvas is never rendered in this experiment. These placeholders
// satisfy its drawing-only color references; no geometry behavior is replaced.
internal static class DesignRequestWindow
{
    internal static Brush BorderColor => Brushes.Black;
    internal static Brush Accent => Brushes.Black;
}
