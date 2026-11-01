using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Manual photo tracing; the result is an unapproved open draft, not a pattern.</summary>
internal sealed class PhotoTraceWindow : Window
{
    private readonly MainWindow workspace;
    private readonly MainWindow.PhotoTraceStamp stamp;
    private readonly BitmapSource image;
    private readonly int sourceWidth,sourceHeight;
    private readonly string? sourceSha256;
    private readonly int sourceByteLength;
    private readonly int sourceOrientation;
    private readonly TextBox xInput=new() { Width=60,MaxLength=24 },yInput=new() { Width=60,MaxLength=24 };
    internal long DraftRevision { get; private set; }
    internal bool HasDraft => CanvasForVerification.Points.Count>0 || LengthForVerification.Text.Length>0 || xInput.Text.Length>0 || yInput.Text.Length>0;
    internal PhotoTraceCanvas CanvasForVerification { get; } = new();
    internal TextBox LengthForVerification { get; } = new() { Width=85,MaxLength=32 };
    internal TextBlock StatusForVerification { get; } = new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,3) };
    internal CheckBox ReviewClosureForVerification { get; } = new() { Content="Kapatmayı incele",Foreground=DesignRequestWindow.Ink,Margin=new Thickness(4,3,0,2) };
    internal Func<string,bool>? ConfirmForVerification { get; set; }
    internal Action<Guid>? OpenClosureForVerification { get; set; }
    internal Func<BitmapSource,Point,Point,CancellationToken,IReadOnlyList<Point>?>? ComputePathForVerification { get; set; }
    private bool allowClose,closed;
    private CancellationTokenSource? pathCancellation;

    internal PhotoTraceWindow(MainWindow workspace,BitmapSource bitmap,MainWindow.PhotoTraceStamp stamp,int sourceWidth=0,int sourceHeight=0,string? sourceSha256=null,int sourceByteLength=0,int sourceOrientation=1)
    {
        this.workspace=workspace;this.stamp=stamp;image=bitmap;this.sourceWidth=sourceWidth==0 ? bitmap.PixelWidth : sourceWidth;this.sourceHeight=sourceHeight==0 ? bitmap.PixelHeight : sourceHeight;this.sourceSha256=sourceSha256;this.sourceByteLength=sourceByteLength;this.sourceOrientation=sourceOrientation;Owner=workspace;Title="Fotoğrafta elle taslak çiz";Width=820;Height=760;MinWidth=560;MinHeight=480;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("DesignRequestTheme.xaml",UriKind.Relative) });Background=DesignRequestWindow.BackgroundColor;Foreground=DesignRequestWindow.Ink;
        var root=new DockPanel { Margin=new Thickness(12) };Content=root;
        var actions=new WrapPanel { Margin=new Thickness(0,5,0,0) };DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        Button Action(string title,Action act) { var b=new Button { Content=title,Padding=new Thickness(9,5,9,5),Margin=new Thickness(0,0,6,4) };b.Click+=(_,_)=>act();actions.Children.Add(b);return b; }
        Action("Son noktayı kaldır",()=>CanvasForVerification.UndoPoint());Action("Noktaları temizle",()=>CanvasForVerification.ClearPoints());
        var apply=Action("CAD'e taslak ekle",TryApply);apply.Background=DesignRequestWindow.Accent;apply.Foreground=DesignRequestWindow.Surface;
        AutomationProperties.SetHelpText(apply,"Seçilen ve açıkça kabul edilen noktalarla açık çizgiyi CAD'e ekler; kalıp ve üretim uygunluğu doğrulanmaz.");
        Action("Kapat",Close);
        var intro=new TextBlock { Text="Önce fotoğrafta bilinen mesafenin iki ucunu, sonra çizginin en az iki noktasını seçin. Görüntü perspektifi ve dökümü ölçüleri değiştirebilir; sonuç açık bir taslaktır, doğrulanmış kalıp değildir.",TextWrapping=TextWrapping.Wrap,Foreground=DesignRequestWindow.Ink,Margin=new Thickness(0,0,0,4) };
        DockPanel.SetDock(intro,Dock.Top);root.Children.Add(intro);
        var input=new WrapPanel();DockPanel.SetDock(input,Dock.Top);root.Children.Add(input);
        input.Children.Add(new Label { Content="İki uç arası bilinen mesafe (mm)",Target=LengthForVerification,Foreground=DesignRequestWindow.Ink });AutomationProperties.SetName(LengthForVerification,"Bilinen referans mesafesi milimetre");input.Children.Add(LengthForVerification);
        input.Children.Add(new Label { Content="Piksel X",Target=xInput,Foreground=DesignRequestWindow.Ink });AutomationProperties.SetName(xInput,"Seçilecek piksel X");input.Children.Add(xInput);
        input.Children.Add(new Label { Content="Y",Target=yInput,Foreground=DesignRequestWindow.Ink });AutomationProperties.SetName(yInput,"Seçilecek piksel Y");input.Children.Add(yInput);
        var addPoint=new Button { Content="Nokta ekle",Padding=new Thickness(8,4,8,4),Margin=new Thickness(6,0,0,4) };input.Children.Add(addPoint);
        addPoint.Click+=(_,_)=> { if(!MeasurementInput.TryParseMillimeters(xInput.Text,out var x) || !MeasurementInput.TryParseMillimeters(yInput.Text,out var y) || !CanvasForVerification.TryAddPixelPoint(new Point(x,y)))StatusForVerification.Text="Piksel koordinatı geçersiz veya nokta eklenemedi."; };
        var viewActions=new WrapPanel { Margin=new Thickness(0,2,0,2) };DockPanel.SetDock(viewActions,Dock.Top);root.Children.Add(viewActions);
        Button ViewAction(string text,string help,Action action) { var button=new Button { Content=text,Padding=new Thickness(8,3,8,3),Margin=new Thickness(0,0,6,2) };AutomationProperties.SetHelpText(button,help);button.Click+=(_,_)=> { action();UpdateStatus(); };viewActions.Children.Add(button);return button; }
        ViewAction("Yakınlaştır","Son seçilen veya klavye imlecindeki noktaya yakınlaştırır.",()=>CanvasForVerification.ZoomAtCursor(2));
        ViewAction("Uzaklaştır","Son seçilen veya klavye imlecindeki noktadan uzaklaştırır.",()=>CanvasForVerification.ZoomAtCursor(0.5));
        ViewAction("Görüntüyü sığdır","Fotoğrafın tamamını pencereye sığdırır.",CanvasForVerification.ResetZoom);
        var suggest=new Button { Content="Kenar öner",Padding=new Thickness(8,3,8,3),Margin=new Thickness(0,0,6,2) };viewActions.Children.Add(suggest);
        AutomationProperties.SetHelpText(suggest,"Son çizgi noktasına yakın görüntü kontrastını arar; mavi halka bir öneridir ve nokta değişmez. Kumaş kenarı olduğu doğrulanmaz.");
        suggest.Click+=(_,_)=> { StatusForVerification.Text=CanvasForVerification.SuggestLastEdge() && CanvasForVerification.EdgeSuggestion is Point p
            ? $"Mavi halka önerisi: X {MeasurementInput.Format(p.X)}, Y {MeasurementInput.Format(p.Y)}. Görüntü kontrastı kumaş kenarı olmayabilir; uygula seçilene kadar nokta değişmez."
            : "Son çizgi noktasının yakınında yeterli kontrast bulunamadı; nokta değişmedi."; };
        var applySuggestion=new Button { Content="Öneriyi uygula",Padding=new Thickness(8,3,8,3),Margin=new Thickness(0,0,6,2) };viewActions.Children.Add(applySuggestion);
        AutomationProperties.SetHelpText(applySuggestion,"Mavi halka önerisini son çizgi noktasına uygular; referans uçlarını değiştirmez.");
        applySuggestion.Click+=(_,_)=> { if(!CanvasForVerification.ApplyEdgeSuggestion())StatusForVerification.Text="Uygulanabilir yeni kenar önerisi yok; nokta değişmedi."; };
        var pathActions=new WrapPanel { Margin=new Thickness(0,2,0,2) };DockPanel.SetDock(pathActions,Dock.Top);root.Children.Add(pathActions);
        Button PathAction(string text,string help,Action action) { var b=new Button { Content=text,Padding=new Thickness(8,3,8,3),Margin=new Thickness(0,0,6,2) };AutomationProperties.SetHelpText(b,help);b.Click+=(_,_)=>action();pathActions.Children.Add(b);return b; }
        PathAction("Yol öner","Son iki çizgi noktası arasında kontrast yolunu arar; kesikli mavi çizgi yalnız önizlemedir.",()=>_ = StartPathSuggestionAsync());
        PathAction("Yolu uygula","Kesikli öneriyi son iki nokta arasına ekler; önceden seçilen uçları korur.",()=> { if(!CanvasForVerification.ApplyPathSuggestion())StatusForVerification.Text="Uygulanabilir yol önerisi yok."; });
        PathAction("Yolu geri al","Son uygulanan yolu tek adımda kaldırır; başka nokta değişikliği yapılmamış olmalıdır.",()=> { if(!CanvasForVerification.UndoPath())StatusForVerification.Text="Geri alınacak son yol yok."; });
        PathAction("Aramayı iptal et","Süren yol aramasına iptal isteği gönderir.",CancelPathSuggestion);
        AutomationProperties.SetHelpText(ReviewClosureForVerification,"Taslak eklendikten sonra ayrı kapanış incelemesini açar. Kapalı parça otomatik eklenmez; fotoğraf geçmişi açık kaynakta kalır.");pathActions.Children.Add(ReviewClosureForVerification);
        AutomationProperties.SetName(StatusForVerification,"Fotoğraf taslağı durumu");AutomationProperties.SetLiveSetting(StatusForVerification,AutomationLiveSetting.Polite);
        DockPanel.SetDock(StatusForVerification,Dock.Top);root.Children.Add(StatusForVerification);
        CanvasForVerification.Image=image;CanvasForVerification.SetSourceAspect(this.sourceWidth,this.sourceHeight);
        CanvasForVerification.PointsChanged+=(_,_)=> { DraftRevision++;UpdateStatus(); };
        LengthForVerification.TextChanged+=(_,_)=>DraftRevision++;xInput.TextChanged+=(_,_)=>DraftRevision++;yInput.TextChanged+=(_,_)=>DraftRevision++;
        ReviewClosureForVerification.Checked+=(_,_)=>DraftRevision++;ReviewClosureForVerification.Unchecked+=(_,_)=>DraftRevision++;
        UpdateStatus();root.Children.Add(CanvasForVerification);
        Closing+=OnClosing;
        workspace.RegisterPhotoTrace(this);Closed+=(_,_)=> { closed=true;pathCancellation?.Cancel();workspace.UnregisterPhotoTrace(this); };
    }
    internal void CancelPathSuggestion()=>pathCancellation?.Cancel();
    internal async Task StartPathSuggestionAsync()
    {
        if(pathCancellation is not null) { StatusForVerification.Text="Yol araması zaten sürüyor.";return; }
        var points=CanvasForVerification.Points;
        if(points.Count<4 || CanvasForVerification.Image is not BitmapSource source) { StatusForVerification.Text="Önce referans uçlarını ve en az iki çizgi noktası seçin.";return; }
        var start=points[^2];var end=points[^1];var revision=DraftRevision;var cts=new CancellationTokenSource();pathCancellation=cts;CanvasForVerification.ClearPathSuggestion();
        StatusForVerification.Text="Son iki nokta arasında yol aranıyor; iptal edebilirsiniz.";
        try
        {
            var compute=ComputePathForVerification;
            var candidate=await Task.Run(()=>compute is null ? (PhotoEdgePath.TryFind(source,start,end,out var found,cts.Token) ? found : null) : compute(source,start,end,cts.Token),cts.Token);
            cts.Token.ThrowIfCancellationRequested();if(closed)return;
            if(DraftRevision!=revision) { StatusForVerification.Text="Taslak arama sırasında değişti; öneri kullanılmadı.";return; }
            StatusForVerification.Text=candidate is not null && CanvasForVerification.SetPathSuggestion(start,end,candidate)
                ? "Kesikli mavi yol yalnız öneridir; doku veya gölgeyi izleyebilir. İnceleyip isterseniz uygulayın."
                : "Uygun yol bulunamadı veya nokta sınırı aşıldı; taslak değişmedi.";
        }
        catch(OperationCanceledException) { if(!closed)StatusForVerification.Text="Yol araması iptal edildi; taslak değişmedi."; }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or System.IO.IOException or OverflowException)
        { if(!closed)StatusForVerification.Text="Yol önerisi hazırlanamadı: "+ex.Message; }
        finally { if(ReferenceEquals(pathCancellation,cts))pathCancellation=null;cts.Dispose(); }
    }
    private void UpdateStatus()
    {
        var n=CanvasForVerification.Points.Count;
        StatusForVerification.Text=$"Önizleme: {image.PixelWidth} × {image.PixelHeight} piksel; yakınlaştırma {MeasurementInput.Format(CanvasForVerification.Zoom)}×. "+(n<2 ? $"Referans ucu seçin: {n}/2." : $"Referans uçları hazır; çizgi noktası: {n-2}. İlk referans CAD'de 0,0 olur; Y aşağı artar.");
    }
    internal void TryApply()
    {
        try
        {
            var p=CanvasForVerification.Points;
            if(p.Count<4) { StatusForVerification.Text="İki referans ucu ve en az iki çizgi noktası seçin.";return; }
            var reviewClosure=ReviewClosureForVerification.IsChecked==true;
            if(reviewClosure && p.Count<5) { StatusForVerification.Text="Kapanışı incelemek için en az üç çizgi noktası seçin.";return; }
            if(string.IsNullOrEmpty(sourceSha256) || sourceByteLength<=0) { StatusForVerification.Text="Fotoğraf kaynak kimliği bulunamadı; taslak eklenmedi.";return; }
            if(!MeasurementInput.TryParseMillimeters(LengthForVerification.Text,out var mm) || mm<=0) { StatusForVerification.Text="Bilinen mesafeyi pozitif milimetre olarak girin.";return; }
            var referenceStart=new Point2(p[0].X,p[0].Y);var referenceEnd=new Point2(p[1].X,p[1].Y);
            var pixels=p.Skip(2).Select(q=>new Point2(q.X,q.Y)).ToList();
            var trace=PhotoTraceCalibration.Convert(image.PixelWidth,image.PixelHeight,sourceWidth,sourceHeight,referenceStart,referenceEnd,mm,pixels);
            var history=new PhotoTraceProvenance { AlgorithmVersion=1,SourceSha256=sourceSha256,SourceByteLength=sourceByteLength,SourceOrientation=sourceOrientation,SourceWidth=sourceWidth,SourceHeight=sourceHeight,PreviewWidth=image.PixelWidth,PreviewHeight=image.PixelHeight,ReferenceStart=referenceStart,ReferenceEnd=referenceEnd,ReferenceLengthMm=mm,TracePixels=pixels,GeneratedPoints=trace.ToList() };
            LargeOpenPathValidator.Validate(trace);
            if(workspace.PhotoTraceProblem(stamp) is { } stale) { StatusForVerification.Text=stale;return; }
            var distance=Math.Sqrt(Math.Pow(p[1].X-p[0].X,2)+Math.Pow(p[1].Y-p[0].Y,2));
            var pendingSuggestion=CanvasForVerification.PathSuggestion is not null || CanvasForVerification.EdgeSuggestion is not null
                ? "\n\nUygulanmamış mavi öneri çizgiye dahil edilmeyecek." : "";
            var closureNotice=reviewClosure ? "\n\nArdından kapanış incelemesi açılacak. Kapalı parça ayrıca onaylanır; fotoğraf geçmişi açık kaynakta kalır." : "";
            var question=$"Referans uçları önizlemede {MeasurementInput.Format(distance)} piksel aralığında. Kısa bir referans, nokta seçme hatasını büyütebilir; mümkünse daha uzun bir referans seçin.\n\nSeçilen ve kabul edilen noktalardan oluşan açık çizgi CAD'e taslak olarak eklenecek. Perspektif, gerçek beden, görünmeyen parçalar ve üretim uygunluğu doğrulanmadı. Fotoğraf özeti, boyutlar, işaretler ve mesafe proje biçimi 8 ile kaydedilir; fotoğrafın kendisi eklenmez. Önceki sürümler dosyayı açamaz.{pendingSuggestion}{closureNotice} Devam edilsin mi?";
            var approvedRevision=DraftRevision;
            var confirmed=ConfirmForVerification?.Invoke(question) ?? (MessageBox.Show(this,question,"Taslak çizgi",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes);
            if(!confirmed) { StatusForVerification.Text="İşlem iptal edildi; proje değiştirilmedi.";return; }
            if(DraftRevision!=approvedRevision) { StatusForVerification.Text="Fotoğraf noktaları veya değerleri değişti; taslak eklenmedi.";return; }
            if(!workspace.ApplyPhotoTrace(stamp,trace,history,out var addedId)) { StatusForVerification.Text=workspace.PhotoTraceProblem(stamp) ?? "Taslak eklenemedi.";return; }
            allowClose=true;Close();
            if(reviewClosure && addedId is Guid id) { if(OpenClosureForVerification is { } open)open(id);else workspace.OpenPhotoTraceClosure(id); }
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or System.IO.InvalidDataException or OverflowException)
        { StatusForVerification.Text="Taslak eklenemedi: "+ex.Message; }
    }
    private void OnClosing(object? sender,CancelEventArgs e)
    {
        if(!allowClose && HasDraft)
        {
            var revision=DraftRevision;
            var accepted=MessageBox.Show(this,"İşaretlenmiş noktalar ve girilen değerler kaybolacak. Kapatılsın mı?","Taslak çizgi",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes;
            if(!accepted || revision!=DraftRevision) { e.Cancel=true;if(accepted)StatusForVerification.Text="Taslak değişti; pencere kapatılmadı."; }
        }
    }
}
