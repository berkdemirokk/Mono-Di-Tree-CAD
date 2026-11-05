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
            VerifyPendingReferenceProtection(checks);
            _=new WindowInteropHelper(owner).EnsureHandle();window=new DesignRequestWindow(owner);
            var emptyObservations=PhotoObservationWindow.Build(0,0,0);
            var manualObservations=PhotoObservationWindow.Build(1,3,0);
            Check(emptyObservations.Count==0 && manualObservations.Count==2 && !manualObservations.Any(x=>x.Contains("Yaka")),"Photo observations default to unknown and include only explicit user choices",checks);
            var observationText=DesignRequestWindow.AppendPhotoObservations("Önceki tarif",manualObservations,1000);
            Check(observationText.StartsWith("Önceki tarif\n",StringComparison.Ordinal) && observationText.Contains("Fotoğraf gözlemi · Kol: Uzun kollu"),"Photo observations append without replacing existing prose",checks);
            bool conflictRejected=false;try { _=DesignRequestWindow.AppendPhotoObservations(observationText,PhotoObservationWindow.Build(0,1,0),1000); } catch(ArgumentException) { conflictRejected=true; }
            Check(conflictRejected,"Repeated or conflicting labeled photo observation is rejected",checks);
            var contradiction=DesignRequestWindow.AppendPhotoObservations("Uzun kollu elbise",PhotoObservationWindow.Build(0,1,0),1000);
            Check(contradiction.StartsWith("Uzun kollu elbise",StringComparison.Ordinal) && contradiction.Contains("Kol: Kolsuz"),"Free-form prose is preserved when a manual observation may contradict it",checks);
            var photoSnapshot=new DesignRequest("tarif","bel 72 cm",new DesignReference("a.png","image/png",[1,2]),"aline",2);
            Check(DesignRequestWindow.PhotoObservationSnapshotMatches(photoSnapshot,photoSnapshot,3,3,"", "",false,false,false) &&
                !DesignRequestWindow.PhotoObservationSnapshotMatches(photoSnapshot,photoSnapshot with { Reference=photoSnapshot.Reference! with { Data=[1,3] } },3,3,"","",false,false,false) &&
                !DesignRequestWindow.PhotoObservationSnapshotMatches(photoSnapshot,photoSnapshot,3,4,"","",false,false,false) &&
                !DesignRequestWindow.PhotoObservationSnapshotMatches(photoSnapshot,photoSnapshot,3,3,"","",false,false,true),
                "Photo observation snapshot rejects changed bytes, revision or pending selection",checks);
            Check(!window.RequestDetailsForVerification.IsExpanded,"Saved request editing starts collapsed for the primary chat flow",checks);
            var modelOnly=new DesignRequest("","bel çevresi 72 cm",null,"aline",2);
            window.LoadForVerification(modelOnly);
            var captured=window.CaptureForVerification();
            Check(captured==modelOnly && DesignRequestStorage.Deserialize(DesignRequestStorage.SerializeV2(captured))==modelOnly,
                "V2 model and measurement draft remains intact without description or image",checks);
            window.LoadForVerification(new DesignRequest("Başlangıç","",null));
            window.PromptForVerification.Text="";
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
            const string multiMessage="A kesim elbise istiyorum uzun kollu bel çevresi 72 cm kalça çevresi 100 cm";
            window.LoadForVerification(new DesignRequest("Önceki not","",null));
            window.ConfirmChatModelForVerification=_=>false;window.ChatInputForVerification.Text=multiMessage;window.SendMessageForVerification();
            Check(window.CaptureForVerification().ModelId is null && window.CaptureForVerification().Prompt.EndsWith(multiMessage,StringComparison.Ordinal) && window.MeasurementTilesForVerification["bel çevresi"].Text=="72 cm",
                "Declining multi-detail suggestion keeps text and explicit measurements",checks);
            window.LoadForVerification(new DesignRequest("Önceki not","",null));
            window.ConfirmChatModelForVerification=_=>true;window.ChatInputForVerification.Text=multiMessage;window.SendMessageForVerification();
            Check(window.CaptureForVerification().ModelId=="aline" && window.MeasurementTilesForVerification["kalça çevresi"].Text=="100 cm" && window.CaptureForVerification().Prompt.EndsWith(multiMessage,StringComparison.Ordinal),
                "Approved multi-detail suggestion preserves message and both measurements",checks);
            window.LoadForVerification(accepted);
            window.LoadForVerification(new DesignRequest("Önceki not","bel çevresi 90 cm",null));
            const string combinedMessage="A kesim elbise istiyorum bel çevresi 72 cm";
            window.ConfirmChatModelForVerification=_=>false;window.ChatInputForVerification.Text=combinedMessage;window.SendMessageForVerification();
            Check(window.CaptureForVerification().ModelId is null && window.CaptureForVerification().Prompt.EndsWith(combinedMessage,StringComparison.Ordinal) && window.MeasurementTilesForVerification["bel çevresi"].Text=="Kontrol edin",
                "Combined message preserves declined model and conflicting prior measurement",checks);
            window.LoadForVerification(new DesignRequest("Önceki not","",null));
            window.ConfirmChatModelForVerification=_=>true;window.ChatInputForVerification.Text=combinedMessage;window.SendMessageForVerification();
            Check(window.CaptureForVerification().ModelId=="aline" && window.MeasurementTilesForVerification["bel çevresi"].Text=="72 cm" && window.CaptureForVerification().Prompt.EndsWith(combinedMessage,StringComparison.Ordinal),
                "Confirmed combined message selects model and displays explicit measurement without rewriting",checks);
            window.LoadForVerification(accepted);
            Check(DesignRequestStorage.Deserialize(DesignRequestStorage.SerializeV2(accepted))==accepted,"V2 serialization roundtrip retains confirmed identity and prose",checks);
            var root=(FrameworkElement)window.Content;root.Measure(new Size(900,700));root.Arrange(new Rect(0,0,900,700));root.UpdateLayout();
            Check(Descendants<Button>(root).Any(b=>AutomationProperties.GetName(b)=="A kesim elbise" && AutomationProperties.GetItemStatus(b)=="Seçili"),"Confirmed chat choice highlights the matching existing model card",checks);
            var decomposed="du\u0308z elbise istiyorum";
            window.ChatInputForVerification.Text=decomposed;window.SendMessageForVerification();
            var unicodeRequest=window.CaptureForVerification();
            Check(unicodeRequest.ModelId=="straight" && unicodeRequest.Prompt.EndsWith(decomposed,StringComparison.Ordinal) &&
                DesignRequestStorage.Deserialize(DesignRequestStorage.SerializeV2(unicodeRequest)).Prompt==unicodeRequest.Prompt,
                "Decomposed Unicode selects the model while original code units survive message and V2 roundtrip",checks);
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
            var bitmap=System.Windows.Media.Imaging.BitmapSource.Create(2,2,96,96,System.Windows.Media.PixelFormats.Bgra32,null,
                new byte[]{0,0,0,255,255,255,255,255,0,0,255,255,0,255,0,255},8);
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var imageStream=new MemoryStream();encoder.Save(imageStream);var imageBytes=imageStream.ToArray();
            DesignRequest ImageRequest()=>new("Görselli başlangıç","bel çevresi 72 cm",new DesignReference("sentetik.png","image/png",(byte[])imageBytes.Clone()));
            const string imageMessage="A kesim elbise istiyorum, uzun kollu";
            void SendImageMessage(string text=imageMessage) { window!.ChatInputForVerification.Text=text;window.SendMessageForVerification(); }
            void CheckImage(string name)
            {
                var captured=window!.CaptureForVerification();
                Check(captured.Reference!.Data.SequenceEqual(imageBytes) && captured.Measurements=="bel çevresi 72 cm" &&
                    captured.Prompt.Split(imageMessage,StringSplitOptions.None).Length==2,name,checks);
            }
            window.LoadForVerification(ImageRequest());var conversions=0;var choices=0;
            window.ConfirmV2ConversionForVerification=_=> { conversions++;return true; };
            window.ConfirmChatModelForVerification=_=> { choices++;return true; };
            SendImageMessage();
            Check(conversions==1 && choices==1 && window.CaptureForVerification().Version==2 && window.CaptureForVerification().ModelId=="aline",
                "Image V1 message validates and converts before separate model approval without resending",checks);
            CheckImage("Successful conversion and selection preserve original image bytes, notes and one message occurrence");
            window.LoadForVerification(ImageRequest());choices=0;
            window.ConfirmV2ConversionForVerification=_=>false;SendImageMessage();
            Check(window.CaptureForVerification().Version==1 && choices==0,"Cancelling image V2 conversion never reaches model approval",checks);
            CheckImage("Cancelled conversion preserves image bytes and exactly one message");
            window.LoadForVerification(ImageRequest());window.ConfirmV2ConversionForVerification=_=>true;window.ConfirmChatModelForVerification=_=>false;
            SendImageMessage();Check(window.CaptureForVerification().Version==2 && window.CaptureForVerification().ModelId is null,"Cancelling model approval keeps conversion but does not select a model",checks);
            CheckImage("Cancelled model approval preserves image and one message");
            foreach(var changeChat in new[]{false,true})
            {
                window.LoadForVerification(ImageRequest());choices=0;
                window.ConfirmChatModelForVerification=_=> { choices++;return true; };
                window.ConfirmV2ConversionForVerification=_=> { if(changeChat)window.ChatInputForVerification.Text="Yeni bekleyen mesaj";else window.MeasurementsForVerification.Text="bel çevresi 73 cm";return true; };
                SendImageMessage();
                Check(window.CaptureForVerification().Version==1 && choices==0 && window.CaptureForVerification().Reference!.Data.SequenceEqual(imageBytes),
                    "Changed "+(changeChat ? "unsent message" : "draft")+" aborts conversion and selection without altering image",checks);
            }
            foreach(var text in new[]{"A kesim istemiyorum","A kesim veya anvelop"})
            {
                window.LoadForVerification(ImageRequest());conversions=0;choices=0;
                window.ConfirmV2ConversionForVerification=_=> { conversions++;return true; };
                window.ConfirmChatModelForVerification=_=> { choices++;return true; };
                SendImageMessage(text);
                Check(conversions==0 && choices==0 && window.CaptureForVerification().Version==1 && window.CaptureForVerification().Prompt.EndsWith(text,StringComparison.Ordinal) && window.CaptureForVerification().Reference!.Data.SequenceEqual(imageBytes),
                    "Negative or multiple model message does not trigger image conversion: "+text,checks);
            }
            var trailingBytes=imageBytes.Concat(new byte[]{0}).ToArray();
            window.LoadForVerification(ImageRequest());conversions=0;choices=0;
            var busyField=typeof(DesignRequestWindow).GetField("referenceLoadRunning",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
            var imageField=typeof(DesignRequestWindow).GetField("reference",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
            var revisionField=typeof(DesignRequestWindow).GetField("referenceRevision",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
            busyField.SetValue(window,true);SendImageMessage();var retained=window.CaptureForVerification().Prompt;
            Check(conversions==0 && choices==0 && window.RetryModelForVerification.IsEnabled,"Busy image load retains an explicit retry route without selecting",checks);
            busyField.SetValue(window,false);
            imageField.SetValue(window,new DesignReference("changed.png","image/png",trailingBytes));
            revisionField.SetValue(window,(long)revisionField.GetValue(window)!+1);
            window.RetryModelForVerification.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(conversions==0 && choices==0 && window.CaptureForVerification().Prompt==retained && window.StatusForVerification.Text.Contains("doğrulaması başarısız"),
                "Retry validates changed image bytes rather than trusting the old revision",checks);
            window.RetryModelForVerification.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(conversions==0 && choices==0 && window.CaptureForVerification().Version==1 &&
                window.CaptureForVerification().Reference!.Data.SequenceEqual(trailingBytes) && window.CaptureForVerification().Prompt==retained &&
                window.CaptureForVerification().Measurements=="bel çevresi 72 cm" && window.RetryModelForVerification.IsEnabled,
                "Repeated invalid PNG retry remains handled and preserves draft, image bytes and retry availability",checks);
            imageField.SetValue(window,new DesignReference("current.png","image/png",(byte[])imageBytes.Clone()));
            revisionField.SetValue(window,(long)revisionField.GetValue(window)!+1);
            window.MeasurementsForVerification.Text="bel çevresi 73 cm";
            window.ConfirmV2ConversionForVerification=_=> { conversions++;return false; };
            window.RetryModelForVerification.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(conversions==1 && choices==0 && window.CaptureForVerification().Version==1 && window.CaptureForVerification().Prompt==retained &&
                window.CaptureForVerification().Reference!.Data.SequenceEqual(imageBytes),
                "Cancelling valid retry after a failed PNG validation still preserves current image and message",checks);
            conversions=0;window.ConfirmV2ConversionForVerification=_=> { conversions++;return true; };
            window.RetryModelForVerification.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(conversions==1 && choices==1 && window.CaptureForVerification().ModelId=="aline" && window.CaptureForVerification().Prompt==retained &&
                window.CaptureForVerification().Measurements=="bel çevresi 73 cm" && window.CaptureForVerification().Reference!.Data.SequenceEqual(imageBytes),
                "Explicit retry after loading uses current notes and image with separate approvals and no duplicate text",checks);
            var counts=conversions+choices;window.ChatInputForVerification.Text="Yeni mesaj";
            window.RetryModelForVerification.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(conversions+choices==counts && window.CaptureForVerification().Prompt==retained,"Unsent new message blocks retry without appending text",checks);
            window.ChatInputForVerification.Clear();window.PromptForVerification.Text="Değiştirilmiş tarif";
            window.RetryModelForVerification.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(conversions+choices==counts && window.CaptureForVerification().Prompt=="Değiştirilmiş tarif","Removed original message cannot be resurrected by retry",checks);
            window.LoadForVerification(new DesignRequest("Görselli başlangıç","",new DesignReference("sentetik.png","image/png",trailingBytes)));
            conversions=0;choices=0;SendImageMessage();
            Check(conversions==0 && choices==0 && window.CaptureForVerification().Version==1 &&
                window.CaptureForVerification().Reference!.Data.SequenceEqual(trailingBytes) && window.StatusForVerification.Text.Contains("doğrulaması başarısız"),
                "PNG trailing data fails the existing V2 image gate before conversion and retains original bytes",checks);
            window.LoadForVerification(new DesignRequest("Soru biçimindeki sentetik ölçü","kalça çevresi 100 cm",null));
            int questionApprovals=0;window.ConfirmChatModelForVerification=_=> { questionApprovals++;return true; };
            const string questionMessage="A kesim elbise istiyorum; göğüs çevresi 96 cm ve bel çevresi 72 cm?";
            window.ChatInputForVerification.Text=questionMessage;window.SendMessageForVerification();
            Check(questionApprovals==0&&window.CaptureForVerification().ModelId is null,"Question measurement prevents long-message model suggestion",checks);
            Check(window.MeasurementTilesForVerification["bel çevresi"].Text=="Kontrol edin"&&window.MeasurementTilesForVerification["göğüs çevresi"].Text=="Kontrol edin"&&window.MeasurementTilesForVerification["kalça çevresi"].Text=="100 cm","Question keeps joined tiles uncertain and independent sentinel definite",checks);
            Check(window.ConversationForVerification.Text.Contains("ifade belirsiz")&&!window.ConversationForVerification.Text.Contains("Temel ölçüler açıkça yazıldı"),"Conversation asks clarification instead of claiming complete measurements",checks);
            var questionSaved=window.CaptureForVerification();var questionLoaded=DesignRequestStorage.Deserialize(DesignRequestStorage.Serialize(questionSaved));window.LoadForVerification(questionLoaded);
            Check(questionLoaded.Prompt==questionSaved.Prompt&&questionLoaded.Prompt.EndsWith(questionMessage,StringComparison.Ordinal)&&window.MeasurementTilesForVerification["bel çevresi"].Text=="Kontrol edin","Question text survives serialization/load without becoming definite",checks);
            window.MeasurementsForVerification.Text="kalça çevresi 100 cm; bel çevresi 72 cm";
            Check(window.MeasurementTilesForVerification["bel çevresi"].Text=="Kontrol edin","Same-value assertion does not silently erase an unresolved question",checks);
            window.PromptForVerification.Text="göğüs çevresi 96 cm ve bel çevresi 72 cm.";
            Check(window.MeasurementTilesForVerification["bel çevresi"].Text=="72 cm"&&window.MeasurementTilesForVerification["göğüs çevresi"].Text=="96 cm","Explicit correction to affirmative punctuation restores definite joined measurements",checks);
            window.LoadForVerification(new DesignRequest("bel çevresi 72 cm?","kalça çevresi 100 cm",null));
            window.ChatInputForVerification.Text="bel çevresi 720 mm";window.SendMessageForVerification();
            var clarified=window.CaptureForVerification();
            Check(clarified.Prompt.StartsWith("bel çevresi 72 cm?",StringComparison.Ordinal)&&clarified.Prompt.EndsWith("bel çevresi 720 mm",StringComparison.Ordinal)&&window.MeasurementTilesForVerification["bel çevresi"].Text=="72 cm","Later exact equivalent chat reply resolves a single question without removing either message",checks);
            Check(window.MeasurementTilesForVerification["kalça çevresi"].Text=="100 cm"&&clarified.Measurements=="kalça çevresi 100 cm","Clarification preserves independent measurement notes",checks);
            window.LoadForVerification(DesignRequestStorage.Deserialize(DesignRequestStorage.Serialize(clarified)));
            Check(window.CaptureForVerification().Prompt==clarified.Prompt&&window.MeasurementTilesForVerification["bel çevresi"].Text=="72 cm","Clarification is deterministic after save/load with original text intact",checks);
            foreach(var unsafeReply in new[]{"bel çevresi 73 cm","bel çevresi 72 cm olursa","bel çevresi 72 cm değil","bel çevresi 72 cm olsun","bel çevresi 72 cm ve göğüs çevresi 96 cm"})
            {
                window.LoadForVerification(new DesignRequest("bel çevresi 72 cm?","kalça çevresi 100 cm",null));window.ChatInputForVerification.Text=unsafeReply;window.SendMessageForVerification();
                Check(window.MeasurementTilesForVerification["bel çevresi"].Text=="Kontrol edin"&&window.CaptureForVerification().Prompt.EndsWith(unsafeReply,StringComparison.Ordinal),"Unsafe clarification remains uncertain with exact reply retained: "+unsafeReply,checks);
            }
            window.LoadForVerification(new DesignRequest("bel çevresi 72 cm?","bel çevresi 72 cm",null));
            Check(window.MeasurementTilesForVerification["bel çevresi"].Text=="Kontrol edin","No clarification chronology inferred across description and notes",checks);
            foreach(var conditional in new[]{"eğer kumaş ipekse; bel çevresi 72 cm","bel çevresi 72 cm; eğer kumaş ipekse","eğer kumaş ipekse; göğüs çevresi 96 cm ve bel çevresi 72 cm","eğer kumaş ipekse; bel çevresi 72 cm?; bel çevresi 72 cm"})
            {
                window.LoadForVerification(new DesignRequest("Sentetik istek","kalça çevresi 100 cm;elbise boyu 110 cm",null));window.ChatInputForVerification.Text=conditional;window.SendMessageForVerification();
                Check(window.MeasurementTilesForVerification["bel çevresi"].Text=="Kontrol edin"&&window.MeasurementTilesForVerification["kalça çevresi"].Text=="Kontrol edin"&&window.ConversationForVerification.Text.Contains("koşullu ifadenin"),"Unknown conditional scope blocks definite tiles across both fields: "+conditional,checks);
                var conditionalSaved=window.CaptureForVerification();window.LoadForVerification(DesignRequestStorage.Deserialize(DesignRequestStorage.Serialize(conditionalSaved)));
                Check(window.CaptureForVerification().Prompt==conditionalSaved.Prompt&&window.CaptureForVerification().Measurements==conditionalSaved.Measurements&&window.MeasurementTilesForVerification["bel çevresi"].Text=="Kontrol edin","Conditional source text and uncertainty survive save/load",checks);
            }
            window.LoadForVerification(new DesignRequest("bel çevresi 72 cm?;bel çevresi 72 cm","eğer kumaş ipekse;bel çevresi 73 cm",null));window.ReviewMeasurementsForVerification();
            Check(window.MeasurementTilesForVerification["bel çevresi"].Text=="Kontrol edin"&&window.ReviewForVerification.Text.Contains("koşullu")&&window.ReviewForVerification.Text.Contains("çelişiyor"),"Condition in notes survives question clarification and retains conflicting-value warning",checks);
            window.LoadForVerification(new DesignRequest("gömlek elbise;bel çevresi 72 cm?;bel çevresi 720 mm!","kalça çevresi 100 cm.",null));
            Check(window.MeasurementTilesForVerification["bel çevresi"].Text=="72 cm"&&window.MeasurementTilesForVerification["kalça çevresi"].Text=="100 cm","Elbise does not trigger suffix detection; nonconditional clarification stays functional",checks);
            var layoutBody=(StackPanel)window.BodyScrollerForVerification.Content;
            var composeParent=(UIElement)LogicalTreeHelper.GetParent(window.ChatInputForVerification);
            var primaryChat=(StackPanel)LogicalTreeHelper.GetParent(composeParent);
            var layoutRoot=(DockPanel)window.Content;
            Check(DockPanel.GetDock(primaryChat)==Dock.Top&&layoutRoot.Children.IndexOf(primaryChat)<layoutRoot.Children.IndexOf(window.BodyScrollerForVerification)&&!layoutBody.Children.Contains(composeParent),"Real chat composer is fixed above scrolling models and collapsible editing",checks);
            bool InDetails(DependencyObject element)
            {
                for(DependencyObject? current=element;current is not null;current=LogicalTreeHelper.GetParent(current))if(ReferenceEquals(current,window.RequestDetailsForVerification))return true;
                return false;
            }
            Check(InDetails(window.PromptForVerification)&&InDetails(window.MeasurementsForVerification)&&InDetails(window.ReferenceLabelForVerification)&&!InDetails(window.ChatInputForVerification),"Saved prose, measurements and photo controls belong to details; chat stays outside",checks);
            window.RequestDetailsForVerification.IsExpanded=false;
            window.LoadForVerification(new DesignRequest("Kayıtlı sentetik tarif","bel çevresi 72 cm",new DesignReference("sentetik.png","image/png",imageBytes)));
            var detailSnapshot=window.CaptureForVerification();window.ChatInputForVerification.Text="Gönderilmeyen taslak";
            window.RequestDetailsForVerification.IsExpanded=true;window.RequestDetailsForVerification.IsExpanded=false;
            Check(window.CaptureForVerification().Prompt==detailSnapshot.Prompt&&window.CaptureForVerification().Measurements==detailSnapshot.Measurements&&window.CaptureForVerification().Reference!.Data.SequenceEqual(imageBytes)&&window.ChatInputForVerification.Text=="Gönderilmeyen taslak","Expanding/collapsing preserves all stored fields, image bytes and unsent message",checks);
            Check(AutomationProperties.GetName(window.RequestDetailsForVerification)=="İstek ayrıntılarını düzenle"&&AutomationProperties.GetName(window.ChatInputForVerification)=="Elbise isteği veya ölçü mesajı","Details and primary chat retain explicit accessible names",checks);
            window.ReviewActionForVerification!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(window.RequestDetailsForVerification.IsExpanded&&window.ReviewForVerification.Text.Contains("720")&&window.ChatInputForVerification.Text=="Gönderilmeyen taslak","Persistent review action expands its result without consuming unsent text",checks);
            window.LoadForVerification(new DesignRequest("Sentetik sohbet başlangıcı","",null));window.RequestDetailsForVerification.IsExpanded=false;
            window.ConfirmChatModelForVerification=_=>false;window.ChatInputForVerification.Text="A kesim elbise istiyorum";window.SendActionForVerification!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(window.CaptureForVerification().ModelId is null&&window.CaptureForVerification().Prompt.EndsWith("A kesim elbise istiyorum",StringComparison.Ordinal)&&!window.RequestDetailsForVerification.IsExpanded,"Primary Send button still requires model approval while retaining text and collapsed details",checks);
            window.ChatInputForVerification.Text="bel çevresi 72 cm";window.SendActionForVerification.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(window.MeasurementTilesForVerification["bel çevresi"].Text=="72 cm"&&!window.RequestDetailsForVerification.IsExpanded&&window.ChatInputForVerification.Text.Length==0,"Chat measurement still updates hidden detail fields without forcing them open",checks);
            var photoShortcut=window.ReferenceShortcutForVerification!;
            var photoRow=(DockPanel)photoShortcut.Parent;
            Check(primaryChat.Children.IndexOf(photoRow)>primaryChat.Children.IndexOf(composeParent)&&!InDetails(photoShortcut)&&!InDetails(window.ReferenceSummaryForVerification),"Photo shortcut and persistent summary are next to chat outside collapsed request details",checks);
            Check(AutomationProperties.GetHelpText(photoShortcut).Contains("Görselden ölçü veya kalıp çıkarmaz")&&photoShortcut.Focusable,"Photo shortcut is accessible without implying image-to-pattern generation",checks);
            window.LoadForVerification(new DesignRequest("Fotoğrafı koru","bel çevresi 72 cm",new DesignReference("sentetik.png","image/png",imageBytes)));window.ChatInputForVerification.Text="Bekleyen mesaj";
            int chooserCalls=0;window.ReferencePathForVerification=()=> { chooserCalls++;return null; };
            photoShortcut.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));window.ReferencePathForVerification=null;
            Check(chooserCalls==1&&window.CaptureForVerification().Reference!.Data.SequenceEqual(imageBytes)&&window.CaptureForVerification().Prompt=="Fotoğrafı koru"&&window.ChatInputForVerification.Text=="Bekleyen mesaj"&&!window.RequestDetailsForVerification.IsExpanded,"Photo shortcut reaches existing chooser and cancellation preserves photo bytes and unsent text",checks);
            Check(window.ReferenceSummaryForVerification.Text.Contains("sentetik.png")&&AutomationProperties.GetName(window.ReferenceSummaryForVerification).Contains("kalıp çıkarılmaz"),"Selected reference stays visible and its accessible summary explains the limit",checks);
            window.LoadForVerification(new DesignRequest("Görsel isteği","",new DesignReference("foto\u202E\n.png","image/png",imageBytes)));
            Check(window.ReferenceSummaryForVerification.Text.Contains("\\u202E")&&window.ReferenceSummaryForVerification.Text.Contains("\\u000A")&&!window.ReferenceSummaryForVerification.Text.Contains('\u202E')&&window.CaptureForVerification().Reference!.Name=="foto\u202E\n.png","Reference name is safely displayed without changing saved name",checks);
            window.ChatInputForVerification.Text="uzun kollu";window.SendMessageForVerification();
            Check(window.ReferenceSummaryForVerification.Text.Contains("\\u202E")&&window.CaptureForVerification().Reference!.Data.SequenceEqual(imageBytes),"Sending a message preserves visible reference and bytes",checks);
            window.LoadForVerification(new DesignRequest("Görsel yok","",null));
            Check(window.ReferenceSummaryForVerification.Text=="Görsel seçilmedi","Opening a request without a reference clears the summary",checks);
            window.LoadForVerification(new DesignRequest("Sentetik kart isteği","bel çevresi 72 cm",null));window.ChatInputForVerification.Text="Uzun kollu olsun";
            var cardPanel=(Panel)window.ModelScrollerForVerification.Content;var alineCard=cardPanel.Children.OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="A kesim elbise");
            alineCard.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(window.CaptureForVerification().Prompt.Contains("A kesim elbise; detaylar:")&&!window.RequestDetailsForVerification.IsExpanded&&window.ChatInputForVerification.Text=="Uzun kollu olsun"&&ReferenceEquals(System.Windows.Input.FocusManager.GetFocusedElement(window),window.ChatInputForVerification),"V1 model card preserves pending message and directs logical focus to chat without opening saved prose",checks);
            Check(!window.IsVisible && !owner.IsVisible,"Verification never shows a window",checks);
            VerifyRequestNotesModel(owner,checks);
            VerifyModelReselection(owner,checks);
            VerifyRequestSavePath(owner,reportPath,checks);
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
