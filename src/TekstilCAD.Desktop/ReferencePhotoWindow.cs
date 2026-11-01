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
    internal Image PhotoForVerification=>photo;
    internal TextBlock StatusForVerification { get; } = new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) };
    private bool closed;

    internal ReferencePhotoWindow(Window owner,byte[] source)
    {
        ArgumentNullException.ThrowIfNull(owner);ArgumentNullException.ThrowIfNull(source);
        data=(byte[])source.Clone();Owner=owner;Title="Referans görseli";Width=640;Height=640;MinWidth=360;MinHeight=360;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        StudioTheme.Apply(this);Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("DesignRequestTheme.xaml",UriKind.Relative) });
        Background=DesignRequestWindow.BackgroundColor;Foreground=DesignRequestWindow.Ink;
        var root=new DockPanel { Margin=new Thickness(14) };Content=root;
        var close=new Button { Content="Kapat",Padding=new Thickness(12,7,12,7),HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };
        close.Click+=(_,_)=>Close();DockPanel.SetDock(close,Dock.Bottom);root.Children.Add(close);
        AutomationProperties.SetName(StatusForVerification,"Görsel önizleme durumu");AutomationProperties.SetLiveSetting(StatusForVerification,AutomationLiveSetting.Polite);
        StatusForVerification.Text="Görsel açılıyor…";DockPanel.SetDock(StatusForVerification,Dock.Top);root.Children.Add(StatusForVerification);
        var explanation=new TextBlock { Text="Referans yalnız görsel inceleme içindir; ölçü veya kalıp çıkarılmaz. EXIF yönü otomatik düzeltilmez; fotoğraf yan dönük görünebilir.",TextWrapping=TextWrapping.Wrap,Foreground=DesignRequestWindow.Muted,Margin=new Thickness(0,0,0,8) };
        DockPanel.SetDock(explanation,Dock.Top);root.Children.Add(explanation);
        AutomationProperties.SetName(photo,"Seçilen referans görselinin önizlemesi");
        AutomationProperties.SetHelpText(photo,"Görüntü içeriği otomatik açıklanmaz; önizleme ölçü doğrulaması değildir.");
        root.Children.Add(new Border { Background=DesignRequestWindow.Surface,BorderBrush=DesignRequestWindow.BorderColor,BorderThickness=new Thickness(1),Child=photo });
        Closed+=(_,_)=>closed=true;Loaded+=async (_,_)=>await LoadPhotoAsync();
    }
    private async Task LoadPhotoAsync()
    {
        try
        {
            var decoded=await Task.Run(()=>ReferencePhotoDecoder.Decode(data));
            if(closed)return;
            photo.Source=decoded;StatusForVerification.Text="Referans görseli gösteriliyor.";
        }
        catch(Exception ex) when(ex is InvalidDataException or IOException or NotSupportedException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            if(closed)return;
            photo.Source=null;StatusForVerification.Text="Görsel önizlenemedi. Kaydedilmiş istek ve görsel baytları korunuyor.";
        }
    }
}
