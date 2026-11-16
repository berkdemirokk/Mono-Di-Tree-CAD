using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TekstilCAD.Core.Commands;

namespace TekstilCAD.Desktop;

/// <summary>Where the command box is shown. The window uses a Popup; the headless verification uses a recording stand-in that opens nothing.</summary>
internal interface ICommandBoxHost
{
    bool IsOpen { get; }
    void Open(CommandBarView view);
    void Close();
}

/// <summary>Shows the box in a Popup below the header, centred and at most 880 px wide.</summary>
internal sealed class PopupCommandBoxHost(MainWindow window) : ICommandBoxHost
{
    private Popup? popup;
    public bool IsOpen=>popup is { IsOpen:true };
    public void Open(CommandBarView view)
    {
        if(popup is null)
        {
            popup=new Popup { AllowsTransparency=true,StaysOpen=false,Placement=PlacementMode.Relative,PopupAnimation=PopupAnimation.Fade,Child=view };
            popup.Closed+=(_,_)=>window.OnCommandBoxClosed();
        }
        var root=(FrameworkElement)window.Content;
        var width=Math.Min(880,Math.Max(320,root.ActualWidth-32));
        view.Width=width;
        popup.PlacementTarget=root;
        popup.HorizontalOffset=Math.Max(16,(root.ActualWidth-width)/2);
        // Below the header: the first top-docked child of the root panel.
        var headerBottom=root is Panel panel && panel.Children.Count>0 && panel.Children[0] is FrameworkElement first ? first.ActualHeight : 56;
        popup.VerticalOffset=Math.Max(8,headerBottom+4);
        popup.IsOpen=true;
    }
    public void Close()
    {
        if(popup is { IsOpen:true })popup.IsOpen=false;
    }
}

public partial class MainWindow
{
    private CommandBarView? commandBox;
    private ICommandBoxHost? commandBoxHost;
    private IInputElement? commandBoxReturnFocus;

    /// <summary>Verification only: replaces the Popup so no window is ever opened.</summary>
    internal Func<ICommandBoxHost>? CommandBoxHostForVerification { get; set; }
    internal CommandBarView? CommandBoxView=>commandBox;
    internal bool IsCommandBoxOpen=>commandBoxHost?.IsOpen ?? false;

    private CommandBarContext CommandContext()=>new(Document.Pieces.Count==0,Document.Pieces.Select(p=>p.Name ?? "").ToList(),
        Selected is not null,canvas is not null && canvas.HasPendingDrawing,dirty);

    /// <summary>The window side of the box. Applying goes through <see cref="ApplyCommand(Plan,bool)"/>; nothing here opens a message box.</summary>
    internal CommandBarHost CreateCommandBarHost()=>new(
        (text,onEnter)=>PreviewCommand(text,onEnter),
        (plan,confirmed)=>ApplyCommand(plan,confirmed),
        CommandContext,
        redo=>
        {
            try
            {
                if(windowClosed || recoveryClosing)return "Pencere kapanıyor.";
                if(!EnsureMainMutationReady())return "Görünüm yenilendi; geri almayı yeniden deneyin.";
                if(canvas.HasPendingDrawing)return "Önce çizimi bitirin (Enter) ya da iptal edin (Esc).";
                var before=redo ? session.RedoCount : session.UndoCount;
                if(before==0)return redo ? "Yinelenecek işlem yok." : "Geri alınacak işlem yok.";
                if(redo)Redo();else Undo();
                return (redo ? session.RedoCount : session.UndoCount)<before ? (redo ? "Yinelendi." : "Geri alındı.") : "Geri alma şimdi yapılamadı.";
            }
            catch(Exception ex) when(ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
            { return ex.Message; }
        },
        scene=> { canvas?.SetGhost(scene); });

    /// <summary>Ctrl+K: shows the command box (built on first use) and puts the keyboard cursor in it.</summary>
    internal void OpenCommandBox()
    {
        commandBox??=new CommandBarView(CreateCommandBarHost());
        commandBoxHost??=CommandBoxHostForVerification?.Invoke() ?? new PopupCommandBoxHost(this);
        if(commandBoxHost.IsOpen)
        {
            commandBox.FocusBox();commandBox.Box.SelectAll();return;
        }
        commandBoxReturnFocus=Keyboard.FocusedElement;
        commandBox.CloseRequested-=CloseCommandBox;
        commandBox.CloseRequested+=CloseCommandBox;
        commandBox.Reset();
        commandBoxHost.Open(commandBox);
        // The popup finishes its layout before it can take the keyboard focus.
        Dispatcher.BeginInvoke(new Action(()=>commandBox?.FocusBox()),System.Windows.Threading.DispatcherPriority.Input);
    }

    internal void CloseCommandBox()
    {
        commandBox?.ClearPreview();
        if(commandBoxHost is { IsOpen:true })commandBoxHost.Close();
        else OnCommandBoxClosed();
    }

    internal void OnCommandBoxClosed()
    {
        commandBox?.ClearPreview();
        var target=commandBoxReturnFocus;commandBoxReturnFocus=null;
        // Back to where the user was (the canvas by default); a control that vanished is simply skipped.
        if(target is UIElement element && element.IsVisible && element.IsEnabled)element.Focus();
        else canvas?.Focus();
    }

    /// <summary>
    /// Ctrl+K opens the box from anywhere (also while typing in a text box, like F1) and Esc closes an open box.
    /// Called first in <c>OnKeyDown</c>, before the text-box test. Returns true when the key was used.
    /// </summary>
    internal bool HandleCommandBoxShortcut(ModifierKeys modifiers,Key key)
    {
        if(modifiers==ModifierKeys.Control && key==Key.K)
        {
            if(windowClosed || recoveryClosing)return false;
            // The simple view has its own box in the page: Ctrl+K goes there instead of opening a second one.
            if(FocusSimpleCommandBar())return true;
            OpenCommandBox();return true;
        }
        if(modifiers==ModifierKeys.None && key==Key.Escape && IsCommandBoxOpen)
        {
            CloseCommandBox();return true;
        }
        return false;
    }
}
