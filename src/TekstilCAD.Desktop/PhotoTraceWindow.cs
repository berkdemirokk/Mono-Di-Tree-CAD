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
    internal Func<string,bool>? ConfirmForVerification { get; set; }
    private bool allowClose;

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
        AutomationProperties.SetHelpText(apply,"Elle işaretlenen açık çizgiyi onaydan sonra CAD'e ekler; kalıp ve üretim uygunluğu doğrulanmaz.");
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
        AutomationProperties.SetName(StatusForVerification,"Fotoğraf taslağı durumu");AutomationProperties.SetLiveSetting(StatusForVerification,AutomationLiveSetting.Polite);
        DockPanel.SetDock(StatusForVerification,Dock.Top);root.Children.Add(StatusForVerification);
        CanvasForVerification.Image=image;CanvasForVerification.SetSourceAspect(this.sourceWidth,this.sourceHeight);
        CanvasForVerification.PointsChanged+=(_,_)=> { DraftRevision++;UpdateStatus(); };
        LengthForVerification.TextChanged+=(_,_)=>DraftRevision++;xInput.TextChanged+=(_,_)=>DraftRevision++;yInput.TextChanged+=(_,_)=>DraftRevision++;
        UpdateStatus();root.Children.Add(CanvasForVerification);
        Closing+=OnClosing;
        workspace.RegisterPhotoTrace(this);Closed+=(_,_)=>workspace.UnregisterPhotoTrace(this);
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
            if(string.IsNullOrEmpty(sourceSha256) || sourceByteLength<=0) { StatusForVerification.Text="Fotoğraf kaynak kimliği bulunamadı; taslak eklenmedi.";return; }
            if(!MeasurementInput.TryParseMillimeters(LengthForVerification.Text,out var mm) || mm<=0) { StatusForVerification.Text="Bilinen mesafeyi pozitif milimetre olarak girin.";return; }
            var referenceStart=new Point2(p[0].X,p[0].Y);var referenceEnd=new Point2(p[1].X,p[1].Y);
            var pixels=p.Skip(2).Select(q=>new Point2(q.X,q.Y)).ToList();
            var trace=PhotoTraceCalibration.Convert(image.PixelWidth,image.PixelHeight,sourceWidth,sourceHeight,referenceStart,referenceEnd,mm,pixels);
            var history=new PhotoTraceProvenance { AlgorithmVersion=1,SourceSha256=sourceSha256,SourceByteLength=sourceByteLength,SourceOrientation=sourceOrientation,SourceWidth=sourceWidth,SourceHeight=sourceHeight,PreviewWidth=image.PixelWidth,PreviewHeight=image.PixelHeight,ReferenceStart=referenceStart,ReferenceEnd=referenceEnd,ReferenceLengthMm=mm,TracePixels=pixels,GeneratedPoints=trace.ToList() };
            LargeOpenPathValidator.Validate(trace);
            if(workspace.PhotoTraceProblem(stamp) is { } stale) { StatusForVerification.Text=stale;return; }
            var distance=Math.Sqrt(Math.Pow(p[1].X-p[0].X,2)+Math.Pow(p[1].Y-p[0].Y,2));
            var question=$"Referans uçları önizlemede {MeasurementInput.Format(distance)} piksel aralığında. Kısa bir referans, nokta seçme hatasını büyütebilir; mümkünse daha uzun bir referans seçin.\n\nElle işaretlenen açık çizgi CAD'e taslak olarak eklenecek. Perspektif, gerçek beden, görünmeyen parçalar ve üretim uygunluğu doğrulanmadı. Fotoğraf özeti, boyutlar, işaretler ve mesafe proje biçimi 8 ile kaydedilir; fotoğrafın kendisi eklenmez. Önceki sürümler dosyayı açamaz. Devam edilsin mi?";
            var approvedRevision=DraftRevision;
            var confirmed=ConfirmForVerification?.Invoke(question) ?? (MessageBox.Show(this,question,"Taslak çizgi",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes);
            if(!confirmed) { StatusForVerification.Text="İşlem iptal edildi; proje değiştirilmedi.";return; }
            if(DraftRevision!=approvedRevision) { StatusForVerification.Text="Fotoğraf noktaları veya değerleri değişti; taslak eklenmedi.";return; }
            if(!workspace.ApplyPhotoTrace(stamp,trace,history)) { StatusForVerification.Text=workspace.PhotoTraceProblem(stamp) ?? "Taslak eklenemedi.";return; }
            allowClose=true;Close();
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
