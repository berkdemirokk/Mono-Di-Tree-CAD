using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

internal static partial class DesktopVerification
{
    // Dedicated noninteractive capture: no Show/ShowDialog or CAD workspace.
    public static int RenderDesignRequest(string reportPath)
    {
        Window? owner=null;DesignRequestWindow? request=null;
        var streams=new List<FileStream>();var created=new List<string>();
        try
        {
            reportPath=AllowedReportPath(reportPath);
            var workRoot=Path.Combine(RepositoryRoot(reportPath),"work")+Path.DirectorySeparatorChar;
            if(!reportPath.StartsWith(workRoot,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Capture reports must be under the checkout work directory.");
            var paths=new[] { reportPath,Path.ChangeExtension(reportPath,".request-480x420.png"),Path.ChangeExtension(reportPath,".request-900x700.png") };
            // Reserve every destination before writing anything. CreateNew also closes
            // the race between an existence check and opening each destination.
            foreach(var path in paths)
            {
                ProjectStorage.EnsureWritableDestination(path);
                var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                streams.Add(stream);created.Add(path);
            }
            owner=new Window();
            _=new WindowInteropHelper(owner).EnsureHandle();
            request=new DesignRequestWindow(owner);
            if(owner.IsVisible || request.IsVisible)throw new InvalidOperationException("Capture windows must remain hidden.");
            Capture(request,streams[1],480,420);
            Capture(request,streams[2],900,700);
            if(owner.IsVisible || request.IsVisible)throw new InvalidOperationException("Capture windows became visible.");
            JsonSerializer.Serialize(streams[0],new { status="PASS",scope="Hidden request content at 480x420 and 900x700; no interactive UI verification." });
            foreach(var stream in streams)stream.Dispose();
            streams.Clear();created.Clear();
            return 0;
        }
        catch
        {
            foreach(var stream in streams)stream.Dispose();
            foreach(var path in created)try { File.Delete(path); } catch { }
            return 1;
        }
        finally { request?.Close();owner?.Close(); }
    }

    private static void Capture(Window window,Stream output,int width,int height)
    {
        var content=(FrameworkElement)window.Content;
        content.Measure(new Size(width,height));content.Arrange(new Rect(0,0,width,height));content.UpdateLayout();
        content.Dispatcher.Invoke(()=> { },DispatcherPriority.ContextIdle);content.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
        var backing=new DrawingVisual();
        using(var dc=backing.RenderOpen())dc.DrawRectangle(window.Background,null,new Rect(0,0,width,height));
        bitmap.Render(backing);bitmap.Render(content);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(output);
    }
}
