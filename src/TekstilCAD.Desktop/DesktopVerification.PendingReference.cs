using System.IO;
using System.Windows;
using System.Windows.Interop;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;
internal static partial class DesktopVerification
{
    private static void VerifyPendingReferenceProtection(List<string> checks)
    {
        var owner=new Window();_=new WindowInteropHelper(owner).EnsureHandle();var window=new DesignRequestWindow(owner);
        var dir=Path.Combine(Path.GetTempPath(),"tcad-pending-reference-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var path=Path.Combine(dir,"next.tcad-request.json");
        DesignRequestStorage.SaveNew(path,new DesignRequest("Yeni istek","",null));
        var reached=new ManualResetEventSlim();var release=new ManualResetEventSlim();Task<bool>? pending=null;
        var photo=new DesignReference("sample.png","image/png",new byte[]{137,80,78,71,13,10,26,10});
        try
        {
            Check(!owner.IsVisible,"Verification owner remains hidden",checks);
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
            int replacementPrompts=0;window.ConfirmDiscardForVerification=_=> { replacementPrompts++;return false; };
            Check(window.ClosingAllowedForVerification() && replacementPrompts==0,"Clean replacement does not warn for the invalidated prior selection",checks);
            var blockedSecond=window.ChooseReferenceFileAsync("ignored");PumpUntil(()=>blockedSecond.IsCompleted);
            Check(!blockedSecond.GetAwaiter().GetResult(),"Replacement cannot start another image worker until old worker stops",checks);
            release.Set();PumpUntil(()=>pending.IsCompleted);Check(!pending.GetAwaiter().GetResult() && window.CaptureForVerification().Reference is null,"Late image cannot attach to replacement request",checks);pending=null;

            window.LoadForVerification(new DesignRequest("Fotoğraflı","",photo));
            window.ReferenceLoaderForVerification=_=>throw new IOException("synthetic failure");
            var failed=window.ChooseReferenceFileAsync("ignored");PumpUntil(()=>failed.IsCompleted);
            Check(!failed.GetAwaiter().GetResult() && window.CaptureForVerification().Reference!.Data.SequenceEqual(photo.Data),"Failed image selection keeps existing image",checks);
            window.ReferenceLoaderForVerification=_=>photo;
            var success=window.ChooseReferenceFileAsync("ignored");PumpUntil(()=>success.IsCompleted);
            Check(success.GetAwaiter().GetResult() && window.CaptureForVerification().Reference!.Data.SequenceEqual(photo.Data),"Successful image selection attaches the checked candidate",checks);

            // Explicit removal invalidates the pending selection immediately;
            // a second load remains blocked until the first worker has stopped.
            reached.Reset();release.Reset();window.LoadForVerification(new DesignRequest("Temiz","",null));
            window.ReferenceLoaderForVerification=_=> { reached.Set();release.Wait();return photo; };
            pending=window.ChooseReferenceFileAsync("ignored");PumpUntil(()=>reached.IsSet);
            window.RemoveReferenceForVerification();int removalPrompts=0;window.ConfirmDiscardForVerification=_=> { removalPrompts++;return false; };
            Check(window.ClosingAllowedForVerification() && removalPrompts==0,"Removed pending selection no longer prompts on a clean request",checks);
            var second=window.ChooseReferenceFileAsync("ignored");PumpUntil(()=>second.IsCompleted);
            Check(!second.GetAwaiter().GetResult(),"A second reference load cannot start until the invalidated worker ends",checks);
            release.Set();PumpUntil(()=>pending.IsCompleted);
            Check(!pending.GetAwaiter().GetResult() && window.CaptureForVerification().Reference is null,"Removed pending image never returns after completion",checks);pending=null;

            // A file-picker callback may pump the dispatcher while the old load completes.
            reached.Reset();release.Reset();window.LoadForVerification(new DesignRequest("Temiz","",null));
            pending=window.ChooseReferenceFileAsync("ignored");PumpUntil(()=>reached.IsSet);
            window.ConfirmDiscardForVerification=_=>true;
            window.OpenPathChoiceForVerification=()=> { release.Set();PumpUntil(()=>pending.IsCompleted);return path; };
            window.OpenForVerification();
            Check(pending.GetAwaiter().GetResult() && window.CaptureForVerification().Prompt=="Temiz" && window.CaptureForVerification().Reference is not null,
                "Image completion while picker is open cancels replacement and preserves completed selection",checks);pending=null;

            reached.Reset();release.Reset();window.LoadForVerification(new DesignRequest("Temiz","",null));
            window.ReferenceLoaderForVerification=_=> { reached.Set();release.Wait();return photo; };
            pending=window.ChooseReferenceFileAsync("ignored");PumpUntil(()=>reached.IsSet);
            window.PromptForVerification.Text="Düzenlenmiş";warning="";
            window.ConfirmDiscardForVerification=m=> { warning=m;return false; };
            Check(!window.ClosingAllowedForVerification() && warning.Contains("Kaydedilmemiş") && warning.Contains("bekleyen görsel"),"Dirty draft and pending selection are both disclosed",checks);
            window.ConfirmDiscardForVerification=_=> { window.PromptForVerification.Text="Değişti";return true; };
            Check(!window.ClosingAllowedForVerification() && window.CaptureForVerification().Prompt=="Değişti","Changes during confirmation refuse stale approval",checks);
            window.ConfirmDiscardForVerification=_=>true;window.Close();release.Set();PumpUntil(()=>pending.IsCompleted);
            Check(!pending.GetAwaiter().GetResult(),"Approved actual close prevents late attachment",checks);pending=null;

            var replacementWindow=new DesignRequestWindow(owner);var reachedReplacement=new ManualResetEventSlim();var releaseReplacement=new ManualResetEventSlim();Task<bool>? replacementPending=null;
            try
            {
                replacementWindow.LoadForVerification(new DesignRequest("Önce","",null));
                replacementWindow.ReferenceLoaderForVerification=_=> { reachedReplacement.Set();releaseReplacement.Wait();return photo; };
                replacementPending=replacementWindow.ChooseReferenceFileAsync("ignored");PumpUntil(()=>reachedReplacement.IsSet);
                replacementWindow.ConfirmDiscardForVerification=_=>true;replacementWindow.OpenPathChoiceForVerification=()=>path;replacementWindow.OpenForVerification();
                int extraPrompts=0;replacementWindow.ConfirmDiscardForVerification=_=> { extraPrompts++;return false; };
                replacementWindow.Close();releaseReplacement.Set();PumpUntil(()=>replacementPending.IsCompleted);
                Check(extraPrompts==0 && !replacementPending.GetAwaiter().GetResult(),"Closing clean replacement needs no stale warning and late worker cannot attach",checks);replacementPending=null;
            }
            finally { releaseReplacement.Set();if(replacementPending is not null){PumpUntil(()=>replacementPending.IsCompleted);replacementPending.GetAwaiter().GetResult();} }
        }
        finally { release.Set();if(pending is not null){PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();}owner.Close(); }
    }
}
