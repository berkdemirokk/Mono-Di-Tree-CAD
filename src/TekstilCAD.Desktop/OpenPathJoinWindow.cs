using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Desktop;

/// <summary>Preview and add a detached joined path; original pieces and their references remain intact.</summary>
internal sealed class OpenPathJoinWindow : Window
{
    private readonly MainWindow workspace;
    private readonly ComboBox first = new() { MinWidth=350, Margin=new Thickness(0,4,0,8) };
    private readonly ComboBox second = new() { MinWidth=350, Margin=new Thickness(0,4,0,8) };
    private readonly TextBox tolerance = new() { Text="0", MinWidth=120, Margin=new Thickness(0,4,0,8) };
    private readonly CheckBox omitProductionData = new() { Content="Üretim bilgisi ve iplik yönü sonuca aktarılmadan devam et", IsChecked=false, Margin=new Thickness(0,6,0,8) };
    private readonly TextBlock message = new() { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,8,0,8) };
    private readonly JoinPreview preview = new() { Height=230, Margin=new Thickness(0,8,0,8) };
    private readonly Button calculate = new() { Content="Önizle", Padding=new Thickness(12,7,12,7), Margin=new Thickness(0,0,8,0) };
    private readonly Button apply = new() { Content="Yeni parça olarak ekle", Padding=new Thickness(12,7,12,7), IsEnabled=false, Margin=new Thickness(0,0,8,0) };
    private CancellationTokenSource? cancellation;
    private OpenPathJoinResult? proposal;
    private PatternPiece? beforeFirst, beforeSecond;
    private ProjectDocument? document;
    private bool closed;
    internal Task CalculateForVerification() => CalculateAsync();
    internal void ApplyForVerification() => Apply();
    internal TextBox ToleranceForVerification => tolerance;
    internal Button ApplyButtonForVerification => apply;
    internal string MessageForVerification => message.Text;

    private sealed record Choice(Guid Id, string Label) { public override string ToString()=>Label; }

    internal OpenPathJoinWindow(MainWindow workspace, Window owner)
    {
        this.workspace=workspace; Owner=owner; Title="Açık konturları birleştir";
        Width=760; Height=760; MinWidth=500; MinHeight=500; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) }; Content=root;
        var buttons=new WrapPanel { Margin=new Thickness(0,8,0,0) }; DockPanel.SetDock(buttons,Dock.Bottom); root.Children.Add(buttons);
        buttons.Children.Add(calculate); buttons.Children.Add(apply);
        var cancel=new Button { Content="Kapat / hesabı iptal et", Padding=new Thickness(12,7,12,7) }; buttons.Children.Add(cancel);
        var scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto }; root.Children.Add(scroll);
        var body=new StackPanel(); scroll.Content=body;
        body.Children.Add(new TextBlock { Text="İki açık konturu seçin. Kaynaklar, serileme kuralları ve kaynak ilişkileri korunur; sonuç ayrı bir açık parça olarak eklenir. Köşe işaretleri aynı koordinatlara kopyalanır; iki işaret tek birleşim noktasına düşerse işlem reddedilir. Üretim bilgisi veya iplik yönü varsa bunlar yalnız açık onayla sonuca aktarılmadan bırakılabilir.", TextWrapping=TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text="Birinci açık parça" }); body.Children.Add(first);
        body.Children.Add(new TextBlock { Text="İkinci açık parça" }); body.Children.Add(second);
        body.Children.Add(new TextBlock { Text="En fazla uç uzaklığı (mm)" }); body.Children.Add(tolerance);
        body.Children.Add(new TextBlock { Text="Uçlar eşit değilse yerleri değiştirilmez; aralarına düz bir köprü eklenir. Birden fazla olası eşleşme veya kesişen sonuç reddedilir.", TextWrapping=TextWrapping.Wrap });
        body.Children.Add(omitProductionData);
        body.Children.Add(new TextBlock { Text="Kaynaklar değişmez. Yeni parçada rol, beden, malzeme, adet ve iplik yönü bulunmaz; üretimden önce bunları ayrıca belirleyin.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning });
        body.Children.Add(preview); body.Children.Add(message);
        AutomationProperties.SetName(first,"Birinci açık parça"); AutomationProperties.SetName(second,"İkinci açık parça");
        AutomationProperties.SetName(tolerance,"En fazla uç uzaklığı, milimetre"); AutomationProperties.SetName(preview,"Birleştirilmiş kontur önizlemesi");
        AutomationProperties.SetLiveSetting(message,AutomationLiveSetting.Polite);
        foreach(var p in workspace.Document.Pieces.Where(p=>!p.Closed))
        {
            var label=$"{p.Name} · {p.Id:D}";
            first.Items.Add(new Choice(p.Id,label)); second.Items.Add(new Choice(p.Id,label));
        }
        if(first.Items.Count>0)first.SelectedIndex=0;
        if(second.Items.Count>1)second.SelectedIndex=1;
        first.SelectionChanged+=(_,_)=>InvalidateProposal(); second.SelectionChanged+=(_,_)=>InvalidateProposal();
        tolerance.TextChanged+=(_,_)=>InvalidateProposal();
        omitProductionData.Checked+=(_,_)=>InvalidateProposal();omitProductionData.Unchecked+=(_,_)=>InvalidateProposal();
        calculate.Click+=async (_,_)=>await CalculateAsync(); apply.Click+=(_,_)=>Apply();
        cancel.Click+=(_,_)=>Close(); Closed+=(_,_)=> { closed=true; cancellation?.Cancel(); };
    }

    private void InvalidateProposal()
    {
        cancellation?.Cancel(); proposal=null; beforeFirst=null; beforeSecond=null; document=null;
        apply.IsEnabled=false; preview.Points=null; preview.Bridge=null; preview.Notches=null; preview.InvalidateVisual(); message.Text="Önizleme hesaplanmadı.";
    }

    private async Task CalculateAsync()
    {
        if(cancellation is not null)return;
        InvalidateProposal();
        if(workspace.HasPendingCanvasOrPropertyWork) { message.Text="Önce tamamlanmamış çizimi veya alan taslaklarını tamamlayın."; return; }
        if(first.SelectedItem is not Choice a || second.SelectedItem is not Choice b || a.Id==b.Id)
        { message.Text="Kimlikleri farklı iki açık parça seçin."; return; }
        if(!MeasurementInput.TryParseMillimeters(tolerance.Text,out var gap) || gap<0 || gap>100000)
        { message.Text="0 ile 100000 mm arasında geçerli bir tolerans girin."; return; }
        var currentDocument=workspace.Document;
        var one=currentDocument.Pieces.SingleOrDefault(p=>p.Id==a.Id);
        var two=currentDocument.Pieces.SingleOrDefault(p=>p.Id==b.Id);
        if(one is null || two is null) { message.Text="Kaynak parça bulunamadı."; return; }
        var oneCopy=one.Clone(); var twoCopy=two.Clone(); var omit=omitProductionData.IsChecked==true;
        using var tokenSource=new CancellationTokenSource(); cancellation=tokenSource;
        calculate.IsEnabled=false; message.Text="Konturlar doğrulanıyor; kapatmak hesabı iptal eder.";
        try
        {
            var result=await Task.Run(()=>OpenPathJoin.Join(oneCopy,twoCopy,gap,tokenSource.Token,omit),tokenSource.Token);
            if(closed || tokenSource.IsCancellationRequested)return;
            if(!ReferenceEquals(workspace.Document,currentDocument) || !SameCurrent(oneCopy) || !SameCurrent(twoCopy))
            { message.Text="Kaynak veya proje değişti; önizlemeyi yeniden hesaplayın."; return; }
            proposal=result; beforeFirst=oneCopy; beforeSecond=twoCopy; document=currentDocument;
            preview.Points=result.Piece.Points;preview.Notches=result.Piece.Notches;
            var endpointsA=new[] { oneCopy.Points[0],oneCopy.Points[^1] };
            var endpointsB=new[] { twoCopy.Points[0],twoCopy.Points[^1] };
            preview.Bridge=(from x in endpointsA from y in endpointsB where TekstilCAD.Core.Geometry.Distance(x,y)<=gap select ((Point2,Point2)?)(x,y)).Single();
            preview.InvalidateVisual();
            message.Text=$"Önizleme: {result.Piece.Points.Count} noktalı açık kontur. {result.Piece.Notches?.Count ?? 0} köşe işareti aynı koordinatlara kopyalanır. Kırmızı çizgi köprüdür; uzunluğu {MeasurementInput.Format(result.BridgeLength)} mm. Kaynaklar projede kalır; sonuç adı kaynakları belirtir, ancak düzenlenebilir ve resmi kaynak kaydı değildir. Sonuç için kaynak kuralları, üretim bilgileri veya kaynak geçmişi oluşturulmaz. Geri al, yalnız eklenen sonucu kaldırır.";
            apply.IsEnabled=true;
        }
        catch(OperationCanceledException) { if(!closed)message.Text="Hesap iptal edildi."; }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or ArithmeticException)
        { if(!closed)message.Text="Önizleme oluşturulamadı: "+ex.Message; }
        finally { if(ReferenceEquals(cancellation,tokenSource))cancellation=null; if(!closed)calculate.IsEnabled=true; }
    }

    private bool SameCurrent(PatternPiece original)
    {
        var p=workspace.Document.Pieces.SingleOrDefault(p=>p.Id==original.Id);
        return p is not null && OpenPathSourceSnapshot.Matches(p,original);
    }

    private void Apply()
    {
        if(proposal is null || beforeFirst is null || beforeSecond is null || !ReferenceEquals(document,workspace.Document) ||
           workspace.HasPendingCanvasOrPropertyWork || !SameCurrent(beforeFirst) || !SameCurrent(beforeSecond))
        { InvalidateProposal(); message.Text="Proje veya kaynak değişti; önizlemeyi yeniden hesaplayın."; return; }
        var generated=proposal.Piece.Clone(); var committed=false;
        apply.IsEnabled=false;
        try
        {
            workspace.CommitObserved("Açık konturlar birleştirildi",()=>workspace.Document.Pieces.Add(generated),()=>committed=true);
            message.Text="Yeni açık parça eklendi. Kaynaklar korundu; ana çalışma alanında Geri al ile sonuç kaldırılabilir. Projeyi kaydedin.";
            proposal=null;
        }
        catch(Exception ex) when(ex is not (OutOfMemoryException or StackOverflowException))
        {
            message.Text=committed ? "Sonuç eklendi ancak görünüm yenilenemedi; ana çalışma alanını ve geri alma geçmişini kontrol edin. "+ex.Message : "Sonuç eklenmedi: "+ex.Message;
            proposal=null;
        }
    }

    private sealed class JoinPreview : FrameworkElement
    {
        internal IReadOnlyList<Point2>? Points { get; set; }
        internal IReadOnlyList<NotchMark>? Notches { get; set; }
        internal (Point2,Point2)? Bridge { get; set; }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc); dc.DrawRectangle(Brushes.WhiteSmoke,null,new Rect(RenderSize));
            if(Points is not { Count: >1 } points || ActualWidth<30 || ActualHeight<30)return;
            var minX=points.Min(p=>p.X); var maxX=points.Max(p=>p.X); var minY=points.Min(p=>p.Y); var maxY=points.Max(p=>p.Y);
            var w=maxX-minX; var h=maxY-minY;
            var scale=Math.Min(w>0 ? (ActualWidth-24)/w : double.PositiveInfinity,h>0 ? (ActualHeight-24)/h : double.PositiveInfinity);
            if(!double.IsFinite(scale))return;
            var geometry=new StreamGeometry();
            // Match the CAD canvas: increasing model Y appears lower on screen.
            Point Map(Point2 p)=>new(12+(p.X-minX)*scale,12+(p.Y-minY)*scale);
            using(var context=geometry.Open())
            {
                context.BeginFigure(Map(points[0]),false,false);
                foreach(var point in points.Skip(1))context.LineTo(Map(point),true,false);
            }
            geometry.Freeze(); dc.DrawGeometry(null,new Pen(Brushes.DarkBlue,1.5),geometry);
            if(Bridge is { } bridge && bridge.Item1!=bridge.Item2)
                dc.DrawLine(new Pen(Brushes.Red,3),Map(bridge.Item1),Map(bridge.Item2));
            if(Notches is not null)foreach(var mark in Notches)dc.DrawEllipse(null,new Pen(Brushes.DarkMagenta,2),Map(mark.Position),5,5);
        }
    }
}
