using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using TekstilCAD.FileFormats;
using Polygon = System.Windows.Shapes.Polygon;

namespace TekstilCAD.Desktop;

internal static partial class DesktopVerification
{
    // No Show/ShowDialog, CAD document, recovery session or production files.
    public static int RunALine(string reportPath)
    {
        var checks=new List<string>();string? failure=null;
        var owner=new Window();ALinePanelPreviewWindow? window=null;ALinePanelPreviewWindow? conflict=null;DesignRequestWindow? request=null;
        try
        {
            // WPF requires an HWND before another Window can name this owner.
            // EnsureHandle creates the native source without Show/ShowDialog.
            var ownerHandle=new WindowInteropHelper(owner).EnsureHandle();
            Check(ownerHandle!=IntPtr.Zero && !owner.IsVisible,"Owner has a native handle without being shown",checks);
            request=new DesignRequestWindow(owner);
            request.LoadForVerification(new DesignRequest("Serbest tarif","",null,"aline",2));
            Check(request.CanPreviewALineForVerification,"Loaded V2 A-line identity enables the preview without requiring a V1 text template",checks);
            request.LoadForVerification(new DesignRequest("A kesim elbise; detaylar: ","",null,"wrap",2));
            Check(!request.CanPreviewALineForVerification,"V2 non-A-line identity cannot be overridden by descriptive text",checks);
            request.LoadForVerification(new DesignRequest("A kesim elbise; detaylar: ","",null,null,2));
            Check(!request.CanPreviewALineForVerification,"Missing V2 identity does not infer an A-line model from text",checks);
            window=new ALinePanelPreviewWindow(owner,"A kesim elbise","bel çevresi 72 cm; kalça çevresi 100 cm");
            window.ConfirmDiscardWorkForVerification=()=>true;
            Check(window.KnownTotalsForVerification.Text.Contains("72 cm") && window.KnownTotalsForVerification.Text.Contains("100 cm") &&
                window.KnownTotalsForVerification.Text.Contains("ön ve arka") && window.FieldsForVerification.All(f=>f.Text.Length==0),
                "Existing waist and hip totals are shown initially without inventing or filling front/back arcs",checks);
            static void Click(Button b)=>b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            static void Fill(ALinePanelPreviewWindow w,bool cm)
            {
                string[] values=cm ? ["36","36","50","50","2","2","2","2","20","70","160"]
                    : ["360","360","500","500","20","20","20","20","200","700","1600"];
                for(int i=0;i<values.Length;i++)w.FieldsForVerification[i].Text=values[i];
            }
            static Point[][] Polygons(ALinePanelPreviewWindow w)=>w.CanvasForVerification.Children.OfType<Polygon>().Select(p=>p.Points.ToArray()).ToArray();
            static bool Cleared(ALinePanelPreviewWindow w)=>w.ConfirmationForVerification.IsChecked!=true && !w.ConfirmationForVerification.IsEnabled &&
                w.SummaryForVerification.Text=="" && w.CanvasForVerification.Children.Count==0;
            void ReviewAndDraw(ALinePanelPreviewWindow w)
            {
                Click(w.ReviewForVerification);Check(w.ConfirmationForVerification.IsEnabled,"Valid fields enable explicit measurement confirmation",checks);
                w.ConfirmationForVerification.IsChecked=true;Click(w.DrawForVerification);
                Check(Polygons(w).Length==2,"Confirmed fields create two preview polygons",checks);
            }
            Check(window.UnitForVerification.SelectedIndex==-1 && window.AgreementForVerification.IsChecked!=true &&
                !window.ConfirmationForVerification.IsEnabled,"Initial unit and assumptions are unset; confirmation unavailable",checks);
            Fill(window,true);Click(window.ReviewForVerification);Click(window.DrawForVerification);
            Check(window.CanvasForVerification.Children.Count==0 && !window.ConfirmationForVerification.IsEnabled &&
                window.StatusForVerification.Text.Contains("varsayım"),"Missing assumption acceptance blocks review and drawing",checks);
            window.AgreementForVerification.IsChecked=true;Click(window.ReviewForVerification);
            Check(!window.ConfirmationForVerification.IsEnabled && window.StatusForVerification.Text.Contains("birimini seçin"),"Accepted assumptions without a selected unit still reject review",checks);
            window.UnitForVerification.SelectedIndex=0;Click(window.ReviewForVerification);
            Check(window.SummaryForVerification.Text.Contains("360 mm = 36 cm") && window.SummaryForVerification.Text.Contains("720 mm = 72 cm") &&
                window.SummaryForVerification.Text.Contains("1000 mm = 100 cm") && window.SummaryForVerification.Text.Contains("72 mm yalnız 7,2 cm"),
                "Review exposes normalized cm/mm values, full body totals and unit mistake example",checks);
            Click(window.DrawForVerification);
            Check(window.CanvasForVerification.Children.Count==0 && window.StatusForVerification.Text.Contains("onaylayın"),"Review alone cannot bypass confirmation",checks);
            window.ConfirmationForVerification.IsChecked=true;Click(window.DrawForVerification);var cmPoints=Polygons(window);
            Check(cmPoints.Length==2,"Explicit confirmation produces front and back preview",checks);
            window.FieldsForVerification[9].Text="71";
            Check(Cleared(window),"Changing a measurement clears summary, confirmation and all old drawing",checks);
            Click(window.DrawForVerification);Check(window.CanvasForVerification.Children.Count==0,"Changed measurement cannot redraw without a new review",checks);
            window.FieldsForVerification[9].Text="70";ReviewAndDraw(window);
            window.UnitForVerification.SelectedIndex=1;Check(Cleared(window),"Changing units invalidates previous confirmation and drawing",checks);
            Fill(window,false);ReviewAndDraw(window);var mmPoints=Polygons(window);
            Check(cmPoints.Length==mmPoints.Length && cmPoints.Zip(mmPoints).All(pair=>pair.First.SequenceEqual(pair.Second)),"Equivalent cm and mm inputs create identical preview geometry",checks);
            window.AgreementForVerification.IsChecked=false;Check(Cleared(window),"Revoking symmetry assumptions clears previous confirmation and drawing",checks);
            window.AgreementForVerification.IsChecked=true;Check(Cleared(window),"Reaccepting assumptions does not restore an old confirmation",checks);
            ReviewAndDraw(window);window.ConfirmationForVerification.IsChecked=false;
            Check(window.CanvasForVerification.Children.Count==0,"Revoking measurement confirmation removes the drawing",checks);
            conflict=new ALinePanelPreviewWindow(owner,"A kesim elbise","bel çevresi 80 cm; kalça çevresi 100 cm");
            conflict.ConfirmDiscardWorkForVerification=()=>true;
            Fill(conflict,true);conflict.UnitForVerification.SelectedIndex=0;conflict.AgreementForVerification.IsChecked=true;
            Click(conflict.ReviewForVerification);conflict.ConfirmationForVerification.IsChecked=true;Click(conflict.DrawForVerification);
            Check(conflict.CanvasForVerification.Children.Count==0 && conflict.StatusForVerification.Text.Contains("kayıtlı istek ölçüsünden farklı"),
                "Existing request waist conflict blocks drawing even after field confirmation",checks);
            var explicitSections=string.Join(";",ALineRequestMeasurements.Labels.Zip(new[]{"36","36","50","50","2","2","2","2","20","70","160"},(label,value)=>label+" "+value+" cm"));
            var sourceText="bel çevresi 72 cm;kalça çevresi 100 cm;"+explicitSections;
            request.LoadForVerification(new DesignRequest("A kesim elbise",sourceText,null,"aline",2));
            var requestBeforeProposal=request.CaptureForVerification();
            var proposalWindow=new ALinePanelPreviewWindow(owner,requestBeforeProposal.Prompt,requestBeforeProposal.Measurements);
            proposalWindow.ConfirmDiscardWorkForVerification=()=>true;
            try
            {
                Check(proposalWindow.KnownTotalsForVerification.Text.Contains("72 cm") && proposalWindow.KnownTotalsForVerification.Text.Contains("100 cm") &&
                    proposalWindow.FieldsForVerification.All(f=>f.Text.Length==0) && !proposalWindow.ProposalApplyForVerification.IsEnabled && !proposalWindow.ProposalAgreementForVerification.IsEnabled && proposalWindow.UnitForVerification.SelectedIndex==-1,
                    "Explicit front/back clauses retain totals but transfer requires an explicit target unit",checks);
                proposalWindow.UnitForVerification.SelectedIndex=1;
                Check(proposalWindow.ProposalAgreementForVerification.IsEnabled,"Selecting mm permits review of the complete transfer plan",checks);
                Check(proposalWindow.ProposalForVerification.Text.Contains("360 mm = 36 cm") && proposalWindow.ProposalForVerification.Text.Contains("Ön bel yayı bolluğu"),
                    "Request proposal exposes normalized source values and explicit ease before acceptance",checks);
                Click(proposalWindow.ProposalApplyForVerification);
                Check(proposalWindow.FieldsForVerification.All(f=>f.Text.Length==0),"Unconfirmed proposal cannot populate experimental fields",checks);
                proposalWindow.ProposalAgreementForVerification.IsChecked=true;Click(proposalWindow.ProposalApplyForVerification);
                Check(proposalWindow.UnitForVerification.SelectedIndex==1 && proposalWindow.FieldsForVerification[0].Text=="360" && proposalWindow.FieldsForVerification[4].Text=="20" &&
                    proposalWindow.CanvasForVerification.Children.Count==0 && proposalWindow.AgreementForVerification.IsChecked!=true && !proposalWindow.ConfirmationForVerification.IsEnabled,
                    "Accepted proposal transfers explicit mm values but cannot accept symmetry, confirm geometry or draw",checks);
                proposalWindow.AgreementForVerification.IsChecked=true;ReviewAndDraw(proposalWindow);
                proposalWindow.FieldsForVerification[0].Text="370";Click(proposalWindow.ReviewForVerification);Click(proposalWindow.DrawForVerification);
                Check(proposalWindow.CanvasForVerification.Children.Count==0 && proposalWindow.StatusForVerification.Text.Contains("açık istek ölçüsüyle çelişiyor"),
                    "Editing a proposed arc invalidates drawing and cannot contradict its source",checks);
                Click(proposalWindow.ProposalApplyForVerification);
                Check(proposalWindow.FieldsForVerification[0].Text=="370","Retrying transfer never overwrites a manually edited field",checks);
                Check(request.CaptureForVerification()==requestBeforeProposal && !request.DirtyForVerification && !proposalWindow.IsVisible,
                    "Proposal verification remains unshown and leaves its request text unchanged",checks);
            }
            finally { proposalWindow.Close(); }
            var partialWindow=new ALinePanelPreviewWindow(owner,"A kesim elbise","ön bel yayı 36 cm;arka kalça yayı 500 mm;ön bel yayı bolluğu 0.0125 cm");
            partialWindow.ConfirmDiscardWorkForVerification=()=>true;
            try
            {
                Check(!partialWindow.ProposalAgreementForVerification.IsEnabled && partialWindow.FieldsForVerification.All(f=>f.Text.Length==0),
                    "Partial source without chosen target unit neither offers acceptance nor fills fields",checks);
                partialWindow.UnitForVerification.SelectedIndex=1;partialWindow.FieldsForVerification[1].Text=" 360 ";
                Check(partialWindow.ProposalAgreementForVerification.IsEnabled && partialWindow.ProposalForVerification.Text.Contains("KORUNACAK") &&
                    partialWindow.ProposalForVerification.Text.Contains("EKSİK") && partialWindow.ProposalForVerification.Text.Contains("0.1250 mm"),
                    "Partial plan shows preserved, missing and exactly represented source values",checks);
                partialWindow.ProposalAgreementForVerification.IsChecked=true;partialWindow.FieldsForVerification[8].Text="200";
                Check(partialWindow.ProposalAgreementForVerification.IsChecked!=true && !partialWindow.ProposalApplyForVerification.IsEnabled,
                    "Editing any field invalidates the accepted transfer plan",checks);
                Click(partialWindow.ProposalApplyForVerification);
                Check(partialWindow.FieldsForVerification[0].Text=="","Stale field acceptance cannot copy a source value",checks);
                partialWindow.ProposalAgreementForVerification.IsChecked=true;partialWindow.UnitForVerification.SelectedIndex=0;
                Check(partialWindow.ProposalAgreementForVerification.IsChecked!=true && !partialWindow.ProposalApplyForVerification.IsEnabled && partialWindow.FieldsForVerification[1].Text==" 360 ",
                    "Changing target units invalidates transfer approval while preserving typed text",checks);
                partialWindow.UnitForVerification.SelectedIndex=1;partialWindow.ProposalAgreementForVerification.IsChecked=true;Click(partialWindow.ProposalApplyForVerification);
                Check(partialWindow.FieldsForVerification[0].Text=="360" && partialWindow.FieldsForVerification[3].Text=="500" && partialWindow.FieldsForVerification[4].Text=="0.1250" &&
                    partialWindow.FieldsForVerification[1].Text==" 360 " && partialWindow.FieldsForVerification[8].Text=="200" && partialWindow.FieldsForVerification[5].Text=="",
                    "Confirmed partial transfer copies only known empty fields, preserving unknown ease and typed values",checks);
                Check(partialWindow.AgreementForVerification.IsChecked!=true && !partialWindow.ConfirmationForVerification.IsEnabled && partialWindow.CanvasForVerification.Children.Count==0,
                    "Partial transfer cannot assume symmetry or enable final confirmation or drawing",checks);
                partialWindow.AgreementForVerification.IsChecked=true;Click(partialWindow.ReviewForVerification);Click(partialWindow.DrawForVerification);
                Check(!partialWindow.ConfirmationForVerification.IsEnabled && partialWindow.CanvasForVerification.Children.Count==0,
                    "Missing fields after transfer still prevent all preview geometry",checks);
                Fill(partialWindow,false);partialWindow.FieldsForVerification[4].Text="0.1250";ReviewAndDraw(partialWindow);
                Check(request.CaptureForVerification()==requestBeforeProposal && !request.DirtyForVerification && !partialWindow.IsVisible,
                    "Partial transfer and manual completion leave the saved request untouched and window unshown",checks);
            }
            finally { partialWindow.Close(); }
            var partialConflict=new ALinePanelPreviewWindow(owner,"ön bel yayı 36 cm","bel çevresi 72 cm");
            partialConflict.ConfirmDiscardWorkForVerification=()=>true;
            try
            {
                partialConflict.UnitForVerification.SelectedIndex=0;partialConflict.FieldsForVerification[1].Text="37";
                Check(!partialConflict.ProposalAgreementForVerification.IsEnabled && partialConflict.ProposalForVerification.Text.Contains("toplam beden ölçüsüyle çelişiyor"),
                    "A preserved manual arc conflicting with the source total blocks partial transfer",checks);
                Click(partialConflict.ProposalApplyForVerification);
                Check(partialConflict.FieldsForVerification[0].Text=="" && partialConflict.FieldsForVerification[1].Text=="37",
                    "Rejected partial transfer preserves both empty and conflicting typed fields",checks);
            }
            finally { partialConflict.Close(); }
            foreach(var unsafeNotes in new[]{sourceText+";ön bel yayı 37 cm",sourceText+";ön bel yayı 36 cm?",sourceText+";eğer kumaş ipekse",
                sourceText+";bu ölçüler geçerli değil",sourceText+";önceki ölçüleri kullanmayın",
                sourceText+";ölçüler doğru değilmiş",sourceText+";ölçüleri kullanmayacağım",
                sourceText+";ölçüler yanlışmış",sourceText+";ölçüleri dikkate almayacağım",sourceText+";ölçüler değiştirilecek",
                sourceText+";bunları kullanmayın",sourceText+";bunlar geçersiz",sourceText+";hepsini kullanmayın",
                sourceText.Replace("Ön bel yayı bolluğu 2 cm","Ön bel yayı bolluğu 1,000 mm"),
                sourceText.Replace("Ön bel yayı bolluğu 2 cm","Ön bel yayı bolluğu 1.000 mm")})
            {
                var refusedWindow=new ALinePanelPreviewWindow(owner,"A kesim elbise",unsafeNotes);
            refusedWindow.ConfirmDiscardWorkForVerification=()=>true;
                try
                {
                    Check(!refusedWindow.ProposalAgreementForVerification.IsEnabled && !refusedWindow.ProposalApplyForVerification.IsEnabled,
                        "Conflicting, questioned, conditional, withdrawn or numeric-ambiguous section source cannot offer a proposal",checks);
                    Fill(refusedWindow,true);refusedWindow.UnitForVerification.SelectedIndex=0;refusedWindow.AgreementForVerification.IsChecked=true;
                    Click(refusedWindow.ReviewForVerification);refusedWindow.ConfirmationForVerification.IsChecked=true;Click(refusedWindow.DrawForVerification);
                    Check(refusedWindow.CanvasForVerification.Children.Count==0,"Manual fields cannot bypass an ambiguous section source",checks);
                }
                finally { refusedWindow.Close(); }
            }
            var designOnlyWindow=new ALinePanelPreviewWindow(owner,"Kumaş doğru değilmiş; önceki modeli kullanmayacağım",sourceText);
            designOnlyWindow.ConfirmDiscardWorkForVerification=()=>true;
            try
            {
                designOnlyWindow.UnitForVerification.SelectedIndex=1;
                Check(designOnlyWindow.ProposalAgreementForVerification.IsEnabled,"Design-only negative inflections do not withdraw explicitly stated measurements",checks);
                designOnlyWindow.ProposalAgreementForVerification.IsChecked=true;Click(designOnlyWindow.ProposalApplyForVerification);
                designOnlyWindow.AgreementForVerification.IsChecked=true;ReviewAndDraw(designOnlyWindow);
            }
            finally { designOnlyWindow.Close(); }
            foreach(var (pronounPrompt,pronounNotes) in new[]{("bunlar geçersiz",sourceText),(sourceText,"hepsini kullanmayın"),
                ("ölçüler geçerli",sourceText+";bunları kullanmayın"),(sourceText+";tümünü kullanmayın","ölçüleri kullanmak istiyorum")})
            {
                var referentWindow=new ALinePanelPreviewWindow(owner,pronounPrompt,pronounNotes);
            referentWindow.ConfirmDiscardWorkForVerification=()=>true;
                try
                {
                    Check(!referentWindow.ProposalAgreementForVerification.IsEnabled && referentWindow.ProposalForVerification.Text.Contains("referans zamirinin"),
                        "Pronoun source in either field stays unresolved despite another field's affirmative phrase",checks);
                    Fill(referentWindow,true);referentWindow.UnitForVerification.SelectedIndex=0;referentWindow.AgreementForVerification.IsChecked=true;
                    Click(referentWindow.ReviewForVerification);referentWindow.ConfirmationForVerification.IsChecked=true;Click(referentWindow.DrawForVerification);
                    Check(referentWindow.CanvasForVerification.Children.Count==0 && referentWindow.StatusForVerification.Text.Contains("referans zamirinin"),
                        "Manual entry cannot bypass an unresolved pronoun referring to the source measurements",checks);
                }
                finally { referentWindow.Close(); }
            }
            foreach(var affirmative in new[]{"ölçüler geçerli","ölçüleri kullanmak istiyorum"})
            {
                var affirmativeWindow=new ALinePanelPreviewWindow(owner,affirmative,sourceText);
            affirmativeWindow.ConfirmDiscardWorkForVerification=()=>true;
                try
                {
                    affirmativeWindow.UnitForVerification.SelectedIndex=1;
                    Check(affirmativeWindow.ProposalAgreementForVerification.IsEnabled,"Documented whole-field affirmative phrase permits an explicit section proposal: "+affirmative,checks);
                    affirmativeWindow.ProposalAgreementForVerification.IsChecked=true;Click(affirmativeWindow.ProposalApplyForVerification);
                    affirmativeWindow.AgreementForVerification.IsChecked=true;ReviewAndDraw(affirmativeWindow);
                }
                finally { affirmativeWindow.Close(); }
            }
            Fill(window,true);window.UnitForVerification.SelectedIndex=0;window.AgreementForVerification.IsChecked=true;
            window.FieldsForVerification[4].Text="1,000";Click(window.ReviewForVerification);Click(window.DrawForVerification);
            Check(window.CanvasForVerification.Children.Count==0 && !window.ConfirmationForVerification.IsEnabled && window.StatusForVerification.Text.Contains("üç basamaklı ayırıcı belirsiz"),
                "Manual ambiguous number cannot bypass source proposal rejection or enable drawing",checks);
            window.FieldsForVerification[4].Text="0";ReviewAndDraw(window);
            Check(!owner.IsVisible && !window.IsVisible && !conflict.IsVisible,"All verification windows remain unshown",checks);
            VerifyALineWorkDraft(owner,reportPath,checks);
            VerifyALineEarlyTotals(owner,checks);
            VerifyALineEntry(owner,checks);
            VerifyALineChat(owner,checks);
        }
        catch(Exception ex) { failure=ex.ToString(); }
        finally { request?.Close();conflict?.Close();window?.Close();owner.Close(); }
        var full=Path.GetFullPath(reportPath);Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        // A verification run never replaces an earlier report.
        using var report=new FileStream(full,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        JsonSerializer.Serialize(report,new { status=failure is null ? "PASS" : "FAIL",checks,failure,
            scope="Synthetic in-process WPF event verification, no visible windows or interactive device test." },new JsonSerializerOptions { WriteIndented=true });
        return failure is null ? 0 : 1;
    }
}
