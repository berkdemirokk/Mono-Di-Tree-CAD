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
            window.ConfirmDiscardWorkForVerification=()=>true;
        try
        {
            static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.ConfirmDiscardWorkForVerification=()=>throw new InvalidOperationException("Clean form must not prompt");
            Check(window.CanCloseWorkForVerification(),"Untouched empty work can close without a discard prompt",checks);
            var path=Path.Combine(folder,"saved"+ALineWorkDraftStorage.Extension);
            string[] values=["36","36","50","50","0","0","0","0","20","70","160"];
            window.UnitForVerification.SelectedIndex=0;
            for(var i=0;i<values.Length;i++)window.FieldsForVerification[i].Text=values[i];
            window.AgreementForVerification.IsChecked=true;Click(window.ReviewForVerification);
            window.ConfirmationForVerification.IsChecked=true;Click(window.DrawForVerification);
            Check(window.CanvasForVerification.Children.Count>0,"Draft fixture initially produces only explicitly confirmed preview",checks);
            VerifyALineStorageErrorBoundary(window,folder,checks);
            window.SaveWorkForVerification(path);
            Check(window.CanCloseWorkForVerification(),"Successfully saved raw fields and unit establish the clean checkpoint",checks);
            window.FieldsForVerification[0].Text="37";
            var closed=false;window.Closed+=(_,_)=>closed=true;
            window.ConfirmDiscardWorkForVerification=()=>false;
            window.Close();
            Check(!closed && window.FieldsForVerification[0].Text=="37","Cancelling actual WPF close preserves unsaved measurements and the window",checks);
            window.ConfirmDiscardWorkForVerification=()=> { window.FieldsForVerification[0].Text="38";return true; };
            Check(!window.CanCloseWorkForVerification() && window.FieldsForVerification[0].Text=="38","Changed work during close approval invalidates that approval",checks);
            window.ConfirmDiscardWorkForVerification=()=> { Check(!window.CanCloseWorkForVerification(),"Nested close request is denied during discard approval",checks);return true; };
            Check(window.CanCloseWorkForVerification(),"Explicit unchanged discard approval permits close",checks);
            window.FieldsForVerification[0].Text="36";
            window.ConfirmDiscardWorkForVerification=()=>throw new InvalidOperationException("Restored saved values must not prompt");
            Check(window.CanCloseWorkForVerification(),"Restoring exact saved values clears dirty state despite intervening edits",checks);
            window.FieldsForVerification[0].Text="37";
            var stalePath=Path.Combine(folder,"stale"+ALineWorkDraftStorage.Extension);
            bool staleSave=false;
            try { window.SaveSelectedWorkForVerification(()=> { window.FieldsForVerification[0].Text="39";return stalePath; }); }
            catch(InvalidOperationException) { staleSave=true; }
            Check(staleSave && !File.Exists(stalePath) && window.FieldsForVerification[0].Text=="39","Save selector cannot silently save a revision changed while it was open",checks);
            Check(!window.SaveSelectedWorkForVerification(()=>null) && !File.Exists(stalePath),"Cancelled save selection creates no file",checks);
            window.ConfirmDiscardWorkForVerification=()=>false;
            Check(!window.CanCloseWorkForVerification(),"Rejected or cancelled save leaves the unsaved checkpoint intact",checks);
            bool saveConflict=false;try { window.SaveWorkForVerification(path); }catch(ProjectConflictException) { saveConflict=true; }
            Check(saveConflict && !window.CanCloseWorkForVerification() && ALineWorkDraftStorage.Load(path).Fields[0]=="36",
                "Existing destination rejection preserves both durable file and unsaved form state",checks);
            window.FieldsForVerification[0].Text="37";
            Check(!window.LoadWorkForVerification(path,()=>false) && window.FieldsForVerification[0].Text=="37", "Cancelled draft opening preserves current form",checks);
            Check(window.LoadWorkForVerification(path,()=>true) && window.FieldsForVerification.Select(f=>f.Text).SequenceEqual(values) &&
                window.UnitForVerification.SelectedIndex==0 && window.AgreementForVerification.IsChecked!=true &&
                window.ConfirmationForVerification.IsChecked!=true && !window.ConfirmationForVerification.IsEnabled &&
                window.ProposalAgreementForVerification.IsChecked!=true && !window.ProposalApplyForVerification.IsEnabled &&
                window.CanvasForVerification.Children.Count==0 && window.SummaryForVerification.Text=="",
                "Opening valid draft restores text and unit but clears all gates, transfer consent and rendered geometry",checks);
            window.ConfirmDiscardWorkForVerification=()=>throw new InvalidOperationException("Loaded values must not prompt");
            Check(window.CanCloseWorkForVerification(),"Successful draft load establishes clean fields without establishing any approval",checks);
            window.UnitForVerification.SelectedIndex=1;window.ConfirmDiscardWorkForVerification=()=>false;
            Check(!window.CanCloseWorkForVerification(),"Unit-only changes require discard approval",checks);
            window.UnitForVerification.SelectedIndex=0;
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
        finally { window.ConfirmDiscardWorkForVerification=()=>true;window.Close();Directory.Delete(folder,true); }
    }
}
