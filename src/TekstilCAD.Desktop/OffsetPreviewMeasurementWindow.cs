using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Desktop;

/// <summary>Ephemeral comparison of explicitly identified source and unverified candidate vertices.</summary>
internal sealed class OffsetPreviewMeasurementWindow : Window
{
    private sealed record Choice(int Index,string Label);
    private readonly Point2[] source,candidate;
    internal ComboBox[] Selectors { get; }=new ComboBox[6];
    internal TextBlock Result { get; }=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0),Focusable=true };
    internal OffsetPreviewMeasurementWindow(Window owner,IReadOnlyList<Point2> source,IReadOnlyList<Point2> candidate)
    {
        this.source=source.ToArray();this.candidate=candidate.ToArray();Owner=owner;
        Title="Doğrulanmamış aday · seçili ölçüler";Width=720;Height=720;MinWidth=460;MinHeight=400;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var close=new Button { Content="Kapat",IsCancel=true,Padding=new Thickness(12,7,12,7),HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };
        close.Click+=(_,_)=>Close();DockPanel.SetDock(close,Dock.Bottom);root.Children.Add(close);
        var body=new StackPanel();root.Children.Add(new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });
        var warning=new TextBlock { Text="Yalnız seçtiğiniz iki üçlü ölçülür. Kaynak ve aday indeksleri bağımsızdır; aynı numaralar aynı özelliği göstermez. Ağız uçlarını iki konturda aynı sırada seçin. Aday doğrulanmamıştır; bu ölçüm diğer ayrıntıları, dikiş payını veya üretim uygunluğunu doğrulamaz. Sonuç kaydedilmez ve projeye eklenmez. Köşe işaretleri bu görünümde eşlenmez.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,0,0,10) };
        AutomationProperties.SetName(warning,warning.Text);body.Children.Add(warning);
        body.Children.Add(new TextBlock { Text=$"Kaynak: {this.source.Length} nokta · Aday: {this.candidate.Length} nokta. İndeksler sıfırdan başlar; tekrar eden kapanış noktası ayrı bir satırdır.",TextWrapping=TextWrapping.Wrap });
        static Choice[] Choices(Point2[] points)=>points.Select((p,i)=>new Choice(i,$"{i} · X {MeasurementInput.Format(p.X)} · Y {MeasurementInput.Format(p.Y)} mm")).ToArray();
        var originalChoices=Choices(this.source);var candidateChoices=Choices(this.candidate);
        string[] labels=["Kaynak ağız başlangıcı","Kaynak tanık noktası","Kaynak ağız bitişi","Aday ağız başlangıcı","Aday tanık noktası","Aday ağız bitişi"];
        for(var i=0;i<Selectors.Length;i++)
        {
            body.Children.Add(new TextBlock { Text=labels[i]+" (indeks)",Margin=new Thickness(0,7,0,3) });
            var box=new ComboBox { ItemsSource=i<3 ? originalChoices : candidateChoices,DisplayMemberPath=nameof(Choice.Label),IsTextSearchEnabled=true,MaxDropDownHeight=220,HorizontalAlignment=HorizontalAlignment.Stretch };
            box.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));VirtualizingPanel.SetIsVirtualizing(box,true);ScrollViewer.SetCanContentScroll(box,true);
            AutomationProperties.SetName(box,labels[i]);box.SelectionChanged+=(_,_)=> { Result.Text="Seçim değişti; ölçümü yenileyin."; };Selectors[i]=box;body.Children.Add(box);
        }
        var compare=new Button { Content="Seçili ölçüleri karşılaştır",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,12,0,0) };
        compare.Click+=(_,_)=>Compare();body.Children.Add(compare);
        AutomationProperties.SetName(Result,"Doğrulanmamış aday ölçüm sonucu");AutomationProperties.SetLiveSetting(Result,AutomationLiveSetting.Polite);body.Children.Add(Result);
    }
    internal void Compare()
    {
        try
        {
            if(Selectors.Any(s=>s.SelectedItem is not Choice))throw new ArgumentException("Her konturda üç nokta seçin.");
            var indices=Selectors.Select(s=>((Choice)s.SelectedItem).Index).ToArray();
            var value=SelectedNotchComparison.Measure(source,candidate,indices[0],indices[1],indices[2],indices[3],indices[4],indices[5]);
            Result.Text=$"Kaynak ağız: {MeasurementInput.Format(value.SourceWidth)} mm · Aday ağız: {MeasurementInput.Format(value.CandidateWidth)} mm · Fark (aday − kaynak): {MeasurementInput.Format(value.CandidateWidth-value.SourceWidth)} mm\n"+
                $"Kaynak derinlik: {MeasurementInput.Format(value.SourceDepth)} mm · Aday derinlik: {MeasurementInput.Format(value.CandidateDepth)} mm · Fark (aday − kaynak): {MeasurementInput.Format(value.CandidateDepth-value.SourceDepth)} mm\n"+
                "Yaklaşık gösterimdir; yalnız seçili noktalar ölçüldü. Aday ve üretim uygunluğu doğrulanmadı.";
        }
        catch(ArgumentException ex) { Result.Text="Ölçüm yapılamadı: "+ex.Message; }
        AutomationProperties.SetName(Result,Result.Text);
        if(UIElementAutomationPeer.CreatePeerForElement(Result) is { } peer)peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
