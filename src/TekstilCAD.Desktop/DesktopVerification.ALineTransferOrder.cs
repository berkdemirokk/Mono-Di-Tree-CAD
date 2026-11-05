using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace TekstilCAD.Desktop;

internal static partial class DesktopVerification
{
    private static void VerifyALineTransferOrder(Window owner,List<string> checks)
    {
        var window=new ALinePanelPreviewWindow(owner,"Ön bel yayı: 36 cm","") { ConfirmDiscardWorkForVerification=()=>true };
        try
        {
            _=new WindowInteropHelper(window).EnsureHandle();
            var scroll=(ScrollViewer)window.Content;var body=(Panel)scroll.Content;var unit=window.UnitForVerification;
            var label=AutomationProperties.GetLabeledBy(unit) as Label;
            var unitIndex=body.Children.IndexOf(unit);var summaryIndex=body.Children.IndexOf(window.ProposalForVerification);
            var consentIndex=body.Children.IndexOf(window.ProposalAgreementForVerification);var applyIndex=body.Children.IndexOf(window.ProposalApplyForVerification);
            Check(label is not null && ReferenceEquals(label.Target,unit) && body.Children.IndexOf(label)<unitIndex && unitIndex>=0 &&
                unitIndex<summaryIndex && summaryIndex<consentIndex && consentIndex<applyIndex && applyIndex<body.Children.IndexOf(window.FieldsForVerification[0]),
                "Real A-line visual tree orders labelled unit before source summary, consent, transfer and manual fields",checks);
            var fieldPositions=window.FieldsForVerification.Select(f=>body.Children.IndexOf(f)).ToArray();
            Check(fieldPositions.Length==11 && fieldPositions.All(i=>i>applyIndex) && fieldPositions.SequenceEqual(fieldPositions.OrderBy(i=>i)),
                "Reordering transfer prerequisites preserves all eleven manual fields in schema order",checks);
            Check(unit.SelectedIndex==-1 && window.FieldsForVerification.All(f=>f.Text=="") && !window.ProposalAgreementForVerification.IsEnabled && !window.ProposalApplyForVerification.IsEnabled,
                "Opening still requires explicit unit choice and never pre-fills or approves transfer",checks);
            foreach(var size in new[]{new Size(500,450),new Size(700,800)})
            {
                scroll.Measure(size);scroll.Arrange(new Rect(new Point(),size));scroll.UpdateLayout();
                unit.BringIntoView();scroll.UpdateLayout();
                var position=unit.TransformToAncestor(scroll).Transform(new Point());
                FocusManager.SetFocusedElement(window,unit);
                Check(unit.Focusable && unit.IsEnabled && unit.IsTabStop && ReferenceEquals(FocusManager.GetFocusedElement(window),unit) &&
                    AutomationProperties.GetName(unit).Contains("zorunlu") && AutomationProperties.GetHelpText(unit).Contains("Varsayılan birim yoktur") &&
                    unit.ActualHeight>0 && position.Y>=-1 && position.Y+unit.ActualHeight<=scroll.ActualHeight+1,
                    "Unit selector supports logical keyboard focus, accessible labelling and scroll-into-view at "+size,checks);
            }
            unit.SelectedIndex=0;
            Check(window.FieldsForVerification.All(f=>f.Text=="") && window.ProposalAgreementForVerification.IsEnabled && !window.ProposalApplyForVerification.IsEnabled,
                "Choosing a unit enables review consent but does not fill fields or approve transfer",checks);
            window.ProposalAgreementForVerification.IsChecked=true;
            window.ProposalApplyForVerification.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(window.FieldsForVerification[0].Text=="36" && window.FieldsForVerification.Skip(1).All(f=>f.Text=="") &&
                window.AgreementForVerification.IsChecked!=true && window.ConfirmationForVerification.IsChecked!=true && window.CanvasForVerification.Children.Count==0,
                "Explicit consent after unit choice transfers only known values without approving symmetry or geometry",checks);
            Check(!window.IsVisible && !owner.IsVisible,"Transfer ordering verification does not show windows",checks);
        }
        finally { window.Close(); }
    }
}
