using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;

namespace TekstilCAD.Desktop;

/// <summary>Preview exact duplicate removal across a fixed project snapshot before one transaction.</summary>
internal sealed class BulkDuplicateCleanupWindow : Window
{
    private readonly MainWindow workspace;
    private readonly Func<PatternPiece,GradingRuleSet?,bool> hasDraft;
    private readonly Action<IReadOnlyCollection<Guid>,ProjectDocument>? resetCleanDrafts;
    private readonly ListBox report=new() { MinHeight=180,MaxHeight=330,HorizontalContentAlignment=HorizontalAlignment.Stretch };
    private readonly TextBlock status=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0) };
    private readonly Button preview=new() { Content="Önizlemeyi hesapla",Margin=new Thickness(4),Padding=new Thickness(10,6,10,6) };
    private readonly Button apply=new() { Content="Önizlenenleri temizle",Margin=new Thickness(4),Padding=new Thickness(10,6,10,6),IsEnabled=false };
    private readonly Button cancel=new() { Content="Hesabı iptal et",Margin=new Thickness(4),Padding=new Thickness(10,6,10,6),Visibility=Visibility.Collapsed };
    private CancellationTokenSource? cancellation;private bool closed;
    private ProjectDocument? original,snapshot;private Guid? selected;
    private Dictionary<Guid,bool>? draftStates;private Dictionary<Guid,PatternPiece>? ready;
    private Dictionary<Guid,GradingRuleSet>? readyRules,sourceRules;
    internal Task? TaskForVerification { get; private set; }
    internal Action? BeforeCalculationForVerification { get; set; }
    internal Task PreviewForVerification()=>PrepareAsync();
    internal void ApplyForVerification()=>Apply();
    internal void CancelForVerification()=>cancellation?.Cancel();
    internal string StatusForVerification=>status.Text;
    internal int ReadyCountForVerification=>ready?.Count ?? 0;
    internal IReadOnlyList<string> ReportForVerification=>report.Items.Cast<string>().ToArray();

    internal BulkDuplicateCleanupWindow(MainWindow workspace,Window owner,Func<PatternPiece,GradingRuleSet?,bool> hasDraft,Action<IReadOnlyCollection<Guid>,ProjectDocument>? resetCleanDrafts=null)
    {
        this.workspace=workspace;this.hasDraft=hasDraft;this.resetCleanDrafts=resetCleanDrafts;Owner=owner;Title="Bitişik tekrarları topluca incele";Width=720;Height=620;MinWidth=500;MinHeight=400;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel();DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var close=new Button { Content="Kapat",IsCancel=true,Margin=new Thickness(4),Padding=new Thickness(10,6,10,6) };
        actions.Children.Add(preview);actions.Children.Add(apply);actions.Children.Add(cancel);actions.Children.Add(close);
        var scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto };root.Children.Add(scroll);var body=new StackPanel();scroll.Content=body;
        body.Children.Add(new TextBlock { Text="Tüm proje parçaları incelenir. Yalnız bitçe aynı bitişik nokta kopyaları kaldırılır. İşaretler yalnız indeksleri ve bitleri güvenle korunabiliyorsa eşlenir; işaretler fiziksel çentik değildir. Kayıtlı serileme kuralları yalnız kaynak ve birleşen artışlar bitçe tam eşleşirse korunur; uygulanmamış taslaklar atlanır. Diğer geometri hataları kalabilir; sonuç üretim veya DXF uygunluğu onayı değildir. Kaynak dosyalar ve geçmiş değiştirilmez. Önce listeyi inceleyin, sonra açıkça uygulayın.",TextWrapping=TextWrapping.Wrap });
        AutomationProperties.SetName(report,"Toplu nokta temizleme önizlemesi ve atlanan parçalar");
        report.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));VirtualizingPanel.SetIsVirtualizing(report,true);ScrollViewer.SetCanContentScroll(report,true);body.Children.Add(report);
        AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);AutomationProperties.SetName(status,"Toplu temizleme durumu");body.Children.Add(status);
        preview.Click+=(_,_)=>TaskForVerification=PrepareAsync();apply.Click+=(_,_)=>Apply();cancel.Click+=(_,_)=>cancellation?.Cancel();close.Click+=(_,_)=>Close();
        Closed+=(_,_)=> { closed=true;cancellation?.Cancel(); };
    }
    private void SetStatus(string message)
    {
        status.Text=message;AutomationProperties.SetName(status,"Toplu temizleme durumu: "+message);
        if(UIElementAutomationPeer.CreatePeerForElement(status) is { } peer)peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    private static string SafeName(string? name)=>string.IsNullOrEmpty(name) ? "(boş)" : string.Concat(name.Take(100).Select(c=>char.IsControl(c)||char.IsSurrogate(c)||char.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator ? $"\\u{(int)c:X4}" : c.ToString()));
    private void Busy(bool busy) { preview.IsEnabled=!busy;apply.IsEnabled=!busy && ready is { Count:>0 };cancel.Visibility=busy?Visibility.Visible:Visibility.Collapsed;cancel.IsEnabled=busy; }
    private async Task PrepareAsync()
    {
        if(closed || cancellation is not null)return;
        ready=null;readyRules=null;sourceRules=null;apply.IsEnabled=false;report.ItemsSource=null;
        if(workspace.HasPendingCanvasOrPropertyWork) { SetStatus("Önce bekleyen çizim veya alan taslaklarını tamamlayın.");return; }
        var document=workspace.Document;var copy=document.Clone();var selection=workspace.SelectedId;
        var rules=copy.GradingRules.ToDictionary(r=>r.SourcePieceId);
        var drafts=copy.Pieces.ToDictionary(p=>p.Id,p=>hasDraft(p,rules.GetValueOrDefault(p.Id)));
        using var cts=new CancellationTokenSource();cancellation=cts;Busy(true);SetStatus("Önizleme hazırlanıyor; proje değiştirilmedi.");
        try
        {
            var before=BeforeCalculationForVerification;
            var calculated=await Task.Run(()=>
            {
                before?.Invoke();var token=cts.Token;token.ThrowIfCancellationRequested();ProjectStorage.Validate(copy,token);
                var changes=new Dictionary<Guid,PatternPiece>();var mappedRules=new Dictionary<Guid,GradingRuleSet>();var details=new List<string>();
                foreach(var piece in copy.Pieces)
                {
                    token.ThrowIfCancellationRequested();var label=$"{SafeName(piece.Name)} · {piece.Id:D}";
                    if(drafts.GetValueOrDefault(piece.Id))
                    { details.Add(label+" · atlandı: uygulanmamış serileme taslağı var");continue; }
                    var adjacent=false;for(int i=1;i<piece.Points.Count;i++) { if((i&1023)==0)token.ThrowIfCancellationRequested();if(piece.Points[i]==piece.Points[i-1]) { adjacent=true;break; } }
                    if(!adjacent) { details.Add(label+" · bitişik tekrar yok");continue; }
                    try
                    {
                        var edited=PatternTools.RemoveConsecutiveDuplicateVertices(piece,token);
                        GradingRuleSet? mapped=null;
                        if(rules.TryGetValue(piece.Id,out var rule))
                        {
                            mapped=rule.WithDuplicateRemovalFor(piece,edited,token);
                            if(mapped is null) { details.Add(label+" · atlandı: serileme kuralı kaynak veya birleşen artışlarla bitçe eşleşmiyor");continue; }
                        }
                        changes.Add(piece.Id,edited);if(mapped is not null)mappedRules.Add(piece.Id,mapped);
                        var markCount=piece.Notches?.Count ?? 0;
                        var markerSummary=markCount==0 ? "" : $" · {markCount} görsel işaret eşlenecek: "+
                            string.Join(", ",piece.Notches!.Zip(edited.Notches!).Take(8).Select(pair=>$"{pair.First.VertexIndex}→{pair.Second.VertexIndex}"))+
                            (markCount>8 ? $" ve {markCount-8} diğer işaret" : "");
                        details.Add(label+$" · {piece.Points.Count-edited.Points.Count} kopya temizlenecek"+markerSummary+(mapped is null ? "" : " · serileme kuralı bitçe eşlenecek"));
                    }
                    catch(ArgumentException ex) { details.Add(label+" · atlandı: "+ex.Message); }
                }
                var candidate=copy.Clone();for(int i=0;i<candidate.Pieces.Count;i++)if(changes.TryGetValue(candidate.Pieces[i].Id,out var item))candidate.Pieces[i]=item.Clone();
                for(int i=0;i<candidate.GradingRules.Count;i++)if(mappedRules.TryGetValue(candidate.GradingRules[i].SourcePieceId,out var mapped))candidate.GradingRules[i]=mapped.Clone();
                ProjectStorage.Validate(candidate,token);token.ThrowIfCancellationRequested();return (changes,mappedRules,details);
            },cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if(!Current(document,copy,selection,drafts,rules)) { SetStatus("Proje, seçim veya taslak değişti; önizleme geçersiz. Yeniden hesaplayın.");return; }
            report.ItemsSource=calculated.details;
            original=document;snapshot=copy;selected=selection;draftStates=drafts;ready=calculated.changes;readyRules=calculated.mappedRules;sourceRules=rules;
            SetStatus($"{ready.Count} parça temizlenebilir; {copy.Pieces.Count-ready.Count} parça temizlenmeyecek. Listeyi inceleyip uygulamayı seçin. Diğer hatalar kalabilir.");
        }
        catch(OperationCanceledException) when(cts.IsCancellationRequested) { if(!closed)SetStatus("Önizleme iptal edildi; proje değiştirilmedi."); }
        catch(Exception ex) when(ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) { if(!closed)SetStatus("Önizleme hazırlanamadı: "+ex.Message); }
        finally { if(ReferenceEquals(cancellation,cts))cancellation=null;if(!closed)Busy(false); }
    }
    private bool Current(ProjectDocument document,ProjectDocument copy,Guid? selection,Dictionary<Guid,bool> drafts,Dictionary<Guid,GradingRuleSet> rules)=>
        !closed && !workspace.IsWindowClosed && !workspace.HasPendingCanvasOrPropertyWork && ReferenceEquals(workspace.Document,document) && workspace.SelectedId==selection &&
        DocumentSession.ContentEquals(workspace.Document,copy) && copy.Pieces.All(piece=>drafts.TryGetValue(piece.Id,out var pending) && hasDraft(piece,rules.GetValueOrDefault(piece.Id))==pending);
    private void Apply()
    {
        if(cancellation is not null || ready is not { Count:>0 } changes || original is null || snapshot is null || draftStates is null || readyRules is null || sourceRules is null)return;
        if(!Current(original,snapshot,selected,draftStates,sourceRules)) { ready=null;apply.IsEnabled=false;SetStatus("Proje, seçim veya taslak değişti; işlem uygulanmadı. Önizlemeyi yenileyin.");return; }
        if(!workspace.CanRetainCurrentUndoSnapshot) { SetStatus("Proje geri alma sınırını aşıyor; işlem uygulanmadı.");return; }
        var mappedRules=readyRules;var sourceSnapshot=snapshot;
        var committed=false;
        try
        {
            workspace.CommitObserved("Bitişik nokta tekrarları temizlendi",()=>
            {
                for(var i=0;i<workspace.Document.Pieces.Count;i++)if(changes.TryGetValue(workspace.Document.Pieces[i].Id,out var item))workspace.Document.Pieces[i]=item;
                for(var i=0;i<workspace.Document.GradingRules.Count;i++)if(mappedRules.TryGetValue(workspace.Document.GradingRules[i].SourcePieceId,out var mapped))workspace.Document.GradingRules[i]=mapped;
            },()=> { committed=true;resetCleanDrafts?.Invoke(changes.Keys.ToArray(),sourceSnapshot); });
            ready=null;apply.IsEnabled=false;SetStatus($"{changes.Count} parça temizlendi; tek adımda geri alınabilir. Diğer hatalar kalabilir; kaynak ve pastal geçmişini kontrol edin.");
        }
        catch(Exception ex) when(ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        { ready=null;apply.IsEnabled=false;SetStatus(committed ? "İşlem uygulandı ancak görünüm yenilenemedi; tekrar denemeden sonucu ve geçmişi kontrol edin: "+ex.Message : "İşlem uygulanmadı: "+ex.Message); }
    }
}
