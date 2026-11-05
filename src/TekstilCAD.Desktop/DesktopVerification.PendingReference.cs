using System.IO;
using System.Windows;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;
internal static partial class DesktopVerification
{
    private static void VerifyPendingReferenceProtection(List<string> checks)
    {
        var owner=new Window();var window=new DesignRequestWindow(owner);
        var dir=Path.Combine(Path.GetTempPath(),"tcad-pending-reference-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var path=Path.Combine(dir,"next.tcad-request.json");
        DesignRequestStorage.SaveNew(path,new DesignRequest("Yeni istek","",null));
        var reached=new ManualResetEventSlim();var release=new ManualResetEventSlim();Task<bool>? pending=null;
        var photo=new DesignReference("sample.png","image/png",new byte[]{137,80,78,71,13,10,26,10});
        try
        {
            window.LoadForVerification(new DesignRequest("Kaydedilmiş","",null));
            window.ReferenceLoaderForVerification=_=> { reached.Set();release.Wait();return photo; };
            pending=window.ChooseReferenceFileAsync("ignored");PumpUntil(()=>reached.IsSet);
            var warning="";window.ConfirmDiscardForVerification=m=> { warning=m;return false; };
            Check(!window.ClosingAllowedForVerification() && warning.Contains("bekleyen görsel") && window.CaptureForVerification().Reference is null,"Pending image explicitly warns and declined closing preserves the operation",checks);
            window.OpenPathChoiceForVerification=()=>path;window.OpenForVerification();
            Check(window.CaptureForVerification().Prompt=="Kaydedilmiş" && !pending.IsCompleted,"Declining open preserves draft and pending image work",checks);
            window.SaveForVerification();Check(window.StatusForVerification.Text.Contains("sürüyor"),"Save waits for pending image",checks);
            var verify=window.ValidateAndSaveForVerification();PumpUntil(()=>verify.IsCompleted);verify.GetAwaiter().GetResult();
            Check(window.StatusForVerification.Text.Contains("sürüyor") && window.CreateDrawingNotes() is null,"Validate-and-save and drawing notes wait for pending image",checks);
            window.ConfirmDiscardForVerification=_=>true;window.OpenPathChoiceForVerification=()=>null;window.OpenForVerification();
            Check(!pending.IsCompleted && window.CaptureForVerification().Prompt=="Kaydedilmiş","Cancelling file picker keeps approved pending operation",checks);
            window.OpenPathChoiceForVerification=()=>path;window.OpenForVerification();
            Check(window.CaptureForVerification().Prompt=="Yeni istek","Approved open loads replacement while old image remains pending",checks);
            release.Set();PumpUntil(()=>pending.IsCompleted);Check(!pending.GetAwaiter().GetResult() && window.CaptureForVerification().Reference is null,"Late image cannot attach to replacement request",checks);pending=null;

            window.LoadForVerification(new DesignRequest("Fotoğraflı","",photo));
            window.ReferenceLoaderForVerification=_=>throw new IOException("synthetic failure");
            var failed=window.ChooseReferenceFileAsync("ignored");PumpUntil(()=>failed.IsCompleted);
            Check(!failed.GetAwaiter().GetResult() && window.CaptureForVerification().Reference!.Data.SequenceEqual(photo.Data),"Failed image selection keeps existing image",checks);
            window.ReferenceLoaderForVerification=_=>photo;
            var success=window.ChooseReferenceFileAsync("ignored");PumpUntil(()=>success.IsCompleted);
            Check(success.GetAwaiter().GetResult() && window.CaptureForVerification().Reference!.Data.SequenceEqual(photo.Data),"Successful image selection attaches the checked candidate",checks);

            reached.Reset();release.Reset();window.LoadForVerification(new DesignRequest("Temiz","",null));
            window.ReferenceLoaderForVerification=_=> { reached.Set();release.Wait();return photo; };
            pending=window.ChooseReferenceFileAsync("ignored");PumpUntil(()=>reached.IsSet);
            window.ConfirmDiscardForVerification=_=> { window.PromptForVerification.Text="Değişti";return true; };
            Check(!window.ClosingAllowedForVerification() && window.CaptureForVerification().Prompt=="Değişti","Changes during confirmation refuse stale approval",checks);
            window.ConfirmDiscardForVerification=_=>true;window.Close();release.Set();PumpUntil(()=>pending.IsCompleted);
            Check(!pending.GetAwaiter().GetResult(),"Approved actual close prevents late attachment",checks);pending=null;
        }
        finally { release.Set();if(pending is not null){PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();}owner.Close(); }
    }
}
