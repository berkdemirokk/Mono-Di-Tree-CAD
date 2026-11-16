using System.Windows;

namespace TekstilCAD.Desktop;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Every mode (interactive, --verify, recovery probes) needs the Studio.* brushes for the DynamicResource styles in App.xaml.
        // The palette stays Dark here; nothing reads a saved preference before the interactive branch.
        StudioTheme.RegisterResources(Resources);
        if (e.Args.Length == 2 && (e.Args[0] == "--verify-recovery-child" || e.Args[0] == "--verify-recovery-inspect"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (e.Args[0] == "--verify-recovery-child") RecoveryCrashProbe.Start(e.Args[1]);
            else Shutdown(RecoveryCrashProbe.Inspect(e.Args[1]));
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == "--verify")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var code = e.Args.Length == 2 ? DesktopVerification.Run(e.Args[1]) : 2;
            Shutdown(code);
            return;
        }
        // Interactive runs only: verification modes keep their own failure reporting and data directory.
        var preferences = TekstilCAD.Core.Theme.UiPreferences.Load(AppPaths.UiPreferencesPath);
        StudioTheme.Use(preferences.Palette);
        CrashReporter.Attach();
        CrashReporter.ShowPendingNotice();
        // New projects are saved where people find them again: Documents\Kalıplarım (created on the first save).
        AppPaths.InteractiveProjectsDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Kalıplarım");
        var firstLaunch = !System.IO.File.Exists(AppPaths.UiPreferencesPath);
        if (firstLaunch) preferences.Save(AppPaths.UiPreferencesPath);
        var window = new MainWindow();
        // The window constructor always builds the detailed layout; the saved view is applied here, after construction, interactive start only.
        window.EnableViewSwitching();
        window.ApplySimpleTextScale(preferences.TextScale);
        window.LoadSimpleViewState(preferences, firstLaunch);
        // An interactive start always begins with an empty project (the start tasks show); the constructor's document is for verification only.
        window.LoadDocument(new TekstilCAD.Core.ProjectDocument());
        if (preferences.View == TekstilCAD.Core.Theme.UiPreferences.SimpleView) window.TrySetSimpleView(true, out _);
        window.Show();
    }
}
