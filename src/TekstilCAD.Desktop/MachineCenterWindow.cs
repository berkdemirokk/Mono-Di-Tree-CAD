using System.Globalization;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Desktop;

/// <summary>Local exchange-file preparation and checksum verification; no device transport.</summary>
public sealed class MachineCenterWindow : Window
{
    private readonly MainWindow studio;
    private readonly SimulationQueue queue;
    private readonly ProjectDocument openingSnapshot;
    private readonly List<ChoiceItem> partChoices=[];
    private readonly ListBox choiceList=new();
    private readonly TextBox choiceSearch=new() { Margin=new Thickness(0,4,8,4),MinWidth=160,Width=250 };
    private readonly Button clearChoiceSearch=new() { Content="Aramayı temizle",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,4,0,4) };
    private readonly TextBlock choiceSearchStatus=new() { TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Muted,Margin=new Thickness(0,4,0,4) };
    private string choiceSearchQuery="";
    private sealed class ChoiceItem : INotifyPropertyChanged
    {
        public Guid Id { get; init; }
        public string Label { get; init; } = "";
        public string SourceName { get; init; } = "";
        public string? SafeFullName { get; init; }
        public string FullDisplayName => SafeFullName ?? SourceName;
        private bool selected;
        public bool IsSelected { get=>selected; set { if(selected==value)return;selected=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(IsSelected))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private bool updatingChoices;
    private bool preparing;
    private CancellationTokenSource? prepareCancellation;
    internal Action? BeforePrepareForVerification;
    internal Action? AfterPrepareForVerification;
    internal Action? AfterHpglReviewPreparationForVerification;
    internal Func<HpglPreparedOutput,IReadOnlyList<PatternPiece>,bool>? ConfirmHpglReviewForVerification;
    private readonly List<Button> choiceActions=[];
    private readonly TextBlock selectionSummary=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,6) };
    private readonly TextBlock footerSelection=new() { TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,0,4,4) };
    private readonly OutputPreview geometryPreview=new();
    private readonly TextBlock previewSummary=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,10),Foreground=StudioTheme.Muted };
    internal IReadOnlyList<Guid> SelectedPieceIds=>partChoices.Where(c=>c.IsSelected).Select(c=>c.Id).ToArray();
    private readonly TextBox profileName=new() { Text="Yerel dosya profili" };
    private readonly TextBox units=new() { Text="40" },scale=new() { Text="1" };
    private readonly ComboBox format=new() { ItemsSource=new[] { "DXF · mm","HPGL / PLT" },SelectedIndex=0 };
    private readonly DataGrid jobs=new() { IsReadOnly=true,AutoGenerateColumns=false,SelectionMode=DataGridSelectionMode.Single,CanUserAddRows=false,Height=210 };
    private readonly TextBox details=new() { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(12) };
    private readonly TextBox filePreview=new() { IsReadOnly=true,AcceptsReturn=true,FontFamily=new FontFamily("Consolas"),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(12) };
    private string? selectedFileReadError;
    private readonly Button prepare=new() { Content="Projeden dosya hazırla" },simulate=new() { Content="Seçili dosyayı doğrula",IsEnabled=false };
    private readonly Button cancelPrepare=new() { Content="Hazırlamayı iptal et",Visibility=Visibility.Collapsed };
    private readonly CheckBox calibrationOrientation=new() { Content="Yön işareti ekle",Margin=new Thickness(0,4,0,4) };
    private readonly Button calibrationSquare=new() { Content="100 mm referans PLT dosyası oluştur" };
    private readonly TextBlock result=new() { TextWrapping=TextWrapping.Wrap,Text="Bir profil belirleyip geçerli projeden yerel dosya oluşturun." };
    private readonly TextBlock inputHint=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0) };
    private readonly ScrollViewer messagesViewer=new();
    public MachineCenterWindow(MainWindow studio,SimulationQueue queue)
    {
        this.studio=studio;this.queue=queue;openingSnapshot=studio.Document.Clone();Owner=studio;
        Title="Makine merkezi · Mono Di Tree CAD";Width=1060;Height=840;MinWidth=720;MinHeight=540;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(26) };Content=root;
        var footer=new Border { Background=StudioTheme.Surface,BorderBrush=StudioTheme.Border,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Padding=new Thickness(14),Margin=new Thickness(0,16,0,0) };
        result.Foreground=StudioTheme.Muted;
        var footerContent=new DockPanel();footer.Child=footerContent;
        StyleButton(prepare);prepare.SetResourceReference(StyleProperty,"Studio.PrimaryButton");prepare.VerticalAlignment=VerticalAlignment.Center;
        DockPanel.SetDock(prepare,Dock.Right);footerContent.Children.Add(prepare);
        StyleButton(cancelPrepare);DockPanel.SetDock(cancelPrepare,Dock.Right);footerContent.Children.Add(cancelPrepare);
        cancelPrepare.Click+=(_,_)=> { prepareCancellation?.Cancel();cancelPrepare.IsEnabled=false;result.Foreground=StudioTheme.Warning;result.Text="İptal istendi; işlem güvenli durma noktasını bekliyor."; };
        var footerMessages=new StackPanel();footerMessages.Children.Add(result);inputHint.Foreground=StudioTheme.Warning;footerMessages.Children.Add(inputHint);
        AutomationProperties.SetName(result,"Dosya hazırlama sonucu");
        AutomationProperties.SetLiveSetting(result,AutomationLiveSetting.Polite);
        AutomationProperties.SetName(inputHint,"Dosya hazırlama giriş durumu");
        AutomationProperties.SetLiveSetting(inputHint,AutomationLiveSetting.Polite);
        AnnounceTextChanges(result,"Dosya hazırlama sonucu");
        AnnounceTextChanges(inputHint,"Dosya hazırlama giriş durumu");
        // Long validation errors can contain user-supplied piece names. Keep the
        // result reachable without letting it consume the selection workspace.
        messagesViewer.Content=footerMessages;messagesViewer.MaxHeight=80;
        messagesViewer.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;messagesViewer.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        messagesViewer.Focusable=true;messagesViewer.VerticalAlignment=VerticalAlignment.Center;
        KeyboardNavigation.SetIsTabStop(messagesViewer,true);
        AutomationProperties.SetName(messagesViewer,"Dosya hazırlama mesajları");
        AutomationProperties.SetHelpText(messagesViewer,"Uzun sonuçları ve giriş uyarılarını yön tuşlarıyla kaydırın.");
        var footerMessageArea=new DockPanel();
        AutomationProperties.SetName(footerSelection,"Dosyaya alınacak seçili parçalar");
        AutomationProperties.SetLiveSetting(footerSelection,AutomationLiveSetting.Polite);
        DockPanel.SetDock(footerSelection,Dock.Top);footerMessageArea.Children.Add(footerSelection);
        footerMessageArea.Children.Add(messagesViewer);footerContent.Children.Add(footerMessageArea);
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };root.Children.Add(scroll);
        var body=new StackPanel();scroll.Content=body;
        body.Children.Add(new TextBlock { Text="ÜRETİM HAZIRLIĞI",FontSize=11,FontWeight=FontWeights.SemiBold,Foreground=StudioTheme.Accent });
        body.Children.Add(new TextBlock { Text="Dosya hazırlama (simülasyon)",FontSize=28,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,6,0,6) });
        body.Children.Add(new TextBlock { Text="Yerel dosya hazırlama ve bütünlük kontrolü",Foreground=StudioTheme.Muted,Margin=new Thickness(0,0,0,18) });
        var notice=new TextBlock { Text="SİMÜLATÖR  ·  Fiziksel bağlantı kurulmaz. Makine uyumluluğu doğrulanmadı. Tuvalde Y aşağı artar; dosyadaki koordinatları Y yukarı gösteren hedefte görünüm dikey ters olabilir. Yönü hedefte kontrol edin.",Foreground=StudioTheme.Warning,TextWrapping=TextWrapping.Wrap };
        body.Children.Add(Card(notice));
        if(openingSnapshot.NestingSnapshot is not null)
        {
            var historyNotice=new TextBlock { Text="Pastal geçmişindeki kumaş ölçüleri, kenar payı ve kaynak–kopya eşleşmesi hazırlanan DXF/HPGL dosyasına aktarılmaz. Yalnız seçili parçaların geometrisi, her parça bir kez hazırlanır. Geçmiş bilgiler üretim veya makine uygunluğu onayı değildir.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning };
            System.Windows.Automation.AutomationProperties.SetName(historyNotice,"Pastal geçmişi çıktı kapsamı uyarısı");
            body.Children.Add(Card(historyNotice));
        }
        if(openingSnapshot.PhotoTraceHistory is { Count:>0 })
        {
            var photoNotice=new TextBlock { Text="Fotoğraf taslağı geçmişi hazırlanan DXF/HPGL dosyasına aktarılmaz; kaynak .tcad projesini saklayın. Geçmiş, çizimin veya üretimin doğruluğunu onaylamaz.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning };
            AutomationProperties.SetName(photoNotice,"Fotoğraf taslağı çıktı kapsamı uyarısı");body.Children.Add(Card(photoNotice));
        }
        if(queue.RecoveryWarnings.Count>0)
            body.Children.Add(Card(new Expander { Header=$"Kuyruk açılış notları ({queue.RecoveryWarnings.Count})",IsExpanded=true,Foreground=StudioTheme.Warning,
                Content=new TextBox { Text=string.Join("\n",queue.RecoveryWarnings),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=150,VerticalScrollBarVisibility=ScrollBarVisibility.Auto } }));
        var settings=new StackPanel();
        settings.Children.Add(new TextBlock { Text="01  Dosya profili",FontSize=17,FontWeight=FontWeights.SemiBold });
        settings.Children.Add(new TextBlock { Text=$"Kaynak: {studio.Document.Name}  ·  {studio.Document.Pieces.Count} parça  ·  {studio.Document.Pieces.Sum(p=>p.Points.Count)} nokta",Foreground=StudioTheme.Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,16) });
        var fields=new WrapPanel();settings.Children.Add(fields);
        Field(fields,"Profil adı",profileName,230);Field(fields,"Dosya biçimi",format,165);Field(fields,"Ölçek çarpanı",scale,115);Field(fields,"HPGL birim/mm",units,125);
        units.ToolTip="40 yalnızca örnek değerdir; fiziksel cihaz kalibrasyonu doğrulanmadı. Nokta veya virgül ondalıktır.";
        var scaleCheck=new StackPanel { Margin=new Thickness(0,4,0,12) };
        scaleCheck.Children.Add(new TextBlock { Text="Ölçek kontrolü · isteğe bağlı",FontWeight=FontWeights.SemiBold });
        scaleCheck.Children.Add(new TextBlock { Text="HPGL biçimini seçerek 100 × 100 mm referans dosyası oluşturabilirsiniz; isteğe bağlı üçgen yön incelemesi içindir; ölçek 1 olmalı ve birim/mm değeri cihaz belgelerine göre seçilmelidir. Dosya makineye gönderilmez. Basılmış referansın X ve Y kenarlarını elle ölçüp aşağıya girin.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Muted,Margin=new Thickness(0,4,0,8) });
        AutomationProperties.SetHelpText(calibrationOrientation,"Kareye ayrı üçgen ekler. Dik köşeden uzun kenar pozitif X, kısa kenar pozitif Y yönüdür. İşaret ve çıktı cihazı doğrulamaz.");
        scaleCheck.Children.Add(calibrationOrientation);
        StyleButton(calibrationSquare);AutomationProperties.SetName(calibrationSquare,"100 mm referans HPGL dosyası oluştur");
        calibrationSquare.Click+=(_,_)=>Execute(CreateCalibrationSquare);scaleCheck.Children.Add(calibrationSquare);
        var measuredX=new TextBox { MaxLength=32 };var measuredY=new TextBox { MaxLength=32 };var measuredFields=new WrapPanel();
        Field(measuredFields,"Ölçülen X · mm",measuredX,150);Field(measuredFields,"Ölçülen Y · mm",measuredY,150);scaleCheck.Children.Add(measuredFields);
        var scaleCheckResult=new TextBlock { Text="İki ölçüyü de girin. Bu hesap cihaz doğrulaması değildir.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning };
        AutomationProperties.SetName(scaleCheckResult,"Ölçek kontrolü sonucu");AutomationProperties.SetLiveSetting(scaleCheckResult,AutomationLiveSetting.Polite);AnnounceTextChanges(scaleCheckResult,"Ölçek kontrolü sonucu");
        void UpdateScaleCheck()
        {
            if(!TryNumber(measuredX,out var x)||!TryNumber(measuredY,out var y)||x<=0||x>1000||y<=0||y>1000)
            { scaleCheckResult.Text="X ve Y için 0'dan büyük, en fazla 1000 mm ölçü girin. Bu hesap cihaz doğrulaması değildir.";return; }
            var comparison=PlotterScaleCheck.Compare(x,y);
            static string Deviation(double value)=>(value>0 ? "+" : "")+value.ToString("G9",CultureInfo.InvariantCulture);
            scaleCheckResult.Text=$"100 mm referansa göre yaklaşık sapma: X {Deviation(comparison.XDeviationMillimeters)} mm, Y {Deviation(comparison.YDeviationMillimeters)} mm. Toleransı ve kalibrasyonu cihaz sorumlusu doğrulamalıdır; ayarlar otomatik değiştirilmez.";
        }
        measuredX.TextChanged+=(_,_)=>UpdateScaleCheck();measuredY.TextChanged+=(_,_)=>UpdateScaleCheck();scaleCheck.Children.Add(scaleCheckResult);settings.Children.Add(scaleCheck);
        settings.Children.Add(new TextBlock { Text="Üretilecek parçalar",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,8,0,4) });
        settings.Children.Add(new TextBlock { Text="Yalnız işaretli parçalar dosyaya alınır. Kaynak kalıp ve türetilen bedenleri kontrol ederek seçin; adetler bu sürümde her seçili parça için 1'dir.",Foreground=StudioTheme.Muted,TextWrapping=TextWrapping.Wrap });
        var searchRow=new WrapPanel { Margin=new Thickness(0,4,0,2) };
        AutomationProperties.SetName(choiceSearch,"Üretilecek parçalarda ad veya kimlikle ara");
        AutomationProperties.SetHelpText(choiceSearch,"Adın veya kimliğin bir bölümünü girin. Arama, işaretli parçaları değiştirmez.");
        AutomationProperties.SetName(clearChoiceSearch,"Üretilecek parça aramasını temizle");
        clearChoiceSearch.Click+=(_,_)=>choiceSearch.Clear();
        searchRow.Children.Add(choiceSearch);searchRow.Children.Add(clearChoiceSearch);settings.Children.Add(searchRow);
        AutomationProperties.SetLiveSetting(choiceSearchStatus,AutomationLiveSetting.Polite);
        AutomationProperties.SetName(choiceSearchStatus,"Üretilecek parça arama sonucu");
        settings.Children.Add(choiceSearchStatus);
        settings.Children.Add(new TextBlock { Text="Toplu seçim ve temizleme arama dışındaki parçaları da kapsar.",Foreground=StudioTheme.Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,2) });
        var selectionActions=new WrapPanel { Margin=new Thickness(0,6,0,2) };
        foreach(var (label,value) in new[] { ("Tüm parçaları seç",true),("Tüm seçimleri temizle",false) })
        {
            var button=new Button { Content=label,Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,8,0) };
            choiceActions.Add(button);
            button.Click+=(_,_)=> { updatingChoices=true;try { foreach(var choice in partChoices)choice.IsSelected=value; } finally { updatingChoices=false;UpdateAvailability(); } };
            selectionActions.Children.Add(button);
        }
        settings.Children.Add(selectionActions);
        foreach(var piece in studio.Document.Pieces)
        {
            var bounds=piece.Points.Count>0 ? TekstilCAD.Core.Geometry.Bounds(piece.Points) : default;
            var dimensions=piece.Points.Count>0 ? $"{MeasurementInput.Format(bounds.Width)} × {MeasurementInput.Format(bounds.Height)} mm" : "ölçü yok";
            var label=$"{SafeChoiceName(piece.Name)}\nKimlik: {piece.Id:D}\nEn × boy: {dimensions}";
            var fullSafeName=NeedsSafeChoiceName(piece.Name) ? SafeChoiceName(piece.Name,int.MaxValue) : null;
            var choice=new ChoiceItem { Label=label,SourceName=piece.Name,SafeFullName=fullSafeName,Id=piece.Id,IsSelected=piece.Id==studio.SelectedId };
            choice.PropertyChanged+=(_,_)=> { if(!updatingChoices)UpdateAvailability(); };partChoices.Add(choice);
        }
        choiceList.ItemsSource=partChoices;choiceList.Items.Filter=MatchesChoiceSearch;choiceList.MaxHeight=120;choiceList.BorderThickness=new Thickness(0);choiceList.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        choiceSearch.TextChanged+=(_,_)=>RefreshChoiceSearch();
        VirtualizingPanel.SetIsVirtualizing(choiceList,true);VirtualizingPanel.SetVirtualizationMode(choiceList,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(choiceList,true);
        ScrollViewer.SetHorizontalScrollBarVisibility(choiceList,ScrollBarVisibility.Disabled);
        choiceList.SelectionChanged+=(_,_)=> { if(choiceList.SelectedIndex>=0)choiceList.SelectedIndex=-1; };
        var checkbox=new FrameworkElementFactory(typeof(CheckBox));
        checkbox.SetValue(FrameworkElement.MarginProperty,new Thickness(3,5,3,5));
        checkbox.SetValue(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch);
        var choiceText=new FrameworkElementFactory(typeof(TextBlock));
        choiceText.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);
        choiceText.SetBinding(TextBlock.TextProperty,new Binding(nameof(ChoiceItem.Label)));
        checkbox.AppendChild(choiceText);
        checkbox.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,new Binding(nameof(ChoiceItem.IsSelected)) { Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged });
        checkbox.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty,new Binding(nameof(ChoiceItem.Label)) { StringFormat="Üretime dahil et: {0}" });
        checkbox.SetBinding(System.Windows.Automation.AutomationProperties.HelpTextProperty,new Binding(nameof(ChoiceItem.FullDisplayName)) { StringFormat="Tam parça adı: {0}" });
        checkbox.SetBinding(FrameworkElement.ToolTipProperty,new Binding(nameof(ChoiceItem.FullDisplayName)) { StringFormat="Tam parça adı: {0}" });
        choiceList.ItemTemplate=new DataTemplate { VisualTree=checkbox };
        System.Windows.Automation.AutomationProperties.SetName(choiceList,"Üretilecek parça seçimi");settings.Children.Add(choiceList);
        settings.Children.Add(selectionSummary);
        StyleButton(simulate);
        prepare.Click+=async (_,_)=>await Prepare();simulate.Click+=(_,_)=>Execute(Simulate);
        profileName.TextChanged+=(_,_)=>UpdateAvailability();units.TextChanged+=(_,_)=>UpdateAvailability();scale.TextChanged+=(_,_)=>UpdateAvailability();format.SelectionChanged+=(_,_)=>UpdateAvailability();
        body.Children.Add(Card(settings));
        var queuePanel=new StackPanel();queuePanel.Children.Add(new TextBlock { Text="02  Kayıtlı dosya kuyruğu",FontSize=17,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6) });
        queuePanel.Children.Add(new TextBlock { Text="Geçerli işlem kayıtları yeniden açılışta yüklenir. Önceki doğrulama güncel dosya kontrolü değildir; kullanmadan önce yeniden doğrulayın.",Foreground=StudioTheme.Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12) });
        jobs.Columns.Add(new DataGridTextColumn { Header="Proje",Binding=new Binding("ProjectName"),Width=new DataGridLength(1,DataGridLengthUnitType.Star) });
        jobs.Columns.Add(new DataGridTextColumn { Header="Biçim",Binding=new Binding("Format"),Width=75 });
        jobs.Columns.Add(new DataGridTextColumn { Header="Durum",Binding=new Binding("Status") { Converter=new StatusLabel() },Width=160 });
        jobs.Columns.Add(new DataGridTextColumn { Header="Parça",Binding=new Binding("PieceCount"),Width=65 });
        jobs.Columns.Add(new DataGridTextColumn { Header="Nokta",Binding=new Binding("PointCount"),Width=70 });
        AutomationProperties.SetName(jobs,"Kayıtlı dosya kuyruğu");
        queuePanel.Children.Add(jobs);body.Children.Add(Card(queuePanel));
        var preview=new StackPanel();preview.Children.Add(new TextBlock { Text="03  Hazırlanan dosyayı kontrol edin",FontSize=17,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6) });
        preview.Children.Add(previewSummary);
        var previewGrid=new Grid { Height=280 };previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(0.8,GridUnitType.Star) });previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1.2,GridUnitType.Star) });
        AutomationProperties.SetName(details,"Seçili dosya ayrıntıları");
        AutomationProperties.SetName(filePreview,"Seçili dosya metni önizlemesi");
        details.Margin=new Thickness(0,0,10,0);previewGrid.Children.Add(details);
        var tabs=new TabControl();tabs.Items.Add(new TabItem { Header="Çıktı geometrisi",Content=geometryPreview });tabs.Items.Add(new TabItem { Header="Dosya metni",Content=filePreview });Grid.SetColumn(tabs,1);previewGrid.Children.Add(tabs);
        preview.Children.Add(previewGrid);simulate.Margin=new Thickness(0,12,0,0);simulate.HorizontalAlignment=HorizontalAlignment.Left;preview.Children.Add(simulate);body.Children.Add(Card(preview));
        jobs.SelectionChanged+=(_,_)=>ShowSelection();
        Closing+=(_,e)=> { if(preparing) { e.Cancel=true;result.Foreground=StudioTheme.Warning;result.Text="Dosya hazırlanıyor; işlem bitince pencereyi kapatabilirsiniz."; } };
        Refresh(null);UpdateAvailability();
    }
    private static bool NeedsSafeChoiceName(string value)
    {
        if(value.Length==0)return true;
        for(var i=0;i<value.Length;i++)
        {
            var c=value[i];
            var pair=char.IsHighSurrogate(c) && i+1<value.Length && char.IsLowSurrogate(value[i+1]);
            var category=pair ? CharUnicodeInfo.GetUnicodeCategory(value,i) : char.GetUnicodeCategory(c);
            if(char.IsSurrogate(c) && !pair || category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)return true;
            if(pair)i++;
        }
        return false;
    }
    private static string SafeChoiceName(string value,int maxLength=80)
    {
        if(value.Length==0)return "(boş)";
        var result=new StringBuilder(Math.Min(value.Length,maxLength));
        for(var i=0;i<value.Length;)
        {
            var c=value[i];
            var pair=char.IsHighSurrogate(c) && i+1<value.Length && char.IsLowSurrogate(value[i+1]);
            var count=pair?2:1;
            var category=pair ? CharUnicodeInfo.GetUnicodeCategory(value,i) : char.GetUnicodeCategory(c);
            var escape=char.IsSurrogate(c) && !pair || category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
            var token=escape ? $"\\u{(int)c:X4}"+(pair ? $"\\u{(int)value[i+1]:X4}" : "") : value.Substring(i,count);
            if(result.Length+token.Length>maxLength || (i+count<value.Length && result.Length+token.Length==maxLength))
            { result.Append('…');return result.ToString(); }
            result.Append(token);i+=count;
        }
        return result.ToString();
    }
    private bool MatchesChoiceSearch(object item)
    {
        if(item is not ChoiceItem choice)return false;
        var query=choiceSearchQuery;
        if(query.Length==0)return true;
        var comparer=CultureInfo.GetCultureInfo("tr-TR").CompareInfo;
        return comparer.IndexOf(choice.SourceName,query,CompareOptions.IgnoreCase)>=0 ||
            choice.Id.ToString("D").Contains(query,StringComparison.OrdinalIgnoreCase) ||
            (choice.SafeFullName is not null && comparer.IndexOf(choice.SafeFullName,query,CompareOptions.IgnoreCase)>=0);
    }
    private void RefreshChoiceSearch()
    {
        choiceSearchQuery=choiceSearch.Text.Trim();
        if(choiceList.ItemsSource is null)return;
        CollectionViewSource.GetDefaultView(choiceList.ItemsSource).Refresh();
        UpdateChoiceSearchStatus();
    }
    private void UpdateChoiceSearchStatus()
    {
        var visible=choiceList.Items.Count;
        var selected=partChoices.Count(choice=>choice.IsSelected);
        var hiddenSelected=partChoices.Count(choice=>choice.IsSelected && !MatchesChoiceSearch(choice));
        var footerText=hiddenSelected>0
            ? $"Seçili: {selected} · Arama dışında: {hiddenSelected} (dosyaya dahil)."
            : $"Seçili: {selected} parça · Her biri 1 adet.";
        if(footerSelection.Text!=footerText)
        {
            footerSelection.Text=footerText;
            AutomationProperties.SetName(footerSelection,"Dosyaya alınacak seçili parçalar: "+footerText);
            if(System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(footerSelection) is { } footerPeer)
                footerPeer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
        var message=(visible==0 && choiceSearchQuery.Length>0 ? "Eşleşen parça yok." : $"{visible} / {partChoices.Count} parça gösteriliyor.")+
            (hiddenSelected>0 ? $" Arama dışında {hiddenSelected} işaretli parça var; onlar da dosyaya alınır." : "");
        if(choiceSearchStatus.Text==message)return;
        choiceSearchStatus.Text=message;
        AutomationProperties.SetName(choiceSearchStatus,"Üretilecek parça arama sonucu: "+message);
        if(System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(choiceSearchStatus) is { } peer)
            peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }
    private static Border Card(UIElement child)=>new() { Child=child,Padding=new Thickness(18),Margin=new Thickness(0,0,0,14),Background=StudioTheme.Surface,BorderBrush=StudioTheme.Border,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10) };
    private static void AnnounceTextChanges(TextBlock text,string name)
    {
        DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty,typeof(TextBlock)).AddValueChanged(text,(_,_)=>
        {
            AutomationProperties.SetName(text,name+": "+text.Text);
            if(System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(text) is { } peer)
                peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        });
    }
    private static void Field(Panel parent,string label,Control input,double width)
    { var column=new StackPanel { Width=width,Margin=new Thickness(0,0,12,10) };column.Children.Add(new TextBlock { Text=label,Foreground=StudioTheme.Muted,Margin=new Thickness(0,0,0,6) });input.Padding=new Thickness(9);System.Windows.Automation.AutomationProperties.SetName(input,label);if(label is "Ölçek çarpanı" or "HPGL birim/mm")input.ToolTip="Nokta veya virgül ondalıktır; binlik ayırıcı kullanmayın.";column.Children.Add(input);parent.Children.Add(column); }
    private static void StyleButton(Button button) { button.Padding=new Thickness(15,10,15,10);button.Margin=new Thickness(0,0,10,0); }
    private static bool TryNumber(TextBox input,out double value)=>MeasurementInput.TryParseMillimeters(input.Text,out value);
    private static double Number(TextBox input)=>TryNumber(input,out var value)?value:throw new ArgumentException("Geçerli bir sayısal değer girin.");
    private void UpdateAvailability()
    {
        units.IsEnabled=!preparing && format.SelectedIndex==1;
        calibrationSquare.IsEnabled=!preparing && format.SelectedIndex==1;calibrationOrientation.IsEnabled=!preparing && format.SelectedIndex==1;
        var selected=SelectedPieceIds.ToHashSet();var existing=studio.Document.Pieces.Where(p=>selected.Contains(p.Id)).ToArray();
        selectionSummary.Text=$"Seçili üretim kümesi: {existing.Length} parça · {existing.Sum(p=>p.Points.Count)} nokta · Her parça 1 adet";
        UpdateChoiceSearchStatus();
        string? error=!DocumentSession.ContentEquals(openingSnapshot,studio.Document)?"Kaynak proje değişti; güncel parçaları görmek için pencereyi kapatıp yeniden açın.":existing.Length==0?"Dosya hazırlamak için üretilecek parçaları işaretleyin.":existing.Length!=selected.Count?"Seçili parça artık projede yok; pencereyi kapatıp yeniden açın.":null;
        if(error is null&&(string.IsNullOrWhiteSpace(profileName.Text)||profileName.Text.Trim().Length>200))error="Profil adı 1–200 karakter olmalıdır.";
        if(error is null&&(!TryNumber(scale,out var s)||s<=0||s>1000))error="Ölçek çarpanı 0'dan büyük, en fazla 1000 olmalıdır.";
        if(error is null&&format.SelectedIndex==1&&(!TryNumber(units,out var u)||u<=0||u>10000))error="HPGL birim/mm değeri 0'dan büyük, en fazla 10000 olmalıdır.";
        inputHint.Text=error??((format.SelectedIndex==0?"DXF milimetre biriminde hazırlanır. HPGL kalibrasyonu bu biçimde kullanılmaz.":"HPGL değeri yalnızca dosya dönüşümünü belirler; fiziksel kalibrasyon yerine geçmez.")+" Nokta veya virgül ondalıktır; binlik ayırıcı kullanmayın.");
        inputHint.Foreground=error is null?StudioTheme.Muted:StudioTheme.Warning;prepare.IsEnabled=!preparing && error is null;
    }
    private void Execute(Action action)
    { try { action(); } catch(Exception ex) { result.Foreground=StudioTheme.Danger;result.Text="İşlem başarısız: "+ex.Message; } }
    private void CreateCalibrationSquare()
    {
        if(preparing || format.SelectedIndex!=1)throw new InvalidOperationException("Önce HPGL / PLT biçimini seçin.");
        var unitValue=Number(units);var scaleValue=Number(scale);
        var withOrientation=calibrationOrientation.IsChecked==true;
        var reference=withOrientation ? PlotterCalibrationSquare.PrepareWithOrientationMarker(unitValue,scaleValue) : PlotterCalibrationSquare.Prepare(unitValue,scaleValue);
        var description=withOrientation ? "İçte yön üçgeni vardır: dik köşeden 50 mm uzun kenar +X, 40 mm kısa kenar +Y yönündedir. Izgara yuvarlamasını ve cihaz başlangıcını ayrıca kontrol edin." : "İçinde yön işareti yoktur; kare eksen yönünü tek başına göstermez.";
        var confirmation=MessageBox.Show(this,$"100 × 100 mm referans dosyası hazırlanacak.\n{description}\nBirim/mm: {unitValue:G17}\nÖlçek: {scaleValue:G17}\n\nBu değerlerin cihazınıza uygunluğu ve çıktı ölçüsü doğrulanmış değildir. Makineye veri gönderilmez.","Referans dosyasını kontrol edin",MessageBoxButton.OKCancel,MessageBoxImage.Information);
        if(confirmation!=MessageBoxResult.OK)return;
        var dialog=new Microsoft.Win32.SaveFileDialog { Title="Yeni 100 mm referans HPGL dosyası",Filter="HPGL / PLT (*.plt)|*.plt|HPGL (*.hpgl)|*.hpgl",FileName="referans-100mm.plt",AddExtension=true,OverwritePrompt=false,InitialDirectory=AppPaths.PrepareProjectsDirectory() };
        if(dialog.ShowDialog(this)!=true)return;
        PlotterCalibrationSquare.WriteNew(reference,dialog.FileName);
        result.Foreground=StudioTheme.Accent;result.Text="100 mm referans dosyası oluşturuldu: "+dialog.FileName+". Makineye gönderilmedi; cihaz ve fiziksel ölçü doğrulanmadı.";
    }
    private async Task Prepare()
    {
        if(preparing)return;
        try
        {
            var selected=SelectedPieceIds.ToHashSet();
            if(selected.Count==0)throw new InvalidOperationException("Önce üretilecek parçaları işaretleyin.");
            if(!DocumentSession.ContentEquals(openingSnapshot,studio.Document))throw new InvalidOperationException("Kaynak proje değişti; pencereyi kapatıp yeniden açın.");
            if(studio.HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("Önce çizimi ve alan taslaklarını tamamlayın veya iptal edin.");
            var originalDocument=studio.Document;var originalSelection=studio.SelectedId;
            var snapshot=studio.Document.Clone();
            var subset=new ProjectDocument { Name=snapshot.Name,Pieces=snapshot.Pieces.Where(p=>selected.Contains(p.Id)).Select(p=>p.Clone()).ToList() };
            if(subset.Pieces.Count!=selected.Count)throw new InvalidOperationException("Seçili parça artık projede yok; kaynak listesini yeniden açın.");
            var profile=new MachineProfile(profileName.Text.Trim(),format.SelectedIndex==1?Number(units):40,Number(scale));
            var jobFormat=format.SelectedIndex==0?JobFormat.Dxf:JobFormat.Hpgl;
            var originalProfileName=profileName.Text;var originalUnits=units.Text;var originalScale=scale.Text;var originalFormat=format.SelectedIndex;
            bool ReviewIsCurrent() => ReferenceEquals(originalDocument,studio.Document)
                && DocumentSession.ContentEquals(snapshot,studio.Document) && !studio.HasPendingCanvasOrPropertyWork
                && originalSelection==studio.SelectedId && selected.SetEquals(SelectedPieceIds)
                && profileName.Text==originalProfileName && units.Text==originalUnits && scale.Text==originalScale && format.SelectedIndex==originalFormat;
            prepareCancellation=jobFormat==JobFormat.Hpgl?new CancellationTokenSource():null;
            preparing=true; SetPreparing(true);
            result.Foreground=StudioTheme.Muted;result.Text="Dosya hazırlanıyor; fiziksel cihaza veri gönderilmez.";
            SimulationJob job;
            if(jobFormat==JobFormat.Hpgl)
            {
                var token=prepareCancellation!.Token;
                var proposal=await Task.Run(()=>
                {
                    BeforePrepareForVerification?.Invoke();
                    var value=queue.PrepareHpglReview(subset,profile,token);
                    AfterHpglReviewPreparationForVerification?.Invoke();
                    return value;
                });
                token.ThrowIfCancellationRequested();
                if(!ReviewIsCurrent())throw new InvalidOperationException("Proje, seçim veya profil hazırlık sırasında değişti; iş oluşturulmadı. Yeniden deneyin.");
                var approved=ConfirmHpglReviewForVerification is { } review
                    ? review(proposal.Output,subset.Pieces)
                    : new HpglReviewWindow(this,proposal.Output,subset.Pieces,profile.Scale).ShowDialog()==true;
                token.ThrowIfCancellationRequested();
                if(!approved)
                {
                    result.Foreground=StudioTheme.Warning;result.Text="HPGL incelemesinden vazgeçildi; kuyruk işi oluşturulmadı.";return;
                }
                if(!ReviewIsCurrent())throw new InvalidOperationException("Proje, seçim veya profil inceleme sırasında değişti; iş oluşturulmadı. Yeniden deneyin.");
                token.ThrowIfCancellationRequested();
                // Keep the approved controls stable while the file/manifest commit runs.
                // The worker will finish a manifest after a file has been committed.
                var previousEnabled=IsEnabled;IsEnabled=false;
                result.Foreground=StudioTheme.Muted;result.Text="HPGL dosyası ve iş kaydı oluşturuluyor; lütfen bekleyin.";
                try
                {
                    job=await Task.Run(()=>
                    {
                        var value=queue.CommitPreparedHpgl(proposal,subset,profile,sourceSnapshotUnconfirmed:true,cancellationToken:token);
                        AfterPrepareForVerification?.Invoke();
                        return value;
                    });
                }
                finally { IsEnabled=previousEnabled; }
            }
            else
            {
                job=await Task.Run(()=>
                {
                    BeforePrepareForVerification?.Invoke();
                    var value=queue.Prepare(subset,profile,jobFormat,sourceSnapshotUnconfirmed:true);
                    AfterPrepareForVerification?.Invoke();
                    return value;
                });
            }
            // The prepared file represents the captured document, selection and
            // profile. Pending edits and changed controls matter for DXF too:
            // equal committed geometry alone does not confirm that snapshot.
            if(ReviewIsCurrent())
            {
                try { job=queue.ConfirmSourceSnapshot(job.Id); }
                catch(Exception ex)
                {
                    // Preparation has already committed a local file and job. Keep
                    // its unconfirmed state visible when the separate manifest
                    // update fails; never silently repeat that write.
                    Refresh(job.Id);
                    result.Foreground=StudioTheme.Warning;
                    result.Text="İş listede görünüyor; kaynak proje doğrulaması kaydedilemedi: "+ex.Message+
                        " Kayıt ve çıktı dosyasının diskteki durumu doğrulanamadı. Kaynak doğrulanmadı; kuyruğu yeniden açın veya güncel proje için dosyayı yeniden hazırlayın."+
                        (selectedFileReadError is null ? "" : " Çıktı önizlemesi okunamadı: "+selectedFileReadError)+
                        " Fiziksel makine testi yapılmadı.";
                    return;
                }
            }
            Refresh(job.Id);
            if(selectedFileReadError is { } readError)
            {
                result.Foreground=StudioTheme.Warning;
                result.Text="İş listede görünüyor ancak çıktı önizlemesi okunamadı: "+readError+
                    " Dosyanın yerini ve durumunu kontrol edin veya güncel proje için yeniden hazırlayın."+
                    (job.SourceSnapshotUnconfirmed ? " Kaynak proje ve seçim doğrulanmadı; güncel durum için yeniden hazırlayın." : "")+
                    " Fiziksel makine testi yapılmadı.";
                if(prepareCancellation?.IsCancellationRequested==true)result.Text+=" İptal isteği işlem tamamlandıktan sonra alındı.";
                return;
            }
            if(!ReviewIsCurrent())
            {
                result.Foreground=StudioTheme.Warning;
                result.Text="Proje, bekleyen çalışma, seçim veya profil hazırlama sırasında değişti. Dosya önceki seçim ve proje kopyasından hazırlandı. " +
                    (job.SourceSnapshotUnconfirmed ? "Kaynak doğrulanmadı. " : "Güncel durum önceki doğrulamayla aynı değil. ") +
                    "Güncel durum için yeniden hazırlayın.";
            }
            else
            { result.Foreground=StudioTheme.Accent;result.Text=$"{subset.Pieces.Count} seçili parça hazırlandı. Çıktı ölçülerini ve geometrisini kontrol edin; bu işlem makineye iş göndermez."; }
            if(prepareCancellation?.IsCancellationRequested==true)
            { result.Foreground=StudioTheme.Warning;result.Text+=" İptal isteğine rağmen dosya kaydı tamamlandı; iş kuyrukta korunuyor."; }
            simulate.BringIntoView();
        }
        catch(OperationCanceledException) { result.Foreground=StudioTheme.Warning;result.Text="HPGL hazırlama iptal edildi; kuyruk işi oluşturulmadı. Oluşturulmuş olabilecek iş klasörü korunmuştur."; }
        catch(Exception ex) { result.Foreground=StudioTheme.Danger;result.Text="İşlem başarısız: "+ex.Message+" Çıktı veya iş kaydı oluşmuş olabilir; kuyruğu kontrol edin."; }
        finally { prepareCancellation?.Dispose();prepareCancellation=null;preparing=false;SetPreparing(false); }
    }
    private void SetPreparing(bool busy)
    {
        var moveFromPrepare=busy && prepare.IsKeyboardFocusWithin;
        var moveFromCancel=!busy && cancelPrepare.IsKeyboardFocusWithin;
        if(moveFromPrepare)
        {
            if(prepareCancellation is not null)
            { cancelPrepare.Visibility=Visibility.Visible;cancelPrepare.IsEnabled=true;cancelPrepare.Focus(); }
            else messagesViewer.Focus();
        }
        profileName.IsEnabled=!busy;format.IsEnabled=!busy;scale.IsEnabled=!busy;jobs.IsEnabled=!busy;
        prepare.Visibility=busy && prepareCancellation is not null?Visibility.Collapsed:Visibility.Visible;
        cancelPrepare.Visibility=busy && prepareCancellation is not null?Visibility.Visible:Visibility.Collapsed;
        cancelPrepare.IsEnabled=busy && prepareCancellation is not null;
        choiceList.IsEnabled=!busy;
        choiceSearch.IsEnabled=!busy;clearChoiceSearch.IsEnabled=!busy;
        foreach(var button in choiceActions)button.IsEnabled=!busy;
        simulate.IsEnabled=!busy && jobs.SelectedItem is SimulationJob;
        UpdateAvailability();
        if(moveFromCancel) { if(prepare.IsEnabled)prepare.Focus();else messagesViewer.Focus(); }
    }
    private void Simulate()
    {
        if(jobs.SelectedItem is not SimulationJob selected)return;
        var job=queue.Simulate(selected.Id);Refresh(job.Id);
        if(selectedFileReadError is { } readError)
        {
            result.Foreground=job.Status==JobStatus.Failed?StudioTheme.Danger:StudioTheme.Warning;
            result.Text=(job.Status==JobStatus.Failed?"Dosya kontrolü başarısız: "+job.Error+". ":"Kontrol tamamlandı. ")+"Çıktı önizlemesi okunamadı: "+readError+
                " Dosyanın yerini ve durumunu kontrol edin; gerekirse yeniden hazırlayın. Fiziksel makine testi yapılmadı.";
            return;
        }
        result.Foreground=job.Status!=JobStatus.Simulated?StudioTheme.Danger:job.SourceSnapshotUnconfirmed?StudioTheme.Warning:StudioTheme.Accent;
        result.Text=job.Status!=JobStatus.Simulated?"Doğrulama başarısız: "+job.Error:
            job.SourceSnapshotUnconfirmed?"Dosya bütünlüğü doğrulandı; ancak önceki proje kopyasından hazırlanmış olabilir. Güncel proje için dosyayı yeniden hazırlayın. Fiziksel makine testi yapılmadı.":
            "Dosya bütünlüğü doğrulandı. Fiziksel makine testi yapılmadı.";
    }
    private void Refresh(Guid? selected)
    { jobs.ItemsSource=null;jobs.ItemsSource=queue.Jobs;jobs.SelectedItem=queue.Jobs.FirstOrDefault(j=>j.Id==selected);ShowSelection(); }
    private void ShowSelection()
    {
        selectedFileReadError=null;
        simulate.IsEnabled=!preparing && jobs.SelectedItem is SimulationJob;
        geometryPreview.Clear();
        if(jobs.SelectedItem is not SimulationJob job) { details.Text="Kuyruktan bir dosya seçin. Profil, ölçü ayarları ve dosya konumu burada görünür.";filePreview.Text="Henüz dosya seçilmedi.";previewSummary.Text="Henüz çıktı seçilmedi. Önizleme, hazırlandıktan sonra gerçek dosyadan okunur.";return; }
        details.Text=$"{job.Profile.Name}\n\nÖlçek: {job.Profile.Scale}\n{(job.Format==JobFormat.Hpgl?$"HPGL birim/mm: {job.Profile.UnitsPerMillimeter}\n":"")}Durum: {StatusLabel.Label(job.Status)}\n\nDosya\n{job.OutputPath}\n\nİşlem kaydı\n{job.ManifestPath}\n\nSHA256\n{job.Sha256}\n{job.Error}";
        details.Text+="\nBu dosya hazırlanma anındaki proje kopyasına aittir; güncel projeyle eşleştiği doğrulanmaz.";
        if(job.SourceSnapshotUnconfirmed)details.Text+="\nKaynak proje hazırlama sırasında değişmiş veya kaynak kontrolü tamamlanmamış olabilir. Güncel proje için dosyayı yeniden hazırlayın.";
        try { using var reader=System.IO.File.OpenText(job.OutputPath);var buffer=new char[12000];var count=reader.ReadBlock(buffer,0,buffer.Length);filePreview.Text=new string(buffer,0,count)+(reader.EndOfStream?"":"\n… İlk 12.000 karakter gösteriliyor."); }
        catch(Exception ex) when(ex is System.IO.IOException or UnauthorizedAccessException) { selectedFileReadError=ex.Message;filePreview.Text="Önizleme okunamadı: "+ex.Message;result.Foreground=StudioTheme.Danger;result.Text="Dosya önizlemesi okunamadı: "+ex.Message; }
        try { previewSummary.Text=geometryPreview.ShowJob(job);previewSummary.Foreground=StudioTheme.Muted; }
        catch(Exception ex) when(ex is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { geometryPreview.Clear();previewSummary.Text="Görsel önizleme yok: "+ex.Message;previewSummary.Foreground=StudioTheme.Warning; }
        if(job.Status==JobStatus.Simulated)details.Text+="\nÖnceden doğrulandı. Güncel bütünlük için yeniden doğrulayın; fiziksel tamamlanma değildir.";
    }
    private sealed class StatusLabel:IValueConverter
    {
        public static string Label(JobStatus status)=>status switch { JobStatus.Prepared=>"Hazır",JobStatus.Simulated=>"Önceden doğrulandı",JobStatus.Failed=>"Başarısız",_=>status.ToString() };
        public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>value is JobStatus status?Label(status):"";
        public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
}
