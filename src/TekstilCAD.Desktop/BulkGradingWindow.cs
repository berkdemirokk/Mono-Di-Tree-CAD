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
    internal ListBox MarkComparison { get; } = new() { Height=130,Margin=new Thickness(0,4,0,0) };
    private readonly Expander markComparisonSection=new() { Header="Köşe işaretlerini koordinatlarla karşılaştır",Margin=new Thickness(0,6,0,0),Visibility=Visibility.Collapsed };
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
        panel.Children.Add(new TextBlock { Text="Her satıra beden adı; adım sayısı; isteğe bağlı kesim adedi yazın. Örnek: S; -1; 12 ve L; 1; 8 ayrı satırlarda. Her beden temel kalıptan hesaplanır. Adımlar virgül veya noktayla ondalıklı olabilir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0) });
        AutomationProperties.SetName(Entries,"Beden adları ve adımları");AutomationProperties.SetHelpText(Entries,"Her satıra beden adı, adım sayısı ve isteğe bağlı kesim adedi girin; örnek S; -1; 12.");panel.Children.Add(Entries);
        var options=new WrapPanel();options.Children.Add(IncludeSize);options.Children.Add(CopyRole);panel.Children.Add(options);
        panel.Children.Add(new TextBlock { Text="Adet kaynaktan kopyalanmaz; her beden için üçüncü alana girilebilir. Sıfır adım veya sıfır artış, temel kalıpla aynı geometriyi oluşturabilir.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Muted });
        if(this.source.Grainline is not null)
            panel.Children.Add(new TextBlock { Text="Tüm noktalar eşit ötelenirse iplik yönü de ötelenir; diğer durumlarda aynı koordinatlarda kalır. Her durumda kontrol bekler. Her bedenin konturuyla ilişkisini kontrol edin; aktarım üretime uygunluk onayı değildir.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,6,0,0) });
        if(this.source.Notches is { Count:>0 })
            panel.Children.Add(new TextBlock { Text="Köşe işaretleri aynı indeksli noktalara aktarılır. Karşılaştırmada temel kalıp büyük sarı, seçili beden küçük turkuaz halkadır; üst üste gelebilir. Bir tarafta 500'den fazla işaret varsa hiçbiri çizilmez. Her bedeni kontrol edin; görsel işaret fiziksel çentik veya kesim komutu değildir.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,6,0,0) });
        var actions=new WrapPanel();actions.Children.Add(PreviewButton);actions.Children.Add(AddButton);actions.Children.Add(CancelButton);panel.Children.Add(actions);
        AutomationProperties.SetName(PreviewList,"Hesaplanan bedenlerin önizleme listesi");panel.Children.Add(PreviewList);
        panel.Children.Add(new TextBlock { Text="Geometrileri karşılaştırmak için yukarıdan bir beden seçin. Konturlar: kesikli sarı temel kalıp, düz turkuaz seçili beden. İplik yönü çizgileri sarı ve kesikli turkuazdır; üst üste gelebilir. Her bedende konturla ilişkisini kontrol edin. Aynı koordinat ve ölçek kullanılır; ekran çizimi fiziksel ölçü değildir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0) });
        AutomationProperties.SetName(ChangeSummary,"Seçili bedenin karşılaştırma ve iplik yönü özeti");
        AutomationProperties.SetLiveSetting(ChangeSummary,AutomationLiveSetting.Polite);panel.Children.Add(ChangeSummary);
        panel.Children.Add(GeometryPreview);
        var markPanel=new StackPanel();
        markPanel.Children.Add(new TextBlock { Text="Her satır aynı indeksli temel ve seçili beden işaretinin X/Y konumlarını mm olarak gösterir. Sayılar gösterim için biçimlendirilir ve yuvarlanabilir. İşaretler fiziksel çentik veya üretim onayı değildir.",TextWrapping=TextWrapping.Wrap });
        AutomationProperties.SetName(MarkComparison,"Temel ve seçili beden köşe işareti koordinatları");
        AutomationProperties.SetHelpText(MarkComparison,"Ok tuşlarıyla işaret satırlarını gezin; her satır nokta indeksi ve iki konumu içerir.");
        VirtualizingPanel.SetIsVirtualizing(MarkComparison,true);VirtualizingPanel.SetVirtualizationMode(MarkComparison,VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(MarkComparison,true);markPanel.Children.Add(MarkComparison);
        markComparisonSection.Content=markPanel;panel.Children.Add(markComparisonSection);
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
        ready=null;readyText=null;AddButton.IsEnabled=false;PreviewList.ItemsSource=null;GeometryPreview.Clear();ClearMarkComparison();SetChangeSummary("");
        if(hadPreview) { Status.Foreground=StudioTheme.Warning;Status.Text="Girdiler değişti; bedenleri yeniden hesaplayın."; }
    }
    private void UpdateGeometryPreview()
    {
        ClearMarkComparison();
        if(ready is null || PreviewList.SelectedIndex<0 || PreviewList.SelectedIndex>=ready.Count) { GeometryPreview.Clear();SetChangeSummary("");return; }
        if(!Current()) { InvalidatePreview();Status.Foreground=StudioTheme.Warning;Status.Text="Proje veya artışlar değişti; önizleme geçersiz. Pencereyi kapatıp yeniden açın.";return; }
        var item=ready[PreviewList.SelectedIndex];GeometryPreview.SetPieces(source,item,SafeName(item.Name));
        if(source.Notches is { Count:>0 } marks && item.Notches is { } gradedMarks)
        {
            var byIndex=gradedMarks.ToDictionary(mark=>mark.VertexIndex);
            if(byIndex.Count==marks.Count && marks.All(mark=>byIndex.ContainsKey(mark.VertexIndex)))
            {
                static string Coordinate(Point2 p)=>$"X {MeasurementInput.Format(p.X)}, Y {MeasurementInput.Format(p.Y)} mm";
                MarkComparison.ItemsSource=marks.Select(mark=>$"Nokta {mark.VertexIndex}: temel {Coordinate(mark.Position)}; seçili beden {Coordinate(byIndex[mark.VertexIndex].Position)}").ToArray();
                markComparisonSection.Visibility=Visibility.Visible;
            }
        }
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
    private void ClearMarkComparison() { MarkComparison.ItemsSource=null;markComparisonSection.Visibility=Visibility.Collapsed; }
    private void SetChangeSummary(string message)
    {
        if(ChangeSummary.Text==message)return;
        ChangeSummary.Text=message;
        AutomationProperties.SetHelpText(ChangeSummary,message);
        if(message.Length>0 && UIElementAutomationPeer.CreatePeerForElement(ChangeSummary) is { } peer)
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    private static IReadOnlyList<GradeBatchEntry> Parse(string input,PatternPiece source,ProjectDocument snapshot)
    {
        var entries=GradeBatchInput.Parse(input);
        var existing=new HashSet<string>(snapshot.Pieces.Select(p=>p.Name.Trim()),StringComparer.OrdinalIgnoreCase);
        foreach(var entry in entries)
            if(existing.Contains($"{source.Name} · {entry.Size.Name}".Trim()))
                throw new ArgumentException($"{entry.Size.Name}: Bu beden projede zaten var; farklı bir ad seçin.");
        return entries;
    }
    internal async Task CalculateAsync()
    {
        if(cancellation is not null)return;
        InvalidatePreview();
        try
        {
            if(!Current())throw new InvalidOperationException("Proje veya artış taslağı değişti. Pencereyi kapatıp güncel kaynakla tekrar açın.");
            var text=Entries.Text;var include=IncludeSize.IsChecked==true;var copy=CopyRole.IsChecked==true;var entries=Parse(text,source,snapshot);var sizes=entries.Select(entry=>entry.Size).ToArray();
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
                    ValidateGrainTransfer(source,generated[i],increments,sizes[i].Steps,cts.Token);
                    GradeBatchInput.ApplyMetadata(generated[i],source,entries[i],include,copy);
                }
                var candidate=snapshot.Clone();candidate.Pieces.AddRange(generated.Select(p=>p.Clone()));MainWindow.Validate(candidate);
                var labels=new string[generated.Count];
                for(var i=0;i<generated.Count;i++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    var item=generated[i];var bounds=Geometry.Bounds(item.Points);var unchanged=item.Points.SequenceEqual(source.Points);
                    labels[i]=$"{SafeName(item.Name)} · {MeasurementInput.Format(bounds.Width)} × {MeasurementInput.Format(bounds.Height)} mm"+(unchanged?" · temel geometriyle aynı":"")+(entries[i].Quantity is int quantity?$" · adet {quantity}":" · adet girilmedi");
                }
                cts.Token.ThrowIfCancellationRequested();return (Generated:generated,Labels:labels);
            });
            cts.Token.ThrowIfCancellationRequested();
            if(!Current() || Entries.Text!=text || (IncludeSize.IsChecked==true)!=include || (CopyRole.IsChecked==true)!=copy)throw new InvalidOperationException("Proje veya girdiler değişti; sonuç uygulanmadı.");
            ready=items.Generated;readyText=text;readySize=include;readyRole=copy;
            PreviewList.ItemsSource=items.Labels;
            PreviewList.SelectedIndex=0;
            Status.Foreground=source.Grainline is null && source.Notches is not { Count:>0 } ? StudioTheme.Text : StudioTheme.Warning;Status.Text=$"{items.Generated.Count} beden hazır. Ölçüleri inceleyin; ekleme tek geri alma işlemidir. Adet yalnız girildiğinde eklenir."+(source.Grainline is null ? "" : " İplik yönü her bedende kontrol bekliyor; konturla ilişkisini kontrol edin.")+(source.Notches is { Count:>0 } ? (GeometryPreview.MarksSuppressed ? " İşaretler 500 sınırı nedeniyle gösterilmiyor; her bedeni kontrol edin." : " Köşe işaretleri önizlemede gösterilir; her bedeni kontrol edin.") : "");AddButton.IsEnabled=true;
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
    internal static void ValidateGrainTransfer(PatternPiece source,PatternPiece generated,IReadOnlyList<Point2> increments,double steps,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(source.Grainline is { } line)
        {
            var uniform=increments.Count>0;
            for(var i=1;i<increments.Count;i++) { if((i & 255)==0)cancellationToken.ThrowIfCancellationRequested();if(increments[i]!=increments[0]) { uniform=false;break; } }
            var shift=uniform ? increments[0]*steps : new Point2(0,0);
            if(generated.Grainline is not { } result || ReferenceEquals(line,result) || result.Start!=line.Start+shift || result.End!=line.End+shift || !result.NeedsReview)
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
