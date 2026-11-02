using System.Windows;

namespace TekstilCAD.Desktop;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
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
        if (e.Args.Length == 2 && e.Args[0] == "--verify-legacy-migration")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(DesktopVerification.RunLegacyMigration(e.Args[1]));
            return;
        }
        new MainWindow().Show();
    }
}
