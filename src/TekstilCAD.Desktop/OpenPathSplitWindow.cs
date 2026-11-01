using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;

namespace TekstilCAD.Desktop;

/// <summary>Explicitly prepares two independent open paths without replacing their source.</summary>
internal sealed class OpenPathSplitWindow : Window
{
    private readonly MainWindow workspace;
    private readonly Func<Guid?> selectedSource;
    private readonly Guid sourceId;
    private readonly TextBox index=new() { Text="1",Margin=new Thickness(0,4,0,8) };
    private readonly TextBlock status=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8) };
    private readonly Button calculate=new() { Content="Önizle",Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,0) };
    private readonly Button apply=new() { Content="İki açık parçayı ekle",Padding=new Thickness(12,7,12,7),IsEnabled=false,Margin=new Thickness(0,0,8,0) };
    private CancellationTokenSource? cancellation;
    private ProjectDocument? original,snapshot;
    private Guid? selection;
    private (PatternPiece First,PatternPiece Second)? proposal;
    private bool closed;
    internal TextBox IndexForVerification=>index;
    internal Button ApplyForVerification=>apply;
    internal string StatusForVerification=>status.Text;
    internal Action? BeforeSplitForVerification { get; set; }
    internal Task CalculateForVerification()=>CalculateAsync();
    internal void CommitForVerification()=>Apply();

    internal OpenPathSplitWindow(MainWindow workspace,Window owner,PatternPiece source,Func<Guid?> selectedSource)
    {
        this.workspace=workspace;this.selectedSource=selectedSource;sourceId=source.Id;
        Owner=owner;Title="Açık konturu böl";Width=620;Height=470;MinWidth=480;MinHeight=360;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel { Margin=new Thickness(0,8,0,0) };DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        actions.Children.Add(calculate);actions.Children.Add(apply);var close=new Button { Content="Kapat / iptal et",Padding=new Thickness(12,7,12,7) };actions.Children.Add(close);
        var body=new StackPanel();root.Children.Add(new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Focusable=true });
        body.Children.Add(new TextBlock { Text="Açık konturu iki parçaya böl",FontSize=20,FontWeight=FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text="İç köşeyi seçin. İki yeni açık parça bu köşeyi paylaşır; kaynak parça ve kayıtlı kuralları değişmez. Köşe işaretleri ilgili parçalara kopyalanır; bölme noktasındaki işaret iki sonuçta da görünür. Bu işaretler fiziksel çentik değildir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8) });
        body.Children.Add(new TextBlock { Text="Yeni parçalara serileme kuralları, üretim bilgisi, iplik yönü veya kaynak ilişkisi aktarılmaz. Kullanım öncesi bunları ayrıca belirleyin; geometri üretim onayı değildir.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning });
        body.Children.Add(new TextBlock { Text="Kaynak: "+CadCanvas.CanvasPieceLabel(source.Name)+"\nKimlik: "+sourceId.ToString("D"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0) });
        body.Children.Add(new TextBlock { Text="Bölme noktası indeksi (0'dan başlar)" });AutomationProperties.SetName(index,"Açık konturu bölecek nokta indeksi");body.Children.Add(index);
        var pick=new Button { Content="Konturdan nokta seç…",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(10,5,10,5) };body.Children.Add(pick);
        AutomationProperties.SetName(status,"Kontur bölme durumu");AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);body.Children.Add(status);
        index.TextChanged+=(_,_)=>Invalidate();calculate.Click+=async (_,_)=>await CalculateAsync();apply.Click+=(_,_)=>Apply();close.Click+=(_,_)=>Close();
        pick.Click+=(_,_)=>
        {
            var live=workspace.Document.Pieces.FirstOrDefault(p=>p.Id==sourceId);
            if(live is null || selectedSource()!=sourceId) { SetStatus("Kaynak değişti; pencereyi yeniden açın.");return; }
            var picker=new PointPickerWindow(workspace,this,live,selectedSource);
            if(picker.ShowDialog()==true && picker.SelectedPointIndex is int chosen)index.Text=chosen.ToString(CultureInfo.InvariantCulture);
        };
        Closed+=(_,_)=> { closed=true;cancellation?.Cancel(); };
    }
    private void SetStatus(string text) { status.Text=text;AutomationProperties.SetName(status,text); }
    private void Invalidate() { cancellation?.Cancel();proposal=null;original=null;snapshot=null;apply.IsEnabled=false;SetStatus("Önizleme hesaplanmadı."); }
    private bool Current(ProjectDocument doc,ProjectDocument copy,Guid? selected) =>
        !closed && !workspace.IsWindowClosed && !workspace.HasPendingCanvasOrPropertyWork && ReferenceEquals(workspace.Document,doc) &&
        workspace.SelectedId==selected && selectedSource()==sourceId && DocumentSession.ContentEquals(workspace.Document,copy);
    private async Task CalculateAsync()
    {
        if(cancellation is not null)return;Invalidate();
        if(workspace.HasPendingCanvasOrPropertyWork || selectedSource()!=sourceId) { SetStatus("Kaynak veya bekleyen çalışma değişti; önce kontrol edin.");return; }
        if(!int.TryParse(index.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var pointIndex)) { SetStatus("Geçerli bir tam sayı indeksi girin.");return; }
        var doc=workspace.Document;var copy=doc.Clone();var selected=workspace.SelectedId;var source=copy.Pieces.FirstOrDefault(p=>p.Id==sourceId);
        if(source is null) { SetStatus("Kaynak parça bulunamadı.");return; }
        using var cancel=new CancellationTokenSource();cancellation=cancel;calculate.IsEnabled=false;SetStatus("Kontur doğrulanıyor; kapatmak hesabı iptal eder.");
        try
        {
            var result=await Task.Run(()=> { BeforeSplitForVerification?.Invoke();cancel.Token.ThrowIfCancellationRequested();var split=OpenPathSplit.Split(source,pointIndex,cancel.Token);var candidate=copy.Clone();candidate.Pieces.Add(split.First);candidate.Pieces.Add(split.Second);ProjectStorage.Validate(candidate,cancel.Token);return split; },cancel.Token);
            if(cancel.IsCancellationRequested || closed)return;
            if(!Current(doc,copy,selected)) { SetStatus("Kaynak veya proje değişti; önizlemeyi yenileyin.");return; }
            original=doc;snapshot=copy;selection=selected;proposal=result;apply.IsEnabled=true;
            var point=source.Points[pointIndex];
            SetStatus($"Önizleme: A {result.First.Points.Count}, B {result.Second.Points.Count} nokta; A {result.First.Notches?.Count ?? 0}, B {result.Second.Notches?.Count ?? 0} görsel işaret. Ortak nokta {pointIndex}: X {MeasurementInput.Format(point.X)} / Y {MeasurementInput.Format(point.Y)} mm (görüntülenen değerler yuvarlanabilir). Kaynak korunur; sonuçların üretim bilgileri ve serileme kuralları yoktur.");
        }
        catch(OperationCanceledException) { if(!closed)SetStatus("İşlem iptal edildi; proje değişmedi."); }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or ArithmeticException or System.IO.InvalidDataException) { if(!closed)SetStatus("Önizleme oluşturulamadı: "+ex.Message); }
        finally { if(ReferenceEquals(cancellation,cancel))cancellation=null;if(!closed)calculate.IsEnabled=true; }
    }
    private void Apply()
    {
        if(proposal is not { } result || original is null || snapshot is null || !Current(original,snapshot,selection))
        { Invalidate();SetStatus("Proje veya seçim değişti; önizlemeyi yenileyin.");return; }
        if(!workspace.CanRetainCurrentUndoSnapshot) { SetStatus("Proje geri alma sınırını aşıyor; işlem uygulanmadı.");return; }
        var first=result.First.Clone();var second=result.Second.Clone();var committed=false;apply.IsEnabled=false;
        try
        {
            workspace.CommitObserved("Açık kontur bölündü",()=> { workspace.Document.Pieces.Add(first);workspace.Document.Pieces.Add(second); },()=>committed=true);
            proposal=null;SetStatus("İki açık parça eklendi; kaynak korundu. Tek Geri al işlemi iki sonucu kaldırır.");
        }
        catch(Exception ex) when(ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        { proposal=null;SetStatus(committed ? "Parçalar eklendi ancak görünüm yenilenemedi; geçmişi kontrol edin: "+ex.Message : "Parçalar eklenmedi: "+ex.Message); }
    }
}
