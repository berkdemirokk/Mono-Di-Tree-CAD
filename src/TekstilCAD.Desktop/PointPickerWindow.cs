using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Chooses one stored contour index without changing the project or coordinate drafts.</summary>
internal sealed class PointPickerWindow : Window
{
    private readonly MainWindow workspace;
    private readonly ProjectDocument originalDocument, snapshot;
    private readonly Guid sourceId;
    private readonly Guid? originalSelection;
    private readonly Func<Guid?> selectedSource;
    private readonly PatternPiece piece;
    private readonly DispatcherTimer monitor = new() { Interval=TimeSpan.FromMilliseconds(400) };
    internal ComboBox PointSelector { get; } = CreateSelector();
    internal PointMeasurePreview Preview { get; }
    internal TextBlock PickStatus { get; } = new() { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,6,0,0) };
    internal Button UseButton { get; }
    internal bool IsStale { get; private set; }
    internal int? SelectedPointIndex { get; private set; }

    internal PointPickerWindow(MainWindow workspace, Window owner, PatternPiece source,
        Func<Guid?> selectedSource, int initialIndex = -1)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(source);
        this.workspace=workspace;
        this.selectedSource=selectedSource ?? throw new ArgumentNullException(nameof(selectedSource));
        originalDocument=workspace.Document; originalSelection=workspace.SelectedId; sourceId=source.Id;
        if (!ReferenceEquals(originalDocument.Pieces.FirstOrDefault(p=>p.Id==source.Id),source) || selectedSource()!=source.Id)
            throw new InvalidOperationException("Kaynak parça değişti; güncel parçayı seçip yeniden deneyin.");
        if(workspace.HasPendingCanvasOrPropertyWork)
            throw new InvalidOperationException("Önce tamamlanmamış çizimi, sürüklemeyi veya parça alan taslaklarını tamamlayın ya da iptal edin.");
        snapshot=originalDocument.Clone(); piece=source.Clone(); Preview=new PointMeasurePreview(piece);
        Owner=owner; Title="Kontur noktası seç · Mono Di Tree CAD"; Width=670; Height=510; MinWidth=510; MinHeight=390;
        WindowStartupLocation=WindowStartupLocation.CenterOwner; StudioTheme.Apply(this);

        var root=new DockPanel { Margin=new Thickness(16) }; Content=root;
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,10,0,0) };
        DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var cancel=new Button { Content="Vazgeç", IsCancel=true, Padding=new Thickness(14,7,14,7), Margin=new Thickness(0,0,8,0) };
        cancel.Click+=(_,_)=>DialogResult=false;actions.Children.Add(cancel);
        UseButton=new Button { Content="Bu noktayı kullan", IsDefault=true, Padding=new Thickness(14,7,14,7) };
        UseButton.Click+=(_,_)=>AcceptSelection();actions.Children.Add(UseButton);

        var panel=new StackPanel();
        var scroller=new ScrollViewer { Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=true };
        AutomationProperties.SetName(scroller,"Kontur noktası seçim içeriği");root.Children.Add(scroller);
        panel.Children.Add(new TextBlock { Text="Kontur noktası seç",FontSize=20,FontWeight=FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text="Noktalar 0'dan başlayan indekslerle listelenir. Listeden veya önizlemeden bir nokta seçin. Yakın ya da üst üste noktaları kesin ayırt etmek için listeyi kullanın. Bu seçim X/Y girişlerini değiştirmez.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,8) });
        var identity=new TextBox { Text=sourceId.ToString("D"),IsReadOnly=true,Margin=new Thickness(0,0,0,8) };
        AutomationProperties.SetName(identity,"Kaynak parça tam kimliği");panel.Children.Add(identity);
        var list=new List<PointChoice>(piece.Points.Count);
        for(var i=0;i<piece.Points.Count;i++)
        {
            var p=piece.Points[i];
            var closing=piece.Closed && i>0 && i==piece.Points.Count-1 && p==piece.Points[0];
            list.Add(new PointChoice(i,$"{i} · X {MeasurementInput.Format(p.X)} · Y {MeasurementInput.Format(p.Y)} mm"+(closing ? " · kapanış noktası, ilk nokta ile aynı" : "")));
        }
        var label=new Label { Content="Kontur noktası",Target=PointSelector,Padding=new Thickness(0) };panel.Children.Add(label);
        AutomationProperties.SetName(PointSelector,"Kontur noktası indeksi ve koordinatları");
        AutomationProperties.SetHelpText(PointSelector,"İndeksi yazarak veya ok tuşlarıyla seçin. İndeksler sıfırdan başlar.");
        PointSelector.ItemsSource=list; PointSelector.SelectedIndex=initialIndex>=0 && initialIndex<list.Count ? initialIndex : -1;
        panel.Children.Add(PointSelector);
        panel.Children.Add(new TextBlock { Text="Kalıp önizlemesi · seçili nokta A halkası",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0),FontWeight=FontWeights.SemiBold });
        if(!Preview.IndividualVerticesVisible)
            panel.Children.Add(new TextBlock { Text="Çok sayıda nokta olduğu için küçük nokta işaretleri gizlendi; kesin seçim için listeyi kullanın.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Muted,Margin=new Thickness(0,4,0,0) });
        panel.Children.Add(Preview);
        AutomationProperties.SetName(PickStatus,"Kontur noktası seçim durumu"); AutomationProperties.SetLiveSetting(PickStatus,AutomationLiveSetting.Polite);panel.Children.Add(PickStatus);
        PointSelector.SelectionChanged+=(_,_)=>UpdateSelection();
        Preview.PointRequested+=index=>
        {
            CheckCurrent(); if(IsStale)return;
            if(index<0) SetStatus(index==-2 ? "Bu konumda birden fazla yakın nokta var; istediğiniz indeksi listeden seçin." : "Yakında nokta bulunamadı; daha yakın tıklayın veya listeden seçin.");
            else { PointSelector.SelectedIndex=index;SetStatus($"{index} numaralı nokta seçildi."); }
        };
        monitor.Tick+=(_,_)=>CheckCurrent();Loaded+=(_,_)=>monitor.Start();Closed+=(_,_)=>monitor.Stop();
        UpdateSelection();
    }

    private static ComboBox CreateSelector()
    {
        var selector=new ComboBox { Margin=new Thickness(0,4,0,0),DisplayMemberPath=nameof(PointChoice.Label),MaxDropDownHeight=260,IsTextSearchEnabled=true };
        selector.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        VirtualizingPanel.SetIsVirtualizing(selector,true);VirtualizingPanel.SetVirtualizationMode(selector,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(selector,true);
        TextSearch.SetTextPath(selector,nameof(PointChoice.Label));return selector;
    }
    private void SetStatus(string message)
    {
        PickStatus.Text=message;AutomationProperties.SetName(PickStatus,message);
        if(UIElementAutomationPeer.CreatePeerForElement(PickStatus) is { } peer)peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    private void UpdateSelection()
    {
        if(IsStale)return;
        var index=PointSelector.SelectedItem is PointChoice choice ? choice.Index : -1;
        Preview.SetSelection(index,-1);UseButton.IsEnabled=index>=0;
        SetStatus(index>=0 ? $"{index} numaralı nokta seçildi. X/Y girişlerini değiştirmek için ayrıca düzenleyin." : "Bir nokta seçin.");
    }
    internal void CheckCurrent()
    {
        if(IsStale)return;
        if(!ReferenceEquals(workspace.Document,originalDocument) || workspace.SelectedId!=originalSelection || selectedSource()!=sourceId ||
           workspace.HasPendingCanvasOrPropertyWork || !DocumentSession.ContentEquals(snapshot,workspace.Document))
        {
            IsStale=true;monitor.Stop();PointSelector.IsEnabled=false;UseButton.IsEnabled=false;Preview.Visibility=Visibility.Collapsed;
            SetStatus("Proje, seçim veya bekleyen çalışma değişti; nokta aktarılamaz. Pencereyi kapatıp güncel kaynakla yeniden açın.");
        }
    }
    internal void AcceptSelection()
    {
        CheckCurrent();if(IsStale)return;
        if(PointSelector.SelectedItem is not PointChoice choice || choice.Index<0 || choice.Index>=piece.Points.Count)
        { SetStatus("Önce bir nokta seçin.");return; }
        SelectedPointIndex=choice.Index;DialogResult=true;
    }
    private sealed record PointChoice(int Index,string Label);
}
