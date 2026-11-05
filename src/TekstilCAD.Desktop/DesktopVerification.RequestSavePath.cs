using System.IO;
using System.Windows;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

internal static partial class DesktopVerification
{
    private static void VerifyRequestSavePath(Window owner,string reportPath,List<string> checks)
    {
        var folder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,"request-save-name-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);var window=new DesignRequestWindow(owner);
        try
        {
            window.LoadForVerification(new DesignRequest("","",null,"aline",2));
            window.MeasurementsForVerification.Text="bel çevresi 72 cm";
            var original=window.CaptureForVerification();var bare=Path.Combine(folder,"Sentetik A kesim");
            window.SavePathChoiceForVerification=()=>null;window.SaveForVerification();
            Check(window.DirtyForVerification && window.CaptureForVerification()==original && !Directory.EnumerateFiles(folder).Any(),
                "Cancelled save choice preserves model-only request state and creates no file",checks);
            window.SavePathChoiceForVerification=()=>bare;window.SaveForVerification();
            var complete=bare+DesignRequestSavePath.Extension;
            Check(File.Exists(complete) && !File.Exists(bare) && DesignRequestStorage.Load(complete)==original && !window.DirtyForVerification,
                "Extensionless UI save writes the full compound suffix and roundtrips selected model and measurements",checks);
            var bytes=File.ReadAllBytes(complete);window.MeasurementsForVerification.Text="bel çevresi 73 cm";
            window.SaveForVerification();
            Check(window.DirtyForVerification && File.ReadAllBytes(complete).SequenceEqual(bytes) && window.StatusForVerification.Text.Contains("kaydedilemedi"),
                "Existing completed filename cannot be overwritten and unsaved edits remain dirty",checks);
            var wrong=Path.Combine(folder,"wrong.json");window.SavePathChoiceForVerification=()=>wrong;window.SaveForVerification();
            Check(window.DirtyForVerification && !File.Exists(wrong) && !File.Exists(wrong+DesignRequestSavePath.Extension) &&
                window.StatusForVerification.Text.Contains("uzantısı değiştirilmedi"),"Wrong explicit extension is rejected rather than silently renamed",checks);
            var exact=Path.Combine(folder,"already"+DesignRequestSavePath.Extension);window.SavePathChoiceForVerification=()=>exact;window.SaveForVerification();
            Check(File.Exists(exact) && !File.Exists(exact+DesignRequestSavePath.Extension) && !window.DirtyForVerification,
                "Correct compound suffix is not duplicated by the UI save path",checks);
            var stale=Path.Combine(folder,"stale");window.SavePathChoiceForVerification=()=> { window.MeasurementsForVerification.Text="bel çevresi 74 cm";return stale; };
            window.SaveForVerification();
            Check(!File.Exists(stale+DesignRequestSavePath.Extension) && window.DirtyForVerification && window.StatusForVerification.Text.Contains("İstek değişti"),
                "Changing request during path selection still rejects save before suffix completion or writing",checks);
            window.LoadForVerification(new DesignRequest("V1 özgün tarif","",null));
            var legacy=Path.Combine(folder,"legacy");window.SavePathChoiceForVerification=()=>legacy;window.SaveForVerification();
            Check(DesignRequestStorage.Load(legacy+DesignRequestSavePath.Extension).Version==1,
                "Same filename completion works for V1 without changing its format",checks);
            Check(!window.IsVisible && !owner.IsVisible,"Save filename verification uses no visible dialog or window",checks);
        }
        finally { window.LoadForVerification(new DesignRequest("Temiz kapanış","",null));window.Close();Directory.Delete(folder,true); }
    }
}
