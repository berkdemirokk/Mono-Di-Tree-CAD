using System.IO;
using System.Windows;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

internal static partial class DesktopVerification
{
    private static void VerifyALineStorageErrorBoundary(ALinePanelPreviewWindow window,string folder,List<string> checks)
    {
        var fields=window.FieldsForVerification.Select(f=>f.Text).ToArray();var unit=window.UnitForVerification.SelectedIndex;
        var summary=window.SummaryForVerification.Text;var canvas=window.CanvasForVerification.Children.Cast<UIElement>().ToArray();
        var symmetry=window.AgreementForVerification.IsChecked;var confirmed=window.ConfirmationForVerification.IsChecked;var enabled=window.ConfirmationForVerification.IsEnabled;
        var transfer=window.ProposalAgreementForVerification.IsChecked;var transferEnabled=window.ProposalApplyForVerification.IsEnabled;
        bool Unchanged()=>window.FieldsForVerification.Select(f=>f.Text).SequenceEqual(fields) && window.UnitForVerification.SelectedIndex==unit &&
            window.SummaryForVerification.Text==summary && window.CanvasForVerification.Children.Cast<UIElement>().SequenceEqual(canvas) &&
            window.AgreementForVerification.IsChecked==symmetry && window.ConfirmationForVerification.IsChecked==confirmed && window.ConfirmationForVerification.IsEnabled==enabled &&
            window.ProposalAgreementForVerification.IsChecked==transfer && window.ProposalApplyForVerification.IsEnabled==transferEnabled;
        var oldDiscard=window.ConfirmDiscardWorkForVerification;window.ConfirmDiscardWorkForVerification=()=>false;
        try
        {
            var validJson=ALineWorkDraftStorage.Serialize(new("cm",fields));
            var samples=new (string Name,byte[] Bytes)[]{
                ("empty-object",System.Text.Encoding.UTF8.GetBytes("{}")),
                ("broken-json",System.Text.Encoding.UTF8.GetBytes("{")),
                ("unknown-version",System.Text.Encoding.UTF8.GetBytes(validJson.Replace("\"version\":1","\"version\":99",StringComparison.Ordinal))),
                ("invalid-utf8",[0xff]),
                ("oversized",new byte[ALineWorkDraftStorage.MaximumFileBytes+1])
            };
            foreach(var (name,bytes) in samples)
            {
                var bad=Path.Combine(folder,"boundary-"+name+ALineWorkDraftStorage.Extension);File.WriteAllBytes(bad,bytes);var asked=false;
                Check(!window.TryOpenWork(()=>bad,()=> { asked=true;return true; }) && !asked && Unchanged() &&
                    window.StatusForVerification.Text.StartsWith("Çalışma açılamadı",StringComparison.Ordinal) && File.ReadAllBytes(bad).SequenceEqual(bytes) && !window.CanCloseWorkForVerification(),
                    "Real open error boundary contains malformed draft, preserves file/form/approval/geometry and dirty state: "+name,checks);
            }
            var wrong=Path.Combine(folder,"boundary-wrong.json");
            Check(!window.TrySaveWork(()=>wrong) && !File.Exists(wrong) && Unchanged() && !window.CanCloseWorkForVerification() &&
                window.StatusForVerification.Text.StartsWith("Çalışma kaydedilemedi",StringComparison.Ordinal),
                "Real save error boundary contains invalid extension and never marks failed work saved",checks);
            var status=window.StatusForVerification.Text;
            Check(!window.TrySaveWork(()=>null) && !window.TryOpenWork(()=>null,()=>throw new InvalidOperationException("Cancel must not ask")) &&
                Unchanged() && window.StatusForVerification.Text==status && !window.CanCloseWorkForVerification(),
                "Cancelled selectors preserve all current state without false success",checks);
            var valid=Path.Combine(folder,"boundary-valid"+ALineWorkDraftStorage.Extension);
            Check(window.TrySaveWork(()=>valid) && Unchanged() && window.CanCloseWorkForVerification() && ALineWorkDraftStorage.Load(valid).Fields.SequenceEqual(fields),
                "Successful save after handled failures writes exact raw fields and establishes the clean checkpoint",checks);
            Check(!window.TryOpenWork(()=>valid,()=>false) && Unchanged(),"Declining valid file replacement through the real boundary preserves confirmed preview",checks);
        }
        finally { window.ConfirmDiscardWorkForVerification=oldDiscard; }
    }
}
