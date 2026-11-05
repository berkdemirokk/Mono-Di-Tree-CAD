using System.Windows;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

internal static partial class DesktopVerification
{
    private static void VerifyRequestCadTarget(MainWindow studio,List<string> checks)
    {
        var source=new ProjectDocument { Name="Sentetik müşteri A",Pieces=[
            new PatternPiece { Points=[new(0,0),new(20,0),new(0,20)] },
            new PatternPiece { Points=[new(30,0),new(50,0),new(30,20)] }] };
        var saved=new DesignRequest("Başka müşteri için özgün istek","bel çevresi 72 cm",null,"aline",2);
        var window=new DesignRequestWindow(studio);
        var pathField=typeof(MainWindow).GetField("filePath",PrivateInstance)!;
        var dirtyField=typeof(MainWindow).GetField("dirty",PrivateInstance)!;
        void Reset()
        {
            studio.LoadDocument(source);studio.Select(source.Pieces[0].Id);
            pathField.SetValue(studio,@"C:\Projects\TekstilCAD\work\sentetik-hedef.tcad");
            dirtyField.SetValue(studio,false);window.LoadForVerification(saved);
        }
        try
        {
            Reset();var before=studio.Document.Clone();var selection=studio.SelectedId;
            window.ConfirmDrawingTargetForVerification=message=>
            {
                Check(message.Contains(source.Name) && message.Contains(@"C:\Projects\TekstilCAD\work\sentetik-hedef.tcad") &&
                    message.Contains("Parça sayısı: 2") && message.Contains("Kaydedilmemiş değişiklik yok") && message.Contains("Yeni proje oluşturulmaz"),
                    "CAD handoff discloses actual project, full file path, piece count and save state without claiming a new project",checks);
                return false;
            };
            Check(window.PrepareDrawingNotesTransition() is null && DocumentSession.ContentEquals(before,studio.Document) &&
                window.CaptureForVerification()==saved && studio.SelectedId==selection,"Cancelled CAD handoff preserves document, selection and request",checks);
            window.ConfirmDrawingTargetForVerification=_=>true;
            var accepted=window.PrepareDrawingNotesTransition();
            Check(accepted is not null && accepted.IsCurrent() && accepted.Notes.DrawingTargetForVerification is { IsReadOnly:true } target &&
                target.Text.Contains(source.Name) && target.Text.Contains("geçiş anına") &&
                DocumentSession.ContentEquals(before,studio.Document),"Approved handoff retains a read-only target snapshot without editing CAD",checks);
            studio.Select(source.Pieces[1].Id);
            Check(!accepted!.IsCurrent(),"Dispatcher-boundary guard rejects a selection changed after approval",checks);accepted.Notes.Close();
            var changes=new (string Name,Action Change)[] {
                ("document content",()=>studio.Document.Name="Başka içerik"),
                ("document replacement",()=>studio.LoadDocument(source)),
                ("selection",()=>studio.Select(source.Pieces[1].Id)),
                ("save state",()=>dirtyField.SetValue(studio,true)),
                ("file destination",()=>pathField.SetValue(studio,@"C:\Projects\TekstilCAD\work\other-synthetic.tcad")),
                ("request",()=>window.PromptForVerification.Text="Yeni kullanıcı değişikliği")
            };
            foreach(var (name,change) in changes)
            {
                Reset();window.ConfirmDrawingTargetForVerification=_=> { change();return true; };
                var result=window.PrepareDrawingNotesTransition();result?.Notes.Close();
                Check(result is null && window.StatusForVerification.Text.Contains("değişti"),"Handoff rejects changed "+name+" during approval",checks);
            }
            Reset();dirtyField.SetValue(studio,true);pathField.SetValue(studio,null);
            window.ConfirmDrawingTargetForVerification=message=>
            {
                Check(message.Contains("Henüz dosyaya kaydedilmedi") && message.Contains("Kaydedilmemiş değişiklikler var"),
                    "Unsaved CAD target is described honestly",checks);
                Check(window.PrepareDrawingNotesTransition() is null,"Reentrant handoff cannot create a second transition",checks);return false;
            };
            Check(window.PrepareDrawingNotesTransition() is null,"Declining unsaved target does not reset the document",checks);
            Check(!window.IsVisible,"Handoff verification does not show the request window",checks);
        }
        finally { window.LoadForVerification(saved);window.Close();studio.LoadDocument(new ProjectDocument()); }
    }
}
