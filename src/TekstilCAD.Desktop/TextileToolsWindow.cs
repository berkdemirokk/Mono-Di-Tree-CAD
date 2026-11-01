using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using TekstilCAD.Core;
using TekstilCAD.Grading;
using TekstilCAD.Nesting;
using TekstilCAD.Patterns;

namespace TekstilCAD.Desktop;

public sealed class TextileToolsWindow : Window
{
    private readonly MainWindow workspace;
    private readonly ComboBox source = new() { MinWidth=200, IsTextSearchEnabled=true, Margin=new Thickness(0,4,0,0) };
    private readonly ObservableCollection<GradeRow> rows = [];
    private readonly DataGrid grades = new() { AutoGenerateColumns=false, CanUserAddRows=false, CanUserDeleteRows=false, SelectionUnit=DataGridSelectionUnit.FullRow, SelectionMode=DataGridSelectionMode.Extended, MinHeight=80, RowHeight=34, ColumnHeaderHeight=32, Margin=new Thickness(0,6,0,8) };
    private readonly TextBlock sourceInfo = new() { TextWrapping=TextWrapping.Wrap, MaxHeight=44, TextTrimming=TextTrimming.CharacterEllipsis, VerticalAlignment=VerticalAlignment.Center };
    private readonly TextBox sourceIdentity = new() { IsReadOnly=true, TextWrapping=TextWrapping.Wrap, AcceptsReturn=true, MaxHeight=42, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, Margin=new Thickness(16,3,0,0) };
    private readonly TextBlock result = new() { TextWrapping=TextWrapping.Wrap, Text="Artışları girip yeni bedeni oluşturun veya diğer araç sekmelerini kullanın." };
    private readonly List<(Button Button,Func<bool> Available)> commands = [];
    private bool refreshingSource;
    private bool offsetBusy;
    private Action? showUnverifiedOffsetPreview;
    private readonly Button unverifiedOffsetPreview = new() { Content="Doğrulanmamış önizleme", Visibility=Visibility.Collapsed };
    // A committed result whose view could not be refreshed must be inspected
    // in the workspace before another allowance can be created in this window.
    private bool offsetReviewRequired;
    // A synchronous tool committed but its view refresh failed. Reopening the
    // workshop rebuilds its source and draft bindings from the current project.
    private bool toolReviewRequired;
    private string? synchronousCommitDescription;
    private TextBox? offsetAllowance;
    private string? offsetAllowanceDescription;
    private string? offsetSourceDescription;
    private CancellationTokenSource? offsetCancellation;
    private readonly Button cancelOffset = new() { Content="Dikiş payı hesabını iptal et", Padding=new Thickness(12,7,12,7), Margin=new Thickness(0,0,8,0), Visibility=Visibility.Collapsed };
    internal Action? BeforeOffsetForVerification { get; set; }
    internal Action? AfterOffsetBeforeReturnForVerification { get; set; }
    private bool gradeBusy;
    private bool vertexMoveBusy;
    private TextBox? vertexIndexInput, vertexXInput, vertexYInput, notchFractionInput, notchDepthInput, notchWidthInput, edgeDistanceInput, edgePercentageInput;
    private CancellationTokenSource? vertexMoveCancellation;
    private readonly Button cancelVertexMove = new() { Content="Nokta doğrulamasını iptal et", Padding=new Thickness(12,7,12,7), Margin=new Thickness(0,0,8,0), Visibility=Visibility.Collapsed };
    internal Action? BeforeVertexMoveForVerification { get; set; }
    internal Action? AfterVertexMoveForVerification { get; set; }
    internal Task? VertexMoveTaskForVerification { get; private set; }
    internal bool VertexMoveRunningForVerification => vertexMoveBusy;
    private bool basicNestingBusy;
    private CancellationTokenSource? basicNestingCancellation;
    private readonly Button cancelBasicNesting = new() { Content="Pastal hesabını iptal et", Padding=new Thickness(12,7,12,7), Margin=new Thickness(0,0,8,0), Visibility=Visibility.Collapsed };
    private TextBox basicWidth = null!, basicGap = null!, basicMargin = null!;
    private TabControl? workshopTabs;
    internal Task? BasicNestingTaskForVerification { get; private set; }
    internal bool BasicNestingRunningForVerification => basicNestingBusy;
    internal Action? BeforeBasicNestingForVerification { get; set; }
    internal Action? AfterBasicNestingBeforeReturnForVerification { get; set; }
    internal Func<bool>? ConfirmBasicNestingForVerification { get; set; }
    private CancellationTokenSource? gradeCancellation;
    private readonly Button cancelGrade = new() { Content="Beden hesabını iptal et", Padding=new Thickness(12,7,12,7), Margin=new Thickness(0,0,8,0), Visibility=Visibility.Collapsed };
    internal Action? BeforeGradeForVerification { get; set; }
    internal Action? AfterGradeBeforeReturnForVerification { get; set; }
    internal Func<bool>? ConfirmGradeResetForVerification { get; set; }
    internal Task? GradeTaskForVerification { get; private set; }
    internal bool GradeRunningForVerification => gradeBusy;
    private bool windowClosed;
    private readonly Dictionary<Guid, GradeDraft> gradeDrafts = [];
    private Guid? activeSourceId;
    private GradeDraft? activeDraft;
    private bool rulesNeedReset;
    private Button? resetRulesButton;

    private sealed record GradeDraft(List<Point2> Points, List<Point2> AnchorPoints, bool Closed, List<GradeRow> Rows, GradingRuleSet? BaselineRule, PatternPiece BaselinePiece);

    public TextileToolsWindow(MainWindow workspace)
    {
        this.workspace=workspace; Owner=workspace; Title="Tekstil araçları · Mono Di Tree CAD";
        Width=930;Height=820;MinWidth=680;MinHeight=540;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock { Text="Tekstil araçları",FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8) });
        var selector=new Grid();selector.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });selector.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        var sourceColumn=new StackPanel();sourceColumn.Children.Add(new TextBlock { Text="TEMEL / KAYNAK PARÇA",FontSize=11,Foreground=StudioTheme.Muted });
        var sourceText=new FrameworkElementFactory(typeof(TextBlock));
        sourceText.SetBinding(TextBlock.TextProperty,new Binding { Converter=new SourceLabelConverter() });
        sourceText.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
        source.ItemTemplate=new DataTemplate { VisualTree=sourceText };
        // The visual template begins with an identity; search the underlying
        // name while keeping all visible labels escaped by SourceLabelConverter.
        TextSearch.SetTextPath(source,nameof(PatternPiece.Name));
        var virtualPanel=new FrameworkElementFactory(typeof(VirtualizingStackPanel));
        source.ItemsPanel=new ItemsPanelTemplate(virtualPanel);
        VirtualizingPanel.SetIsVirtualizing(source,true);VirtualizingPanel.SetVirtualizationMode(source,VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(source,true);
        sourceColumn.Children.Add(source);selector.Children.Add(sourceColumn);
        var sourceDetails=new StackPanel();sourceInfo.Margin=new Thickness(16,0,0,0);sourceDetails.Children.Add(sourceInfo);
        AutomationProperties.SetName(sourceIdentity,"Seçili kaynağın tam adı ve kimliği");
        AutomationProperties.SetHelpText(sourceIdentity,"Tam ad ve kimliği seçip kopyalayabilirsiniz. Görünmeyen kontrol karakterleri Unicode kodu olarak gösterilir.");
        sourceDetails.Children.Add(sourceIdentity);Grid.SetColumn(sourceDetails,1);selector.Children.Add(sourceDetails);
        AutomationProperties.SetName(source,"Tekstil araçları için kaynak parça");
        AutomationProperties.SetHelpText(source,"Ölçüm, nokta düzenleme ve serileme için kullanılacak kaynak parçayı seçin. Oluşturulan beden bu seçimi değiştirmez.");
        AutomationProperties.SetName(grades,"Nokta bazlı beden artışları, milimetre");
        AutomationProperties.SetName(result,"Tekstil aracı işlem sonucu: "+result.Text);
        AutomationProperties.SetLiveSetting(result,AutomationLiveSetting.Polite);
        top.Children.Add(Card(selector,new Thickness(0,0,0,8)));
        source.SelectionChanged+=(_,_)=>
        {
            if(refreshingSource)return;
            // Keep the old source and its editing control alive if WPF cannot commit the cell.
            if(!CommitGradeEdit())
            {
                refreshingSource=true;
                try { source.SelectedItem=source.Items.Cast<PatternPiece>().FirstOrDefault(p=>p.Id==activeSourceId); }
                finally { refreshingSource=false; }
                ShowGradeEditError();
                return;
            }
            ReloadRows();
        };
        var footer=new Grid { Margin=new Thickness(0,8,0,0) };footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);result.Foreground=StudioTheme.Muted;result.VerticalAlignment=VerticalAlignment.Center;
        // Keep long results readable without allowing an error message to consume the whole workspace.
        // The viewer itself is a tab stop so keyboard users can scroll the complete message.
        var resultViewer=new ScrollViewer { Content=result,MaxHeight=64,Margin=new Thickness(0,0,12,0),
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
            Focusable=true,VerticalAlignment=VerticalAlignment.Center };
        KeyboardNavigation.SetIsTabStop(resultViewer,true);
        AutomationProperties.SetName(resultViewer,"İşlem sonucu ayrıntıları");
        AutomationProperties.SetHelpText(resultViewer,"Uzun sonuçları okumak için yön tuşlarıyla kaydırın.");
        result.SetBinding(ToolTipProperty,new Binding(nameof(TextBlock.Text)) { Source=result });footer.Children.Add(resultViewer);
        var footerActions=new WrapPanel { VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right };
        unverifiedOffsetPreview.Click+=(_,_)=>showUnverifiedOffsetPreview?.Invoke();
        AutomationProperties.SetHelpText(unverifiedOffsetPreview,"Yalnızca görüntüleme; proje değişmez ve sonuç dışa aktarılmaz.");
        footerActions.Children.Add(unverifiedOffsetPreview);
        cancelGrade.Click+=(_,_)=>RequestGradeCancellation();
        AutomationProperties.SetName(cancelGrade,"Beden hesaplamasını iptal et");
        AutomationProperties.SetHelpText(cancelGrade,"İptal isteğinden sonra işlem durana kadar bekleyin; sonuç projeye eklenmez.");
        footerActions.Children.Add(cancelGrade);
        cancelOffset.Click+=(_,_)=>RequestOffsetCancellation();
        AutomationProperties.SetName(cancelOffset,"Dikiş payı hesabını iptal et");
        AutomationProperties.SetHelpText(cancelOffset,"İptal isteğinden sonra hesaplama durana kadar bekleyin; sonuç projeye eklenmez.");
        footerActions.Children.Add(cancelOffset);
        cancelBasicNesting.Click+=(_,_)=>RequestBasicNestingCancellation();
        AutomationProperties.SetName(cancelBasicNesting,"Temel pastal hesabını iptal et");
        AutomationProperties.SetHelpText(cancelBasicNesting,"İptal isteğinden sonra hesap durana kadar bekleyin; sonuç projeye uygulanmaz.");
        footerActions.Children.Add(cancelBasicNesting);
        cancelVertexMove.Click+=(_,_)=>RequestVertexMoveCancellation();
        AutomationProperties.SetName(cancelVertexMove,"Nokta düzenleme doğrulamasını iptal et");
        AutomationProperties.SetHelpText(cancelVertexMove,"İptal isteğinden sonra doğrulama durana kadar bekleyin; sonuç projeye uygulanmaz.");
        footerActions.Children.Add(cancelVertexMove);
        var close=new Button { Content="Atölyeyi kapat",Padding=new Thickness(12,7,12,7),VerticalAlignment=VerticalAlignment.Center };close.Click+=(_,_)=>Close();footerActions.Children.Add(close);Grid.SetColumn(footerActions,1);footer.Children.Add(footerActions);
        var tabs=new TabControl();workshopTabs=tabs;root.Children.Add(tabs);
        tabs.Items.Add(new TabItem { Header="01  Beden serileme",Content=GradePanel() });
        tabs.Items.Add(new TabItem { Header="02  Kontur düzenleme",Content=PatternPanel() });
        tabs.Items.Add(new TabItem { Header="03  Temel pastal",Content=NestingPanel() });
        RefreshSources(workspace.Selected?.Id);
        var textDescriptor=DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty,typeof(TextBlock))
            ?? throw new InvalidOperationException("İşlem sonucu metni izlenemiyor.");
        EventHandler resultTextChanged=(_,_)=>
        {
            AutomationProperties.SetName(result,"Tekstil aracı işlem sonucu: "+result.Text);
            if(System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(result) is { } peer)
                peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        };
        textDescriptor.AddValueChanged(result,resultTextChanged);
        // Constructor setup may already have changed the text before observing it.
        AutomationProperties.SetName(result,"Tekstil aracı işlem sonucu: "+result.Text);
        Closed+=(_,_)=> { textDescriptor.RemoveValueChanged(result,resultTextChanged);windowClosed=true; };
        Closing+=(_,e)=>
        {
            if(vertexMoveBusy)
            {
                e.Cancel=true;result.Foreground=StudioTheme.Warning;
                result.Text=vertexMoveCancellation?.IsCancellationRequested==true
                    ? "Nokta doğrulaması için iptal istendi; işlemin durması bekleniyor."
                    : "Nokta doğrulanıyor; isterseniz iptal isteyin ve işlemin bitmesini bekleyin.";
                return;
            }
            if(basicNestingBusy)
            {
                e.Cancel=true;result.Foreground=StudioTheme.Warning;
                result.Text=basicNestingCancellation?.IsCancellationRequested==true
                    ? "Pastal için iptal istendi; hesabın durması bekleniyor."
                    : "Pastal hesaplanıyor; isterseniz iptal isteyin ve işlemin bitmesini bekleyin.";
                return;
            }
            if(offsetBusy)
            {
                e.Cancel=true;result.Foreground=StudioTheme.Warning;
                result.Text=offsetCancellation?.IsCancellationRequested==true
                    ? "Dikiş payı için iptal istendi; hesaplamanın durması bekleniyor. Atölye işlem bitince kapatılabilir."
                    : "Dikiş payı hesaplanıyor. İsterseniz iptal isteyin; atölyeyi kapatmak için işlemin bitmesini bekleyin.";
                if(offsetAllowanceDescription is not null)result.Text+=" "+offsetAllowanceDescription+".";
                if(offsetSourceDescription is not null)result.Text+=" "+offsetSourceDescription+".";
                return;
            }
            if(gradeBusy)
            {
                e.Cancel=true;result.Foreground=StudioTheme.Warning;
                result.Text=gradeCancellation?.IsCancellationRequested==true
                    ? "İptal istendi; işlemin durması bekleniyor. Atölye işlem bitince kapatılabilir."
                    : "Beden hesaplanıyor. İsterseniz iptal isteyin; atölyeyi kapatmak için işlemin bitmesini bekleyin.";
                return;
            }
            if(!IsVisible)return;
            if(!CommitGradeEdit()) { e.Cancel=true;ShowGradeEditError();return; }
            if(HasUnsavedGradeRules()&&!ConfirmDiscardGradeRules())e.Cancel=true;
        };
    }
    private bool CommitGradeEdit() => grades.CommitEdit(DataGridEditingUnit.Cell,true) && grades.CommitEdit(DataGridEditingUnit.Row,true);
    internal void RequestVertexMoveCancellation()
    {
        if(!vertexMoveBusy || vertexMoveCancellation is null || vertexMoveCancellation.IsCancellationRequested)return;
        vertexMoveCancellation.Cancel();cancelVertexMove.IsEnabled=false;
        result.Foreground=StudioTheme.Warning;result.Text="Nokta doğrulaması için iptal istendi; işlemin durması bekleniyor. Proje henüz değiştirilmedi.";
    }
    internal void RequestBasicNestingCancellation()
    {
        if(!basicNestingBusy || basicNestingCancellation is null || basicNestingCancellation.IsCancellationRequested)return;
        basicNestingCancellation.Cancel();cancelBasicNesting.IsEnabled=false;
        result.Foreground=StudioTheme.Warning;result.Text="Pastal için iptal istendi; hesabın durması bekleniyor. Proje henüz değiştirilmedi.";
    }
    private void RequestOffsetCancellation()
    {
        if(!offsetBusy || offsetCancellation is null || offsetCancellation.IsCancellationRequested)return;
        offsetCancellation.Cancel();cancelOffset.IsEnabled=false;
        result.Foreground=StudioTheme.Warning;
        result.Text="Dikiş payı için iptal istendi; hesaplamanın durması bekleniyor. Sonuç henüz projeye eklenmedi."+
            (offsetAllowanceDescription is null ? "" : " "+offsetAllowanceDescription+".")+
            (offsetSourceDescription is null ? "" : " "+offsetSourceDescription+".");
    }
    private void RequestGradeCancellation()
    {
        if(!gradeBusy || gradeCancellation is null || gradeCancellation.IsCancellationRequested)return;
        gradeCancellation.Cancel();cancelGrade.IsEnabled=false;
        result.Foreground=StudioTheme.Warning;
        result.Text="İptal istendi; hesaplamanın durması bekleniyor. Sonuç henüz projeye eklenmedi.";
    }
    private void ShowGradeEditError()
    {
        result.Foreground=StudioTheme.Danger;
        result.Text="Kaynak değiştirilmedi veya pencere kapatılmadı. Lütfen düzenlenen tablo hücresini tamamlayın.";
        grades.Focus();
    }
    private static Border Card(UIElement child,Thickness margin) => new() { Child=child,Padding=new Thickness(10),Margin=margin,Background=StudioTheme.Surface,BorderBrush=StudioTheme.Border,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10) };
    private static StackPanel Panel() => new() { Margin=new Thickness(18) };
    private static void Note(Panel parent,string text) => parent.Children.Add(new TextBlock { Text=text,Foreground=StudioTheme.Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12),LineHeight=21 });
    private static void Heading(Panel parent,string text) => parent.Children.Add(new TextBlock { Text=text,FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8) });
    private TextBox Input(Panel parent,string label,string value)
    {
        parent.Children.Add(new TextBlock { Text=label,Foreground=StudioTheme.Muted,Margin=new Thickness(0,8,0,6) });
        var input=new TextBox { Text=value,Padding=new Thickness(10),MaxWidth=350,HorizontalAlignment=HorizontalAlignment.Left,MinWidth=220 };
        AutomationProperties.SetName(input,label);
        input.TextChanged+=(_,_)=>RefreshCommands();parent.Children.Add(input);return input;
    }
    private Button Button(Panel parent,string text,Action action,Func<bool>? available=null)
    {
        var button=new Button { Content=text,Padding=new Thickness(16,10,16,10),Margin=new Thickness(0,14,0,8),HorizontalAlignment=HorizontalAlignment.Left };
        button.SetResourceReference(StyleProperty,"Studio.PrimaryButton");
        commands.Add((button,available ?? (()=>true)));
        button.Click+=(_,_)=>
        {
            if(toolReviewRequired) { ReportToolReview("Son işlem",null);return; }
            synchronousCommitDescription=null;
            try { action();RefreshCommands(); }
            catch(Exception ex)
            {
                if(synchronousCommitDescription is not null || toolReviewRequired)
                    ReportToolReview(synchronousCommitDescription ?? "Son işlem",ex);
                else { result.Foreground=StudioTheme.Danger;result.Text="İşlem uygulanmadı: "+ex.Message;RefreshSources(source.SelectedItem is PatternPiece p?p.Id:null,false); }
            }
            finally { synchronousCommitDescription=null; }
        };
        parent.Children.Add(button);
        return button;
    }
    private void RefreshCommands()
    {
        foreach(var command in commands)command.Button.IsEnabled=!toolReviewRequired&&!offsetBusy&&!gradeBusy&&!basicNestingBusy&&!vertexMoveBusy&&command.Available();
        if(resetRulesButton is not null)resetRulesButton.Visibility=rulesNeedReset?Visibility.Visible:Visibility.Collapsed;
    }
    private void Success(string text) { result.Foreground=StudioTheme.Accent;result.Text=text;workspace.SetStatus(text); }
    private void ReportToolReview(string description,Exception? error)
    {
        toolReviewRequired=true;
        result.Foreground=StudioTheme.Warning;
        result.Text=$"{description} projeye uygulandı; görünüm yenilenemedi. Ana çalışma alanını ve geri alma geçmişini kontrol edin. Tekrar işlem yapmak için atölyeyi kapatıp açın."+
            (error is null ? "" : " " + error.Message);
        // Disable without evaluating availability predicates that might depend
        // on the same inconsistent view state that caused the failure.
        foreach(var command in commands)command.Button.IsEnabled=false;
    }
    private void CommitToolChange(string description,Action change,Action afterCommit)
    {
        if(toolReviewRequired) { ReportToolReview("Son işlem",null);return; }
        var committed=false;
        try
        {
            workspace.CommitObserved(description,change,()=> { committed=true;synchronousCommitDescription=description; });
            afterCommit();
        }
        catch(Exception ex) when(committed)
        {
            ReportToolReview(description,ex);
        }
    }
    private PatternPiece Piece() => source.SelectedItem as PatternPiece ?? throw new InvalidOperationException("Önce bir kaynak parça seçin.");
    private bool HasPiece() => source.SelectedItem is PatternPiece;
    private static bool IsNumber(string value,out double number) => MeasurementInput.TryParseMillimeters(value,out number);
    private static double Number(string value) => IsNumber(value,out var number)?number:throw new ArgumentException("Geçerli bir sayı girin. Virgül veya nokta ondalıktır; binlik ayırıcı kullanmayın.");
    private static List<Point2> ParseGradeIncrements(IEnumerable<GradeRow> gradeRows)
    {
        var increments=new List<Point2>();
        foreach(var row in gradeRows)
        {
            if(!IsNumber(row.Dx,out var dx))
                throw new ArgumentException($"Nokta {row.Index}, ΔX: geçerli bir sayı girin. Ondalık için virgül veya nokta kullanın; binlik ayırıcı kullanmayın.");
            if(!IsNumber(row.Dy,out var dy))
                throw new ArgumentException($"Nokta {row.Index}, ΔY: geçerli bir sayı girin. Ondalık için virgül veya nokta kullanın; binlik ayırıcı kullanmayın.");
            increments.Add(new Point2(dx,dy));
        }
        return increments;
    }
    private void RefreshSources(Guid? selected,bool reload=true)
    {
        refreshingSource=true;source.ItemsSource=workspace.Document.Pieces.ToList();
        source.SelectedItem=workspace.Document.Pieces.FirstOrDefault(p=>p.Id==selected)??workspace.Document.Pieces.FirstOrDefault();refreshingSource=false;
        if(reload)ReloadRows();else RefreshCommands();
    }
    private void ReloadRows()
    {
        // Draft rows are reference-owned by their source, so changing the selector does not erase typed values.
        rows.Clear();sourceInfo.Foreground=StudioTheme.Muted;rulesNeedReset=false;
        if(source.SelectedItem is not PatternPiece piece) { activeSourceId=null;activeDraft=null;grades.IsReadOnly=true;sourceInfo.Text="Projede kaynak parça yok. Çizim alanında bir parça oluşturun.";sourceIdentity.Text="";RefreshCommands();return; }
        activeSourceId=piece.Id;
        if(!gradeDrafts.TryGetValue(piece.Id,out var draft))
            gradeDrafts[piece.Id]=draft=DraftFromProject(piece);
        activeDraft=draft;
        rulesNeedReset=draft.Closed!=piece.Closed||!draft.Points.SequenceEqual(piece.Points);
        foreach(var row in draft.Rows)rows.Add(row);
        grades.IsReadOnly=rulesNeedReset;
        var bounds=TekstilCAD.Core.Geometry.Bounds(piece.Points);
        sourceInfo.Text=$"{piece.Points.Count} nokta  ·  {(piece.Closed?"Kapalı kontur":"Açık çizgi")}  ·  {MeasurementInput.Format(bounds.Width)} × {MeasurementInput.Format(bounds.Height)} mm";
        sourceIdentity.Text=$"{SafeSourceName(piece.Name)}\nKimlik: {piece.Id:D}";
        if(rulesNeedReset)
        {
            sourceInfo.Foreground=StudioTheme.Warning;
            sourceInfo.Text="Geometri değişti · Taslağı kontrol edin; uygunsa eşleştirin veya sıfırlayın.\n"+sourceInfo.Text;
        }
        sourceInfo.ToolTip=sourceInfo.Text;
        RefreshCommands();
    }
    private sealed class SourceLabelConverter : IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,CultureInfo culture)
        {
            if(value is not PatternPiece piece)return "";
            var bounds=TekstilCAD.Core.Geometry.Bounds(piece.Points);
            return $"{piece.Id:D} · {SafeSourceName(piece.Name)} · {MeasurementInput.Format(bounds.Width)} × {MeasurementInput.Format(bounds.Height)} mm";
        }
        public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture) => throw new NotSupportedException();
    }
    private static string SafeSourceName(string value)
    {
        var text=new StringBuilder();
        for(var i=0;i<value.Length;i++)
        {
            var c=value[i];
            if(char.IsHighSurrogate(c) && i+1<value.Length && char.IsLowSurrogate(value[i+1]))
            {
                var low=value[i+1]; var pairCategory=CharUnicodeInfo.GetUnicodeCategory(value,i);i++;
                if(pairCategory is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                    text.Append($"\\u{(int)c:X4}\\u{(int)low:X4}");
                else { text.Append(c);text.Append(low); }
                continue;
            }
            var category=char.GetUnicodeCategory(c);
            if(char.IsSurrogate(c) || category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)text.Append($"\\u{(int)c:X4}");
            else text.Append(c);
        }
        return text.Length==0?"(boş)":text.ToString();
    }
    private GradeDraft DraftFromProject(PatternPiece piece)
    {
        var saved=workspace.Document.GradingRules.FirstOrDefault(r=>r.SourcePieceId==piece.Id);
        if(saved is null)return NewDraft(piece);
        return new([..saved.SourcePoints],[..saved.SourcePoints],saved.SourceClosed,saved.SourcePoints.Select((point,index)=>new GradeRow {
            Index=index,X=point.X,Y=point.Y,Dx=MeasurementInput.Format(saved.Deltas[index].X),Dy=MeasurementInput.Format(saved.Deltas[index].Y)
        }).ToList(),saved.Clone(),piece.Clone());
    }
    private GradeDraft NewDraft(PatternPiece piece) => new([..piece.Points],[..piece.Points],piece.Closed,
        piece.Points.Select((point,index)=>new GradeRow { Index=index,X=point.X,Y=point.Y }).ToList(),
        workspace.Document.GradingRules.FirstOrDefault(r=>r.SourcePieceId==piece.Id)?.Clone(),piece.Clone());
    private bool SavedRuleMatchesDraft(Guid pieceId,GradeDraft draft)
    {
        var current=workspace.Document.GradingRules.FirstOrDefault(r=>r.SourcePieceId==pieceId);
        var baseline=draft.BaselineRule;
        return baseline is null ? current is null : current is not null &&
            DocumentSession.ContentEquals(new ProjectDocument { GradingRules=[baseline] },new ProjectDocument { GradingRules=[current] });
    }
    internal static bool IsTranslation(IReadOnlyList<Point2> before,IReadOnlyList<Point2> after)
    {
        if(before.Count!=after.Count || before.Count==0)return false;
        static bool Valid(Point2 p) => p.IsFinite && Math.Abs(p.X)<=Geometry.MaxCoordinate && Math.Abs(p.Y)<=Geometry.MaxCoordinate;
        if(before.Any(p=>!Valid(p)) || after.Any(p=>!Valid(p)))return false;
        var dx=after[0].X-before[0].X;var dy=after[0].Y-before[0].Y;
        if(!double.IsFinite(dx)||!double.IsFinite(dy))return false;
        static bool Equal(double a,double b,double firstBefore,double firstAfter,double currentBefore)
        {
            if(!double.IsFinite(a)||!double.IsFinite(b))return false;
            static double Ulp(double value)=>Math.Abs(Math.BitIncrement(value)-value);
            // Account for rounding in both the anchor subtraction and point
            // addition, including cancellation when a point moves near zero.
            var tolerance=Math.Min(1e-10,4*(Ulp(firstBefore)+Ulp(firstAfter)+Ulp(currentBefore)+Ulp(b)));
            return Math.Abs(a-b)<=tolerance;
        }
        for(var i=0;i<before.Count;i++)
            if((dx==0 ? before[i].X!=after[i].X : !Equal(before[i].X+dx,after[i].X,before[0].X,after[0].X,before[i].X))||
               (dy==0 ? before[i].Y!=after[i].Y : !Equal(before[i].Y+dy,after[i].Y,before[0].Y,after[0].Y,before[i].Y)))return false;
        return true;
    }
    private void ResetRulesForCurrentGeometry()
    {
        var piece=Piece();
        if(gradeDrafts.TryGetValue(piece.Id,out var baselineDraft) && !SavedRuleMatchesDraft(piece.Id,baselineDraft))
            throw new InvalidOperationException("Kaydedilmiş serileme kuralı değişti; taslak sıfırlanmadı. Girilen artışları not edip atölyeyi yeniden açarak güncel kuralı kontrol edin.");
        if(gradeDrafts.TryGetValue(piece.Id,out var draft) && HasUnsavedGradeDraft(piece.Id,draft))
        {
            var document=workspace.Document;
            var original=document.Clone();
            var values=draft.Rows.Select(row=>(row.Dx,row.Dy)).ToArray();
            var confirmed=ConfirmGradeResetForVerification is { } confirm ? confirm() :
                MessageBox.Show(this,"Bu parçanın projeye uygulanmamış serileme artışları silinecek ve geri alınamayacak. Projeye uygulanmış kurallar bu işlemle silinmez. Artışları sıfırlamak istiyor musunuz?",
                    "Uygulanmamış artışları sıfırla",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes;
            if(!confirmed)return;
            if(!ReferenceEquals(workspace.Document,document) || !DocumentSession.ContentEquals(document,original) ||
                source.SelectedItem is not PatternPiece selected || selected.Id!=piece.Id ||
                !gradeDrafts.TryGetValue(piece.Id,out var current) || !ReferenceEquals(current,draft) ||
                !draft.Rows.Select(row=>(row.Dx,row.Dy)).SequenceEqual(values))
            { result.Foreground=StudioTheme.Warning;result.Text="Proje veya artış taslağı değişti; sıfırlama yapılmadı. Tekrar kontrol edin.";return; }
        }
        gradeDrafts[piece.Id]=NewDraft(piece);ReloadRows();
        Success("Bu kaynağın yeni geometrisi için artışlar sıfırlandı. Yeni ΔX / ΔY değerlerini girin.");
    }
    private void RebaseMovedGradeDraft()
    {
        if(!CommitGradeEdit())throw new InvalidOperationException("Önce serileme hücresini tamamlayın; taslak korundu.");
        if(workspace.HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("Önce tamamlanmamış çizimi veya alan taslaklarını tamamlayın; serileme taslağı korundu.");
        if(source.SelectedItem is not PatternPiece selected || !gradeDrafts.TryGetValue(selected.Id,out var draft))
            throw new InvalidOperationException("Eşlenecek serileme taslağı bulunamadı.");
        var piece=workspace.Document.Pieces.FirstOrDefault(p=>p.Id==selected.Id);
        var current=workspace.Document.GradingRules.FirstOrDefault(r=>r.SourcePieceId==selected.Id);
        var baseline=draft.BaselineRule;
        if(piece is null)throw new InvalidOperationException("Kaynak parça bulunamadı; taslak değiştirilmedi.");
        var anchor=baseline?.SourcePoints ?? draft.BaselinePiece.Points;
        var ruleMatches=baseline is null ? current is null : current is not null && baseline.IsExactTranslationTo(current,piece);
        if(!ruleMatches || draft.Closed!=draft.BaselinePiece.Closed || draft.Closed!=piece.Closed ||
           !draft.AnchorPoints.SequenceEqual(anchor) || !draft.BaselinePiece.Points.SequenceEqual(anchor) ||
           (!draft.Points.SequenceEqual(anchor) && !draft.Points.SequenceEqual(piece.Points)) || draft.Rows.Count!=piece.Points.Count ||
           !GradingRuleSet.IsExactPointTranslation(anchor,piece.Points))
            throw new InvalidOperationException("Kaynağın ve varsa kayıtlı kuralın yalnızca tam olarak taşındığı doğrulanamadı; taslak değiştirilmedi.");
        var beforePiece=draft.BaselinePiece.Clone();beforePiece.Points=[..piece.Points];
        if(beforePiece.Grainline is { } beforeLine && piece.Grainline is { } afterLine)
        {
            if(beforeLine.NeedsReview!=afterLine.NeedsReview ||
               !GradingRuleSet.IsExactPointTranslation([anchor[0],beforeLine.Start,beforeLine.End],
                   [piece.Points[0],afterLine.Start,afterLine.End]))
                throw new InvalidOperationException("İplik yönünün kaynakla tam aynı miktarda taşındığı doğrulanamadı; taslak değiştirilmedi.");
            beforePiece.Grainline=afterLine.Clone();
        }
        if(beforePiece.Notches is { } beforeMarks && piece.Notches is { } afterMarks)
        {
            if(beforeMarks.Count!=afterMarks.Count)throw new InvalidOperationException("Köşe işaretleri değişti; taslak değiştirilmedi.");
            for(var i=0;i<beforeMarks.Count;i++)
            {
                if(beforeMarks[i].VertexIndex!=afterMarks[i].VertexIndex ||
                   !GradingRuleSet.IsExactPointTranslation([anchor[0],beforeMarks[i].Position],[piece.Points[0],afterMarks[i].Position]))
                    throw new InvalidOperationException("Köşe işaretlerinin kaynakla tam aynı miktarda taşındığı doğrulanamadı; taslak değiştirilmedi.");
            }
            beforePiece.Notches=afterMarks.Select(mark=>mark.Clone()).ToList();
        }
        if(!DocumentSession.ContentEquals(new ProjectDocument { Pieces=[beforePiece] },new ProjectDocument { Pieces=[piece.Clone()] }))
            throw new InvalidOperationException("Kaynağın başka bilgileri de değişti; taslak değiştirilmedi.");
        var rebased=new GradeDraft([..piece.Points],[..piece.Points],piece.Closed,draft.Rows.Select((row,index)=>new GradeRow {
            Index=index,X=piece.Points[index].X,Y=piece.Points[index].Y,Dx=row.Dx,Dy=row.Dy
        }).ToList(),current?.Clone(),piece.Clone());
        gradeDrafts[piece.Id]=rebased;
        RefreshSources(piece.Id);
        Success("Taslak taşınan kaynakla eşlendi; girilen artışlar korundu. Projeye uygulamak için artışları kontrol edin.");
    }
    internal Action<PointPickerWindow>? GradePickerOpenedForVerification { get; set; }
    private void SelectGradePoint()
    {
        if(!CommitGradeEdit()) { ShowGradeEditError();return; }
        var piece=Piece();
        if(rulesNeedReset || activeDraft is null || activeSourceId!=piece.Id)
            throw new InvalidOperationException("Önce kaynak geometrisi için artışları güncelleyin.");
        var draft=activeDraft;
        var picker=new PointPickerWindow(workspace,this,piece,()=> (source.SelectedItem as PatternPiece)?.Id,
            grades.SelectedItem is GradeRow selected ? selected.Index : -1);
        if(GradePickerOpenedForVerification is { } probe) picker.Loaded+=(_,_)=>probe(picker);
        if(picker.ShowDialog()!=true || picker.SelectedPointIndex is not int index)return;
        if(activeSourceId!=piece.Id || !ReferenceEquals(activeDraft,draft) || rulesNeedReset ||
           index<0 || index>=rows.Count || rows[index].Index!=index)
            throw new InvalidOperationException("Serileme taslağı değişti; nokta seçimi uygulanmadı.");
        var row=rows[index];grades.SelectedItem=row;
        grades.CurrentCell=new DataGridCellInfo(row,grades.Columns[3]);
        grades.ScrollIntoView(row,grades.Columns[3]);grades.Focus();
        result.Foreground=StudioTheme.Muted;
        result.Text=$"Nokta {index} seçildi. Tabloda ΔX ve ΔY artışlarını düzenleyebilirsiniz; değerler değiştirilmedi.";
    }
    private static ScrollViewer Scroll(UIElement content) => new() { Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
    private UIElement GradePanel()
    {
        var layout=new Grid { Margin=new Thickness(12) };
        layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star),MinHeight=80 });
        layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        var help=new Expander { Header="Artışları mm olarak girin · Projeye uygulayıp kaydedin",IsExpanded=false,Margin=new Thickness(0,0,0,2),MaxWidth=400,HorizontalAlignment=HorizontalAlignment.Left };
        help.Content=new TextBlock { Text="ΔX / ΔY bir beden adımındaki mm değişimidir. Her beden aynı seçili temel parçadan hesaplanır. Artışları projeye uygulayın, ardından ana pencereden projeyi kaydedin. Uygulanmamış hücreler atölye kapanınca kaybolur. Köşe işaretleri yeni bedende aynı indeksli noktalara taşınır; görsel işaretler fiziksel çentik veya kesim komutu değildir. Her bedende konumlarını kontrol edin.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Muted,Margin=new Thickness(0,6,0,6) };
        var gradeHeader=new StackPanel { Orientation=Orientation.Horizontal };gradeHeader.Children.Add(help);
        var pickGradePoint=Button(gradeHeader,"Konturdan nokta seç…",SelectGradePoint,()=>HasPiece()&&!rulesNeedReset);
        pickGradePoint.Margin=new Thickness(0,0,0,2);pickGradePoint.Padding=new Thickness(10,3,10,3);
        AutomationProperties.SetHelpText(pickGradePoint,"Kontur önizlemesinden bir nokta seçip serileme tablosundaki satırına gidin. Artışlar değiştirilmez.");
        layout.Children.Add(gradeHeader);
        foreach(var (title,property,readOnly) in new[] { ("Nokta","Index",true),("X · mm","X",true),("Y · mm","Y",true),("ΔX · mm","Dx",false),("ΔY · mm","Dy",false) })
            grades.Columns.Add(new DataGridTextColumn { Header=title,Binding=new Binding(property) { UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged },IsReadOnly=readOnly,Width=new DataGridLength(1,DataGridLengthUnitType.Star) });
        grades.ItemsSource=rows;Grid.SetRow(grades,1);layout.Children.Add(grades);
        // Keep the table visible when optional fields are expanded. The lower
        // controls scroll as a group so nothing is clipped at the minimum size.
        var lower=new StackPanel();
        lower.Children.Add(new TextBlock { Text="Tüm noktalar eşit ötelenirse iplik yönü de ötelenir; diğer durumlarda aynı koordinatlarda kalır. Her durumda kontrol bekler. Her yeni bedende çizginin konturla ilişkisini kontrol edin; aktarım üretime uygunluk onayı değildir.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,0,0,6) });
        var lowerScroll=new ScrollViewer { Content=lower,MaxHeight=150,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=true };
        AutomationProperties.SetName(lowerScroll,"Serileme seçenekleri ve işlemleri");
        AutomationProperties.SetHelpText(lowerScroll,"Seçenekler ve işlemler görünmüyorsa bu alanı kaydırın.");
        KeyboardNavigation.SetIsTabStop(lowerScroll,true);
        Grid.SetRow(lowerScroll,2);layout.Children.Add(lowerScroll);
        var resetRow=new StackPanel();lower.Children.Add(resetRow);

        resetRulesButton=Button(resetRow,"Yeni geometri için artışları sıfırla",ResetRulesForCurrentGeometry,()=>HasPiece()&&rulesNeedReset);
        resetRulesButton.Margin=new Thickness(0,0,0,6);resetRulesButton.Padding=new Thickness(10,5,10,5);resetRulesButton.Visibility=Visibility.Collapsed;
        var batchEdit=new Expander { Header="Seçili satırlara aynı artışı yaz",IsExpanded=false,Margin=new Thickness(0,0,0,6) };
        var batchPanel=new StackPanel();batchEdit.Content=batchPanel;
        batchPanel.Children.Add(new TextBlock { Text="Tabloda Ctrl veya Shift ile satırları seçin. İki değer seçili satırların mevcut artışlarının yerine yazılır; yalnız taslak değişir. Tekrarlı kapanış için ilk ve son satırı ayrı seçin.",TextWrapping=TextWrapping.Wrap });
        var batchFields=new WrapPanel();batchPanel.Children.Add(batchFields);
        var batchX=new TextBox { Width=100,Margin=new Thickness(0,4,8,4),Text="0" };
        var batchY=new TextBox { Width=100,Margin=new Thickness(0,4,8,4),Text="0" };
        AutomationProperties.SetName(batchX,"Seçili satırlar için ΔX mm");AutomationProperties.SetName(batchY,"Seçili satırlar için ΔY mm");
        batchFields.Children.Add(new TextBlock { Text="ΔX (mm)",VerticalAlignment=VerticalAlignment.Center });batchFields.Children.Add(batchX);
        batchFields.Children.Add(new TextBlock { Text="ΔY (mm)",VerticalAlignment=VerticalAlignment.Center });batchFields.Children.Add(batchY);
        var writeBatch=Button(batchPanel,"Seçili satırlara yaz",()=>
        {
            if(rulesNeedReset || activeDraft is null || source.SelectedItem is not PatternPiece piece || activeSourceId!=piece.Id ||
               !ReferenceEquals(workspace.Document.Pieces.FirstOrDefault(p=>p.Id==piece.Id),piece) ||
               activeDraft.Closed!=piece.Closed || !activeDraft.Points.SequenceEqual(piece.Points))
            { result.Text="Kaynak veya taslak güncel değil; toplu işlem uygulanmadı.";return; }
            var draft=activeDraft;var selected=grades.SelectedItems.Cast<GradeRow>().ToArray();
            if(selected.Length==0) { result.Text="Önce tabloda en az bir nokta satırı seçin; toplu işlem uygulanmadı.";return; }
            var textX=batchX.Text;var textY=batchY.Text;
            if(!IsNumber(textX,out _) || !IsNumber(textY,out _))
            { result.Text="İki artış için geçerli mm değerleri girin; toplu işlem uygulanmadı.";return; }
            bool SelectionMatches()
            {
                var current=grades.SelectedItems.Cast<GradeRow>().ToHashSet();
                return selected.Length==current.Count && selected.All(row=>current.Contains(row) &&
                    row.Index>=0 && row.Index<rows.Count && ReferenceEquals(rows[row.Index],row));
            }
            if(!SelectionMatches()) { result.Text="Seçim değişti; toplu işlem uygulanmadı.";return; }
            if(!CommitGradeEdit()) { ShowGradeEditError();return; }
            if(rulesNeedReset || !ReferenceEquals(activeDraft,draft) || source.SelectedItem is not PatternPiece current ||
               !ReferenceEquals(current,piece) || !ReferenceEquals(workspace.Document.Pieces.FirstOrDefault(p=>p.Id==piece.Id),piece) ||
               activeSourceId!=piece.Id || draft.Closed!=piece.Closed || !draft.Points.SequenceEqual(piece.Points) ||
               textX!=batchX.Text || textY!=batchY.Text || !SelectionMatches())
            { result.Text="Kaynak, seçim veya değerler değişti; toplu işlem uygulanmadı. Açık hücre düzenlemesini kontrol edin.";return; }
            foreach(var row in selected) { row.Dx=textX.Trim();row.Dy=textY.Trim(); }
            result.Text=$"{selected.Length} satırın taslak artışları değişti; projeye henüz uygulanmadı. Tekrarlı kapanış varsa ilk ve son satırı kontrol edin.";
        },()=>HasPiece()&&!rulesNeedReset);
        writeBatch.Margin=new Thickness(0,4,0,4);writeBatch.Padding=new Thickness(10,5,10,5);
        batchPanel.Children.Add(new TextBlock { Text="Alternatif: tam iki uç satır seçin. Aradaki satırlar, geometrik mesafeye değil nokta indeksine göre doğrusal doldurulur; uçlar değişmez. Tekrarlı kapanışın iki satırını ayrıca kontrol edin.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0) });
        var interpolate=Button(batchPanel,"İki seçili satır arasını doldur",()=>
        {
            if(rulesNeedReset || activeDraft is null || source.SelectedItem is not PatternPiece piece || activeSourceId!=piece.Id ||
               !ReferenceEquals(workspace.Document.Pieces.FirstOrDefault(p=>p.Id==piece.Id),piece) ||
               activeDraft.Closed!=piece.Closed || !activeDraft.Points.SequenceEqual(piece.Points))
            { result.Text="Kaynak veya taslak güncel değil; ara artışlar değiştirilmedi.";return; }
            var selected=grades.SelectedItems.Cast<GradeRow>().OrderBy(row=>row.Index).ToArray();
            if(selected.Length!=2 || selected[1].Index-selected[0].Index<2 || selected.Any(row=>row.Index<0 || row.Index>=rows.Count || !ReferenceEquals(rows[row.Index],row)))
            { result.Text="Aralarında en az bir satır bulunan iki uç satır seçin; ara artışlar değiştirilmedi.";return; }
            var draft=activeDraft;var first=selected[0];var last=selected[1];
            var firstIndex=first.Index;var lastIndex=last.Index;
            var beforeRows=rows.Skip(firstIndex).Take(lastIndex-firstIndex+1).Select(row=>(Row:row,Dx:row.Dx,Dy:row.Dy)).ToArray();
            if(!IsNumber(first.Dx,out _) || !IsNumber(first.Dy,out _) || !IsNumber(last.Dx,out _) || !IsNumber(last.Dy,out _))
            { result.Text="İki uç satırın ΔX ve ΔY değerleri geçerli olmalı; ara doldurma uygulanmadı.";return; }
            if(!CommitGradeEdit()) { ShowGradeEditError();return; }
            if(rulesNeedReset || !ReferenceEquals(activeDraft,draft) || source.SelectedItem is not PatternPiece current ||
               !ReferenceEquals(current,piece) || !ReferenceEquals(workspace.Document.Pieces.FirstOrDefault(p=>p.Id==piece.Id),piece) ||
               activeSourceId!=piece.Id || draft.Closed!=piece.Closed || !draft.Points.SequenceEqual(piece.Points) ||
               grades.SelectedItems.Count!=2 || !grades.SelectedItems.Contains(first) || !grades.SelectedItems.Contains(last) ||
               first.Index!=firstIndex || last.Index!=lastIndex || lastIndex>=rows.Count ||
               beforeRows.Length!=lastIndex-firstIndex+1 || beforeRows.Where((entry,offset)=>!ReferenceEquals(rows[firstIndex+offset],entry.Row) || entry.Row.Dx!=entry.Dx || entry.Row.Dy!=entry.Dy).Any())
            { result.Text="Kaynak veya seçim değişti; ara artışlar değiştirilmedi. Açık hücre düzenlemesini kontrol edin.";return; }
            if(!IsNumber(first.Dx,out var x0) || !IsNumber(first.Dy,out var y0) || !IsNumber(last.Dx,out var x1) || !IsNumber(last.Dy,out var y1))
            { result.Text="İki uç satırın ΔX ve ΔY değerleri geçerli olmalı; ara artışlar değiştirilmedi.";return; }
            if(piece.Closed && piece.Points.Count>1 && piece.Points[0]==piece.Points[^1] && firstIndex==0 && lastIndex==piece.Points.Count-1 &&
               (BitConverter.DoubleToInt64Bits(x0)!=BitConverter.DoubleToInt64Bits(x1) || BitConverter.DoubleToInt64Bits(y0)!=BitConverter.DoubleToInt64Bits(y1)))
            { result.Text="Tekrarlı kapanışın ilk ve son satır artışları farklı; ara doldurma uygulanmadı. Aynı köşenin iki artışını kontrol edin.";return; }            static System.Numerics.BigInteger Units(double value)
            {
                var bits=BitConverter.DoubleToInt64Bits(value);var exponent=(int)((bits>>52)&0x7ff);
                var mantissa=bits&0x000f_ffff_ffff_ffffL;if(exponent!=0)mantissa|=1L<<52;
                var units=new System.Numerics.BigInteger(mantissa)<<(exponent==0 ? 0 : exponent-1);
                return bits<0 ? -units : units;
            }
            static bool Interpolate(double a,double b,int offset,int count,out double value)
            {
                if(a==b) { value=a;return true; }
                var t=(double)offset/count;value=a*(1-t)+b*t;
                if(!double.IsFinite(value))return false;
                // Opposite signs can cancel exactly even when floating-point
                // products leave a residual. A rounded zero also needs proof.
                if(value==0 || (a<0 && b>0) || (a>0 && b<0))
                {
                    var exact=Units(a)*(count-offset)+Units(b)*offset;
                    if(exact.IsZero) { value=0;return true; }
                    if(value==0)return false;
                }
                return true;
            }
            var span=last.Index-first.Index;var updates=new List<(GradeRow Row,string X,string Y)>();
            for(var index=first.Index+1;index<last.Index;index++)
            {
                var offset=index-first.Index;
                if(!Interpolate(x0,x1,offset,span,out var x) || !Interpolate(y0,y1,offset,span,out var y))
                { result.Text="Ara artış sonlu veya sıfır olmayan değeri koruyarak hesaplanamadı; hiçbir ara satır değiştirilmedi.";return; }
                updates.Add((rows[index],MeasurementInput.Format(x),MeasurementInput.Format(y)));
            }
            foreach(var update in updates) { update.Row.Dx=update.X;update.Row.Dy=update.Y; }
            result.Text=$"{updates.Count} ara satır indeks aralığına göre dolduruldu; uçlar ve proje değişmedi. Sonuçları inceleyip projeye ayrıca uygulayın.";
        },()=>HasPiece()&&!rulesNeedReset);
        interpolate.Margin=new Thickness(0,4,0,4);interpolate.Padding=new Thickness(10,5,10,5);
        var fields=new WrapPanel { VerticalAlignment=VerticalAlignment.Bottom };lower.Children.Add(fields);
        var sizeColumn=new StackPanel { Width=140,Margin=new Thickness(0,0,12,0) };
        var stepColumn=new StackPanel { Width=190,Margin=new Thickness(0,0,12,0) };
        fields.Children.Add(sizeColumn);fields.Children.Add(stepColumn);
        var size=Input(sizeColumn,"Yeni beden adı","L");
        var steps=Input(stepColumn,"Temel bedenden adım sayısı","1");
        foreach(var input in new[] { size,steps }) { input.MinWidth=0;input.MaxWidth=double.PositiveInfinity;input.HorizontalAlignment=HorizontalAlignment.Stretch;input.Padding=new Thickness(8,5,8,5); }
        var sizeHint=new TextBlock { Foreground=StudioTheme.Warning,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,0),Visibility=Visibility.Collapsed };
        var stepsHint=new TextBlock { Foreground=StudioTheme.Warning,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,0),Visibility=Visibility.Collapsed };
        AutomationProperties.SetLiveSetting(sizeHint,AutomationLiveSetting.Polite);AutomationProperties.SetLiveSetting(stepsHint,AutomationLiveSetting.Polite);
        AutomationProperties.SetHelpText(size,"Beden adı boş olamaz ve en fazla 128 karakter olabilir.");
        AutomationProperties.SetHelpText(steps,"Bir sayı girin. Negatif ve ondalıklı adımlar kullanılabilir.");
        sizeColumn.Children.Add(sizeHint);stepColumn.Children.Add(stepsHint);
        bool ValidSize()=>!string.IsNullOrWhiteSpace(size.Text)&&size.Text.Trim().Length<=128;
        void SetInputHint(TextBlock hint,string message)
        {
            if(hint.Text==message)return;
            hint.Text=message;hint.Visibility=message.Length==0?Visibility.Collapsed:Visibility.Visible;
            AutomationProperties.SetName(hint,message);
            if(message.Length>0 && System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(hint) is { } peer)
                peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
        void UpdateGradeInputHints()
        {
            SetInputHint(sizeHint,ValidSize()?"":"1–128 karakterlik bir beden adı girin.");
            SetInputHint(stepsHint,IsNumber(steps.Text,out _)?"":"Geçerli bir adım sayısı girin; virgül veya nokta kullanabilirsiniz.");
        }
        size.TextChanged+=(_,_)=>UpdateGradeInputHints();steps.TextChanged+=(_,_)=>UpdateGradeInputHints();
        UpdateGradeInputHints();
        // Optional, explicit transfers: production quantities always need a fresh decision.
        var metadataOptions=new Expander { Header="İsteğe bağlı parça bilgileri ve toplu artışlar",IsExpanded=false,Margin=new Thickness(0,0,0,4) };
        var metadataPanel=new StackPanel();
        var includeSize=new CheckBox { Content="Yeni beden adını beden bilgisi olarak ekle",IsChecked=false,Margin=new Thickness(0,3,0,3) };
        var copyRoleMaterial=new CheckBox { Content="Kaynağın rol ve malzemesini kopyala",IsChecked=false,Margin=new Thickness(0,3,0,3) };
        AutomationProperties.SetName(includeSize,"Yeni beden adını beden bilgisi olarak ekle");
        AutomationProperties.SetName(copyRoleMaterial,"Kaynağın rol ve malzemesini kopyala");
        AutomationProperties.SetHelpText(copyRoleMaterial,"Yalnız mevcut rol ve malzeme kopyalanır; adet hiçbir zaman kopyalanmaz.");
        metadataPanel.Children.Add(includeSize);metadataPanel.Children.Add(copyRoleMaterial);
        metadataPanel.Children.Add(new TextBlock { Text="Adet aktarılmaz; adetli pastal için yeni parçada ayrıca girin.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Muted });
        metadataPanel.Children.Add(batchEdit);
        metadataOptions.Content=metadataPanel;resetRow.Children.Add(metadataOptions);
        source.SelectionChanged+=(_,_)=> { if(!refreshingSource) { includeSize.IsChecked=false;copyRoleMaterial.IsChecked=false; } };
        var apply=Button(fields,"Artışları projeye uygula",()=>
        {
            if(!grades.CommitEdit(DataGridEditingUnit.Cell,true)||!grades.CommitEdit(DataGridEditingUnit.Row,true))throw new ArgumentException("Serileme tablosundaki hücreyi tamamlayın.");
            var piece=Piece();
            if(!ReferenceEquals(workspace.Document.Pieces.FirstOrDefault(p=>p.Id==piece.Id),piece))
                throw new InvalidOperationException("Kaynak parça veya proje değişti; artışlar uygulanmadı. Taslak korundu; güncel parçayı ve geometriyi kontrol edin.");
            if(rulesNeedReset||activeDraft is null||activeSourceId!=piece.Id||activeDraft.Closed!=piece.Closed||!activeDraft.Points.SequenceEqual(piece.Points))throw new InvalidOperationException("Kaynak geometrisiyle artış tablosu eşleşmiyor.");
            if(!SavedRuleMatchesDraft(piece.Id,activeDraft))
                throw new InvalidOperationException("Kaydedilmiş serileme kuralı değişti; üzerine yazılmadı. Girilen artışlar korundu; güncel kuralı kontrol edin.");
            var rule=new GradingRuleSet { SourcePieceId=piece.Id,SourceClosed=piece.Closed,SourcePoints=[..piece.Points],Deltas=ParseGradeIncrements(rows) };
            CommitToolChange("Serileme artışları",()=> { workspace.Document.GradingRules.RemoveAll(r=>r.SourcePieceId==rule.SourcePieceId);workspace.Document.GradingRules.Add(rule); },()=>
            {
                gradeDrafts[piece.Id]=DraftFromProject(piece); // Explicit application establishes a new verified baseline.
                RefreshSources(piece.Id);Success("Artışlar projeye uygulandı. Dosyada korumak için projeyi kaydedin.");
            });
        },()=>HasPiece()&&!rulesNeedReset);
        apply.VerticalAlignment=VerticalAlignment.Bottom;apply.Margin=new Thickness(0,0,8,0);apply.Padding=new Thickness(12,7,12,7);
        var create=Button(fields,"Yeni bedeni projeye ekle",()=>
        {
            if(!gradeBusy) GradeTaskForVerification=CreateGradeAsync();
        },()=>HasPiece()&&!rulesNeedReset&&ValidSize()&&IsNumber(steps.Text,out _));
        async Task CreateGradeAsync()
        {
            if(gradeBusy || offsetBusy || basicNestingBusy || vertexMoveBusy || toolReviewRequired)return;
            var gradeCommitted=false;
            try
            {
                if(!CommitGradeEdit())throw new ArgumentException("Serileme tablosundaki hücreyi tamamlayın.");
                if(workspace.HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("Önce tamamlanmamış çizimi, sürüklemeyi veya parça alan taslaklarını tamamlayın ya da iptal edin.");
                var piece=Piece();
                if(!ReferenceEquals(workspace.Document.Pieces.FirstOrDefault(p=>p.Id==piece.Id),piece))
                    throw new InvalidOperationException("Kaynak listesi güncel değil. Çalışmanızı koruyup pencereyi kapatın ve yeniden açın.");
                if(rulesNeedReset||activeSourceId!=piece.Id||activeDraft is null||activeDraft.Closed!=piece.Closed||!activeDraft.Points.SequenceEqual(piece.Points))
                    throw new InvalidOperationException("Kaynak geometrisiyle artış tablosu eşleşmiyor. Kaynağı yeniden seçip kuralları kontrol edin.");
                if(!SavedRuleMatchesDraft(piece.Id,activeDraft))
                    throw new InvalidOperationException("Kaydedilmiş serileme kuralı değişti; yeni beden oluşturulmadı. Girilen artışlar korundu; güncel kuralı kontrol edin.");
                var sizeText=size.Text;var stepText=steps.Text;
                var sizeName=sizeText.Trim();
                if(string.IsNullOrWhiteSpace(sizeName)||sizeName.Length>128)throw new ArgumentException("Beden adını 1-128 karakter arasında girin.");
                var expectedName=$"{piece.Name} · {sizeName}";
                if(workspace.Document.Pieces.Any(p=>string.Equals(p.Name.Trim(),expectedName,StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException($"“{expectedName}” zaten projede var. Farklı bir beden adı girin veya mevcut bedeni ana çalışma alanında düzenleyin.");
                var rowText=rows.Select(r=>(r.Dx,r.Dy)).ToArray();
                var increments=ParseGradeIncrements(rows);
                var stepCount=Number(stepText);
                var transferSize=includeSize.IsChecked==true;
                var transferRoleMaterial=copyRoleMaterial.IsChecked==true;
                var document=workspace.Document;
                var original=document.Clone();
                var snapshot=piece.Clone();
                var role=transferRoleMaterial ? snapshot.Metadata?.Role : null;
                var material=transferRoleMaterial ? snapshot.Metadata?.Material : null;
                var beforeGrade=BeforeGradeForVerification;
                var afterGrade=AfterGradeBeforeReturnForVerification;
                gradeCancellation=new CancellationTokenSource();var token=gradeCancellation.Token;
                gradeBusy=true;cancelGrade.IsEnabled=true;cancelGrade.Visibility=Visibility.Visible;source.IsEnabled=false;grades.IsEnabled=false;lower.IsEnabled=false;RefreshCommands();
                result.Foreground=StudioTheme.Muted;result.Text="Yeni beden hesaplanıyor. İptal isteyebilirsiniz; atölyeyi kapatmak için işlemin bitmesini bekleyin.";
                var generated=await Task.Run(()=>
                {
                    beforeGrade?.Invoke();
                    token.ThrowIfCancellationRequested();
                    var item=GradingEngine.Generate(snapshot,increments,stepCount,sizeName,token);
                    BulkGradingWindow.ValidateGrainTransfer(snapshot,item,increments,stepCount);
                    if(transferSize || role is not null || material is not null)
                        item.Metadata=new PieceMetadata { Size=transferSize ? sizeName : null,Role=role,Material=material,Quantity=null };
                    token.ThrowIfCancellationRequested();
                    var candidate=original.Clone();candidate.Pieces.Add(item.Clone());MainWindow.Validate(candidate);
                    afterGrade?.Invoke();
                    token.ThrowIfCancellationRequested();
                    return item;
                });
                if(windowClosed)return;
                token.ThrowIfCancellationRequested();
                if(workspace.HasPendingCanvasOrPropertyWork || !ReferenceEquals(workspace.Document,document) ||
                   !DocumentSession.ContentEquals(workspace.Document,original) || source.SelectedItem is not PatternPiece current || current.Id!=snapshot.Id ||
                   activeSourceId!=snapshot.Id || rulesNeedReset || activeDraft is null || activeDraft.Closed!=snapshot.Closed || !activeDraft.Points.SequenceEqual(snapshot.Points) ||
                   size.Text!=sizeText || steps.Text!=stepText || (includeSize.IsChecked==true)!=transferSize || (copyRoleMaterial.IsChecked==true)!=transferRoleMaterial ||
                   !rows.Select(r=>(r.Dx,r.Dy)).SequenceEqual(rowText))
                {
                    result.Foreground=StudioTheme.Warning;result.Text="Proje veya serileme girdileri hesaplama sırasında değişti; yeni beden eklenmedi. Kontrol edip yeniden deneyin.";return;
                }
                var roleNotice=transferRoleMaterial ? (role is null ? "kaynakta yoktu" : "kopyalandı") : "kopyalanmadı";
                var materialNotice=transferRoleMaterial ? (material is null ? "kaynakta yoktu" : "kopyalandı") : "kopyalanmadı";
                var metadataNotice=transferSize || transferRoleMaterial
                    ? $"Beden bilgisi {(transferSize ? "eklendi" : "eklenmedi")}; rol {roleNotice}, malzeme {materialNotice}. Adet aktarılmadı. Adetli pastal için yeni parçanın rol, malzeme ve adet bilgilerini kontrol edin ve adedi ayrıca girin."
                    : "Yeni parçaya rol, beden, malzeme ve adet bilgileri aktarılmadı. Adetli pastalda kullanmak için yeni parçayı seçip Parça bilgileri bölümünden rol, malzeme ve adedi ayrı ayrı girin; beden bilgisini de burada belirtebilirsiniz.";
                // The dispatcher cannot process a new cancellation while this commit runs.
                // A request already received before this point must leave the project untouched.
                token.ThrowIfCancellationRequested();
                var grainNotice=generated.Grainline is null ? "" : " İplik yönü aktarıldı ve kontrol bekliyor; yeni konturla ilişkisini kontrol edin. Bu aktarım üretime uygunluk onayı değildir.";
                var markNotice=generated.Notches is { Count:>0 } ? $" {generated.Notches.Count} köşe işareti aynı indeksli yeni noktalara aktarıldı; konumlarını kontrol edin. İşaretler fiziksel çentik değildir." : "";
                AddGenerated(generated,true,onCommitted:()=>gradeCommitted=true);Success($"{generated.Name} oluşturuldu. Temel parça “{snapshot.Name}” ve artışlar korundu. {metadataNotice}{grainNotice}{markNotice}");
                if(generated.Grainline is not null)result.Foreground=StudioTheme.Warning;
                if(generated.Points.SequenceEqual(snapshot.Points))
                {
                    result.Foreground=StudioTheme.Warning;
                    result.Text=$"{generated.Name} eklendi; geometri temel parçayla aynı. Adım sayısı veya tüm artışlar sıfır olabilir. Ölçü değişikliği bekliyorsanız kuralları kontrol edin. {metadataNotice}{grainNotice}{markNotice}";
                    workspace.SetStatus(result.Text);
                }
            }
            catch(OperationCanceledException) when(!gradeCommitted && gradeCancellation?.IsCancellationRequested == true)
            {
                if(!windowClosed) { result.Foreground=StudioTheme.Warning;result.Text="Beden hesabı iptal edildi; proje ve serileme girdileri korunuyor."; }
            }
            catch(Exception ex)
            {
                if(gradeCommitted)
                {
                    toolReviewRequired=true;
                    if(!windowClosed)ReportToolReview("Beden",ex);
                }
                else if(!windowClosed) { result.Foreground=StudioTheme.Danger;result.Text="İşlem uygulanmadı: "+ex.Message; }
            }
            finally
            {
                gradeBusy=false;gradeCancellation?.Dispose();gradeCancellation=null;cancelGrade.Visibility=Visibility.Collapsed;cancelGrade.IsEnabled=false;
                if(!windowClosed) { source.IsEnabled=true;grades.IsEnabled=true;lower.IsEnabled=true;RefreshCommands(); }
            }
        }
        AutomationProperties.SetHelpText(create,"Yeni parça bilgileri varsayılan olarak otomatik aktarılmaz; isteğe bağlı seçilebilir. Adet hiçbir zaman aktarılmaz; adetli pastal için ayrıca girin.");
        create.ToolTip="İsteğe bağlı beden, rol ve malzeme seçilebilir; adet her zaman ayrıca girilir.";
        create.VerticalAlignment=VerticalAlignment.Bottom;create.Margin=new Thickness(0,0,0,0);create.Padding=new Thickness(12,7,12,7);
        // Leave room below the last row for the scroll viewer's clipped viewport,
        // so its buttons can be scrolled fully into view at the minimum size.
        var secondaryActions=new WrapPanel { Margin=new Thickness(0,0,0,12) };lower.Children.Add(secondaryActions);
        var batch=Button(secondaryActions,"Birden fazla beden oluştur…",()=>
        {
            if(!CommitGradeEdit())throw new ArgumentException("Serileme tablosundaki hücreyi tamamlayın.");
            if(workspace.HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("Önce tamamlanmamış çizimi, sürüklemeyi veya parça alan taslaklarını tamamlayın ya da iptal edin.");
            var piece=Piece();
            if(!ReferenceEquals(workspace.Document.Pieces.FirstOrDefault(p=>p.Id==piece.Id),piece))throw new InvalidOperationException("Kaynak listesi güncel değil. Atölyeyi yeniden açın.");
            if(rulesNeedReset || activeDraft is null || activeSourceId!=piece.Id || activeDraft.Closed!=piece.Closed || !activeDraft.Points.SequenceEqual(piece.Points))
                throw new InvalidOperationException("Kaynak geometrisiyle artış tablosu eşleşmiyor.");
            if(!SavedRuleMatchesDraft(piece.Id,activeDraft))throw new InvalidOperationException("Kaydedilmiş serileme kuralı değişti; artışlarınızı koruyup güncel kuralı kontrol edin.");
            var draft=activeDraft;var rowText=rows.Select(r=>(r.Dx,r.Dy)).ToArray();var increments=ParseGradeIncrements(rows);var selected=piece.Id;
            bool CurrentDraft()=>!windowClosed && !gradeBusy && !offsetBusy && source.SelectedItem is PatternPiece current && current.Id==selected &&
                activeSourceId==selected && ReferenceEquals(activeDraft,draft) && !rulesNeedReset && draft.Closed==current.Closed && draft.Points.SequenceEqual(current.Points) &&
                SavedRuleMatchesDraft(selected,draft) && rows.Select(r=>(r.Dx,r.Dy)).SequenceEqual(rowText);
            new BulkGradingWindow(workspace,this,piece,increments,CurrentDraft,()=> { RefreshSources(selected);Success("Bedenler projeye eklendi. Temel parça ve artış taslağı korundu; projeyi kaydedin."+(piece.Grainline is null ? "" : " İplik yönleri aktarıldı ve kontrol bekliyor; her bedenin konturuyla ilişkisini kontrol edin. Bu aktarım üretime uygunluk onayı değildir."));if(piece.Grainline is not null)result.Foreground=StudioTheme.Warning; }).ShowDialog();
        },()=>HasPiece()&&!rulesNeedReset);
        batch.Margin=new Thickness(0,8,8,0);batch.HorizontalAlignment=HorizontalAlignment.Left;
        AutomationProperties.SetHelpText(batch,"Birden fazla beden ve adım girip hepsini inceleyin; onaylanınca tek işlem olarak eklenir.");
        var rebase=Button(secondaryActions,"Taşınan kaynakla taslağı eşleştir",RebaseMovedGradeDraft,()=>HasPiece());
        rebase.Margin=new Thickness(0,8,0,0);rebase.Padding=new Thickness(10,5,10,5);rebase.HorizontalAlignment=HorizontalAlignment.Left;
        AutomationProperties.SetHelpText(rebase,"Kaynak ve varsa kayıtlı kural tam olarak birlikte taşındıysa taslak artışlarını koruyarak yeniden eşleştirir.");
        return layout;
    }
    private bool HasUnsavedGradeDraft(Guid pieceId,GradeDraft draft)
    {
        var saved=workspace.Document.GradingRules.FirstOrDefault(r=>r.SourcePieceId==pieceId);
        return HasUnsavedGradeDraft(draft,saved);
    }
    private static bool HasUnsavedGradeDraft(GradeDraft draft,GradingRuleSet? saved)
    {
        static bool SameBits(double a,double b)=>BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b);
        if(saved is null)return draft.Rows.Any(row=>!IsNumber(row.Dx,out var dx)||!SameBits(dx,0)||!IsNumber(row.Dy,out var dy)||!SameBits(dy,0));
        if(saved.SourceClosed!=draft.Closed||(!saved.SourcePoints.SequenceEqual(draft.Points)&&!IsTranslation(saved.SourcePoints,draft.Points))||saved.Deltas.Count!=draft.Rows.Count)return true;
        return draft.Rows.Select((row,index)=>!IsNumber(row.Dx,out var dx)||!IsNumber(row.Dy,out var dy)||!SameBits(dx,saved.Deltas[index].X)||!SameBits(dy,saved.Deltas[index].Y)).Any(changed=>changed);
    }
    private bool HasSnapshotGradeDraft(PatternPiece piece,GradingRuleSet? saved)
    {
        if(!gradeDrafts.TryGetValue(piece.Id,out var draft))return false;
        if(draft.Closed!=piece.Closed || draft.Points.Count!=piece.Points.Count)return true;
        for(var i=0;i<draft.Points.Count;i++)
            if(BitConverter.DoubleToInt64Bits(draft.Points[i].X)!=BitConverter.DoubleToInt64Bits(piece.Points[i].X) ||
               BitConverter.DoubleToInt64Bits(draft.Points[i].Y)!=BitConverter.DoubleToInt64Bits(piece.Points[i].Y))return true;
        return HasUnsavedGradeDraft(draft,saved);
    }
    internal void ResetCleanGradeDrafts(IReadOnlyCollection<Guid> ids,ProjectDocument sourceSnapshot)
    {
        var pieces=sourceSnapshot.Pieces.ToDictionary(piece=>piece.Id);
        var rules=sourceSnapshot.GradingRules.ToDictionary(rule=>rule.SourcePieceId);
        foreach(var id in ids)
            if(pieces.TryGetValue(id,out var piece) && !HasSnapshotGradeDraft(piece,rules.GetValueOrDefault(id)))gradeDrafts.Remove(id);
    }
    private bool HasUnsavedGradeRules() => gradeDrafts.Any(entry=>HasUnsavedGradeDraft(entry.Key,entry.Value));
    private bool ConfirmDiscardGradeRules()
    {
        // Headless verification must never create a visible prompt.
        if(!IsVisible)return true;
        var prompt=new Window { Owner=this,Title="Uygulanmamış artışlar",Width=480,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner };
        StudioTheme.Apply(prompt);
        var panel=new StackPanel { Margin=new Thickness(20) };prompt.Content=panel;
        panel.Children.Add(new TextBlock { Text="Atölyeyi kapatmak istiyor musunuz?",FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,10) });
        panel.Children.Add(new TextBlock { Text="Projeye uygulanmamış artışlar atölyeyi kapatırsanız kaybolur. Önce atölyeye dönüp artışları projeye uygulayabilir, sonra projeyi kaydedebilirsiniz.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,18) });
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right };panel.Children.Add(actions);
        var cancel=new Button { Content="Atölyeye dön",IsCancel=true,IsDefault=true,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,0) };
        cancel.Click+=(_,_)=>prompt.DialogResult=false;actions.Children.Add(cancel);
        var discard=new Button { Content="Uygulamadan kapat",Padding=new Thickness(12,8,12,8) };
        discard.Click+=(_,_)=>prompt.DialogResult=true;actions.Children.Add(discard);
        return prompt.ShowDialog()==true;
    }
    private UIElement PatternPanel()
    {
        var panel=Panel();Heading(panel,"İki nokta arası mesafe");
        Note(panel,"Kaynak parçanın mevcut noktaları arasındaki düz geometrik mesafeyi gösterir.");
        Button(panel,"İki nokta arasını ölç…",()=>
        {
            if(workspace.HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("Önce tamamlanmamış çizimi veya alan taslaklarını tamamlayın ya da iptal edin.");
            new PointDistanceWindow(workspace,this,Piece(),()=> (source.SelectedItem as PatternPiece)?.Id).ShowDialog();
        },()=>HasPiece());
        Button(panel,"İki dikiş kenarını karşılaştır…",()=>new SeamLengthComparisonWindow(workspace,this,()=> (source.SelectedItem as PatternPiece)?.Id).ShowDialog(),()=>HasPiece());
        Button(panel,"Kapalı konturdan açık kenar çıkar…",()=>
        {
            if(workspace.HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("Önce tamamlanmamış çizimi veya alan taslaklarını tamamlayın ya da iptal edin.");
            var selected=Piece();new ContourPathExtractionWindow(workspace,this,selected,()=> (source.SelectedItem as PatternPiece)?.Id).ShowDialog();
            RefreshSources((source.SelectedItem as PatternPiece)?.Id);
        },()=>source.SelectedItem is PatternPiece { Closed:true,Points.Count:>=3 });
        Button(panel,"Açık konturu kapat…",()=>
        {
            if(!CommitGradeEdit())throw new InvalidOperationException("Önce serileme tablosundaki hücreyi tamamlayın.");
            if(workspace.HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("Önce tamamlanmamış çizimi veya alan taslaklarını tamamlayın ya da iptal edin.");
            new OpenPathClosureWindow(workspace,this,HasSnapshotGradeDraft).ShowDialog();
            RefreshSources((source.SelectedItem as PatternPiece)?.Id);
        },()=>workspace.Document.Pieces.Any(p=>!p.Closed));
        Button(panel,"Açık konturu böl…",()=>
        {
            if(!CommitGradeEdit())throw new InvalidOperationException("Önce serileme tablosundaki hücreyi tamamlayın.");
            if(workspace.HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("Önce tamamlanmamış çizimi veya alan taslaklarını tamamlayın ya da iptal edin.");
            var selected=Piece();new OpenPathSplitWindow(workspace,this,selected,()=> (source.SelectedItem as PatternPiece)?.Id,HasSnapshotGradeDraft).ShowDialog();
            RefreshSources((source.SelectedItem as PatternPiece)?.Id);
        },()=>source.SelectedItem is PatternPiece { Closed:false,Points.Count:>=3 });
        Button(panel,"İki açık konturu birleştir…",()=>
        {
            if(!CommitGradeEdit())throw new InvalidOperationException("Önce serileme tablosundaki hücreyi tamamlayın.");
            if(workspace.HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("Önce tamamlanmamış çizimi veya alan taslaklarını tamamlayın ya da iptal edin.");
            var dialog=new OpenPathJoinWindow(workspace,this,HasSnapshotGradeDraft);
            dialog.ShowDialog();
            RefreshSources((source.SelectedItem as PatternPiece)?.Id);
        },()=>workspace.Document.Pieces.Count(p=>!p.Closed)>=2);
        panel.Children.Add(new Separator { Margin=new Thickness(0,18,0,18) });
        Heading(panel,"Dikiş payı");Note(panel,"Düz kenarlı, basit ve kapalı konturlara eşit dış pay ekler. Ölçüler 0,000001 mm ızgarasına yuvarlanır; dar köşe veya belirsiz kontur reddedilebilir. Sonuç ayrı parçadır. Eğri, çentik ve kenara özel pay desteği yoktur. Deneysel sonucu üretimden önce kontrol edin.");
        var allowance=Input(panel,"Dışa dikiş payı · mm","10");
        offsetAllowance=allowance;
        AutomationProperties.SetHelpText(allowance,"0,000001 ile 100000 mm arasında bir değer girin. Ondalık için virgül veya nokta kullanın.");
        var allowanceHint=new TextBlock { TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,4,0,0),Visibility=Visibility.Collapsed };
        AutomationProperties.SetLiveSetting(allowanceHint,AutomationLiveSetting.Polite);
        panel.Children.Add(allowanceHint);
        var allowanceGrainHint=new TextBlock { TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,4,0,0),Visibility=Visibility.Collapsed };
        AutomationProperties.SetLiveSetting(allowanceGrainHint,AutomationLiveSetting.Polite);
        panel.Children.Add(allowanceGrainHint);
        void UpdateAllowanceGrainHint()
        {
            var selected=source.SelectedItem as PatternPiece;
            var line=selected?.Grainline;
            var message=line is null ? "" :
                $"Kaynak iplik yönü · Parça kimliği: {selected!.Id:D} · Başlangıç X {MeasurementInput.Format(line.Start.X)}, Y {MeasurementInput.Format(line.Start.Y)} mm; bitiş X {MeasurementInput.Format(line.End.X)}, Y {MeasurementInput.Format(line.End.Y)} mm. Tüm noktalar eşit ötelenirse çizgi de ötelenir; aksi halde aynı koordinatlarda kalır. Yeniden inceleme gerektirir. Çizginin varlığı fiziksel üretim uygunluğunu doğrulamaz.";
            if(allowanceGrainHint.Text==message)return;
            allowanceGrainHint.Text=message;
            AutomationProperties.SetName(allowanceGrainHint,message);
            allowanceGrainHint.Visibility=message.Length==0 ? Visibility.Collapsed : Visibility.Visible;
            if(message.Length>0 && System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(allowanceGrainHint) is { } peer)
                peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
        bool ValidAllowance()=>IsNumber(allowance.Text,out var value)&&value>=0.000001&&value<=100000;
        void UpdateAllowanceHint()
        {
            var message=!ValidAllowance()
                ? "0,000001 ile 100000 mm arasında bir sayı girin. Ondalık için virgül veya nokta kullanabilirsiniz."
                : source.SelectedItem is not PatternPiece piece
                    ? "Dikiş payı için önce bir kaynak parça seçin."
                    : !piece.Closed ? "Dikiş payı için kapalı bir parça seçin." : "";
            if(allowanceHint.Text==message)return;
            allowanceHint.Text=message;
            AutomationProperties.SetName(allowanceHint,message);
            allowanceHint.Visibility=message.Length==0?Visibility.Collapsed:Visibility.Visible;
            if(message.Length>0 && System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(allowanceHint) is { } peer)
                peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
        allowance.TextChanged+=(_,_)=>UpdateAllowanceHint();
        source.SelectionChanged+=(_,_)=> { UpdateAllowanceHint();UpdateAllowanceGrainHint(); };
        UpdateAllowanceHint();UpdateAllowanceGrainHint();
        Button(panel,"Dikiş payını ayrı parça oluştur",()=>_ = CreateOffsetAsync(allowance.Text),()=>!offsetReviewRequired&&source.SelectedItem is PatternPiece { Closed:true }&&ValidAllowance());
        Button(panel,"Seçili çentik ölçülerini karşılaştır…",()=>
        {
            if(workspace.HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("Önce bekleyen çizimi veya taslağı tamamlayın ya da iptal edin.");
            new SelectedNotchComparisonWindow(workspace,this,Piece()).ShowDialog();
        },()=>HasPiece() && workspace.Document.Pieces.Count>0);
        panel.Children.Add(new Separator { Margin=new Thickness(0,18,0,18) });Heading(panel,"Kontur noktaları");Note(panel,"Nokta indeksleri 0'dan başlar. Değişiklik seçili parçaya uygulanır; ana çalışma alanında geri alınabilir. Tekrarlanan kapanış noktasında ilk ve son indeks aynı köşedir; bu köşe birlikte düzenlenir. Ekleme bu köşeden sonra yapılır.");
        var index=Input(panel,"Nokta indeksi / eklenecek noktanın önceki indeksi","0");var px=Input(panel,"Yeni nokta X · mm","0");var py=Input(panel,"Yeni nokta Y · mm","0");
        Button(panel,"Kontur noktası seç…",()=>
        {
            if(workspace.HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("Önce tamamlanmamış çizimi, sürüklemeyi veya parça alan taslaklarını tamamlayın ya da iptal edin.");
            var selected=Piece();var indexBefore=index.Text;var xBefore=px.Text;var yBefore=py.Text;
            var initial=int.TryParse(indexBefore,NumberStyles.Integer,CultureInfo.InvariantCulture,out var parsed) ? parsed : -1;
            var picker=new PointPickerWindow(workspace,this,selected,()=> (source.SelectedItem as PatternPiece)?.Id,initial);
            if(picker.ShowDialog()!=true)return;
            picker.CheckCurrent();
            if(picker.IsStale || picker.SelectedPointIndex is not { } picked || index.Text!=indexBefore || px.Text!=xBefore || py.Text!=yBefore)
            { result.Foreground=StudioTheme.Warning;result.Text="Nokta seçimi sırasında kaynak veya girişler değişti; indeks aktarılmadı.";return; }
            index.Text=picked.ToString(CultureInfo.InvariantCulture);
            result.Foreground=StudioTheme.Muted;result.Text=$"{picked} numaralı nokta seçildi. X/Y girişleri değiştirilmedi; taşıma öncesinde kontrol edin.";
        },()=>HasPiece());
        vertexIndexInput=index;vertexXInput=px;vertexYInput=py;
        bool TryIndex(out int value)
        {
            value=-1;
            return source.SelectedItem is PatternPiece p && int.TryParse(index.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out value) && value>=0 && value<p.Points.Count;
        }
        bool ValidIndex()=>TryIndex(out _);
        int LogicalCount(PatternPiece p)=>p.Points.Count-(p.Closed && p.Points.Count>1 && p.Points[0]==p.Points[^1] ? 1 : 0);
        bool SelectedIsMarked()
        {
            if(!TryIndex(out var selected) || source.SelectedItem is not PatternPiece p)return false;
            if(p.Closed && selected==p.Points.Count-1 && LogicalCount(p)<p.Points.Count)selected=0;
            return p.Notches?.Any(mark=>mark.VertexIndex==selected)==true;
        }
        Note(panel,"Mevcut koordinatları yüklemek X/Y girişlerindeki değerleri değiştirir; parçayı değiştirmez.");
        Button(panel,"Seçili noktanın koordinatlarını yükle",()=>
        {
            if(workspace.HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("Önce tamamlanmamış çizimi, sürüklemeyi veya parça alan taslaklarını tamamlayın ya da iptal edin.");
            var selected=Piece();
            var current=workspace.Document.Pieces.FirstOrDefault(p=>p.Id==selected.Id);
            if(!ReferenceEquals(current,selected))throw new InvalidOperationException("Kaynak parça değişti; seçimi yenileyin.");
            if(!int.TryParse(index.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var pointIndex) || pointIndex<0 || pointIndex>=current.Points.Count)
                throw new ArgumentException("Geçerli bir nokta indeksi seçin.");
            var point=current.Points[pointIndex];
            var x=MeasurementInput.Format(point.X);var y=MeasurementInput.Format(point.Y);
            px.Text=x;py.Text=y;
            result.Foreground=StudioTheme.Muted;result.Text=$"{pointIndex} numaralı noktanın mevcut koordinatları yüklendi. Parça değiştirilmedi; taşıma için koordinatları düzenleyin.";
        },()=>ValidIndex());
        AutomationProperties.SetHelpText(index,"Seçili parçanın 0'dan başlayan mevcut nokta indeksini girin; ekleme bu noktadan sonra yapılır.");
        AutomationProperties.SetHelpText(px,"Yeni noktanın X koordinatını milimetre cinsinden girin; ondalık için virgül veya nokta kullanın.");
        AutomationProperties.SetHelpText(py,"Yeni noktanın Y koordinatını milimetre cinsinden girin; ondalık için virgül veya nokta kullanın.");
        var pointHint=new TextBlock { TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,4,0,0),Visibility=Visibility.Collapsed };
        AutomationProperties.SetLiveSetting(pointHint,AutomationLiveSetting.Polite);panel.Children.Add(pointHint);
        void UpdatePointHint()
        {
            var piece=source.SelectedItem as PatternPiece;
            var message=piece is null ? "Nokta düzenlemek için önce bir kaynak parça seçin."
                : piece.Points.Count==0 ? "Seçili parçada düzenlenecek nokta yok."
                : !int.TryParse(index.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var value) || value<0 || value>=piece.Points.Count
                    ? $"0 ile {piece.Points.Count-1} arasında mevcut bir nokta indeksi girin."
                : SelectedIsMarked()
                    ? "Bu köşe işaretli; sayısal taşımada işaret köşeyle taşınır. Silmek için önce işareti kaldırın. İşaret fiziksel çentik değildir."
                : !IsNumber(px.Text,out _) || !IsNumber(py.Text,out _)
                    ? "Nokta eklemek veya taşımak için X ve Y koordinatlarına geçerli sayılar girin; silme bu alanları kullanmaz. Ondalık için virgül veya nokta kullanın."
                : LogicalCount(piece) <= (piece.Closed?3:2)
                    ? "Bu parçada daha az nokta bulunamaz; nokta ekleyebilirsiniz, ancak silemezsiniz." : "";
            if(pointHint.Text==message)return;
            pointHint.Text=message;AutomationProperties.SetName(pointHint,message);
            pointHint.Visibility=message.Length==0?Visibility.Collapsed:Visibility.Visible;
            if(message.Length>0 && System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(pointHint) is { } peer)
                peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
        index.TextChanged+=(_,_)=>UpdatePointHint();px.TextChanged+=(_,_)=>UpdatePointHint();py.TextChanged+=(_,_)=>UpdatePointHint();
        source.SelectionChanged+=(_,_)=>UpdatePointHint();UpdatePointHint();
        Note(panel,"İşaret yalnız seçili köşeyi belirtir; kesim derinliği, yönü veya makine çıktısı değildir. İşaretler proje sürümü 6 gerektirir. İşaretli parçada işaretsiz köşeler taşınabilir veya silinebilir, kenarlara nokta ve V çentik eklenebilir, kontur yönü ters çevrilebilir; işaretler korunur. İşaretli köşe sayısal olarak taşındığında işaretiyle birlikte taşınır; silmek için önce işareti kaldırın.");
        Button(panel,"Seçili noktaya işaret ekle",()=>SetVertexNotch(index.Text,true),()=>ValidIndex()&&!SelectedIsMarked());
        Button(panel,"Seçili noktanın işaretini kaldır",()=>SetVertexNotch(index.Text,false),()=>ValidIndex()&&SelectedIsMarked());
        Button(panel,"Nokta ekle",()=>VertexMoveTaskForVerification=InsertVertexAsync(index.Text,px.Text,py.Text),()=>ValidIndex()&&IsNumber(px.Text,out _)&&IsNumber(py.Text,out _));
        Note(panel,"Seçili noktadan sonraki kenarın tam ortasına nokta eklenebilir. Kapalı konturun son kenarı ilk noktaya uzanır; açık konturun son noktasından sonra kenar yoktur. Serileme kuralı varsa yeni nokta için yeniden gözden geçirin.");
        Button(panel,"Sonraki kenarın ortasına nokta ekle",()=>VertexMoveTaskForVerification=InsertEdgeMidpointAsync(index.Text),()=>TryIndex(out var value)&&source.SelectedItem is PatternPiece p&&(p.Closed || value<p.Points.Count-1));
        var edgeDistance=Input(panel,"Seçili noktadan mesafe · mm","10");
        edgeDistanceInput=edgeDistance;
        Note(panel,"Mesafe seçili noktadan sonraki kenar boyunca ölçülür. Sayısal yuvarlama nedeniyle yeni noktanın eski kenardan ve istenen mesafeden sapması en fazla 0,000001 mm olabilir. Serileme kurallarını yeniden gözden geçirin.");
        Button(panel,"Mesafede nokta ekle",()=>VertexMoveTaskForVerification=InsertEdgeDistanceAsync(index.Text,edgeDistance.Text),()=>TryIndex(out var value)&&IsNumber(edgeDistance.Text,out _)&&source.SelectedItem is PatternPiece p&&(p.Closed || value<p.Points.Count-1));
        var edgePercentage=Input(panel,"Seçili noktadan kenar yüzdesi · %","50");
        edgePercentageInput=edgePercentage;
        Note(panel,"Yüzde seçili noktadan sonraki kenar boyunca ölçülür; 0 ile 100 arasında olmalıdır. Yeni noktanın kenardan ve hesaplanan mesafeden sapması en fazla 0,000001 mm olabilir. Serileme kurallarını yeniden gözden geçirin.");
        Button(panel,"Yüzdede nokta ekle",()=>VertexMoveTaskForVerification=InsertEdgePercentageAsync(index.Text,edgePercentage.Text),()=>TryIndex(out var value)&&IsNumber(edgePercentage.Text,out var percentage)&&percentage>0&&percentage<100&&source.SelectedItem is PatternPiece p&&(p.Closed || value<p.Points.Count-1));
        Note(panel,"Seçili noktadan sonraki kenarda konturdan fiziksel olarak içe doğru V kesilir; bu işlem yalnızca işaret eklemez. Konum 0 ile 1 arasındaki kenar oranıdır; ağız genişliği ve derinlik ayrı ölçülerdir. Konturu ve serileme kurallarını kontrol edin.");
        var notchFraction=Input(panel,"Çentik konumu · kenar oranı","0,5");
        var notchWidth=Input(panel,"Çentik ağız genişliği · mm","6");
        var notchDepth=Input(panel,"Çentik derinliği · mm","3");
        notchFractionInput=notchFraction;notchDepthInput=notchDepth;notchWidthInput=notchWidth;
        Button(panel,"Sonraki kenara V çentik aç",()=>VertexMoveTaskForVerification=InsertNotchAsync(index.Text,notchFraction.Text,notchWidth.Text,notchDepth.Text),()=>ValidIndex()&&source.SelectedItem is PatternPiece { Closed:true }&&IsNumber(notchFraction.Text,out _)&&IsNumber(notchDepth.Text,out _)&&IsNumber(notchWidth.Text,out _));
        Note(panel,"Açık konturun başlangıç ve bitiş yönünü ters çevirebilirsiniz. Noktaların konumu değişmez. Eşleşen kayıtlı serileme artışları noktalarla birlikte ters çevrilir; uygulanmamış taslaklar korunur.");
        Button(panel,"Açık kontur yönünü ters çevir",()=>VertexMoveTaskForVerification=ReverseOpenPathAsync(),()=>source.SelectedItem is PatternPiece { Closed:false });
        Note(panel,"Kapalı konturun sonundaki birden fazla aynı kapanış kopyası bir kopyaya indirilebilir. İşaretli parçalar ve uygulanmamış serileme taslakları reddedilir; kayıtlı artışlar yalnız tam eşleşiyorsa korunur; kaynak dosya değiştirilmez. Kaynak ve pastal geçmişi önceki durumu anlatmaya devam eder. Diğer kontur hataları kalabilir; dışa aktarım veya üretim uygunluğu doğrulanmaz.");
        Button(panel,"Fazla kapanış noktalarını temizle",()=>VertexMoveTaskForVerification=CleanExtraClosureAsync(),()=>source.SelectedItem is PatternPiece { Closed:true });
        Note(panel,"Bitişik eş nokta kopyaları ayrı bir işlemle temizlenebilir; başka geometri hataları kalabilir ve üretim uygunluğu doğrulanmaz. İşaretler yalnız benzersiz ve bitçe tam eşlenebiliyorsa korunur; kayıtlı serileme kuralı yalnız tam eşleşirse korunur; uygulanmamış taslaklar reddedilir.");
        Button(panel,"Bitişik eş noktaları temizle",()=>VertexMoveTaskForVerification=CleanConsecutiveAsync(),()=>HasPiece());
        Button(panel,"Projedeki tekrarları topluca incele…",()=>
        {
            if(!CommitGradeEdit()) { ShowGradeEditError();return; }
            var chosen=source.SelectedItem is PatternPiece selected ? selected.Id : (Guid?)null;
            new BulkDuplicateCleanupWindow(workspace,this,HasSnapshotGradeDraft,ResetCleanGradeDrafts).ShowDialog();
            RefreshSources(chosen);
        },()=>HasPiece());
        Note(panel,"Kapalı konturun yönünü ters çevirin; ilk nokta ve şekil korunur. Nokta indeksleri değişir. Eşleşen kayıtlı serileme artışları ilgili noktalarla birlikte taşınır; uygulanmamış taslaklar korunur.");
        Button(panel,"Kapalı kontur yönünü ters çevir",()=>VertexMoveTaskForVerification=ReverseClosedContourAsync(),()=>source.SelectedItem is PatternPiece { Closed:true });
        Note(panel,"Noktayı taşı, girilen indeksteki mevcut noktanın koordinatlarını değiştirir. Kapalı konturda yinelenen ilk ve son nokta birlikte taşınır. Doğrulama arka planda yapılır; sonucun uygulanmasından önce proje yeniden kontrol edilir.");
        Button(panel,"Seçilen indeksteki noktayı taşı",()=>VertexMoveTaskForVerification=MoveVertexAsync(index.Text,px.Text,py.Text),()=>ValidIndex()&&IsNumber(px.Text,out _)&&IsNumber(py.Text,out _));
        Button(panel,"Seçilen indeksteki noktayı sil",()=>VertexMoveTaskForVerification=DeleteVertexAsync(index.Text),()=>ValidIndex()&&!SelectedIsMarked()&&source.SelectedItem is PatternPiece p&&LogicalCount(p)>(p.Closed?3:2));return Scroll(panel);
    }
    private enum VertexEdit { Move, Insert, Delete, Midpoint, Reverse, ReverseClosed, Notch, Distance, Percentage, CleanClosure, CleanConsecutive }
    internal void SetVertexNotch(string indexText,bool add)
    {
        try
        {
            if(toolReviewRequired || vertexMoveBusy || workspace.HasPendingCanvasOrPropertyWork)
                throw new InvalidOperationException("Önce bekleyen çizimi veya düzenlemeyi tamamlayın.");
            var piece=Piece();
            var document=workspace.Document;
            if(document.Pieces.Count(p=>p.Id==piece.Id)!=1 || !ReferenceEquals(document.Pieces.FirstOrDefault(p=>p.Id==piece.Id),piece))
                throw new InvalidOperationException("Seçili parça güncel projeye ait değil. Atölyeyi yeniden açıp parçayı seçin.");
            if(!int.TryParse(indexText,NumberStyles.Integer,CultureInfo.InvariantCulture,out var index) || index<0 || index>=piece.Points.Count)
                throw new ArgumentException("Geçerli bir nokta indeksi girin.");
            if(piece.Closed && piece.Points.Count>1 && index==piece.Points.Count-1 && piece.Points[0]==piece.Points[^1]) index=0;
            var existing=piece.Notches?.Any(n=>n.VertexIndex==index)==true;
            if(existing==add) { result.Foreground=StudioTheme.Muted;result.Text=add?"Bu köşede zaten işaret var.":"Bu köşede işaret yok.";return; }
            if(add && ((piece.Notches?.Count ?? 0)>=10_000 || workspace.Document.Pieces.Sum(p=>(long)(p.Notches?.Count ?? 0))>=200_000))
                throw new InvalidOperationException("İşaret sayısı sınırına ulaşıldı.");
            var updated=piece.Notches?.Select(n=>new NotchMark { VertexIndex=n.VertexIndex, Position=n.Position }).ToList() ?? [];
            if(add) updated.Add(new NotchMark { VertexIndex=index,Position=piece.Points[index] });
            else updated.RemoveAll(n=>n.VertexIndex==index);
            var next=updated.Count==0 ? null : updated;
            CommitToolChange(add?"Köşe işareti eklendi":"Köşe işareti kaldırıldı",()=>piece.Notches=next,()=>
            { RefreshSources(piece.Id);workspace.Select(piece.Id);Success(add?"Köşe işareti eklendi; değişiklik geri alınabilir. Kesim derinliği veya yönü tanımlanmadı.":"Köşe işareti kaldırıldı; değişiklik geri alınabilir."); });
        }
        catch(Exception ex) { result.Foreground=StudioTheme.Danger;result.Text="İşaret değiştirilemedi: "+ex.Message; }
    }

    internal Task MoveVertexAsync(string indexText,string xText,string yText) => EditVertexAsync(VertexEdit.Move,indexText,xText,yText);
    internal Task InsertVertexAsync(string indexText,string xText,string yText) => EditVertexAsync(VertexEdit.Insert,indexText,xText,yText);
    internal Task DeleteVertexAsync(string indexText) => EditVertexAsync(VertexEdit.Delete,indexText,null,null);
    internal Task InsertEdgeMidpointAsync(string indexText) => EditVertexAsync(VertexEdit.Midpoint,indexText,null,null);
    internal Task InsertEdgeDistanceAsync(string indexText,string distanceText) => EditVertexAsync(VertexEdit.Distance,indexText,distanceText,"0");
    internal Task InsertEdgePercentageAsync(string indexText,string percentageText) => EditVertexAsync(VertexEdit.Percentage,indexText,percentageText,"0");
    internal Task InsertNotchAsync(string indexText,string fractionText,string depthText) => EditVertexAsync(VertexEdit.Notch,indexText,fractionText,depthText);
    internal Task InsertNotchAsync(string indexText,string fractionText,string widthText,string depthText) => EditVertexAsync(VertexEdit.Notch,indexText,fractionText,depthText,widthText);
    internal Task CleanExtraClosureAsync() => EditVertexAsync(VertexEdit.CleanClosure,"0",null,null);
    internal Task CleanConsecutiveAsync() => EditVertexAsync(VertexEdit.CleanConsecutive,"0",null,null);
    internal Task ReverseOpenPathAsync() => EditVertexAsync(VertexEdit.Reverse,"0",null,null);
    internal Task ReverseClosedContourAsync() => EditVertexAsync(VertexEdit.ReverseClosed,"0",null,null);
    private async Task EditVertexAsync(VertexEdit operation,string indexText,string? xText,string? yText,string? widthText=null)
    {
        if(vertexMoveBusy || basicNestingBusy || gradeBusy || offsetBusy || toolReviewRequired)return;
        var started=false;
        try
        {
            if(!CommitGradeEdit())throw new ArgumentException("Serileme tablosundaki hücreyi tamamlayın.");
            if(workspace.HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("Önce tamamlanmamış çizimi, sürüklemeyi veya parça alan taslaklarını tamamlayın ya da iptal edin.");
            if(!int.TryParse(indexText,NumberStyles.Integer,CultureInfo.InvariantCulture,out var index))throw new ArgumentException("Nokta indeksi tam sayı olmalıdır.");
            var position=operation is VertexEdit.Delete or VertexEdit.Midpoint or VertexEdit.Reverse or VertexEdit.ReverseClosed or VertexEdit.CleanClosure or VertexEdit.CleanConsecutive ? default : new Point2(Number(xText!),Number(yText!));
            var width=widthText is null ? position.Y * 2 : Number(widthText);
            var selected=Piece();var sourceId=selected.Id;
            var document=workspace.Document;var original=document.Clone();var snapshot=selected.Clone();var workspaceSelection=workspace.SelectedId;
            if(!ReferenceEquals(document.Pieces.FirstOrDefault(piece=>piece.Id==sourceId),selected))
                throw new InvalidOperationException("Kaynak parça güncel değil. Atölyeyi yeniden açıp parçayı seçin.");
            var inputIndex=vertexIndexInput?.Text;var inputX=vertexXInput?.Text;var inputY=vertexYInput?.Text;var inputFraction=notchFractionInput?.Text;var inputDepth=notchDepthInput?.Text;var inputWidth=notchWidthInput?.Text;var inputDistance=edgeDistanceInput?.Text;var inputPercentage=edgePercentageInput?.Text;
            if(index<0 || index>=snapshot.Points.Count)throw new ArgumentOutOfRangeException(nameof(index),"Nokta indeksi kaynak parçada bulunmuyor.");
            if(operation==VertexEdit.Move && BitConverter.DoubleToInt64Bits(snapshot.Points[index].X)==BitConverter.DoubleToInt64Bits(position.X) &&
               BitConverter.DoubleToInt64Bits(snapshot.Points[index].Y)==BitConverter.DoubleToInt64Bits(position.Y))
            { result.Foreground=StudioTheme.Muted;result.Text="Nokta zaten bu konumda; proje ve işlem geçmişi değişmedi.";return; }
            bool ClosureHasUnsafeDependencies()=>(operation==VertexEdit.CleanClosure && snapshot.Notches is not null) ||
                (gradeDrafts.TryGetValue(sourceId,out var pendingDraft) && HasUnsavedGradeDraft(sourceId,pendingDraft));
            if((operation is VertexEdit.CleanClosure or VertexEdit.CleanConsecutive) && ClosureHasUnsafeDependencies())
                throw new InvalidOperationException("İşaret veya serileme bilgisi bu işlemle güvenle korunamıyor; tekrarlı noktalar temizlenmedi.");
            var before=BeforeVertexMoveForVerification;var after=AfterVertexMoveForVerification;
            vertexMoveCancellation=new CancellationTokenSource();var token=vertexMoveCancellation.Token;
            vertexMoveBusy=true;started=true;source.IsEnabled=false;grades.IsEnabled=false;
            if(workshopTabs is not null)workshopTabs.IsEnabled=false;
            cancelVertexMove.Visibility=Visibility.Visible;cancelVertexMove.IsEnabled=true;RefreshCommands();
            result.Foreground=StudioTheme.Muted;result.Text="Nokta doğrulanıyor. İptal isteyebilirsiniz; proje henüz değiştirilmedi.";
            var edited=await Task.Run(()=>
            {
                before?.Invoke();token.ThrowIfCancellationRequested();
                var value=operation switch
                {
                    VertexEdit.Move => PatternTools.MovePoint(snapshot,index,position,token,allowMarkedVertexMove:true),
                    VertexEdit.CleanClosure => PatternTools.RemoveExtraClosingVertices(snapshot,token),
                    VertexEdit.CleanConsecutive => PatternTools.RemoveConsecutiveDuplicateVertices(snapshot,token),
                    VertexEdit.Insert => PatternTools.InsertPoint(snapshot,index,position,token),
                    VertexEdit.Delete => PatternTools.DeletePoint(snapshot,index,token),
                    VertexEdit.Midpoint => PatternTools.InsertEdgeMidpoint(snapshot,index,token),
                    VertexEdit.Distance => PatternTools.InsertPointAtDistance(snapshot,index,position.X,token),
                    VertexEdit.Percentage => PatternTools.InsertPointAtPercentage(snapshot,index,position.X,token),
                    VertexEdit.Reverse => PatternTools.ReverseOpenPath(snapshot,token),
                    VertexEdit.ReverseClosed => PatternTools.ReverseClosedContour(snapshot,token),
                    VertexEdit.Notch => PatternTools.InsertNotch(snapshot,index,position.X,width,position.Y,token),
                    _ => throw new InvalidOperationException("Bilinmeyen nokta düzenleme işlemi.")
                };
                after?.Invoke();token.ThrowIfCancellationRequested();return value;
            });
            if(windowClosed)return;
            token.ThrowIfCancellationRequested();
            if(workspace.HasPendingCanvasOrPropertyWork || !ReferenceEquals(workspace.Document,document) ||
               !DocumentSession.ContentEquals(workspace.Document,original) || workspace.SelectedId!=workspaceSelection ||
               source.SelectedItem is not PatternPiece selectedNow || selectedNow.Id!=sourceId ||
               !ReferenceEquals(document.Pieces.FirstOrDefault(piece=>piece.Id==sourceId),selectedNow) ||
               vertexIndexInput?.Text!=inputIndex || vertexXInput?.Text!=inputX || vertexYInput?.Text!=inputY || notchFractionInput?.Text!=inputFraction || notchDepthInput?.Text!=inputDepth || notchWidthInput?.Text!=inputWidth || edgeDistanceInput?.Text!=inputDistance || edgePercentageInput?.Text!=inputPercentage)
                throw new InvalidOperationException("Proje, seçim veya bekleyen çalışma değişti. Nokta düzenlemesi uygulanmadı; güncel durumu kontrol edin.");
            token.ThrowIfCancellationRequested();
            if((operation is VertexEdit.CleanClosure or VertexEdit.CleanConsecutive) && ClosureHasUnsafeDependencies())
                throw new InvalidOperationException("İşaret veya serileme bilgisi değişti; kapanış temizlenmedi.");
            GradingRuleSet? cleanedRule=null;
            if(operation is VertexEdit.CleanClosure or VertexEdit.CleanConsecutive)
            {
                var savedRule=document.GradingRules.FirstOrDefault(rule=>rule.SourcePieceId==sourceId);
                if(savedRule is not null)
                {
                    cleanedRule=savedRule.WithDuplicateRemovalFor(snapshot,edited,token);
                    if(cleanedRule is null)throw new InvalidOperationException("Kayıtlı serileme kuralı kaynakla veya birleştirilen nokta artışlarıyla bitçe eşleşmiyor; temizlik uygulanmadı.");
                }
            }
            ReplacePiece(edited,operation is VertexEdit.Reverse or VertexEdit.ReverseClosed,
                operation is VertexEdit.Midpoint or VertexEdit.Distance or VertexEdit.Percentage ? index : null,
                operation is VertexEdit.Distance or VertexEdit.Percentage,operation is VertexEdit.Insert or VertexEdit.Midpoint or VertexEdit.Distance or VertexEdit.Percentage or VertexEdit.Notch ? index : null,operation==VertexEdit.Delete ? index : null,operation==VertexEdit.Move ? index : null,operation is VertexEdit.CleanClosure or VertexEdit.CleanConsecutive,cleanedRule);
        }
        catch(OperationCanceledException) when(vertexMoveCancellation?.IsCancellationRequested==true)
        { if(!windowClosed) { result.Foreground=StudioTheme.Warning;result.Text="Nokta düzenleme doğrulaması iptal edildi; proje değiştirilmedi."; } }
        catch(Exception ex)
        { if(!windowClosed) { result.Foreground=StudioTheme.Danger;result.Text="Nokta düzenlemesi uygulanmadı: "+ex.Message; } }
        finally
        {
            if(started)
            {
                vertexMoveBusy=false;vertexMoveCancellation?.Dispose();vertexMoveCancellation=null;
                cancelVertexMove.Visibility=Visibility.Collapsed;cancelVertexMove.IsEnabled=false;
                if(!windowClosed) { source.IsEnabled=true;grades.IsEnabled=true;if(workshopTabs is not null)workshopTabs.IsEnabled=true;RefreshCommands(); }
            }
        }
    }
    private async Task CreateOffsetAsync(string allowanceText)
    {
        if(offsetBusy || gradeBusy || basicNestingBusy || vertexMoveBusy || toolReviewRequired)return;
        if(offsetReviewRequired)
        {
            if(!windowClosed) { result.Foreground=StudioTheme.Warning;result.Text="Önce eklenmiş dikiş payı parçasını ana çalışma alanında kontrol edin. Yeniden hesaplamak için atölyeyi kapatıp açın."; }
            return;
        }
        showUnverifiedOffsetPreview=null; unverifiedOffsetPreview.Visibility=Visibility.Collapsed;
        var offsetCommitted=false;
        Guid? committedPieceId=null;
        Guid? sourcePieceId=null;
        try
        {
            // Capture all mutable input on the dispatcher; the geometry worker never touches WPF or the live document.
            if(workspace.HasPendingCanvasOrPropertyWork)
                throw new InvalidOperationException("Önce tamamlanmamış çizimi, sürüklemeyi veya parça alan taslaklarını tamamlayın ya da iptal edin.");
            var selected=Piece();
            var originalDocument=workspace.Document;
            var originalContent=originalDocument.Clone();
            var snapshot=selected.Clone();
            // Preserve the actual control state independently of the method's
            // input argument, which can also be supplied by verification code.
            var originalAllowanceText=offsetAllowance?.Text;
            sourcePieceId=snapshot.Id;
            offsetSourceDescription=$"İşlem kaynağı: {SafeSourceName(snapshot.Name)} · Kimlik: {snapshot.Id:D}";
            var allowance=Number(allowanceText);
            var requestedAllowance=MeasurementInput.Format(allowance);
            // Invalid amounts still go to the worker's usual validation path.
            var gridAllowance=allowance>=0.000001 && allowance<=100000
                ? MeasurementInput.Format(PatternTools.QuantizeAllowance(allowance)) : null;
            var allowanceDescription=$"İstenen dikiş payı: {requestedAllowance} mm"+
                (gridAllowance is null ? "" : $"; hesaplama parametresi: {gridAllowance} mm (ızgara değeri)");
            offsetAllowanceDescription=allowanceDescription;
            offsetCancellation=new CancellationTokenSource();var token=offsetCancellation.Token;
            var beforeOffset=BeforeOffsetForVerification;var afterOffset=AfterOffsetBeforeReturnForVerification;
            offsetBusy=true;source.IsEnabled=false;if(offsetAllowance is not null)offsetAllowance.IsEnabled=false;
            cancelOffset.Visibility=Visibility.Visible;cancelOffset.IsEnabled=true;RefreshCommands();
            result.Foreground=StudioTheme.Muted;
            result.Text=allowanceDescription+"; hesaplama sürüyor. İptal isteyebilirsiniz; atölyeyi kapatmak için işlemin bitmesini bekleyin. "+offsetSourceDescription+".";
            PatternPiece generated;
            try { generated=await Task.Run(()=> { beforeOffset?.Invoke();token.ThrowIfCancellationRequested();var value=PatternTools.Offset(snapshot,allowance,token);afterOffset?.Invoke();token.ThrowIfCancellationRequested();return value; }); }
            catch(ArgumentException ex)
            {
                token.ThrowIfCancellationRequested();
                if(!windowClosed)
                {
                    result.Foreground=StudioTheme.Danger;
                    // This conservative check can also reject a valid contour.
                    // Give specific next steps without declaring the source bad
                    // or suggesting an unapproved change to the allowance.
                    var localDetailUnverified=ex is OffsetLocalDetailUnverifiedException;
                    var nextStep=localDetailUnverified
                        ? " Bu uyarı kaynak konturun kesin hatalı olduğu anlamına gelmez; bu sürüm ayrıntının korunduğunu doğrulayamadı. Ana çalışma alanında parçayı yakınlaştırıp dar girintileri ve konturu kaynak çizimle karşılaştırın; gerekirse üretim sorumlusuna danışın."
                        : " Ölçüyü ve konturu kontrol edin; seçilen payı doğrulatın.";
                    result.Text=$"Dikiş payı uygulanmadı ({allowanceDescription}): "+ex.Message+
                        " Bu hesaplama kaynak parçayı değiştirmedi."+nextStep+" Üretim payını yalnız üretim şartları izin veriyorsa değiştirin. "+offsetSourceDescription+".";
                    if(ex is OffsetLocalDetailUnverifiedException { Preview.IsDefaultOrEmpty: false } unverified)
                    {
                        var preview=unverified.Preview;
                        showUnverifiedOffsetPreview=()=>
                        {
                            if(windowClosed || workspace.HasPendingCanvasOrPropertyWork ||
                               !ReferenceEquals(workspace.Document,originalDocument) || !DocumentSession.ContentEquals(workspace.Document,originalContent) ||
                               offsetAllowance?.Text!=originalAllowanceText || source.SelectedItem is not PatternPiece selectedAgain || selectedAgain.Id!=snapshot.Id)
                            {
                                showUnverifiedOffsetPreview=null; unverifiedOffsetPreview.Visibility=Visibility.Collapsed;
                                result.Foreground=StudioTheme.Warning;
                                result.Text="Proje, seçim veya dikiş payı değişti; eski önizleme gösterilmedi. Güncel konturla yeniden hesaplayın.";
                                return;
                            }
                            new OffsetUnverifiedPreviewWindow(this,snapshot.Points,preview).ShowDialog();
                        };
                        unverifiedOffsetPreview.Visibility=Visibility.Visible;
                    }                }
                return;
            }
            if(windowClosed)return;
            token.ThrowIfCancellationRequested();
            var current=workspace.Document.Pieces.FirstOrDefault(p=>p.Id==snapshot.Id);
            if(workspace.HasPendingCanvasOrPropertyWork || offsetAllowance?.Text!=originalAllowanceText ||
               !ReferenceEquals(workspace.Document,originalDocument) || !DocumentSession.ContentEquals(workspace.Document,originalContent) || source.SelectedItem is not PatternPiece selectedNow || selectedNow.Id!=snapshot.Id || current is null || current.Name!=snapshot.Name || current.Closed!=snapshot.Closed || !current.Points.SequenceEqual(snapshot.Points))
            {
                result.Foreground=StudioTheme.Warning;
                result.Text=$"Proje, bekleyen çalışma veya dikiş payı girişi hesaplama sırasında değişti; sonuç eklenmedi. {allowanceDescription}. Kaynağı ve girişleri kontrol edip yeniden deneyin. {offsetSourceDescription}.";
                return;
            }
            token.ThrowIfCancellationRequested();
            if(snapshot.Grainline is { } sourceGrain)
            {
                if(generated.Grainline is not { } generatedGrain || generatedGrain.Start!=sourceGrain.Start ||
                   generatedGrain.End!=sourceGrain.End || !generatedGrain.NeedsReview)
                    throw new InvalidOperationException("Yeni parçanın iplik yönü beklenen şekilde korunamadı; sonuç projeye eklenmedi.");
            }
            else if(generated.Grainline is not null)
                throw new InvalidOperationException("Yeni parçada beklenmeyen iplik yönü bulundu; sonuç projeye eklenmedi.");
            var grainDetail=generated.Grainline is { } line
                ? $" Yeni parçanın iplik yönü aynı koordinatlarda korundu: başlangıç X {MeasurementInput.Format(line.Start.X)}, Y {MeasurementInput.Format(line.Start.Y)} mm; bitiş X {MeasurementInput.Format(line.End.X)}, Y {MeasurementInput.Format(line.End.Y)} mm. Yeni konturla ilişkisini yeniden kontrol edin. Bu işaret fiziksel üretim uygunluğunu doğrulamaz."
                : "";
            AddGenerated(generated,detail:$" {allowanceDescription}. Bu değer hesaplama parametresidir; fiziksel ölçüm değildir. {offsetSourceDescription}."+grainDetail,
                notice:"Dikiş payı deneyseldir; üretimden önce oluşan konturu ve ölçüleri kontrol edin. ",
                onCommitted:()=> { offsetCommitted=true;committedPieceId=generated.Id; });
        }
        catch(OperationCanceledException) when(!offsetCommitted && offsetCancellation?.IsCancellationRequested==true)
        { if(!windowClosed) { result.Foreground=StudioTheme.Warning;result.Text="Dikiş payı hesabı iptal edildi; sonuç projeye eklenmedi."+(offsetAllowanceDescription is null ? "" : " "+offsetAllowanceDescription+".")+(offsetSourceDescription is null ? "" : " "+offsetSourceDescription+"."); } }
        catch(Exception ex)
        {
            if(offsetCommitted)offsetReviewRequired=true;
            if(!windowClosed)
            {
                result.Foreground=StudioTheme.Danger;
                result.Text=offsetCommitted
                    ? $"Dikiş payı parçası projeye eklendi (kimlik: {committedPieceId:D}; kaynak: {sourcePieceId:D}); görünüm yenilenemedi. Ana çalışma alanını ve geri alma geçmişini kontrol edin. Tekrar hesaplamak için atölyeyi kapatıp açın: {ex.Message} {offsetSourceDescription}."
                    : "Dikiş payı işlemi tamamlanamadı: "+ex.Message+" Projedeki parçaları kontrol edin."+(offsetSourceDescription is null ? "" : " "+offsetSourceDescription+".");
            }
        }
        finally
        {
            offsetBusy=false;offsetCancellation?.Dispose();offsetCancellation=null;offsetAllowanceDescription=null;offsetSourceDescription=null;cancelOffset.Visibility=Visibility.Collapsed;cancelOffset.IsEnabled=false;
            if(!windowClosed) { source.IsEnabled=true;if(offsetAllowance is not null)offsetAllowance.IsEnabled=true;RefreshCommands(); }
        }
    }
    private void AddGenerated(PatternPiece generated,bool keepBaseSource=false,string? detail=null,string? notice=null,Action? onCommitted=null)
    {
        var sourcePiece=source.SelectedItem as PatternPiece; var baseId=sourcePiece?.Id; var metadataNotCopied=sourcePiece?.Metadata is not null && generated.Metadata is null;
        workspace.CommitObserved("Kalıp aracı uygulandı",()=>workspace.Document.Pieces.Add(generated),onCommitted);workspace.Select(generated.Id);workspace.FitView();RefreshSources(keepBaseSource?baseId:generated.Id);
        Success(notice+generated.Name+" projeye eklendi. Kaynak parça korundu."+(metadataNotCopied ? " Kaynağın rol, beden, malzeme ve adet bilgileri yeni parçaya aktarılmadı; gerekiyorsa Parça bilgileri bölümünde girin." : "")+detail);
    }
    private void ReplacePiece(PatternPiece edited,bool reverseOpenPath=false,int? midpointIndex=null,bool fractionalSplit=false,int? insertedAfter=null,int? deletedIndex=null,int? movedIndex=null,bool closureCleanup=false,GradingRuleSet? cleanedRule=null)
    {
        if(toolReviewRequired) { ReportToolReview("Son işlem",null);return; }
        var previous=Piece();var index=workspace.Document.Pieces.FindIndex(p=>p.Id==previous.Id);
        var normalizedInsertion=insertedAfter is { } selectedIndex && previous.Closed && previous.Points.Count>1 && previous.Points[0]==previous.Points[^1] && selectedIndex==previous.Points.Count-1 ? 0 : insertedAfter;
        var insertedCount=edited.Points.Count-previous.Points.Count;
        if(insertedAfter is not null && (insertedAfter<0 || insertedAfter>=previous.Points.Count || insertedCount is not (1 or 3)))
            throw new InvalidOperationException("Eklenen nokta kaynakla eşleşmedi; kontur değiştirilmedi.");
        var normalizedDeletion=deletedIndex is { } deleteAt && previous.Closed && previous.Points.Count>1 && previous.Points[0]==previous.Points[^1] && deleteAt==previous.Points.Count-1 ? 0 : deletedIndex;
        var normalizedMove=movedIndex is { } moveAt && previous.Closed && previous.Points.Count>1 && previous.Points[0]==previous.Points[^1] && moveAt==previous.Points.Count-1 ? 0 : movedIndex;
        if(movedIndex is not null && (movedIndex<0 || movedIndex>=previous.Points.Count || edited.Points.Count!=previous.Points.Count))
            throw new InvalidOperationException("Taşınan nokta kaynakla eşleşmedi; kontur değiştirilmedi.");
        var logicalCount=previous.Points.Count-(previous.Closed && previous.Points.Count>1 && previous.Points[0]==previous.Points[^1] ? 1 : 0);
        static bool SamePointBits(Point2 a,Point2 b)=>BitConverter.DoubleToInt64Bits(a.X)==BitConverter.DoubleToInt64Bits(b.X) &&
            BitConverter.DoubleToInt64Bits(a.Y)==BitConverter.DoubleToInt64Bits(b.Y);
        int[]? cleanupMap=null;
        if(closureCleanup && previous.Notches is not null)
        {
            cleanupMap=new int[previous.Points.Count];var output=-1;
            for(var i=0;i<previous.Points.Count;i++)
            {
                var point=previous.Points[i];
                if(i==0 || previous.Points[i-1]!=point)output++;
                else if(!SamePointBits(previous.Points[i-1],point))throw new InvalidOperationException("Tekrar eden noktanın bitleri farklı; işaretler eşlenmedi.");
                if(output>=edited.Points.Count || !SamePointBits(point,edited.Points[output]))throw new InvalidOperationException("Temizlenmiş geometri kaynakla eşleşmedi.");
                cleanupMap[i]=output;
            }
            if(output+1!=edited.Points.Count)throw new InvalidOperationException("Temizlenmiş nokta sayısı kaynakla eşleşmedi.");
            if(previous.Closed && edited.Points.Count>1 && edited.Points[0]==edited.Points[^1])
                for(var i=0;i<cleanupMap.Length;i++)if(cleanupMap[i]==edited.Points.Count-1)cleanupMap[i]=0;
        }
        int MappedMarkIndex(int oldIndex) => cleanupMap is not null ? cleanupMap[oldIndex] : reverseOpenPath
            ? previous.Closed ? (oldIndex==0 ? 0 : logicalCount-oldIndex) : previous.Points.Count-1-oldIndex
            : normalizedInsertion is { } at && oldIndex>at ? oldIndex+insertedCount : normalizedDeletion is { } deleted && oldIndex>deleted ? oldIndex-1 : oldIndex;
        if(deletedIndex is not null && (deletedIndex<0 || deletedIndex>=previous.Points.Count || edited.Points.Count!=previous.Points.Count-1))
            throw new InvalidOperationException("Silinen nokta kaynakla eşleşmedi; kontur değiştirilmedi.");
        bool MovedMarkBitsMatch(NotchMark oldMark,NotchMark newMark)
        {
            var before=previous.Points[oldMark.VertexIndex];var after=edited.Points[newMark.VertexIndex];
            static double Axis(double source,double target,double mark) => BitConverter.DoubleToInt64Bits(source)==BitConverter.DoubleToInt64Bits(target) ? mark : target;
            return SamePointBits(newMark.Position,new Point2(Axis(before.X,after.X,oldMark.Position.X),Axis(before.Y,after.Y,oldMark.Position.Y)));
        }
        if(previous.Notches is { } marks && (edited.Notches is null || edited.Notches.Count!=marks.Count ||
           !marks.Zip(edited.Notches).All(pair=>(normalizedDeletion is not { } removed || pair.First.VertexIndex!=removed) && pair.Second.VertexIndex==MappedMarkIndex(pair.First.VertexIndex) && (normalizedMove==pair.First.VertexIndex || (closureCleanup ? SamePointBits(pair.First.Position,pair.Second.Position) : pair.First.Position==pair.Second.Position)) &&
               pair.Second.VertexIndex>=0 && pair.Second.VertexIndex<edited.Points.Count && edited.Points[pair.Second.VertexIndex]==pair.Second.Position &&
               ((normalizedMove!=pair.First.VertexIndex && !closureCleanup) || (closureCleanup ? SamePointBits(edited.Points[pair.Second.VertexIndex],pair.Second.Position) : MovedMarkBitsMatch(pair.First,pair.Second))))))
            throw new InvalidOperationException("Köşe işaretleri korunamadı; kontur değiştirilmedi.");
        if(index<0)throw new InvalidOperationException("Kaynak parça artık projede yok.");edited.Id=previous.Id;edited.Metadata=previous.Metadata?.Clone();
        edited.Grainline=previous.Grainline?.Clone();
        // Reversal changes only vertex order, not the grainline's relationship to the contour.
        var grainNeedsReview=edited.Grainline is not null && !reverseOpenPath && (edited.Closed!=previous.Closed || !edited.Points.SequenceEqual(previous.Points));
        if(grainNeedsReview)edited.Grainline!.NeedsReview=true;
        var rule=workspace.Document.GradingRules.FirstOrDefault(r=>r.SourcePieceId==previous.Id);
        var hasDraft=movedIndex.HasValue ? HasSnapshotGradeDraft(previous,rule) : gradeDrafts.TryGetValue(previous.Id,out var draft) && HasUnsavedGradeDraft(previous.Id,draft);
        var reversedRule=!hasDraft ? (cleanedRule ?? (reverseOpenPath ? (previous.Closed ? rule?.ReversedClosedFor(previous) : rule?.ReversedFor(previous)) : midpointIndex is { } midpoint ? (fractionalSplit ? rule?.WithEdgeSplitFor(previous,edited,midpoint) : rule?.WithEdgeMidpointFor(previous,edited,midpoint)) : deletedIndex is { } deleted ? rule?.WithVertexDeletionFor(previous,edited,deleted) : movedIndex is { } moved ? rule?.WithVertexMoveFor(previous,edited.Points,moved) : null)) : null;
        var gradingNeedsReview=(edited.Closed!=previous.Closed || edited.Points.Count!=previous.Points.Count || !edited.Points.Zip(previous.Points).All(pair=>SamePointBits(pair.First,pair.Second))) && rule is not null && reversedRule is null;
        CommitToolChange("Kontur noktası düzenlemesi",()=> {
            workspace.Document.Pieces[index]=edited;
            if(reversedRule is not null) { var ruleIndex=workspace.Document.GradingRules.IndexOf(rule!);workspace.Document.GradingRules[ruleIndex]=reversedRule; }
        },()=>
        { if(reversedRule is not null || closureCleanup)gradeDrafts.Remove(edited.Id);RefreshSources(edited.Id);workspace.Select(edited.Id);workspace.FitView();Success((closureCleanup ? "Tekrarlı nokta kopyaları temizlendi; diğer kontur hataları kalabilir. Dışa aktarım ve üretim uygunluğu doğrulanmadı. Kaynak dosya değiştirilmedi; kaynak ve pastal geçmişi önceki durumu anlatır." : "Kontur noktası düzenlendi ve doğrulandı.")+(grainNeedsReview ? " İplik yönü korundu; değişen konturla ilişkisini yeniden kontrol edin." : "")+(reversedRule is not null ? (midpointIndex.HasValue ? (fractionalSplit ? " Yeni noktanın serileme artışı kenar oranına göre hesaplandı; kontrol edin." : " Yeni noktanın serileme artışı komşu artışların ortalamasından oluşturuldu; kontrol edin.") : closureCleanup ? " Kayıtlı serileme kuralı eşleşen noktalara bitçe korunarak aktarıldı." : deletedIndex.HasValue ? " Silinen noktanın artışı çıkarıldı; diğer serileme artışları korundu." : movedIndex.HasValue ? " Kayıtlı serileme artışları taşınan noktalarla korundu; yeni bedenleri kontrol edin." : " Kayıtlı serileme artışları nokta sırasıyla ters çevrildi.") : "")+(gradingNeedsReview ? " Mevcut serileme kuralı önceki kontura aittir; yeniden gözden geçirin." : "")); });
    }
    private UIElement NestingPanel()
    {
        var panel=Panel(); Note(panel,"Kenar payı dört kenarın her birine uygulanır; sıfır seçilirse pay eklenmez. Boşluk yalnız parçalar arasındadır.");
        Button(panel,"Adetli pastal önizle…",()=>new ProductionNestingWindow(workspace,this).ShowDialog()); Heading(panel,"Kumaş üzerinde temel yerleşim");Note(panel,"Deterministik raf yöntemi, parçaların çevre kutularını sıralar ve parça yönlerini korur. En düşük fireyi garanti etmez. Tüm parçalar birer kez yerleştirilir; kayıtlı rol ve adet uygulanmaz. Projedeki tüm parçalar kapalı ve geçerli olmalıdır.");
        const string placementImpact="Bu işlem mevcut projedeki tüm parçaların konumlarını değiştirir. Geri alma geçmişi boyut sınırına tabidir; önemli çalışmalarınızı önce farklı adla kaydedin. Kaynak proje konumlarını korumak için Adetli pastal önizlemesini kullanın.";
        var impactNotice=new TextBlock { Text=placementImpact,TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12) };
        AutomationProperties.SetName(impactNotice,placementImpact);panel.Children.Add(impactNotice);
        basicWidth=Input(panel,"Kumaş eni · mm","1500");basicGap=Input(panel,"Parçalar arası en az boşluk · mm","5");basicMargin=Input(panel,"Her kenarda kumaş payı · mm","0");
        AutomationProperties.SetHelpText(basicMargin,"Aynı pay sol, sağ, başlangıç ve bitiş kenarlarına uygulanır. Sıfır kenar payı eklemez.");
        var arrange=Button(panel,"Tüm proje parçalarını yerleştir",()=> BasicNestingTaskForVerification=CalculateBasicNestingAsync(),
            ()=>workspace.Document.Pieces.Count>0&&workspace.Document.Pieces.All(p=>p.Closed)&&IsNumber(basicWidth.Text,out var w)&&w>0&&IsNumber(basicGap.Text,out var g)&&g>=0&&IsNumber(basicMargin.Text,out var m)&&m>=0);
        arrange.ToolTip=placementImpact;AutomationProperties.SetHelpText(arrange,placementImpact);return Scroll(panel);
    }
    internal async Task CalculateBasicNestingAsync()
    {
        if(basicNestingBusy || gradeBusy || offsetBusy || vertexMoveBusy || toolReviewRequired)return;
        var started=false;
        try
        {
            if(!CommitGradeEdit())throw new ArgumentException("Serileme tablosundaki hücreyi tamamlayın.");
            if(workspace.HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("Önce tamamlanmamış çizimi, sürüklemeyi veya parça alan taslaklarını tamamlayın ya da iptal edin.");
            var widthText=basicWidth.Text;var gapText=basicGap.Text;var marginText=basicMargin.Text;
            var width=Number(widthText);var gap=Number(gapText);var margin=Number(marginText);
            // A modal confirmation pumps the dispatcher. Its approval must bind
            // to the same document, selection and inputs that were displayed.
            var document=workspace.Document;var original=document.Clone();
            var selectedId=workspace.SelectedId;var sourceId=(source.SelectedItem as PatternPiece)?.Id;
            if(original.Pieces.Any(p=>p.Metadata is not null))
            {
                var accepted=ConfirmBasicNestingForVerification?.Invoke() ?? (!IsVisible || MessageBox.Show(this,"Bu işlem mevcut projedeki tüm parçaların konumlarını değiştirir. Geri alma geçmişi boyut sınırına tabidir; önemli çalışmalarınızı önce farklı adla kaydedin. Tüm parçalar birer kez yerleştirilir; kayıtlı rol ve adet dikkate alınmaz. Kaynak konumlarını korumak için Adetli pastal önizlemesini kullanın. Devam edilsin mi?","Pastal kapsamı",MessageBoxButton.YesNo,MessageBoxImage.Information)==MessageBoxResult.Yes);
                if(!accepted)return;
            }
            if(windowClosed || workspace.HasPendingCanvasOrPropertyWork || !ReferenceEquals(workspace.Document,document) ||
               !DocumentSession.ContentEquals(workspace.Document,original) || workspace.SelectedId!=selectedId ||
               (source.SelectedItem as PatternPiece)?.Id!=sourceId || basicWidth.Text!=widthText || basicGap.Text!=gapText || basicMargin.Text!=marginText)
                throw new InvalidOperationException("Onay sırasında proje, seçim veya pastal girdileri değişti. Hesap başlatılmadı; güncel durumu kontrol edin.");
            var before=BeforeBasicNestingForVerification;var after=AfterBasicNestingBeforeReturnForVerification;
            basicNestingCancellation=new CancellationTokenSource();var token=basicNestingCancellation.Token;
            basicNestingBusy=true;started=true;source.IsEnabled=false;grades.IsEnabled=false;
            if(workshopTabs is not null)workshopTabs.IsEnabled=false;
            cancelBasicNesting.Visibility=Visibility.Visible;cancelBasicNesting.IsEnabled=true;RefreshCommands();
            result.Foreground=StudioTheme.Muted;result.Text="Pastal hesaplanıyor. İptal isteyebilirsiniz; proje henüz değiştirilmedi.";
            var layout=await Task.Run(()=>
            {
                before?.Invoke();token.ThrowIfCancellationRequested();
                var computed=NestingEngine.Arrange(original.Pieces,width,gap,double.PositiveInfinity,margin,token);
                var sourceById=original.Pieces.ToDictionary(p=>p.Id);
                var placedById=computed.Layout.Pieces.ToDictionary(p=>p.Id);
                var rules=new List<GradingRuleSet>(original.GradingRules.Count);var stale=new List<Guid>();var movedRules=new List<Guid>();
                foreach(var existing in original.GradingRules)
                {
                    token.ThrowIfCancellationRequested();
                    var sourcePiece=sourceById[existing.SourcePieceId];var placed=placedById[existing.SourcePieceId];
                    var copy=existing.Clone();
                    if(!sourcePiece.Points.SequenceEqual(placed.Points))
                    {
                        if(existing.SourceClosed==sourcePiece.Closed && existing.Deltas.Count==sourcePiece.Points.Count &&
                           existing.SourcePoints.SequenceEqual(sourcePiece.Points) && GradingRuleSet.IsExactPointTranslation(sourcePiece.Points,placed.Points))
                        { copy.SourcePoints=[..placed.Points];movedRules.Add(sourcePiece.Id); }
                        else stale.Add(sourcePiece.Id);
                    }
                    rules.Add(copy);
                }
                after?.Invoke();token.ThrowIfCancellationRequested();return (Result:computed,Rules:rules,Stale:stale,Moved:movedRules);
            });
            if(windowClosed)return;
            token.ThrowIfCancellationRequested();
            if(workspace.HasPendingCanvasOrPropertyWork || !ReferenceEquals(workspace.Document,document) ||
               !DocumentSession.ContentEquals(workspace.Document,original) || workspace.SelectedId!=selectedId ||
               (source.SelectedItem as PatternPiece)?.Id!=sourceId || basicWidth.Text!=widthText || basicGap.Text!=gapText || basicMargin.Text!=marginText)
                throw new InvalidOperationException("Proje, seçim veya pastal girdileri değişti. Sonuç uygulanmadı; güncel durumla yeniden hesaplayın.");
            token.ThrowIfCancellationRequested();
            var candidate=original.Clone();candidate.Pieces=layout.Result.Layout.Pieces;candidate.GradingRules=layout.Rules;
            if(DocumentSession.ContentEquals(original,candidate))
            {
                Success($"Hesaplanan boy {MeasurementInput.Format(layout.Result.UsedLength)} mm  ·  Kullanım %{layout.Result.UtilizationPercent:0.##}  ·  Fire %{layout.Result.WastePercent:0.##}. Yerleşim zaten aynı; proje ve geri alma geçmişi değişmedi. Her parça 1 adet; rol ve kayıtlı adet uygulanmadı.");
                return;
            }
            var cleanDrafts=layout.Moved.Where(id=>gradeDrafts.TryGetValue(id,out var draft) && !HasUnsavedGradeDraft(id,draft)).ToArray();
            CommitToolChange("Raf pastal yerleşimi",()=> { workspace.Document.Pieces=layout.Result.Layout.Pieces;workspace.Document.GradingRules=layout.Rules; },()=>
            { foreach(var id in cleanDrafts)gradeDrafts.Remove(id);workspace.Select(null);workspace.FitView();RefreshSources(null);
              var staleNotice=layout.Stale.Count==0 ? "" : $" {layout.Stale.Count} parçada serileme kuralı eski konumda korundu; atölyede gözden geçirin. Kimlikler: "+string.Join(", ",layout.Stale.Take(3))+(layout.Stale.Count>3 ? " ve diğerleri." : ".");
              var markNotice=layout.Result.Layout.Pieces.Any(p=>p.Notches is { Count:>0 }) ? " Köşe işaretleri aynı indeksli yerleştirilmiş noktalarda korundu; fiziksel çentik değildir, konumlarını kontrol edin." : "";
              Success($"Hesaplanan boy {MeasurementInput.Format(layout.Result.UsedLength)} mm  ·  Kullanım %{layout.Result.UtilizationPercent:0.##}  ·  Fire %{layout.Result.WastePercent:0.##}. Hesap: kumaş eni × kullanılan uzunluk. Her parça 1 adet; rol ve kayıtlı adet uygulanmadı. Geri alma için atölyeyi kapatıp işlem geçmişini kontrol edin."+staleNotice+markNotice);
              if(layout.Stale.Count>0)result.Foreground=StudioTheme.Warning; });
        }
        catch(OperationCanceledException) when(basicNestingCancellation?.IsCancellationRequested==true)
        { if(!windowClosed) { result.Foreground=StudioTheme.Warning;result.Text="Pastal hesabı iptal edildi; proje ve girdiler korundu."; } }
        catch(Exception ex)
        { if(!windowClosed) { result.Foreground=StudioTheme.Danger;result.Text="Pastal uygulanmadı: "+ex.Message; } }
        finally
        {
            if(started)
            {
                basicNestingBusy=false;basicNestingCancellation?.Dispose();basicNestingCancellation=null;
                cancelBasicNesting.Visibility=Visibility.Collapsed;cancelBasicNesting.IsEnabled=false;
                if(!windowClosed) { source.IsEnabled=true;grades.IsEnabled=true;if(workshopTabs is not null)workshopTabs.IsEnabled=true;RefreshCommands(); }
            }
        }
    }
    public sealed class GradeRow : INotifyPropertyChanged
    {
        public int Index { get; set; } public double X { get; set; } public double Y { get; set; }
        private string dx="0",dy="0";
        public string Dx { get=>dx;set { if(dx==value)return;dx=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Dx))); } }
        public string Dy { get=>dy;set { if(dy==value)return;dy=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Dy))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
