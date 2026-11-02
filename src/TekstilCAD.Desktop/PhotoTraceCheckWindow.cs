using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Collections.ObjectModel;
using System.Windows.Data;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Read-only comparison of a detached photo trace snapshot.</summary>
internal sealed class PhotoTraceCheckWindow : Window
{
    private readonly Point2[] points;
    private readonly int previewWidth,previewHeight,sourceWidth,sourceHeight;
    private readonly double referenceLength;
    private readonly PhotoPlaneCalibration? plane;
    internal ComboBox FirstForVerification { get; } = new() { MinWidth=220,Margin=new Thickness(0,3,0,6) };
    internal ComboBox SecondForVerification { get; } = new() { MinWidth=220,Margin=new Thickness(0,3,0,6) };
    internal TextBox KnownForVerification { get; } = new() { MinWidth=120,MaxLength=32,Margin=new Thickness(0,3,0,6) };
    internal TextBox ResultForVerification { get; } = new() { IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,MinHeight=75,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    internal PhotoTraceMeasurementResult? LastResultForVerification { get; private set; }
    internal ObservableCollection<string> ComparisonsForVerification { get; } = [];
    internal ListBox ComparisonListForVerification { get; } = new() { MinHeight=75,MaxHeight=160,Margin=new Thickness(0,4,0,4) };
    internal PhotoMeasurementPreview PreviewForVerification { get; }
    internal TextBlock PickStatusForVerification { get; } = new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,4),Text="Fotoğrafta sol tık: 1; sağ tık: 2. Yakın noktalar için listeyi kullanın." };

    internal PhotoTraceCheckWindow(Window owner,BitmapSource image,int sourceWidth,int sourceHeight,double referenceLength,IReadOnlyList<Point> snapshot,PhotoPlaneCalibration? plane=null)
    {
        ArgumentNullException.ThrowIfNull(owner);ArgumentNullException.ThrowIfNull(snapshot);ArgumentNullException.ThrowIfNull(image);
        if(snapshot.Count<3 || snapshot.Count>PhotoTraceCanvas.MaximumPoints)throw new ArgumentOutOfRangeException(nameof(snapshot));
        previewWidth=image.PixelWidth;previewHeight=image.PixelHeight;this.sourceWidth=sourceWidth;this.sourceHeight=sourceHeight;this.referenceLength=referenceLength;this.plane=plane?.Clone();
        points=snapshot.Select(p=>new Point2(p.X,p.Y)).ToArray();
        PreviewForVerification=new PhotoMeasurementPreview(image,points,sourceWidth,sourceHeight);
        Owner=owner;Title="Fotoğrafta ölçü karşılaştır";Width=610;Height=580;MinWidth=460;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel { Margin=new Thickness(0,8,0,0) };DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var calculate=new Button { Content="Karşılaştır",Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,0) };
        var close=new Button { Content="Kapat",Padding=new Thickness(12,7,12,7),IsCancel=true };actions.Children.Add(calculate);actions.Children.Add(close);
        var scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };root.Children.Add(scroll);
        var body=new StackPanel();scroll.Content=body;
        body.Children.Add(new TextBlock { Text="Fotoğraf taslağının bu pencere açıldığındaki noktaları kullanılır. İki referans köşesini birlikte seçmeyin. Başka bir gerçek mesafeyi bağımsız olarak ölçüp girin; karşılaştırma yalnız iki nokta arasındaki düz mesafeyi değerlendirir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) });
        var selection=new Grid();selection.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });selection.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        var selectors=new StackPanel();selectors.Children.Add(new Label { Content="1 · Kontrol başlangıcı",Target=FirstForVerification,Foreground=StudioTheme.Text });selectors.Children.Add(FirstForVerification);
        selectors.Children.Add(new Label { Content="2 · Kontrol bitişi",Target=SecondForVerification,Foreground=StudioTheme.Text });selectors.Children.Add(SecondForVerification);selection.Children.Add(selectors);
        var previewColumn=new StackPanel { Margin=new Thickness(10,0,0,0) };previewColumn.Children.Add(PreviewForVerification);
        var zoomActions=new WrapPanel();
        void ZoomButton(string label,string name,Action action) { var button=new Button { Content=label,MinWidth=34,Padding=new Thickness(4,2,4,2),Margin=new Thickness(0,2,4,0) };AutomationProperties.SetName(button,name);if(label!="Sığdır") { button.ToolTip="Seçili ilk uca, yoksa ikinci uca odaklanır.";AutomationProperties.SetHelpText(button,(string)button.ToolTip); } button.Click+=(_,_)=>action();zoomActions.Children.Add(button); }
        ZoomButton("+","Fotoğrafı yakınlaştır",()=>PreviewForVerification.ZoomSelected(2));
        ZoomButton("−","Fotoğrafı uzaklaştır",()=>PreviewForVerification.ZoomSelected(.5));
        ZoomButton("Sığdır","Fotoğrafın tamamını göster",PreviewForVerification.ResetZoom);
        previewColumn.Children.Add(zoomActions);Grid.SetColumn(previewColumn,1);selection.Children.Add(previewColumn);body.Children.Add(selection);
        AutomationProperties.SetLiveSetting(PickStatusForVerification,AutomationLiveSetting.Polite);body.Children.Add(PickStatusForVerification);
        body.Children.Add(new Label { Content="Bağımsız bilinen mesafe (mm)",Target=KnownForVerification,Foreground=StudioTheme.Text });body.Children.Add(KnownForVerification);
        body.Children.Add(new TextBlock { Text="Sonuç, çizimi veya projeyi değiştirmez ve kaydedilmez. Uyum tek başına perspektifi, diğer ölçüleri veya üretim uygunluğunu doğrulamaz.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,4,0,8) });
        body.Children.Add(ResultForVerification);
        body.Children.Add(new TextBlock { Text="Bu penceredeki kontrol listesi",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,0) });
        body.Children.Add(new TextBlock { Text="Farklı nokta çiftlerini ve bağımsız ölçüleri karşılaştırıp listeye ekleyin. Liste geçicidir; pencere kapanınca silinir. Eşleşmeler diğer bölgeleri veya üretime uygunluğu doğrulamaz.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,4) });
        ComparisonListForVerification.ItemsSource=ComparisonsForVerification;ComparisonListForVerification.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        ScrollViewer.SetHorizontalScrollBarVisibility(ComparisonListForVerification,ScrollBarVisibility.Disabled);
        var comparisonText=new FrameworkElementFactory(typeof(TextBlock));comparisonText.SetBinding(TextBlock.TextProperty,new Binding());comparisonText.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);
        ComparisonListForVerification.ItemTemplate=new DataTemplate { VisualTree=comparisonText };
        AutomationProperties.SetName(ComparisonListForVerification,"Fotoğraf ölçü karşılaştırmaları");body.Children.Add(ComparisonListForVerification);
        var listActions=new WrapPanel();
        var addComparison=new Button { Content="Bu karşılaştırmayı listeye ekle",Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,0,6,4) };
        var removeComparison=new Button { Content="Seçileni kaldır",Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,0,6,4) };
        var clearComparisons=new Button { Content="Listeyi temizle",Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,0,6,4) };
        addComparison.Click+=(_,_)=> { if(!AddComparison())ResultForVerification.BringIntoView(); };removeComparison.Click+=(_,_)=>RemoveComparison();clearComparisons.Click+=(_,_)=>ComparisonsForVerification.Clear();
        listActions.Children.Add(addComparison);listActions.Children.Add(removeComparison);listActions.Children.Add(clearComparisons);body.Children.Add(listActions);
        var referenceCount=plane is null ? 2 : 4;
        var options=points.Select((p,i)=>new Choice(i,$"{i+1} · {(i<referenceCount ? "referans" : "çizgi")} · X {MeasurementInput.Format(p.X)}, Y {MeasurementInput.Format(p.Y)}")).ToArray();
        FirstForVerification.ItemsSource=options;SecondForVerification.ItemsSource=options;
        AutomationProperties.SetName(FirstForVerification,"Kontrol başlangıç noktası");AutomationProperties.SetName(SecondForVerification,"Kontrol bitiş noktası");
        AutomationProperties.SetName(KnownForVerification,"Bağımsız bilinen kontrol mesafesi milimetre");AutomationProperties.SetName(ResultForVerification,"Ölçü karşılaştırması sonucu");AutomationProperties.SetLiveSetting(ResultForVerification,AutomationLiveSetting.Polite);
        void SelectionChanged() { ClearResult();PreviewForVerification.Select((FirstForVerification.SelectedItem as Choice)?.Index,(SecondForVerification.SelectedItem as Choice)?.Index); }
        FirstForVerification.SelectionChanged+=(_,_)=>SelectionChanged();SecondForVerification.SelectionChanged+=(_,_)=>SelectionChanged();KnownForVerification.TextChanged+=(_,_)=>ClearResult();
        PreviewForVerification.PointChosen+=(index,second)=> { if(second)SecondForVerification.SelectedIndex=index;else FirstForVerification.SelectedIndex=index; };
        PreviewForVerification.PickMessage+=message=>PickStatusForVerification.Text=message;
        calculate.Click+=(_,_)=> { Calculate();ResultForVerification.BringIntoView(); };close.Click+=(_,_)=>Close();
    }
    private void ClearResult() { LastResultForVerification=null;ResultForVerification.Text=""; }
    internal bool AddComparison()
    {
        if(ComparisonsForVerification.Count>=100) { ResultForVerification.Text="Liste 100 karşılaştırmayla sınırlıdır; yeni eklemeden önce bir satırı kaldırın.";LastResultForVerification=null;return false; }
        Calculate();
        if(LastResultForVerification is not { } result || FirstForVerification.SelectedItem is not Choice first || SecondForVerification.SelectedItem is not Choice second)return false;
        var relative=result.DifferenceMm/result.KnownMm*100;
        var percent=double.IsFinite(relative) ? relative.ToString("G6",System.Globalization.CultureInfo.CurrentCulture)+" %" : "gösterilemiyor";
        ComparisonsForVerification.Add($"{first.Label} → {second.Label} | tahmin {MeasurementInput.Format(result.EstimatedMm)} mm | ölçülen {MeasurementInput.Format(result.KnownMm)} mm | fark {MeasurementInput.Format(result.DifferenceMm)} mm ({percent})");
        return true;
    }
    internal bool RemoveComparison()
    {
        if(ComparisonListForVerification.SelectedIndex<0)return false;
        ComparisonsForVerification.RemoveAt(ComparisonListForVerification.SelectedIndex);return true;
    }
    internal void Calculate()
    {
        LastResultForVerification=null;
        try
        {
            if(FirstForVerification.SelectedItem is not Choice first || SecondForVerification.SelectedItem is not Choice second)
            { ResultForVerification.Text="İki kontrol noktası seçin.";return; }
            if(!MeasurementInput.TryParseMillimeters(KnownForVerification.Text,out var known))
            { ResultForVerification.Text="Bilinen kontrol mesafesini milimetre olarak girin.";return; }
            PhotoTraceMeasurementResult result;
            if(plane is null)result=PhotoTraceMeasurement.Compare(previewWidth,previewHeight,sourceWidth,sourceHeight,points[0],points[1],referenceLength,points[first.Index],points[second.Index],known);
            else
            {
                if(!double.IsFinite(known) || known<=0 || known>Geometry.MaxCoordinate)throw new ArgumentOutOfRangeException(nameof(known),"Kontrol mesafesi pozitif ve desteklenen aralıkta olmalıdır.");
                var start=points[first.Index];var end=points[second.Index];
                if(plane.Corners.Contains(start) && plane.Corners.Contains(end))throw new ArgumentException("Kontrol için iki referans köşesini birlikte kullanmayın.");
                var corrected=PhotoPlaneRectification.Convert(previewWidth,previewHeight,plane.Corners,plane.WidthMm,plane.HeightMm,[start,end]);
                var estimated=Geometry.Distance(corrected[0],corrected[1]);result=new PhotoTraceMeasurementResult(estimated,known,estimated-known);
            }
            LastResultForVerification=result;
            var percent=result.DifferenceMm/result.KnownMm*100;
            var relative=double.IsFinite(percent)
                ? $"Göreli fark (fark / girilen): {percent.ToString("G6",System.Globalization.CultureInfo.CurrentCulture)} %"
                : "Göreli fark bu sayısal aralıkta gösterilemiyor.";
            ResultForVerification.Text=$"Fotoğraftan tahmin: {MeasurementInput.Format(result.EstimatedMm)} mm\nGirilen mesafe: {MeasurementInput.Format(result.KnownMm)} mm\nFark (tahmin − girilen): {MeasurementInput.Format(result.DifferenceMm)} mm\n{relative}\n\nYalnız seçilen iki nokta karşılaştırıldı; ölçü veya üretim onayı verilmedi.";
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or OverflowException)
        { ResultForVerification.Text="Karşılaştırma yapılamadı: "+ex.Message; }
    }
    private sealed record Choice(int Index,string Label) { public override string ToString()=>Label; }
}
