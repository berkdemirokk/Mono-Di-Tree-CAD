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
            window.ConfirmUnitConversionForVerification=()=>true;
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
            var stored=ALineWorkDraftStorage.Load(path);
            var expectedId=ALineWorkDraftStorage.SourceIdentity(new DesignRequest("A kesim elbise","bel çevresi 72 cm; kalça çevresi 100 cm",null));
            Check(stored.SourceHash==expectedId && stored.Fields.SequenceEqual(values) && stored.Unit=="cm" && File.ReadAllText(path).Contains(ALineWorkDraftStorage.SourceAlgorithm),
                "Version 2 work records the snapshot identity and preserves all raw fields and unit",checks);
            Click(window.BalanceExploreForVerification);window.ConfirmBalanceForVerification=()=>true;Click(window.BalanceAcceptForVerification);
            var candidatePath=Path.Combine(folder,"candidate"+ALineWorkDraftStorage.Extension);
            window.SaveWorkForVerification(candidatePath);var candidateDraft=ALineWorkDraftStorage.Load(candidatePath);
            Check(candidateDraft.Balance is not null && File.ReadAllText(candidatePath).Contains("\"version\":3") && candidateDraft.SourceHash==expectedId,
                "Candidate work saves versioned unapproved values bound to the request",checks);
            var candidateBytes=File.ReadAllBytes(candidatePath);bool candidateConflict=false;
            try { window.SaveWorkForVerification(candidatePath); } catch(ProjectConflictException) { candidateConflict=true; }
            Check(candidateConflict && File.ReadAllBytes(candidatePath).SequenceEqual(candidateBytes),"Candidate save cannot overwrite an existing work file",checks);
            var alteredCandidate=candidateDraft.Balance!.Candidate.ToArray();alteredCandidate[0]+=1;
            var tamperedPath=Path.Combine(folder,"tampered"+ALineWorkDraftStorage.Extension);
            ALineWorkDraftStorage.SaveNew(tamperedPath,candidateDraft with { Balance=candidateDraft.Balance with { Candidate=alteredCandidate } });
            var currentBefore=window.FieldsForVerification.Select(f=>f.Text).ToArray();bool tamperRejected=false;
            try { window.LoadWorkForVerification(tamperedPath,()=>throw new InvalidOperationException("Tampered candidate must not ask confirmation")); } catch(InvalidDataException) { tamperRejected=true; }
            Check(tamperRejected && currentBefore.SequenceEqual(window.FieldsForVerification.Select(f=>f.Text)),"Tampered candidate is rejected before replacing the form",checks);
            Check(window.LoadWorkForVerification(candidatePath,()=>true) && window.StatusForVerification.Text.Contains("yeniden hesaplandı") &&
                window.BalanceCanvasForVerification.Visibility==Visibility.Collapsed && !window.BalanceAcceptForVerification.IsEnabled && window.AgreementForVerification.IsChecked!=true && window.ConfirmationForVerification.IsChecked!=true,
                "Loading a candidate recomputes it and never restores prior approval",checks);
            window.ConfirmUnitConversionForVerification=()=>false;window.UnitForVerification.SelectedIndex=1;
            Check(window.UnitForVerification.SelectedIndex==0 && window.FieldsForVerification.Select(f=>f.Text).SequenceEqual(values),
                "Declining candidate unit conversion keeps unit and fields",checks);
            var canceledConversionPath=Path.Combine(folder,"candidate-canceled-unit"+ALineWorkDraftStorage.Extension);
            window.SaveWorkForVerification(canceledConversionPath);var afterCancel=ALineWorkDraftStorage.Load(canceledConversionPath);
            Check(afterCancel.Balance is not null && afterCancel.Balance.Original.SequenceEqual(candidateDraft.Balance!.Original) &&
                afterCancel.Balance.Candidate.SequenceEqual(candidateDraft.Balance.Candidate) && window.LoadWorkForVerification(canceledConversionPath,()=>true) &&
                window.StatusForVerification.Text.Contains("yeniden hesaplandı") && window.BalanceCanvasForVerification.Visibility==Visibility.Collapsed,
                "V3 load, canceled conversion, save and reload retain the unapproved candidate",checks);
            window.UnitForVerification.SelectedIndex=-1;
            window.ConfirmUnitConversionForVerification=()=> { window.UnitForVerification.SelectedIndex=1;return false; };window.UnitForVerification.SelectedIndex=1;
            var reentrantPath=Path.Combine(folder,"candidate-reentrant-unit"+ALineWorkDraftStorage.Extension);window.SaveWorkForVerification(reentrantPath);
            Check(window.UnitForVerification.SelectedIndex==0 && ALineWorkDraftStorage.Load(reentrantPath).Balance is not null,
                "Invalid or reentrant unit attempts leave the candidate available to save",checks);
            var wrongSourcePath=Path.Combine(folder,"candidate-wrong-source"+ALineWorkDraftStorage.Extension);
            ALineWorkDraftStorage.SaveNew(wrongSourcePath,candidateDraft with { SourceHash=new string('B',64) });
            Check(!window.TryOpenWork(()=>wrongSourcePath,()=>throw new InvalidOperationException("Different source must be rejected before approval")) &&
                window.StatusForVerification.Text.Contains("farklı bir isteğe bağlı") && window.StatusForVerification.Text.Contains("aynı kaynak istekle") &&
                window.FieldsForVerification.Select(f=>f.Text).SequenceEqual(values),
                "A candidate from a different request is rejected with a concrete explanation and no form changes",checks);
            window.ConfirmUnitConversionForVerification=()=>true;
            window.AgreementForVerification.IsChecked=true;Click(window.ReviewForVerification);window.ConfirmationForVerification.IsChecked=true;Click(window.DrawForVerification);Click(window.BalanceExploreForVerification);
            Check(window.BalanceAcceptForVerification.IsEnabled && window.BalanceCanvasForVerification.Visibility==Visibility.Collapsed,
                "Loaded candidate requires fresh review and approval before separate display",checks);
            var differentId=ALineWorkDraftStorage.SourceIdentity(new DesignRequest("Başka sipariş","",null));
            var differentPath=Path.Combine(folder,"different"+ALineWorkDraftStorage.Extension);
            ALineWorkDraftStorage.SaveNew(differentPath,new("cm",values,differentId));
            window.ConfirmSourceMismatchForVerification=warning=> { Check(warning.Contains(differentId) && warning.Contains(expectedId),"Mismatch confirmation displays both source identities",checks);return false; };
            Check(!window.LoadWorkForVerification(differentPath,()=>throw new InvalidOperationException("Replacement should not be asked")) && window.FieldsForVerification[0].Text=="36",
                "Declining a different source preserves the form",checks);
            window.ConfirmSourceMismatchForVerification=_=>true;
            Check(window.LoadWorkForVerification(differentPath,()=>true) && window.StatusForVerification.Text.Contains(differentId),"Explicit approval opens the different source with identity visible",checks);
            Check(window.SourceIdentityForVerification.Text.Contains(expectedId) && window.SourceIdentityForVerification.Text.Contains(differentId) && window.SourceIdentityForVerification.Text.Contains("farklı"),
                "Current and different draft source identities remain separately visible",checks);
            window.FieldsForVerification[0].Text="37";
            Check(window.SourceIdentityForVerification.Text.Contains(differentId) && window.SourceIdentityForVerification.Text.Contains("farklı"),
                "Editing the form does not erase the persistent different-source warning",checks);
            window.FieldsForVerification[0].Text="36";
            var retainedPath=Path.Combine(folder,"retained"+ALineWorkDraftStorage.Extension);window.SaveWorkForVerification(retainedPath);
            Check(ALineWorkDraftStorage.Load(retainedPath).SourceHash==differentId,"Resaving an approved different source retains its original identity",checks);
            window.LoadWorkForVerification(path,()=>true);
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
            Check(window.UnitForVerification.SelectedIndex==1 && window.FieldsForVerification[0].Text=="360" && !window.CanCloseWorkForVerification(),"Approved unit conversion changes raw values and requires discard approval",checks);
            window.UnitForVerification.SelectedIndex=0;
            Check(window.FieldsForVerification.Select(f=>f.Text).SequenceEqual(values),"Reverse unit conversion restores the saved values exactly",checks);
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
            Check(window.UnitForVerification.SelectedIndex==1 && window.FieldsForVerification[0].Text=="360" && window.StatusForVerification.Text.Contains("kaynak kimliği yok"),"Legacy draft preserves raw values and warns that source identity is absent",checks);
            window.FieldsForVerification[0].Text="361";
            Check(window.SourceIdentityForVerification.Text.Contains("kaynağı bilinmiyor"),"Legacy unknown origin stays visible after editing",checks);
            window.FieldsForVerification[0].Text="360";
            var conflict=Path.Combine(folder,"conflict"+ALineWorkDraftStorage.Extension);
            var changed=incompatible.Fields.ToArray();changed[0]="400";ALineWorkDraftStorage.SaveNew(conflict,new("mm",changed));
            window.LoadWorkForVerification(conflict,()=>true);window.AgreementForVerification.IsChecked=true;Click(window.ReviewForVerification);
            window.ConfirmationForVerification.IsChecked=true;Click(window.DrawForVerification);
            Check(window.CanvasForVerification.Children.Count==0 && window.StatusForVerification.Text.Contains("farklı"),
                "Loaded measurements still face current request circumference conflict guard before drawing",checks);
            var request=new DesignRequestWindow(owner) { ConfirmDiscardForVerification=_=>true };var requestData=new DesignRequest("A kesim elbise","ön bel yayı 36 cm",null,"aline",2);request.LoadForVerification(requestData);_=new System.Windows.Interop.WindowInteropHelper(request).EnsureHandle();
            var sourceWindow=new ALinePanelPreviewWindow(request,requestData.Prompt,requestData.Measurements) { ConfirmDiscardWorkForVerification=()=>true };
            try {
                var sourceFile=Path.Combine(folder,"source"+ALineWorkDraftStorage.Extension);sourceWindow.SaveWorkForVerification(sourceFile);
                sourceWindow.FieldsForVerification[0].Text="41";
                bool sourceRejected=false;
                try { sourceWindow.LoadWorkForVerification(sourceFile,()=> { request.PromptForVerification.Text="başka";request.PromptForVerification.Text=requestData.Prompt;return true; }); }
                catch(InvalidOperationException) { sourceRejected=true; }
                Check(sourceRejected && sourceWindow.FieldsForVerification[0].Text=="41","Source A-B-A during approval invalidates opening and preserves the form",checks);
                sourceRejected=false;
                try { sourceWindow.SaveSelectedWorkForVerification(()=> { request.PromptForVerification.Text="başka";request.PromptForVerification.Text=requestData.Prompt;return Path.Combine(folder,"stale-source"+ALineWorkDraftStorage.Extension); }); }
                catch(InvalidOperationException) { sourceRejected=true; }
                Check(sourceRejected && !File.Exists(Path.Combine(folder,"stale-source"+ALineWorkDraftStorage.Extension)),"Source A-B-A during save selection writes no file",checks);
                sourceWindow.UnitForVerification.SelectedIndex=0;sourceWindow.ConfirmSourceCorrectionForVerification=()=>true;
                sourceWindow.CorrectionFieldsForVerification.SelectedIndex=0;Click(sourceWindow.CorrectSourceForVerification);
                Check(request.CaptureForVerification().Measurements.Contains("41") && sourceWindow.FieldsForVerification[0].Text=="41", "Approved source correction updates the request and preserves form",checks);
                var correctedFile=Path.Combine(folder,"corrected-source"+ALineWorkDraftStorage.Extension);sourceWindow.SaveWorkForVerification(correctedFile);
                Check(ALineWorkDraftStorage.Load(correctedFile).SourceHash==ALineWorkDraftStorage.SourceIdentity(request.CaptureForVerification()) && sourceWindow.LoadWorkForVerification(correctedFile,()=>true),
                      "Corrected source work saves and reopens with the updated request identity",checks);
                sourceWindow.FieldsForVerification[1].Text="31";sourceWindow.ConfirmFormToRequestForVerification=()=>true;Click(sourceWindow.FormToRequestForVerification);
                Check(request.CaptureForVerification().Measurements.Contains("31"),"Approved form transfer updates source notes",checks);
                var transferredFile=Path.Combine(folder,"transferred-source"+ALineWorkDraftStorage.Extension);sourceWindow.SaveWorkForVerification(transferredFile);
                Check(ALineWorkDraftStorage.Load(transferredFile).SourceHash==ALineWorkDraftStorage.SourceIdentity(request.CaptureForVerification()) && sourceWindow.LoadWorkForVerification(transferredFile,()=>true),
                      "Transferred form work saves and reopens with the updated request identity",checks);
            } finally { sourceWindow.Close();request.Close(); }
            Check(!window.IsVisible && !owner.IsVisible,"Draft verification never shows windows",checks);
        }
        finally { window.ConfirmDiscardWorkForVerification=()=>true;window.Close();Directory.Delete(folder,true); }
    }
}
