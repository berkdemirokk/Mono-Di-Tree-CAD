using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Data;
using System.Windows.Threading;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using System.Globalization;
using System.Text;
using Microsoft.Win32;
using TekstilCAD.Core;
using TekstilCAD.Nesting;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>Independent, non-destructive quantity layout preview. Never sends output to a machine.</summary>
internal sealed class ProductionNestingWindow : Window
{
    private readonly MainWindow workspace;
    private readonly ProjectDocument initialSnapshot;
    private readonly ListBox choices = new();
    private readonly TextBox choiceSearch = new() { Margin=new Thickness(4,2,4,3), Padding=new Thickness(7,5,7,5), MinWidth=0, MaxLength=1024 };
    private readonly Button clearChoiceSearch = new() { Content="Temizle", Padding=new Thickness(8,4,8,4), Margin=new Thickness(4,2,4,3) };
    private readonly Button selectShownChoices = new() { Content="Görünenleri seç", Padding=new Thickness(4,3,4,3), Margin=new Thickness(4,0,2,3), FontSize=12 };
    private readonly Button clearShownChoices = new() { Content="Görünen seçimi kaldır", Padding=new Thickness(4,3,4,3), Margin=new Thickness(2,0,4,3), FontSize=12 };
    private readonly TextBlock choiceSearchStatus = new() { TextWrapping=TextWrapping.Wrap, Foreground=StudioTheme.Muted, Margin=new Thickness(4,2,4,6) };
    private bool updatingChoices;
    private string choiceSearchQuery="";
    private Guid? exactChoiceFilter;
    private readonly List<ChoiceItem> choiceItems = [];
    private sealed class ChoiceItem : INotifyPropertyChanged
    {
        public Guid Id { get; init; }
        public string Label { get; init; } = "";
        public string Name { get; init; } = "";
        public string DisplayName { get; init; } = "";
        private bool selected;
        private bool navigationTarget;
        public bool IsNavigationTarget { get=>navigationTarget; set { if(navigationTarget==value)return; navigationTarget=value; PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(IsNavigationTarget))); } }
        public bool IsSelected { get=>selected; set { if(selected==value)return; selected=value; PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(IsSelected))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private readonly TextBox width = new() { Text="1500", Width=110, Margin=new Thickness(4) };
    private readonly TextBox fabricLength = new() { Width=110, Margin=new Thickness(4), ToolTip="Boş bırakılırsa kumaş boyu sınırlandırılmaz." };
    private readonly ComboBox grainPolicy = new() { Width=190, Margin=new Thickness(4), ItemsSource=new[] { "İplik yönü denetlenmez", "Tek yön (+Y)", "Çift yön (±Y)" }, SelectedIndex=0 };
    private readonly TextBox gap = new() { Text="5", Width=90, Margin=new Thickness(4) };
    private readonly TextBox edgeMargin = new() { Text="0", Width=80, Margin=new Thickness(4) };
    private readonly TextBlock status = new() { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(4,8,4,8) };
    private readonly Button locateError = new() { Content="Sorunlu parçaya git", Padding=new Thickness(10,6,10,6), Margin=new Thickness(4,0,4,6), HorizontalAlignment=HorizontalAlignment.Left, Visibility=Visibility.Collapsed };
    private Guid? errorTargetId;
    private int errorVersion;
    private ChoiceItem? markedErrorChoice;
    private readonly TextBlock mapping = new() { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(4) };
    private readonly Preview preview = new() { MinHeight=240 };
    private readonly ComboBox highlight = new() { Margin=new Thickness(4), MinWidth=180 };
    private sealed record PlacementChoice(Guid Id, string Label) { public override string ToString()=>Label; }
    private readonly Button calculate = new() { Content="Adetli pastalı hesapla", Padding=new Thickness(12,7,12,7), Margin=new Thickness(4) };
    private readonly Button cancelCalculation = new() { Content="Hesabı iptal et", Padding=new Thickness(12,7,12,7), Margin=new Thickness(4), IsEnabled=false, Visibility=Visibility.Collapsed };
    private CancellationTokenSource? calculationCancellation;
    internal Action? BeforeCalculationForVerification { get; set; }
    private readonly Button saveLayout = new() { Content="Pastalı ayrı proje olarak kaydet…", Padding=new Thickness(12,7,12,7), Margin=new Thickness(4), IsEnabled=false };
    private bool closed, busy, savingLayout;
    // Instance-scoped barrier for hidden verification; normal windows leave it null.
    internal Action? BeforeLayoutSaveForVerification { get; set; }
    private int inputsVersion;
    private readonly DispatcherTimer documentWatch = new() { Interval=TimeSpan.FromSeconds(1) };
    public ProductionNestingWindow(MainWindow workspace, Window owner)
    {
        this.workspace=workspace; initialSnapshot=workspace.Document.Clone(); Owner=owner; Title="Adetli pastal önizlemesi"; Width=1000; Height=760; MinWidth=680; MinHeight=520;
        WindowStartupLocation=WindowStartupLocation.CenterOwner; StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) }; Content=root;
        var intro=new TextBlock { Text="Seçilen kesim parçaları belirtilen adet kadar yerleştirilir. Rol, adet ve tek malzeme belirtilmelidir. Kaynak proje değiştirilmez.", TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,0,0,4) }; DockPanel.SetDock(intro,Dock.Top);root.Children.Add(intro);
        var controls=new WrapPanel(); controls.Children.Add(new TextBlock { Text="Kumaş eni · mm",VerticalAlignment=VerticalAlignment.Center });controls.Children.Add(width);controls.Children.Add(new TextBlock { Text="Parça boşluğu · mm",VerticalAlignment=VerticalAlignment.Center });controls.Children.Add(gap);controls.Children.Add(new TextBlock { Text="Kenar payı · mm",VerticalAlignment=VerticalAlignment.Center });controls.Children.Add(edgeMargin);controls.Children.Add(calculate);controls.Children.Add(cancelCalculation);controls.Children.Add(saveLayout);DockPanel.SetDock(controls,Dock.Top);root.Children.Add(controls);
        var advancedControls=new WrapPanel();
        advancedControls.Children.Add(new TextBlock { Text="Kumaş boyu · mm (isteğe bağlı)",VerticalAlignment=VerticalAlignment.Center });
        advancedControls.Children.Add(fabricLength);advancedControls.Children.Add(grainPolicy);
        var advanced=new Expander { Header="Kumaş boyu ve iplik yönü",Content=advancedControls,Margin=new Thickness(4,0,4,0),FontSize=11 };
        DockPanel.SetDock(advanced,Dock.Top);root.Children.Add(advanced);
        void UpdateAdvancedHeader()
        {
            var lengthSummary=string.IsNullOrWhiteSpace(fabricLength.Text) ? "boy sınırsız" : "boy sınırı girildi";
            var grainSummary=grainPolicy.SelectedIndex switch { 1 => "tek yön", 2 => "çift yön", _ => "iplik denetlenmez" };
            advanced.Header=$"Kumaş boyu ve iplik · {lengthSummary}, {grainSummary}";
        }
        UpdateAdvancedHeader();
        AutomationProperties.SetName(grainPolicy,"İplik yönü doğrulama politikası");
        AutomationProperties.SetHelpText(grainPolicy,"Tek veya çift yön seçilirse her parçanın gözden geçirilmiş iplik yönü kumaş boyuna tam paralel olmalıdır. Parçalar döndürülmez.");
        AutomationProperties.SetName(width,"Kumaş eni, milimetre"); AutomationProperties.SetName(gap,"Parçalar arası boşluk, milimetre");
        AutomationProperties.SetName(fabricLength,"Kullanılabilir kumaş boyu, milimetre, isteğe bağlı");
        AutomationProperties.SetHelpText(fabricLength,"Boş bırakılırsa kumaş boyu sınırlandırılmaz. Bir değer girilirse pastal bu boyu aşamaz.");
        AutomationProperties.SetHelpText(width,"Kumaş eni 0'dan büyük ve en fazla 100 milyon milimetre olmalıdır. Ondalık için virgül veya nokta kullanın.");
        AutomationProperties.SetHelpText(gap,"Parça boşluğu 0 ile 100 milyon milimetre arasında olmalıdır. Ondalık için virgül veya nokta kullanın.");
        AutomationProperties.SetName(edgeMargin,"Dört kenarın her biri için kumaş kenar payı, milimetre");
        AutomationProperties.SetHelpText(edgeMargin,"Aynı pay sol, sağ, başlangıç ve bitiş kenarlarına uygulanır. Sıfır kenar payı eklemez.");
        var warning=new TextBlock { Text="Kenar payı dört kenarın her birine uygulanır; sıfır seçilirse pay eklenmez. Boşluk parçalar arasındadır. İplik yalnız seçilirse denetlenir; dönüş, kusur kontrolü ve makine çıktısı yok.",TextWrapping=TextWrapping.Wrap, Foreground=StudioTheme.Muted, Margin=new Thickness(4,4,4,8) }; DockPanel.SetDock(warning,Dock.Bottom);root.Children.Add(warning);
        var panel=new Grid();panel.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(300) });panel.ColumnDefinitions.Add(new ColumnDefinition());root.Children.Add(panel);
        VirtualizingPanel.SetIsVirtualizing(choices,true);
        VirtualizingPanel.SetVirtualizationMode(choices,VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(choices,true);
        ScrollViewer.SetVerticalScrollBarVisibility(choices,ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(choices,ScrollBarVisibility.Disabled);
        choices.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        ApplyChoiceListTheme();
        AutomationProperties.SetName(choices,"Pastala dahil edilecek parçalar");
        AutomationProperties.SetHelpText(choices,"Parçaları satırdaki onay kutusuyla seçin; yalnız işaretli kutular hesaba katılır.");
        // ListBox provides virtualization; its single-selection state is not the
        // production selection. Clear it immediately so only checkboxes convey it.
        choices.SelectionChanged+=(_,_)=> { if(choices.SelectedIndex>=0) choices.SelectedIndex=-1; };
        var checkbox=new FrameworkElementFactory(typeof(CheckBox));
        checkbox.SetValue(FrameworkElement.MarginProperty,new Thickness(4,6,4,6));
        // Keep text legible while the parent list is disabled; IsEnabled still blocks all input and reports the disabled state to UIA.
        checkbox.SetValue(UIElement.OpacityProperty,1d);
        checkbox.SetValue(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch);
        checkbox.SetBinding(AutomationProperties.NameProperty,new Binding(nameof(ChoiceItem.Label)));
        checkbox.SetBinding(AutomationProperties.HelpTextProperty,new Binding(nameof(ChoiceItem.DisplayName)) { StringFormat="Tam parça adı: {0}" });
        checkbox.SetBinding(FrameworkElement.ToolTipProperty,new Binding(nameof(ChoiceItem.DisplayName)) { StringFormat="Tam parça adı: {0}" });
        var choiceText=new FrameworkElementFactory(typeof(TextBlock));
        choiceText.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);
        choiceText.SetBinding(TextBlock.TextProperty,new Binding(nameof(ChoiceItem.Label)));
        checkbox.AppendChild(choiceText);
        checkbox.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,new Binding(nameof(ChoiceItem.IsSelected)) { Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged });
        var choiceBorder=new FrameworkElementFactory(typeof(Border),"ErrorNavigationBorder");
        choiceBorder.SetValue(Border.BorderBrushProperty,Brushes.Transparent);choiceBorder.SetValue(Border.BorderThicknessProperty,new Thickness(2));choiceBorder.SetValue(Border.CornerRadiusProperty,new CornerRadius(4));choiceBorder.AppendChild(checkbox);
        var choiceTemplate=new DataTemplate { VisualTree=choiceBorder };
        var navigatedTrigger=new DataTrigger { Binding=new Binding(nameof(ChoiceItem.IsNavigationTarget)),Value=true };
        navigatedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty,StudioTheme.Accent,"ErrorNavigationBorder"));choiceTemplate.Triggers.Add(navigatedTrigger);
        choices.ItemTemplate=choiceTemplate;
        choices.ItemsSource=choiceItems;
        var choicePanel=new DockPanel();panel.Children.Add(choicePanel);
        var searchPanel=new StackPanel();DockPanel.SetDock(searchPanel,Dock.Top);choicePanel.Children.Add(searchPanel);
        searchPanel.Children.Add(new TextBlock { Text="Parça adı veya kimliği ara", Margin=new Thickness(4,0,4,2),Foreground=StudioTheme.Muted });
        var searchRow=new DockPanel();searchPanel.Children.Add(searchRow);
        DockPanel.SetDock(clearChoiceSearch,Dock.Right);searchRow.Children.Add(clearChoiceSearch);searchRow.Children.Add(choiceSearch);
        AutomationProperties.SetName(choiceSearch,"Pastal parçası adı veya kimliği ara");
        AutomationProperties.SetHelpText(choiceSearch,"Arama yalnız listeyi süzer; gizli seçili parçalar hesaplamaya dahil kalır.");
        AutomationProperties.SetName(clearChoiceSearch,"Pastal parça aramasını temizle");
        AutomationProperties.SetLiveSetting(choiceSearchStatus,AutomationLiveSetting.Polite);searchPanel.Children.Add(choiceSearchStatus);
        var selectionActions=new Grid();selectionActions.ColumnDefinitions.Add(new ColumnDefinition());selectionActions.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(clearShownChoices,1);selectionActions.Children.Add(selectShownChoices);selectionActions.Children.Add(clearShownChoices);searchPanel.Children.Add(selectionActions);
        AutomationProperties.SetHelpText(selectShownChoices,"Yalnız gösterilen parçaları seçer; arama dışında seçili parçalar korunur.");
        AutomationProperties.SetHelpText(clearShownChoices,"Yalnız gösterilen parçaların seçimini kaldırır; arama dışında seçili parçalar korunur.");
        selectShownChoices.Click+=(_,_)=>SetShownSelection(true);clearShownChoices.Click+=(_,_)=>SetShownSelection(false);
        choicePanel.Children.Add(choices);
        foreach(var p in workspace.Document.Pieces)
        {
            var bounds=p.Points.Count>0 ? TekstilCAD.Core.Geometry.Bounds(p.Points) : default;
            var dimensions=p.Points.Count>0 ? $"{MeasurementInput.Format(bounds.Width)} × {MeasurementInput.Format(bounds.Height)} mm" : "ölçü yok";
            var label=$"{SafeChoiceText(p.Name,100)}\nKimlik: {p.Id:D}\nEn × boy: {dimensions}\nRol: {SafeChoiceText(p.Metadata?.Role,40)} · Adet: {p.Metadata?.Quantity?.ToString(CultureInfo.InvariantCulture) ?? "belirtilmedi"}\nMalzeme: {SafeChoiceText(p.Metadata?.Material,60)} · Beden: {SafeChoiceText(p.Metadata?.Size,60)}";
            var displayedName=SafeChoiceText(p.Name,int.MaxValue);
            var choice=new ChoiceItem { Id=p.Id,Label=label,Name=p.Name,DisplayName=displayedName,IsSelected=p.Id==workspace.SelectedId };
            choice.PropertyChanged+=(_,e)=> { if(e.PropertyName==nameof(ChoiceItem.IsSelected) && !updatingChoices){InvalidatePreview();UpdateChoiceSearchStatus();} }; choiceItems.Add(choice);
        }
        choices.Items.Filter=MatchesChoiceSearch;
        CollectionViewSource.GetDefaultView(choiceItems).Refresh();
        choiceSearch.TextChanged+=(_,_)=>RefreshChoiceSearch();clearChoiceSearch.Click+=(_,_)=>choiceSearch.Clear();
        UpdateChoiceSearchStatus();
        var right=new DockPanel { Margin=new Thickness(12,0,0,0) };Grid.SetColumn(right,1);panel.Children.Add(right);
        var statusViewer=new ScrollViewer { Content=status,MaxHeight=72,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=true };
        KeyboardNavigation.SetIsTabStop(statusViewer,true);
        AutomationProperties.SetName(statusViewer,"Pastal işlem sonucu ayrıntıları");
        AutomationProperties.SetHelpText(statusViewer,"Uzun sonuçları yön tuşlarıyla kaydırabilirsiniz.");
        DockPanel.SetDock(statusViewer,Dock.Top);right.Children.Add(statusViewer);
        DockPanel.SetDock(locateError,Dock.Top);right.Children.Add(locateError);
        AutomationProperties.SetHelpText(locateError,"Hata veren kaynak parçayı kimliğiyle filtreler; seçim kutularını değiştirmez. Tüm listeyi görmek için aramayı temizleyin.");
        locateError.Click+=(_,_)=>GoToErrorChoice();
        AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(mapping,AutomationLiveSetting.Polite);
        AutomationProperties.SetName(highlight,"Çizimde vurgulanacak yerleşim");
        AutomationProperties.SetHelpText(highlight,"Listeden bir kopya seçin; konumu ve boyutları metin olarak gösterilir.");
        var placementText=new FrameworkElementFactory(typeof(TextBlock));
        placementText.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);
        placementText.SetValue(FrameworkElement.MaxWidthProperty,260d);
        placementText.SetBinding(TextBlock.TextProperty,new Binding(nameof(PlacementChoice.Label)));
        placementText.SetBinding(AutomationProperties.NameProperty,new Binding(nameof(PlacementChoice.Label)));
        highlight.ItemTemplate=new DataTemplate { VisualTree=placementText };
        highlight.SelectionChanged+=(_,_)=>UpdateHighlight();
        DockPanel.SetDock(highlight,Dock.Top);right.Children.Add(highlight);
        var mapScroll=new ScrollViewer { Content=mapping,MaxHeight=145,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Focusable=true };KeyboardNavigation.SetIsTabStop(mapScroll,true);AutomationProperties.SetName(mapScroll,"Seçili pastal parçası ayrıntıları");DockPanel.SetDock(mapScroll,Dock.Bottom);right.Children.Add(mapScroll);right.Children.Add(preview);
        AutomationProperties.SetName(preview,"Adetli pastal geometrisi önizlemesi");
        AutomationProperties.SetHelpText(preview,"Dış çerçeve kumaş enini ve kullanılan boyu gösterir. Pozitif kenar payında kesik iç çerçeve kullanılabilir alanı gösterir; pay miktarı sonuç metnindedir. Fiziksel baskı ve makine uygunluğu doğrulanmaz.");
        width.TextChanged+=(_,_)=>InvalidatePreview(); fabricLength.TextChanged+=(_,_)=> { UpdateAdvancedHeader();InvalidatePreview(); }; gap.TextChanged+=(_,_)=>InvalidatePreview(); edgeMargin.TextChanged+=(_,_)=>InvalidatePreview();
        grainPolicy.SelectionChanged+=(_,_)=> { UpdateAdvancedHeader();InvalidatePreview(); };
        calculate.Click+=async (_,_)=>await Calculate();
        cancelCalculation.Click+=(_,_)=>RequestCalculationCancellation();
        AutomationProperties.SetHelpText(cancelCalculation,"Pastal hesaplamasını durdurur; mevcut proje değiştirilmez.");
        saveLayout.Click+=async (_,_)=>await SaveLayout();
        documentWatch.Tick+=(_,_)=>CheckVisibleResultCurrent();
        Closing+=(_,e)=> { if(savingLayout) { e.Cancel=true; SetStatus("Pastal kaydı sürüyor; kapatmak için işlemin bitmesini bekleyin."); } else if(calculationCancellation is not null) { e.Cancel=true; RequestCalculationCancellation(); } };
        Closed+=(_,_)=> { closed=true; documentWatch.Stop(); };
        SetStatus("Parçaları seçin ve hesaplayın.");
    }
    // The operating-system ListBox template replaces its background with a light
    // system color when disabled. A local template keeps the dark surface while
    // retaining the standard ScrollViewer/ItemsPresenter virtualization contract.
    private void ApplyChoiceListTheme()
    {
        var border=new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty,new TemplateBindingExtension(Control.BorderThicknessProperty));
        var scroll=new FrameworkElementFactory(typeof(ScrollViewer),"PART_ScrollViewer");
        scroll.SetValue(ScrollViewer.CanContentScrollProperty,true);
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty,ScrollBarVisibility.Auto);
        scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty,ScrollBarVisibility.Disabled);
        scroll.SetValue(UIElement.FocusableProperty,false);
        scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        border.AppendChild(scroll);
        choices.Template=new ControlTemplate(typeof(ListBox)) { VisualTree=border };

        // The shared item template reduces opacity on disable. Combined with the
        // checkbox's own disabled opacity that makes labels illegible. Keep a
        // visible focus border without dimming either disabled layer.
        var chrome=new FrameworkElementFactory(typeof(Border),"Chrome");
        chrome.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));
        chrome.SetValue(Border.PaddingProperty,new TemplateBindingExtension(Control.PaddingProperty));
        chrome.SetValue(Border.BorderThicknessProperty,new Thickness(1));
        chrome.SetValue(Border.BorderBrushProperty,Brushes.Transparent);
        chrome.SetValue(Border.CornerRadiusProperty,new CornerRadius(5));
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Stretch);
        chrome.AppendChild(presenter);
        var itemTemplate=new ControlTemplate(typeof(ListBoxItem)) { VisualTree=chrome };
        var focused=new Trigger { Property=UIElement.IsKeyboardFocusedProperty,Value=true };
        focused.Setters.Add(new Setter(Border.BorderBrushProperty,StudioTheme.Accent,"Chrome"));
        itemTemplate.Triggers.Add(focused);
        var hover=new Trigger { Property=UIElement.IsMouseOverProperty,Value=true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty,StudioTheme.Raised,"Chrome"));
        itemTemplate.Triggers.Add(hover);
        var baseStyle=TryFindResource(typeof(ListBoxItem)) as Style;
        var itemStyle=new Style(typeof(ListBoxItem),baseStyle);
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty,itemTemplate));
        choices.ItemContainerStyle=itemStyle;
    }
    private void RequestCalculationCancellation()
    {
        if(calculationCancellation is null)return;
        calculationCancellation.Cancel();cancelCalculation.IsEnabled=false;
        SetStatus("İptal istendi; işlem durana kadar bekleyin. Kaynak proje değişmez.");
    }
    private void SetShownSelection(bool selected)
    {
        if(busy || savingLayout)return;
        var changed=false;updatingChoices=true;
        try
        {
            foreach(var choice in choiceItems)
                if(MatchesChoiceSearch(choice) && choice.IsSelected!=selected)
                { choice.IsSelected=selected;changed=true; }
        }
        finally { updatingChoices=false; }
        if(changed){InvalidatePreview();UpdateChoiceSearchStatus();}
    }
    private bool MatchesChoiceSearch(object item)
    {
        if(item is not ChoiceItem choice)return false;
        if(exactChoiceFilter is { } exact)return choice.Id==exact;
        var query=choiceSearchQuery;if(query.Length==0)return true;
        var compare=CultureInfo.GetCultureInfo("tr-TR").CompareInfo;
        return compare.IndexOf(choice.Name,query,CompareOptions.IgnoreCase)>=0 ||
            compare.IndexOf(choice.DisplayName,query,CompareOptions.IgnoreCase)>=0 ||
            choice.Id.ToString("D").Contains(query,StringComparison.OrdinalIgnoreCase);
    }
    private void RefreshChoiceSearch()
    {
        exactChoiceFilter=null;choiceSearchQuery=choiceSearch.Text.Trim();
        CollectionViewSource.GetDefaultView(choiceItems).Refresh();
        UpdateChoiceSearchStatus();
    }
    private void UpdateChoiceSearchStatus()
    {
        var shown=choices.Items.Count;var selected=choiceItems.Count(item=>item.IsSelected);
        var hidden=choiceItems.Count(item=>item.IsSelected && !MatchesChoiceSearch(item));
        var message=(shown==0 && choiceSearchQuery.Length>0 ? "Eşleşen parça yok." : $"{shown} / {choiceItems.Count} parça gösteriliyor.")+
            $" Seçili: {selected}."+(hidden>0 ? $" Arama dışında seçili: {hidden}; hesaplamaya dahildir." : "");
        if(choiceSearchStatus.Text==message)return;
        choiceSearchStatus.Text=message;
        AutomationProperties.SetName(choiceSearchStatus,"Pastal parça arama sonucu: "+message);
        Announce(choiceSearchStatus);
    }
    private static string SafeChoiceText(string? value,int maximumCodeUnits)
    {
        if(string.IsNullOrEmpty(value))return "belirtilmedi";
        var text=new StringBuilder();var i=0;
        while(i<value.Length && i<maximumCodeUnits)
        {
            var c=value[i++];
            if(char.IsHighSurrogate(c) && i<value.Length && char.IsLowSurrogate(value[i]))
            {
                var low=value[i++];
                var pairCategory=CharUnicodeInfo.GetUnicodeCategory(value,i-2);
                if(pairCategory is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                    text.Append($"\\u{(int)c:X4}\\u{(int)low:X4}");
                else { text.Append(c);text.Append(low); }
                continue;
            }
            var category=char.GetUnicodeCategory(c);
            if(char.IsSurrogate(c) || category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                text.Append($"\\u{(int)c:X4}");
            else text.Append(c);
        }
        if(i<value.Length)text.Append('…');
        return text.ToString();
    }
    // Resolve a reported source ID against the immutable calculation snapshot, never
    // against a subsequently changed live document or a generated copy identity.
    internal static string FormatNestingFailure(Exception error,ProjectDocument snapshot)
    {
        if(NestingPieceError.TryGet(error,out var pieceId,out var reason))
        {
            var source=snapshot.Pieces.FirstOrDefault(p=>p.Id==pieceId);
            var name=source is null ? "bulunamadı" : SafeChoiceText(source.Name,int.MaxValue);
            return $"Hesaplanamadı: {SafeChoiceText(reason,int.MaxValue)}\nKimlik: {pieceId:D}\nKaynak parça: {name}";
        }
        return "Hesaplanamadı: "+SafeChoiceText(error.Message,int.MaxValue);
    }
    private void ClearErrorChoice()
    {
        errorTargetId=null;locateError.Visibility=Visibility.Collapsed;
        if(markedErrorChoice is not null) { markedErrorChoice.IsNavigationTarget=false;markedErrorChoice=null; }
    }
    private void GoToErrorChoice()
    {
        if(busy || closed || errorTargetId is not { } id)return;
        if(errorVersion!=inputsVersion || !DocumentSession.ContentEquals(initialSnapshot,workspace.Document))
        {
            InvalidatePreview();SetStatus("Proje veya seçim değişti; hata güncel değil. Pencereyi kapatıp yeniden açın.");return;
        }
        var target=choiceItems.FirstOrDefault(c=>c.Id==id);
        if(target is null) { ClearErrorChoice();SetStatus("Sorunlu kaynak parça artık listede yok; pencereyi yeniden açın.");return; }
        choiceSearch.Text=id.ToString("D");
        exactChoiceFilter=id;CollectionViewSource.GetDefaultView(choiceItems).Refresh();UpdateChoiceSearchStatus();
        if(markedErrorChoice is not null && !ReferenceEquals(markedErrorChoice,target))markedErrorChoice.IsNavigationTarget=false;
        markedErrorChoice=target;target.IsNavigationTarget=true;
        choices.ScrollIntoView(target);choices.UpdateLayout();

        const string navigationNote="\nSorunlu parçayı göstermek için arama kimliğe ayarlandı; işaretli parçalar değiştirilmedi. Tüm parçalar için aramayı temizleyin.";
        if(!status.Text.EndsWith(navigationNote,StringComparison.Ordinal))SetStatus(status.Text+navigationNote);
        // The container can be realized asynchronously by the virtualizing panel.
        // Recheck the error identity and project before moving keyboard focus.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>
        {
            if(closed || busy || errorTargetId!=id || errorVersion!=inputsVersion)return;
            if(!DocumentSession.ContentEquals(initialSnapshot,workspace.Document))
            { InvalidatePreview();SetStatus("Proje değişti; hata güncel değil. Pencereyi kapatıp yeniden açın.");return; }
            choices.ScrollIntoView(target);choices.UpdateLayout();

            if(choices.ItemContainerGenerator.ContainerFromItem(target) is DependencyObject container && FindChoiceCheckbox(container) is { } checkbox)
            { checkbox.BringIntoView();checkbox.Focus(); }
        }));
    }
    private static CheckBox? FindChoiceCheckbox(DependencyObject parent)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is CheckBox checkbox)return checkbox;
            if(FindChoiceCheckbox(child) is { } found)return found;
        }
        return null;
    }
    private static void Announce(TextBlock block)
    {
        if (UIElementAutomationPeer.CreatePeerForElement(block) is { } peer)
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    private void SetStatus(string text) { status.Text=text; Announce(status); }
    private void UpdateHighlight()
    {
        var selected=highlight.SelectedItem as PlacementChoice;
        preview.HighlightId=selected?.Id;
        var piece=preview.Drawing?.Pieces.FirstOrDefault(p=>p.Id==selected?.Id);
        if (piece is null || piece.Points.Count==0) { mapping.Text=""; Announce(mapping); return; }
        var bounds=TekstilCAD.Core.Geometry.Bounds(piece.Points);
        mapping.Text=$"{selected!.Label} · Kumaş solundan {MeasurementInput.Format(bounds.MinX)} mm, üstünden {MeasurementInput.Format(bounds.MinY)} mm · {MeasurementInput.Format(bounds.Width)} × {MeasurementInput.Format(bounds.Height)} mm. Çizimde kesik ve kalın çizgiyle gösterilir.";
        Announce(mapping);
    }
    private void InvalidatePreview()
    {
        inputsVersion++; ClearErrorChoice();
        documentWatch.Stop();
        preview.Drawing=null; highlight.ItemsSource=null; mapping.Text="";saveLayout.IsEnabled=false;
        SetStatus("Seçim veya ölçüler değişti; güncel önizleme için yeniden hesaplayın.");
    }
    private void CheckVisibleResultCurrent()
    {
        if (preview.Drawing is null && errorTargetId is null) { documentWatch.Stop(); return; }
        if (workspace.HasPendingCanvasOrPropertyWork)
        {
            InvalidatePreview();
            SetStatus("Bekleyen çizim veya parça alan taslağı var; önce tamamlayın veya iptal edin, sonra pastalı yeniden hesaplayın.");
            return;
        }
        if (!DocumentSession.ContentEquals(initialSnapshot, workspace.Document))
        {
            InvalidatePreview();
            SetStatus("Proje değişti; önceki sonuç kaldırıldı. Güncel parçalar için pencereyi kapatıp yeniden açın.");
        }
    }
    // Validate the three related fabric settings before scheduling the worker so
    // operators get an actionable field-specific explanation in the result area.
    internal static string? ValidateFabricInputs(string widthText,string gapText,string marginText,out double fabricWidth,out double pieceGap,out double margin)
    {
        fabricWidth=0;pieceGap=0;margin=0;
        if(!MeasurementInput.TryParseMillimeters(widthText,out fabricWidth) || fabricWidth<=0 || fabricWidth>NestingEngine.MaximumDimension)
            return "Kumaş eni 0'dan büyük ve en fazla 100.000.000 mm olmalıdır.";
        if(!MeasurementInput.TryParseMillimeters(gapText,out pieceGap) || pieceGap<0 || pieceGap>NestingEngine.MaximumDimension)
            return "Parça boşluğu 0 ile 100.000.000 mm arasında olmalıdır.";
        if(!MeasurementInput.TryParseMillimeters(marginText,out margin) || margin<0 || margin>NestingEngine.MaximumDimension)
            return "Kenar payı 0 ile 100.000.000 mm arasında olmalıdır.";
        if(margin*2>=fabricWidth)
            return "İki kenar payının toplamı kumaş eninden küçük olmalıdır; yerleşim için kullanılabilir en bırakın.";
        return null;
    }
    private async Task Calculate()
    {
        if(busy)return; ClearErrorChoice();
        if(workspace.HasPendingCanvasOrPropertyWork)
        { InvalidatePreview();SetStatus("Önce bekleyen çizimi, sürüklemeyi veya parça alan taslaklarını tamamlayın ya da iptal edin.");return; }
        if(!DocumentSession.ContentEquals(initialSnapshot,workspace.Document)) { InvalidatePreview();SetStatus("Proje değişti; güncel parçalar için pencereyi kapatıp yeniden açın.");return; }
        var inputProblem=ValidateFabricInputs(width.Text,gap.Text,edgeMargin.Text,out var w,out var g,out var m);
        if(inputProblem is not null) { SetStatus(inputProblem);return; }
        var length=double.PositiveInfinity;
        if(!string.IsNullOrWhiteSpace(fabricLength.Text) &&
           (!MeasurementInput.TryParseMillimeters(fabricLength.Text,out length) || length<=0 || length>NestingEngine.MaximumDimension || 2*m>=length))
        { SetStatus("Kumaş boyu 0'dan büyük, en fazla 100.000.000 mm ve iki kenar payının toplamından büyük olmalıdır; boş bırakılırsa sınırsızdır.");return; }
        var ids=choiceItems.Where(c=>c.IsSelected).Select(c=>c.Id).ToArray();
        var grainMode=grainPolicy.SelectedIndex;
        var snapshot=workspace.Document.Clone(); var requestVersion=inputsVersion;
        var cancellation=new CancellationTokenSource();calculationCancellation=cancellation;var token=cancellation.Token;
        var beforeCalculation=BeforeCalculationForVerification;cancelCalculation.Visibility=Visibility.Visible;cancelCalculation.IsEnabled=true;
        busy=true; calculate.IsEnabled=false;saveLayout.IsEnabled=false;choices.IsEnabled=false;choiceSearch.IsEnabled=false;clearChoiceSearch.IsEnabled=false;selectShownChoices.IsEnabled=false;clearShownChoices.IsEnabled=false;width.IsEnabled=false;fabricLength.IsEnabled=false;grainPolicy.IsEnabled=false;gap.IsEnabled=false;edgeMargin.IsEnabled=false;preview.Drawing=null;highlight.ItemsSource=null;mapping.Text="";SetStatus("Hesaplanıyor…");
        try
        {
            var prepared=await Task.Run(()=>
            {
                beforeCalculation?.Invoke();token.ThrowIfCancellationRequested();
                var result=grainMode switch {
                    1 => GrainConstrainedNesting.Arrange(snapshot,ids,w,g,length,m,GrainDirectionPolicy.Direction.OneWay,0,token),
                    2 => GrainConstrainedNesting.Arrange(snapshot,ids,w,g,length,m,GrainDirectionPolicy.Direction.TwoWay,0,token),
                    0 => ProductionNestingPlan.Arrange(snapshot,ids,w,g,length,m,token),
                    _ => throw new InvalidOperationException("Geçersiz iplik yönü politikası.")
                };
                token.ThrowIfCancellationRequested();
                var layout=result.Nesting.Layout;
                // Preserve the source import record as historical context in the
                // separate layout project. Original piece IDs may no longer be
                // present because placements receive new IDs.
                layout.ImportProvenance=snapshot.ImportProvenance?.Clone();
                layout.NestingSnapshot=new NestingSnapshot
                {
                    FabricWidth=result.Nesting.FabricWidth,
                    UsedLength=result.Nesting.UsedLength,
                    Gap=result.Nesting.Gap,
                    EdgeMargin=result.Nesting.EdgeMargin,
                    Algorithm=NestingSnapshot.ShelfAlgorithm,
                    LayoutFingerprint=NestingSnapshot.ComputeLayoutFingerprint(layout,token),
                    Placements=result.Placements.Select(p=>new NestingPlacementSnapshot(p.SourcePieceId,p.CopyNumber,p.PlacedPieceId)).ToList()
                };
                // Build frozen geometry and labels away from the UI dispatcher.
                token.ThrowIfCancellationRequested();
                var geometry=Preview.BuildGeometryCancellable(layout,token);
                token.ThrowIfCancellationRequested();
                var sources=snapshot.Pieces.ToDictionary(p=>p.Id);
                var dimensions=result.Placements.Select(p=>p.SourcePieceId).Distinct().ToDictionary(id=>id,id=> { token.ThrowIfCancellationRequested();return TekstilCAD.Core.Geometry.Bounds(sources[id].Points); });
                var placementChoices=result.Placements.Select((placement,index)=>
                {
                    token.ThrowIfCancellationRequested();
                    var source=sources[placement.SourcePieceId];var bounds=dimensions[placement.SourcePieceId];
                    var label=$"{index+1}. {SafeChoiceText(source.Name,60)} · kopya {placement.CopyNumber}\nKaynak: {source.Id:D} · {MeasurementInput.Format(bounds.Width)} × {MeasurementInput.Format(bounds.Height)} mm";
                    return new PlacementChoice(placement.PlacedPieceId,label);
                }).ToArray();
                token.ThrowIfCancellationRequested();
                return (Plan:result,Geometry:geometry,Choices:placementChoices);
            });
            if(closed)return;
            token.ThrowIfCancellationRequested();
            var planned=prepared.Plan;
            if(requestVersion!=inputsVersion) { InvalidatePreview(); return; }
            if(workspace.HasPendingCanvasOrPropertyWork)
            { InvalidatePreview();SetStatus("Hesap sırasında bekleyen çizim veya alan taslağı oluştu; sonuç uygulanmadı. Önce bunları tamamlayın veya iptal edin.");return; }
            if(!DocumentSession.ContentEquals(snapshot,workspace.Document)) { SetStatus("Proje değişti; sonuç uygulanmadı. Pencereyi kapatıp yeniden açın.");return; }
            preview.SetDrawing(planned.Nesting.Layout,w,planned.Nesting.UsedLength,planned.Nesting.EdgeMargin,prepared.Geometry);
            saveLayout.IsEnabled=true;
            documentWatch.Start();
            highlight.ItemsSource=prepared.Choices; if(prepared.Choices.Length>0)highlight.SelectedIndex=0;
            var edgeDescription=planned.Nesting.EdgeMargin>0
                ? $"Dört kenarda {MeasurementInput.Format(planned.Nesting.EdgeMargin)} mm pay; kesik iç çerçeve kullanılabilir alanı gösterir."
                : "Kenar payı 0 mm; kesik iç çerçeve yok.";
            var grainDescription=grainMode switch { 1 => "İplik yönü tek yön (+Y) için doğrulandı.", 2 => "İplik yönü çift yön (±Y) için doğrulandı.", _ => "İplik yönü denetlenmedi." };
            SetStatus($"{planned.Placements.Count} parça · kullanılan boy {MeasurementInput.Format(planned.Nesting.UsedLength)} mm · kullanım %{planned.Nesting.UtilizationPercent:0.##}. {edgeDescription} {grainDescription} Kaynak proje değişmedi.");
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { if(!closed)SetStatus("Hesap iptal edildi; kaynak proje değiştirilmedi."); }
        catch(Exception ex)
        {
            if(!closed)
            {
                if(requestVersion!=inputsVersion || !DocumentSession.ContentEquals(initialSnapshot,workspace.Document))
                { ClearErrorChoice();SetStatus("Proje veya seçim değişti; hata güncel değil. Pencereyi kapatıp yeniden açın."); }
                else
                {
                    SetStatus(FormatNestingFailure(ex,snapshot));
                    if(NestingPieceError.TryGet(ex,out var failedId,out _) && choiceItems.Count(c=>c.Id==failedId)==1)
                    { errorTargetId=failedId;errorVersion=requestVersion;locateError.Visibility=Visibility.Visible;documentWatch.Start(); }
                }
            }
        }
        finally { calculationCancellation=null;cancellation.Dispose();cancelCalculation.IsEnabled=false;cancelCalculation.Visibility=Visibility.Collapsed;busy=false;if(!closed){calculate.IsEnabled=true;choices.IsEnabled=true;choiceSearch.IsEnabled=true;clearChoiceSearch.IsEnabled=true;selectShownChoices.IsEnabled=true;clearShownChoices.IsEnabled=true;width.IsEnabled=true;fabricLength.IsEnabled=true;grainPolicy.IsEnabled=true;gap.IsEnabled=true;edgeMargin.IsEnabled=true;} }
    }
    private bool IsCurrentLayout(int version, ProjectDocument layout) => !closed && !busy && !workspace.HasPendingCanvasOrPropertyWork && version==inputsVersion &&
        ReferenceEquals(preview.Drawing,layout) && DocumentSession.ContentEquals(initialSnapshot,workspace.Document);
    private async Task SaveLayout()
    {
        if(workspace.HasPendingCanvasOrPropertyWork)
        { InvalidatePreview();SetStatus("Bekleyen çizim veya parça alan taslağı var; önce tamamlayın veya iptal edin, sonra pastalı yeniden hesaplayın.");return; }
        if(preview.Drawing is not { } layout || !IsCurrentLayout(inputsVersion,layout)) { InvalidatePreview();return; }
        var version=inputsVersion;
        var message=FormatLayoutSaveConfirmation(layout);
        if(MessageBox.Show(this,message,"Pastalı ayrı kaydet",MessageBoxButton.YesNo,MessageBoxImage.Information,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        if(!IsCurrentLayout(version,layout)) { InvalidatePreview();return; }
        var dialog=new SaveFileDialog { Title="Pastalı ayrı proje olarak kaydet",Filter="Mono Di Tree CAD projesi (*.tcad)|*.tcad",FileName="Pastal.tcad",AddExtension=true,DefaultExt=".tcad",InitialDirectory=AppPaths.PrepareProjectsDirectory() };
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            if(!IsCurrentLayout(version,layout)) { InvalidatePreview();return; }
            var expected=CaptureLayoutDestination(dialog.FileName,version,layout);
            if(expected.Sha256 is not null && MessageBox.Show(this,
                "Seçilen dosya ayrı pastal projesiyle değiştirilsin mi? Kayıt tamamlanana kadar aynı dosyayı başka uygulamalarda değiştirmeyin. Dosyayı korumak için farklı bir ad seçebilirsiniz.",
                "Mevcut dosyayı değiştir",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
            await StartSaveLayoutAsync(dialog.FileName,version,layout,expected);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            var previewCurrent=IsCurrentLayout(version,layout);
            SetStatus(previewCurrent
                ? "Pastal kaydedilemedi; önizleme korunuyor: "+ex.Message
                : "Pastal kaydedilemedi; proje veya önizleme değişti. Güncel sonuç için yeniden hesaplayın: "+ex.Message);
        }
    }
    internal static string FormatLayoutSaveConfirmation(ProjectDocument layout)
    {
        var fileVersion=layout.ImportProvenance is not null ? 5 : layout.Pieces.Any(p => p.Grainline is not null) ? 4 : 3;
        return $"Ayrı proje yerleştirilmiş parçaları, kumaş ölçülerini, boşluğu, kenar payını ve kaynak-kopya eşleşmesini tarihsel pastal bilgisi olarak saklar. Parçalar sonradan değiştirilirse bu bilgi güncel değildir. Bu dosya sürüm {fileVersion} biçimindedir; eski TekstilCAD sürümleri açamaz. Bu bir makine işi değildir. Devam edilsin mi?";
    }
    internal ProjectFileVersion CaptureLayoutDestination(string path,int version,ProjectDocument layout)
    {
        if(!IsCurrentLayout(version,layout)) throw new InvalidOperationException("Pastal önizlemesi değişti; yeniden hesaplayın.");
        if(!string.Equals(Path.GetExtension(path),".tcad",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Pastal dosyası .tcad uzantısıyla kaydedilmelidir.");
        workspace.EnsureSeparateLayoutDestination(path);
        return ProjectStorage.CaptureVersion(path);
    }
    internal void SaveLayoutTo(string path,int version,ProjectDocument layout,ProjectFileVersion expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if(!IsCurrentLayout(version,layout)) throw new InvalidOperationException("Pastal önizlemesi değişti; yeniden hesaplayın.");
        if(!string.Equals(Path.GetExtension(path),".tcad",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Pastal dosyası .tcad uzantısıyla kaydedilmelidir.");
        workspace.EnsureSeparateLayoutDestination(path);
        ProjectStorage.SaveIfUnchanged(layout.Clone(),path,expected);
    }
    // The snapshot and all UI-owned state are captured on the dispatcher. The worker
    // sees only detached data and never touches the open document or WPF controls.
    internal async Task StartSaveLayoutAsync(string path,int version,ProjectDocument layout,ProjectFileVersion expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if(!IsCurrentLayout(version,layout)) throw new InvalidOperationException("Pastal önizlemesi değişti; yeniden hesaplayın.");
        if(!string.Equals(Path.GetExtension(path),".tcad",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Pastal dosyası .tcad uzantısıyla kaydedilmelidir.");
        workspace.EnsureSeparateLayoutDestination(path);
        var snapshot=layout.Clone();
        var beforeSave=BeforeLayoutSaveForVerification;
        savingLayout=true;busy=true;
        calculate.IsEnabled=false;saveLayout.IsEnabled=false;choices.IsEnabled=false;choiceSearch.IsEnabled=false;clearChoiceSearch.IsEnabled=false;selectShownChoices.IsEnabled=false;clearShownChoices.IsEnabled=false;width.IsEnabled=false;fabricLength.IsEnabled=false;grainPolicy.IsEnabled=false;gap.IsEnabled=false;edgeMargin.IsEnabled=false;
        SetStatus("Pastal ayrı proje olarak kaydediliyor; pencereyi kapatmak için işlemin bitmesini bekleyin.");
        try
        {
            await Task.Run(()=> { beforeSave?.Invoke(); ProjectStorage.SaveIfUnchanged(snapshot,path,expected); });
            if(closed)return;
            var current=!workspace.HasPendingCanvasOrPropertyWork && version==inputsVersion && ReferenceEquals(preview.Drawing,layout) && DocumentSession.ContentEquals(initialSnapshot,workspace.Document);
            SetStatus(current
                ? "Pastal ayrı proje olarak kaydedildi. Kumaş ayarları ve kaynak-kopya eşleşmesi tarihsel bilgi olarak saklandı; makine işi oluşturulmadı. Eski TekstilCAD sürümleri bu dosyayı açamaz."
                : "Pastalın önceki önizlemesi ayrı proje olarak kaydedildi; proje, seçimler veya bekleyen çizim/alan taslakları işlem sırasında değişti. Güncel sonuç için yeniden hesaplayın. Kaydedilen pastal bilgisi tarihsel görüntüye aittir.");
        }
        finally
        {
            savingLayout=false;busy=false;
            if(!closed)
            {
                calculate.IsEnabled=true;choices.IsEnabled=true;choiceSearch.IsEnabled=true;clearChoiceSearch.IsEnabled=true;selectShownChoices.IsEnabled=true;clearShownChoices.IsEnabled=true;width.IsEnabled=true;fabricLength.IsEnabled=true;grainPolicy.IsEnabled=true;gap.IsEnabled=true;edgeMargin.IsEnabled=true;
                saveLayout.IsEnabled=IsCurrentLayout(version,layout);
            }
        }
    }
    private sealed class Preview : FrameworkElement
    {
        private ProjectDocument? drawing;
        private StreamGeometry? geometry;
        private Guid? highlightId;
        public Guid? HighlightId { get=>highlightId; set { highlightId=value; InvalidateVisual(); } }
        private double fabricWidth, usedLength, edgeMargin;
        public ProjectDocument? Drawing { get=>drawing;set { drawing=value;geometry=null;edgeMargin=0;InvalidateVisual(); } }
        internal static StreamGeometry BuildGeometry(ProjectDocument value) => BuildGeometryCancellable(value,CancellationToken.None);
        internal static StreamGeometry BuildGeometryCancellable(ProjectDocument value,CancellationToken cancellationToken)
        {
            var cached = new StreamGeometry();
            using (var context = cached.Open())
            {
                foreach (var piece in value.Pieces)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (piece.Points.Count < 2) continue;
                    context.BeginFigure(new Point(piece.Points[0].X,piece.Points[0].Y),false,piece.Closed);
                    for(var i=1;i<piece.Points.Count;i++)
                    {
                        if((i&255)==0)cancellationToken.ThrowIfCancellationRequested();
                        context.LineTo(new Point(piece.Points[i].X,piece.Points[i].Y),true,false);
                    }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();cached.Freeze();return cached;
        }
        public void SetDrawing(ProjectDocument value,double widthMm,double lengthMm,double marginMm,StreamGeometry cached)
        {
            if(!cached.IsFrozen)throw new ArgumentException("Önizleme geometrisi dondurulmuş olmalıdır.",nameof(cached));
            drawing=value;geometry=cached;fabricWidth=widthMm;usedLength=lengthMm;edgeMargin=marginMm;InvalidateVisual();
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);dc.DrawRectangle(StudioTheme.Background,null,new Rect(RenderSize));
            if(drawing is null||drawing.Pieces.Count==0||ActualWidth<=24||ActualHeight<=24)return;
            if(geometry is null)return;
            if(fabricWidth<=0||usedLength<=0)return;
            var scale=Math.Min((ActualWidth-24)/fabricWidth,(ActualHeight-24)/usedLength);
            dc.DrawRectangle(Brushes.Transparent,new Pen(Brushes.SlateGray,1),new Rect(12,12,fabricWidth*scale,usedLength*scale));
            // Cache model-space contours once per result; only the scale changes on resize.
            // Compensate the stroke width for the transform so it remains one screen unit.
            dc.PushTransform(new MatrixTransform(new Matrix(scale,0,0,scale,12,12)));
            if(edgeMargin>0 && fabricWidth>2*edgeMargin && usedLength>2*edgeMargin)
            {
                var guidePen=new Pen(Brushes.Goldenrod,1/scale) { DashStyle=DashStyles.Dash };
                dc.DrawRectangle(null,guidePen,new Rect(edgeMargin,edgeMargin,fabricWidth-2*edgeMargin,usedLength-2*edgeMargin));
            }
            dc.DrawGeometry(null,new Pen(Brushes.MediumTurquoise,1/scale),geometry);
            var selected=drawing.Pieces.FirstOrDefault(p=>p.Id==highlightId);
            if(selected is { Points.Count: >1 })
            {
                var selectedGeometry=new StreamGeometry();
                using(var context=selectedGeometry.Open()) { context.BeginFigure(new Point(selected.Points[0].X,selected.Points[0].Y),false,selected.Closed); context.PolyLineTo(selected.Points.Skip(1).Select(p=>new Point(p.X,p.Y)).ToArray(),true,false); }
                selectedGeometry.Freeze(); var selectedPen=new Pen(Brushes.Orange,3/scale) { DashStyle=DashStyles.Dash }; dc.DrawGeometry(null,selectedPen,selectedGeometry);
            }
            dc.Pop();
        }
    }
}
