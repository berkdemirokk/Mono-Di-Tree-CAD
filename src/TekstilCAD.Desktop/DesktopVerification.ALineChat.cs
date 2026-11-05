using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

internal static partial class DesktopVerification
{
    private static void VerifyALineChat(Window owner,List<string> checks)
    {
        VerifyALineSourceRecovery(owner,checks);
        var window=new DesignRequestWindow(owner);
        var source=new DesignRequest("Özgün tasarım","bel çevresi 72 cm;kalça çevresi 100 cm;elbise boyu 110 cm",null,"aline",2);
        try
        {
            window.LoadForVerification(source);
            Check(window.ConversationForVerification.Text.Contains("Sıradaki alan (1/11): Ön bel yayı") &&
                !window.ConversationForVerification.Text.Contains("göğüs çevresi: açık ölçü"),
                "Explicit A-line starts its own measured-arc questions rather than the general dress checklist",checks);
            Check(window.NextStepForVerification.Text.Contains("Sıradaki alan (1/11): Ön bel yayı") && window.ConversationForVerification.Text.Contains(window.NextStepForVerification.Text),"Fixed next step uses the current A-line question",checks);
            const string combined="Ön bel yayı 36 cm ve arka bel yayı 360 mm";
            window.ChatInputForVerification.Text=combined;window.SendMessageForVerification();
            var combinedRequest=window.CaptureForVerification();var combinedSource=ALineRequestMeasurements.Review(combinedRequest.Prompt,combinedRequest.Measurements);
            Check(combinedRequest.Prompt.EndsWith(combined,StringComparison.Ordinal) && combinedRequest.Measurements==source.Measurements && combinedSource.Issues.Count==0 && combinedSource.Millimeters[0]==360 && combinedSource.Millimeters[1]==360 && window.ConversationForVerification.Text.Contains("Sıradaki alan (3/11)"),"Conjoined explicit chat values remain verbatim and advance to the next missing field",checks);
            Check(window.NextStepForVerification.Text.Contains("Sıradaki alan (3/11)") && window.ConversationForVerification.Text.Contains(window.NextStepForVerification.Text),"Fixed next step advances with the existing A-line question",checks);
            var combinedPlan=TekstilCAD.Patterns.ALineTransferPlan.Create(combinedSource.Millimeters,combinedSource.Issues,Enumerable.Repeat("",11).ToArray(),TekstilCAD.Patterns.ALineLengthUnit.Millimeter);
            var combinedFields=combinedPlan.ApplyTo(Enumerable.Repeat("",11).ToArray(),TekstilCAD.Patterns.ALineLengthUnit.Millimeter);
            Check(combinedFields[0]=="360" && combinedFields[1]=="360" && combinedFields[2]=="","Conjoined chat values transfer without deriving missing fields",checks);
            foreach(var answer in new[]{"36"," 36,5 ","\u00a036\u202f!"})
            {
                window.LoadForVerification(source);window.ChatInputForVerification.Text=answer;window.SendMessageForVerification();
                Check(window.CaptureForVerification()==source && window.ChatInputForVerification.Text==answer && window.StatusForVerification.Text.Contains("cm veya mm"),
                    "A unitless short measurement stays editable and requests an explicit unit",checks);
            }
            window.LoadForVerification(source);window.ChatInputForVerification.Text="36";window.SendMessageForVerification();
            window.ChatInputForVerification.Text+=" cm";window.SendMessageForVerification();
            Check(window.CaptureForVerification().Prompt.EndsWith("Ön bel yayı: 36 cm",StringComparison.Ordinal) && window.ChatInputForVerification.Text.Length==0,
                "Adding the requested unit sends the same answer to the current field",checks);
            window.LoadForVerification(source);window.ChatInputForVerification.Text="36 cm";
            window.MeasurementsForVerification.Text+="; not";window.MeasurementsForVerification.Text=source.Measurements;window.InsertMeasurementLabel();
            Check(window.ChatInputForVerification.Text=="36 cm" && window.StatusForVerification.Text.Contains("etiket eklenmedi"),
                  "A-line A-B-A source changes invalidate the numeric label action",checks);
            foreach(var answer in new[]{"36\u00a0cm","36\u202fcm!","\u200736\u2009cm.\u00a0"})
            {
                window.LoadForVerification(source);window.ChatInputForVerification.Text=answer;window.SendMessageForVerification();
                var captured=window.CaptureForVerification();var reviewed=ALineRequestMeasurements.Review(captured.Prompt,captured.Measurements);
                Check(captured.Prompt.EndsWith("Ön bel yayı: "+answer,StringComparison.Ordinal) && reviewed.Millimeters[0]==360 && window.ChatInputForVerification.Text.Length==0,
                    "A-line short answer accepts horizontal Unicode spaces without changing the user's text",checks);
            }
            window.LoadForVerification(source);window.ChatInputForVerification.Text="36\u00a0cm";window.MeasurementsForVerification.Text+="; not";
            var changedSource=window.CaptureForVerification();window.SendMessageForVerification();
            Check(window.CaptureForVerification()==changedSource && window.ChatInputForVerification.Text=="36\u00a0cm",
                "Unicode-spaced answer with stale question is retained instead of attached to a changed source",checks);
            window.LoadForVerification(source);window.ChatInputForVerification.Text="0\u202fcm!";window.SendMessageForVerification();
            Check(window.CaptureForVerification()==source && window.ChatInputForVerification.Text=="0\u202fcm!",
                "Unicode spacing does not bypass zero body-arc validation",checks);
            window.LoadForVerification(source);
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

            window.ChatInputForVerification.Clear();window.ChatInputForVerification.Text="12";window.SendMessageForVerification();
            Check(window.CaptureForVerification().Prompt.EndsWith("\n12",StringComparison.Ordinal) && window.ChatInputForVerification.Text=="",
                "With no remaining question a bare number remains ordinary user text, not a guessed measurement",checks);
            window.LoadForVerification(source);window.ChatInputForVerification.Text="36";window.MeasurementsForVerification.Text+="; not";
            var staleBare=window.CaptureForVerification();window.SendMessageForVerification();
            Check(window.CaptureForVerification()==staleBare && window.ChatInputForVerification.Text=="36" && window.StatusForVerification.Text.Contains("Soru veya kaynak değişti"),
                "A unitless answer to a stale question stays editable and reports the changed context",checks);
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
            foreach(var answer in new[]{"36\u00a0cm\n40\u00a0cm","36\u202fcm değil"})
            {
                window.LoadForVerification(source);window.ChatInputForVerification.Text=answer;window.SendMessageForVerification();
                var result=window.CaptureForVerification();
                Check(result.Prompt.EndsWith(answer,StringComparison.Ordinal) && !result.Prompt.Contains("Ön bel yayı: ",StringComparison.Ordinal) &&
                    ALineRequestMeasurements.Review(result.Prompt,result.Measurements).Millimeters[0] is null,
                    "Unicode-spaced multiline or negated text is retained verbatim and never attached as a definite short answer",checks);
            }
            var easeSource=source with { Prompt=source.Prompt+"\nÖn bel yayı: 36 cm\nArka bel yayı: 36 cm\nÖn kalça yayı: 50 cm\nArka kalça yayı: 50 cm" };
            window.LoadForVerification(easeSource);
            Check(window.ConversationForVerification.Text.Contains("Sıradaki alan (5/11): Ön bel yayı bolluğu"),"Explicit arcs lead to the first ease question",checks);
            window.ChatInputForVerification.Text="0\u202fcm!";window.SendMessageForVerification();
            var easeResult=window.CaptureForVerification();
            Check(easeResult.Prompt.EndsWith("Ön bel yayı bolluğu: 0\u202fcm!",StringComparison.Ordinal) &&
                  ALineRequestMeasurements.Review(easeResult.Prompt,easeResult.Measurements).Millimeters[4]==0 && window.ChatInputForVerification.Text.Length==0,
                  "Unicode-spaced zero ease is accepted only for the explicitly asked ease field",checks);
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
            foreach(var answer in new[]{"96 cm.","960 mm!","96,5 cm."})
            {
                window.LoadForVerification(source with { ModelId="wrap" });
                window.ChatInputForVerification.Text=answer;window.SendMessageForVerification();
                var captured=window.CaptureForVerification();
                var review=DressMeasurementParser.Review(captured.Prompt,captured.Measurements);
                Check(captured.Prompt.EndsWith("göğüs çevresi: "+answer,StringComparison.Ordinal) &&
                    review.Measurements.Any(m=>m.Label=="göğüs çevresi") && window.ChatInputForVerification.Text.Length==0,
                    "General measurement answer retains terminal punctuation and receives the question label: "+answer,checks);
            }
            foreach(var answer in new[]{"0 cm!","1.000 cm.","96 cm?"})
            {
                window.LoadForVerification(source with { ModelId="wrap" });window.ChatInputForVerification.Text=answer;window.SendMessageForVerification();
                Check(!window.CaptureForVerification().Prompt.Contains("göğüs çevresi: "+answer,StringComparison.Ordinal),
                    "General answer does not label invalid or questioning values: "+answer,checks);
            }
            window.LoadForVerification(source with { ModelId="wrap" });window.ChatInputForVerification.Text="96 cm.";
            window.MeasurementsForVerification.Text+="; not";var genericStale=window.CaptureForVerification();window.SendMessageForVerification();
            Check(window.CaptureForVerification()==genericStale && window.ChatInputForVerification.Text=="96 cm." && window.StatusForVerification.Text.Contains("Soru veya kaynak"),
                "General punctuated answer retains stale-context protection",checks);
            foreach(var answer in new[]{"96 cm\r\n","\r\n96 cm!\r\n","96 cm\r\n "," \r\n96 cm","\t\r\n96 cm!\r\n\u00a0"})
            {
                window.LoadForVerification(source with { ModelId="wrap" });window.ChatInputForVerification.Text=answer;
                var original=window.ChatInputForVerification.Text;window.SendMessageForVerification();
                var captured=window.CaptureForVerification();
                Check(captured.Prompt.Contains(original.Replace("96", "göğüs çevresi: 96"),StringComparison.Ordinal) &&
                    DressMeasurementParser.Review(captured.Prompt,captured.Measurements).Measurements.Any(m=>m.Label=="göğüs çevresi") && window.ChatInputForVerification.Text.Length==0,
                    "General answer recognizes surrounding line breaks and preserves message: "+answer,checks);
            }
            window.LoadForVerification(source);window.ChatInputForVerification.Text="36 cm\r\n";var alineOriginal=window.ChatInputForVerification.Text;window.SendMessageForVerification();
            Check(window.CaptureForVerification().Prompt.EndsWith("Ön bel yayı: "+alineOriginal,StringComparison.Ordinal) &&
                ALineRequestMeasurements.Review(window.CaptureForVerification().Prompt,window.CaptureForVerification().Measurements).Millimeters[0]==360,
                "A-line answer recognizes a trailing line break",checks);
            foreach(var answer in new[]{"36 cm\r\n "," \r\n36 cm", "\t\r\n36 cm!\r\n\u00a0"})
            {
                window.LoadForVerification(source);window.ChatInputForVerification.Text=answer;
                var original=window.ChatInputForVerification.Text;window.SendMessageForVerification();
                var captured=window.CaptureForVerification();
                Check(captured.Prompt.Contains(original.Replace("36", "Ön bel yayı: 36"),StringComparison.Ordinal) &&
                    ALineRequestMeasurements.Review(captured.Prompt,captured.Measurements).Millimeters[0]==360 && window.ChatInputForVerification.Text.Length==0,
                    "A-line answer recognizes whitespace on surrounding lines and preserves message: "+answer,checks);
            }
            foreach(var model in new[]{"wrap","aline"})
            {
                var modelSource=source with { ModelId=model };
                window.LoadForVerification(modelSource);window.ChatInputForVerification.Text=" \r\n36\r\n ";var bare=window.ChatInputForVerification.Text;window.SendMessageForVerification();
                Check(window.CaptureForVerification()==modelSource && window.ChatInputForVerification.Text==bare && window.StatusForVerification.Text.Contains("cm veya mm"),
                    "Line break does not bypass unit requirement: "+model,checks);
                window.LoadForVerification(modelSource);window.ChatInputForVerification.Text=" \r\n36 cm\r\n ";var staleText=window.ChatInputForVerification.Text;
                window.MeasurementsForVerification.Text+="; not";var stale=window.CaptureForVerification();window.SendMessageForVerification();
                Check(window.CaptureForVerification()==stale && window.ChatInputForVerification.Text==staleText && window.StatusForVerification.Text.Contains("Soru veya kaynak"),
                    "Line break does not bypass stale context: "+model,checks);
                window.LoadForVerification(modelSource);window.ChatInputForVerification.Text=" \r\n36 cm\r\nnot\r\n ";window.SendMessageForVerification();
                Check(!window.CaptureForVerification().Prompt.Contains(model=="aline" ? "Ön bel yayı: 36 cm" : "göğüs çevresi: 36 cm",StringComparison.Ordinal),
                    "A second line is not treated as a short answer: "+model,checks);
            }
            Check(!window.IsVisible && !owner.IsVisible,"A-line question checks never show windows",checks);
        }
        finally { window.LoadForVerification(new DesignRequest("Temiz kapanış","",null));window.Close(); }
    }
}
