using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TekstilCAD.Desktop;
internal static partial class DesktopVerification
{
    private static void VerifyObservationRegionEntry(Window owner,List<string> checks)
    {
        var bitmap=BitmapSource.Create(64,64,96,96,PixelFormats.Bgra32,null,new byte[64*64*4],64*4);bitmap.Freeze();
        var current=true;var opened=0;
        var dialog=new PhotoObservationWindow(owner,bitmap,"A kesim elbise","Özgün tarif",1000,()=>current);
        try
        {
            _=new WindowInteropHelper(dialog).EnsureHandle();
            dialog.Silhouette.SelectedIndex=1;dialog.Confirm.IsChecked=true;
            var layout=(FrameworkElement)dialog.Content;layout.Measure(new Size(500,450));layout.Arrange(new Rect(0,0,500,450));layout.UpdateLayout();
            Check(FullyVisible(dialog.RegionForVerification,layout) && dialog.RegionForVerification.Focusable && AutomationProperties.GetHelpText(dialog.RegionForVerification).Contains("CAD kalıbı"),
                "Photo observation exposes accessible region review at minimum size",checks);
            dialog.ShowRegionForVerification=region=> { opened++;Check(region.OmitHoles.Parent is null,"Observation opens the region tool in review-only mode",checks);region.Close(); };
            dialog.OpenRegionReview();
            Check(opened==1 && dialog.Silhouette.SelectedIndex==1 && dialog.Confirm.IsChecked==true && dialog.ExistingDescription.Text=="Özgün tarif" && dialog.Approved is null && dialog.Status.Text.Contains("otomatik eklenmedi"),
                "Region review preserves observation choices and does not approve or alter the request",checks);
            current=false;dialog.OpenRegionReview();
            Check(opened==1 && dialog.Status.Text.Contains("değişti"),"A stale photo request cannot open region review",checks);
            current=true;dialog.ShowRegionForVerification=region=> { dialog.Sleeve.SelectedIndex=1;region.Close(); };
            dialog.OpenRegionReview();
            Check(dialog.Sleeve.SelectedIndex==1 && dialog.Status.Text.Contains("yeniden kontrol"),"Changes during region review are retained and flagged",checks);
        }
        finally { dialog.Close(); }
    }
}
