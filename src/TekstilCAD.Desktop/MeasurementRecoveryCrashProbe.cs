using System.IO;

namespace TekstilCAD.Desktop;

/// <summary>Workspace-only hidden child used by synthetic cross-process recovery verification.</summary>
internal static class MeasurementRecoveryCrashProbe
{
    internal static int Run(string directory)
    {
        try
        {
            var root=Path.GetFullPath(directory);AppPaths.UseVerificationDirectory(Path.Combine(root,"appdata"));Directory.CreateDirectory(root);
            using var store=new MeasurementFormRecoveryStore(Path.Combine(root,"records"),"basic");
            store.Save(["81","","",""]);File.WriteAllText(Path.Combine(root,"ready.txt"),store.PathForVerification);
            store.BeforeCommitForVerification=()=>
            {
                File.WriteAllText(Path.Combine(root,"before-commit.txt"),"ready");
                if(!new ManualResetEventSlim(false).Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException("Synthetic crash barrier expired.");
            };
            store.Save(["82","","",""]);return 1;
        }
        catch(Exception ex)
        {
            try { File.WriteAllText(Path.Combine(directory,"error.txt"),ex.ToString()); } catch { }
            return 1;
        }
    }
}
