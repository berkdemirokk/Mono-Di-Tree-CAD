using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;

namespace TekstilCAD.Desktop;

internal sealed class OpenPathClosureWindow : Window
{
    private readonly MainWindow workspace;
    private readonly Func<PatternPiece,GradingRuleSet?,bool>? hasDraft;
    private readonly CheckBox copyRule=new() { Content="Uyumlu kayıtlı serileme artışlarını sonuca aktar",Margin=new Thickness(0,6,0,8) };
    private readonly ComboBox source=new() { MinWidth=350,IsTextSearchEnabled=true,Margin=new Thickness(0,4,0,8) };
    private readonly TextBox tolerance=new() { Text="0",MinWidth=120,Margin=new Thickness(0,4,0,8) };
    private readonly CheckBox omitProductionData = new() { Content="Üretim bilgisi ve iplik yönü sonuca aktarılmadan devam et", IsChecked=false, Margin=new Thickness(0,6,0,8) };
    private readonly TextBlock message=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8) };
    private readonly ClosurePreview preview=new() { Height=230,Margin=new Thickness(0,8,0,8) };
    private readonly Button calculate=new() { Content="Önizle",Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,0) };
    private readonly Button apply=new() { Content="Kapalı parçayı ekle",Padding=new Thickness(12,7,12,7),IsEnabled=false,Margin=new Thickness(0,0,8,0) };
    private CancellationTokenSource? cancellation;
    private OpenPathClosureResult? proposal;
    private GradingRuleSet? proposalRule;
    private PatternPiece? before;
    private ProjectDocument? document,documentSnapshot;
    private bool closed;
    private sealed record Choice(Guid Id,string Label,string SearchLabel) { public override string ToString()=>Label; }
    internal Action? BeforeClosureForVerification { get; set; }
    internal Task CalculateForVerification()=>CalculateAsync();
    internal void ApplyForVerification()=>Apply();
    internal TextBox ToleranceForVerification=>tolerance;
    internal Button ApplyButtonForVerification=>apply;
    internal CheckBox CopyRuleForVerification=>copyRule;
    internal string MessageForVerification=>message.Text;

    internal OpenPathClosureWindow(MainWindow workspace,Window owner,Func<PatternPiece,GradingRuleSet?,bool>? hasDraft=null)
    {
        this.workspace=workspace;this.hasDraft=hasDraft; Owner=owner; Title="Açık konturu kapat"; Width=760;Height=760;MinWidth=500;MinHeight=500;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var buttons=new WrapPanel { Margin=new Thickness(0,8,0,0) };DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        buttons.Children.Add(calculate);buttons.Children.Add(apply);
        var close=new Button { Content="Kapat / hesabı iptal et",Padding=new Thickness(12,7,12,7) };buttons.Children.Add(close);
        var scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto };root.Children.Add(scroll);
        var body=new StackPanel();scroll.Content=body;
        body.Children.Add(new TextBlock { Text="Bir açık parça seçin. İlk ve son nokta değiştirilmeden düz bir kenarla kapatılır. Kaynak, serileme kuralları ve kaynak ilişkileri korunur; sonuç ayrı bir kapalı parçadır. Köşe işaretleri aynı noktalara kopyalanır. Üst üste gelen açık uçlardan sonuncusu işaretliyse bu işaret kapalı biçimde ayrı temsil edilemez ve işlem reddedilir. Üretim bilgisi veya iplik yönü varsa bunlar yalnız açık onayla sonuca aktarılmadan bırakılabilir.",TextWrapping=TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text="Açık parça" });body.Children.Add(source);
        body.Children.Add(new TextBlock { Text="En fazla uç uzaklığı (mm)" });body.Children.Add(tolerance);
        body.Children.Add(new TextBlock { Text="Kırmızı çizgi eklenecek kapanış kenarıdır. Sıfır mesafede uçlar zaten aynı yerdedir. Kesişen veya geçersiz kontur reddedilir.",TextWrapping=TextWrapping.Wrap });
        body.Children.Add(omitProductionData);
        AutomationProperties.SetHelpText(copyRule,"Kaynak geometriyle bitçe eşleşen kayıtlı kural ve temiz taslak gerekir. Kapanış kenarının iki ucundaki artışlar bitçe aynı olmalıdır; yeni bedenleri kontrol edin.");body.Children.Add(copyRule);
        body.Children.Add(new TextBlock { Text="Kaynaklar değişmez. Yeni parçada rol, beden, malzeme, adet ve iplik yönü bulunmaz; üretimden önce bunları ayrıca belirleyin.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning });
        body.Children.Add(preview);body.Children.Add(message);
        AutomationProperties.SetName(source,"Kapatılacak açık parça");TextSearch.SetTextPath(source,nameof(Choice.SearchLabel));AutomationProperties.SetName(tolerance,"En fazla uç uzaklığı, milimetre");
        AutomationProperties.SetName(preview,"Kapalı kontur ve kapanış kenarı önizlemesi");AutomationProperties.SetLiveSetting(message,AutomationLiveSetting.Polite);
        foreach(var p in workspace.Document.Pieces.Where(p=>!p.Closed))
        { var safeName=CadCanvas.CanvasPieceLabel(p.Name);source.Items.Add(new Choice(p.Id,$"{p.Id:D} · {safeName}",safeName)); }
        if(source.Items.Count>0)source.SelectedIndex=0;
        source.SelectionChanged+=(_,_)=>InvalidateProposal();tolerance.TextChanged+=(_,_)=>InvalidateProposal();
        omitProductionData.Checked+=(_,_)=>InvalidateProposal();omitProductionData.Unchecked+=(_,_)=>InvalidateProposal();
        copyRule.Checked+=(_,_)=>InvalidateProposal();copyRule.Unchecked+=(_,_)=>InvalidateProposal();
        calculate.Click+=async (_,_)=>await CalculateAsync();apply.Click+=(_,_)=>Apply();close.Click+=(_,_)=>Close();
        Closed+=(_,_)=> { closed=true;cancellation?.Cancel(); };
    }
    private void InvalidateProposal()
    {
        cancellation?.Cancel();proposal=null;proposalRule=null;before=null;document=null;documentSnapshot=null;apply.IsEnabled=false;preview.Points=null;preview.Notches=null;preview.InvalidateVisual();message.Text="Önizleme hesaplanmadı.";
    }
    private async Task CalculateAsync()
    {
        if(cancellation is not null)return;
        InvalidateProposal();
        if(workspace.HasPendingCanvasOrPropertyWork) { message.Text="Önce tamamlanmamış çizimi veya alan taslaklarını tamamlayın.";return; }
        if(source.SelectedItem is not Choice choice) { message.Text="Açık parça seçin.";return; }
        if(!MeasurementInput.TryParseMillimeters(tolerance.Text,out var gap) || gap<0 || gap>100000)
        { message.Text="0 ile 100000 mm arasında geçerli bir tolerans girin.";return; }
        var currentDocument=workspace.Document;var fullSnapshot=currentDocument.Clone();
        var current=currentDocument.Pieces.SingleOrDefault(p=>p.Id==choice.Id);
        if(current is null) { message.Text="Kaynak parça bulunamadı.";return; }
        var copy=current.Clone();var omit=omitProductionData.IsChecked==true;var carry=copyRule.IsChecked==true;
        var rule=fullSnapshot.GradingRules.FirstOrDefault(r=>r.SourcePieceId==copy.Id);
        if(carry && (rule is null || hasDraft is null || hasDraft(copy,rule))) { message.Text="Kaynağa ait kayıtlı kural ve kaynakla eşleşen temiz serileme taslağı gerekli.";return; }
        using var tokenSource=new CancellationTokenSource();cancellation=tokenSource;calculate.IsEnabled=false;message.Text="Kontur doğrulanıyor; kapatmak hesabı iptal eder.";
        try
        {
            var calculated=await Task.Run(()=> { BeforeClosureForVerification?.Invoke();tokenSource.Token.ThrowIfCancellationRequested();var result=OpenPathClosure.Close(copy,gap,tokenSource.Token,omit);
                var mapped=carry ? OpenPathClosure.CloseGrading(copy,rule!,result,tokenSource.Token) : null;
                if(carry && mapped is null)throw new InvalidOperationException("Kayıtlı kural veya kapanış uçlarının artışları bitçe eşleşmiyor.");
                var candidate=fullSnapshot.Clone();candidate.Pieces.Add(result.Piece);if(mapped is not null)candidate.GradingRules.Add(mapped);
                ProjectStorage.Validate(candidate,tokenSource.Token);return (result,mapped); },tokenSource.Token);
            var result=calculated.result;
            if(closed || tokenSource.IsCancellationRequested)return;
            if(!ReferenceEquals(workspace.Document,currentDocument) || !DocumentSession.ContentEquals(workspace.Document,fullSnapshot) || !SameCurrent(copy) || (carry && hasDraft!(copy,rule)))
            { message.Text="Kaynak veya proje değişti; önizlemeyi yeniden hesaplayın.";return; }
            proposal=result;proposalRule=calculated.mapped;before=copy;document=currentDocument;documentSnapshot=fullSnapshot;preview.Points=result.Piece.Points;preview.Notches=result.Piece.Notches;preview.InvalidateVisual();
            message.Text=$"Kapanış kenarı: {MeasurementInput.Format(result.ClosingLength)} mm. {result.Piece.Notches?.Count ?? 0} köşe işareti aynı noktalara kopyalanır. Kaynak projede kalır; sonuç adı kaynağı belirtir, ancak düzenlenebilir ve resmi kaynak kaydı değildir. Sonuca üretim bilgileri veya kaynak geçmişi aktarılmaz. "+(carry ? "Kayıtlı artışlar sonuca aktarılacak; yeni bedenleri kontrol edin." : "Sonuca serileme kuralı aktarılmaz.")+" Geri al eklenen sonucu kaldırır.";
            apply.IsEnabled=true;
        }
        catch(OperationCanceledException) { if(!closed)message.Text="Hesap iptal edildi."; }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or ArithmeticException or System.IO.InvalidDataException)
        { if(!closed)message.Text="Önizleme oluşturulamadı: "+ex.Message; }
        finally { if(ReferenceEquals(cancellation,tokenSource))cancellation=null;if(!closed)calculate.IsEnabled=true; }
    }
    private bool SameCurrent(PatternPiece original)
    {
        var p=workspace.Document.Pieces.SingleOrDefault(p=>p.Id==original.Id);
        return p is not null && OpenPathSourceSnapshot.Matches(p,original);
    }
    private void Apply()
    {
        if(proposal is null || before is null || documentSnapshot is null || !ReferenceEquals(document,workspace.Document) || !DocumentSession.ContentEquals(workspace.Document,documentSnapshot) || workspace.HasPendingCanvasOrPropertyWork || !SameCurrent(before))
        { InvalidateProposal();message.Text="Proje veya kaynak değişti; önizlemeyi yeniden hesaplayın.";return; }
        if(!workspace.CanRetainCurrentUndoSnapshot) { message.Text="Proje geri alma sınırını aşıyor; sonuç eklenmedi.";return; }
        if(proposalRule is not null && (hasDraft is null || hasDraft(before,documentSnapshot.GradingRules.FirstOrDefault(r=>r.SourcePieceId==before.Id))))
        { InvalidateProposal();message.Text="Serileme taslağı değişti; önizlemeyi yeniden hesaplayın.";return; }
        var generated=proposal.Piece.Clone();var mappedRule=proposalRule?.Clone();var committed=false;apply.IsEnabled=false;
        try
        {
            workspace.CommitObserved("Açık kontur kapatıldı",()=> { workspace.Document.Pieces.Add(generated);if(mappedRule is not null)workspace.Document.GradingRules.Add(mappedRule); },()=>committed=true);
            proposal=null;message.Text="Kapalı parça eklendi. Kaynak korundu; Geri al ile sonuç kaldırılabilir. Projeyi kaydedin.";
        }
        catch(Exception ex) when(ex is not (OutOfMemoryException or StackOverflowException))
        {
            proposal=null;message.Text=committed ? "Sonuç eklendi ancak görünüm yenilenemedi; ana çalışma alanını ve geri alma geçmişini kontrol edin. "+ex.Message : "Sonuç eklenmedi: "+ex.Message;
        }
    }
    private sealed class ClosurePreview : FrameworkElement
    {
        internal IReadOnlyList<Point2>? Points { get;set; }
        internal IReadOnlyList<NotchMark>? Notches { get;set; }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);dc.DrawRectangle(Brushes.WhiteSmoke,null,new Rect(RenderSize));
            if(Points is not { Count:>2 } points || ActualWidth<30 || ActualHeight<30)return;
            var minX=points.Min(p=>p.X);var maxX=points.Max(p=>p.X);var minY=points.Min(p=>p.Y);var maxY=points.Max(p=>p.Y);
            var w=maxX-minX;var h=maxY-minY;
            var scale=Math.Min(w>0 ? (ActualWidth-24)/w : double.PositiveInfinity,h>0 ? (ActualHeight-24)/h : double.PositiveInfinity);
            if(!double.IsFinite(scale))return;
            // Match the CAD canvas: increasing model Y appears lower on screen.
            Point Map(Point2 p)=>new(12+(p.X-minX)*scale,12+(p.Y-minY)*scale);
            var geometry=new StreamGeometry();using(var context=geometry.Open()) { context.BeginFigure(Map(points[0]),false,false);foreach(var p in points.Skip(1))context.LineTo(Map(p),true,false); }
            geometry.Freeze();dc.DrawGeometry(null,new Pen(Brushes.DarkBlue,1.5),geometry);
            if(points[0]!=points[^1])dc.DrawLine(new Pen(Brushes.Red,3),Map(points[^1]),Map(points[0]));
            if(Notches is not null)foreach(var mark in Notches)dc.DrawEllipse(null,new Pen(Brushes.DarkMagenta,2),Map(mark.Position),5,5);
        }
    }
}
