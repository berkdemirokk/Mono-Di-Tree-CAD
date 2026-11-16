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
    private readonly CommandBarView view;
    private readonly OverlaySlot slot;

    internal CommandBarViewHost(MainWindow window)
    {
        view = new CommandBarView(window.CreateCommandBarHost())
        {
            // The slot draws the card behind a preview; at rest the box and its chips sit directly on the page.
            Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = null,
        };
        slot = new OverlaySlot { Child = view };
    }

    public FrameworkElement Element => slot;
    public void FocusBox() => view.FocusBox();
    public void SetText(string text) { view.SetText(text); view.FocusBox(); }

    public void ClearPreview() => view.ClearPreview();

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
        private const double IdleHeight = 96;   // 52 box + 8 gap + 36 chip row
        private double drawnHeight;

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
            // The card is drawn in OnRender, so it is redrawn only when the height it covers changes.
            if (Math.Abs(Child.DesiredSize.Height - drawnHeight) > .5) { drawnHeight = Child.DesiredSize.Height; InvalidateVisual(); }
            return finalSize;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (Child is not { } child || child.RenderSize.Height <= IdleHeight + .5) return;
            drawingContext.DrawRoundedRectangle(StudioTheme.Surface, new Pen(StudioTheme.Border, 1),
                new Rect(-8, -6, ActualWidth + 16, child.RenderSize.Height + 14), 10, 10);
        }
    }
}
