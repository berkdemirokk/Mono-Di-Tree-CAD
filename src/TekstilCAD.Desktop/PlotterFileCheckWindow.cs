using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Desktop;

/// <summary>User-declared file inspection only; no printer or port is opened.</summary>
internal sealed class PlotterFileCheckWindow : Window
{
    internal readonly Dictionary<string,TextBox> InputsForVerification = new(StringComparer.Ordinal);
    internal ComboBox TransportForVerification { get; } = new() { MinWidth=220 };
    internal CheckBox OriginForVerification { get; } = new() { Content="Hedefin X=0, Y=0 başlangıcını ve dosyanın pozitif X/Y yönlerini bildiğimi beyan ediyorum" };
    internal CheckBox CommandsForVerification { get; } = new() { Content="IN, SP1, PA, PU, PD, SP0 ve satır sonlarını hedefin kabul ettiğini beyan ediyorum" };
    internal TextBlock ResultForVerification { get; } = new() { TextWrapping=TextWrapping.Wrap };
    internal Button CheckForVerification { get; } = new() { Content="Dosyayı beyanla karşılaştır",Padding=new Thickness(10,6,10,6),Margin=new Thickness(0,8,8,0) };
    internal Button CancelForVerification { get; } = new() { Content="İptal / kapat",Padding=new Thickness(10,6,10,6),Margin=new Thickness(0,8,0,0) };
    private readonly HpglPreparedOutput output;
    private readonly StackPanel form = new();
    private CancellationTokenSource? cancellation;
    private bool closed;
    private long declarationRevision;
    internal Task? CheckTaskForVerification { get; private set; }

    internal PlotterFileCheckWindow(Window owner,HpglPreparedOutput output)
    {
        this.output=output;Owner=owner;Title="HPGL dosyası · beyan edilen alan";Width=650;Height=700;MinWidth=480;MinHeight=420;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var buttons=new StackPanel { Orientation=Orientation.Horizontal };buttons.Children.Add(CheckForVerification);buttons.Children.Add(CancelForVerification);
        DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        var scroll=new ScrollViewer { Content=form,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };root.Children.Add(scroll);
        form.Children.Add(new TextBlock { Text=$"Yalnız hazırlanan dosyanın sizin bildirdiğiniz sınırlarla karşılaştırılmasıdır. Cihaza bağlanmaz veya veri göndermez; cihaz uyumluluğu, yön ve kalibrasyonu kanıtlamaz. Dosya: {output.UnitsPerMillimeter:G17} birim/mm; X {output.Bounds.MinX}…{output.Bounds.MaxX}, Y {output.Bounds.MinY}…{output.Bounds.MaxY}. Başlangıç 0,0 kabul edilir; dönüş veya yön değişimi yapılmaz. Negatif koordinatlar bu kontrolde kabul edilmez.",TextWrapping=TextWrapping.Wrap });
        void Field(string key,string label)
        {
            var input=new TextBox { MinHeight=28,MaxLength=200 };InputsForVerification.Add(key,input);
            input.TextChanged+=(_,_)=>InvalidateDeclaration();
            form.Children.Add(new Label { Content=label,Target=input });form.Children.Add(input);AutomationProperties.SetName(input,label);
        }
        Field("manufacturer","Üretici (beyan)");Field("model","Model (beyan)");Field("firmware","Yazılım sürümü (beyan)");
        Field("driver","Sürücü sürümü (beyan)");Field("adapter","Bağlantı / adaptör sürümü (beyan)");Field("reference","Profil referansı (beyan; doğrulama belgesi değildir)");
        Field("protocol","HPGL protokol tanımı (beyan)");
        form.Children.Add(new Label { Content="Windows aktarım türü (beyan)",Target=TransportForVerification });
        TransportForVerification.Items.Add("Windows yazdırma kuyruğu");TransportForVerification.Items.Add("Üretici SDK");form.Children.Add(TransportForVerification);
        TransportForVerification.SelectionChanged+=(_,_)=>InvalidateDeclaration();
        Field("units","Hedef birim/mm");Field("width","Çizilebilir X genişliği (mm)");Field("height","Çizilebilir Y uzunluğu (mm)");
        Field("maxX","En büyük X koordinatı (çizici birimi)");Field("maxY","En büyük Y koordinatı (çizici birimi)");
        Field("command","Komut başına en çok bayt");Field("job","İş başına en çok bayt");
        form.Children.Add(OriginForVerification);form.Children.Add(CommandsForVerification);
        OriginForVerification.Checked+=(_,_)=>InvalidateDeclaration();OriginForVerification.Unchecked+=(_,_)=>InvalidateDeclaration();
        CommandsForVerification.Checked+=(_,_)=>InvalidateDeclaration();CommandsForVerification.Unchecked+=(_,_)=>InvalidateDeclaration();
        ResultForVerification.Text="Kontrol edilmedi. Tüm alanları bilginize göre doldurun; bilinmeyen değerleri tahmin etmeyin.";
        AutomationProperties.SetName(ResultForVerification,"Dosya ön kontrol sonucu");AutomationProperties.SetLiveSetting(ResultForVerification,AutomationLiveSetting.Polite);form.Children.Add(ResultForVerification);
        CheckForVerification.Click+=(_,_)=>CheckTaskForVerification=CheckAsync();
        CancelForVerification.Click+=(_,_)=> { if(cancellation is not null) { cancellation.Cancel();ResultForVerification.Text="İptal istendi."; } else Close(); };
        Closed+=(_,_)=> { closed=true;cancellation?.Cancel(); };
    }
    private void InvalidateDeclaration()
    {
        declarationRevision++;cancellation?.Cancel();
        ResultForVerification.Text="Beyan değişti; dosyayı yeniden kontrol edin. Önceki sonuç artık geçerli değil.";
    }
    private static string ProfileSummary(PlotterCapabilityProfile p)=>
        $"Kontrol edilen beyan: {p.Manufacturer} · {p.Model}; yazılım {p.Firmware}; sürücü {p.DriverVersion}; adaptör {p.AdapterVersion}; kayıt {p.QualificationId}; protokol {p.ProtocolIdentifier}; aktarım {(p.Transport==PlotterTransport.WindowsSpooler ? "Windows yazdırma kuyruğu" : "üretici SDK")}.\n"+
        $"Birim/mm: {p.UnitsPerMillimeter:G17}; çizilebilir X/Y: {p.WidthMillimeters:G17} × {p.HeightMillimeters:G17} mm; koordinat üst sınırı X/Y: {p.MaximumXPlotUnits}/{p.MaximumYPlotUnits}; komut/iş sınırı: {p.MaximumCommandBytes}/{p.MaximumJobBytes} bayt. Başlangıç 0,0 ve pozitif X/Y ile listelenen komutlar kullanıcı tarafından beyan edildi.";
    internal PlotterCapabilityProfile? ReadProfileForVerification()
    {
        string Text(string key)=>InputsForVerification[key].Text.Trim();
        bool Number(string key,out double value)=>MeasurementInput.TryParseMillimeters(Text(key),out value) && double.IsFinite(value) && value>0;
        bool Integer(string key,out int value)=>int.TryParse(Text(key),NumberStyles.None,CultureInfo.InvariantCulture,out value) && value>0;
        if(!Number("units",out var units) || !Number("width",out var width) || !Number("height",out var height) ||
           !Integer("maxX",out var maxX) || !Integer("maxY",out var maxY) || !Integer("command",out var command) || !Integer("job",out var job) ||
           TransportForVerification.SelectedIndex is <0 or >1 || CommandsForVerification.IsChecked!=true)return null;
        var transport=TransportForVerification.SelectedIndex==0 ? PlotterTransport.WindowsSpooler : PlotterTransport.VendorSdk;
        return new PlotterCapabilityProfile(Text("manufacturer"),Text("model"),Text("firmware"),Text("driver"),Text("adapter"),Text("reference"),PlotterPlatform.Windows,transport,units,width,height,command,CapabilitySupport.Supported,CapabilitySupport.Unknown)
        { ProtocolIdentifier=Text("protocol"),SupportedCommands=PlotterHpglCommands.CurrentExporter,LineBreaks=CapabilitySupport.Supported,MaximumXPlotUnits=maxX,MaximumYPlotUnits=maxY,MaximumJobBytes=job };
    }
    internal async Task CheckAsync()
    {
        if(cancellation is not null)return;
        var profile=ReadProfileForVerification();var origin=OriginForVerification.IsChecked==true;
        if(profile is null || !profile.IsComplete || !origin)
        { ResultForVerification.Text="Beyan eksik veya geçersiz. Tüm alanları ve başlangıç/komut bildirimlerini kontrol edin; dosya değiştirilmedi.";return; }
        var tokenSource=new CancellationTokenSource();var revision=declarationRevision;cancellation=tokenSource;form.IsEnabled=false;CheckForVerification.IsEnabled=false;
        ResultForVerification.Text="Dosya kontrol ediliyor…";
        try
        {
            var result=await Task.Run(()=>PlotterFileCheck.Inspect(output,profile,origin,tokenSource.Token));
            tokenSource.Token.ThrowIfCancellationRequested();
            if(!closed && revision==declarationRevision)
                ResultForVerification.Text=result.Message+"\n"+ProfileSummary(profile)+(result.Inspection is { } info ? $"\nDosya özeti: {info.Sha256} · {info.ByteCount} bayt · {info.CommandCount} komut." : "");
        }
        catch(OperationCanceledException) { if(!closed)ResultForVerification.Text=revision==declarationRevision ? "Kontrol iptal edildi; dosya değiştirilmedi." : "Beyan değişti; dosyayı yeniden kontrol edin. Önceki sonuç artık geçerli değil."; }
        finally { cancellation=null;tokenSource.Dispose();if(!closed) { form.IsEnabled=true;CheckForVerification.IsEnabled=true; } }
    }
}
