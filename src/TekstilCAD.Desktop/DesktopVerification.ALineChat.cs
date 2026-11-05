using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

internal static partial class DesktopVerification
{
    private static void VerifyALineChat(Window owner,List<string> checks)
    {
        var window=new DesignRequestWindow(owner);
        var source=new DesignRequest("Özgün tasarım","bel çevresi 72 cm;kalça çevresi 100 cm;elbise boyu 110 cm",null,"aline",2);
        try
        {
            window.LoadForVerification(source);
            Check(window.ConversationForVerification.Text.Contains("Sıradaki alan (1/11): Ön bel yayı") &&
                !window.ConversationForVerification.Text.Contains("göğüs çevresi: açık ölçü"),
                "Explicit A-line starts its own measured-arc questions rather than the general dress checklist",checks);
            string[] answers=["36 cm.","360 mm!","50 cm","500 mm","0 cm.","0 mm!","2 cm","20 mm","20 cm.","70 cm!","160 cm"];
            for(var i=0;i<answers.Length;i++)
            {
                var before=window.CaptureForVerification();var label=ALineRequestMeasurements.Labels[i];
                Check(window.ConversationForVerification.Text.Contains($"Sıradaki alan ({i+1}/11): {label}"),"A-line asks field in source schema order: "+label,checks);
                window.ChatInputForVerification.Text=answers[i];window.SendMessageForVerification();
                var after=window.CaptureForVerification();
                Check(after.Prompt.StartsWith(before.Prompt,StringComparison.Ordinal) && after.Prompt.EndsWith(label+": "+answers[i],StringComparison.Ordinal) &&
                    after.Measurements==source.Measurements && after.ModelId=="aline" && window.ChatInputForVerification.Text.Length==0,
                    "Current short answer appends only its explicit label and preserves raw answer and source notes: "+label,checks);
            }
            var complete=window.CaptureForVerification();var parsed=ALineRequestMeasurements.Review(complete.Prompt,complete.Measurements);
            Check(parsed.Complete && parsed.Millimeters[4]==0 && parsed.Millimeters[5]==0 && parsed.Millimeters[9]==700 &&
                window.ConversationForVerification.Text.Contains("11 alan açıkça yazılmış") && window.ConversationForVerification.Text.Contains("geometri onayı değildir") &&
                window.ALinePreviewEntryForVerification.Visibility==Visibility.Visible,
                "Complete explicit fields direct to review shortcut, retain explicit zero ease and never derive hem length from dress length",checks);
            window.ChatInputForVerification.Text="80 cm";window.SendMessageForVerification();
            Check(window.CaptureForVerification()==complete && window.ChatInputForVerification.Text=="80 cm", "After final field no unlabeled number is guessed",checks);

            window.LoadForVerification(source);window.ChatInputForVerification.Text="0 cm";window.SendMessageForVerification();
            Check(window.CaptureForVerification()==source && window.ChatInputForVerification.Text=="0 cm" && window.StatusForVerification.Text.Contains("Sıfır yalnız bolluk"),
                "Zero is rejected for a body arc without losing the answer",checks);
            var root=(FrameworkElement)window.Content;root.Measure(new Size(900,700));root.Arrange(new Rect(0,0,900,700));root.UpdateLayout();
            var wrap=Descendants<Button>(root).Single(b=>AutomationProperties.GetName(b)=="Anvelop elbise");
            var aline=Descendants<Button>(root).Single(b=>AutomationProperties.GetName(b)=="A kesim elbise");
            var changes=new (string Label,Action Change)[] {
                ("prompt",()=>window.PromptForVerification.Text+=";yeni tarif"),
                ("notes",()=>window.MeasurementsForVerification.Text+=";ön bel yayı 35 cm"),
                ("model",()=>wrap.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))),
                ("model round trip",()=> { wrap.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));aline.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }),
                ("prompt round trip",()=> { var text=window.PromptForVerification.Text;window.PromptForVerification.Text+=";yeni tarif";window.PromptForVerification.Text=text; }),
                ("notes round trip",()=> { var text=window.MeasurementsForVerification.Text;window.MeasurementsForVerification.Text+=";ön bel yayı 35 cm";window.MeasurementsForVerification.Text=text; }),
                ("pending reference",()=>typeof(DesignRequestWindow).GetField("referenceSelectionPending",PrivateInstance)!.SetValue(window,true)),
                ("reference revision",()=> { var field=typeof(DesignRequestWindow).GetField("referenceRevision",PrivateInstance)!;field.SetValue(window,(long)field.GetValue(window)!+1); })
            };
            foreach(var (label,change) in changes)
            foreach(var answer in new[]{"36 cm","36 cm.","36 cm!"})
            {
                window.LoadForVerification(source);window.ChatInputForVerification.Text=answer;change();var afterChange=window.CaptureForVerification();
                window.SendMessageForVerification();
                Check(window.CaptureForVerification()==afterChange && window.ChatInputForVerification.Text==answer && window.StatusForVerification.Text.Contains("Soru veya kaynak"),
                    "Typed answer cannot migrate to a changed question context: "+label+" / "+answer,checks);
            }
            window.LoadForVerification(source);window.ChatInputForVerification.Text="36 cm";
            window.PromptForVerification.Text+=";yeni tarif";
            window.ChatInputForVerification.Clear();window.ChatInputForVerification.Text="36 cm";window.SendMessageForVerification();
            Check(window.CaptureForVerification().Prompt.EndsWith("Ön bel yayı: 36 cm",StringComparison.Ordinal) && window.ChatInputForVerification.Text.Length==0,
                "A newly entered answer can bind to the current question after the stale message is explicitly cleared",checks);
            foreach(var (prompt,notes) in new[]{
                ("Ön bel yayı 36 cm","Ön bel yayı 35 cm;kalça çevresi 100 cm"),
                ("eğer kumaş uygunsa","bel çevresi 72 cm"),
                ("Ön bel yayı 36 cm?","Arka kalça yayı 50 cm?"),
                ("Ön bel yayı 36 cm","bunları kullanmayın")})
            {
                var blocked=new DesignRequest(prompt,notes,null,"aline",2);window.LoadForVerification(blocked);
                Check(!window.ConversationForVerification.Text.Contains("Sıradaki alan (") && window.ConversationForVerification.Text.Contains("Önce kaynak ifadelerini düzeltin"),
                    "Ambiguous, conditional or conflicting sources show issues instead of another value question",checks);
                foreach(var answer in new[]{"36 cm","36 cm.","36 cm!"})
                {
                    window.ChatInputForVerification.Clear();window.ChatInputForVerification.Text=answer;window.SendMessageForVerification();
                    Check(window.CaptureForVerification()==blocked && window.ChatInputForVerification.Text==answer,"Unresolved source cannot attach a short answer to any field: "+answer,checks);
                }
            }
            foreach(var answer in new[]{"36 cm?","36 cm..","36 cm!!","36 cm.!","36 cm. 40 cm","36 cm;40 cm","36 cm değil","eğer 36 cm"})
            {
                window.LoadForVerification(source);window.ChatInputForVerification.Text=answer;window.SendMessageForVerification();
                var result=window.CaptureForVerification();
                // AppendText receives LF; WPF may retain it rather than using the OS CRLF.
                // Permit only that separator difference, never normalize the user's message.
                Check((result.Prompt==source.Prompt+"\n"+answer || result.Prompt==source.Prompt+"\r\n"+answer) && result.Measurements==source.Measurements,
                    "Question, repeated punctuation and mixed clauses remain verbatim: "+answer,checks);
                Check(ALineRequestMeasurements.Review(result.Prompt,result.Measurements).Millimeters[0] is null,
                    "Question, repeated punctuation and mixed clauses never become a definite arc: "+answer,checks);
            }
            foreach(var answer in new[]{"0 cm.","0 mm!"})
            {
                window.LoadForVerification(source);window.ChatInputForVerification.Text=answer;window.SendMessageForVerification();
                Check(window.CaptureForVerification()==source && window.ChatInputForVerification.Text==answer,
                    "Terminal punctuation does not permit zero body arcs: "+answer,checks);
            }
            window.LoadForVerification(new DesignRequest("A kesim elbise; detaylar: ","",null));
            Check(!window.ConversationForVerification.Text.Contains("Sıradaki alan (") && window.ConversationForVerification.Text.Contains("göğüs çevresi: açık ölçü"),
                "Unconfirmed loaded V1 candidate retains general guidance without inferring experimental model choice",checks);
            window.LoadForVerification(source with { ModelId="wrap" });
            Check(!window.ConversationForVerification.Text.Contains("Sıradaki alan ("),"Other models retain the existing general questions",checks);
            window.ChatInputForVerification.Text="96 cm.";window.SendMessageForVerification();
            var genericPrompt=window.CaptureForVerification().Prompt;
            Check(genericPrompt==source.Prompt+"\n96 cm." || genericPrompt==source.Prompt+"\r\n96 cm.",
                "Punctuation extension does not alter general model answer labeling",checks);
            Check(!window.IsVisible && !owner.IsVisible,"A-line question checks never show windows",checks);
        }
        finally { window.LoadForVerification(new DesignRequest("Temiz kapanış","",null));window.Close(); }
    }
}
