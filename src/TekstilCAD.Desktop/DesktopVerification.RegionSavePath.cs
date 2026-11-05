using System.IO;

namespace TekstilCAD.Desktop;

internal static partial class DesktopVerification
{
    private static void VerifyRegionSavePaths(PhotoRegionWindow window,string checkpoint,List<string> checks)
    {
        bool Save(string path) { var task=window.TrySaveSessionAsync(path);PumpUntil(()=>task.IsCompleted);return task.GetAwaiter().GetResult(); }
        var mask=window.CopyMaskForVerification()!;
        var mark=window.AppliedMarkForVerification(40,30);
        var selection=window.Regions.SelectedIndex;
        var prefix=Path.ChangeExtension(checkpoint,null)+"-names-"+Guid.NewGuid().ToString("N");
        var extensionless=prefix+"-plain";
        var completed=extensionless+".tdregion";
        var upper=prefix+"-upper.TDREGION";
        var wrong=prefix+"-wrong.json";
        var freshWrong=prefix+"-fresh.txt";
        byte[] unrelated=[1,2,3,4];
        var oldConfirmation=window.ConfirmRectangleChangeForVerification;
        try
        {
            File.WriteAllBytes(wrong,unrelated);
            foreach(var invalid in new[] { wrong,freshWrong,prefix+"-trailing.",prefix+"-space " })
            {
                Check(!Save(invalid) && window.Status.Text.Contains(".tdregion",StringComparison.Ordinal) &&
                      window.CopyMaskForVerification()!.SequenceEqual(mask) && window.AppliedMarkForVerification(40,30)==mark &&
                      window.Regions.SelectedIndex==selection && window.PendingMarkCount==0 &&
                      window.CancelSessionForVerification.Visibility==System.Windows.Visibility.Collapsed,
                      "Invalid checkpoint extension preserves the mask and reports a correctable filename: "+Path.GetExtension(invalid),checks);
            }
            Check(File.ReadAllBytes(wrong).SequenceEqual(unrelated) && !File.Exists(freshWrong) && !File.Exists(wrong+".tdregion"),
                  "Wrong checkpoint extension neither overwrites unrelated data nor silently renames it",checks);
            Check(Save(extensionless) && File.Exists(completed) && !File.Exists(extensionless) && !File.Exists(completed+".tdregion"),
                  "Extensionless checkpoint save creates exactly one discoverable tdregion file after invalid input",checks);
            var bytes=File.ReadAllBytes(completed);
            Check(!Save(extensionless) && File.ReadAllBytes(completed).SequenceEqual(bytes),
                  "Completed extension still refuses to overwrite an existing checkpoint",checks);
            Check(Save(upper) && File.Exists(upper) && !File.Exists(upper+".tdregion"),
                  "Checkpoint save accepts an existing uppercase extension without duplication",checks);
            window.ConfirmRectangleChangeForVerification=()=>true;
            var load=window.TryLoadSessionAsync(upper);PumpUntil(()=>load.IsCompleted);
            Check(load.GetAwaiter().GetResult() && window.CopyMaskForVerification()!.SequenceEqual(mask) && window.AppliedMarkForVerification(40,30)==mark,
                  "Case-preserved checkpoint reopens through the real loader with mask and corrections intact",checks);
        }
        finally { window.ConfirmRectangleChangeForVerification=oldConfirmation;foreach(var path in new[] { completed,upper,wrong,freshWrong })if(File.Exists(path))File.Delete(path); }
    }
}
