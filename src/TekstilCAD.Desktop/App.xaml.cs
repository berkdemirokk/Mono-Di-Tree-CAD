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
        CrashReporter.Attach();
        CrashReporter.ShowPendingNotice();
        new MainWindow().Show();
    }
}
