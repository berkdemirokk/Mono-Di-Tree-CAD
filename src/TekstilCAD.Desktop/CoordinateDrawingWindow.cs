using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Enters unsnapped world coordinates through the same drawing operation as the canvas.</summary>
internal sealed class CoordinateDrawingWindow : Window
{
    private readonly Func<string> guidance;
    private readonly Func<Point2,string?> addPoint;
    private readonly Func<string?> canFinish;
    private readonly Func<string?> removePoint;
    private readonly Func<string> summary;
    internal TextBox XInput { get; } = new() { MinWidth=160, Margin=new Thickness(0,4,0,10), Padding=new Thickness(7) };
    internal TextBox YInput { get; } = new() { MinWidth=160, Margin=new Thickness(0,4,0,10), Padding=new Thickness(7) };
    internal TextBlock Guidance { get; } = new() { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,6,0,12) };
    internal TextBlock Feedback { get; } = new() { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,8,0,0), Foreground=StudioTheme.Warning };
    internal TextBlock PointSummary { get; } = new() { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,4,0,10) };
    internal Button AddPointButton { get; }
    internal Button FinishButton { get; }
    internal Button RemovePointButton { get; }

    // A null callback result means success; an explanatory string means the
    // operation was not accepted or its completion needs the user's attention.
    internal CoordinateDrawingWindow(Window owner, Func<string> guidance, Func<Point2,string?> addPoint, Func<string?> canFinish, Func<string?> removePoint, Func<string> summary)
    {
        ArgumentNullException.ThrowIfNull(owner);
        this.guidance=guidance ?? throw new ArgumentNullException(nameof(guidance));
        this.addPoint=addPoint ?? throw new ArgumentNullException(nameof(addPoint));
        this.canFinish=canFinish ?? throw new ArgumentNullException(nameof(canFinish));
        this.removePoint=removePoint ?? throw new ArgumentNullException(nameof(removePoint));
        this.summary=summary ?? throw new ArgumentNullException(nameof(summary));
        Owner=owner; Title="Koordinatla çizim · Mono Di Tree CAD";
        Width=500; Height=490; MinWidth=390; MinHeight=370;
        WindowStartupLocation=WindowStartupLocation.CenterOwner; StudioTheme.Apply(this);

        var root=new DockPanel { Margin=new Thickness(18) }; Content=root;
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,10,0,0) };
        DockPanel.SetDock(actions,Dock.Bottom); root.Children.Add(actions);
        AddPointButton=new Button { Content="Nokta ekle", Padding=new Thickness(12,7,12,7), Margin=new Thickness(0,0,8,4), IsDefault=true };
        FinishButton=new Button { Content="Çoklu çizgiyi bitir", Padding=new Thickness(12,7,12,7), Margin=new Thickness(0,0,8,4) };
        RemovePointButton=new Button { Content="Son noktayı kaldır", Padding=new Thickness(12,7,12,7), Margin=new Thickness(0,0,8,4) };
        var close=new Button { Content="Kapat", Padding=new Thickness(12,7,12,7), Margin=new Thickness(0,0,0,4), IsCancel=true };
        actions.Children.Add(AddPointButton); actions.Children.Add(RemovePointButton); actions.Children.Add(FinishButton); actions.Children.Add(close);
        close.Click+=(_,_)=>DialogResult=false;
        var body=new StackPanel();
        var scroller=new ScrollViewer { Content=body, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
        root.Children.Add(scroller);
        body.Children.Add(new TextBlock { Text="Koordinatla çizim", FontSize=20, FontWeight=FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text="Seçili çizim aracı için mutlak X ve Y koordinatlarını milimetre cinsinden girin. Izgaraya otomatik yuvarlama uygulanmaz. Virgül veya nokta kullanabilirsiniz.", TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,8,0,0) });
        body.Children.Add(new TextBlock { Text="Sıradaki adım", FontWeight=FontWeights.SemiBold, Margin=new Thickness(0,12,0,0) });
        AutomationProperties.SetName(Guidance,"Çizimde sıradaki adım"); AutomationProperties.SetLiveSetting(Guidance,AutomationLiveSetting.Polite); body.Children.Add(Guidance);
        AutomationProperties.SetName(PointSummary,"Tamamlanmamış çizimde nokta özeti"); AutomationProperties.SetLiveSetting(PointSummary,AutomationLiveSetting.Polite); body.Children.Add(PointSummary);
        var xLabel=new Label { Content="X koordinatı (mm)", Target=XInput, Padding=new Thickness(0) }; body.Children.Add(xLabel); body.Children.Add(XInput);
        var yLabel=new Label { Content="Y koordinatı (mm)", Target=YInput, Padding=new Thickness(0) }; body.Children.Add(yLabel); body.Children.Add(YInput);
        AutomationProperties.SetName(XInput,"X koordinatı, milimetre"); AutomationProperties.SetName(YInput,"Y koordinatı, milimetre");
        body.Children.Add(new TextBlock { Text="Kapat, tamamlanmamış çizimi silmez. Çoklu çizgiyi kapatmak için en az üç nokta ekleyin ve Çoklu çizgiyi bitir'i seçin.", TextWrapping=TextWrapping.Wrap, Foreground=StudioTheme.Muted, Margin=new Thickness(0,4,0,0) });
        AutomationProperties.SetName(Feedback,"Koordinat girişi durumu"); AutomationProperties.SetLiveSetting(Feedback,AutomationLiveSetting.Polite); body.Children.Add(Feedback);
        XInput.TextChanged+=(_,_)=>ClearFeedback(); YInput.TextChanged+=(_,_)=>ClearFeedback();
        AddPointButton.Click+=(_,_)=>AddPoint(); RemovePointButton.Click+=(_,_)=>RemoveLastPoint(); FinishButton.Click+=(_,_)=>RequestFinish();
        Loaded+=(_,_)=>XInput.Focus();
        UpdateDrawingState();
    }

    private void ClearFeedback() => Feedback.Text="";
    private static void Announce(TextBlock control)
    {
        if(UIElementAutomationPeer.CreatePeerForElement(control) is { } peer)
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    private void ShowFeedback(string message, bool error)
    {
        Feedback.Foreground=error ? StudioTheme.Warning : StudioTheme.Text;
        Feedback.Text=message; Announce(Feedback);
    }
    private void UpdateDrawingState()
    {
        Guidance.Text=guidance(); PointSummary.Text=summary();
        AutomationProperties.SetName(PointSummary,"Tamamlanmamış çizimde nokta özeti: "+PointSummary.Text);
        Announce(Guidance); Announce(PointSummary);
    }

    internal void AddPoint()
    {
        if(!MeasurementInput.TryParseMillimeters(XInput.Text,1e8,out var x))
        { ShowFeedback("Geçerli bir X koordinatı girin (−100.000.000 ile 100.000.000 mm arasında).",true); XInput.Focus(); return; }
        if(!MeasurementInput.TryParseMillimeters(YInput.Text,1e8,out var y))
        { ShowFeedback("Geçerli bir Y koordinatı girin (−100.000.000 ile 100.000.000 mm arasında).",true); YInput.Focus(); return; }
        string? problem;
        try { problem=addPoint(new Point2(x,y)); }
        catch(Exception ex) when(ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        { ShowFeedback("Nokta işleminin durumu doğrulanamadı. Yeniden denemeden önce çizimi kontrol edin: "+ex.Message,true); return; }
        try
        {
            UpdateDrawingState();
            ShowFeedback(problem ?? "Nokta işlendi. Sıradaki adımı kontrol edin.",problem is not null);
        }
        catch(Exception ex) when(ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        { ShowFeedback("Nokta işlemi uygulanmış olabilir, ancak görünüm güncellenemedi. Yeniden denemeden önce çizimi kontrol edin: "+ex.Message,true); }
    }

    internal void RemoveLastPoint()
    {
        string? problem;
        try { problem=removePoint(); }
        catch(Exception ex) when(ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        { ShowFeedback("Nokta kaldırma işleminin durumu doğrulanamadı. Yeniden denemeden önce çizimi kontrol edin: "+ex.Message,true); return; }
        try
        {
            UpdateDrawingState();
            ShowFeedback(problem ?? "Son nokta kaldırıldı.",problem is not null);
        }
        catch(Exception ex) when(ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        { ShowFeedback("Nokta kaldırılmış olabilir, ancak görünüm güncellenemedi. Yeniden denemeden önce çizimi kontrol edin: "+ex.Message,true); }
    }

    internal void RequestFinish()
    {
        string? problem;
        try { problem=canFinish(); }
        catch(Exception ex) when(ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        { ShowFeedback("Çizimin bitirilip bitirilemeyeceği doğrulanamadı: "+ex.Message,true); return; }
        if(problem is not null) { ShowFeedback(problem,true); return; }
        DialogResult=true;
    }

}
