using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;
internal static partial class DesktopVerification
{
    public static int RunChatModel(string reportPath)
    {
        var checks=new List<string>();string? failure=null;var owner=new Window();DesignRequestWindow? window=null;
        try
        {
            _=new WindowInteropHelper(owner).EnsureHandle();window=new DesignRequestWindow(owner);
            var initial=new DesignRequest("","bel çevresi 72 cm",null);
            // Empty description is valid as a transient form, not persisted here.
            window.MeasurementsForVerification.Text=initial.Measurements;
            string message="  A KESİM ELBİSE İSTİYORUM, uzun kollu; bel çevresi 72 cm  ";int confirmations=0;
            window.ConfirmChatModelForVerification=warning=> { confirmations++;Check(warning.Contains("V2") && warning.Contains("Kalıp oluşturulmaz"),"V1 confirmation discloses V2 conversion and no pattern generation",checks);return false; };
            window.ChatInputForVerification.Text=message;window.SendMessageForVerification();
            var declined=window.CaptureForVerification();
            Check(confirmations==1 && declined.Version==1 && declined.ModelId is null && declined.Prompt==message && declined.Measurements==initial.Measurements,
                "Declining preserves the exact message, measurements and V1 identity state",checks);
            window.LoadForVerification(new DesignRequest("Önceki serbest tarif","bel çevresi 72 cm",null));
            window.ConfirmChatModelForVerification=_=>true;window.ChatInputForVerification.Text=message;window.SendMessageForVerification();
            var accepted=window.CaptureForVerification();
            Check(accepted.Version==2 && accepted.ModelId=="aline" && accepted.Prompt.StartsWith("Önceki serbest tarif",StringComparison.Ordinal) && accepted.Prompt.EndsWith(message,StringComparison.Ordinal) && accepted.Measurements==initial.Measurements,
                "Explicit V1 approval selects A-line in V2 while retaining prior prose and exact new message",checks);
            Check(window.CanPreviewALineForVerification,"Chat selection reaches the existing A-line preview eligibility",checks);
            Check(DesignRequestStorage.Deserialize(DesignRequestStorage.SerializeV2(accepted))==accepted,"V2 serialization roundtrip retains confirmed identity and prose",checks);
            var root=(FrameworkElement)window.Content;root.Measure(new Size(900,700));root.Arrange(new Rect(0,0,900,700));root.UpdateLayout();
            Check(Descendants<Button>(root).Any(b=>AutomationProperties.GetName(b)=="A kesim elbise" && AutomationProperties.GetItemStatus(b)=="Seçili"),"Confirmed chat choice highlights the matching existing model card",checks);
            window.LoadForVerification(new DesignRequest("A kesim eski tarif","",null,"aline",2));
            confirmations=0;window.ConfirmChatModelForVerification=_=> { confirmations++;return true; };
            window.ChatInputForVerification.Text="gömlek tipi elbise seç";window.SendMessageForVerification();
            Check(confirmations==1 && window.CaptureForVerification().ModelId=="shirt" && window.CaptureForVerification().Prompt.StartsWith("A kesim eski tarif",StringComparison.Ordinal),"V2 model replacement requires confirmation and preserves conflicting old prose",checks);
            foreach(var text in new[]{"Anvelop değil","Anvelop veya A kesim","A kesim; bel çevresi 72 cm; bel çevresi 80 cm","gömlek elbise mi?","A kesim, uzun kollu, kısa kollu","A kesim, olursa uzun kollu"})
            {
                var before=window.CaptureForVerification();confirmations=0;window.ChatInputForVerification.Text=text;window.SendMessageForVerification();
                Check(confirmations==0 && window.CaptureForVerification().ModelId==before.ModelId && window.CaptureForVerification().Prompt.EndsWith(text,StringComparison.Ordinal),"Ambiguous/negative message retained without model confirmation: "+text,checks);
            }
            window.ConfirmChatModelForVerification=_=> { window.MeasurementsForVerification.Text="bel çevresi 73 cm";return true; };
            window.ChatInputForVerification.Text="A kesim";window.SendMessageForVerification();
            Check(window.CaptureForVerification().ModelId=="shirt" && window.StatusForVerification.Text.Contains("onay sırasında değişti"),"Changing the request during confirmation invalidates model selection",checks);
            window.LoadForVerification(new DesignRequest("A kesim elbise; detaylar: ","",null));
            window.ConfirmChatModelForVerification=_=>
            {
                var field=typeof(DesignRequestWindow).GetField("modelAmbiguous",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
                field.SetValue(window,!(bool)field.GetValue(window)!);return true;
            };
            window.ChatInputForVerification.Text="Gömlek elbise istiyorum";window.SendMessageForVerification();
            Check(window.CaptureForVerification().Version==1 && window.CaptureForVerification().ModelId is null && window.StatusForVerification.Text.Contains("onay sırasında değişti"),
                "Changing V1 tracking state without changing saved request fields invalidates approval",checks);
            Check(!window.IsVisible && !owner.IsVisible,"Verification never shows a window",checks);
        }
        catch(Exception ex) { failure=ex.ToString(); }
        finally
        {
            if(window is not null) { window.LoadForVerification(new DesignRequest("Sentetik temiz kapanış","",null));window.Close(); }
            owner.Close();
        }
        var full=Path.GetFullPath(reportPath);Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var output=new FileStream(full,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        JsonSerializer.Serialize(output,new { status=failure is null ? "PASS" : "FAIL",checks,failure,scope="Synthetic unshown WPF event checks; no interactive device, AI service, or garment production." },new JsonSerializerOptions { WriteIndented=true });
        return failure is null ? 0 : 1;
    }
}
