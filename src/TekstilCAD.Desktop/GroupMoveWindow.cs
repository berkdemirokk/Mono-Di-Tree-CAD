using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>Choose multiple live pieces for one detached, atomic translation.</summary>
internal sealed class GroupMoveWindow : Window
{
    private sealed record Choice(Guid Id,string Label) { public override string ToString()=>Label; }
    private readonly MainWindow workspace;
    private readonly ListBox choices=new() { SelectionMode=SelectionMode.Multiple,MinHeight=180,MaxHeight=350 };
    private readonly TextBlock selectionCount=new() { Margin=new Thickness(0,4,0,4) };
    private readonly Button selectAll=new() { Content="Tümünü seç",Margin=new Thickness(0,0,8,0) };
    private readonly Button clearSelection=new() { Content="Seçimi temizle" };
    private readonly TextBox dx=new() { Text="0",Margin=new Thickness(0,4,0,8) };
    private readonly TextBox dy=new() { Text="0",Margin=new Thickness(0,4,0,8) };
    private readonly TextBlock status=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0) };
    private readonly Button apply=new() { Content="Seçilenleri taşı",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,0) };
    private readonly Button stop=new() { Content="Hesabı iptal et",Padding=new Thickness(12,8,12,8),Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,8,0) };
    private CancellationTokenSource? cancellation;
    private long revision;
    private bool closed;
    internal Action? BeforeCalculationForVerification { get; set; }
    internal Task? TaskForVerification { get; private set; }
    internal ListBox ChoicesForVerification=>choices;
    internal Button SelectAllForVerification=>selectAll;
    internal Button ClearSelectionForVerification=>clearSelection;
    internal string SelectionCountForVerification=>selectionCount.Text;
    internal TextBox XForVerification=>dx;
    internal TextBox YForVerification=>dy;
    internal string StatusForVerification=>status.Text;
    internal Task ApplyForVerification()=>MoveAsync();
    internal void CancelForVerification()=>cancellation?.Cancel();

    internal GroupMoveWindow(MainWindow workspace)
    {
        this.workspace=workspace;Owner=workspace;Title="Birden fazla parçayı taşı";Width=680;Height=680;MinWidth=490;MinHeight=450;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var buttons=new WrapPanel { Margin=new Thickness(0,8,0,0) };DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        buttons.Children.Add(apply);buttons.Children.Add(stop);
        var close=new Button { Content="Kapat",IsCancel=true,Padding=new Thickness(12,8,12,8) };buttons.Children.Add(close);
        var scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto };root.Children.Add(scroll);var body=new StackPanel();scroll.Content=body;
        body.Children.Add(new TextBlock { Text="En az iki parça seçin. Bir parçaya tıklayarak seçime ekleyin veya seçimden çıkarın. Parça adı ve kimliği birlikte gösterilir.",TextWrapping=TextWrapping.Wrap });
        choices.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        VirtualizingPanel.SetIsVirtualizing(choices,true);VirtualizingPanel.SetVirtualizationMode(choices,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(choices,true);
        foreach(var piece in workspace.Document.Pieces)choices.Items.Add(new Choice(piece.Id,$"{SafeName(piece.Name)} · {piece.Id:D}"));
        AutomationProperties.SetName(choices,"Birlikte taşınacak parçalar");AutomationProperties.SetHelpText(choices,"Bir parçaya tıklayarak veya boşluk tuşuna basarak seçimini değiştirin; kimlikler aynı adlı parçaları ayırır.");
        body.Children.Add(choices);
        AutomationProperties.SetLiveSetting(selectionCount,AutomationLiveSetting.Polite);body.Children.Add(selectionCount);
        var selectionActions=new WrapPanel { Margin=new Thickness(0,0,0,8) };selectionActions.Children.Add(selectAll);selectionActions.Children.Add(clearSelection);body.Children.Add(selectionActions);
        selectAll.Click+=(_,_)=>choices.SelectAll();clearSelection.Click+=(_,_)=>choices.UnselectAll();
        body.Children.Add(new TextBlock { Text="X yönünde taşıma (mm)" });AutomationProperties.SetName(dx,"X yönünde taşıma milimetre");body.Children.Add(dx);
        body.Children.Add(new TextBlock { Text="Y yönünde taşıma (mm)" });AutomationProperties.SetName(dy,"Y yönünde taşıma milimetre");body.Children.Add(dy);
        body.Children.Add(new TextBlock { Text="Seçilen parçalar ve iplik yönleri aynı miktarla taşınır. Kaynakla eşleşen serileme kuralları da taşınır; eski kurallar korunur ve bildirilir. Küçük ayrıntıların ve üretim ölçülerinin korunumu garanti edilmez. Bir parça güvenle taşınamazsa hiçbiri uygulanmaz.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,6,0,0) });
        AutomationProperties.SetName(status,"Toplu taşıma durumu");AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);body.Children.Add(status);
        choices.SelectionChanged+=(_,_)=> { revision++;UpdateSelectionCount(); };UpdateSelectionCount();dx.TextChanged+=(_,_)=>revision++;dy.TextChanged+=(_,_)=>revision++;
        apply.Click+=(_,_)=>TaskForVerification=MoveAsync();stop.Click+=(_,_)=> { cancellation?.Cancel();status.Text="İptal isteniyor; proje henüz değiştirilmedi."; };
        close.Click+=(_,_)=>Close();Closed+=(_,_)=> { closed=true;cancellation?.Cancel(); };
    }
    private void UpdateSelectionCount()=>selectionCount.Text=$"{choices.SelectedItems.Count} / {choices.Items.Count} parça seçildi";
    private static string SafeName(string? name)
    {
        if(string.IsNullOrEmpty(name))return "(boş)";
        return new string(name.Take(128).Select(c=>char.IsControl(c)||char.IsSurrogate(c)||char.GetUnicodeCategory(c)==UnicodeCategory.Format ? '?' : c).ToArray())+(name.Length>128 ? "…" : "");
    }
    private void Busy(bool busy)
    {
        choices.IsEnabled=selectAll.IsEnabled=clearSelection.IsEnabled=dx.IsEnabled=dy.IsEnabled=apply.IsEnabled=!busy;
        stop.Visibility=busy?Visibility.Visible:Visibility.Collapsed;stop.IsEnabled=busy;
    }
    private async Task MoveAsync()
    {
        if(closed || cancellation is not null)return;
        if(workspace.HasPendingCanvasOrPropertyWork) { status.Text="Önce bekleyen çizim veya alan taslaklarını tamamlayın.";return; }
        var ids=choices.SelectedItems.Cast<Choice>().Select(c=>c.Id).ToArray();
        if(ids.Length<2 || ids.Distinct().Count()!=ids.Length) { status.Text="Farklı en az iki parça seçin.";return; }
        if(!MeasurementInput.TryParseMillimeters(dx.Text,200_000_000,out var mx) || !MeasurementInput.TryParseMillimeters(dy.Text,200_000_000,out var my))
        { status.Text="Geçerli X ve Y taşıma miktarları girin (mm).";return; }
        if(mx==0 && my==0) { status.Text="Taşıma miktarı sıfır; proje ve geçmiş değiştirilmedi.";return; }
        var document=workspace.Document;var snapshot=document.Clone();var selected=workspace.SelectedId;var selectionRevision=revision;
        var set=ids.ToHashSet();var sources=snapshot.Pieces.Where(p=>set.Contains(p.Id)).ToArray();
        if(sources.Length!=ids.Length) { status.Text="Seçili parçalar artık mevcut değil; pencereyi yeniden açın.";return; }
        using var cts=new CancellationTokenSource();cancellation=cts;Busy(true);status.Text="Parçalar arka planda doğrulanıyor; proje henüz değiştirilmedi.";
        var committed=false;
        try
        {
            var before=BeforeCalculationForVerification;
            var moved=await Task.Run(()=>
            {
                before?.Invoke();var token=cts.Token;token.ThrowIfCancellationRequested();ProjectStorage.Validate(snapshot);
                // Existing translation precision checks also compare the relative anchor
                // positions across pieces, preserving group layout to the same budget.
                var anchors=sources.Select(p=>p.Points[0]).ToArray();var movedAnchors=Geometry.Translate(anchors,mx,my);
                var results=new Dictionary<Guid,PatternPiece>();
                var rules=snapshot.GradingRules.ToDictionary(r=>r.SourcePieceId);
                var movedRules=new Dictionary<Guid,GradingRuleSet>();var staleRules=new List<Guid>();
                for(var i=0;i<sources.Length;i++)
                {
                    token.ThrowIfCancellationRequested();var result=sources[i].Clone();
                    (result.Points,result.Notches)=MarkedPieceTranslation.Translate(sources[i],mx,my,token);
                    result.Grainline=GrainlineTransform.Translate(sources[i].Grainline,sources[i].Points[0],result.Points[0],mx,my);
                    if((mx!=0 && result.Points[0].X==sources[i].Points[0].X) || (my!=0 && result.Points[0].Y==sources[i].Points[0].Y))
                        throw new ArgumentException("Taşıma miktarı bir parçada sayısal hassasiyetle temsil edilemiyor.");
                    if(result.Points[0]!=movedAnchors[i])throw new InvalidOperationException("Parçalar aynı taşıma ile güvenle hesaplanamadı.");
                    if(rules.TryGetValue(result.Id,out var rule))
                    {
                        var movedRule=rule.TranslatedFor(sources[i],result.Points,mx,my);
                        if(movedRule is null)staleRules.Add(result.Id);else movedRules.Add(result.Id,movedRule);
                    }
                    results.Add(result.Id,result);
                }
                token.ThrowIfCancellationRequested();return (Pieces:results,Rules:movedRules,Stale:staleRules);
            },cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if(closed || workspace.IsWindowClosed)return;
            if(revision!=selectionRevision || !ReferenceEquals(workspace.Document,document) || workspace.SelectedId!=selected ||
               workspace.HasPendingCanvasOrPropertyWork || !DocumentSession.ContentEquals(workspace.Document,snapshot))
            { status.Text="Proje, seçim veya girişler değişti; işlem uygulanmadı. Pencereyi yeniden açın.";return; }
            if(!workspace.CanRetainCurrentUndoSnapshot)
            { status.Text="Proje geri alma sınırını aşıyor; taşıma uygulanmadı ve geçmiş korunuyor.";return; }
            cts.Token.ThrowIfCancellationRequested();
            workspace.CommitObserved("Parçalar birlikte taşındı",()=>
            {
                for(int i=0;i<workspace.Document.Pieces.Count;i++)
                    if(moved.Pieces.TryGetValue(workspace.Document.Pieces[i].Id,out var item))workspace.Document.Pieces[i]=item;
                for(int i=0;i<workspace.Document.GradingRules.Count;i++)
                    if(moved.Rules.TryGetValue(workspace.Document.GradingRules[i].SourcePieceId,out var rule))workspace.Document.GradingRules[i]=rule;
            },()=>committed=true);
            status.Text=$"{moved.Pieces.Count} parça taşındı; işlem ana çalışma alanında tek adımda geri alınabilir. Ölçüleri kontrol edin."+
                (moved.Stale.Count==0 ? "" : $" {moved.Stale.Count} eski serileme kuralı korundu; serileme atölyesinde gözden geçirin. Parça kimlikleri: "+string.Join(", ",moved.Stale.Take(3))+(moved.Stale.Count>3 ? " ve diğerleri." : "."));
            apply.IsEnabled=false;
        }
        catch(OperationCanceledException) when(cts.IsCancellationRequested && !committed)
        { if(!closed)status.Text="Toplu taşıma iptal edildi; proje değiştirilmedi."; }
        catch(Exception ex) when(ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            if(!closed)status.Text=committed ? "Taşıma projeye uygulandı ancak görünüm yenilenemedi. Ana çalışma alanını ve geri alma geçmişini kontrol edin: "+ex.Message : "Toplu taşıma uygulanmadı: "+ex.Message;
        }
        finally { if(ReferenceEquals(cancellation,cts))cancellation=null;if(!closed) { Busy(false);if(committed)apply.IsEnabled=false; } }
    }
}
