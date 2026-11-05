using System.Windows;
using System.Windows.Controls;

namespace TekstilCAD.Desktop;

internal static partial class DesktopVerification
{
    private static void VerifyALineEarlyTotals(Window owner,List<string> checks)
    {
        foreach(var (notes,label) in new[]{("bel çevresi 80 cm; kalça çevresi 100 cm","bel çevresi"),
            ("bel çevresi 72 cm; kalça çevresi 110 cm","kalça çevresi")})
        foreach(var mm in new[]{false,true})
        {
            var window=new ALinePanelPreviewWindow(owner,"A kesim elbise",notes) { ConfirmDiscardWorkForVerification=()=>true };
            try
            {
                static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                string[] values=mm ? ["360","360","500","500","20","20","20","20","200","700","1600"]
                    : ["36","36","50","50","2","2","2","2","20","70","160"];
                window.UnitForVerification.SelectedIndex=mm ? 1 : 0;
                for(var i=0;i<values.Length;i++)window.FieldsForVerification[i].Text=values[i];
                window.AgreementForVerification.IsChecked=true;Click(window.ReviewForVerification);
                Check(!window.ConfirmationForVerification.IsEnabled && window.ConfirmationForVerification.IsChecked!=true &&
                    window.SummaryForVerification.Text=="" && window.CanvasForVerification.Children.Count==0 &&
                    window.StatusForVerification.Text.Contains(label) && window.StatusForVerification.Text.Contains("farklı") &&
                    window.FieldsForVerification.Select(f=>f.Text).SequenceEqual(values),
                    $"{label} mismatch is rejected before summary or consent, preserving every {(mm ? "mm" : "cm")} input",checks);
                // Attempt to bypass the disabled UI flag: the final calculation
                // boundary must independently recheck the same source totals.
                window.ConfirmationForVerification.IsChecked=true;Click(window.DrawForVerification);
                Check(window.CanvasForVerification.Children.Count==0 && window.StatusForVerification.Text.Contains(label) &&
                    window.StatusForVerification.Text.Contains("farklı"),"Calculation still rejects source total mismatch despite forced confirmation",checks);
                var correction=label=="bel çevresi" ? new[]{0,1} : new[]{2,3};
                foreach(var index in correction)window.FieldsForVerification[index].Text=label=="bel çevresi" ? (mm ? "400" : "40") : (mm ? "550" : "55");
                Click(window.ReviewForVerification);
                Check(window.ConfirmationForVerification.IsEnabled && window.ConfirmationForVerification.IsChecked!=true &&
                    window.SummaryForVerification.Text.Contains("BEDEN") && window.CanvasForVerification.Children.Count==0,
                    "Corrected source-consistent totals permit a fresh summary but never automatic drawing",checks);
                window.ConfirmationForVerification.IsChecked=true;Click(window.DrawForVerification);
                Check(window.CanvasForVerification.Children.Count>0,"Explicitly reconfirmed corrected totals restore experimental preview",checks);
            }
            finally { window.Close(); }
        }
    }
}
