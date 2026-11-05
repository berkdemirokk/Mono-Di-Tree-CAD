using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

internal static partial class DesktopVerification
{
    private static void VerifyALineEntry(Window owner,List<string> checks)
    {
        var request=new DesignRequestWindow(owner);_ =new WindowInteropHelper(request).EnsureHandle();
        try
        {
            static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var root=(FrameworkElement)request.Content;
            void Layout(Size size)
            {
                root.Measure(size);root.Arrange(new Rect(size));root.UpdateLayout();
                request.BodyScrollerForVerification.ScrollToTop();root.UpdateLayout();
            }
            Layout(new Size(900,700));
            var aline=Descendants<Button>(root).Single(b=>AutomationProperties.GetName(b)=="A kesim elbise");
            var loaded=new DesignRequest("Özgün tarif","bel çevresi 72 cm;kalça çevresi 100 cm",null,"aline",2);
            request.LoadForVerification(loaded);
            foreach(var size in new[]{new Size(480,420),new Size(900,700)})
            {
                Layout(size);var entry=request.ALinePreviewEntryForVerification;var viewport=request.BodyScrollerForVerification;
                var position=entry.TransformToAncestor(viewport).Transform(new Point());
                Check(entry.Visibility==Visibility.Visible && entry.IsEnabled && entry.Focusable &&
                    entry.Parent==viewport.Content && ((Panel)viewport.Content).Children.OfType<UIElement>().First(child=>child.Visibility==Visibility.Visible)==entry &&
                    request.ALineSourceRecoveryForVerification.Visibility==Visibility.Collapsed &&
                    !request.RequestDetailsForVerification.IsExpanded && viewport.ActualHeight>0 && position.Y>=0 && position.Y+entry.ActualHeight<=viewport.ActualHeight &&
                    entry.ActualHeight>0 && entry.ActualWidth>0 && request.ChatInputForVerification.ActualHeight>0 &&
                    Descendants<TextBlock>(entry).Any(t=>t.Text.Contains("Tam elbise kalıbı değildir")),
                    $"A-line entry is the first scrollable action below chat at {size.Width}x{size.Height}, with visible capability boundary",checks);
            }
            request.LoadForVerification(loaded with { Prompt="Ön bel yayı: 35 cm\nÖn bel yayı: 36 cm" });
            foreach(var size in new[]{new Size(480,420),new Size(900,700)})
            {
                Layout(size);var viewport=request.BodyScrollerForVerification;
                Check(request.ALineSourceRecoveryForVerification.Visibility==Visibility.Visible &&
                    ((Panel)viewport.Content).Children.OfType<UIElement>().First(child=>child.Visibility==Visibility.Visible)==request.ALineSourceRecoveryForVerification,
                    $"Blocked A-line source puts recovery first at {size.Width}x{size.Height}",checks);
                foreach(var action in new[]{request.EditALinePromptForVerification,request.EditALineNotesForVerification,request.ALinePreviewEntryForVerification})
                {
                    action.BringIntoView();root.UpdateLayout();
                    var position=action.TransformToAncestor(viewport).Transform(new Point());
                    Check(action.Visibility==Visibility.Visible && action.IsEnabled && action.Focusable && action.IsTabStop &&
                        action.ActualHeight>0 && viewport.ActualHeight>0 && position.Y>=-1 && position.Y+action.ActualHeight<=viewport.ActualHeight+1 &&
                        !string.IsNullOrEmpty(AutomationProperties.GetName(action)),
                        $"Recovery and preview actions remain individually scrollable and keyboard accessible at {size.Width}x{size.Height}: "+AutomationProperties.GetName(action),checks);
                }
            }
            request.LoadForVerification(loaded);
            request.ChatInputForVerification.Text="Gönderilmemiş özgün mesaj";
            Check(request.CreateALinePreview() is null && request.ChatInputForVerification.Text=="Gönderilmemiş özgün mesaj" &&
                request.CaptureForVerification()==loaded,"Preview entry preserves and blocks unsent message",checks);
            request.ChatInputForVerification.Clear();
            var preview=request.CreateALinePreview();
            Check(preview is not null && preview.KnownTotalsForVerification.Text.Contains("72 cm") &&
                preview.KnownTotalsForVerification.Text.Contains("100 cm") && preview.FieldsForVerification.All(f=>f.Text.Length==0) &&
                !preview.ConfirmationForVerification.IsEnabled && preview.CanvasForVerification.Children.Count==0 &&
                request.CaptureForVerification()==loaded,"Discoverable entry uses existing snapshot, measurement and approval gates without inventing fields",checks);preview?.Close();
            foreach(var id in new string?[]{"wrap","corset","sheath","straight","shirt",null})
            {
                request.LoadForVerification(loaded with { ModelId=id,Prompt="A kesim elbise; detaylar: " });
                Check(request.ALinePreviewEntryForVerification.Visibility==Visibility.Collapsed && request.CreateALinePreview() is null,
                    "Other or missing V2 identity does not advertise A-line preview: "+(id??"none"),checks);
            }
            request.LoadForVerification(new DesignRequest("A kesim elbise; detaylar: ","",null));
            Check(request.ALinePreviewEntryForVerification.Visibility==Visibility.Collapsed && request.CreateALinePreview() is null,
                "Loaded V1 template is only a candidate and cannot advertise preview before explicit choice",checks);
            Click(aline);
            Check(request.ALinePreviewEntryForVerification.Visibility==Visibility.Visible,
                "Explicit confirmation of the loaded V1 model reveals the same preview entry",checks);
            request.PromptForVerification.Text="A kesim elbise istemiyorum";
            Check(request.ALinePreviewEntryForVerification.Visibility==Visibility.Collapsed && request.CreateALinePreview() is null,
                "Editing tracked V1 model text invalidates preview discovery",checks);
            request.LoadForVerification(new DesignRequest("A kesim elbise; detaylar: Anvelop elbise; detaylar: ","",null));
            Check(request.ALinePreviewEntryForVerification.Visibility==Visibility.Collapsed,"Ambiguous V1 source does not expose preview",checks);
            Check(!request.IsVisible && !owner.IsVisible,"Entry verification does not show windows",checks);
        }
        finally { request.LoadForVerification(new DesignRequest("Temiz kapanış","",null));request.Close(); }
    }
}
