using System.IO;
using System.Windows;
using System.Windows.Controls;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

internal static partial class DesktopVerification
{
    private static void VerifyALineWorkDraft(Window owner,string reportPath,List<string> checks)
    {
        var folder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,"aline-work-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var window=new ALinePanelPreviewWindow(owner,"A kesim elbise","bel çevresi 72 cm; kalça çevresi 100 cm");
        try
        {
            static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var path=Path.Combine(folder,"saved"+ALineWorkDraftStorage.Extension);
            string[] values=["36","36","50","50","0","0","0","0","20","70","160"];
            window.UnitForVerification.SelectedIndex=0;
            for(var i=0;i<values.Length;i++)window.FieldsForVerification[i].Text=values[i];
            window.AgreementForVerification.IsChecked=true;Click(window.ReviewForVerification);
            window.ConfirmationForVerification.IsChecked=true;Click(window.DrawForVerification);
            Check(window.CanvasForVerification.Children.Count>0,"Draft fixture initially produces only explicitly confirmed preview",checks);
            window.SaveWorkForVerification(path);
            window.FieldsForVerification[0].Text="37";
            Check(!window.LoadWorkForVerification(path,()=>false) && window.FieldsForVerification[0].Text=="37", "Cancelled draft opening preserves current form",checks);
            Check(window.LoadWorkForVerification(path,()=>true) && window.FieldsForVerification.Select(f=>f.Text).SequenceEqual(values) &&
                window.UnitForVerification.SelectedIndex==0 && window.AgreementForVerification.IsChecked!=true &&
                window.ConfirmationForVerification.IsChecked!=true && !window.ConfirmationForVerification.IsEnabled &&
                window.ProposalAgreementForVerification.IsChecked!=true && !window.ProposalApplyForVerification.IsEnabled &&
                window.CanvasForVerification.Children.Count==0 && window.SummaryForVerification.Text=="",
                "Opening valid draft restores text and unit but clears all gates, transfer consent and rendered geometry",checks);
            Click(window.DrawForVerification);
            Check(window.CanvasForVerification.Children.Count==0,"Restored draft cannot draw without renewed confirmations",checks);
            var bad=Path.Combine(folder,"bad"+ALineWorkDraftStorage.Extension);File.WriteAllText(bad,"{}");
            bool rejected=false;try { window.LoadWorkForVerification(bad,()=>throw new InvalidOperationException("Must not ask before validation")); }catch(InvalidDataException) { rejected=true; }
            Check(rejected && window.FieldsForVerification.Select(f=>f.Text).SequenceEqual(values),"Malformed file is rejected before confirmation and preserves every existing value",checks);
            rejected=false;try { window.LoadWorkForVerification(path,()=> { window.FieldsForVerification[0].Text="38";return true; }); }catch(InvalidOperationException) { rejected=true; }
            Check(rejected && window.FieldsForVerification[0].Text=="38","Changed form during open confirmation is not overwritten",checks);
            var incompatible=new ALineWorkDraft("mm",["360","360","500","500","0","0","0","0","200","700","1600"]);
            var mm=Path.Combine(folder,"mm"+ALineWorkDraftStorage.Extension);ALineWorkDraftStorage.SaveNew(mm,incompatible);
            window.LoadWorkForVerification(mm,()=>true);
            Check(window.UnitForVerification.SelectedIndex==1 && window.FieldsForVerification[0].Text=="360","Draft unit is restored with unchanged raw values, without implicit conversion",checks);
            var conflict=Path.Combine(folder,"conflict"+ALineWorkDraftStorage.Extension);
            var changed=incompatible.Fields.ToArray();changed[0]="400";ALineWorkDraftStorage.SaveNew(conflict,new("mm",changed));
            window.LoadWorkForVerification(conflict,()=>true);window.AgreementForVerification.IsChecked=true;Click(window.ReviewForVerification);
            window.ConfirmationForVerification.IsChecked=true;Click(window.DrawForVerification);
            Check(window.CanvasForVerification.Children.Count==0 && window.StatusForVerification.Text.Contains("farklı"),
                "Loaded measurements still face current request circumference conflict guard before drawing",checks);
            Check(!window.IsVisible && !owner.IsVisible,"Draft verification never shows windows",checks);
        }
        finally { window.Close();Directory.Delete(folder,true); }
    }
}
