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
            Fill(conflict,true);conflict.UnitForVerification.SelectedIndex=0;conflict.AgreementForVerification.IsChecked=true;
            Click(conflict.ReviewForVerification);conflict.ConfirmationForVerification.IsChecked=true;Click(conflict.DrawForVerification);
            Check(conflict.CanvasForVerification.Children.Count==0 && conflict.StatusForVerification.Text.Contains("kayıtlı istek ölçüsünden farklı"),
                "Existing request waist conflict blocks drawing even after field confirmation",checks);
            Check(!owner.IsVisible && !window.IsVisible && !conflict.IsVisible,"All verification windows remain unshown",checks);
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
