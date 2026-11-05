using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

internal static partial class DesktopVerification
{
    private static void VerifyModelReselection(Window owner,List<string> checks)
    {
        var window=new DesignRequestWindow(owner);
        try
        {
            var root=(FrameworkElement)window.Content;root.Measure(new Size(900,700));root.Arrange(new Rect(0,0,900,700));root.UpdateLayout();
            var cards=new[]{("wrap","Anvelop elbise"),("aline","A kesim elbise"),("corset","Balenli korpus"),
                ("sheath","Minimal kılıf"),("straight","Düz elbise"),("shirt","Gömlek elbise")};
            Button Card(string name)=>Descendants<Button>(root).Single(b=>AutomationProperties.GetName(b)==name);
            static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            foreach(var (id,label) in cards)
            {
                var saved=new DesignRequest("Özgün tarif; model adı metne eklenmemeli.","bel çevresi 72 cm",null,id,2);
                window.LoadForVerification(saved);
                window.ConfirmDiscardForVerification=_=>throw new InvalidOperationException("Clean reselection must not ask to discard edits.");
                Click(Card(label));Click(Card(label));
                Check(!window.DirtyForVerification && window.CaptureForVerification()==saved && window.ClosingAllowedForVerification(),
                    "Repeated current V2 card selection retains clean saved content and closes without false loss warning: "+id,checks);
                var notes=window.CreateDrawingNotes();
                Check(notes is not null,"Clean reselection still permits drawing notes without duplicate save: "+id,checks);notes?.Close();
                Check(ReferenceEquals(FocusManager.GetFocusedElement(window),window.ChatInputForVerification) &&
                    window.StatusForVerification.Text.Contains("zaten seçili") && AutomationProperties.GetItemStatus(Card(label))=="Seçili",
                    "Same selection keeps card highlight, logical chat focus and explicit status: "+id,checks);
                window.PromptForVerification.Text="Gerçek kaydedilmemiş düzenleme";
                var dirty=window.CaptureForVerification();Click(Card(label));
                window.ConfirmDiscardForVerification=_=>false;
                Check(window.DirtyForVerification && window.CaptureForVerification()==dirty && !window.ClosingAllowedForVerification() &&
                    window.CreateDrawingNotes() is null,"Same selection preserves pre-existing real edits and their save gate: "+id,checks);
                window.LoadForVerification(saved);
                var other=cards.First(c=>c.Item1!=id);Click(Card(other.Item2));
                Check(window.DirtyForVerification && window.CaptureForVerification().ModelId==other.Item1 &&
                    window.CaptureForVerification().Prompt==saved.Prompt && window.CaptureForVerification().Measurements==saved.Measurements &&
                    window.CreateDrawingNotes() is null,"Different V2 model remains a real unsaved change without rewriting prose: "+id,checks);
            }
            Check(!window.IsVisible && !owner.IsVisible,"Model reselection verification uses no visible windows",checks);
        }
        finally { window.LoadForVerification(new DesignRequest("Temiz kapanış","",null));window.Close(); }
    }
}
