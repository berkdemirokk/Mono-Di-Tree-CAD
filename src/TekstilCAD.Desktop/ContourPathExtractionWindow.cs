using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;

namespace TekstilCAD.Desktop;

/// <summary>Preview a contour interval as a separate open path without changing its source.</summary>
internal sealed class ContourPathExtractionWindow : Window
{
    private readonly MainWindow workspace;
    private readonly Func<Guid?> selectedSource;
    private readonly Guid sourceId;
    private readonly Func<PatternPiece,GradingRuleSet?,bool>? hasDraft;
    private readonly CheckBox reverse=new() { Content="Seçilen kenarın yönünü ters çevir",Margin=new Thickness(0,8,0,4) };
    private readonly CheckBox copyRules=new() { Content="Kayıtlı serileme artışlarını açık kenara aktar",Margin=new Thickness(0,8,0,8) };
    private readonly ProjectDocument openingDocument,openingSnapshot;
    private readonly Guid? openingSelection;
    private readonly TextBox start=new() { Text="0",Margin=new Thickness(0,4,0,4) };
    private readonly TextBox end=new() { Text="1",Margin=new Thickness(0,4,0,4) };
    private readonly TextBlock status=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8) };
    private readonly ContentControl preview=new();
    private readonly Button calculate=new() { Content="Önizle",Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,0) };
    private readonly Button apply=new() { Content="Açık kenarı ekle",Padding=new Thickness(12,7,12,7),IsEnabled=false,Margin=new Thickness(0,0,8,0) };
    private CancellationTokenSource? cancellation;
    private ProjectDocument? document,snapshot;
    private Guid? selected;
    private ContourPathExtractionResult? proposal;
    private GradingRuleSet? proposalRule;
    private bool closed;
    internal CheckBox CopyRulesForVerification=>copyRules;
    internal CheckBox ReverseForVerification=>reverse;
    internal TextBox StartForVerification=>start;
    internal TextBox EndForVerification=>end;
    internal Button ApplyForVerification=>apply;
    internal string StatusForVerification=>status.Text;
    internal Action? BeforeExtractionForVerification { get; set; }
    internal Task CalculateForVerification()=>CalculateAsync();
    internal void CommitForVerification()=>Apply();

    internal ContourPathExtractionWindow(MainWindow workspace,Window owner,PatternPiece source,Func<Guid?> selectedSource,Func<PatternPiece,GradingRuleSet?,bool>? hasDraft=null)
    {
        this.workspace=workspace;this.selectedSource=selectedSource;this.hasDraft=hasDraft;sourceId=source.Id;
        openingDocument=workspace.Document;openingSelection=workspace.SelectedId;
        if(selectedSource()!=sourceId || !ReferenceEquals(openingDocument.Pieces.FirstOrDefault(p=>p.Id==sourceId),source) || workspace.HasPendingCanvasOrPropertyWork)
            throw new InvalidOperationException("Kaynak veya bekleyen çalışma değişti; kenar seçimini yeniden başlatın.");
        openingSnapshot=openingDocument.Clone();Owner=owner;Title="Konturdan açık kenar çıkar";
        Width=670;Height=640;MinWidth=500;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var buttons=new WrapPanel { Margin=new Thickness(0,8,0,0) };DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        buttons.Children.Add(calculate);buttons.Children.Add(apply);var close=new Button { Content="Kapat / iptal et",Padding=new Thickness(12,7,12,7) };buttons.Children.Add(close);
        var body=new StackPanel();root.Children.Add(new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Focusable=true });
        body.Children.Add(new TextBlock { Text="Konturdan açık kenar çıkar",FontSize=20,FontWeight=FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text="Başlangıç ve bitiş köşelerini seçin. Kenar, saklanan nokta sırasını ileri izler; bitiş indeksi küçükse kapanıştan geçer. Başlangıç ve bitişi değiştirmek karşı taraftaki aralığı seçer; aynı aralığın yönünü ters çevirmek için aşağıdaki seçeneği kullanın. Tekrarlı kapanış son indeksi ilk köşeyle aynıdır. Kaynak parça değişmez.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8) });
        body.Children.Add(new TextBlock { Text="Yalnız seçilen aralıktaki görsel köşe işaretleri kopyalanır; diğerleri kaynakta kalır. Serileme kuralı yalnız açıkça seçilirse ve kaynakla tam eşleşirse aktarılır. Yeni parçada üretim bilgisi, iplik yönü veya kaynak ilişkisi bulunmaz. İşaretler fiziksel çentik değildir; sonucu üretim öncesi inceleyin.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning });
        AutomationProperties.SetName(reverse,"Seçilen açık kenarın yönünü ters çevir");
        AutomationProperties.SetHelpText(reverse,"Aynı kontur aralığının nokta sırası, görsel işaretleri ve seçilirse serileme artışları birlikte ters çevrilir.");body.Children.Add(reverse);
        AutomationProperties.SetHelpText(copyRules,"Yalnız kayıtlı kural kaynakla bitçe eşleşiyorsa ve uygulanmamış serileme taslağı yoksa seçilen aralığın artışları kopyalanır.");body.Children.Add(copyRules);
        body.Children.Add(new TextBlock { Text="Kaynak: "+CadCanvas.CanvasPieceLabel(source.Name)+"\nKimlik: "+sourceId.ToString("D"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8) });
        void AddPoint(string title,TextBox input,string automationName)
        {
            body.Children.Add(new TextBlock { Text=title });AutomationProperties.SetName(input,automationName);body.Children.Add(input);
            var pick=new Button { Content="Konturdan seç…",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,0,6) };
            AutomationProperties.SetName(pick,title+" konturdan seç");body.Children.Add(pick);
            pick.Click+=(_,_)=>
            {
                var live=workspace.Document.Pieces.FirstOrDefault(p=>p.Id==sourceId);
                if(live is null || !OpeningCurrent()) { SetStatus("Kaynak değişti; pencereyi yeniden açın.");return; }
                var picker=new PointPickerWindow(workspace,this,live,selectedSource);
                if(picker.ShowDialog()==true && picker.SelectedPointIndex is int chosen)input.Text=chosen.ToString(CultureInfo.InvariantCulture);
            };
        }
        AddPoint("Başlangıç indeksi",start,"Kenar başlangıç noktası indeksi");AddPoint("Bitiş indeksi",end,"Kenar bitiş noktası indeksi");
        body.Children.Add(preview);AutomationProperties.SetName(status,"Kenar çıkarma durumu");AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);body.Children.Add(status);
        start.TextChanged+=(_,_)=>Invalidate();end.TextChanged+=(_,_)=>Invalidate();reverse.Checked+=(_,_)=>Invalidate();reverse.Unchecked+=(_,_)=>Invalidate();copyRules.Checked+=(_,_)=>Invalidate();copyRules.Unchecked+=(_,_)=>Invalidate();calculate.Click+=async (_,_)=>await CalculateAsync();apply.Click+=(_,_)=>Apply();close.Click+=(_,_)=>Close();
        Closed+=(_,_)=> { closed=true;cancellation?.Cancel(); };
    }
    private void SetStatus(string text) { status.Text=text;AutomationProperties.SetName(status,text);if(UIElementAutomationPeer.CreatePeerForElement(status) is { } peer)peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged); }
    private void Invalidate() { cancellation?.Cancel();proposal=null;proposalRule=null;document=null;snapshot=null;apply.IsEnabled=false;preview.Content=null;SetStatus("Önizleme hesaplanmadı."); }
    private bool OpeningCurrent()=> !workspace.IsWindowClosed && !workspace.HasPendingCanvasOrPropertyWork &&
        ReferenceEquals(workspace.Document,openingDocument) && workspace.SelectedId==openingSelection && selectedSource()==sourceId && DocumentSession.ContentEquals(workspace.Document,openingSnapshot);
    private bool Current(ProjectDocument original,ProjectDocument copy,Guid? selection) =>
        !closed && !workspace.IsWindowClosed && !workspace.HasPendingCanvasOrPropertyWork && ReferenceEquals(workspace.Document,original) &&
        workspace.SelectedId==selection && selectedSource()==sourceId && DocumentSession.ContentEquals(workspace.Document,copy);
    private async Task CalculateAsync()
    {
        if(cancellation is not null)return;Invalidate();
        if(!OpeningCurrent()) { SetStatus("Kaynak, seçim veya bekleyen çalışma değişti; pencereyi yeniden açın.");return; }
        if(!int.TryParse(start.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var a) || !int.TryParse(end.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var b))
        { SetStatus("Geçerli iki tam sayı indeksi girin.");return; }
        var original=workspace.Document;var copy=original.Clone();var selection=workspace.SelectedId;var source=copy.Pieces.FirstOrDefault(p=>p.Id==sourceId);
        if(source is null) { SetStatus("Kaynak parça bulunamadı.");return; }
        var reverseOutput=reverse.IsChecked==true;var carry=copyRules.IsChecked==true;var rule=copy.GradingRules.FirstOrDefault(r=>r.SourcePieceId==sourceId);
        if(carry && (rule is null || hasDraft is null || hasDraft(source,rule))) { SetStatus("Kayıtlı kural ve kaynakla eşleşen temiz taslak gereklidir; önce taslağı kontrol edin.");return; }
        using var tokenSource=new CancellationTokenSource();cancellation=tokenSource;calculate.IsEnabled=false;SetStatus("Kenar doğrulanıyor; kapatmak hesabı iptal eder.");
        try
        {
            var calculated=await Task.Run(()=> { BeforeExtractionForVerification?.Invoke();tokenSource.Token.ThrowIfCancellationRequested();
                var extracted=ContourPathExtraction.Extract(source,a,b,reverseOutput,tokenSource.Token);
                var mapped=carry ? ContourPathExtraction.ExtractGrading(source,rule!,extracted,tokenSource.Token) : null;
                if(carry && mapped is null)throw new InvalidOperationException("Kayıtlı serileme kuralı kaynak veya kenarla bitçe eşleşmiyor.");
                var candidate=copy.Clone();candidate.Pieces.Add(extracted.Piece);if(mapped is not null)candidate.GradingRules.Add(mapped);
                ProjectStorage.Validate(candidate,tokenSource.Token);return (extracted,mapped); },tokenSource.Token);
            if(closed || tokenSource.IsCancellationRequested)return;
            var result=calculated.extracted;
            if(!Current(original,copy,selection) || (carry && hasDraft!(source,rule))) { SetStatus("Kaynak veya proje değişti; önizlemeyi yenileyin.");return; }
            var display=new PointMeasurePreview(result.Piece,showMarkedVertices:true);display.SetSelection(0,result.Piece.Points.Count-1);preview.Content=display;
            document=original;snapshot=copy;selected=selection;proposal=result;proposalRule=calculated.mapped;apply.IsEnabled=true;
            SetStatus($"Önizleme: {result.StartIndex} → {result.EndIndex}; {(result.CrossesClosure ? "kapanıştan geçer" : "kapanıştan geçmez")}. {result.Piece.Points.Count} nokta, {result.Piece.Notches?.Count ?? 0} görsel işaret; kaynakta kalan {result.OmittedMarks} işaret sonuca aktarılmaz. "+(result.Reversed ? "Kenar yönü ters çevrildi. " : "")+
                (display.MarkersSuppressed ? "500'den fazla işaret önizlemede gösterilmiyor. " : "")+(carry ? "Kayıtlı artışlar açık kenara eşlendi; kullanım öncesi kontrol edin. " : "Yeni parçada serileme kuralı yoktur. ")+"Yeni parçada üretim bilgisi yoktur.");
        }
        catch(OperationCanceledException) { if(!closed)SetStatus("İşlem iptal edildi; proje değişmedi."); }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or ArithmeticException or System.IO.InvalidDataException)
        { if(!closed)SetStatus("Önizleme oluşturulamadı: "+ex.Message); }
        finally { if(ReferenceEquals(cancellation,tokenSource))cancellation=null;if(!closed)calculate.IsEnabled=true; }
    }
    private void Apply()
    {
        if(proposal is null || document is null || snapshot is null || !Current(document,snapshot,selected))
        { Invalidate();SetStatus("Proje veya seçim değişti; önizlemeyi yenileyin.");return; }
        if(proposalRule is not null && (hasDraft is null || hasDraft(snapshot.Pieces.Single(p=>p.Id==sourceId),snapshot.GradingRules.FirstOrDefault(r=>r.SourcePieceId==sourceId))))
        { Invalidate();SetStatus("Serileme taslağı değişti; önizlemeyi yenileyin.");return; }
        if(!workspace.CanRetainCurrentUndoSnapshot) { SetStatus("Proje geri alma sınırını aşıyor; işlem uygulanmadı.");return; }
        var result=proposal.Piece.Clone();
        var candidate=workspace.Document.Clone();candidate.Pieces.Add(result);if(proposalRule is not null)candidate.GradingRules.Add(proposalRule.Clone());
        if(!workspace.CanRetainUndoSnapshot(candidate)) { SetStatus("Sonuç yineleme geçmişi sınırını aşıyor; işlem uygulanmadı.");return; }
        var committed=false;apply.IsEnabled=false;
        try
        {
            workspace.CommitObserved("Konturdan açık kenar çıkarıldı",()=> { workspace.Document.Pieces.Add(result);if(proposalRule is not null)workspace.Document.GradingRules.Add(proposalRule.Clone()); },()=>committed=true);
            proposal=null;SetStatus("Açık kenar eklendi; kaynak korundu. Geri al ile sonuç kaldırılabilir.");
        }
        catch(Exception ex) when(ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        { proposal=null;SetStatus(committed ? "Kenar eklendi ancak görünüm yenilenemedi; geçmişi kontrol edin: "+ex.Message : "Kenar eklenmedi: "+ex.Message); }
    }
}
