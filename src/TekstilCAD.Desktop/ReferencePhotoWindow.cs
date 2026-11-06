using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TekstilCAD.Desktop;

/// <summary>Optional local view of a captured reference; no analysis or pattern is created.</summary>
internal sealed class ReferencePhotoWindow : Window
{
    private readonly byte[] data;
    private readonly Image photo=new() { Stretch=Stretch.Uniform,SnapsToDevicePixels=true };
    private ReferencePhotoPreview? decodedPreview;
    private readonly Button traceButton=new() { Content="Fotoğrafta elle taslak çiz…",Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,0,8,4),IsEnabled=false };
    private PhotoTraceWindow? traceWindow;
    internal Image PhotoForVerification=>photo;
    internal TextBlock StatusForVerification { get; } = new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) };
    private bool closed;
    internal Func<bool>? ConfirmTraceTargetForVerification { get; private set; }
    internal Func<bool>? TraceSourceCurrentForVerification { get; private set; }

    internal ReferencePhotoWindow(Window owner,byte[] source,Func<bool>? confirmTraceTarget=null,Func<bool>? traceSourceCurrent=null,bool ownTrace=false,string? requestMeasurements=null,string? requestPrompt=null)
    {
        ArgumentNullException.ThrowIfNull(owner);ArgumentNullException.ThrowIfNull(source);
        TraceSourceCurrentForVerification=traceSourceCurrent;ConfirmTraceTargetForVerification=confirmTraceTarget;data=(byte[])source.Clone();Owner=owner;Title="Referans görseli";Width=640;Height=640;MinWidth=360;MinHeight=360;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        StudioTheme.Apply(this);Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("DesignRequestTheme.xaml",UriKind.Relative) });
        Background=DesignRequestWindow.BackgroundColor;Foreground=DesignRequestWindow.Ink;
        var root=new DockPanel { Margin=new Thickness(14) };Content=root;
        var actions=new WrapPanel { Margin=new Thickness(0,8,0,0) };
        var close=new Button { Content="Kapat",Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,0,4) };
        close.Click+=(_,_)=>Close();actions.Children.Add(traceButton);actions.Children.Add(close);DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        AutomationProperties.SetHelpText(traceButton,"Bilinen mesafeyle fotoğraf üzerinde elle açık taslak çizgi oluşturur; otomatik veya doğrulanmış kalıp değildir.");
        traceButton.Click+=(_,_)=>
        {
            if(traceWindow is not null) { traceWindow.Activate();return; }
            var studio=Owner as MainWindow ?? (Owner as Window)?.Owner as MainWindow;
            if(studio is null || decodedPreview is not { } captured) { StatusForVerification.Text="Çizim penceresi veya fotoğraf bulunamadı.";return; }
            if(confirmTraceTarget is not null && !confirmTraceTarget()) { StatusForVerification.Text="Çizime geçiş iptal edildi veya kaynak değişti; istek ve fotoğraf korundu.";return; }
            var stamp=studio.CapturePhotoTrace();if(stamp is null) { StatusForVerification.Text="Çizime geçilemedi; ana penceredeki durumu kontrol edin.";return; }
            var window=new PhotoTraceWindow(studio,captured.Bitmap,stamp,captured.OrientedSourceWidth,captured.OrientedSourceHeight,captured.SourceSha256,captured.SourceByteLength,captured.SourceOrientation,traceSourceCurrent,ownTrace ? this : null,requestMeasurements,requestPrompt);traceWindow=window;window.Closed+=(_,_)=>traceWindow=null;window.Show();
        };
        AutomationProperties.SetName(StatusForVerification,"Görsel önizleme durumu");AutomationProperties.SetLiveSetting(StatusForVerification,AutomationLiveSetting.Polite);
        StatusForVerification.Text="Görsel açılıyor…";DockPanel.SetDock(StatusForVerification,Dock.Top);root.Children.Add(StatusForVerification);
        var explanation=new TextBlock { Text="Referans yalnız görsel inceleme içindir; ölçü veya kalıp çıkarılmaz. JPEG EXIF yön etiketi uygulanır; diğer yön bilgileri uygulanmayabilir.",TextWrapping=TextWrapping.Wrap,Foreground=DesignRequestWindow.Muted,Margin=new Thickness(0,0,0,8) };
        DockPanel.SetDock(explanation,Dock.Top);root.Children.Add(explanation);
        AutomationProperties.SetName(photo,"Seçilen referans görselinin önizlemesi");
        AutomationProperties.SetHelpText(photo,"Görüntü içeriği otomatik açıklanmaz; önizleme ölçü doğrulaması değildir.");
        root.Children.Add(new Border { Background=DesignRequestWindow.Surface,BorderBrush=DesignRequestWindow.BorderColor,BorderThickness=new Thickness(1),Child=photo });
        if(ownTrace)Closing+=(_,e)=> { if(traceWindow is not null) { e.Cancel=true;StatusForVerification.Text="Önce açık fotoğraf taslağını kapatın; taslak korunuyor.";traceWindow.Activate(); } };
        Closed+=(_,_)=>closed=true;Loaded+=async (_,_)=>await LoadPhotoAsync();
    }
    private async Task LoadPhotoAsync()
    {
        try
        {
            var decoded=await Task.Run(()=>ReferencePhotoDecoder.DecodeWithGeometry(data));
            if(closed)return;
            decodedPreview=decoded;photo.Source=decoded.Bitmap;traceButton.IsEnabled=true;StatusForVerification.Text="Referans görseli gösteriliyor.";
        }
        catch(Exception ex) when(ex is InvalidDataException or IOException or NotSupportedException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            if(closed)return;
            photo.Source=null;StatusForVerification.Text="Görsel önizlenemedi. İstek ve görsel baytları korunuyor.";
        }
    }
}
