using TekstilCAD.FileFormats;
namespace TekstilCAD.Desktop;
internal static partial class DesktopVerification
{
    private static void VerifyGenericChatContext(DesignRequestWindow window,List<string> checks)
    {
        var source=new DesignRequest("Düz elbise istiyorum","",null,"straight",2);
        window.LoadForVerification(source);window.ChatInputForVerification.Text="96 cm";window.SendMessageForVerification();
        Check(window.CaptureForVerification().Prompt.EndsWith("göğüs çevresi: 96 cm",StringComparison.Ordinal) && window.ChatInputForVerification.Text=="",
              "Current generic short answer attaches to its original question",checks);
        window.LoadForVerification(source);window.ChatInputForVerification.Text="96 cm";
        window.MeasurementsForVerification.Text="göğüs çevresi 90 cm";var changed=window.CaptureForVerification();window.SendMessageForVerification();
        Check(window.CaptureForVerification()==changed && window.ChatInputForVerification.Text=="96 cm" && window.StatusForVerification.Text.Contains("Soru veya kaynak değişti"),
              "Changing generic question does not attach a pending answer to another measurement",checks);
        window.ChatInputForVerification.Clear();window.ChatInputForVerification.Text="72 cm";window.SendMessageForVerification();
        Check(window.CaptureForVerification().Prompt.EndsWith("bel çevresi: 72 cm",StringComparison.Ordinal),
              "An explicitly re-entered answer attaches to the new question",checks);
        window.LoadForVerification(source);window.ChatInputForVerification.Text="96 cm";
        window.MeasurementsForVerification.Text="göğüs çevresi 90 cm";window.MeasurementsForVerification.Text="";var restored=window.CaptureForVerification();
        window.SendMessageForVerification();
        Check(window.CaptureForVerification()==restored && window.ChatInputForVerification.Text=="96 cm",
              "Generic question A-B-A changes invalidate the pending answer even when source text returns",checks);
        window.LoadForVerification(source);window.ChatInputForVerification.Text="96 cm";
        window.LoadForVerification(source with { ModelId="aline" });window.ChatInputForVerification.Text="96 cm";window.SendMessageForVerification();
        Check(window.CaptureForVerification().ModelId=="aline" && !window.CaptureForVerification().Prompt.Contains("göğüs çevresi: 96 cm",StringComparison.Ordinal),
              "A model change cannot apply a general measurement label to an A-line question",checks);
    }
}
