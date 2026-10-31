using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Read-only straight and contour distances between existing vertices of one source piece.</summary>
internal sealed class PointDistanceWindow : Window
{
    private readonly MainWindow workspace;
    private readonly ProjectDocument originalDocument;
    private readonly ProjectDocument snapshot;
    private readonly Guid? originalSelection;
    private readonly Guid sourceId;
    private readonly Func<Guid?> selectedSource;
    private readonly PatternPiece piece;
    private double? closedPerimeter;
    private readonly DispatcherTimer monitor = new() { Interval=TimeSpan.FromMilliseconds(400) };
    internal ComboBox StartSelector { get; } = Selector();
    internal ComboBox EndSelector { get; } = Selector();
    internal TextBlock Result { get; } = new() { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,12,0,0), FontSize=15 };
    internal TextBlock ContourResult { get; } = new() { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,8,0,0), FontSize=14 };
    internal PointMeasurePreview Preview { get; }
    internal RadioButton PickStart { get; } = new() { Content="A noktasını seç", Foreground=StudioTheme.Text, IsChecked=true, Margin=new Thickness(0,0,18,0) };
    internal RadioButton PickEnd { get; } = new() { Content="B noktasını seç", Foreground=StudioTheme.Text };
    internal TextBlock PickStatus { get; } = new() { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,6,0,0) };
    internal bool IsStale { get; private set; }

    internal PointDistanceWindow(MainWindow workspace, Window owner, PatternPiece source, Func<Guid?> selectedSource)
    {
        this.workspace=workspace; this.selectedSource=selectedSource;
        originalDocument=workspace.Document; originalSelection=workspace.SelectedId; sourceId=source.Id;
        if (!ReferenceEquals(originalDocument.Pieces.FirstOrDefault(p=>p.Id==source.Id),source) || selectedSource()!=source.Id)
            throw new InvalidOperationException("Kaynak parça değişti; güncel parçayı seçip yeniden deneyin.");
        snapshot=originalDocument.Clone(); piece=source.Clone(); Preview=new PointMeasurePreview(piece);
        Owner=owner; Title="İki nokta arası mesafe · Mono Di Tree CAD";
        Width=670; Height=480; MinWidth=510; MinHeight=390;
        WindowStartupLocation=WindowStartupLocation.CenterOwner; StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) }; Content=root;
        var close=new Button { Content="Kapat", Padding=new Thickness(14,7,14,7), HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,10,0,0), IsCancel=true };
        close.Click+=(_,_)=>Close(); DockPanel.SetDock(close,Dock.Bottom); root.Children.Add(close);
        var panel=new StackPanel();
        root.Children.Add(new ScrollViewer { Content=panel, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });
        panel.Children.Add(new TextBlock { Text="İki nokta arası mesafe", FontSize=20, FontWeight=FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text="Kayıtlı kontur noktaları arasında düz geometrik mesafe ve kontur boyunca uzunluk ayrı ayrı gösterilir. Bunlar fiziksel ölçüm değildir.", TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,6,0,8) });
        var identity=new TextBox { Text=$"Kaynak parça kimliği: {sourceId}", IsReadOnly=true, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,0,0,8) };
        AutomationProperties.SetName(identity,"Kaynak parça kimliği"); panel.Children.Add(identity);
        panel.Children.Add(new TextBlock { Text="İndeksler 0'dan başlar. Listede indeks yazarak noktaya ulaşabilirsiniz; koordinatlar mm cinsindedir.", TextWrapping=TextWrapping.Wrap });
        var list=new List<PointChoice>(piece.Points.Count);
        for(var i=0;i<piece.Points.Count;i++)
        {
            var p=piece.Points[i];
            var closing=piece.Closed && i>0 && i==piece.Points.Count-1 && p==piece.Points[0];
            list.Add(new PointChoice(i,p,$"{i} · X {MeasurementInput.Format(p.X)} · Y {MeasurementInput.Format(p.Y)} mm"+(closing ? " · kapanış noktası, ilk nokta ile aynı" : "")));
        }
        var choices=new Grid { Margin=new Thickness(0,8,0,0) };
        choices.ColumnDefinitions.Add(new ColumnDefinition()); choices.ColumnDefinitions.Add(new ColumnDefinition());
        var startColumn=new StackPanel { Margin=new Thickness(0,0,8,0) }; startColumn.Children.Add(new TextBlock { Text="Başlangıç noktası (A)" }); startColumn.Children.Add(StartSelector); choices.Children.Add(startColumn);
        var endColumn=new StackPanel { Margin=new Thickness(8,0,0,0) }; endColumn.Children.Add(new TextBlock { Text="Bitiş noktası (B)" }); endColumn.Children.Add(EndSelector); Grid.SetColumn(endColumn,1); choices.Children.Add(endColumn); panel.Children.Add(choices);
        AutomationProperties.SetName(StartSelector,"Başlangıç noktası A"); AutomationProperties.SetName(EndSelector,"Bitiş noktası B");
        AutomationProperties.SetHelpText(StartSelector,"İndeksi yazarak veya ok tuşlarıyla nokta seçin."); AutomationProperties.SetHelpText(EndSelector,"İndeksi yazarak veya ok tuşlarıyla nokta seçin.");
        StartSelector.ItemsSource=list; EndSelector.ItemsSource=list;
        StartSelector.SelectedIndex=list.Count>0 ? 0 : -1; EndSelector.SelectedIndex=list.Count>1 ? 1 : list.Count>0 ? 0 : -1;
        AutomationProperties.SetName(Result,"Geometrik mesafe sonucu"); AutomationProperties.SetLiveSetting(Result,AutomationLiveSetting.Polite); panel.Children.Add(Result);
        AutomationProperties.SetName(ContourResult,"Kontur boyunca ölçüm sonucu"); AutomationProperties.SetLiveSetting(ContourResult,AutomationLiveSetting.Polite); panel.Children.Add(ContourResult);
        panel.Children.Add(new TextBlock { Text="Kalıp önizlemesi · A halka, B baklava · kesik çizgi düz mesafe", TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,12,0,0), FontWeight=FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text="Değiştireceğiniz noktayı seçin, ardından kalıp üzerindeki bir noktaya tıklayın. Birbirine yakın noktalar için yukarıdaki listeyi kullanın.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,6) });
        if(!Preview.IndividualVerticesVisible)panel.Children.Add(new TextBlock { Text="Çok sayıda nokta olduğu için küçük nokta işaretleri gizlendi; kesin seçim için listeyi kullanın.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6),Foreground=StudioTheme.Muted });
        var pickModes=new WrapPanel();pickModes.Children.Add(PickStart);pickModes.Children.Add(PickEnd);panel.Children.Add(pickModes);
        panel.Children.Add(Preview);
        AutomationProperties.SetName(PickStatus,"Önizleme seçimi durumu");AutomationProperties.SetLiveSetting(PickStatus,AutomationLiveSetting.Polite);panel.Children.Add(PickStatus);
        panel.Children.Add(new TextBlock { Text="ΔX ve ΔY: B eksi A. Aynı noktanın mesafesi sıfırdır. Değerler çift duyarlıklı hesaplanır; gösterimde yuvarlama yerine tam geri dönüş duyarlılığı ve ondalık nokta kullanılır.", TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,10,0,0), Foreground=StudioTheme.Muted });
        StartSelector.SelectionChanged+=(_,_)=>Calculate(); EndSelector.SelectionChanged+=(_,_)=>Calculate();
        Preview.PointRequested+=index=>
        {
            CheckCurrent(); if(IsStale)return;
            if(index<0)
            {
                PickStatus.Text=index==-2 ? "Bu konumda birden fazla yakın nokta var; istediğiniz indeksi listeden seçin." : "Yakında nokta bulunamadı; bir kontur noktasına daha yakın tıklayın.";
            }
            else
            {
                var isStart=PickStart.IsChecked==true;
                (isStart ? StartSelector : EndSelector).SelectedIndex=index;
                PickStatus.Text=$"{(isStart ? "A" : "B")} noktası {index} olarak seçildi.";
            }
            if(UIElementAutomationPeer.CreatePeerForElement(PickStatus) is { } peer)peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        };
        monitor.Tick+=(_,_)=>CheckCurrent(); Loaded+=(_,_)=>monitor.Start(); Closed+=(_,_)=>monitor.Stop();
        Calculate();
    }

    private static ComboBox Selector()
    {
        var selector=new ComboBox { Margin=new Thickness(0,4,0,0), DisplayMemberPath=nameof(PointChoice.Label), MaxDropDownHeight=260, IsTextSearchEnabled=true };
        selector.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        VirtualizingPanel.SetIsVirtualizing(selector,true); VirtualizingPanel.SetVirtualizationMode(selector,VirtualizationMode.Recycling); ScrollViewer.SetCanContentScroll(selector,true);
        TextSearch.SetTextPath(selector,nameof(PointChoice.Label));
        return selector;
    }

    internal void CheckCurrent()
    {
        if(IsStale)return;
        if(!ReferenceEquals(workspace.Document,originalDocument) || workspace.SelectedId!=originalSelection || selectedSource()!=sourceId || !DocumentSession.ContentEquals(snapshot,workspace.Document))
        {
            IsStale=true; monitor.Stop(); StartSelector.IsEnabled=false; EndSelector.IsEnabled=false; PickStart.IsEnabled=false; PickEnd.IsEnabled=false; PickStatus.Text="";
            Result.Foreground=StudioTheme.Warning;
            SetResult("Proje veya seçim değişti; mesafe sonucu geçersiz. Pencereyi kapatıp güncel kaynakla yeniden açın."); SetContourResult(""); Preview.Visibility=Visibility.Collapsed;
        }
    }

    private void Calculate()
    {
        CheckCurrent(); if(IsStale)return;
        if(StartSelector.SelectedItem is not PointChoice a || EndSelector.SelectedItem is not PointChoice b)
        { SetResult("Mesafe için iki nokta seçin."); SetContourResult(""); Preview.SetSelection(StartSelector.SelectedIndex,EndSelector.SelectedIndex); return; }
        try
        {
            Preview.SetSelection(a.Index,b.Index);
            var distance=Geometry.Distance(a.Point,b.Point);
            SetResult($"Düz mesafe: {MeasurementInput.Format(distance)} mm\nΔX: {MeasurementInput.Format(b.Point.X-a.Point.X)} mm · ΔY: {MeasurementInput.Format(b.Point.Y-a.Point.Y)} mm");
            var forward=ContourLength(a.Index,b.Index,1);
            if(piece.Closed)
            {
                var backward=ContourLength(a.Index,b.Index,-1);
                var perimeter=closedPerimeter ??= ContourMeasurement.ClosedPerimeter(piece.Points);
                SetContourResult($"Kontur boyunca A → B: ileri {MeasurementInput.Format(forward)} mm · ters {MeasurementInput.Format(backward)} mm. İleri yön nokta indekslerini artırır.\nToplam kapalı kontur uzunluğu: {MeasurementInput.Format(perimeter)} mm.");
            }
            else
            {
                var length=a.Index<=b.Index ? forward : ContourLength(a.Index,b.Index,-1);
                SetContourResult($"Açık kontur boyunca A ile B arası: {MeasurementInput.Format(length)} mm.");
            }
        }
        catch(ArgumentOutOfRangeException)
        { SetResult("Geçersiz koordinat nedeniyle mesafe hesaplanamadı."); SetContourResult(""); }
    }

    // Traverse the stored polyline, not its bounding box or the straight chord.
    // Closed contours wrap; open contours use their only connecting direction.
    private double ContourLength(int from,int to,int direction)
    {
        if(from==to)return 0;
        if(!piece.Closed && ((direction>0 && from>to) || (direction<0 && from<to))) return 0;
        double sum=0,correction=0;
        for(var current=from;current!=to;)
        {
            var next=(current+direction+piece.Points.Count)%piece.Points.Count;
            var segment=Geometry.Distance(piece.Points[current],piece.Points[next]);
            var adjusted=segment-correction; var updated=sum+adjusted;
            correction=(updated-sum)-adjusted; sum=updated; current=next;
        }
        return sum;
    }
    private void SetContourResult(string text)
    {
        if(ContourResult.Text==text)return;
        ContourResult.Text=text;
        if(UIElementAutomationPeer.CreatePeerForElement(ContourResult) is { } peer)peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    private void SetResult(string text)
    {
        if(Result.Text==text)return;
        Result.Text=text;
        if(UIElementAutomationPeer.CreatePeerForElement(Result) is { } peer)peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private sealed record PointChoice(int Index, Point2 Point, string Label);
}
