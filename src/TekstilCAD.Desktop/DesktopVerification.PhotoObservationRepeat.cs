using System.Windows;
using System.Windows.Media.Imaging;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

internal static partial class DesktopVerification
{
    private static void VerifyRepeatedPhotoObservations(Window owner,BitmapSource bitmap,List<string> checks)
    {
        var first=DesignRequestWindow.AppendPhotoObservations("Özgün tarif",PhotoObservationWindow.Build(1,0,0),1000);
        var both=PhotoObservationWindow.Build(1,3,0);
        var expected=first+"\nFotoğraf gözlemi · Kol: Uzun kollu";
        Check(DesignRequestWindow.AppendPhotoObservations(first,both,1000)==expected,
            "Identical silhouette is skipped while a new sleeve is appended without changing original prose",checks);
        Check(DesignRequestWindow.AppendPhotoObservations(expected,both,1000)==expected,
            "All identical observations are an exact no-op",checks);
        bool atomic=false;
        try { _=DesignRequestWindow.AppendPhotoObservations(expected,PhotoObservationWindow.Build(1,1,1),1000); }
        catch(ArgumentException) { atomic=true; }
        Check(atomic,"A conflicting sleeve rejects the entire batch, including a new neckline",checks);
        var snapshot=new DesignRequest(first,"bel çevresi 72 cm",new DesignReference("sentetik.png","image/png",[1,2]),"aline",2);
        foreach(var changed in new[]{snapshot with { Prompt=expected },snapshot with { Measurements="bel çevresi 80 cm" },snapshot with { ModelId="wrap" }})
            Check(!DesignRequestWindow.PhotoObservationSnapshotMatches(snapshot,changed,1,1,"", "",false,false,false),
                "Observation application rejects a changed description, measurement field or selected model",checks);
        Check(!DesignRequestWindow.PhotoObservationSnapshotMatches(snapshot,snapshot,1,1,"", "bekleyen mesaj",false,false,false),
            "Observation application rejects changed chat without consuming its text",checks);

        void Rejected(string description,int limit,bool current,int silhouette,int sleeve,int neck,string message)
        {
            var dialog=new PhotoObservationWindow(owner,bitmap,"A kesim elbise",description,limit,()=>current);
            var closed=false;dialog.Closed+=(_,_)=>closed=true;
            try
            {
                dialog.Silhouette.SelectedIndex=silhouette;dialog.Sleeve.SelectedIndex=sleeve;dialog.Neck.SelectedIndex=neck;dialog.Confirm.IsChecked=true;
                Check(!dialog.TryApprove() && dialog.Approved is null && !closed && dialog.DialogResult!=true &&
                    dialog.Silhouette.SelectedIndex==silhouette && dialog.Sleeve.SelectedIndex==sleeve && dialog.Neck.SelectedIndex==neck && dialog.Confirm.IsChecked==true &&
                    dialog.ExistingDescription.Text==description && dialog.PhotoForVerification.Source==bitmap && dialog.Status.Text.Contains(message,StringComparison.Ordinal),
                    "Rejected observation keeps all choices, photo and source in the open dialog: "+message,checks);
            }
            finally { dialog.Close(); }
        }
        Rejected(first,1000,true,0,0,0,"En az bir");
        Rejected(first,1000,true,1,0,0,"tamamı tarifte zaten var");
        Rejected(expected,1000,true,1,1,1,"farklı bir fotoğraf gözlemi");
        Rejected(first,first.Length,true,1,3,1,"Tarif sınırı");
        Rejected(first,1000,false,1,3,1,"İstek veya görsel değişti");

        var editable=new PhotoObservationWindow(owner,bitmap,"A kesim elbise",expected,1000);
        try
        {
            editable.Silhouette.SelectedIndex=1;editable.Sleeve.SelectedIndex=1;editable.Neck.SelectedIndex=1;editable.Confirm.IsChecked=true;
            Check(!editable.TryApprove(),"Conflicting manual choice stays available for correction",checks);
            editable.Sleeve.SelectedIndex=3;
            Check(editable.TryApprove() && editable.Approved is { Count:3 } &&
                DesignRequestWindow.AppendPhotoObservations(expected,editable.Approved!,1000)==expected+"\nFotoğraf gözlemi · Yaka: Yuvarlak yaka",
                "Correcting the conflict retains other selections and adds only the new neckline",checks);
        }
        finally { editable.Close(); }
    }
}
