using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

internal static partial class DesktopVerification
{
    private static void VerifyGrainlineDirectionDisplay(MainWindow studio,List<string> checks)
    {
        var piece=new PatternPiece { Points=[new(0,0),new(200,0),new(200,200)],
            Grainline=new Grainline { Start=new(80,40),End=new(80,160),NeedsReview=true } };
        var document=new ProjectDocument { Pieces=[piece] };
        studio.LoadDocument(document);studio.Select(piece.Id);
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        canvas.Measure(new Size(600,400));canvas.Arrange(new Rect(0,0,600,400));canvas.Fit();canvas.UpdateLayout();
        var before=studio.Document.Clone();
        byte[] Render()
        {
            canvas.InvalidateVisual();canvas.UpdateLayout();
            var bitmap=new RenderTargetBitmap(600,400,96,96,PixelFormats.Pbgra32);bitmap.Render(canvas);
            var bytes=new byte[600*400*4];bitmap.CopyPixels(bytes,600*4,0);return bytes;
        }
        var forward=Render();
        var peer=FrameworkElementAutomationPeer.CreatePeerForElement(canvas)!;
        Check(peer.GetHelpText().Contains("KONTROL BEKLİYOR") && peer.GetHelpText().Contains("bitiş (80, 160)") &&
            DocumentSession.ContentEquals(before,studio.Document),"Canvas grainline exposes direction and review status accessibly without changing source",checks);
        var reversed=document.Clone();reversed.Pieces[0].Grainline!.Start=new(80,160);reversed.Pieces[0].Grainline!.End=new(80,40);
        studio.LoadDocument(reversed);studio.Select(piece.Id);canvas.Fit();
        var backward=Render();
        Check(!forward.SequenceEqual(backward) && peer.GetHelpText().Contains("bitiş (80, 40)"),
            "Reversed grainline changes hidden WPF rendering and accessible endpoint",checks);
        reversed.Pieces[0].Grainline!.NeedsReview=false;studio.LoadDocument(reversed);studio.Select(piece.Id);canvas.Fit();
        Check(!backward.SequenceEqual(Render()) && !peer.GetHelpText().Contains("KONTROL BEKLİYOR") &&
            peer.GetHelpText().Contains("sayısı: 0"),"Reviewed flag changes rendering and accessible state without granting production approval",checks);
        studio.LoadDocument(new ProjectDocument());
    }
}
