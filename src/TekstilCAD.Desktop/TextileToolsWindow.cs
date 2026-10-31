using System.Collections.ObjectModel;
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
    private readonly ComboBox source = new() { MinWidth=200, Margin=new Thickness(0,4,0,0) };
    private readonly ObservableCollection<GradeRow> rows = [];
    private readonly DataGrid grades = new() { AutoGenerateColumns=false, CanUserAddRows=false, CanUserDeleteRows=false, MinHeight=80, RowHeight=34, ColumnHeaderHeight=32, Margin=new Thickness(0,6,0,8) };
    private readonly TextBlock sourceInfo = new() { TextWrapping=TextWrapping.Wrap, MaxHeight=44, TextTrimming=TextTrimming.CharacterEllipsis, VerticalAlignment=VerticalAlignment.Center };
    private readonly TextBox sourceIdentity = new() { IsReadOnly=true, TextWrapping=TextWrapping.Wrap, AcceptsReturn=true, MaxHeight=42, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, Margin=new Thickness(16,3,0,0) };
    private readonly TextBlock result = new() { TextWrapping=TextWrapping.Wrap, Text="Artışları girip yeni bedeni oluşturun veya diğer araç sekmelerini kullanın." };
    private readonly List<(Button Button,Func<bool> Available)> commands = [];
    private bool refreshingSource;
    private bool offsetBusy;
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

    private sealed record GradeDraft(List<Point2> Points, List<Point2> AnchorPoints, bool Closed, List<GradeRow> Rows, GradingRuleSet? BaselineRule);

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
        AutomationProperties.SetName(result,"Tekstil aracı işlem sonucu");
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
        Closed+=(_,_)=>windowClosed=true;
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
        if(draft.Closed==piece.Closed && !draft.Points.SequenceEqual(piece.Points) && IsTranslation(draft.AnchorPoints,piece.Points))
        {
            // A placement changes coordinates, not the per-point size increments.
            // Keep the original anchor across rebases so small shape changes cannot accumulate unnoticed.
            // Persistence changes on explicit Apply.
            draft=new([..piece.Points],draft.AnchorPoints,piece.Closed,draft.Rows.Select((row,index)=>new GradeRow {
                Index=index,X=piece.Points[index].X,Y=piece.Points[index].Y,Dx=row.Dx,Dy=row.Dy
            }).ToList(),draft.BaselineRule);
            gradeDrafts[piece.Id]=draft;
        }
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
            sourceInfo.Text="Geometri değişti · Önce artışları sıfırlayın.\n"+sourceInfo.Text;
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
        }).ToList(),saved.Clone());
    }
    private GradeDraft NewDraft(PatternPiece piece) => new([..piece.Points],[..piece.Points],piece.Closed,
        piece.Points.Select((point,index)=>new GradeRow { Index=index,X=point.X,Y=point.Y }).ToList(),
        workspace.Document.GradingRules.FirstOrDefault(r=>r.SourcePieceId==piece.Id)?.Clone());
    private bool SavedRuleMatchesDraft(Guid pieceId,GradeDraft draft)
    {
        var current=workspace.Document.GradingRules.FirstOrDefault(r=>r.SourcePieceId==pieceId);
        var baseline=draft.BaselineRule;
        return baseline is null ? current is null : current is not null && current.SourcePieceId==baseline.SourcePieceId &&
            current.SourceClosed==baseline.SourceClosed && current.SourcePoints.SequenceEqual(baseline.SourcePoints) && current.Deltas.SequenceEqual(baseline.Deltas);
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
        help.Content=new TextBlock { Text="ΔX / ΔY bir beden adımındaki mm değişimidir. Her beden aynı seçili temel parçadan hesaplanır. Artışları projeye uygulayın, ardından ana pencereden projeyi kaydedin. Uygulanmamış hücreler atölye kapanınca kaybolur.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Muted,Margin=new Thickness(0,6,0,6) };
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
        var metadataOptions=new Expander { Header="Yeni parça bilgileri · isteğe bağlı",IsExpanded=false,Margin=new Thickness(0,0,0,4) };
        var metadataPanel=new StackPanel();
        var includeSize=new CheckBox { Content="Yeni beden adını beden bilgisi olarak ekle",IsChecked=false,Margin=new Thickness(0,3,0,3) };
        var copyRoleMaterial=new CheckBox { Content="Kaynağın rol ve malzemesini kopyala",IsChecked=false,Margin=new Thickness(0,3,0,3) };
        AutomationProperties.SetName(includeSize,"Yeni beden adını beden bilgisi olarak ekle");
        AutomationProperties.SetName(copyRoleMaterial,"Kaynağın rol ve malzemesini kopyala");
        AutomationProperties.SetHelpText(copyRoleMaterial,"Yalnız mevcut rol ve malzeme kopyalanır; adet hiçbir zaman kopyalanmaz.");
        metadataPanel.Children.Add(includeSize);metadataPanel.Children.Add(copyRoleMaterial);
        metadataPanel.Children.Add(new TextBlock { Text="Adet aktarılmaz; adetli pastal için yeni parçada ayrıca girin.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Muted });
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
                AddGenerated(generated,true,onCommitted:()=>gradeCommitted=true);Success($"{generated.Name} oluşturuldu. Temel parça “{snapshot.Name}” ve artışlar korundu. {metadataNotice}{grainNotice}");
                if(generated.Grainline is not null)result.Foreground=StudioTheme.Warning;
                if(generated.Points.SequenceEqual(snapshot.Points))
                {
                    result.Foreground=StudioTheme.Warning;
                    result.Text=$"{generated.Name} eklendi; geometri temel parçayla aynı. Adım sayısı veya tüm artışlar sıfır olabilir. Ölçü değişikliği bekliyorsanız kuralları kontrol edin. {metadataNotice}{grainNotice}";
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
        var batch=Button(lower,"Birden fazla beden oluştur…",()=>
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
        batch.Margin=new Thickness(0,8,0,0);batch.HorizontalAlignment=HorizontalAlignment.Left;
        AutomationProperties.SetHelpText(batch,"Birden fazla beden ve adım girip hepsini inceleyin; onaylanınca tek işlem olarak eklenir.");
        return layout;
    }
    private bool HasUnsavedGradeDraft(Guid pieceId,GradeDraft draft)
    {
        var saved=workspace.Document.GradingRules.FirstOrDefault(r=>r.SourcePieceId==pieceId);
        if(saved is null)return draft.Rows.Any(row=>!IsNumber(row.Dx,out var dx)||dx!=0||!IsNumber(row.Dy,out var dy)||dy!=0);
        if(saved.SourceClosed!=draft.Closed||(!saved.SourcePoints.SequenceEqual(draft.Points)&&!IsTranslation(saved.SourcePoints,draft.Points))||saved.Deltas.Count!=draft.Rows.Count)return true;
        return draft.Rows.Select((row,index)=>!IsNumber(row.Dx,out var dx)||!IsNumber(row.Dy,out var dy)||dx!=saved.Deltas[index].X||dy!=saved.Deltas[index].Y).Any(changed=>changed);
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
        Button(panel,"İki nokta arasını ölç…",()=>new PointDistanceWindow(workspace,this,Piece(),()=> (source.SelectedItem as PatternPiece)?.Id).ShowDialog(),()=>HasPiece());
        Button(panel,"İki dikiş kenarını karşılaştır…",()=>new SeamLengthComparisonWindow(workspace,this,()=> (source.SelectedItem as PatternPiece)?.Id).ShowDialog(),()=>HasPiece());
        Button(panel,"Açık konturu kapat…",()=>
        {
            if(workspace.HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("Önce tamamlanmamış çizimi veya alan taslaklarını tamamlayın ya da iptal edin.");
            new OpenPathClosureWindow(workspace,this).ShowDialog();
            RefreshSources((source.SelectedItem as PatternPiece)?.Id);
        },()=>workspace.Document.Pieces.Any(p=>!p.Closed));
        Button(panel,"İki açık konturu birleştir…",()=>
        {
            if(workspace.HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("Önce tamamlanmamış çizimi veya alan taslaklarını tamamlayın ya da iptal edin.");
            var dialog=new OpenPathJoinWindow(workspace,this);
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
        panel.Children.Add(new Separator { Margin=new Thickness(0,18,0,18) });Heading(panel,"Kontur noktaları");Note(panel,"Nokta indeksleri 0'dan başlar. Değişiklik seçili parçaya uygulanır; ana çalışma alanında geri alınabilir. Tekrarlanan kapanış noktasında ilk ve son indeks aynı köşedir; bu köşe birlikte düzenlenir. Ekleme bu köşeden sonra yapılır.");
        var index=Input(panel,"Nokta indeksi / eklenecek noktanın önceki indeksi","0");var px=Input(panel,"Yeni nokta X · mm","0");var py=Input(panel,"Yeni nokta Y · mm","0");
        Button(panel,"Kontur noktası seç…",()=>
        {
            if(workspace.HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("Önce tamamlanmamış çizimi, sürüklemeyi veya parça alan taslaklarını tamamlayın ya da iptal edin.");
            var selected=Piece();var indexBefore=index.Text;var xBefore=px.Text;var yBefore=py.Text;
            var initial=int.TryParse(indexBefore,out var parsed) ? parsed : -1;
            var picker=new PointPickerWindow(workspace,this,selected,()=> (source.SelectedItem as PatternPiece)?.Id,initial);
            if(picker.ShowDialog()!=true)return;
            picker.CheckCurrent();
            if(picker.IsStale || picker.SelectedPointIndex is not { } picked || index.Text!=indexBefore || px.Text!=xBefore || py.Text!=yBefore)
            { result.Foreground=StudioTheme.Warning;result.Text="Nokta seçimi sırasında kaynak veya girişler değişti; indeks aktarılmadı.";return; }
            index.Text=picked.ToString(CultureInfo.InvariantCulture);
            result.Foreground=StudioTheme.Muted;result.Text=$"{picked} numaralı nokta seçildi. X/Y girişleri değiştirilmedi; taşıma öncesinde kontrol edin.";
        },()=>HasPiece());
        vertexIndexInput=index;vertexXInput=px;vertexYInput=py;
        bool ValidIndex()=>source.SelectedItem is PatternPiece p&&int.TryParse(index.Text,out var value)&&value>=0&&value<p.Points.Count;
        Note(panel,"Mevcut koordinatları yüklemek X/Y girişlerindeki değerleri değiştirir; parçayı değiştirmez.");
        Button(panel,"Seçili noktanın koordinatlarını yükle",()=>
        {
            if(workspace.HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("Önce tamamlanmamış çizimi, sürüklemeyi veya parça alan taslaklarını tamamlayın ya da iptal edin.");
            var selected=Piece();
            var current=workspace.Document.Pieces.FirstOrDefault(p=>p.Id==selected.Id);
            if(!ReferenceEquals(current,selected))throw new InvalidOperationException("Kaynak parça değişti; seçimi yenileyin.");
            if(!int.TryParse(index.Text,out var pointIndex) || pointIndex<0 || pointIndex>=current.Points.Count)
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
                : !int.TryParse(index.Text,out var value) || value<0 || value>=piece.Points.Count
                    ? $"0 ile {piece.Points.Count-1} arasında mevcut bir nokta indeksi girin."
                : !IsNumber(px.Text,out _) || !IsNumber(py.Text,out _)
                    ? "Nokta eklemek veya taşımak için X ve Y koordinatlarına geçerli sayılar girin; silme bu alanları kullanmaz. Ondalık için virgül veya nokta kullanın."
                : piece.Points.Count <= (piece.Closed?3:2)
                    ? "Bu parçada daha az nokta bulunamaz; nokta ekleyebilirsiniz, ancak silemezsiniz." : "";
            if(pointHint.Text==message)return;
            pointHint.Text=message;AutomationProperties.SetName(pointHint,message);
            pointHint.Visibility=message.Length==0?Visibility.Collapsed:Visibility.Visible;
            if(message.Length>0 && System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(pointHint) is { } peer)
                peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
        index.TextChanged+=(_,_)=>UpdatePointHint();px.TextChanged+=(_,_)=>UpdatePointHint();py.TextChanged+=(_,_)=>UpdatePointHint();
        source.SelectionChanged+=(_,_)=>UpdatePointHint();UpdatePointHint();
        Button(panel,"Nokta ekle",()=>VertexMoveTaskForVerification=InsertVertexAsync(index.Text,px.Text,py.Text),()=>ValidIndex()&&IsNumber(px.Text,out _)&&IsNumber(py.Text,out _));
        Note(panel,"Seçili noktadan sonraki kenarın tam ortasına nokta eklenebilir. Kapalı konturun son kenarı ilk noktaya uzanır; açık konturun son noktasından sonra kenar yoktur. Serileme kuralı varsa yeni nokta için yeniden gözden geçirin.");
        Button(panel,"Sonraki kenarın ortasına nokta ekle",()=>VertexMoveTaskForVerification=InsertEdgeMidpointAsync(index.Text),()=>ValidIndex()&&source.SelectedItem is PatternPiece p&&(p.Closed || int.Parse(index.Text,CultureInfo.InvariantCulture)<p.Points.Count-1));
        var edgeDistance=Input(panel,"Seçili noktadan mesafe · mm","10");
        edgeDistanceInput=edgeDistance;
        Note(panel,"Mesafe seçili noktadan sonraki kenar boyunca ölçülür. Sayısal yuvarlama nedeniyle yeni noktanın eski kenardan ve istenen mesafeden sapması en fazla 0,000001 mm olabilir. Serileme kurallarını yeniden gözden geçirin.");
        Button(panel,"Mesafede nokta ekle",()=>VertexMoveTaskForVerification=InsertEdgeDistanceAsync(index.Text,edgeDistance.Text),()=>ValidIndex()&&IsNumber(edgeDistance.Text,out _)&&source.SelectedItem is PatternPiece p&&(p.Closed || int.Parse(index.Text,CultureInfo.InvariantCulture)<p.Points.Count-1));
        var edgePercentage=Input(panel,"Seçili noktadan kenar yüzdesi · %","50");
        edgePercentageInput=edgePercentage;
        Note(panel,"Yüzde seçili noktadan sonraki kenar boyunca ölçülür; 0 ile 100 arasında olmalıdır. Yeni noktanın kenardan ve hesaplanan mesafeden sapması en fazla 0,000001 mm olabilir. Serileme kurallarını yeniden gözden geçirin.");
        Button(panel,"Yüzdede nokta ekle",()=>VertexMoveTaskForVerification=InsertEdgePercentageAsync(index.Text,edgePercentage.Text),()=>ValidIndex()&&IsNumber(edgePercentage.Text,out var percentage)&&percentage>0&&percentage<100&&source.SelectedItem is PatternPiece p&&(p.Closed || int.Parse(index.Text,CultureInfo.InvariantCulture)<p.Points.Count-1));
        Note(panel,"Seçili noktadan sonraki kenarda konturdan fiziksel olarak içe doğru V kesilir; bu işlem yalnızca işaret eklemez. Konum 0 ile 1 arasındaki kenar oranıdır; ağız genişliği ve derinlik ayrı ölçülerdir. Konturu ve serileme kurallarını kontrol edin.");
        var notchFraction=Input(panel,"Çentik konumu · kenar oranı","0,5");
        var notchWidth=Input(panel,"Çentik ağız genişliği · mm","6");
        var notchDepth=Input(panel,"Çentik derinliği · mm","3");
        notchFractionInput=notchFraction;notchDepthInput=notchDepth;notchWidthInput=notchWidth;
        Button(panel,"Sonraki kenara V çentik aç",()=>VertexMoveTaskForVerification=InsertNotchAsync(index.Text,notchFraction.Text,notchWidth.Text,notchDepth.Text),()=>ValidIndex()&&source.SelectedItem is PatternPiece { Closed:true }&&IsNumber(notchFraction.Text,out _)&&IsNumber(notchDepth.Text,out _)&&IsNumber(notchWidth.Text,out _));
        Note(panel,"Açık konturun başlangıç ve bitiş yönünü ters çevirebilirsiniz. Noktaların konumu değişmez. Eşleşen kayıtlı serileme artışları noktalarla birlikte ters çevrilir; uygulanmamış taslaklar korunur.");
        Button(panel,"Açık kontur yönünü ters çevir",()=>VertexMoveTaskForVerification=ReverseOpenPathAsync(),()=>source.SelectedItem is PatternPiece { Closed:false });
        Note(panel,"Kapalı konturun yönünü ters çevirin; ilk nokta ve şekil korunur. Nokta indeksleri değişir. Eşleşen kayıtlı serileme artışları ilgili noktalarla birlikte taşınır; uygulanmamış taslaklar korunur.");
        Button(panel,"Kapalı kontur yönünü ters çevir",()=>VertexMoveTaskForVerification=ReverseClosedContourAsync(),()=>source.SelectedItem is PatternPiece { Closed:true });
        Note(panel,"Noktayı taşı, girilen indeksteki mevcut noktanın koordinatlarını değiştirir. Kapalı konturda yinelenen ilk ve son nokta birlikte taşınır. Doğrulama arka planda yapılır; sonucun uygulanmasından önce proje yeniden kontrol edilir.");
        Button(panel,"Seçilen indeksteki noktayı taşı",()=>VertexMoveTaskForVerification=MoveVertexAsync(index.Text,px.Text,py.Text),()=>ValidIndex()&&IsNumber(px.Text,out _)&&IsNumber(py.Text,out _));
        Button(panel,"Seçilen indeksteki noktayı sil",()=>VertexMoveTaskForVerification=DeleteVertexAsync(index.Text),()=>ValidIndex()&&source.SelectedItem is PatternPiece p&&p.Points.Count>(p.Closed?3:2));return Scroll(panel);
    }
    private enum VertexEdit { Move, Insert, Delete, Midpoint, Reverse, ReverseClosed, Notch, Distance, Percentage }
    internal Task MoveVertexAsync(string indexText,string xText,string yText) => EditVertexAsync(VertexEdit.Move,indexText,xText,yText);
    internal Task InsertVertexAsync(string indexText,string xText,string yText) => EditVertexAsync(VertexEdit.Insert,indexText,xText,yText);
    internal Task DeleteVertexAsync(string indexText) => EditVertexAsync(VertexEdit.Delete,indexText,null,null);
    internal Task InsertEdgeMidpointAsync(string indexText) => EditVertexAsync(VertexEdit.Midpoint,indexText,null,null);
    internal Task InsertEdgeDistanceAsync(string indexText,string distanceText) => EditVertexAsync(VertexEdit.Distance,indexText,distanceText,"0");
    internal Task InsertEdgePercentageAsync(string indexText,string percentageText) => EditVertexAsync(VertexEdit.Percentage,indexText,percentageText,"0");
    internal Task InsertNotchAsync(string indexText,string fractionText,string depthText) => EditVertexAsync(VertexEdit.Notch,indexText,fractionText,depthText);
    internal Task InsertNotchAsync(string indexText,string fractionText,string widthText,string depthText) => EditVertexAsync(VertexEdit.Notch,indexText,fractionText,depthText,widthText);
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
            var position=operation is VertexEdit.Delete or VertexEdit.Midpoint or VertexEdit.Reverse or VertexEdit.ReverseClosed ? default : new Point2(Number(xText!),Number(yText!));
            var width=widthText is null ? position.Y * 2 : Number(widthText);
            var selected=Piece();var sourceId=selected.Id;
            var document=workspace.Document;var original=document.Clone();var snapshot=selected.Clone();var workspaceSelection=workspace.SelectedId;
            if(!ReferenceEquals(document.Pieces.FirstOrDefault(piece=>piece.Id==sourceId),selected))
                throw new InvalidOperationException("Kaynak parça güncel değil. Atölyeyi yeniden açıp parçayı seçin.");
            var inputIndex=vertexIndexInput?.Text;var inputX=vertexXInput?.Text;var inputY=vertexYInput?.Text;var inputFraction=notchFractionInput?.Text;var inputDepth=notchDepthInput?.Text;var inputWidth=notchWidthInput?.Text;var inputDistance=edgeDistanceInput?.Text;var inputPercentage=edgePercentageInput?.Text;
            if(index<0 || index>=snapshot.Points.Count)throw new ArgumentOutOfRangeException(nameof(index),"Nokta indeksi kaynak parçada bulunmuyor.");
            if(operation==VertexEdit.Move && snapshot.Points[index]==position)
            { result.Foreground=StudioTheme.Muted;result.Text="Nokta zaten bu konumda; proje ve işlem geçmişi değişmedi.";return; }
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
                    VertexEdit.Move => PatternTools.MovePoint(snapshot,index,position,token),
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
               vertexIndexInput?.Text!=inputIndex || vertexXInput?.Text!=inputX || vertexYInput?.Text!=inputY || notchFractionInput?.Text!=inputFraction || notchDepthInput?.Text!=inputDepth || notchWidthInput?.Text!=inputWidth || edgeDistanceInput?.Text!=inputDistance || edgePercentageInput?.Text!=inputPercentage)
                throw new InvalidOperationException("Proje, seçim veya bekleyen çalışma değişti. Nokta düzenlemesi uygulanmadı; güncel durumu kontrol edin.");
            token.ThrowIfCancellationRequested();
            ReplacePiece(edited,operation is VertexEdit.Reverse or VertexEdit.ReverseClosed,operation==VertexEdit.Midpoint ? index : null);
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
                }
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
    private void ReplacePiece(PatternPiece edited,bool reverseOpenPath=false,int? midpointIndex=null)
    {
        if(toolReviewRequired) { ReportToolReview("Son işlem",null);return; }
        var previous=Piece();var index=workspace.Document.Pieces.FindIndex(p=>p.Id==previous.Id);
        if(index<0)throw new InvalidOperationException("Kaynak parça artık projede yok.");edited.Id=previous.Id;edited.Metadata=previous.Metadata?.Clone();
        edited.Grainline=previous.Grainline?.Clone();
        // Reversal changes only vertex order, not the grainline's relationship to the contour.
        var grainNeedsReview=edited.Grainline is not null && !reverseOpenPath && (edited.Closed!=previous.Closed || !edited.Points.SequenceEqual(previous.Points));
        if(grainNeedsReview)edited.Grainline!.NeedsReview=true;
        var rule=workspace.Document.GradingRules.FirstOrDefault(r=>r.SourcePieceId==previous.Id);
        var hasDraft=gradeDrafts.TryGetValue(previous.Id,out var draft) && HasUnsavedGradeDraft(previous.Id,draft);
        var reversedRule=!hasDraft ? (reverseOpenPath ? (previous.Closed ? rule?.ReversedClosedFor(previous) : rule?.ReversedFor(previous)) : midpointIndex is { } midpoint ? rule?.WithEdgeMidpointFor(previous,edited,midpoint) : null) : null;
        var gradingNeedsReview=(edited.Closed!=previous.Closed || !edited.Points.SequenceEqual(previous.Points)) && rule is not null && reversedRule is null;
        CommitToolChange("Kontur noktası düzenlemesi",()=> {
            workspace.Document.Pieces[index]=edited;
            if(reversedRule is not null) { var ruleIndex=workspace.Document.GradingRules.IndexOf(rule!);workspace.Document.GradingRules[ruleIndex]=reversedRule; }
        },()=>
        { if(reversedRule is not null)gradeDrafts.Remove(edited.Id);RefreshSources(edited.Id);workspace.Select(edited.Id);workspace.FitView();Success("Kontur noktası düzenlendi ve doğrulandı."+(grainNeedsReview ? " İplik yönü korundu; değişen konturla ilişkisini yeniden kontrol edin." : "")+(reversedRule is not null ? (midpointIndex.HasValue ? " Yeni noktanın serileme artışı komşu artışların ortalamasından oluşturuldu; kontrol edin." : " Kayıtlı serileme artışları nokta sırasıyla ters çevrildi.") : "")+(gradingNeedsReview ? " Mevcut serileme kuralı önceki kontura aittir; yeniden gözden geçirin." : "")); });
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
                after?.Invoke();token.ThrowIfCancellationRequested();return computed;
            });
            if(windowClosed)return;
            token.ThrowIfCancellationRequested();
            if(workspace.HasPendingCanvasOrPropertyWork || !ReferenceEquals(workspace.Document,document) ||
               !DocumentSession.ContentEquals(workspace.Document,original) || workspace.SelectedId!=selectedId ||
               (source.SelectedItem as PatternPiece)?.Id!=sourceId || basicWidth.Text!=widthText || basicGap.Text!=gapText || basicMargin.Text!=marginText)
                throw new InvalidOperationException("Proje, seçim veya pastal girdileri değişti. Sonuç uygulanmadı; güncel durumla yeniden hesaplayın.");
            token.ThrowIfCancellationRequested();
            var candidate=original.Clone();candidate.Pieces=layout.Layout.Pieces;
            if(DocumentSession.ContentEquals(original,candidate))
            {
                Success($"Hesaplanan boy {MeasurementInput.Format(layout.UsedLength)} mm  ·  Kullanım %{layout.UtilizationPercent:0.##}  ·  Fire %{layout.WastePercent:0.##}. Yerleşim zaten aynı; proje ve geri alma geçmişi değişmedi. Her parça 1 adet; rol ve kayıtlı adet uygulanmadı.");
                return;
            }
            CommitToolChange("Raf pastal yerleşimi",()=>workspace.Document.Pieces=layout.Layout.Pieces,()=>
            { workspace.Select(null);workspace.FitView();RefreshSources(null);
              Success($"Hesaplanan boy {MeasurementInput.Format(layout.UsedLength)} mm  ·  Kullanım %{layout.UtilizationPercent:0.##}  ·  Fire %{layout.WastePercent:0.##}. Hesap: kumaş eni × kullanılan uzunluk. Her parça 1 adet; rol ve kayıtlı adet uygulanmadı. Geri alma için atölyeyi kapatıp işlem geçmişini kontrol edin."); });
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
    public sealed class GradeRow
    {
        public int Index { get; set; } public double X { get; set; } public double Y { get; set; }
        public string Dx { get; set; }="0";public string Dy { get; set; }="0";
    }
}


