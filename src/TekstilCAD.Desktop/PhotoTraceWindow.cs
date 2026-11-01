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
    internal PhotoTraceCanvas CanvasForVerification { get; } = new();
    internal TextBox LengthForVerification { get; } = new() { Width=85,MaxLength=32 };
    internal TextBlock StatusForVerification { get; } = new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,3) };
    internal Func<string,bool>? ConfirmForVerification { get; set; }
    private bool allowClose;

    internal PhotoTraceWindow(MainWindow workspace,BitmapSource bitmap,MainWindow.PhotoTraceStamp stamp)
    {
        this.workspace=workspace;this.stamp=stamp;image=bitmap;Owner=workspace;Title="Fotoğrafta elle taslak çiz";Width=820;Height=760;MinWidth=560;MinHeight=480;
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
        var xInput=new TextBox { Width=60,MaxLength=24 };var yInput=new TextBox { Width=60,MaxLength=24 };
        input.Children.Add(new Label { Content="Piksel X",Target=xInput,Foreground=DesignRequestWindow.Ink });AutomationProperties.SetName(xInput,"Seçilecek piksel X");input.Children.Add(xInput);
        input.Children.Add(new Label { Content="Y",Target=yInput,Foreground=DesignRequestWindow.Ink });AutomationProperties.SetName(yInput,"Seçilecek piksel Y");input.Children.Add(yInput);
        var addPoint=new Button { Content="Nokta ekle",Padding=new Thickness(8,4,8,4),Margin=new Thickness(6,0,0,4) };input.Children.Add(addPoint);
        addPoint.Click+=(_,_)=> { if(!MeasurementInput.TryParseMillimeters(xInput.Text,out var x) || !MeasurementInput.TryParseMillimeters(yInput.Text,out var y) || !CanvasForVerification.TryAddPixelPoint(new Point(x,y)))StatusForVerification.Text="Piksel koordinatı geçersiz veya nokta eklenemedi."; };
        AutomationProperties.SetName(StatusForVerification,"Fotoğraf taslağı durumu");AutomationProperties.SetLiveSetting(StatusForVerification,AutomationLiveSetting.Polite);
        DockPanel.SetDock(StatusForVerification,Dock.Top);root.Children.Add(StatusForVerification);
        CanvasForVerification.Image=image;CanvasForVerification.PointsChanged+=(_,_)=>UpdateStatus();UpdateStatus();root.Children.Add(CanvasForVerification);
        Closing+=OnClosing;
    }
    private void UpdateStatus()
    {
        var n=CanvasForVerification.Points.Count;
        StatusForVerification.Text=$"Önizleme: {image.PixelWidth} × {image.PixelHeight} piksel. "+(n<2 ? $"Referans ucu seçin: {n}/2." : $"Referans uçları hazır; çizgi noktası: {n-2}. İlk referans CAD'de 0,0 olur; Y aşağı artar.");
    }
    internal void TryApply()
    {
        try
        {
            var p=CanvasForVerification.Points;
            if(p.Count<4) { StatusForVerification.Text="İki referans ucu ve en az iki çizgi noktası seçin.";return; }
            if(!MeasurementInput.TryParseMillimeters(LengthForVerification.Text,out var mm) || mm<=0) { StatusForVerification.Text="Bilinen mesafeyi pozitif milimetre olarak girin.";return; }
            var trace=PhotoTraceCalibration.Convert(image.PixelWidth,image.PixelHeight,new Point2(p[0].X,p[0].Y),new Point2(p[1].X,p[1].Y),mm,p.Skip(2).Select(q=>new Point2(q.X,q.Y)).ToArray());
            LargeOpenPathValidator.Validate(trace);
            if(workspace.PhotoTraceProblem(stamp) is { } stale) { StatusForVerification.Text=stale;return; }
            const string question="Elle işaretlenen açık çizgi CAD'e taslak olarak eklenecek. Fotoğraf perspektifi, gerçek beden, görünmeyen parçalar ve üretim uygunluğu doğrulanmadı. Kalibrasyon ve fotoğraf bu çizgiyle projeye kaydedilmez. Devam edilsin mi?";
            var confirmed=ConfirmForVerification?.Invoke(question) ?? (MessageBox.Show(this,question,"Taslak çizgi",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes);
            if(!confirmed) { StatusForVerification.Text="İşlem iptal edildi; proje değiştirilmedi.";return; }
            if(!workspace.ApplyPhotoTrace(stamp,trace)) { StatusForVerification.Text=workspace.PhotoTraceProblem(stamp) ?? "Taslak eklenemedi.";return; }
            allowClose=true;Close();
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or System.IO.InvalidDataException or OverflowException)
        { StatusForVerification.Text="Taslak eklenemedi: "+ex.Message; }
    }
    private void OnClosing(object? sender,CancelEventArgs e)
    {
        if(!allowClose && CanvasForVerification.Points.Count>0 && MessageBox.Show(this,"İşaretlenmiş noktalar kaybolacak. Kapatılsın mı?","Taslak çizgi",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)e.Cancel=true;
    }
}
