using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace TekstilCAD.Desktop;

/// <summary>Collects explicit notes only; no drafting engine is connected.</summary>
internal sealed class ExperimentalBodyMeasurementsWindow : Window
{
    private readonly DesignRequestWindow request;
    private readonly string originalPrompt,originalMeasurements;
    internal Dictionary<string,TextBox> InputsForVerification { get; } = new(StringComparer.Ordinal);
    internal TextBlock StatusForVerification { get; } = new() { TextWrapping=TextWrapping.Wrap,Foreground=DesignRequestWindow.Ink,Margin=new Thickness(0,8,0,0) };
    private static readonly (string Label,bool Angle)[] Fields = [
        ("üst kol çevresi",false),("ön göğüs",false),("göğüs noktaları arası",false),
        ("üst göğüs çevresi",false),("ön üst göğüs",false),("omuz üst noktasından göğse",false),
        ("omuz üst noktasından arka bele",false),("omuz üst noktasından ön bele",false),
        ("bel çevresi",false),("belden kalçaya",false),("boyun çevresi",false),
        ("omuzdan omuza",false),("omuz eğimi",true),("omuzdan bileğe",false),("bilek çevresi",false) ];

    internal ExperimentalBodyMeasurementsWindow(DesignRequestWindow request)
    {
        this.request=request;Owner=request;originalPrompt=request.PromptForVerification.Text;originalMeasurements=request.MeasurementsForVerification.Text;
        Title="Deneysel beden ölçüleri";Width=600;Height=650;MinWidth=440;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("DesignRequestTheme.xaml",UriKind.Relative) });Background=DesignRequestWindow.BackgroundColor;Foreground=DesignRequestWindow.Ink;
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };
        var cancel=new Button { Content="Vazgeç",IsCancel=true,Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,0) };
        var apply=new Button { Content="Girilenleri notlara ekle",Padding=new Thickness(12,6,12,6),Background=DesignRequestWindow.Accent,Foreground=DesignRequestWindow.Surface };
        apply.Click+=(_,_)=> { if(TryApply())DialogResult=true; };actions.Children.Add(cancel);actions.Children.Add(apply);DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        AutomationProperties.SetName(StatusForVerification,"Deneysel ölçü girişi durumu");AutomationProperties.SetLiveSetting(StatusForVerification,AutomationLiveSetting.Polite);
        var statusScroll=new ScrollViewer { Content=StatusForVerification,MaxHeight=70,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Focusable=true };
        AutomationProperties.SetName(statusScroll,"Deneysel ölçü durumu");DockPanel.SetDock(statusScroll,Dock.Bottom);root.Children.Add(statusScroll);
        var scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Focusable=true };AutomationProperties.SetName(scroll,"Deneysel ölçü alanları");root.Children.Add(scroll);
        var body=new StackPanel();scroll.Content=body;
        body.Children.Add(new TextBlock { Text="Deneysel beden ölçüleri",FontSize=20,FontWeight=FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text="Bu alanlar yalnızca ölçü notlarını hazırlar. Henüz beden önizlemesi veya kalıp oluşturulmaz. Bildiğiniz değerleri girin; boş alanlar eklenmez. Uzunluklar santimetre, omuz eğimi derecedir. Önceki notlar silinmez; tekrar girilen değerleri ve çelişkileri kontrol edin.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,10) });
        foreach(var (label,angle) in Fields)
        {
            var input=new TextBox { MaxLength=24,MinHeight=32,Margin=new Thickness(0,0,0,8) };
            var unit=angle ? "derece" : "cm";
            body.Children.Add(new Label { Content=label+" ("+unit+")",Target=input,Foreground=DesignRequestWindow.Ink,Padding=new Thickness(0,2,0,2) });
            AutomationProperties.SetName(input,label+" "+unit);AutomationProperties.SetHelpText(input,angle ? "Yataya göre işaretli açı; bilinmiyorsa boş bırakın." : "Santimetre cinsinden pozitif sayı; bilinmiyorsa boş bırakın.");
            body.Children.Add(input);InputsForVerification.Add(label,input);
        }
    }
    internal bool TryApply()
    {
        if(request.PromptForVerification.Text!=originalPrompt || request.MeasurementsForVerification.Text!=originalMeasurements)
        { StatusForVerification.Text="İstek değişti; bu pencereyi kapatıp yeniden açın.";return false; }
        var lines=new List<string>();
        foreach(var (label,angle) in Fields)
        {
            var value=InputsForVerification[label].Text.Trim();if(value.Length==0)continue;
            var pattern=angle ? @"\A-?[0-9]{1,5}(?:[.,][0-9]{1,8})?\z" : @"\A[0-9]{1,5}(?:[.,][0-9]{1,8})?\z";
            if(!Regex.IsMatch(value,pattern,RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)) ||
                !decimal.TryParse(value.Replace(',','.'),NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var number) ||
                (angle ? number<=-90m || number>=90m : number<=0m || number>1000m))
            { StatusForVerification.Text=$"{label}: "+(angle ? "-90 ile 90 arasında bir açı girin (sınırlar hariç)." : "0 ile 1000 arasında pozitif bir santimetre değeri girin.");return false; }
            lines.Add(label+" "+value+(angle ? " derece" : " cm"));
        }
        if(lines.Count==0) { StatusForVerification.Text="Eklemek için en az bir ölçü girin.";return false; }
        if(request.TryAppendMeasurementLines(originalPrompt,originalMeasurements,lines,out var error))return true;
        StatusForVerification.Text=error;return false;
    }
}
