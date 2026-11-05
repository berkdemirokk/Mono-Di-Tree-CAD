using System.Windows;
using System.Windows.Media.Imaging;

namespace TekstilCAD.Desktop;

internal static partial class DesktopVerification
{
    private static void VerifyPhotoObservationEdits(Window owner,BitmapSource image,List<string> checks)
    {
        const string old="Fotoğraf gözlemi · Kol: Uzun kollu";
        const string next="Fotoğraf gözlemi · Kol: Kısa kollu";
        var original="Serbest uzun kollu tarif\r\nÖn bel yayı: 36 cm\n"+old+"\r\nSon not";
        var changed=PhotoObservationEdit.Plan(original,1,2,false,1000);
        Check(changed.Description==original.Replace(old,next,StringComparison.Ordinal) && changed.Previous==old && changed.Replacement==next,
            "Exact generated observation replacement preserves unrelated prose, measurement and mixed line separators",checks);
        Check(PhotoObservationEdit.Plan(original,1,0,true,1000).Description==original.Replace(old,"",StringComparison.Ordinal),
            "Explicit removal deletes only the observation text, preserving surrounding characters",checks);
        foreach(var invalid in new[]{original+"\n"+old,original+"\nFotoğraf gözlemi · Kol: Kolsuz",original.Replace(old,old+"?",StringComparison.Ordinal),original.Replace(old," "+old,StringComparison.Ordinal)})
        {
            bool rejected=false;try { _=PhotoObservationEdit.Plan(invalid,1,2,false,1000); }catch(ArgumentException) { rejected=true; }
            Check(rejected,"Duplicate or malformed source observation cannot be replaced",checks);
        }
        var current=true;var dialog=new PhotoObservationWindow(owner,image,"A kesim elbise",original,1000,()=>current);
        var closed=false;dialog.Closed+=(_,_)=>closed=true;
        try
        {
            dialog.Sleeve.SelectedIndex=2;dialog.Neck.SelectedIndex=1;dialog.Confirm.IsChecked=true;
            dialog.ConfirmEditForVerification=message=> { Check(message.Contains(old)&&message.Contains(next)&&message.Contains("Yalnız bu"),"Replacement confirmation names exact before/after scope",checks);return false; };
            Check(!dialog.TryEdit(1,false) && dialog.ApprovedDescription is null && dialog.Sleeve.SelectedIndex==2 && dialog.Neck.SelectedIndex==1 && !closed,
                "Declining replacement keeps all selections and source unchanged in the open dialog",checks);
            dialog.ConfirmEditForVerification=_=>true;
            Check(dialog.TryEdit(1,false) && dialog.ApprovedDescription==changed.Description && dialog.ExistingDescription.Text==original && dialog.PhotoForVerification.Source==image,
                "Accepted replacement prepares only its target observation without mutating source or photo",checks);
            dialog.Sleeve.SelectedIndex=0;var asked=false;dialog.ConfirmEditForVerification=_=> { asked=true;return true; };
            Check(!dialog.TryEdit(1,false) && !asked && dialog.ApprovedDescription is null && dialog.Status.Text.Contains("Bilinmiyor"),
                "Unknown choice is never an implicit removal",checks);
            dialog.ConfirmEditForVerification=message=> { Check(message.Contains(old)&&message.Contains("kaldırılacak"),"Removal confirmation explicitly names the deleted observation",checks);return true; };
            Check(dialog.TryEdit(1,true) && dialog.ApprovedDescription==original.Replace(old,"",StringComparison.Ordinal),
                "Dedicated confirmed removal works even when selector is unknown",checks);
            dialog.Sleeve.SelectedIndex=2;
            dialog.ConfirmEditForVerification=_=> { current=false;return true; };
            Check(!dialog.TryEdit(1,false) && dialog.ApprovedDescription is null && !closed,"Source changing during confirmation blocks replacement without closing",checks);
            current=true;dialog.ConfirmEditForVerification=_=> { dialog.Sleeve.SelectedIndex=1;dialog.Sleeve.SelectedIndex=2;return true; };
            Check(!dialog.TryEdit(1,false) && dialog.ApprovedDescription is null,"Selector round trip during confirmation invalidates old plan",checks);
            Check(!dialog.IsVisible && !owner.IsVisible,"Observation edit verification never displays a window",checks);
        }
        finally { dialog.Close(); }
        const string shortSource="Fotoğraf gözlemi · Kol: Kolsuz";
        var limited=new PhotoObservationWindow(owner,image,"A kesim elbise",shortSource,shortSource.Length);
        try
        {
            limited.Sleeve.SelectedIndex=3;limited.Confirm.IsChecked=true;
            var asked=false;limited.ConfirmEditForVerification=_=> { asked=true;return true; };
            Check(!limited.TryEdit(1,false) && !asked && limited.ApprovedDescription is null && limited.Sleeve.SelectedIndex==3 && limited.Status.Text.Contains("Tarif sınırı"),
                "Over-limit replacement fails before confirmation and retains selection",checks);
        }
        finally { limited.Close(); }
    }
}
