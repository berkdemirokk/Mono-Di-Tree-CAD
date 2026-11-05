using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;

namespace TekstilCAD.Desktop;

internal static partial class DesktopVerification
{
    // Dedicated noninteractive capture: no Show/ShowDialog or CAD workspace.
    public static int RenderDesignRequest(string reportPath)
    {
        Window? owner=null;DesignRequestWindow? request=null;
        try
        {
            reportPath=AllowedReportPath(reportPath);
            owner=new Window();
            _=new WindowInteropHelper(owner).EnsureHandle();
            request=new DesignRequestWindow(owner);
            if(owner.IsVisible || request.IsVisible)throw new InvalidOperationException("Capture windows must remain hidden.");
            Render(request,Path.ChangeExtension(reportPath,".request-480x420.png"),480,420);
            Render(request,Path.ChangeExtension(reportPath,".request-900x700.png"),900,700);
            if(owner.IsVisible || request.IsVisible)throw new InvalidOperationException("Capture windows became visible.");
            File.WriteAllText(reportPath,JsonSerializer.Serialize(new { status="PASS",scope="Hidden request content at 480x420 and 900x700; no interactive UI verification." }));
            return 0;
        }
        catch(Exception ex)
        {
            try { var path=AllowedReportPath(reportPath);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,JsonSerializer.Serialize(new { status="FAIL",failure=ex.ToString() })); } catch { }
            return 1;
        }
        finally { request?.Close();owner?.Close(); }
    }
}
