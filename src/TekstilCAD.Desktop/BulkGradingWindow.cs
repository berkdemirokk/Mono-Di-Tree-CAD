using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Automation.Peers;
using TekstilCAD.Core;
using TekstilCAD.Grading;

namespace TekstilCAD.Desktop;

/// <summary>Preview and atomically add a batch generated from one captured grade draft.</summary>
internal sealed class BulkGradingWindow : Window
{
    private readonly MainWindow workspace;
    private readonly ProjectDocument originalDocument, snapshot;
    private readonly PatternPiece source;
    private readonly IReadOnlyList<Point2> increments;
    private readonly Func<bool> draftCurrent;
    private readonly Action added;
    private CancellationTokenSource? cancellation;
    private IReadOnlyList<PatternPiece>? ready;
    private string? readyText;
    private bool readySize,readyRole;
    internal TextBox Entries { get; } = new() { AcceptsReturn=true,TextWrapping=TextWrapping.NoWrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=112,Margin=new Thickness(0,5,0,5) };
    internal CheckBox IncludeSize { get; } = new() { Content="Beden bilgisini ekle",Foreground=StudioTheme.Text,Margin=new Thickness(0,4,12,4) };
    internal CheckBox CopyRole { get; } = new() { Content="Kaynağın rol ve malzemesini kopyala",Foreground=StudioTheme.Text,Margin=new Thickness(0,4,0,4) };
    internal Button PreviewButton { get; } = new() { Content="Bedenleri hesapla ve incele",Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,6,8,6) };
    internal Button AddButton { get; } = new() { Content="Tüm bedenleri projeye ekle",Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,6,8,6),IsEnabled=false };
    internal Button CancelButton { get; } = new() { Content="Hesabı iptal et",Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,6,8,6),IsEnabled=false };
    internal ListBox PreviewList { get; } = new() { Height=135,Margin=new Thickness(0,6,0,0) };
    internal TextBlock Status { get; } = new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0) };
    internal BulkGradePreview GeometryPreview { get; } = new();
    internal TextBlock ChangeSummary { get; } = new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0),Foreground=StudioTheme.Text };
    internal Action? BeforeBulkCalculationForVerification { get; set; }
    internal Task? Calculation { get; private set; }
    internal IReadOnlyList<PatternPiece>? Ready => ready;

    internal BulkGradingWindow(MainWindow workspace,Window owner,PatternPiece source,IReadOnlyList<Point2> increments,Func<bool> draftCurrent,Action added)
    {
        this.workspace=workspace; this.source=source.Clone(); this.increments=increments.ToArray(); this.draftCurrent=draftCurrent; this.added=added;
        originalDocument=workspace.Document;snapshot=originalDocument.Clone();Owner=owner;
        Title="Birden fazla beden · Mono Di Tree CAD";Width=720;Height=700;MinWidth=550;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var close=new Button { Content="Kapat",Padding=new Thickness(14,7,14,7),HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };
        close.Click+=(_,_)=>Close();DockPanel.SetDock(close,Dock.Bottom);root.Children.Add(close);
        DockPanel.SetDock(Status,Dock.Bottom);root.Children.Add(Status);
        var panel=new StackPanel();root.Children.Add(new ScrollViewer { Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });
        panel.Children.Add(new TextBlock { Text="Birden fazla beden oluştur",FontSize=20,FontWeight=FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text="Her satıra beden adı; adım sayısı yazın. Örnek: S; -1 ve L; 1 ayrı satırlarda. Her beden temel kalıptan hesaplanır. Adımlar virgül veya noktayla ondalıklı olabilir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0) });
        AutomationProperties.SetName(Entries,"Beden adları ve adımları");AutomationProperties.SetHelpText(Entries,"Her satıra beden adı, noktalı virgül ve temel bedenden adım sayısı girin; örnek S; -1.");panel.Children.Add(Entries);
        var options=new WrapPanel();options.Children.Add(IncludeSize);options.Children.Add(CopyRole);panel.Children.Add(options);
        panel.Children.Add(new TextBlock { Text="Adet hiçbir zaman kopyalanmaz. Sıfır adım veya sıfır artış, temel kalıpla aynı geometriyi oluşturabilir.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Muted });
        if(this.source.Grainline is not null)
            panel.Children.Add(new TextBlock { Text="İplik yönü her bedene aynı koordinatlarla aktarılır ve kontrol bekler. Her bedenin konturuyla ilişkisini kontrol edin; aktarım üretime uygunluk onayı değildir.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,6,0,0) });
        var actions=new WrapPanel();actions.Children.Add(PreviewButton);actions.Children.Add(AddButton);actions.Children.Add(CancelButton);panel.Children.Add(actions);
        AutomationProperties.SetName(PreviewList,"Hesaplanan bedenlerin önizleme listesi");panel.Children.Add(PreviewList);
        panel.Children.Add(new TextBlock { Text="Geometrileri karşılaştırmak için yukarıdan bir beden seçin. Konturlar: kesikli sarı temel kalıp, düz turkuaz seçili beden. İplik yönü çizgileri sarı ve kesikli turkuazdır; üst üste gelebilir. Her bedende konturla ilişkisini kontrol edin. Aynı koordinat ve ölçek kullanılır; ekran çizimi fiziksel ölçü değildir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0) });
        AutomationProperties.SetName(ChangeSummary,"Seçili bedenin karşılaştırma ve iplik yönü özeti");
        AutomationProperties.SetLiveSetting(ChangeSummary,AutomationLiveSetting.Polite);panel.Children.Add(ChangeSummary);
        panel.Children.Add(GeometryPreview);
        AutomationProperties.SetLiveSetting(Status,AutomationLiveSetting.Polite);AutomationProperties.SetName(Status,"Toplu beden durumu");
        var statusProperty=DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty,typeof(TextBlock));
        EventHandler announceStatus=(_,_)=> { if(UIElementAutomationPeer.CreatePeerForElement(Status) is { } peer)peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged); };
        statusProperty.AddValueChanged(Status,announceStatus);
        PreviewList.SelectionChanged+=(_,_)=>UpdateGeometryPreview();
        var monitor=new DispatcherTimer { Interval=TimeSpan.FromSeconds(1) };
        monitor.Tick+=(_,_)=> { if(ready is not null && !Current()) { InvalidatePreview();Status.Foreground=StudioTheme.Warning;Status.Text="Proje veya artışlar değişti; önizleme geçersiz. Pencereyi kapatıp yeniden açın."; } };
        Loaded+=(_,_)=>monitor.Start(); Closed+=(_,_)=> { monitor.Stop();statusProperty.RemoveValueChanged(Status,announceStatus); };
        Entries.TextChanged+=(_,_)=>InvalidatePreview();IncludeSize.Checked+=(_,_)=>InvalidatePreview();IncludeSize.Unchecked+=(_,_)=>InvalidatePreview();CopyRole.Checked+=(_,_)=>InvalidatePreview();CopyRole.Unchecked+=(_,_)=>InvalidatePreview();
        PreviewButton.Click+=(_,_)=>Calculation=CalculateAsync();AddButton.Click+=(_,_)=>AddReady();CancelButton.Click+=(_,_)=>RequestCancellation();
        Closing+=(_,e)=>{ if(cancellation is not null){e.Cancel=true;RequestCancellation();} };
    }
    private void RequestCancellation()
    {
        if(cancellation is null)return;
        cancellation.Cancel();CancelButton.IsEnabled=false;
        Status.Foreground=StudioTheme.Warning;
        Status.Text="İptal istendi; işlem durana kadar bekleyin. Proje değişmez.";
    }
    private bool Current() => !workspace.HasPendingCanvasOrPropertyWork && ReferenceEquals(workspace.Document,originalDocument) && DocumentSession.ContentEquals(workspace.Document,snapshot) && draftCurrent();
    private void InvalidatePreview()
    {
        var hadPreview=ready is not null;
        ready=null;readyText=null;AddButton.IsEnabled=false;PreviewList.ItemsSource=null;GeometryPreview.Clear();SetChangeSummary("");
        if(hadPreview) { Status.Foreground=StudioTheme.Warning;Status.Text="Girdiler değişti; bedenleri yeniden hesaplayın."; }
    }
    private void UpdateGeometryPreview()
    {
        if(ready is null || PreviewList.SelectedIndex<0 || PreviewList.SelectedIndex>=ready.Count) { GeometryPreview.Clear();SetChangeSummary("");return; }
        if(!Current()) { InvalidatePreview();Status.Foreground=StudioTheme.Warning;Status.Text="Proje veya artışlar değişti; önizleme geçersiz. Pencereyi kapatıp yeniden açın.";return; }
        var item=ready[PreviewList.SelectedIndex];GeometryPreview.SetPieces(source,item,SafeName(item.Name));
        var grainSummary=item.Grainline is null ? "İplik yönü yok."
            : item.Grainline.NeedsReview ? "İplik yönü var; konturla ilişkisi kontrol bekliyor."
            : "İplik yönü var; kontrol beklemiyor.";
        var selectedSummary=$"Seçili beden: {SafeName(item.Name)}. {grainSummary} ";
        if(item.Points.Count!=source.Points.Count)
        {
            SetChangeSummary(selectedSummary+"Nokta sayıları farklı; nokta bazında değişim karşılaştırılamıyor.");
            return;
        }
        var changed=0;double maximum=0;
        for(var i=0;i<source.Points.Count;i++)
        {
            if(source.Points[i]==item.Points[i])continue;
            changed++;maximum=Math.Max(maximum,Geometry.Distance(source.Points[i],item.Points[i]));
        }
        SetChangeSummary(selectedSummary+(changed==0 ? "Temel kalıpla aynı geometri; değişen nokta: 0, en büyük yer değiştirme: 0 mm."
            : $"Değişen nokta: {changed} / {source.Points.Count}. En büyük düz yer değiştirme: {MeasurementInput.Format(maximum)} mm."));
    }
    private void SetChangeSummary(string message)
    {
        if(ChangeSummary.Text==message)return;
        ChangeSummary.Text=message;
        AutomationProperties.SetHelpText(ChangeSummary,message);
        if(message.Length>0 && UIElementAutomationPeer.CreatePeerForElement(ChangeSummary) is { } peer)
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    private static List<GradeSize> Parse(string input,PatternPiece source,ProjectDocument snapshot)
    {
        if(input.Length>1_000_000)throw new ArgumentException("Beden listesi çok uzun; en fazla 1.000.000 karakter girin.");
        var lines=input.Replace("\r","").Split('\n');var result=new List<GradeSize>();var names=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        var existing=new HashSet<string>(snapshot.Pieces.Select(p=>p.Name.Trim()),StringComparer.OrdinalIgnoreCase);
        for(var lineIndex=0;lineIndex<lines.Length;lineIndex++)
        {
            var raw=lines[lineIndex];if(string.IsNullOrWhiteSpace(raw))continue;
            var line=lineIndex+1;
            var fields=raw.Split(';');if(fields.Length!=2)throw new ArgumentException($"{line}. satır: Beden adı ve adımı noktalı virgülle ayırın; örnek S; -1.");
            var name=fields[0].Trim();if(name.Length is <1 or >128)throw new ArgumentException($"{line}. satır: Beden adı 1–128 karakter olmalıdır.");
            if(names.TryGetValue(name,out var previousLine))throw new ArgumentException($"{line}. satır: Aynı beden adı {previousLine}. satırda da var; farklı bir ad girin.");
            names.Add(name,line);
            if(existing.Contains($"{source.Name} · {name}".Trim()))throw new ArgumentException($"{line}. satır: Bu beden projede zaten var; farklı bir ad seçin.");
            if(!MeasurementInput.TryParseMillimeters(fields[1],out var step))throw new ArgumentException($"{line}. satır: Geçerli bir adım sayısı girin; virgül veya nokta kullanabilirsiniz.");
            result.Add(new GradeSize(name,step));if(result.Count>1000)throw new ArgumentException("Bir grupta en fazla 1000 beden olabilir.");
        }
        if(result.Count==0)throw new ArgumentException("En az bir beden ve adım girin.");return result;
    }
    internal async Task CalculateAsync()
    {
        if(cancellation is not null)return;
        InvalidatePreview();
        try
        {
            if(!Current())throw new InvalidOperationException("Proje veya artış taslağı değişti. Pencereyi kapatıp güncel kaynakla tekrar açın.");
            var text=Entries.Text;var include=IncludeSize.IsChecked==true;var copy=CopyRole.IsChecked==true;var sizes=Parse(text,source,snapshot);
            var cts=new CancellationTokenSource();cancellation=cts;Entries.IsEnabled=false;IncludeSize.IsEnabled=false;CopyRole.IsEnabled=false;PreviewButton.IsEnabled=false;CancelButton.IsEnabled=true;
            Status.Foreground=StudioTheme.Muted;Status.Text="Bedenler hesaplanıyor; isterseniz iptal edebilirsiniz.";
            var items=await Task.Run(()=>
            {
                BeforeBulkCalculationForVerification?.Invoke();
                cts.Token.ThrowIfCancellationRequested();
                var generated=GradingEngine.GenerateSizes(source,increments,sizes,cts.Token);
                for(int i=0;i<generated.Count;i++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    ValidateGrainTransfer(source,generated[i]);
                    if(include || (copy && (source.Metadata?.Role is not null || source.Metadata?.Material is not null)))
                        generated[i].Metadata=new PieceMetadata { Size=include?sizes[i].Name:null,Role=copy?source.Metadata?.Role:null,Material=copy?source.Metadata?.Material:null,Quantity=null };
                }
                var candidate=snapshot.Clone();candidate.Pieces.AddRange(generated.Select(p=>p.Clone()));MainWindow.Validate(candidate);
                var labels=new string[generated.Count];
                for(var i=0;i<generated.Count;i++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    var item=generated[i];var bounds=Geometry.Bounds(item.Points);var unchanged=item.Points.SequenceEqual(source.Points);
                    labels[i]=$"{SafeName(item.Name)} · {MeasurementInput.Format(bounds.Width)} × {MeasurementInput.Format(bounds.Height)} mm"+(unchanged?" · temel geometriyle aynı":"");
                }
                cts.Token.ThrowIfCancellationRequested();return (Generated:generated,Labels:labels);
            });
            cts.Token.ThrowIfCancellationRequested();
            if(!Current() || Entries.Text!=text || (IncludeSize.IsChecked==true)!=include || (CopyRole.IsChecked==true)!=copy)throw new InvalidOperationException("Proje veya girdiler değişti; sonuç uygulanmadı.");
            ready=items.Generated;readyText=text;readySize=include;readyRole=copy;
            PreviewList.ItemsSource=items.Labels;
            PreviewList.SelectedIndex=0;
            Status.Foreground=source.Grainline is null ? StudioTheme.Text : StudioTheme.Warning;Status.Text=$"{items.Generated.Count} beden hazır. Ölçüleri inceleyin; ekleme tek geri alma işlemidir. Adet aktarılmaz."+(source.Grainline is null ? "" : " İplik yönü her bedende kontrol bekliyor; konturla ilişkisini kontrol edin.");AddButton.IsEnabled=true;
        }
        catch(OperationCanceledException) { InvalidatePreview();Status.Foreground=StudioTheme.Warning;Status.Text="Hesap iptal edildi; proje değişmedi."; }
        catch(Exception ex) { InvalidatePreview();Status.Foreground=StudioTheme.Warning;Status.Text="Bedenler hazırlanamadı: "+ex.Message; }
        finally { cancellation?.Dispose();cancellation=null;Entries.IsEnabled=true;IncludeSize.IsEnabled=true;CopyRole.IsEnabled=true;PreviewButton.IsEnabled=true;CancelButton.IsEnabled=false; }
    }
    private static string SafeName(string input)
    {
        var result=new System.Text.StringBuilder();
        for(int i=0;i<input.Length;i++)
        {
            var c=input[i];if(char.IsHighSurrogate(c) && i+1<input.Length && char.IsLowSurrogate(input[i+1]))
            { var low=input[++i];var category=System.Globalization.CharUnicodeInfo.GetUnicodeCategory(input,i-1);if(category==System.Globalization.UnicodeCategory.Format)result.Append($"\\u{(int)c:X4}\\u{(int)low:X4}");else result.Append(c).Append(low);continue; }
            var cat=char.GetUnicodeCategory(c);if(char.IsSurrogate(c) || cat is System.Globalization.UnicodeCategory.Control or System.Globalization.UnicodeCategory.Format or System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator)result.Append($"\\u{(int)c:X4}");else result.Append(c);
        }
        return result.ToString();
    }
    internal static void ValidateGrainTransfer(PatternPiece source,PatternPiece generated)
    {
        if(source.Grainline is { } line)
        {
            if(generated.Grainline is not { } result || ReferenceEquals(line,result) || result.Start!=line.Start || result.End!=line.End || !result.NeedsReview)
                throw new InvalidOperationException("Yeni bedenin iplik yönü beklenen şekilde korunamadı; sonuç projeye eklenmedi.");
        }
        else if(generated.Grainline is not null)
            throw new InvalidOperationException("Yeni bedende beklenmeyen iplik yönü bulundu; sonuç projeye eklenmedi.");
    }
    internal void AddReady()
    {
        var committed=false;
        try
        {
            if(cancellation is not null || ready is null || ready.Count==0 || Entries.Text!=readyText || (IncludeSize.IsChecked==true)!=readySize || (CopyRole.IsChecked==true)!=readyRole || !Current())
                throw new InvalidOperationException("Proje veya girdiler değişti; bedenleri yeniden hesaplayın.");
            var items=ready.Select(p=>p.Clone()).ToArray();
            workspace.CommitObserved("Bedenler birlikte oluşturuldu",()=>workspace.Document.Pieces.AddRange(items),()=>committed=true);
            ready=null;AddButton.IsEnabled=false;added();Close();
        }
        catch(Exception ex)
        {
            InvalidatePreview();Status.Foreground=StudioTheme.Warning;
            Status.Text=committed ? "Bedenler projeye eklendi; görünüm yenilenemedi. Ana çalışma alanını kontrol edin: "+ex.Message : "Bedenler eklenmedi: "+ex.Message;
        }
    }
}
