using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TekstilCAD.Patterns;

namespace TekstilCAD.Desktop;

/// <summary>Collects explicit notes only; no drafting engine is connected.</summary>
internal sealed class ExperimentalBodyMeasurementsWindow : Window
{
    private readonly DesignRequestWindow request;
    private readonly string originalPrompt,originalMeasurements;
    internal Dictionary<string,TextBox> InputsForVerification { get; } = new(StringComparer.Ordinal);
    internal TextBlock StatusForVerification { get; } = new() { TextWrapping=TextWrapping.Wrap,Foreground=DesignRequestWindow.Ink,Margin=new Thickness(0,8,0,0) };
    private static readonly (string Label,string Key,bool Angle)[] Fields = [
        ("üst kol çevresi","biceps",false),("ön göğüs","bustFront",false),("göğüs noktaları arası","bustSpan",false),
        ("üst göğüs çevresi","highBust",false),("ön üst göğüs","highBustFront",false),("omuz üst noktasından göğse","hpsToBust",false),
        ("omuz üst noktasından arka bele","hpsToWaistBack",false),("omuz üst noktasından ön bele","hpsToWaistFront",false),
        ("bel çevresi","waist",false),("belden kalçaya","waistToHips",false),("boyun çevresi","neck",false),
        ("omuzdan omuza","shoulderToShoulder",false),("omuz eğimi","shoulderSlope",true),("omuzdan bileğe","shoulderToWrist",false),("bilek çevresi","wrist",false) ];

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
        body.Children.Add(new TextBlock { Text="Henüz beden önizlemesi veya kalıp oluşturulmaz. Bildiğiniz değerleri girin; uzunluklar santimetre, omuz eğimi derecedir. Notlara ekleme kısmi girişe izin verir ve ölçüler arasındaki çelişkileri denetlemez. 15 ölçüyü yerel denetle seçeneği tüm alanları ve bazı ilişkileri denetler; ölçüm doğruluğunu kanıtlamaz. Önceki notlar silinmez.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,10) });
        AutomationProperties.SetHelpText(apply,"Girilen geçerli sayıları notlara ekler; kısmi giriş mümkündür ve ölçüler arası çelişkiler bu işlemde denetlenmez.");
        var review=new Button { Content="15 ölçüyü yerel denetle…",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,0,8) };
        AutomationProperties.SetHelpText(review,"Tüm alanları yerel olarak denetler ve geçici ölçü özetini açar; kalıp hesaplamaz veya veri göndermez.");
        review.Click+=(_,_)=> { if(TryPrepare(out var prepared))new ExperimentalBodyReviewWindow(this,prepared!).ShowDialog(); };body.Children.Add(review);
        foreach(var (label,key,angle) in Fields)
        {
            var input=new TextBox { MaxLength=24,MinHeight=32,Margin=new Thickness(0,0,0,8) };
            var unit=angle ? "derece" : "cm";
            body.Children.Add(new Label { Content=label+" ("+unit+")",Target=input,Foreground=DesignRequestWindow.Ink,Padding=new Thickness(0,2,0,2) });
            var instructions=BreannaMeasurementGate.Definitions.Single(d=>d.Key==key).Instructions;
            input.ToolTip=instructions;AutomationProperties.SetName(input,label+" "+unit);AutomationProperties.SetHelpText(input,instructions+" "+(angle ? "Bilinmiyorsa boş bırakın." : "Santimetre cinsinden; bilinmiyorsa boş bırakın."));
            body.Children.Add(input);InputsForVerification.Add(label,input);
        }
    }
    internal bool TryApply()
    {
        if(!TryRead(out var lines,out _))return false;
        if(lines.Count==0) { StatusForVerification.Text="Eklemek için en az bir ölçü girin.";return false; }
        if(request.TryAppendMeasurementLines(originalPrompt,originalMeasurements,lines,out var error))return true;
        StatusForVerification.Text=error;return false;
    }
    internal bool TryPrepare(out ExperimentalBodyDraftRequest? prepared)
    {
        prepared=null;if(!TryRead(out _,out var inputs))return false;
        if(!ExperimentalBodyDraftRequest.TryCreate(inputs,out prepared,out var errors))
        {
            var labels=Fields.ToDictionary(f=>f.Key,f=>f.Label,StringComparer.Ordinal);
            StatusForVerification.Text=string.Join(" ",errors.Select(e=>(labels.TryGetValue(e.Key,out var label) ? label+": " : "")+e.Message));return false;
        }
        StatusForVerification.Text="15 ölçü teknik olarak denetlendi; henüz kalıp veya beden önizlemesi oluşturulmadı.";return true;
    }
    private bool TryRead(out List<string> lines,out List<BreannaMeasurementInput> inputs)
    {
        lines=[];inputs=[];
        if(request.PromptForVerification.Text!=originalPrompt || request.MeasurementsForVerification.Text!=originalMeasurements)
        { StatusForVerification.Text="İstek değişti; bu pencereyi kapatıp yeniden açın.";return false; }
        foreach(var (label,key,angle) in Fields)
        {
            var value=InputsForVerification[label].Text.Trim();if(value.Length==0)continue;
            var pattern=angle ? @"\A-?[0-9]{1,5}(?:[.,][0-9]{1,8})?\z" : @"\A[0-9]{1,5}(?:[.,][0-9]{1,8})?\z";
            if(!Regex.IsMatch(value,pattern,RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)) ||
                !decimal.TryParse(value.Replace(',','.'),NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var number) ||
                (angle ? number<=-90m || number>=90m : number<=0m || number>1000m))
            { StatusForVerification.Text=$"{label}: "+(angle ? "-90 ile 90 arasında bir açı girin (sınırlar hariç)." : "0 ile 1000 arasında pozitif bir santimetre değeri girin.");return false; }
            lines.Add(label+" "+value+(angle ? " derece" : " cm"));
            inputs.Add(new BreannaMeasurementInput(key,number,angle ? BreannaUnit.Degree : BreannaUnit.Centimeter));
        }
        return true;
    }
}
