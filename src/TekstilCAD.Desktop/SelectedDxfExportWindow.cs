using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Explicit source selection for a separate DXF; filtering never drops hidden selections.</summary>
internal sealed class SelectedDxfExportWindow : Window
{
    internal sealed class Choice : INotifyPropertyChanged
    {
        public Guid Id { get; }
        public string SearchText { get; }
        public string Label { get; }
        public bool Eligible { get; }
        private bool isSelected;
        public bool IsSelected
        {
            get=>isSelected;
            set { if(value && !Eligible || isSelected==value)return;isSelected=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(IsSelected))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        public Choice(PatternPiece piece)
        {
            Id=piece.Id;var safeName=CadCanvas.CanvasPieceLabel(piece.Name);
            SearchText=$"{piece.Name} {safeName} {piece.Id:D}";
            Eligible=piece.Notches is null && piece.Grainline is null;
            Label=$"{safeName} · {piece.Id:D}"+(Eligible ? "" : " · NORMAL DXF DESTEKLEMEZ: görsel işaret veya iplik yönü");
        }
    }
    private readonly List<Choice> allChoices;
    private readonly ICollectionView view;
    private bool updating;
    internal ListBox Choices { get; } = new();
    internal TextBox Search { get; } = new() { Margin=new Thickness(0,4,0,4) };
    internal Button SelectVisible { get; } = new() { Content="Görünen uygunları seç",Margin=new Thickness(0,0,8,4),Padding=new Thickness(8,5,8,5) };
    internal Button ClearVisible { get; } = new() { Content="Görünenlerin seçimini kaldır",Margin=new Thickness(0,0,0,4),Padding=new Thickness(8,5,8,5) };
    internal TextBlock Summary { get; } = new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,6) };
    internal Button ContinueButton { get; } = new() { Content="Seçilenlerle devam et",Padding=new Thickness(12,7,12,7),IsEnabled=false };
    internal IReadOnlyList<Choice> AllChoices => allChoices;
    internal IReadOnlyList<Guid> SelectedIds => allChoices.Where(choice=>choice.IsSelected).Select(choice=>choice.Id).ToArray();

    internal SelectedDxfExportWindow(Window owner,ProjectDocument snapshot)
    {
        Owner=owner;Title="DXF için kalıp seç";Width=680;Height=640;MinWidth=480;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var buttons=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };
        var cancel=new Button { Content="Vazgeç",IsCancel=true,Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,0) };
        cancel.Click+=(_,_)=>DialogResult=false;ContinueButton.Click+=(_,_)=>DialogResult=true;
        buttons.Children.Add(cancel);buttons.Children.Add(ContinueButton);DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock { Text="Yalnız seçilen parçalar ayrı DXF'e yazılır; diğerleri kaynakta kalır. Kimlik, üretim bilgileri, serileme kuralları ve kaynak geçmişi DXF'te korunmaz. İşaret veya iplik yönü içeren parçalar normal DXF'e uygun değildir; ayrı inceleme çıktısını kullanın.",TextWrapping=TextWrapping.Wrap });
        top.Children.Add(new TextBlock { Text="Ada veya kimliğe göre ara",Margin=new Thickness(0,6,0,0) });
        AutomationProperties.SetName(Search,"DXF parçasını ada veya kimliğe göre ara");
        AutomationProperties.SetHelpText(Search,"Arama yalnız listeyi süzer; gizlenen seçili parçalar seçili kalır.");top.Children.Add(Search);
        AutomationProperties.SetHelpText(SelectVisible,"Yalnız aramada görünen ve DXF ile desteklenen parçaları seçer; gizli seçimler korunur.");
        AutomationProperties.SetHelpText(ClearVisible,"Yalnız aramada görünen parçaların seçimini kaldırır; gizli seçimler korunur.");
        var batch=new WrapPanel();batch.Children.Add(SelectVisible);batch.Children.Add(ClearVisible);top.Children.Add(batch);
        AutomationProperties.SetName(Summary,"DXF parça seçim özeti");AutomationProperties.SetLiveSetting(Summary,AutomationLiveSetting.Polite);top.Children.Add(Summary);
        Choices.Background=StudioTheme.Surface;Choices.Foreground=StudioTheme.Text;Choices.BorderBrush=StudioTheme.Border;
        AutomationProperties.SetName(Choices,"DXF'e aktarılacak parçalar");
        AutomationProperties.SetHelpText(Choices,"Satırdaki onay kutusuyla seçim yapın. Her satır parça adını, tam kimliğini ve varsa desteklenmeme nedenini gösterir.");
        VirtualizingPanel.SetIsVirtualizing(Choices,true);VirtualizingPanel.SetVirtualizationMode(Choices,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(Choices,true);
        Choices.SelectionChanged+=(_,_)=> { if(Choices.SelectedIndex>=0)Choices.SelectedIndex=-1; };
        var checkbox=new FrameworkElementFactory(typeof(CheckBox));checkbox.SetValue(FrameworkElement.MarginProperty,new Thickness(4,6,4,6));checkbox.SetValue(Control.ForegroundProperty,StudioTheme.Text);
        checkbox.SetBinding(AutomationProperties.NameProperty,new Binding(nameof(Choice.Label)));
        checkbox.SetBinding(UIElement.IsEnabledProperty,new Binding(nameof(Choice.Eligible)));
        checkbox.SetBinding(ToggleButton.IsCheckedProperty,new Binding(nameof(Choice.IsSelected)) { Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged });
        var text=new FrameworkElementFactory(typeof(TextBlock));text.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);text.SetBinding(TextBlock.TextProperty,new Binding(nameof(Choice.Label)));checkbox.AppendChild(text);
        Choices.ItemTemplate=new DataTemplate { VisualTree=checkbox };
        allChoices=snapshot.Pieces.Select(piece=>new Choice(piece)).ToList();
        foreach(var choice in allChoices)choice.PropertyChanged+=(_,_)=> { if(!updating)RefreshSummary(); };
        view=CollectionViewSource.GetDefaultView(allChoices);view.Filter=item=>Matches((Choice)item);Choices.ItemsSource=view;root.Children.Add(Choices);
        Search.TextChanged+=(_,_)=> { view.Refresh();RefreshSummary(); };
        SelectVisible.Click+=(_,_)=>SetVisible(true);ClearVisible.Click+=(_,_)=>SetVisible(false);RefreshSummary();
    }
    private bool Matches(Choice choice) => choice.SearchText.Contains(Search.Text.Trim(),StringComparison.OrdinalIgnoreCase);
    private void SetVisible(bool selected)
    {
        updating=true;
        try { foreach(var choice in allChoices)if(Matches(choice) && choice.Eligible)choice.IsSelected=selected; }
        finally { updating=false; }
        RefreshSummary();
    }
    private void RefreshSummary()
    {
        var selected=0;var hidden=0;var unsupported=0;
        foreach(var choice in allChoices)
        {
            var visible=Matches(choice);
            if(choice.IsSelected) { selected++;if(!visible)hidden++; }
            if(visible && !choice.Eligible)unsupported++;
        }
        var message=$"{selected} parça seçili; {hidden} seçili parça aramada gizli. Görünen {unsupported} parça normal DXF'te desteklenmez.";
        Summary.Text=message;AutomationProperties.SetName(Summary,message);ContinueButton.IsEnabled=selected>0;
        if(UIElementAutomationPeer.CreatePeerForElement(Summary) is { } peer)peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
