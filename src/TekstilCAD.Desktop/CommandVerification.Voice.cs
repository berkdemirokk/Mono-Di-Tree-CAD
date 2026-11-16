using System.Windows;
using System.Windows.Controls;
using TekstilCAD.Core;
using static TekstilCAD.Desktop.DesktopVerification;

namespace TekstilCAD.Desktop;

// PR 11: the "Sesle yaz" button only points to Windows voice typing.
internal static partial class CommandVerification
{
    private static void VerifyVoiceButton(List<string> checks,string? reportPath)
    {
        windowsStable=true;
        Scenario(window=>
        {
            var piece=MarkedPiece();window.LoadDocument(new ProjectDocument { Pieces=[piece] });window.Select(piece.Id);
            var session=SessionOf(window);var before=window.Document.Clone();var view=NewBox(window);
            var button=view.VoiceButton;
            Relayout(view);
            Check(button.ActualHeight>=44 && button.ActualWidth>=44 && button.Focusable && button.IsEnabled
                && System.Windows.Automation.AutomationProperties.GetName(button)=="Sesle yaz"
                && System.Windows.Automation.AutomationProperties.GetHelpText(button).Contains("Win + H")
                && Descendants<TextBlock>(button).Any(t=>t.Text=="Sesle yaz"),
                "The voice button is named, focusable and at least 44 px with its icon and text",checks);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            RenderView(view,reportPath,"voice");
            Check(ReferenceEquals(view.LastFocusRequest,view.Box) && view.State==CommandBarState.Voice
                && view.RowText=="Konuşmak için Win + H tuşlarına basın. Söyledikleriniz bu kutuya yazılır; siz uygulamadan hiçbir şey değişmez."
                && view.CaveatText=="Ses tanıma Windows'un özelliğidir; internet gerekebilir."
                && view.Text.Length==0 && DocumentSession.ContentEquals(before,window.Document) && session.UndoCount==0 && view.ShownPlan is null,
                "Voice button focuses the command box and shows the Windows voice typing guidance without starting recognition",checks);
            Type(view,"a");
            Check(view.State==CommandBarState.Idle && view.RowText.Length==0 && view.CaveatText.Length==0,"The voice guidance disappears when typing starts",checks);
            // Pressing the button never applies something that was waiting.
            Type(view,"25 sağa taşı");Enter(view);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(view.ShownPlan is null && view.State==CommandBarState.Voice && session.UndoCount==0 && DocumentSession.ContentEquals(before,window.Document),
                "The voice button applies nothing that was previewed",checks);
        });
        Check(windowsStable,"The voice button opens no window and no message box",checks);
    }
}
