using System.Windows;

namespace TekstilCAD.Desktop;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if(e.Args.Length==2 && e.Args[0]=="--verify-measurement-recovery-child")
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;Shutdown(MeasurementRecoveryCrashProbe.Run(e.Args[1]));return;
        }
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
        var main=new MainWindow();
        LaunchAtelierAfterFirstRender(main,()=>new DesignRequestWindow(main).ShowDialog());
        main.Show();
    }
    internal static void LaunchAtelierAfterFirstRender(Window owner,Action launch)
    {
        ArgumentNullException.ThrowIfNull(owner);ArgumentNullException.ThrowIfNull(launch);
        EventHandler? handler=null;
        handler=(_,_)=> { owner.ContentRendered-=handler;launch(); };
        owner.ContentRendered+=handler;
    }
}
