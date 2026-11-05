using System.Windows;
using System.Windows.Automation.Peers;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

internal static partial class DesktopVerification
{
    private static void VerifyRequestNotesModel(Window owner,List<string> checks)
    {
        foreach(var (id,label) in new[]{("wrap","Anvelop elbise"),("aline","A kesim elbise"),("corset","Balenli korpus"),
            ("sheath","Minimal kılıf elbise"),("straight","Düz elbise"),("shirt","Gömlek elbise")})
        {
            // Deliberately conflicting/free-form text must survive unchanged.
            var request=new DesignRequest("Anvelop elbise; özgün tarif aynen korunacak.","bel çevresi 72 cm",null,id,2);
            var requestWindow=new DesignRequestWindow(owner);
            requestWindow.LoadForVerification(request);
            var window=requestWindow.CreateDrawingNotes() ?? throw new InvalidOperationException("Saved V2 request did not create drawing notes.");
            try
            {
                Check(window.ModelForVerification.Text==$"Seçilen model fikri: {label} ({id})" && window.ModelForVerification.IsReadOnly &&
                    window.DescriptionForVerification.Text==request.Prompt && window.MeasurementsForVerification.Text==request.Measurements,
                    "Drawing notes retain separate V2 model identity and unchanged prose: "+id,checks);
                var peer=FrameworkElementAutomationPeer.CreatePeerForElement(window.ModelForVerification);
                Check(peer is not null && peer.GetName()=="Kaydedilmiş model kimliği" && peer.GetHelpText().Contains("tarif ayrı") &&
                    window.ModelForVerification.Focusable,"Model identity is keyboard accessible with an explicit comparison explanation: "+id,checks);
                foreach(var size in new[]{new Size(360,360),new Size(480,620)})
                {
                    var root=(FrameworkElement)window.Content;root.Measure(size);root.Arrange(new Rect(size));root.UpdateLayout();
                    var model=window.ModelForVerification;var position=model.TransformToAncestor(root).Transform(new Point());
                    Check(model.ActualWidth>0 && model.ActualHeight>0 && position.Y>=0 && position.Y+model.ActualHeight<=size.Height &&
                        window.DescriptionForVerification.ActualHeight>0 && window.MeasurementsForVerification.ActualHeight>0,
                        $"Model identity and original text viewports remain available at {size.Width}x{size.Height}: {id}",checks);
                }
                Check(!window.IsVisible,"Model note verification never shows a window: "+id,checks);
            }
            finally { window.Close();requestWindow.Close(); }
        }
        foreach(var version in new[]{1,2})
        {
            var request=new DesignRequest("A kesim elbise; detaylar: ","",null,null,version);
            var window=new RequestNotesWindow(owner,request);
            try
            {
                Check(window.ModelForVerification.Text.Contains("Ayrı model kimliği kayıtlı değil") &&
                    window.ModelForVerification.Text.Contains("tahmin edilmedi") && window.DescriptionForVerification.Text==request.Prompt,
                    "Identity-free request never infers a model from a matching template: V"+version,checks);
            }
            finally { window.Close(); }
        }
    }
}
