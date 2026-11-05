using System.Windows;
using System.Windows.Media.Imaging;

namespace TekstilCAD.Desktop;

internal static partial class DesktopVerification
{
    private static void VerifyRegionConfirmation(Window owner,BitmapSource bitmap,string checkpoint,List<string> checks)
    {
        var window=new PhotoRegionWindow(owner,bitmap,reviewOnly:true);
        var reply=MessageBoxResult.No;var calls=0;var closedCount=0;string? lastTitle=null;
        window.Closed+=(_,_)=>closedCount++;
        window.ShowDiscardDialogForVerification=(dialogOwner,text,title,buttons,icon,defaultResult)=>
        {
            calls++;lastTitle=title;
            Check(ReferenceEquals(dialogOwner,window) && !string.IsNullOrWhiteSpace(text) &&
                  buttons==MessageBoxButton.YesNo && icon==MessageBoxImage.Warning && defaultResult==MessageBoxResult.No,
                  "Region discard dialog passes owned Yes/No warning with explicit default No: "+title,checks);
            return reply;
        };
        bool Load() { var task=window.TryLoadSessionAsync(checkpoint);PumpUntil(()=>task.IsCompleted);return task.GetAwaiter().GetResult(); }
        try
        {
            Check(Load() && calls==0,"An empty region window loads without a destructive confirmation",checks);
            var mask=window.CopyMaskForVerification()!;var originalLeft=window.left.Text;
            window.MarkMode.SelectedIndex=1;
            Check(window.TryAddMark(3,3),"Discard safety probe contains an unsaved brush correction",checks);
            foreach(var result in new[] { MessageBoxResult.No,MessageBoxResult.Cancel,MessageBoxResult.None })
            {
                reply=result;var previous=calls;
                window.left.Text="1";
                Check(calls==previous+1 && lastTitle=="Bölgeyi değiştir" && window.left.Text==originalLeft &&
                      window.CopyMaskForVerification()!.SequenceEqual(mask) && window.PendingMarkAtForVerification(3,3)==true,
                      "Non-Yes rectangle confirmation restores the field and keeps mask and pending correction: "+result,checks);
                previous=calls;
                Check(!Load() && calls==previous+1 && lastTitle=="Bölgeyi aç" &&
                      window.CopyMaskForVerification()!.SequenceEqual(mask) && window.PendingMarkAtForVerification(3,3)==true,
                      "Non-Yes checkpoint replacement preserves current mask and pending correction: "+result,checks);
                previous=calls;window.Close();
                Check(calls==previous+1 && lastTitle=="Bölgeyi kapat" && closedCount==0 &&
                      window.CopyMaskForVerification()!.SequenceEqual(mask) && window.PendingMarkAtForVerification(3,3)==true,
                      "Non-Yes close confirmation keeps the window and unsaved mask work: "+result,checks);
            }
            var originalDialog=window.ShowDiscardDialogForVerification;
            reply=MessageBoxResult.Yes;
            window.ShowDiscardDialogForVerification=(dialogOwner,text,title,buttons,icon,defaultResult)=>
            {
                var result=originalDialog!(dialogOwner,text,title,buttons,icon,defaultResult);
                Check(window.TryAddMark(4,4),"A correction can arrive while the discard dialog is open",checks);
                return result;
            };
            Check(!Load() && window.PendingMarkAtForVerification(3,3)==true && window.PendingMarkAtForVerification(4,4)==true &&
                  window.CopyMaskForVerification()!.SequenceEqual(mask),
                  "Even explicit Yes rejects a stale confirmation and preserves both pending corrections",checks);
            window.ShowDiscardDialogForVerification=originalDialog;
            Check(Load() && window.PendingMarkCount==0 && window.CopyMaskForVerification()!.SequenceEqual(mask),
                  "Explicit current Yes still permits replacing the pending work with the saved checkpoint",checks);
            window.Close();Check(closedCount==1,"Explicit current Yes still permits closing the region window",checks);
        }
        finally { window.ConfirmCloseForVerification=()=>true;if(closedCount==0)window.Close(); }
    }
}
