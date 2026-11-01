using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Read-only comparison of a detached photo trace snapshot.</summary>
internal sealed class PhotoTraceCheckWindow : Window
{
    private readonly Point2[] points;
    private readonly int previewWidth,previewHeight,sourceWidth,sourceHeight;
    private readonly double referenceLength;
    internal ComboBox FirstForVerification { get; } = new() { MinWidth=220,Margin=new Thickness(0,3,0,6) };
    internal ComboBox SecondForVerification { get; } = new() { MinWidth=220,Margin=new Thickness(0,3,0,6) };
    internal TextBox KnownForVerification { get; } = new() { MinWidth=120,MaxLength=32,Margin=new Thickness(0,3,0,6) };
    internal TextBox ResultForVerification { get; } = new() { IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,MinHeight=75,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };

    internal PhotoTraceCheckWindow(Window owner,int previewWidth,int previewHeight,int sourceWidth,int sourceHeight,double referenceLength,IReadOnlyList<Point> snapshot)
    {
        ArgumentNullException.ThrowIfNull(owner);ArgumentNullException.ThrowIfNull(snapshot);
        if(snapshot.Count<3 || snapshot.Count>PhotoTraceCanvas.MaximumPoints)throw new ArgumentOutOfRangeException(nameof(snapshot));
        this.previewWidth=previewWidth;this.previewHeight=previewHeight;this.sourceWidth=sourceWidth;this.sourceHeight=sourceHeight;this.referenceLength=referenceLength;
        points=snapshot.Select(p=>new Point2(p.X,p.Y)).ToArray();
        Owner=owner;Title="Fotoğrafta ölçü karşılaştır";Width=610;Height=580;MinWidth=460;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel { Margin=new Thickness(0,8,0,0) };DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var calculate=new Button { Content="Karşılaştır",Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,0) };
        var close=new Button { Content="Kapat",Padding=new Thickness(12,7,12,7),IsCancel=true };actions.Children.Add(calculate);actions.Children.Add(close);
        var scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };root.Children.Add(scroll);
        var body=new StackPanel();scroll.Content=body;
        body.Children.Add(new TextBlock { Text="Fotoğraf taslağının bu pencere açıldığındaki noktaları kullanılır. Referansla aynı iki ucu seçmeyin. Başka bir gerçek mesafeyi bağımsız olarak ölçüp girin; karşılaştırma yalnız iki nokta arasındaki düz mesafeyi değerlendirir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) });
        body.Children.Add(new Label { Content="Kontrol başlangıcı",Target=FirstForVerification,Foreground=StudioTheme.Text });body.Children.Add(FirstForVerification);
        body.Children.Add(new Label { Content="Kontrol bitişi",Target=SecondForVerification,Foreground=StudioTheme.Text });body.Children.Add(SecondForVerification);
        body.Children.Add(new Label { Content="Bağımsız bilinen mesafe (mm)",Target=KnownForVerification,Foreground=StudioTheme.Text });body.Children.Add(KnownForVerification);
        body.Children.Add(new TextBlock { Text="Sonuç, çizimi veya projeyi değiştirmez ve kaydedilmez. Uyum tek başına perspektifi, diğer ölçüleri veya üretim uygunluğunu doğrulamaz.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,4,0,8) });
        body.Children.Add(ResultForVerification);
        var options=points.Select((p,i)=>new Choice(i,$"{i+1} · {(i<2 ? "referans" : "çizgi")} · X {MeasurementInput.Format(p.X)}, Y {MeasurementInput.Format(p.Y)}")).ToArray();
        FirstForVerification.ItemsSource=options;SecondForVerification.ItemsSource=options;
        AutomationProperties.SetName(FirstForVerification,"Kontrol başlangıç noktası");AutomationProperties.SetName(SecondForVerification,"Kontrol bitiş noktası");
        AutomationProperties.SetName(KnownForVerification,"Bağımsız bilinen kontrol mesafesi milimetre");AutomationProperties.SetName(ResultForVerification,"Ölçü karşılaştırması sonucu");AutomationProperties.SetLiveSetting(ResultForVerification,AutomationLiveSetting.Polite);
        FirstForVerification.SelectionChanged+=(_,_)=>ClearResult();SecondForVerification.SelectionChanged+=(_,_)=>ClearResult();KnownForVerification.TextChanged+=(_,_)=>ClearResult();
        calculate.Click+=(_,_)=> { Calculate();ResultForVerification.BringIntoView(); };close.Click+=(_,_)=>Close();
    }
    private void ClearResult()=>ResultForVerification.Text="";
    internal void Calculate()
    {
        try
        {
            if(FirstForVerification.SelectedItem is not Choice first || SecondForVerification.SelectedItem is not Choice second)
            { ResultForVerification.Text="İki kontrol noktası seçin.";return; }
            if(!MeasurementInput.TryParseMillimeters(KnownForVerification.Text,out var known))
            { ResultForVerification.Text="Bilinen kontrol mesafesini milimetre olarak girin.";return; }
            var result=PhotoTraceMeasurement.Compare(previewWidth,previewHeight,sourceWidth,sourceHeight,points[0],points[1],referenceLength,points[first.Index],points[second.Index],known);
            ResultForVerification.Text=$"Fotoğraftan tahmin: {MeasurementInput.Format(result.EstimatedMm)} mm\nGirilen gerçek mesafe: {MeasurementInput.Format(result.KnownMm)} mm\nFark (tahmin − girilen): {MeasurementInput.Format(result.DifferenceMm)} mm\n\nYalnız seçilen iki nokta karşılaştırıldı; ölçü veya üretim onayı verilmedi.";
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or OverflowException)
        { ResultForVerification.Text="Karşılaştırma yapılamadı: "+ex.Message; }
    }
    private sealed record Choice(int Index,string Label) { public override string ToString()=>Label; }
}
