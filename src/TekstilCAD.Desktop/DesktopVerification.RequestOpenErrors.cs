using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

internal static partial class DesktopVerification
{
    private static void VerifyRequestOpenErrors(Window owner,string reportPath,List<string> checks)
    {
        var folder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,"request-open-errors-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var window=new DesignRequestWindow(owner);
        var previousContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(owner.Dispatcher));
        try
        {
            var bitmap=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[16],8);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var memory=new MemoryStream();encoder.Save(memory);
            var reference=new DesignReference("sentetik.png","image/png",memory.ToArray());
            var original=new DesignRequest("Özgün tarif","bel çevresi 72 cm",reference,"aline",2);
            var valid=new DesignRequest("Geçerli yeni istek","kalça çevresi 100 cm",reference,"wrap",2);
            var good=Path.Combine(folder,"valid.tcad-request.json");DesignRequestStorage.SaveNewV2(good,valid,ReferencePhotoDecoder.ValidateV2);
            var goodBytes=File.ReadAllBytes(good);
            var unsupported=Encoding.UTF8.GetString(goodBytes).Replace("\"version\":2","\"version\":99",StringComparison.Ordinal);
            var badImage=JsonSerializer.Serialize(new { format=DesignRequestStorage.Format,version=2,prompt="Eksik PNG",measurements="",model=new { id="aline" },
                reference=new { name="eksik.png",mediaType="image/png",dataBase64=Convert.ToBase64String(new byte[]{137,80,78,71,13,10,26,10}) } });
            var samples=new (string Name,byte[] Bytes)[]{("missing-fields",Encoding.UTF8.GetBytes("{}")),("broken-json",Encoding.UTF8.GetBytes("{")),
                ("bad-utf8",[0xff]),("unsupported-version",Encoding.UTF8.GetBytes(unsupported)),("undecodable-image",Encoding.UTF8.GetBytes(badImage))};
            static bool Same(DesignRequest a,DesignRequest b)=>a.Prompt==b.Prompt && a.Measurements==b.Measurements && a.ModelId==b.ModelId && a.Version==b.Version &&
                (a.Reference is null ? b.Reference is null : b.Reference is not null && a.Reference.Name==b.Reference.Name && a.Reference.MediaType==b.Reference.MediaType && a.Reference.Data.SequenceEqual(b.Reference.Data));
            foreach(var (name,bytes) in samples)
            {
                window.LoadForVerification(original);window.PromptForVerification.AppendText(" — kaydedilmemiş düzenleme");window.ChatInputForVerification.Text="Gönderilmemiş 36 cm";
                var before=window.CaptureForVerification();var wasDirty=window.DirtyForVerification;var chat=window.ChatInputForVerification.Text;var conversation=window.ConversationForVerification.Text;
                var bad=Path.Combine(folder,name+".tcad-request.json");File.WriteAllBytes(bad,bytes);var confirmations=0;
                window.ConfirmDiscardForVerification=_=> { confirmations++;return true; };window.OpenPathChoiceForVerification=()=>bad;
                WaitForOpen(window);
                Check(confirmations==1 && Same(before,window.CaptureForVerification()) && wasDirty && window.DirtyForVerification==wasDirty &&
                    window.ChatInputForVerification.Text==chat && window.ConversationForVerification.Text==conversation &&
                    window.StatusForVerification.Text.StartsWith("İstek açılamadı; mevcut alanlar korundu",StringComparison.Ordinal) && File.ReadAllBytes(bad).SequenceEqual(bytes),
                    "Real request Open contains invalid file and preserves draft, model, notes, image, dirty state and source bytes: "+name,checks);
                window.OpenPathChoiceForVerification=()=>null;WaitForOpen(window);
                Check(Same(before,window.CaptureForVerification()) && window.ChatInputForVerification.Text==chat && window.DirtyForVerification==wasDirty,
                    "Cancelling open after an error does not consume pending work: "+name,checks);
                window.ConfirmDiscardForVerification=_=>false;window.OpenPathChoiceForVerification=()=>throw new InvalidOperationException("Declining discard must not open chooser");WaitForOpen(window);
                Check(Same(before,window.CaptureForVerification()) && window.ChatInputForVerification.Text==chat,"Declining replacement keeps the failed-open draft intact: "+name,checks);
                window.ConfirmDiscardForVerification=_=>true;window.OpenPathChoiceForVerification=()=>good;WaitForOpen(window);
                Check(Same(valid,window.CaptureForVerification()) && !window.DirtyForVerification && window.ChatInputForVerification.Text.Length==0 &&
                    window.StatusForVerification.Text.StartsWith("İstek açıldı",StringComparison.Ordinal) && File.ReadAllBytes(good).SequenceEqual(goodBytes) && File.ReadAllBytes(bad).SequenceEqual(bytes),
                    "Explicit valid open succeeds after error without rewriting either source file: "+name,checks);
            }
            // Hold the worker to verify responsiveness, stale edits, supersession and close.
            var reached=new ManualResetEventSlim();var release=new ManualResetEventSlim();
            try
            {
                window.LoadForVerification(original);window.ConfirmDiscardForVerification=_=>true;window.OpenPathChoiceForVerification=()=>good;
                window.RequestLoaderForVerification=_=> { reached.Set();release.Wait();return valid; };
                var pending=window.OpenForVerification();PumpUntil(()=>reached.IsSet);
                Check(!pending.IsCompleted && Same(original,window.CaptureForVerification()),"Request loading runs without blocking the dispatcher or replacing the draft",checks);
                Check(window.CreateALinePreview() is null && Same(original,window.CaptureForVerification()),
                    "Pending request load blocks A-line session and preserves the source",checks);
                window.PromptForVerification.Text="changed";window.PromptForVerification.Text=original.Prompt;
                release.Set();PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();
                Check(Same(original,window.CaptureForVerification()) && window.StatusForVerification.Text.Contains("iptal"),"Reverted edits invalidate the pending request import",checks);

                reached.Reset();release.Reset();window.LoadForVerification(original);
                pending=window.OpenForVerification();PumpUntil(()=>reached.IsSet);
                window.RequestLoaderForVerification=_=>valid;var newer=window.OpenForVerification();PumpUntil(()=>newer.IsCompleted);newer.GetAwaiter().GetResult();
                release.Set();PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();
                Check(Same(valid,window.CaptureForVerification()),"Late older import cannot replace a newer result",checks);

                window.LoadForVerification(original);_ = new WindowInteropHelper(window).EnsureHandle();var activeSession=window.CreateALinePreview();
                Check(activeSession is not null,"A-line session can start after the request load ends",checks);
                if(activeSession is not null)
                {
                    activeSession.ConfirmDiscardWorkForVerification=()=>true;
                    activeSession.FieldsForVerification[0].Text="36";
                    window.OpenPathChoiceForVerification=()=>throw new InvalidOperationException("An active session must block the chooser");
                    WaitForOpen(window);
                    Check(Same(original,window.CaptureForVerification()) && activeSession.FieldsForVerification[0].Text=="36",
                        "Open is blocked while an A-line session holds edited work",checks);
                    activeSession.Close();
                }
                window.OpenPathChoiceForVerification=()=>
                {
                    var transient=window.CreateALinePreview();
                    if(transient is null)throw new InvalidOperationException("Expected session during picker callback");
                    transient.Close();return good;
                };
                WaitForOpen(window);
                Check(Same(original,window.CaptureForVerification()) && window.StatusForVerification.Text.Contains("iptal"),
                    "A session started and closed during the chooser invalidates the old open",checks);
                window.OpenPathChoiceForVerification=()=>good;
                WaitForOpen(window);
                Check(Same(valid,window.CaptureForVerification()),"A fresh open succeeds after the A-line session closes",checks);

                reached.Reset();release.Reset();var closingWindow=new DesignRequestWindow(owner);closingWindow.LoadForVerification(original);
                closingWindow.OpenPathChoiceForVerification=()=>good;closingWindow.RequestLoaderForVerification=_=> { reached.Set();release.Wait();return valid; };
                pending=closingWindow.OpenForVerification();PumpUntil(()=>reached.IsSet);closingWindow.Close();release.Set();PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();
                Check(Same(original,closingWindow.CaptureForVerification()),"Closing the window prevents a late import",checks);
            }
            finally { release.Set();window.RequestLoaderForVerification=null; }
            Check(!window.IsVisible && !owner.IsVisible,"Request open error checks never display windows",checks);
        }
        finally
        {
            try { window.ConfirmDiscardForVerification=_=>true;window.Close();Directory.Delete(folder,true); }
            finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
        }
    }
}
