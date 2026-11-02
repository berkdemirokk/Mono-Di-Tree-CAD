using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Explicitly replaces a selected trace interval with a straight edge.</summary>
internal sealed class PhotoTraceRangeWindow : Window
{
    private readonly PhotoTraceWindow trace;
    private readonly Point[] snapshot;
    private readonly long revision;
    internal ComboBox First { get; } = new() { MinWidth=230,Margin=new Thickness(0,3,0,8) };
    internal ComboBox Last { get; } = new() { MinWidth=230,Margin=new Thickness(0,3,0,8) };
    internal TextBlock Status { get; } = new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0) };
    private sealed record Choice(int Index,Point Pixel)
    {
        public override string ToString()=>$"Nokta {Index+1} · X {MeasurementInput.Format(Pixel.X)}, Y {MeasurementInput.Format(Pixel.Y)}";
    }
    internal PhotoTraceRangeWindow(Window owner,PhotoTraceWindow trace)
    {
        Owner=owner;this.trace=trace;revision=trace.DraftRevision;snapshot=trace.CanvasForVerification.Points.ToArray();
        Title="Çizgi aralığını düzleştir";Width=500;Height=380;MinWidth=440;MinHeight=360;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var buttons=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };
        var cancel=new Button { Content="Vazgeç",IsCancel=true,Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,0) };
        var apply=new Button { Content="Aralığı düzleştir",Padding=new Thickness(12,6,12,6) };
        apply.Click+=(_,_)=> { if(TryApply())DialogResult=true; };buttons.Children.Add(cancel);buttons.Children.Add(apply);DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        var body=new StackPanel();root.Children.Add(body);
        body.Children.Add(new TextBlock { Text="İki çizgi noktası seçin. Aralarındaki bütün noktalar kaldırılır; iki uç düz bir kenarla bağlanır. Son işlem ana pencerede geri alınabilir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6) });
        body.Children.Add(new Label { Content="İlk uç",Target=First,Foreground=StudioTheme.Text,Padding=new Thickness(0) });body.Children.Add(First);
        body.Children.Add(new Label { Content="Son uç",Target=Last,Foreground=StudioTheme.Text,Padding=new Thickness(0) });body.Children.Add(Last);
        AutomationProperties.SetName(First,"Düzleştirilecek aralığın ilk çizgi noktası");AutomationProperties.SetName(Last,"Düzleştirilecek aralığın son çizgi noktası");
        AutomationProperties.SetName(Status,"Aralık düzleştirme durumu");AutomationProperties.SetLiveSetting(Status,AutomationLiveSetting.Polite);body.Children.Add(Status);
        for(var i=trace.CanvasForVerification.ReferencePointCount;i<snapshot.Length;i++) { First.Items.Add(new Choice(i,snapshot[i]));Last.Items.Add(new Choice(i,snapshot[i])); }
        First.SelectionChanged+=(_,_)=>UpdateStatus();Last.SelectionChanged+=(_,_)=>UpdateStatus();UpdateStatus();
    }
    private void UpdateStatus()
    {
        Status.Text=First.SelectedItem is Choice a && Last.SelectedItem is Choice b && b.Index-a.Index>=2
            ? $"Aradaki {b.Index-a.Index-1} nokta kaldırılacak; iki uç korunacak." : "İlk uçtan sonra ve en az bir ara nokta bırakacak son uç seçin.";
    }
    internal bool TryApply()
    {
        if(trace.DraftRevision!=revision || !trace.CanvasForVerification.Points.SequenceEqual(snapshot)) { Status.Text="Taslak değişti; pencereyi kapatıp yeniden açın.";return false; }
        if(First.SelectedItem is not Choice a || Last.SelectedItem is not Choice b || !trace.CanvasForVerification.TryFlattenTraceRange(a.Index,b.Index))
        { Status.Text="Aralık düzleştirilemedi; sıralı, farklı uçlar ve en az bir ara nokta seçin.";return false; }
        return true;
    }
}
