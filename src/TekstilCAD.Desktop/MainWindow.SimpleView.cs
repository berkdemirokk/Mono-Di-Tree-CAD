using System.Windows;
using System.Windows.Controls;
using TekstilCAD.Core.Theme;

namespace TekstilCAD.Desktop;

/// <summary>The command box region of the simple view. Implemented by CommandBarViewHost.</summary>
internal interface ICommandBarHost
{
    /// <summary>Root of the region (the box and its help row); the simple view docks it under the header.</summary>
    FrameworkElement Element { get; }
    /// <summary>Moves keyboard focus into the box.</summary>
    void FocusBox();
    /// <summary>Fills the box with text. This never applies anything: the user still confirms with Enter.</summary>
    void SetText(string text);
    /// <summary>Called after every window refresh while the simple view is shown.</summary>
    void OnRefreshed();
    /// <summary>Drops a shown preview and its canvas ghost (the box is leaving the window).</summary>
    void ClearPreview();
}

// The simple view is a state of this same window: the constructor still builds the detailed layout and reads no
// preference; SimpleViewController adds a layer on top (Attach) and removes it again (Detach).
public partial class MainWindow
{
    // Layout containers of the constructor, kept so that the simple view can rearrange the same control instances.
    private DockPanel rootPanel = null!;
    private Grid headerGrid = null!;
    private WrapPanel commandAreaPanel = null!;
    private Grid workspaceGrid = null!;
    private StackPanel toolsPanel = null!;
    private ScrollViewer railScroller = null!;
    private StackPanel inspectorPanel = null!;
    private DockPanel canvasHeaderPanel = null!;
    private Border statusBarBorder = null!;
    private DockPanel footerPanel = null!;
    private TextBlock taglineText = null!;
    private DockPanel brandLinePanel = null!;
    private Button aboutButton = null!;
    private TextBlock unitsText = null!;
    private Border modeChip = null!;
    private StackPanel projectPanel = null!;
    private WrapPanel navigationPanel = null!;
    private StackPanel viewCommandsPanel = null!;
    private Border canvasShellBorder = null!;
    private Border inspectorShellBorder = null!;

    private SimpleViewController? simpleView;
    private bool simpleViewSwitching;

    /// <summary>Creates the command box region; verification may replace it.</summary>
    internal static Func<MainWindow, ICommandBarHost> CommandBarFactory { get; set; } = window => new CommandBarViewHost(window);

    internal bool IsSimpleView => simpleView is { IsAttached: true };

    /// <summary>
    /// Switches between the detailed layout and the simple view: synchronous and without any dialog. Refuses (and changes
    /// nothing) while a drawing or an unapplied draft is pending or a long operation runs.
    /// </summary>
    internal bool TrySetSimpleView(bool on, out string? refusal)
    {
        refusal = null;
        if (on == IsSimpleView) return true;
        if (HasPendingCanvasOrPropertyWork || ExportRunning || DxfImportRunning || projectOpenRunning)
        {
            refusal = "Görünüm değiştirilemedi · Önce çizimi bitirin; yarım kalmış ad ve konum kutularını uygulayın ya da temizleyin.";
            return false;
        }
        simpleView ??= new SimpleViewController(this);
        if (on) simpleView.Attach(); else simpleView.Detach();
        Refresh();
        return true;
    }

    /// <summary>Interactive start only: shows the switch button (and enables F9). Verification windows never call this.</summary>
    internal void EnableViewSwitching()
    {
        simpleViewSwitching = true;
        simpleView ??= new SimpleViewController(this);
        simpleView.MountSwitchPermanently();
    }

    /// <summary>Interactive start only: text size in percent (100, 115, 130) for the simple view.</summary>
    internal void ApplySimpleTextScale(int percent) => (simpleView ??= new SimpleViewController(this)).SetTextScale(percent);

    private void ToggleSimpleView()
    {
        var target = !IsSimpleView;
        try
        {
            if (!TrySetSimpleView(target, out var refusal)) { SetStatus(refusal!); return; }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NullReferenceException)
        {
            SetStatus("Görünüm değiştirilemedi · " + ex.Message);
            return;
        }
        // Keep keyboard focus on something that still exists, and say what happened (the status line is silent in the simple view).
        if (IsSimpleView) FocusSimpleCommandBar(); else simpleView?.FocusSwitch();
        SetStatus(IsSimpleView ? "Basit görünüm açık · F9 ile Ayrıntılı görünüme dönebilirsiniz." : "Ayrıntılı görünüm açık · F9 ile Basit görünüme dönebilirsiniz.");
        if (!simpleViewSwitching) return;
        var preferences = UiPreferences.Load(AppPaths.UiPreferencesPath);
        if (!(preferences with { View = target ? UiPreferences.SimpleView : UiPreferences.DetailedView }).Save(AppPaths.UiPreferencesPath))
            SetStatus("Görünüm değişti; ancak tercih dosyası yazılamadı, sonraki açılışta eski görünüm gelir.");
    }

    private string? SimpleToolHeading(string tool) => simpleView is { IsAttached: true } view ? view.ToolHeading(tool) : null;
    private bool FocusSimpleCommandBar() => simpleView is { IsAttached: true } view && view.FocusCommandBar();
}
