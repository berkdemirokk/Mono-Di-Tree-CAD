using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;
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
            var correctionSource=new DesignRequest("A kesim elbise","Ön bel yayı: 35 cm; başka not",null,"aline",2);
            request.LoadForVerification(correctionSource);
            var correction=ALineSourceCorrection.Create(correctionSource.Prompt,correctionSource.Measurements,0,"36",ALineLengthUnit.Centimeter);
            Check(correction.Place=="Ölçü notları" && correction.OldText=="35 cm" && correction.NewText=="36 cm" && correction.Notes=="Ön bel yayı: 36 cm; başka not", "Correction plan changes only the exact explicit value span",checks);
            Check(!request.CorrectALineSource(correctionSource.Prompt,correctionSource.Measurements,correction,()=>false,()=>true) && request.CaptureForVerification()==correctionSource,"Declining correction keeps source unchanged",checks);
            bool nested=false;
            Check(request.CorrectALineSource(correctionSource.Prompt,correctionSource.Measurements,correction,()=>{ nested=request.CorrectALineSource(correctionSource.Prompt,correctionSource.Measurements,correction,()=>true,()=>true);return true; },()=>true) && !nested &&
                request.CaptureForVerification()==(correctionSource with { Measurements=correction.Notes }),"Correction applies only once and retains unrelated request fields",checks);
            request.LoadForVerification(correctionSource);
            Check(!request.CorrectALineSource(correctionSource.Prompt,correctionSource.Measurements,correction,()=>true,()=>false) && request.CaptureForVerification()==correctionSource,"Changed A-line form prevents source update",checks);
            Check(!request.CorrectALineSource(correctionSource.Prompt,correctionSource.Measurements,correction,()=>{ request.MeasurementsForVerification.Text+=" değişti";return true; },()=>true),"Changed request during correction confirmation is never overwritten",checks);
            foreach(var ambiguous in new[]{"Ön bel yayı: 35 cm; Ön bel yayı: 35 cm","Ön bel yayı: 35 cm?","Ön bel yayı: 35 cm ise"})
            {
                bool rejected=false;try { _=ALineSourceCorrection.Create("",ambiguous,0,"36",ALineLengthUnit.Centimeter); } catch(ArgumentException) { rejected=true; }
                Check(rejected,"Ambiguous or repeated source is never rewritten: "+ambiguous,checks);
            }
            var sourcePrompt="Önceki tasarım\r\nÖn bel yayı: 35.25 cm; kumaş notu\r\nSon satır";
            var sourceNotes="Korunacak not";
            var millimeterCorrection=ALineSourceCorrection.Create(sourcePrompt,sourceNotes,0,"352.5001",ALineLengthUnit.Millimeter);
            Check(millimeterCorrection.Place=="Tarif" && millimeterCorrection.OldText=="35.25 cm" && millimeterCorrection.NewText=="352.5001 mm" &&
                millimeterCorrection.Prompt=="Önceki tasarım\r\nÖn bel yayı: 352.5001 mm; kumaş notu\r\nSon satır" && millimeterCorrection.Notes==sourceNotes,
                "Prompt correction preserves CRLF and surrounding text while retaining precise mm input",checks);
            var centimeterCorrection=ALineSourceCorrection.Create("Ön bel yayı: 350 mm","diğer",0,"35.01",ALineLengthUnit.Centimeter);
            Check(centimeterCorrection.OldText=="350 mm" && centimeterCorrection.NewText=="35.01 cm" && centimeterCorrection.Prompt=="Ön bel yayı: 35.01 cm" && centimeterCorrection.Notes=="diğer",
                "Correction retains explicit decimal centimeter input when the source used millimeters",checks);
            string[] formTransfer=["35","37","50","50","0","0","0","0","20","70","160"];
            var transferSource=new DesignRequest("A kesim elbise","bel çevresi 72 cm; ön bel yayı 350 mm",new DesignReference("x.png","image/png",[137,80,78,71,13,10,26,10]),"aline",2);
            var formPlan=ALineFormToRequest.Create(transferSource.Prompt,transferSource.Measurements,formTransfer,ALineLengthUnit.Centimeter);
            Check(formPlan.Added.Count==10 && formPlan.Preserved.Contains("Ön bel yayı") && formPlan.Missing.Count==0 && formPlan.Notes.StartsWith(transferSource.Measurements+"\n",StringComparison.Ordinal) && formPlan.Notes.Contains("Ön bel yayı bolluğu: 0 cm"),
                "Form transfer plans only missing values, preserving equivalent mm source and explicit zero",checks);
            var partialValues=new string[11];Array.Fill(partialValues,"");partialValues[1]="370";partialValues[4]="0";
            var partialPlan=ALineFormToRequest.Create(transferSource.Prompt,transferSource.Measurements,partialValues,ALineLengthUnit.Millimeter);
            Check(partialPlan.Added.Count==2 && partialPlan.Missing.Count==8 && partialPlan.Notes.Contains("Arka bel yayı: 370 mm"),"Partial transfer keeps missing fields missing and preserves selected unit",checks);
            foreach(var badNotes in new[]{"bel çevresi 80 cm", "bel çevresi 72 cm; ölçüler geçersiz", "ön bel yayı: 36 cm"})
            {
                bool rejected=false;try { _=ALineFormToRequest.Create("A kesim elbise",badNotes,formTransfer,ALineLengthUnit.Centimeter); }catch(ArgumentException) { rejected=true; }
                Check(rejected,"Conflicting or conditional form transfer never rewrites source: "+badNotes,checks);
            }
            request.LoadForVerification(transferSource);
            Check(!request.AppendALineForm(transferSource.Prompt,transferSource.Measurements,formPlan,()=>false,()=>true) && request.CaptureForVerification().Measurements==transferSource.Measurements,"Declining form transfer keeps request",checks);
            Check(!request.AppendALineForm(transferSource.Prompt,transferSource.Measurements,formPlan,()=>true,()=>false) && request.CaptureForVerification().Measurements==transferSource.Measurements,"Changed form rejects transfer",checks);
            bool nestedTransfer=false;
            Check(request.AppendALineForm(transferSource.Prompt,transferSource.Measurements,formPlan,()=> { nestedTransfer=request.AppendALineForm(transferSource.Prompt,transferSource.Measurements,formPlan,()=>true,()=>true);return true; },()=>true) && !nestedTransfer,"Confirmed form transfer is nonreentrant",checks);
            var transferred=request.CaptureForVerification();
            Check(transferred.Prompt==transferSource.Prompt && transferred.ModelId==transferSource.ModelId && transferred.Version==2 && transferred.Reference!.Data.SequenceEqual(transferSource.Reference!.Data) && transferred.Measurements==formPlan.Notes && request.DirtyForVerification,
                "Form transfer preserves model, prompt and image bytes and marks request dirty",checks);
            var reopened=DesignRequestStorage.Deserialize(DesignRequestStorage.SerializeV2(transferred,_=>{}),_=>{});
            Check(reopened.Measurements==formPlan.Notes && reopened.ModelId=="aline" && reopened.Reference!.Data.SequenceEqual(transferSource.Reference.Data),"V2 form values survive portable serialization",checks);
            var repeat=ALineFormToRequest.Create(transferred.Prompt,transferred.Measurements,formTransfer,ALineLengthUnit.Centimeter);
            Check(repeat.Added.Count==0 && !request.AppendALineForm(transferred.Prompt,transferred.Measurements,repeat,()=>throw new Exception("No confirmation for no-op"),()=>true),"Repeating equivalent transfer is a no-op",checks);
            var v1=new DesignRequest("A kesim elbise","",null);var v1Plan=ALineFormToRequest.Create(v1.Prompt,v1.Measurements,partialValues,ALineLengthUnit.Millimeter);
            var v1Reopened=DesignRequestStorage.Deserialize(DesignRequestStorage.Serialize(v1 with { Measurements=v1Plan.Notes }));
            Check(v1Reopened.Version==1 && v1Reopened.ModelId is null && v1Reopened.Measurements==v1Plan.Notes,"V1 retains explicit form measurements without model conversion",checks);
            request.LoadForVerification(transferSource);
            Check(!request.AppendALineForm(transferSource.Prompt,transferSource.Measurements,formPlan,()=> { request.MeasurementsForVerification.Text+=" değişti";return true; },()=>true),"Changing source during transfer confirmation cannot be overwritten",checks);
            request.LoadForVerification(transferSource);_=new WindowInteropHelper(request).EnsureHandle();
            var transferWindow=new ALinePanelPreviewWindow(request,transferSource.Prompt,transferSource.Measurements) { ConfirmDiscardWorkForVerification=()=>true,ConfirmFormToRequestForVerification=()=>true };
            try
            {
                transferWindow.UnitForVerification.SelectedIndex=0;for(int i=0;i<11;i++)transferWindow.FieldsForVerification[i].Text=formTransfer[i];
                transferWindow.AgreementForVerification.IsChecked=true;transferWindow.ReviewForVerification.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                transferWindow.ConfirmationForVerification.IsChecked=true;transferWindow.DrawForVerification.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(transferWindow.CanvasForVerification.Children.Count>0,"Form transfer fixture starts with confirmed preview",checks);
                transferWindow.FormToRequestForVerification.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(transferWindow.FieldsForVerification.Select(f=>f.Text).SequenceEqual(formTransfer) && transferWindow.AgreementForVerification.IsChecked!=true && !transferWindow.ConfirmationForVerification.IsEnabled && transferWindow.CanvasForVerification.Children.Count==0 && request.CaptureForVerification().Measurements==formPlan.Notes,
                    "Applying form values preserves fields and invalidates symmetry, review and preview",checks);
            }
            finally { transferWindow.Close(); }
            var closedRequest=new DesignRequestWindow(owner);closedRequest.LoadForVerification(transferSource);closedRequest.Close();
            Check(!closedRequest.AppendALineForm(transferSource.Prompt,transferSource.Measurements,formPlan,()=>throw new Exception("Closed owner must not ask"),()=>true),"Closed owner cannot accept a form transfer",checks);
            window=new ALinePanelPreviewWindow(owner,"A kesim elbise","bel çevresi 72 cm; kalça çevresi 100 cm");
            window.ConfirmDiscardWorkForVerification=()=>true;
            window.ConfirmUnitConversionForVerification=()=>true;
            Check(window.KnownTotalsForVerification.Text.Contains("72 cm") && window.KnownTotalsForVerification.Text.Contains("100 cm") &&
                window.KnownTotalsForVerification.Text.Contains("ön ve arka") && window.FieldsForVerification.All(f=>f.Text.Length==0),
                "Existing waist and hip totals are shown initially without inventing or filling front/back arcs",checks);
            Check(window.WaistComparisonForVerification.Text.Contains("eksik"),"Partial waist comparison begins unresolved",checks);
            window.UnitForVerification.SelectedIndex=0;window.FieldsForVerification[0].Text="35";window.FieldsForVerification[1].Text="37";
            Check(window.WaistComparisonForVerification.Text.Contains("720 mm") && window.WaistComparisonForVerification.Text.Contains("eşit") && window.WaistComparisonForVerification.Text.Contains("üretim onayı değildir"),
                "Explicit asymmetric waist arcs compare to request before other fields are supplied",checks);
            window.FieldsForVerification[1].Text="38";
            Check(window.WaistComparisonForVerification.Text.Contains("farklı") && window.WaistComparisonForVerification.Text.Contains("730 mm"),"Edited waist immediately invalidates the earlier equality",checks);
            window.FieldsForVerification[1].Clear();window.FieldsForVerification[0].Clear();window.UnitForVerification.SelectedIndex=-1;
            Check(window.WaistComparisonForVerification.Text.Contains("eksik"),"Clearing inputs removes previous waist comparison",checks);
            Check(ALineWaistSourceComparison.Describe("","bel çevresi 72 cm?","350","370",ALineLengthUnit.Millimeter).Contains("belirsiz") &&
                ALineWaistSourceComparison.Describe("","bel çevresi 72 cm; bel çevresi 80 cm","350","370",ALineLengthUnit.Millimeter).Contains("belirsiz") &&
                ALineWaistSourceComparison.Describe("","bel çevresi 72 cm ise","350","370",ALineLengthUnit.Millimeter).Contains("belirsiz") &&
                ALineWaistSourceComparison.Describe("","bel çevresi 72 cm","350","370",ALineLengthUnit.Millimeter).Contains("eşit"),
                "Waist comparison rejects question and conflicting sources and normalizes explicit units",checks);
            foreach(var statement in new[]{"bel çevresi 80 cm; ölçüler geçersiz","bel çevresi 80 cm; bunlar geçerli mi?","bel çevresi 80 cm?"})
                Check(!ALineWaistSourceComparison.Describe("",statement,"35","45",ALineLengthUnit.Centimeter).Contains("sayılar eşit"),"Ambiguous or withdrawn source cannot yield early waist equality: "+statement,checks);
            Check(ALineWaistSourceComparison.Describe("","bel çevresi 80 cm","35","45",ALineLengthUnit.Centimeter).Contains("sayılar eşit"),"Explicit valid source still permits a numeric comparison",checks);
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
            var geometryFeedback=new ALinePanelPreviewWindow(owner,"A kesim elbise","");
            geometryFeedback.ConfirmDiscardWorkForVerification=()=>true;
            try
            {
                geometryFeedback.UnitForVerification.SelectedIndex=1;geometryFeedback.AgreementForVerification.IsChecked=true;
                void RejectGeometry(int field,string value,string expected)
                {
                    Fill(geometryFeedback,false);geometryFeedback.FieldsForVerification[field].Text=value;
                    var before=geometryFeedback.FieldsForVerification.Select(f=>f.Text).ToArray();
                    Click(geometryFeedback.ReviewForVerification);
                    Check(!geometryFeedback.ConfirmationForVerification.IsEnabled && geometryFeedback.StatusForVerification.Text.Contains(expected) && before.SequenceEqual(geometryFeedback.FieldsForVerification.Select(f=>f.Text)),"Invalid geometry blocks review, identifies "+expected+" and preserves input",checks);
                }
                RejectGeometry(4,"200","Ön beden");RejectGeometry(5,"200","Arka beden");
                RejectGeometry(10,"1040","Ön beden");RejectGeometry(8,"700","düşüklük");
                Fill(geometryFeedback,false);geometryFeedback.FieldsForVerification[3].Text="600";geometryFeedback.FieldsForVerification[10].Text="1200";
                Click(geometryFeedback.ReviewForVerification);
                Check(!geometryFeedback.ConfirmationForVerification.IsEnabled && geometryFeedback.StatusForVerification.Text.Contains("Arka beden"),"Back hem failure identifies the back",checks);
            }
            finally { geometryFeedback.Close(); }
            var balanceSource=new DesignRequest("Serbest tarif","",null,"aline",2);request.LoadForVerification(balanceSource);
            var sourceBalance=new ALinePanelPreviewWindow(request,balanceSource.Prompt,balanceSource.Measurements) { ConfirmDiscardWorkForVerification=()=>true };
            try
            {
                sourceBalance.UnitForVerification.SelectedIndex=1;Fill(sourceBalance,false);sourceBalance.AgreementForVerification.IsChecked=true;
                ReviewAndDraw(sourceBalance);Click(sourceBalance.BalanceExploreForVerification);
                sourceBalance.ConfirmBalanceForVerification=()=> { var saved=request.PromptForVerification.Text;request.PromptForVerification.Text=saved+" değişiklik";request.PromptForVerification.Text=saved;return true; };
                Click(sourceBalance.BalanceAcceptForVerification);
                Check(sourceBalance.BalanceCanvasForVerification.Visibility==Visibility.Collapsed && sourceBalance.BalanceTextForVerification.Text.Contains("değişti") && request.CaptureForVerification()==balanceSource,
                    "Source change and restoration during candidate approval rejects the stale candidate",checks);
                ReviewAndDraw(sourceBalance);Click(sourceBalance.BalanceExploreForVerification);sourceBalance.ConfirmBalanceForVerification=()=>true;Click(sourceBalance.BalanceAcceptForVerification);
                Check(sourceBalance.BalanceCanvasForVerification.Visibility==Visibility.Visible,"Approved candidate becomes visible",checks);
                request.PromptForVerification.Text+=" değişti";
                Check(sourceBalance.BalanceCanvasForVerification.Visibility==Visibility.Collapsed && sourceBalance.BalanceCanvasForVerification.Children.Count==0 &&
                    !sourceBalance.BalanceAcceptForVerification.IsEnabled && sourceBalance.BalanceTextForVerification.Text.Contains("kaldırıldı"),
                    "Changing request removes a previously visible candidate",checks);
            }
            finally { sourceBalance.Close();request.LoadForVerification(transferSource); }
            var sideDisplay=new ALinePanelPreviewWindow(owner,"A kesim elbise","");
            sideDisplay.ConfirmDiscardWorkForVerification=()=>true;
            try
            {
                sideDisplay.UnitForVerification.SelectedIndex=1;Fill(sideDisplay,false);sideDisplay.AgreementForVerification.IsChecked=true;
                sideDisplay.FieldsForVerification[0].Text="360.0000001";Click(sideDisplay.ReviewForVerification);
                Check(sideDisplay.SummaryForVerification.Text.Contains("Ön bel yayı: 360.0000001 mm = 36.00000001 cm") &&
                    sideDisplay.SummaryForVerification.Text.Contains("720.0000001 mm = 72.00000001 cm") &&
                    sideDisplay.SummaryForVerification.Text.Contains("geometri hesabında bilgisayar sayıları yaklaşık"),
                    "Measurement review preserves entered decimal precision and exact decimal totals",checks);
                sideDisplay.FieldsForVerification[0].Text="360";
                ReviewAndDraw(sideDisplay);
                Check(sideDisplay.StatusForVerification.Text.Contains("hesaplanan fark 0 mm") && sideDisplay.StatusForVerification.Text.Contains("yuvarlanır") && sideDisplay.StatusForVerification.Text.Contains("Dikiş eşleşmesi onaylanmadı"),"Equal side lengths report computed zero and rounding limits",checks);
                sideDisplay.FieldsForVerification[3].Text="500.0010";ReviewAndDraw(sideDisplay);
                Check(sideDisplay.StatusForVerification.Text.Contains("fark sıfırdan büyük") && sideDisplay.StatusForVerification.Text.Contains("yuvarlanınca"),"Small nonzero side difference is not presented as equality",checks);
                var originalCulture=System.Globalization.CultureInfo.CurrentCulture;
                try
                {
                    foreach(var name in new[]{"tr-TR","en-US"})
                    {
                        System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo(name);
                        ReviewAndDraw(sideDisplay);
                        string zero=0d.ToString("F3");
                        Check(sideDisplay.StatusForVerification.Text.Contains($"yuvarlanınca {zero} mm") && sideDisplay.StatusForVerification.Text.Contains("fark sıfırdan büyük"),"Small difference uses formatted zero for "+name,checks);
                    }
                }
                finally { System.Globalization.CultureInfo.CurrentCulture=originalCulture; }
                Check(sideDisplay.StatusForVerification.Text.Contains("Bel–kalça yan aralığı") && sideDisplay.StatusForVerification.Text.Contains("Kalça–etek yan aralığı") && sideDisplay.StatusForVerification.Text.Contains("ara noktalar uyuşmayabilir"),"Separate side segments disclose hip-point mismatch and do not certify stitching",checks);
                Click(sideDisplay.BalanceExploreForVerification);
                Check(sideDisplay.BalanceAcceptForVerification.IsEnabled && sideDisplay.BalanceTextForVerification.Text.Contains("Ön bel bolluğu") &&
                    sideDisplay.BalanceTextForVerification.Text.Contains("Arka kalça bolluğu") && sideDisplay.BalanceTextForVerification.Text.Contains("yaklaşık") &&
                    sideDisplay.BalanceTextForVerification.Text.Contains("üretim") && sideDisplay.BalanceCanvasForVerification.Visibility==Visibility.Collapsed,
                    "Balanced candidate shows all ease changes and limitations before explicit approval",checks);
                var originalForm=sideDisplay.FieldsForVerification.Select(f=>f.Text).ToArray();
                sideDisplay.ConfirmBalanceForVerification=()=>false;Click(sideDisplay.BalanceAcceptForVerification);
                Check(sideDisplay.BalanceCanvasForVerification.Visibility==Visibility.Collapsed && originalForm.SequenceEqual(sideDisplay.FieldsForVerification.Select(f=>f.Text)),
                    "Declining candidate preserves the form and does not display candidate",checks);
                sideDisplay.ConfirmBalanceForVerification=()=> { sideDisplay.FieldsForVerification[4].Text="21";return true; };Click(sideDisplay.BalanceAcceptForVerification);
                Check(sideDisplay.BalanceCanvasForVerification.Visibility==Visibility.Collapsed && sideDisplay.FieldsForVerification[4].Text=="21" &&
                    sideDisplay.BalanceTextForVerification.Text.Contains("değişti"),"Form change during candidate approval prevents stale display",checks);
                Fill(sideDisplay,false);sideDisplay.FieldsForVerification[0].Text="300";sideDisplay.FieldsForVerification[1].Text="500";
                sideDisplay.FieldsForVerification[4].Text="0";sideDisplay.FieldsForVerification[5].Text="0";ReviewAndDraw(sideDisplay);Click(sideDisplay.BalanceExploreForVerification);
                Check(!sideDisplay.BalanceAcceptForVerification.IsEnabled && sideDisplay.BalanceTextForVerification.Text.Contains("Negatif"),
                    "Candidate is unavailable when nonnegative ease cannot balance the arcs",checks);
                Fill(sideDisplay,false);ReviewAndDraw(sideDisplay);Click(sideDisplay.BalanceExploreForVerification);sideDisplay.ConfirmBalanceForVerification=()=>true;
                var beforeCandidate=sideDisplay.FieldsForVerification.Select(f=>f.Text).ToArray();Click(sideDisplay.BalanceAcceptForVerification);
                Check(sideDisplay.BalanceCanvasForVerification.Visibility==Visibility.Visible && sideDisplay.BalanceCanvasForVerification.Children.OfType<Polygon>().Count()==2 &&
                    beforeCandidate.SequenceEqual(sideDisplay.FieldsForVerification.Select(f=>f.Text)) && sideDisplay.BalanceTextForVerification.Text.Contains("üretim doğrulanmadı"),
                    "Explicit candidate approval opens only a separate experimental view and preserves the form",checks);
                var candidateText=sideDisplay.BalanceTextForVerification.Text;var candidateCount=sideDisplay.BalanceCanvasForVerification.Children.Count;
                Click(sideDisplay.BalanceAcceptForVerification);
                Check(!sideDisplay.BalanceAcceptForVerification.IsEnabled && sideDisplay.BalanceTextForVerification.Text==candidateText && sideDisplay.BalanceCanvasForVerification.Children.Count==candidateCount,
                    "Repeated candidate acceptance neither duplicates geometry nor appends status",checks);
                sideDisplay.FieldsForVerification[3].Text="500.1";ReviewAndDraw(sideDisplay);
                Check(sideDisplay.StatusForVerification.Text.Contains("fark yaklaşık") && !sideDisplay.StatusForVerification.Text.Contains("fark sıfırdan büyük"),"Difference above displayed rounding threshold is shown as approximate",checks);
            }
            finally { sideDisplay.Close(); }
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
            window.UnitForVerification.SelectedIndex=1;Check(Cleared(window) && window.UnitForVerification.SelectedIndex==1 && window.FieldsForVerification[0].Text=="360","Approved conversion changes units without reinterpretation and invalidates confirmation",checks);
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
            var preciseTotal=new ALinePanelPreviewWindow(owner,"A kesim elbise","bel çevresi 72 cm; kalça çevresi 100 cm") { ConfirmDiscardWorkForVerification=()=>true };
            try {
                Fill(preciseTotal,false);preciseTotal.UnitForVerification.SelectedIndex=1;preciseTotal.AgreementForVerification.IsChecked=true;
                preciseTotal.FieldsForVerification[0].Text="360.0000001";Click(preciseTotal.ReviewForVerification);
                Check(!preciseTotal.ConfirmationForVerification.IsEnabled && preciseTotal.StatusForVerification.Text.Contains("kayıtlı istek ölçüsünden farklı"),
                    "A sub-micrometer decimal mismatch against a recorded body total blocks review",checks);
            } finally { preciseTotal.Close(); }
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
                proposalWindow.FieldsForVerification[0].Text="360.0000001";Click(proposalWindow.ReviewForVerification);
                Check(!proposalWindow.ConfirmationForVerification.IsEnabled && proposalWindow.StatusForVerification.Text.Contains("açık istek ölçüsüyle çelişiyor"),
                    "A sub-micrometer decimal mismatch against an explicit source field blocks review",checks);
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
            partialWindow.ConfirmUnitConversionForVerification=()=>true;
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
                Check(partialWindow.ProposalAgreementForVerification.IsChecked!=true && !partialWindow.ProposalApplyForVerification.IsEnabled && partialWindow.FieldsForVerification[1].Text=="36",
                    "Changing target units invalidates transfer approval while converting the typed value",checks);
                partialWindow.UnitForVerification.SelectedIndex=1;partialWindow.ProposalAgreementForVerification.IsChecked=true;Click(partialWindow.ProposalApplyForVerification);
                Check(partialWindow.FieldsForVerification[0].Text=="360" && partialWindow.FieldsForVerification[3].Text=="500" && partialWindow.FieldsForVerification[4].Text=="0.1250" &&
                    partialWindow.FieldsForVerification[1].Text=="360" && partialWindow.FieldsForVerification[8].Text=="200" && partialWindow.FieldsForVerification[5].Text=="",
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
            foreach(var field in window.FieldsForVerification)field.Clear();window.UnitForVerification.SelectedIndex=0;Fill(window,true);window.AgreementForVerification.IsChecked=true;
            window.FieldsForVerification[4].Text="1,000";Click(window.ReviewForVerification);Click(window.DrawForVerification);
            Check(window.CanvasForVerification.Children.Count==0 && !window.ConfirmationForVerification.IsEnabled && window.StatusForVerification.Text.Contains("üç basamaklı ayırıcı belirsiz"),
                "Manual ambiguous number cannot bypass source proposal rejection or enable drawing",checks);
            window.FieldsForVerification[4].Text="0";ReviewAndDraw(window);
            var unitWindow=new ALinePanelPreviewWindow(owner,"","") { ConfirmDiscardWorkForVerification=()=>true };
            try
            {
                unitWindow.UnitForVerification.SelectedIndex=0;unitWindow.FieldsForVerification[0].Text="0.1250";unitWindow.FieldsForVerification[4].Text="0";
                unitWindow.ConfirmUnitConversionForVerification=()=>false;unitWindow.UnitForVerification.SelectedIndex=1;
                Check(unitWindow.UnitForVerification.SelectedIndex==0 && unitWindow.FieldsForVerification[0].Text=="0.1250" && unitWindow.FieldsForVerification[4].Text=="0", "Declined unit conversion preserves partial fields and zero ease",checks);
                unitWindow.ConfirmUnitConversionForVerification=()=>true;unitWindow.UnitForVerification.SelectedIndex=1;
                Check(unitWindow.UnitForVerification.SelectedIndex==1 && unitWindow.FieldsForVerification[0].Text=="1.25" && unitWindow.FieldsForVerification[1].Text=="" && unitWindow.FieldsForVerification[4].Text=="0", "Approved partial conversion retains empty and zero values",checks);
                unitWindow.UnitForVerification.SelectedIndex=0;
                Check(unitWindow.FieldsForVerification[0].Text=="0.1250", "Fractional conversion round-trips without ambiguous grouping",checks);
                unitWindow.FieldsForVerification[2].Text="geçersiz";var unchanged=unitWindow.FieldsForVerification.Select(f=>f.Text).ToArray();unitWindow.UnitForVerification.SelectedIndex=1;
                Check(unitWindow.UnitForVerification.SelectedIndex==0 && unchanged.SequenceEqual(unitWindow.FieldsForVerification.Select(f=>f.Text)), "Invalid value rejects the whole conversion without mutation",checks);
                unitWindow.FieldsForVerification[2].Clear();unitWindow.ConfirmUnitConversionForVerification=()=> { unitWindow.FieldsForVerification[1].Text="10";return true; };
                unitWindow.UnitForVerification.SelectedIndex=1;
                Check(unitWindow.UnitForVerification.SelectedIndex==0 && unitWindow.FieldsForVerification[0].Text=="0.1250" && unitWindow.FieldsForVerification[1].Text=="10", "Edit during conversion approval aborts without overwriting the newer edit",checks);
                unitWindow.ConfirmUnitConversionForVerification=()=> { unitWindow.UnitForVerification.SelectedIndex=1;return true; };
                unitWindow.UnitForVerification.SelectedIndex=1;
                Check(unitWindow.UnitForVerification.SelectedIndex==0 && unitWindow.FieldsForVerification[0].Text=="0.1250", "Reentrant unit selection aborts the outer conversion",checks);
            }
            finally { unitWindow.Close(); }
            Check(!owner.IsVisible && !window.IsVisible && !conflict.IsVisible,"All verification windows remain unshown",checks);
            VerifyALineWorkDraft(owner,reportPath,checks);
            VerifyALineEarlyTotals(owner,checks);
            VerifyALineEntry(owner,checks);
            VerifyALineTransferOrder(owner,checks);
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
