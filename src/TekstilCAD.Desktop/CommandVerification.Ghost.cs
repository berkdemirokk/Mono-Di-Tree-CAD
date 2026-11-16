using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TekstilCAD.Core;
using TekstilCAD.Core.Commands;
using static TekstilCAD.Desktop.DesktopVerification;

namespace TekstilCAD.Desktop;

// PR 10: the dashed preview frame on the canvas. It is only drawn: the document, the selection and the undo stack never change.
internal static partial class CommandVerification
{
    private static byte[] Pixels(FrameworkElement element)
    {
        var w=Math.Max(1,(int)Math.Ceiling(element.ActualWidth));var h=Math.Max(1,(int)Math.Ceiling(element.ActualHeight));
        var bitmap=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);bitmap.Render(element);
        var bytes=new byte[w*h*4];bitmap.CopyPixels(bytes,w*4,0);return bytes;
    }

    private static string StatusOf(MainWindow window)=>Field<TextBlock>(window,"status").Text;

    private static void VerifyGhost(List<string> checks,string? reportPath)
    {
        windowsStable=true;
        string RenderPath(string suffix)=>reportPath is null ? Path.Combine(Path.GetTempPath(),"ghost-"+suffix) : Path.ChangeExtension(reportPath,suffix);
        Scenario(window=>
        {
            var piece=new PatternPiece { Name="Düz",Points=[new(20,20),new(120,20),new(120,100),new(20,100)] };
            window.LoadDocument(new ProjectDocument { Pieces=[piece] });window.Select(piece.Id);
            Render(window,RenderPath(".simple-view-ghost-before-1440x900.png"),1440,900);
            var canvas=Field<CadCanvas>(window,"canvas");var session=SessionOf(window);
            var before=window.Document.Clone();var plain=Pixels(canvas);
            var view=NewBox(window);
            Type(view,"genişliği 95 yap");Enter(view);
            Render(window,RenderPath(".simple-view-ghost-1440x900.png"),1440,900);
            var shown=canvas.Ghost;var framed=Pixels(canvas);
            Check(view.State==CommandBarState.Preview && shown is { IsEmpty:false } && shown.Polygons.Count>0 && shown.Labels.Any(label=>label.Text.Contains("95"))
                && DocumentSession.ContentEquals(before,window.Document) && session.UndoCount==0 && window.SelectedId==piece.Id && !plain.SequenceEqual(framed),
                "Ghost preview draws without changing the document or the undo stack",checks);
            // Cleared by apply.
            Enter(view);
            Check(view.State==CommandBarState.Result && canvas.Ghost is null && session.UndoCount==1,"Ghost preview is cleared after apply",checks);
            // Cleared by Esc and by Vazgeç.
            Type(view,"25 sağa taşı");Enter(view);var shownMove=canvas.Ghost is not null;
            view.HandleKey(Key.Escape,ModifierKeys.None);
            var afterEsc=canvas.Ghost is null && view.State==CommandBarState.Idle;
            Enter(view);var again=canvas.Ghost is not null;
            view.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(shownMove && afterEsc && again && canvas.Ghost is null && session.UndoCount==1 && window.Document.Pieces.Single().Points[0].X==20,
                "Ghost preview is cleared after Esc and cancel",checks);
            // Cleared when the text changes (the old preview is dropped).
            Enter(view);var third=canvas.Ghost is not null;Type(view,"25 sağa taşıx");
            Check(third && canvas.Ghost is null,"Ghost preview is cleared when the text changes",checks);
        });
        // Cleared by a selection change and by closing the box.
        Scenario(window=>
        {
            var a=new PatternPiece { Name="A",Points=[new(20,20),new(120,20),new(120,100),new(20,100)] };
            var b=new PatternPiece { Name="B",Points=[new(200,20),new(300,20),new(300,100),new(200,100)] };
            window.LoadDocument(new ProjectDocument { Pieces=[a,b] });window.Select(a.Id);
            Render(window,RenderPath(".command-box-ghost-selection.png"),1440,900);
            var canvas=Field<CadCanvas>(window,"canvas");var session=SessionOf(window);
            // The window tells its own box (the one Ctrl+K opens) about selection changes.
            var host=new RecordingHost();window.CommandBoxHostForVerification=()=>host;
            window.OpenCommandBox();var view=window.CommandBoxView!;view.AutoPreview=false;
            Type(view,"25 sağa taşı");Enter(view);var shown=canvas.Ghost is not null;
            window.Select(b.Id);
            Check(shown && canvas.Ghost is null && view.State==CommandBarState.Idle && session.UndoCount==0,
                "Ghost preview is cleared after a selection change",checks);
            var real=view;real.Reset();
            real.SetText("sil");real.HandleKey(Key.Enter,ModifierKeys.None);
            var danger=canvas.Ghost is { Style:GhostStyle.Danger };
            window.CloseCommandBox();
            Check(danger && canvas.Ghost is null && session.UndoCount==0 && window.Document.Pieces.Count==2,
                "A delete preview shows a red ghost and closing the box clears it",checks);
        });
        // A frame outside the visible area is explained.
        Scenario(window=>
        {
            var piece=new PatternPiece { Name="Düz",Points=[new(20,20),new(120,20),new(120,100),new(20,100)] };
            window.LoadDocument(new ProjectDocument { Pieces=[piece] });window.Select(piece.Id);
            Render(window,RenderPath(".command-box-ghost-outside.png"),1440,900);
            var canvas=Field<CadCanvas>(window,"canvas");var view=NewBox(window);
            canvas.PanView(2_000_000,0);
            Type(view,"25 sağa taşı");Enter(view);
            Check(canvas.Ghost is not null && StatusOf(window)=="Önizleme görünümün dışında; Tümünü göster düğmesine basın.",
                "Ghost outside the view shows the guidance sentence",checks);
            view.HandleKey(Key.Escape,ModifierKeys.None);
        });
        Check(windowsStable,"Ghost checks open no window and no message box",checks);
    }
}
