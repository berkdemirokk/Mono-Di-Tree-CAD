using System.IO;
using System.Text;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TekstilCAD.Desktop;

internal static partial class DesktopVerification
{
    private static void VerifyMeasurementRecoveryErrors(DesignRequestWindow owner,string reportPath,List<string> checks)
    {
        var folder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,"mform-errors-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var context=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(owner.Dispatcher));
        var request=owner.CaptureForVerification();var chat=owner.ChatInputForVerification.Text;
        try
        {
            foreach(var kind in new[]{"basic","experimental"})
            {
                var count=kind=="basic"?4:15;
                var prior=Enumerable.Range(0,count).Select(i=>(70+i).ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                var restored=Enumerable.Range(0,count).Select(i=>(80+i).ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                var inputs=prior.Select(s=>new TextBox { Text=s,MaxLength=24 }).ToArray();
                var controls=new MeasurementRecoveryControls(owner,kind,inputs);
                // Redirect only the fixture root: real path/lock/DPAPI/format checks remain active.
                controls.LoadForVerification=(path,type)=>MeasurementFormRecoveryStore.Load(folder,path,type);
                string valid;
                using(var store=new MeasurementFormRecoveryStore(folder,kind)) { store.Save(restored);valid=store.PathForVerification; }
                var validBytes=File.ReadAllBytes(valid);
                try
                {
                    byte[][] badBytes=[[],Encoding.UTF8.GetBytes("broken"),Encoding.UTF8.GetBytes("TekstilCAD.MeasurementRecovery.v1\nAQID"),new byte[16_385]];
                    foreach(var bytes in badBytes)
                    {
                        var bad=Path.Combine(folder,$"form-{kind}-{Guid.NewGuid():N}.mform");File.WriteAllBytes(bad,bytes);
                        var read=controls.RestoreFromPathAsync(bad,prior);PumpUntil(()=>read.IsCompleted);
                        Check(!read.GetAwaiter().GetResult() && inputs.Select(i=>i.Text).SequenceEqual(prior) &&
                            File.ReadAllBytes(bad).SequenceEqual(bytes) && controls.StatusForVerification.Text.Contains("Taslak yüklenemedi") && controls.ReadyToClose(),
                            "Real recovery boundary contains malformed/unprotectable file, preserves fields and source bytes, and releases busy: "+kind+" / "+bytes.Length,checks);
                        var retry=controls.RestoreFromPathAsync(valid,prior);PumpUntil(()=>retry.IsCompleted);
                        Check(retry.GetAwaiter().GetResult() && inputs.Select(i=>i.Text).SequenceEqual(restored) && controls.ReadyToClose() &&
                            File.ReadAllBytes(valid).SequenceEqual(validBytes),
                            "Valid encrypted recovery succeeds immediately after rejected file with original bytes intact: "+kind,checks);
                        for(var i=0;i<inputs.Length;i++)inputs[i].Text=prior[i];
                    }
                    var after=owner.CaptureForVerification();
                    Check(after.Prompt==request.Prompt && after.Measurements==request.Measurements && after.ModelId==request.ModelId && after.Version==request.Version &&
                        (request.Reference is null ? after.Reference is null : after.Reference is not null && after.Reference.Name==request.Reference.Name && after.Reference.MediaType==request.Reference.MediaType && after.Reference.Data.SequenceEqual(request.Reference.Data)) &&
                        owner.ChatInputForVerification.Text==chat,
                        "Failed and retried recovery never changes parent request, model, unsent chat or image bytes: "+kind,checks);
                }
                finally { controls.FinishForVerification();PumpUntil(()=>controls.ReleaseForVerification.IsCompleted); }
            }
        }
        finally { SynchronizationContext.SetSynchronizationContext(context);Directory.Delete(folder,true); }
    }
}
