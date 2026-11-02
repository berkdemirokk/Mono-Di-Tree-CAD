using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>Read-only snapshot for manual drawing; it is not attached to the CAD document.</summary>
internal sealed class RequestNotesWindow : Window
{
    internal TextBox DescriptionForVerification { get; }
    internal TextBox MeasurementsForVerification { get; }
    internal TextBox ReferenceForVerification { get; }
    internal Button PhotoButtonForVerification { get; }
    internal Button TraceButtonForVerification { get; }
    internal TextBlock TraceStatusForVerification { get; } = new() { TextWrapping=TextWrapping.Wrap,Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,0,6) };
    internal Func<byte[],ReferencePhotoPreview>? DecodeForVerification { get; set; }
    private ReferencePhotoWindow? photoWindow;
    private PhotoTraceWindow? traceWindow;
    private readonly byte[]? referenceBytes;
    private bool closed,decoding;

    internal RequestNotesWindow(Window owner,DesignRequest request)
    {
        ArgumentNullException.ThrowIfNull(owner);DesignRequestStorage.Validate(request);
        Owner=owner;Title="Çizim için istek notları";Width=480;Height=620;MinWidth=360;MinHeight=360;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        StudioTheme.Apply(this);
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("DesignRequestTheme.xaml",UriKind.Relative) });
        Background=DesignRequestWindow.BackgroundColor;Foreground=DesignRequestWindow.Ink;
        var root=new DockPanel { Margin=new Thickness(14) };Content=root;
        var actions=new WrapPanel { Margin=new Thickness(0,8,0,0) };
        PhotoButtonForVerification=new Button { Content="Görseli göster",Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,4),IsEnabled=request.Reference is not null };
        AutomationProperties.SetHelpText(PhotoButtonForVerification,"Kaydedilmiş görselin yerel önizlemesini açar; fotoğraftan ölçü veya kalıp çıkarmaz.");
        TraceButtonForVerification=new Button { Content="Fotoğraftan çiz…",Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,4),IsEnabled=request.Reference is not null && owner is MainWindow };
        AutomationProperties.SetHelpText(TraceButtonForVerification,"Kaydedilmiş referansı kullanarak elle açık taslak çizim penceresi açar; otomatik kalıp oluşturmaz.");
        var close=new Button { Content="Kapat",Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,0,4) };
        close.Click+=(_,_)=>Close();actions.Children.Add(PhotoButtonForVerification);actions.Children.Add(TraceButtonForVerification);actions.Children.Add(close);DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        referenceBytes=request.Reference is null ? null : (byte[])request.Reference.Data.Clone();
        PhotoButtonForVerification.Click+=(_,_)=>
        {
            if(referenceBytes is null)return;
            if(photoWindow is not null) { photoWindow.Activate();return; }
            var preview=new ReferencePhotoWindow(this,referenceBytes);photoWindow=preview;
            preview.Closed+=(_,_)=>photoWindow=null;preview.Show();
        };
        TraceButtonForVerification.Click+=async (_,_)=> { var window=await PreparePhotoTraceAsync();if(window is null)return;if(window.IsVisible)window.Activate();else window.Show(); };
        Closed+=(_,_)=>closed=true;
        AutomationProperties.SetName(TraceStatusForVerification,"Fotoğraf taslağı açma durumu");AutomationProperties.SetLiveSetting(TraceStatusForVerification,AutomationLiveSetting.Polite);
        DockPanel.SetDock(TraceStatusForVerification,Dock.Top);root.Children.Add(TraceStatusForVerification);
        var notice=new TextBlock { Text="Açılış anındaki istek notlarıdır; otomatik güncellenmez. Çizim elle yapılır; kalıp oluşturulmadı ve ölçüler doğrulanmadı.",TextWrapping=TextWrapping.Wrap,Foreground=DesignRequestWindow.Muted,Margin=new Thickness(0,0,0,8) };
        DockPanel.SetDock(notice,Dock.Top);root.Children.Add(notice);
        var grid=new Grid();root.Children.Add(grid);
        for(var i=0;i<6;i++)grid.RowDefinitions.Add(new RowDefinition { Height=i is 1 or 3 ? new GridLength(1,GridUnitType.Star) : GridLength.Auto });
        TextBox ReadOnly(string name,string text)
        {
            var box=new TextBox { Text=text,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=0,VerticalContentAlignment=VerticalAlignment.Top };
            AutomationProperties.SetName(box,name);return box;
        }
        void AddRow(string title,TextBox box,int row)
        {
            var label=new Label { Content=title,Target=box,Foreground=DesignRequestWindow.Ink,Padding=new Thickness(0,3,0,3) };
            Grid.SetRow(label,row);grid.Children.Add(label);Grid.SetRow(box,row+1);grid.Children.Add(box);
        }
        DescriptionForVerification=ReadOnly("İstek açıklaması",request.Prompt);
        MeasurementsForVerification=ReadOnly("Ölçü notları",request.Measurements);
        var reference=request.Reference;
        ReferenceForVerification=ReadOnly("Referans görsel bilgisi",reference is null ? "Görsel seçilmedi." : $"{SafeName(reference.Name)} · {reference.MediaType} · {reference.Data.Length} bayt\nİsteğe bağlı görüntülenebilir; yorumlanmıyor.");
        ReferenceForVerification.MaxHeight=64;
        AddRow("Açıklama",DescriptionForVerification,0);AddRow("Ölçü notları",MeasurementsForVerification,2);AddRow("Referans görsel bilgisi",ReferenceForVerification,4);
    }
    internal async Task<PhotoTraceWindow?> PreparePhotoTraceAsync()
    {
        if(closed || decoding || referenceBytes is null || Owner is not MainWindow studio)return null;
        if(traceWindow is not null)return traceWindow;
        decoding=true;TraceButtonForVerification.IsEnabled=false;SetTraceStatus("Görsel çizim için hazırlanıyor…");
        try
        {
            var decode=DecodeForVerification;
            var captured=await Task.Run(()=>decode is null ? ReferencePhotoDecoder.DecodeWithGeometry(referenceBytes) : decode(referenceBytes));
            if(closed)return null;
            var stamp=studio.CapturePhotoTrace();
            if(stamp is null) { SetTraceStatus("Çizim açılamadı; ana penceredeki bekleyen işlemi kontrol edin.");return null; }
            var window=new PhotoTraceWindow(studio,captured.Bitmap,stamp,captured.OrientedSourceWidth,captured.OrientedSourceHeight,captured.SourceSha256,captured.SourceByteLength,captured.SourceOrientation);
            traceWindow=window;window.Closed+=(_,_)=>traceWindow=null;
            SetTraceStatus("Fotoğraf taslağı hazır; çizgi ve referans noktalarını siz seçin.");return window;
        }
        catch(Exception ex) when(ex is InvalidDataException or IOException or NotSupportedException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { if(!closed)SetTraceStatus("Görsel çizim için açılamadı; istek ve proje değişmedi.");return null; }
        finally { decoding=false;if(!closed)TraceButtonForVerification.IsEnabled=true; }
    }
    private void SetTraceStatus(string text) { TraceStatusForVerification.Text=text;TraceStatusForVerification.Visibility=Visibility.Visible; }
    private static string SafeName(string name)
    {
        var result=new StringBuilder(name.Length);
        foreach(var rune in name.EnumerateRunes())
        {
            var category=Rune.GetUnicodeCategory(rune);
            if(category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                result.Append(rune.Value<=0xffff ? $"\\u{rune.Value:X4}" : $"\\U{rune.Value:X8}");
            else result.Append(rune.ToString());
        }
        return result.ToString();
    }
}
