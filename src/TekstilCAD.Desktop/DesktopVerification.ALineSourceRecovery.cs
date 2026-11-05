using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

internal static partial class DesktopVerification
{
    private static void VerifyALineSourceRecovery(Window owner,List<string> checks)
    {
        var window=new DesignRequestWindow(owner);
        const string photoNote="Fotoğraf gözlemi · Kol: Uzun kollu";
        var conflict=new DesignRequest(photoNote+"\nÖn bel yayı: 35 cm\nÖn bel yayı: 36 cm","bel çevresi 72 cm;kalça çevresi 100 cm",null,"aline",2);
        try
        {
            window.LoadForVerification(conflict);window.ChatInputForVerification.Text="Gönderilmemiş tasarım notum";
            var before=window.CaptureForVerification();
            Check(window.ALineSourceRecoveryForVerification.Visibility==Visibility.Visible && !window.RequestDetailsForVerification.IsExpanded,
                "A-line source issues expose recovery before opening normally collapsed details",checks);
            window.EditALinePromptForVerification.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(window.RequestDetailsForVerification.IsExpanded && ReferenceEquals(FocusManager.GetFocusedElement(window),window.PromptForVerification) &&
                window.CaptureForVerification()==before && window.ChatInputForVerification.Text=="Gönderilmemiş tasarım notum",
                "Prompt recovery opens the existing editable source with accessible focus and preserves unsent chat and all notes",checks);
            window.RequestDetailsForVerification.IsExpanded=false;
            Check(window.CaptureForVerification()==before && window.ChatInputForVerification.Text=="Gönderilmemiş tasarım notum",
                "Leaving recovery without editing is a no-op",checks);
            window.EditALinePromptForVerification.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.PromptForVerification.Text=photoNote+"\nÖn bel yayı: 36 cm";
            Check(window.ALineSourceRecoveryForVerification.Visibility==Visibility.Collapsed && window.ConversationForVerification.Text.Contains("Sıradaki alan (2/11): Arka bel yayı") &&
                window.MeasurementsForVerification.Text==conflict.Measurements && window.ChatInputForVerification.Text=="Gönderilmemiş tasarım notum",
                "Explicit source correction resumes the next missing question while preserving photo notes, measurement notes and draft chat",checks);

            var cross=conflict with { Prompt=photoNote+"\nÖn bel yayı: 36 cm",Measurements="Ön bel yayı: 35 cm;bel çevresi 72 cm" };
            window.LoadForVerification(cross);window.ChatInputForVerification.Text="36 cm";
            window.EditALineNotesForVerification.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(ReferenceEquals(FocusManager.GetFocusedElement(window),window.MeasurementsForVerification) && window.CaptureForVerification()==cross,
                "Cross-field recovery can explicitly focus notes without guessing which conflicting value is correct",checks);
            window.MeasurementsForVerification.Text="bel çevresi 72 cm";
            var corrected=window.CaptureForVerification();window.SendMessageForVerification();
            Check(window.CaptureForVerification()==corrected && window.ChatInputForVerification.Text=="36 cm" && window.StatusForVerification.Text.Contains("Soru veya kaynak"),
                "Correcting source does not silently rebind a previously typed numeric chat answer",checks);

            window.LoadForVerification(conflict);
            var root=(FrameworkElement)window.Content;
            foreach(var size in new[]{new Size(480,420),new Size(900,700)})
            {
                root.Measure(size);root.Arrange(new Rect(new Point(),size));root.UpdateLayout();
                var panel=window.ALineSourceRecoveryForVerification;
                Check(panel.ActualWidth>0 && panel.ActualHeight>0 && panel.ActualWidth<=size.Width &&
                    window.EditALinePromptForVerification.ActualWidth>0 && window.EditALineNotesForVerification.ActualWidth>0 &&
                    window.EditALinePromptForVerification.IsTabStop && window.EditALineNotesForVerification.IsTabStop &&
                    AutomationProperties.GetHelpText(window.EditALineNotesForVerification).Contains("korunur"),
                    "Recovery wraps inside the scrollable body and remains keyboard-labelled at "+size,checks);
            }
            var wrap=Descendants<Button>(root).Single(b=>AutomationProperties.GetName(b)=="Anvelop elbise");
            window.RequestDetailsForVerification.IsExpanded=false;wrap.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var other=window.CaptureForVerification();
            window.EditALinePromptForVerification.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(window.ALineSourceRecoveryForVerification.Visibility==Visibility.Collapsed && !window.RequestDetailsForVerification.IsExpanded && window.CaptureForVerification()==other,
                "Changing model hides recovery and an old action cannot reopen or alter another model's source",checks);
            Check(!window.IsVisible && !owner.IsVisible,"Source recovery verification does not show windows",checks);
        }
        finally { window.LoadForVerification(new DesignRequest("Temiz kapanış","",null));window.Close(); }
    }
}
