using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>Appends explicitly entered centimetre values to the original request text.</summary>
internal sealed class AtelierMeasurementEntryWindow : Window
{
    private readonly DesignRequestWindow request;
    private readonly string originalPrompt,originalMeasurements;
    internal Dictionary<string,TextBox> InputsForVerification { get; } = new(StringComparer.Ordinal);
    internal TextBlock StatusForVerification { get; } = new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0),Foreground=DesignRequestWindow.Ink };
    internal AtelierMeasurementEntryWindow(DesignRequestWindow request)
    {
        this.request=request;Owner=request;originalPrompt=request.PromptForVerification.Text;originalMeasurements=request.MeasurementsForVerification.Text;
        Title="Ölçüleri gir";Width=500;Height=410;MinWidth=440;MinHeight=390;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("DesignRequestTheme.xaml",UriKind.Relative) });Background=DesignRequestWindow.BackgroundColor;Foreground=DesignRequestWindow.Ink;
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };
        var cancel=new Button { Content="Vazgeç",IsCancel=true,Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,0) };
        var apply=new Button { Content="Ölçüleri ekle",Padding=new Thickness(12,6,12,6),Background=DesignRequestWindow.Accent,Foreground=DesignRequestWindow.Surface };
        apply.Click+=(_,_)=> { if(TryApply())DialogResult=true; };actions.Children.Add(cancel);actions.Children.Add(apply);DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var body=new StackPanel();root.Children.Add(body);
        body.Children.Add(new TextBlock { Text="Bildiğiniz ölçüleri santimetre olarak girin. Boş alanlar eklenmez; mevcut notlar korunur. Farklı değerler çelişki olarak gösterilir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10) });
        var fields=new WrapPanel();body.Children.Add(fields);
        void Field(string key,string title)
        {
            var panel=new StackPanel { Width=190,Margin=new Thickness(0,0,12,8) };
            var input=new TextBox { MaxLength=24,MinHeight=34 };var label=new Label { Content=title+" (cm)",Target=input,Foreground=DesignRequestWindow.Ink,Padding=new Thickness(0,2,0,2) };
            AutomationProperties.SetName(input,title+" santimetre");AutomationProperties.SetHelpText(input,"Yalnız sayı girin; birim santimetredir. Bilinmiyorsa boş bırakın.");
            panel.Children.Add(label);panel.Children.Add(input);fields.Children.Add(panel);InputsForVerification.Add(key,input);
        }
        Field("göğüs çevresi","Göğüs çevresi");Field("bel çevresi","Bel çevresi");Field("kalça çevresi","Kalça çevresi");Field("elbise boyu","Elbise boyu");
        AutomationProperties.SetName(StatusForVerification,"Ölçü girişi durumu");AutomationProperties.SetLiveSetting(StatusForVerification,AutomationLiveSetting.Polite);body.Children.Add(StatusForVerification);
    }
    internal bool TryApply()
    {
        if(request.PromptForVerification.Text!=originalPrompt || request.MeasurementsForVerification.Text!=originalMeasurements)
        { StatusForVerification.Text="İstek değişti; bu pencereyi kapatıp yeniden açın.";return false; }
        var lines=new List<string>();
        foreach(var (key,input) in InputsForVerification)
        {
            var text=input.Text.Trim();if(text.Length==0)continue;
            var line=key+" "+text+" cm";
            if(!System.Text.RegularExpressions.Regex.IsMatch(text,@"\A[0-9]{1,5}(?:[.,][0-9]{1,8})?\z",System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))
            { StatusForVerification.Text=$"{key}: yalnız pozitif bir sayı girin; birim santimetredir.";return false; }
            var parsed=DressMeasurementParser.Review("",line);
            if(parsed.Measurements.Count!=1 || parsed.Measurements[0].Label!=key || parsed.Unrecognized.Count!=0)
            { StatusForVerification.Text=$"{key}: pozitif bir sayı girin; yalnız virgül veya nokta ondalığı ve en çok 1000 cm desteklenir.";return false; }
            lines.Add(line);
        }
        if(lines.Count==0) { StatusForVerification.Text="Eklemek için en az bir ölçü girin.";return false; }
        return request.TryAppendMeasurementLines(originalPrompt,originalMeasurements,lines,out var error) || SetError(error);
    }
    private bool SetError(string message) { StatusForVerification.Text=message;return false; }
}
