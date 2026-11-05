using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Collections.ObjectModel;
using System.Windows.Data;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using TekstilCAD.FileFormats;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Read-only comparison of a detached photo trace snapshot.</summary>
internal sealed class PhotoTraceCheckWindow : Window
{
    private readonly List<Point2> points;
    private readonly int originalPointCount;
    private const int MaximumControlPoints=100;
    private readonly int previewWidth,previewHeight,sourceWidth,sourceHeight;
    private readonly double referenceLength;
    private readonly PhotoPlaneCalibration? plane;
    private readonly string? sourceSha256;private readonly int sourceByteLength,sourceOrientation;
    private double? assumedPixelError;
    internal ComboBox FirstForVerification { get; } = new() { MinWidth=220,Margin=new Thickness(0,3,0,6) };
    internal ComboBox SecondForVerification { get; } = new() { MinWidth=220,Margin=new Thickness(0,3,0,6) };
    internal TextBox KnownForVerification { get; } = new() { MinWidth=120,MaxLength=32,Margin=new Thickness(0,3,0,6) };
    internal TextBox PixelUncertaintyForVerification { get; } = new() { MinWidth=120,MaxLength=24,Margin=new Thickness(0,3,0,6) };
    private string sensitivitySummary="";
    internal TextBox ResultForVerification { get; } = new() { IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,MinHeight=75,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    internal PhotoTraceMeasurementResult? LastResultForVerification { get; private set; }
    private sealed record Comparison(int First,int Second,bool AlongTrace,PhotoTraceMeasurementResult Result,double? AssumedPixelError,string Sensitivity,string Display)
    { public override string ToString()=>Display; }
    private readonly ObservableCollection<Comparison> comparisons=[];
    private long reportRevision;
    private bool reportClosed;
    internal IReadOnlyList<string> ComparisonsForVerification=>comparisons.Select(c=>c.Display).ToArray();
    internal TextBlock ReportStatusForVerification { get; } = new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,4) };
    internal TextBlock ReportDisclosureForVerification { get; } = new() { Text="Rapor; fotoğrafın SHA-256 özeti ve boyutları, tüm taslak ve geçici kontrol noktalarının koordinatları, kalibrasyon ile girilen ölçüleri içerir. Fotoğrafın kendisi eklenmez.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,6) };
    internal ListBox ComparisonListForVerification { get; } = new() { MinHeight=75,MaxHeight=160,Margin=new Thickness(0,4,0,4) };
    internal PhotoMeasurementPreview PreviewForVerification { get; }
    internal TextBlock PickStatusForVerification { get; } = new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,4),Text="Fotoğrafta sol tık: 1; sağ tık: 2. Yakın noktalar için listeyi kullanın." };
    internal TextBox ControlXForVerification { get; } = new() { Width=90,MaxLength=24,Margin=new Thickness(0,2,8,2) };
    internal TextBox ControlYForVerification { get; } = new() { Width=90,MaxLength=24,Margin=new Thickness(0,2,8,2) };
    internal CheckBox AddControlModeForVerification { get; } = new() { Content="Fotoğrafa tıklayarak kontrol noktası ekle",Foreground=StudioTheme.Text,Margin=new Thickness(0,4,0,4) };
    internal CheckBox AlongTraceForVerification { get; } = new() { Content="İki çizgi noktası arasında çizgi boyunca ölç",Foreground=StudioTheme.Text,Margin=new Thickness(0,4,0,4) };

    internal PhotoTraceCheckWindow(Window owner,BitmapSource image,int sourceWidth,int sourceHeight,double referenceLength,IReadOnlyList<Point> snapshot,PhotoPlaneCalibration? plane=null,string? sourceSha256=null,int sourceByteLength=0,int sourceOrientation=1)
    {
        ArgumentNullException.ThrowIfNull(owner);ArgumentNullException.ThrowIfNull(snapshot);ArgumentNullException.ThrowIfNull(image);
        comparisons.CollectionChanged+=(_,_)=>reportRevision++;Closed+=(_,_)=>reportClosed=true;
        if(snapshot.Count<3 || snapshot.Count>PhotoTraceCanvas.MaximumPoints)throw new ArgumentOutOfRangeException(nameof(snapshot));
        previewWidth=image.PixelWidth;previewHeight=image.PixelHeight;this.sourceWidth=sourceWidth;this.sourceHeight=sourceHeight;this.referenceLength=referenceLength;this.plane=plane?.Clone();this.sourceSha256=sourceSha256;this.sourceByteLength=sourceByteLength;this.sourceOrientation=sourceOrientation;
        points=snapshot.Select(p=>new Point2(p.X,p.Y)).ToList();originalPointCount=points.Count;
        PreviewForVerification=new PhotoMeasurementPreview(image,points,sourceWidth,sourceHeight);
        Owner=owner;Title="Fotoğrafta ölçü karşılaştır";Width=610;Height=580;MinWidth=460;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel { Margin=new Thickness(0,8,0,0) };DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var calculate=new Button { Content="Karşılaştır",Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,0) };
        var close=new Button { Content="Kapat",Padding=new Thickness(12,7,12,7),IsCancel=true };actions.Children.Add(calculate);actions.Children.Add(close);
        var scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };root.Children.Add(scroll);
        var body=new StackPanel();scroll.Content=body;
        body.Children.Add(new TextBlock { Text="Fotoğraf taslağının bu pencere açıldığındaki noktaları kullanılır. İki referans köşesini birlikte seçmeyin. Başka bir gerçek mesafeyi bağımsız olarak ölçüp girin; düz mesafe veya çizgi boyunca ölçüm seçebilirsiniz.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) });
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
        AutomationProperties.SetHelpText(AddControlModeForVerification,"Açıkken sol tık fotoğrafa yalnız bu pencere için kontrol noktası ekler. Çizim değişmez. Kapatınca sol tık tekrar birinci ucu seçer.");
        AddControlModeForVerification.Checked+=(_,_)=>PreviewForVerification.AddControlPointMode=true;AddControlModeForVerification.Unchecked+=(_,_)=>PreviewForVerification.AddControlPointMode=false;body.Children.Add(AddControlModeForVerification);
        var controlCoordinates=new WrapPanel();controlCoordinates.Children.Add(new Label { Content="Kontrol X (piksel)",Target=ControlXForVerification,Foreground=StudioTheme.Text });controlCoordinates.Children.Add(ControlXForVerification);
        controlCoordinates.Children.Add(new Label { Content="Y",Target=ControlYForVerification,Foreground=StudioTheme.Text });controlCoordinates.Children.Add(ControlYForVerification);
        AutomationProperties.SetName(ControlXForVerification,"Yeni kontrol noktası piksel X");AutomationProperties.SetName(ControlYForVerification,"Yeni kontrol noktası piksel Y");
        var addControl=new Button { Content="Kontrol noktası ekle",Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,2,0,2) };
        addControl.Click+=(_,_)=> { if(!MeasurementInput.TryParseMillimeters(ControlXForVerification.Text,out var x) || !MeasurementInput.TryParseMillimeters(ControlYForVerification.Text,out var y)) { PickStatusForVerification.Text="Piksel koordinatlarını girin.";return; } AddControlPoint(new Point2(x,y)); };controlCoordinates.Children.Add(addControl);body.Children.Add(controlCoordinates);
        body.Children.Add(new TextBlock { Text="Ek noktalar yalnız bu ölçüm penceresinde kalır; çizgiye ve projeye eklenmez. En fazla 100 nokta eklenebilir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,5) });
        body.Children.Add(AlongTraceForVerification);
        body.Children.Add(new TextBlock { Text="Bu seçenek iki çizgi noktası arasındaki sıralı düz kenarları toplar; kapanış kenarını ve geçici kontrol noktalarını içermez. Seçili noktalar arasında farklı bir rota seçmez. Kıvrımlı gerçek kenarı ve nokta seçim hatasını doğrulamaz.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,5) });
        body.Children.Add(new Label { Content="Bağımsız bilinen mesafe (mm)",Target=KnownForVerification,Foreground=StudioTheme.Text });body.Children.Add(KnownForVerification);
        if(plane is null)
        {
            body.Children.Add(new Label { Content="Kabul edilen en çok piksel sapması · her eksen (isteğe bağlı)",Target=PixelUncertaintyForVerification,Foreground=StudioTheme.Text });
            AutomationProperties.SetName(PixelUncertaintyForVerification,"Her uç ve eksen için kabul edilen en çok piksel sapması");body.Children.Add(PixelUncertaintyForVerification);
            body.Children.Add(new TextBlock { Text="Boş bırakılabilir. Değer girilirse dört ucun her birinin X ve Y seçiminde en çok bu kadar önizleme pikseli sapma olduğu varsayılır; referansın mm değeri kesin kabul edilir. Yaklaşık senaryo aralığı perspektif, lens, kumaş, fiziksel ölçüm veya diğer hataları kapsamaz.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6) });
        }
        body.Children.Add(new TextBlock { Text="Sonuç çizimi veya projeyi değiştirmez. İsterseniz listeyi ayrı inceleme raporu olarak kaydedebilirsiniz. Uyum tek başına perspektifi, diğer ölçüleri veya üretim uygunluğunu doğrulamaz.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,4,0,8) });
        body.Children.Add(ResultForVerification);
        body.Children.Add(new TextBlock { Text="Bu penceredeki kontrol listesi",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,0) });
        body.Children.Add(new TextBlock { Text="Farklı nokta çiftlerini ve bağımsız ölçüleri karşılaştırıp listeye ekleyin. Kaydedilmezse liste pencere kapanınca silinir. Eşleşmeler diğer bölgeleri veya üretime uygunluğu doğrulamaz.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,4) });
        ComparisonListForVerification.ItemsSource=comparisons;ComparisonListForVerification.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        ScrollViewer.SetHorizontalScrollBarVisibility(ComparisonListForVerification,ScrollBarVisibility.Disabled);
        var comparisonText=new FrameworkElementFactory(typeof(TextBlock));comparisonText.SetBinding(TextBlock.TextProperty,new Binding(nameof(Comparison.Display)));comparisonText.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);
        ComparisonListForVerification.ItemTemplate=new DataTemplate { VisualTree=comparisonText };
        AutomationProperties.SetName(ComparisonListForVerification,"Fotoğraf ölçü karşılaştırmaları");body.Children.Add(ComparisonListForVerification);
        AutomationProperties.SetName(ReportDisclosureForVerification,"Raporun içerdiği bilgiler");body.Children.Add(ReportDisclosureForVerification);
        var listActions=new WrapPanel();
        var addComparison=new Button { Content="Bu karşılaştırmayı listeye ekle",Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,0,6,4) };
        var removeComparison=new Button { Content="Seçileni kaldır",Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,0,6,4) };
        var clearComparisons=new Button { Content="Listeyi temizle",Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,0,6,4) };
        var saveReport=new Button { Content="İnceleme raporu kaydet…",Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,0,6,4) };
        AutomationProperties.SetHelpText(saveReport,"Kaynak özeti, tüm nokta koordinatları, kalibrasyon ve ölçüleri yeni JSON dosyasına kaydeder; fotoğrafı içermez ve üretim onayı değildir.");
        saveReport.Click+=(_,_)=>SelectAndSaveReport(()=> { var dialog=new SaveFileDialog { Filter="Fotoğraf ölçü incelemesi (*.json)|*.json",DefaultExt=".json",AddExtension=true,OverwritePrompt=false }; return dialog.ShowDialog(this)==true?dialog.FileName:null; });
        addComparison.Click+=(_,_)=> { if(!AddComparison())ResultForVerification.BringIntoView(); };removeComparison.Click+=(_,_)=>RemoveComparison();clearComparisons.Click+=(_,_)=>comparisons.Clear();
        listActions.Children.Add(addComparison);listActions.Children.Add(removeComparison);listActions.Children.Add(clearComparisons);listActions.Children.Add(saveReport);body.Children.Add(listActions);
        AutomationProperties.SetName(ReportStatusForVerification,"Fotoğraf inceleme raporu durumu");AutomationProperties.SetLiveSetting(ReportStatusForVerification,AutomationLiveSetting.Polite);body.Children.Add(ReportStatusForVerification);
        var referenceCount=plane is null ? 2 : 4;
        var options=points.Select((p,i)=>new Choice(i,$"{i+1} · {(i<referenceCount ? "referans" : "çizgi")} · X {MeasurementInput.Format(p.X)}, Y {MeasurementInput.Format(p.Y)}")).ToArray();
        FirstForVerification.ItemsSource=options;SecondForVerification.ItemsSource=options;
        AutomationProperties.SetName(FirstForVerification,"Kontrol başlangıç noktası");AutomationProperties.SetName(SecondForVerification,"Kontrol bitiş noktası");
        AutomationProperties.SetName(KnownForVerification,"Bağımsız bilinen kontrol mesafesi milimetre");AutomationProperties.SetName(ResultForVerification,"Ölçü karşılaştırması sonucu");AutomationProperties.SetLiveSetting(ResultForVerification,AutomationLiveSetting.Polite);
        void SelectionChanged() { ClearResult();PreviewForVerification.Select((FirstForVerification.SelectedItem as Choice)?.Index,(SecondForVerification.SelectedItem as Choice)?.Index); }
        FirstForVerification.SelectionChanged+=(_,_)=>SelectionChanged();SecondForVerification.SelectionChanged+=(_,_)=>SelectionChanged();KnownForVerification.TextChanged+=(_,_)=>ClearResult();PixelUncertaintyForVerification.TextChanged+=(_,_)=>ClearResult();
        AlongTraceForVerification.Checked+=(_,_)=>ClearResult();AlongTraceForVerification.Unchecked+=(_,_)=>ClearResult();
        PreviewForVerification.PointChosen+=(index,second)=> { if(second)SecondForVerification.SelectedIndex=index;else FirstForVerification.SelectedIndex=index; };
        PreviewForVerification.ControlPointChosen+=point=>AddControlPoint(point);
        PreviewForVerification.PickMessage+=message=>PickStatusForVerification.Text=message;
        calculate.Click+=(_,_)=> { Calculate();ResultForVerification.BringIntoView(); };close.Click+=(_,_)=>Close();
    }
    internal bool AddControlPoint(Point2 point)
    {
        if(points.Count-originalPointCount>=MaximumControlPoints) { PickStatusForVerification.Text="En fazla 100 geçici kontrol noktası eklenebilir.";return false; }
        if(!point.IsFinite || point.X<0 || point.Y<0 || point.X>previewWidth-1 || point.Y>previewHeight-1)
        { PickStatusForVerification.Text="Kontrol noktası fotoğrafın piksel sınırları içinde olmalıdır.";return false; }
        if(points.Contains(point)) { PickStatusForVerification.Text="Bu pikselde zaten bir nokta var; ilgili ucu listeden seçin.";return false; }
        var first=FirstForVerification.SelectedIndex;var second=SecondForVerification.SelectedIndex;
        var options=((Choice[])FirstForVerification.ItemsSource).ToList();var index=points.Count;
        options.Add(new Choice(index,$"{index+1} · kontrol · X {MeasurementInput.Format(point.X)}, Y {MeasurementInput.Format(point.Y)}"));
        points.Add(point);reportRevision++;PreviewForVerification.AddControlPoint(point);var updated=options.ToArray();FirstForVerification.ItemsSource=updated;SecondForVerification.ItemsSource=updated;
        FirstForVerification.SelectedIndex=first;SecondForVerification.SelectedIndex=second;
        PickStatusForVerification.Text=$"{index+1}. geçici kontrol noktası eklendi; çizim ve proje değişmedi. Ölçüm için uç listesinden seçin.";return true;
    }
    private void ClearResult() { LastResultForVerification=null;assumedPixelError=null;sensitivitySummary="";ResultForVerification.Text=""; }
    internal bool AddComparison()
    {
        if(comparisons.Count>=100) { ResultForVerification.Text="Liste 100 karşılaştırmayla sınırlıdır; yeni eklemeden önce bir satırı kaldırın.";LastResultForVerification=null;return false; }
        Calculate();
        if(LastResultForVerification is not { } result || FirstForVerification.SelectedItem is not Choice first || SecondForVerification.SelectedItem is not Choice second)return false;
        var relative=result.DifferenceMm/result.KnownMm*100;
        var percent=double.IsFinite(relative) ? relative.ToString("G6",System.Globalization.CultureInfo.CurrentCulture)+" %" : "gösterilemiyor";
        var along=AlongTraceForVerification.IsChecked==true;
        var display=$"{(along ? "Çizgi boyunca" : "Düz mesafe")} · {first.Label} → {second.Label} | tahmin {MeasurementInput.Format(result.EstimatedMm)} mm | ölçülen {MeasurementInput.Format(result.KnownMm)} mm | fark {MeasurementInput.Format(result.DifferenceMm)} mm ({percent})"+(sensitivitySummary.Length>0 ? " | "+sensitivitySummary : "");
        comparisons.Add(new(first.Index,second.Index,along,result,assumedPixelError,sensitivitySummary,display));
        return true;
    }
    internal bool RemoveComparison()
    {
        if(ComparisonListForVerification.SelectedIndex<0)return false;
        comparisons.RemoveAt(ComparisonListForVerification.SelectedIndex);return true;
    }
    internal void Calculate()
    {
        LastResultForVerification=null;assumedPixelError=null;sensitivitySummary="";
        try
        {
            if(FirstForVerification.SelectedItem is not Choice first || SecondForVerification.SelectedItem is not Choice second)
            { ResultForVerification.Text="İki kontrol noktası seçin.";return; }
            if(!MeasurementInput.TryParseMillimeters(KnownForVerification.Text,out var known))
            { ResultForVerification.Text="Bilinen kontrol mesafesini milimetre olarak girin.";return; }
            PhotoTraceMeasurementResult result;
            var along=AlongTraceForVerification.IsChecked==true;
            if(along)
            {
                var referenceCount=plane is null ? 2 : 4;
                if(first.Index<referenceCount || second.Index<referenceCount || first.Index>=originalPointCount || second.Index>=originalPointCount || first.Index==second.Index)
                    throw new ArgumentException("Çizgi boyunca ölçmek için iki farklı çizgi noktası seçin; referans ve geçici kontrol noktaları kullanılamaz.");
                if(!string.IsNullOrWhiteSpace(PixelUncertaintyForVerification.Text))throw new ArgumentException("Çizgi boyunca ölçüm için piksel duyarlılık alanını boş bırakın; bu aralık yalnız düz mesafeye uygulanır.");
                var low=Math.Min(first.Index,second.Index);var high=Math.Max(first.Index,second.Index);
                var trace=points.GetRange(low,high-low+1);
                result=plane is null ? PhotoTraceMeasurement.ComparePolyline(previewWidth,previewHeight,sourceWidth,sourceHeight,points[0],points[1],referenceLength,trace,known)
                    : PhotoTraceMeasurement.ComparePlanePolyline(previewWidth,previewHeight,plane,trace,known);
            }
            else if(plane is null)result=PhotoTraceMeasurement.Compare(previewWidth,previewHeight,sourceWidth,sourceHeight,points[0],points[1],referenceLength,points[first.Index],points[second.Index],known);
            else
            {
                if(!double.IsFinite(known) || known<=0 || known>Geometry.MaxCoordinate)throw new ArgumentOutOfRangeException(nameof(known),"Kontrol mesafesi pozitif ve desteklenen aralıkta olmalıdır.");
                var start=points[first.Index];var end=points[second.Index];
                if(plane.Corners.Contains(start) && plane.Corners.Contains(end))throw new ArgumentException("Kontrol için iki referans köşesini birlikte kullanmayın.");
                var corrected=PhotoPlaneRectification.Convert(previewWidth,previewHeight,plane.Corners,plane.WidthMm,plane.HeightMm,[start,end]);
                var estimated=Geometry.Distance(corrected[0],corrected[1]);result=new PhotoTraceMeasurementResult(estimated,known,estimated-known);
            }
            if(!along && plane is null && !string.IsNullOrWhiteSpace(PixelUncertaintyForVerification.Text))
            {
                if(!MeasurementInput.TryParseMillimeters(PixelUncertaintyForVerification.Text,out var assumedPixels) || assumedPixels<0 || assumedPixels>1000)
                    throw new ArgumentException("Piksel sapmasını 0–1000 arasında girin veya alanı boş bırakın.");
                assumedPixelError=assumedPixels;
                var sensitivity=PhotoSelectionSensitivity.Estimate(previewWidth,previewHeight,sourceWidth,sourceHeight,points[0],points[1],referenceLength,points[first.Index],points[second.Index],assumedPixels);
                static string Short(double value)=>value.ToString("G6",System.Globalization.CultureInfo.CurrentCulture);
                var upper=sensitivity.UpperMm is double max ? Short(max)+" mm" : "sonlu üst sınır yok";
                sensitivitySummary=$"Her uç ve eksende ±{Short(assumedPixels)} önizleme pikseli varsayımıyla yaklaşık aralık: {Short(sensitivity.LowerMm)} mm – {upper}. Referans mm kesin kabul edildi; perspektif ve diğer hatalar dahil değil.";
            }
            LastResultForVerification=result;
            var percent=result.DifferenceMm/result.KnownMm*100;
            var relative=double.IsFinite(percent)
                ? $"Göreli fark (fark / girilen): {percent.ToString("G6",System.Globalization.CultureInfo.CurrentCulture)} %"
                : "Göreli fark bu sayısal aralıkta gösterilemiyor.";
            ResultForVerification.Text=$"{(along ? "Çizgi boyunca" : "Düz mesafe")} fotoğraf tahmini: {MeasurementInput.Format(result.EstimatedMm)} mm\nGirilen mesafe: {MeasurementInput.Format(result.KnownMm)} mm\nFark (tahmin − girilen): {MeasurementInput.Format(result.DifferenceMm)} mm\n{relative}"+(sensitivitySummary.Length>0 ? "\n\n"+sensitivitySummary : "")+"\n\nYalnız seçilen aralık karşılaştırıldı; ölçü veya üretim onayı verilmedi.";
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or OverflowException)
        { ResultForVerification.Text="Karşılaştırma yapılamadı: "+ex.Message; }
    }
    // A separate, user-requested snapshot; this is not a project or approval.
    internal bool SelectAndSaveReport(Func<string?> choosePath)
    {
        if(reportClosed)return false;
        if(comparisons.Count==0) { ReportStatusForVerification.Text="Rapor için önce bir karşılaştırmayı listeye ekleyin.";return false; }
        var revision=reportRevision;var path=choosePath();
        if(path is null)return false;
        if(reportClosed || reportRevision!=revision) { if(!reportClosed)ReportStatusForVerification.Text="Rapor seçimi sırasında liste veya noktalar değişti; dosya kaydedilmedi.";return false; }
        return TrySaveReport(path);
    }

    internal bool TrySaveReport(string path)
    {
        try
        {
            if(comparisons.Count==0)throw new InvalidOperationException("Önce karşılaştırmaları listeye ekleyin.");
            if(string.IsNullOrEmpty(sourceSha256) || sourceByteLength<=0)throw new InvalidOperationException("Fotoğraf kaynak özeti bulunamadı; rapor kaydedilmedi.");
            var version=comparisons.Any(c=>c.AlongTrace) ? 2 : 1;
            var report=new {
                format="tekstilcad.photo-measurement-review",version,coordinateUnit="preview-pixel",lengthUnit="mm",
                notice=version==1 ? "Yalnız seçilen nokta çiftleri karşılaştırılmıştır. Ölçüm bağımsızlığı, fotoğraf, kalıp ve üretim uygunluğu doğrulanmamıştır." : "Yalnız seçilen aralıklar karşılaştırılmıştır. Ölçüm bağımsızlığı, fotoğraf, kalıp ve üretim uygunluğu doğrulanmamıştır.",
                source=new { sha256=sourceSha256,byteLength=sourceByteLength,orientation=sourceOrientation,sourceWidth,sourceHeight,previewWidth,previewHeight },
                reference=new { mode=plane is null ? "single-distance" : "four-corner-plane",lengthMm=referenceLength,
                    planeWidthMm=plane?.WidthMm,planeHeightMm=plane?.HeightMm,
                    points=points.Take(plane is null ? 2 : 4).Select(p=>new { x=p.X,y=p.Y }).ToArray() },
                points=points.Select((p,i)=>new { index=i+1,kind=i<(plane is null ? 2 : 4) ? "reference" : i<originalPointCount ? "draft" : "temporary-control",x=p.X,y=p.Y }).ToArray(),
                comparisons=comparisons.Select(c=>version==1 ? (object)new { firstIndex=c.First+1,secondIndex=c.Second+1,
                    estimatedMm=c.Result.EstimatedMm,knownMm=c.Result.KnownMm,differenceMm=c.Result.DifferenceMm,
                    assumedPixelError=c.AssumedPixelError,sensitivity=c.Sensitivity } : new { firstIndex=c.First+1,secondIndex=c.Second+1,
                    kind=c.AlongTrace ? "trace-polyline" : "straight",pointIndices=c.AlongTrace ? Enumerable.Range(Math.Min(c.First,c.Second),Math.Abs(c.First-c.Second)+1).Select(i=>i+1).ToArray() : new[] { c.First+1,c.Second+1 },
                    estimatedMm=c.Result.EstimatedMm,knownMm=c.Result.KnownMm,differenceMm=c.Result.DifferenceMm,
                    assumedPixelError=c.AssumedPixelError,sensitivity=c.Sensitivity }).ToArray() };
            var text=JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented=true });
            var full=Path.GetFullPath(path);ProjectStorage.AtomicWriteIfUnchanged(full,text,new ProjectFileVersion(full,null));
            ReportStatusForVerification.Text="İnceleme raporu yeni dosyaya kaydedildi. Bu rapor üretim onayı değildir.";return true;
        }
        catch(ProjectConflictException)
        { ReportStatusForVerification.Text="Rapor kaydedilmedi: Bu ad zaten var; farklı bir dosya adı seçin. Mevcut dosya ve liste korundu.";return false; }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException)
        { ReportStatusForVerification.Text="Rapor kaydedilmedi: "+ex.Message;return false; }
    }
    private sealed record Choice(int Index,string Label) { public override string ToString()=>Label; }
}
