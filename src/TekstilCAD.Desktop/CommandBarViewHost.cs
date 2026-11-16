using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TekstilCAD.Desktop;

/// <summary>
/// The simple view's command box: the real <see cref="CommandBarView"/> (parser, preview card, apply/cancel, canvas ghost,
/// voice hint, example chips) wrapped behind <see cref="ICommandBarHost"/>. The window side is the same one Ctrl+K uses.
/// </summary>
internal sealed class CommandBarViewHost : ICommandBarHost
{
    private readonly MainWindow window;
    private readonly CommandBarView view;
    private readonly OverlaySlot slot;

    internal CommandBarViewHost(MainWindow window)
    {
        this.window = window;
        view = new CommandBarView(window.CreateCommandBarHost())
        {
            // The slot draws the card behind a preview; at rest the box and its chips sit directly on the page.
            Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = null,
        };
        slot = new OverlaySlot { Child = view };
        slot.LayoutUpdated += (_, _) => KeepCardRightOfRail();
        window.SetCanvasTopInset(CardCover);
    }

    public FrameworkElement Element => slot;
    public void FocusBox() => view.FocusBox();
    public void SetText(string text) { view.SetText(text); view.FocusBox(); }

    public void ClearPreview() => view.ClearPreview();

    public void ShowSuggestions(bool show) => view.ShowSuggestions(show);

    // The tool rail sits left of the canvas: the preview card starts at the canvas's left edge, so it never covers a tool button.
    private void KeepCardRightOfRail()
        => view.RowIndent = window.CanvasBoundsIn(slot) is { } canvas ? Math.Clamp(canvas.Left, 0, Math.Max(0, slot.ActualWidth - 320)) : 0;

    // How far down the canvas the card reaches, so a preview frame is moved clear of it.
    private double CardCover()
    {
        if (view.PreviewRow is not { Visibility: Visibility.Visible, ActualHeight: > 0 } row || window.CanvasBoundsIn(slot) is not { } canvas) return 0;
        var bottom = row.TransformToAncestor(slot).TransformBounds(new Rect(row.RenderSize)).Bottom;
        return Math.Max(0, bottom + 4 - canvas.Top);
    }

    public void OnRefreshed()
    {
        view.OnContextChanged();
        // Chips follow the project and selection; while a preview or result is shown they are hidden or carry its undo.
        if (view.State == CommandBarState.Idle) view.RefreshChips();
    }

    /// <summary>
    /// Reserves only the height of the idle box plus one chip row. A preview, result or the example list grows past it and
    /// is drawn over the canvas on a card of its own, so typing a command never shrinks the drawing area.
    /// </summary>
    private sealed class OverlaySlot : Decorator
    {
        private const double IdleHeight = 96;   // 48 box + 8 gap + 40 chip row
        protected override Size MeasureOverride(Size available)
        {
            if (Child is null) return default;
            Child.Measure(new Size(available.Width, double.PositiveInfinity));
            // The slot's own size can stay 96 while the card grows, so the child must be arranged again at its full height.
            InvalidateArrange();
            return new Size(Child.DesiredSize.Width, Math.Min(IdleHeight, Child.DesiredSize.Height));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (Child is null) return finalSize;
            Child.Arrange(new Rect(0, 0, finalSize.Width, Child.DesiredSize.Height));
            // The card is drawn in OnRender behind the child, so it follows every layout of the child.
            InvalidateVisual();
            return finalSize;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (Child is not CommandBarView view) return;
            // The preview card and the example list each get a surface of their own; at rest the box sits on the page.
            foreach (var part in new[] { view.PreviewRow, view.ExamplesPanel })
            {
                if (part.Visibility != Visibility.Visible || part.ActualHeight <= 0) continue;
                var bounds = part.TransformToAncestor(this).TransformBounds(new Rect(part.RenderSize));
                bounds.Inflate(6, 6);
                drawingContext.DrawRoundedRectangle(StudioTheme.Surface, new Pen(StudioTheme.Border, 1), bounds, 10, 10);
            }
        }
    }
}
