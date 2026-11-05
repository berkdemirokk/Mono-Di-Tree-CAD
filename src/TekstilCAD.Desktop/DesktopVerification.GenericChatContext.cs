using TekstilCAD.FileFormats;
namespace TekstilCAD.Desktop;
internal static partial class DesktopVerification
{
    private static void VerifyGenericChatContext(DesignRequestWindow window,List<string> checks)
    {
        var source=new DesignRequest("Düz elbise istiyorum","",null,"straight",2);
        window.LoadForVerification(source);window.ChatInputForVerification.Text="96\u00a0cm";window.InsertMeasurementLabel();
        Check(window.ChatInputForVerification.Text=="göğüs çevresi: 96\u00a0cm", "Label action prefixes the existing standalone measurement without rewriting it",checks);
        window.SendMessageForVerification();
        Check(DressMeasurementParser.Review(window.CaptureForVerification().Prompt,"").Measurements.Any(m=>m.Label=="göğüs çevresi" && m.Millimeters==960),
              "An explicitly prefixed measurement can be sent to the requested field",checks);
        window.LoadForVerification(source);window.ChatInputForVerification.Text="96";window.InsertMeasurementLabel();
        Check(window.ChatInputForVerification.Text=="göğüs çevresi: 96", "Label action preserves a standalone unitless value for further editing",checks);
        window.LoadForVerification(source);window.ChatInputForVerification.Text="96 cm değil";window.InsertMeasurementLabel();
        Check(window.ChatInputForVerification.Text=="96 cm değil\ngöğüs çevresi: ", "Negated prose keeps its original order when adding a label",checks);
        foreach(var answer in new[]{"96","\u00a096,5\u202f"})
        {
            window.LoadForVerification(source);window.ChatInputForVerification.Text=answer;window.SendMessageForVerification();
            Check(window.CaptureForVerification()==source && window.ChatInputForVerification.Text==answer && window.StatusForVerification.Text.Contains("cm veya mm"),
                "Generic unitless answer remains editable and requests a unit",checks);
        }
        window.LoadForVerification(source);window.ChatInputForVerification.Text="96";window.SendMessageForVerification();
        window.ChatInputForVerification.Text+=" cm";window.SendMessageForVerification();
        Check(window.CaptureForVerification().Prompt.EndsWith("göğüs çevresi: 96 cm",StringComparison.Ordinal),
            "Generic answer attaches after adding the requested unit",checks);
        window.LoadForVerification(source);window.ChatInputForVerification.Text="96\u00a0cm";window.SendMessageForVerification();
        var unicodeAnswer=window.CaptureForVerification();
        Check(unicodeAnswer.Prompt.EndsWith("göğüs çevresi: 96\u00a0cm",StringComparison.Ordinal) &&
              DressMeasurementParser.Review(unicodeAnswer.Prompt,unicodeAnswer.Measurements).Measurements.Any(m=>m.Label=="göğüs çevresi" && m.Millimeters==960),
              "Generic Unicode-spaced measurement retains text and attaches to its current question",checks);
        window.LoadForVerification(source);window.ChatInputForVerification.Text="96\u202fcm";window.MeasurementsForVerification.Text="göğüs çevresi 90 cm";
        var staleUnicode=window.CaptureForVerification();window.SendMessageForVerification();
        Check(window.CaptureForVerification()==staleUnicode && window.ChatInputForVerification.Text=="96\u202fcm",
              "Generic Unicode-spaced answer cannot attach after its question changes",checks);
        foreach(var answer in new[]{"1.000 cm","1,000 cm"})
        {
            window.LoadForVerification(source);window.ChatInputForVerification.Text=answer;window.SendMessageForVerification();
            Check(window.CaptureForVerification()==source && window.ChatInputForVerification.Text==answer && window.StatusForVerification.Text.Contains("ayırıcı belirsiz"),
                "Generic ambiguous three-digit separator remains editable instead of becoming a measurement",checks);
            window.InsertMeasurementLabel();window.SendMessageForVerification();
            var explicitSource=window.CaptureForVerification();var reviewed=DressMeasurementParser.Review(explicitSource.Prompt,explicitSource.Measurements);
            Check(!reviewed.Measurements.Any(m=>m.Label=="göğüs çevresi") && reviewed.Questions.Any(q=>q.Contains("göğüs çevresi") && q.Contains("belirsiz")),
                "Explicitly labeled ambiguous number is preserved but not accepted as a definite measurement",checks);
        }
        foreach(var answer in new[]{"0 cm","1001 cm"})
        {
            window.LoadForVerification(source);window.ChatInputForVerification.Text=answer;window.SendMessageForVerification();
            Check(window.CaptureForVerification()==source && window.ChatInputForVerification.Text==answer && window.StatusForVerification.Text.Contains("geçersiz"),
                "Out-of-range generic short answer is preserved rather than appended",checks);
        }
        window.LoadForVerification(source);window.ChatInputForVerification.Text="96";window.MeasurementsForVerification.Text="göğüs çevresi 90 cm";
        var staleUnitless=window.CaptureForVerification();window.SendMessageForVerification();
        Check(window.CaptureForVerification()==staleUnitless && window.ChatInputForVerification.Text=="96" && window.StatusForVerification.Text.Contains("Soru veya kaynak değişti"),
            "Generic unitless answer for a stale question is preserved without guessing a unit",checks);
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
