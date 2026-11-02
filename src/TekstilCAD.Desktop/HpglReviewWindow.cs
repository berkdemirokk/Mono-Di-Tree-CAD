using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>Informational review of the captured source used for a prepared HPGL file.</summary>
internal sealed class HpglReviewWindow : Window
{
    internal DataGrid ReportsGrid { get; } = new();
    internal TextBox SelectedDetails { get; } = new();
    internal HpglGeometryComparison GeometryComparison { get; } = new();
    internal TextBox GeometryText { get; } = new() { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(6,2,6,2) };
    internal ComboBox SampleSelector { get; } = new() { MinWidth=160,Margin=new Thickness(0,0,8,0),IsEnabled=false };
    internal Button ShowWholeButton { get; } = new() { Content="Tüm parça",Padding=new Thickness(8,4,8,4),IsEnabled=false };
    internal Button ZoomInButton { get; } = new() { Content="+",Width=32,Padding=new Thickness(0),Margin=new Thickness(4,0,0,0),IsEnabled=false };
    internal Button ZoomOutButton { get; } = new() { Content="−",Width=32,Padding=new Thickness(0),Margin=new Thickness(4,0,0,0),IsEnabled=false };
    internal Button ResetZoomButton { get; } = new() { Content="1×",Width=38,Padding=new Thickness(0),Margin=new Thickness(4,0,0,0),IsEnabled=false };
    private readonly TextBlock comparisonMode = new() { Text="Tüm parça · ekrana sığdırıldı",Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center };
    private HpglPieceGeometryPreview? currentGeometry;
    private HpglLengthReview.Result? currentLengths;
    private bool updatingSamples;
    private sealed record SampleChoice(HpglTurnReview.Sample Sample)
    {
        public override string ToString()=> $"Nokta {Sample.SourceVertexIndex+1} · "+(Sample.SourceSign!=0 && Sample.PlotSign==0 ? "düzleşen" : Sample.SourceSign==0 ? "yeni dönüş" : "yönü değişen");
    }
    internal TextBlock PageStatus { get; } = new() { Text="Parça seçin.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,4) };
    internal TextBlock BoundsSummary { get; } = new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,4) };
    internal string InteractionTextForVerification { get; private set; } = "";
    internal TextBox InteractionDetailsForVerification { get; } = new() { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(6) };
    internal Button FirstInteractionButtonForVerification { get; } = new() { Content="İlk parçayı göster",Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,0,6,4) };
    internal Button SecondInteractionButtonForVerification { get; } = new() { Content="İkinci parçayı göster",Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,0,6,4) };
    internal CheckBox ChangedOnlyFilter { get; } = new() { Content="Yalnız değişen parçaları göster",Margin=new Thickness(0,5,0,4) };
    internal Button MoreSamplesButton { get; } = new() { Content="Seçili parçayı ayrıntılı incele",Margin=new Thickness(8,2,0,2),Padding=new Thickness(8,4,8,4),IsEnabled=false };
    internal Button CancelReviewButton { get; } = new() { Content="İncelemeyi iptal et",Margin=new Thickness(8,2,0,2),Padding=new Thickness(8,4,8,4),Visibility=Visibility.Collapsed };
    internal Button PreviousPageButton { get; } = new() { Content="Önceki",Margin=new Thickness(8,2,0,2),Padding=new Thickness(8,4,8,4),Visibility=Visibility.Collapsed };
    internal Button NextPageButton { get; } = new() { Content="Sonraki",Margin=new Thickness(4,2,0,2),Padding=new Thickness(8,4,8,4),Visibility=Visibility.Collapsed };
    internal Action? BeforePieceReviewForVerification { get; set; }
    internal Action? AfterPieceReviewForVerification { get; set; }
    internal Task? PieceReviewTaskForVerification { get; private set; }
    private CancellationTokenSource? pieceReviewCancellation;
    private readonly HpglPreparedOutput preparedForReview;
    private bool closed;
    private long selectionGeneration;
    private Row? pendingRow;
    private long pendingGeneration;
    private const int PieceReviewPageSize=20;
    private HpglTurnReview.Page? currentPage;
    private Row? pageRow;
    internal Button CancelButton { get; } = new() { Content="Vazgeç",IsCancel=true,IsDefault=true,Padding=new Thickness(14,8,14,8),Margin=new Thickness(0,0,8,0) };
    internal Button ExportButton { get; } = new() { Content="HPGL dosyasını oluştur",Padding=new Thickness(14,8,14,8) };
    private sealed record Row(string Name,string FullName,string Identity,int Examined,int Changed,int Flattened,int Introduced,int Reversed,HpglTurnReview.Report Report);

    internal static string FormatOutputDimensions(HpglOutputBounds bounds,double unitsPerMillimeter)
    {
        // Widen before subtracting: the two valid integer coordinates can span
        // more than Int32.MaxValue. These are encoded bounds, not physical output.
        var width=((long)bounds.MaxX-bounds.MinX)/unitsPerMillimeter;
        var height=((long)bounds.MaxY-bounds.MinY)/unitsPerMillimeter;
        if(!double.IsFinite(width) || !double.IsFinite(height))return "Yaklaşık çıktı boyutu hesaplanamadı.";
        return $"≈{width.ToString("G6",CultureInfo.CurrentCulture)} × {height.ToString("G6",CultureInfo.CurrentCulture)} mm";
    }

    internal HpglReviewWindow(Window owner,HpglPreparedOutput prepared,IReadOnlyList<PatternPiece> sourcePieces,double? simulationScale=null)
    {
        ArgumentNullException.ThrowIfNull(owner);ArgumentNullException.ThrowIfNull(prepared);ArgumentNullException.ThrowIfNull(sourcePieces);
        preparedForReview=prepared;
        if(simulationScale is { } invalidScale && (!double.IsFinite(invalidScale) || invalidScale<=0))throw new ArgumentOutOfRangeException(nameof(simulationScale));
        var simulation=simulationScale.HasValue;
        if(simulation)ExportButton.Content="Simülasyon dosyasını oluştur";
        Owner=owner;Title="HPGL dönüş incelemesi";Width=900;Height=650;MinWidth=580;MinHeight=420;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var names=sourcePieces.ToDictionary(piece=>piece.Id,piece=>piece.Name);
        var rows=prepared.TurnReviews.Select(report=>new Row(
            names.TryGetValue(report.PieceId,out var name) ? ShortName(name) : throw new InvalidOperationException("İnceleme parçası kaynak proje kopyasında bulunamadı."),name,
            report.PieceId.ToString("D"),report.ExaminedTurns,report.ChangedTurns,report.FlattenedTurns,report.IntroducedTurns,report.ReversedTurns,report)).ToArray();
        if(rows.Length!=sourcePieces.Count || rows.Select(r=>r.Report.PieceId).Distinct().Count()!=rows.Length)
            throw new InvalidOperationException("İnceleme raporu tüm kaynak parçalarla eşleşmiyor.");
        var root=new Grid { Margin=new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });Content=root;
        var interaction=prepared.Interactions;
        var interactionHeadline=interaction?.Status switch {
            HpglPieceInteraction.State.Found=>"Parçalar arası bulgu var; Yol ilişkisi sekmesini açın.",
            HpglPieceInteraction.State.Clear=>"Parçalar arası temas veya iç içe yerleşim bulunmadı; sınırlar için kaydırın.",
            _=>"Parçalar arası kontrol tamamlanmadı; ayrıntı için kaydırın." };
        var interactionDetails=interaction?.Status==HpglPieceInteraction.State.Found
            ? $"İlk bulunan çiftin parça kimlikleri: {interaction.FirstPiece:D} ve {interaction.SecondPiece:D}. " : "";
        InteractionTextForVerification=interactionHeadline+" "+interactionDetails;
        var description=new TextBlock { TextWrapping=TextWrapping.Wrap,Text=
            interactionHeadline+"\n"+interactionDetails+
            "Bu kontrol yalnız yuvarlanmış HPGL yollarını inceler; kaynak geometrisi, mesafe, üretim veya cihaz uygunluğu onayı değildir. İlk bulguda durur; tamamlanmamış kontrol bulgu yok anlamına gelmez.\n"+
            $"{rows.Length} {(simulation ? "seçili " : "")}parça · {rows.Sum(r=>(long)r.Examined)} dönüş incelendi · {rows.Sum(r=>(long)r.Changed)} dönüşün yön sınıfı değişti. "+
            (simulation ? $"Seçili parçalar {MeasurementInput.Format(simulationScale!.Value)} ölçek çarpanı uygulanarak incelendi. " : "")+
            $"{MeasurementInput.Format(prepared.UnitsPerMillimeter)} birim/mm; dosya koordinat adımı {MeasurementInput.Format(1/prepared.UnitsPerMillimeter)} mm.\n"+
            "Dönüş incelemesi yalnız kaynak noktalarındaki yerel dönüşleri karşılaştırır; tüm detayları değerlendirmez. "+
            "Sayımlar incelenen dönüşlerin tamamını kapsar; ilk raporda örnekler iş genelinde sınırlıdır. Seçili parçanın örneklerini sayfalar halinde inceleyebilirsiniz. "+
            "Değişiklikler yoğun eğrilerde zararsız olabilir; sıfır bulgu güvenlik veya ölçü doğruluğu onayı değildir. Bu rapor HPGL dosyasına eklenmez. Fiziksel makine uyumluluğu ve kalibrasyon doğrulanmadı."+
            (simulation ? " Fiziksel cihaza bağlantı kurulmaz veya iş gönderilmez." : ""),Margin=new Thickness(0,0,0,8) };
        var descriptionViewer=new ScrollViewer { Content=description,MaxHeight=24,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=true };
        KeyboardNavigation.SetIsTabStop(descriptionViewer,true);AutomationProperties.SetName(descriptionViewer,"HPGL incelemesinin kapsamı ve sınırlamaları");
        AutomationProperties.SetHelpText(descriptionViewer,"Tam kapsam ve uyarılar için bu alanı yön tuşlarıyla kaydırın.");
        Grid.SetRow(descriptionViewer,0);root.Children.Add(descriptionViewer);
        var bounds=prepared.Bounds;
        BoundsSummary.Text=$"HPGL: X {bounds.MinX}…{bounds.MaxX}, Y {bounds.MinY}…{bounds.MaxY}; "+
            FormatOutputDimensions(bounds,prepared.UnitsPerMillimeter)+". "+
            (bounds.HasNegativeCoordinates ? "Negatif koordinat; cihaz alanını kontrol edin." : "Cihaz doğrulanmadı.");
        BoundsSummary.Foreground=bounds.HasNegativeCoordinates ? StudioTheme.Warning : StudioTheme.Text;
        AutomationProperties.SetName(BoundsSummary,"Hazırlanan HPGL çıktı koordinat sınırları");
        AutomationProperties.SetHelpText(BoundsSummary,"Yaklaşık en ve boy, dosyadaki tamsayı sınırlarının seçilen birim/mm değerine bölünmesiyle bulunur; fiziksel çıktı ölçüsü değildir.");
        Grid.SetRow(BoundsSummary,1);root.Children.Add(BoundsSummary);
        var actions=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };
        CancelButton.Click+=(_,_)=>DialogResult=false;ExportButton.Click+=(_,_)=>DialogResult=true;
        actions.Children.Add(CancelButton);actions.Children.Add(ExportButton);Grid.SetRow(actions,4);root.Children.Add(actions);
        SelectedDetails.IsReadOnly=true;SelectedDetails.AcceptsReturn=true;SelectedDetails.TextWrapping=TextWrapping.Wrap;
        SelectedDetails.MinHeight=48;SelectedDetails.MaxHeight=48;SelectedDetails.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
        SelectedDetails.Margin=new Thickness(0,8,0,0);
        AutomationProperties.SetName(SelectedDetails,"Seçili parçanın dönüş incelemesi ayrıntıları");
        AutomationProperties.SetHelpText(SelectedDetails,"Tam ad ve kimlik ile birden başlayan kaynak nokta numaralarını seçip kopyalayabilirsiniz. Uzun ayrıntıyı kaydırın.");
        var detailsTabs=new TabControl { Margin=new Thickness(0,6,0,0) };
        var sourcePanel=new DockPanel();DockPanel.SetDock(SelectedDetails,Dock.Bottom);sourcePanel.Children.Add(SelectedDetails);
        var comparisonPanel=new DockPanel();
        var comparisonActions=new WrapPanel { Margin=new Thickness(0,0,0,4) };
        AutomationProperties.SetName(SampleSelector,"Yakından incelenecek dönüş örneği");
        AutomationProperties.SetHelpText(SampleSelector,"Sayfadaki örneği seçince önceki, seçilen ve sonraki dönüş aynı ölçekte yakınlaştırılır.");
        AutomationProperties.SetName(ShowWholeButton,"Tüm parçayı yeniden göster");
        AutomationProperties.SetName(ZoomInButton,"Seçili nokta çevresini yakınlaştır");AutomationProperties.SetName(ZoomOutButton,"Seçili nokta çevresini uzaklaştır");AutomationProperties.SetName(ResetZoomButton,"Yakınlaştırmayı sıfırla");
        AutomationProperties.SetHelpText(ZoomInButton,"Orantılı büyütür; görünüm dışındaki çizgiler kırpılır. Fiziksel ölçüm değildir.");
        AutomationProperties.SetName(comparisonMode,"Karşılaştırma görünümü ve yakınlaştırma düzeyi");AutomationProperties.SetLiveSetting(comparisonMode,AutomationLiveSetting.Polite);
        comparisonActions.Children.Add(SampleSelector);comparisonActions.Children.Add(ShowWholeButton);comparisonActions.Children.Add(ZoomOutButton);comparisonActions.Children.Add(ZoomInButton);comparisonActions.Children.Add(ResetZoomButton);comparisonActions.Children.Add(comparisonMode);
        DockPanel.SetDock(comparisonActions,Dock.Top);comparisonPanel.Children.Add(comparisonActions);
        var legend=new TextBlock { Text="Kaynak: kesik/üçgen; HPGL: düz/daire. Ölçüm değildir; görünüm kırpılabilir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,3) };
        DockPanel.SetDock(legend,Dock.Top);comparisonPanel.Children.Add(legend);
        AutomationProperties.SetName(GeometryText,"Kaynak ve HPGL örnek koordinatları");
        AutomationProperties.SetHelpText(GeometryText,"Yaklaşık kaynak ve HPGL kenar uzunluklarını, en büyük kenar farkını ve örnek dönüş koordinatlarını okuyun ve kopyalayın.");
        GeometryText.Text="Karşılaştırma için bir parça seçip ayrıntılı incelemeyi başlatın.";
        GeometryComparison.MinHeight=70;comparisonPanel.Children.Add(GeometryComparison);
        ReportsGrid.IsReadOnly=true;ReportsGrid.AutoGenerateColumns=false;ReportsGrid.CanUserAddRows=false;
        ReportsGrid.EnableRowVirtualization=true;ReportsGrid.EnableColumnVirtualization=true;
        VirtualizingPanel.SetIsVirtualizing(ReportsGrid,true);VirtualizingPanel.SetVirtualizationMode(ReportsGrid,VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(ReportsGrid,true);
        var nameStyle=new Style(typeof(TextBlock));nameStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis));
        nameStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,new Binding(nameof(Row.Name))));
        ReportsGrid.Columns.Add(new DataGridTextColumn { Header="Parça",Binding=new Binding(nameof(Row.Name)),ElementStyle=nameStyle,MinWidth=100,Width=new DataGridLength(1,DataGridLengthUnitType.Star) });
        ReportsGrid.Columns.Add(new DataGridTextColumn { Header="İncelenen",Binding=new Binding(nameof(Row.Examined)),Width=new DataGridLength(75) });
        ReportsGrid.Columns.Add(new DataGridTextColumn { Header="Değişen",Binding=new Binding(nameof(Row.Changed)),Width=new DataGridLength(75) });
        ReportsGrid.Columns.Add(new DataGridTextColumn { Header="Düzleşen",Binding=new Binding(nameof(Row.Flattened)),Width=new DataGridLength(75) });
        ReportsGrid.Columns.Add(new DataGridTextColumn { Header="Yeni dönüş",Binding=new Binding(nameof(Row.Introduced)),Width=new DataGridLength(80) });
        ReportsGrid.Columns.Add(new DataGridTextColumn { Header="Yönü değişen",Binding=new Binding(nameof(Row.Reversed)),Width=new DataGridLength(90) });
        var view=CollectionViewSource.GetDefaultView(rows);
        ReportsGrid.ItemsSource=view;
        AutomationProperties.SetName(ReportsGrid,"HPGL parça dönüş raporları");
        AutomationProperties.SetHelpText(ReportsGrid,"Parça seçerek aşağıdaki tam kimliği ve kaynak nokta örneklerini inceleyin.");
        ReportsGrid.SelectionChanged+=(_,_)=>
        {
            selectionGeneration++;pieceReviewCancellation?.Cancel();CancelReviewButton.IsEnabled=false;currentPage=null;pageRow=null;
            PreviousPageButton.Visibility=NextPageButton.Visibility=Visibility.Collapsed;
            ClearComparison();
            SelectedDetails.Text=ReportsGrid.SelectedItem is Row row ? Details(row) : "Parça seçerek ayrıntıları inceleyin.";
            PageStatus.Text=ReportsGrid.SelectedItem is Row ? "Sayfalı incelemeyi başlatın." : "Parça seçin.";
            MoreSamplesButton.IsEnabled=pieceReviewCancellation is null && ReportsGrid.SelectedItem is Row;
        };
        AutomationProperties.SetName(ChangedOnlyFilter,"Yalnız dönüş sınıfı değişen parçaları göster");
        AutomationProperties.SetHelpText(ChangedOnlyFilter,"Filtre yalnız görünen satırları değiştirir; tam rapor sayıları ve kaynak korunur.");
        var filterPanel=new StackPanel();var filterActions=new WrapPanel();filterActions.Children.Add(ChangedOnlyFilter);
        filterActions.Children.Add(MoreSamplesButton);filterActions.Children.Add(CancelReviewButton);
        filterActions.Children.Add(PreviousPageButton);filterActions.Children.Add(NextPageButton);filterPanel.Children.Add(filterActions);
        AutomationProperties.SetName(MoreSamplesButton,"Seçili parçanın dönüş örneklerini ayrıntılı incele");
        AutomationProperties.SetHelpText(MoreSamplesButton,"Yakalanmış proje kopyasından seçili parçanın örneklerini sayfalar halinde arka planda hesaplar.");
        AutomationProperties.SetName(CancelReviewButton,"Seçili parça incelemesini iptal et");
        AutomationProperties.SetName(PreviousPageButton,"Önceki dönüş örnekleri sayfası");AutomationProperties.SetName(NextPageButton,"Sonraki dönüş örnekleri sayfası");
        MoreSamplesButton.Click+=(_,_)=> { if(pieceReviewCancellation is null && ReportsGrid.SelectedItem is Row row)PieceReviewTaskForVerification=ReviewPieceAsync(row,0); };
        PreviousPageButton.Click+=(_,_)=> { if(pieceReviewCancellation is null && ReportsGrid.SelectedItem is Row row && currentPage is { Offset: >0 } page && ReferenceEquals(pageRow,row))PieceReviewTaskForVerification=ReviewPieceAsync(row,Math.Max(0,page.Offset-PieceReviewPageSize)); };
        NextPageButton.Click+=(_,_)=> { if(pieceReviewCancellation is null && ReportsGrid.SelectedItem is Row row && currentPage?.NextOffset is int next && ReferenceEquals(pageRow,row))PieceReviewTaskForVerification=ReviewPieceAsync(row,next); };
        CancelReviewButton.Click+=(_,_)=> { if(pieceReviewCancellation is not null) { pieceReviewCancellation.Cancel();CancelReviewButton.IsEnabled=false;
            if(pendingGeneration==selectionGeneration && ReferenceEquals(pendingRow,ReportsGrid.SelectedItem))PageStatus.Text="İptal istendi; incelemenin durması bekleniyor."; } };
        var filterStatus=new TextBlock { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,4) };
        AutomationProperties.SetName(filterStatus,"HPGL rapor filtresi durumu");AutomationProperties.SetLiveSetting(filterStatus,AutomationLiveSetting.Polite);
        var statusRow=new Grid();statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        filterStatus.Margin=new Thickness(0,0,12,0);filterStatus.MaxWidth=190;statusRow.Children.Add(filterStatus);
        AutomationProperties.SetName(PageStatus,"HPGL örnek sayfası durumu");AutomationProperties.SetLiveSetting(PageStatus,AutomationLiveSetting.Polite);
        Grid.SetColumn(PageStatus,1);statusRow.Children.Add(PageStatus);filterPanel.Children.Add(statusRow);Grid.SetRow(filterPanel,2);root.Children.Add(filterPanel);
        var gridArea=new Grid();gridArea.Children.Add(ReportsGrid);
        var empty=new TextBlock { Text="Gösterilecek parça yok. Tüm parçaları görmek için filtreyi kaldırın.",TextWrapping=TextWrapping.Wrap,
            HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12),IsHitTestVisible=false,Visibility=Visibility.Collapsed };
        gridArea.Children.Add(empty);sourcePanel.Children.Add(gridArea);
        detailsTabs.Items.Add(new TabItem { Header="Parçalar ve noktalar",Content=sourcePanel });
        detailsTabs.Items.Add(new TabItem { Header="Kaynak / HPGL karşılaştırması",Content=comparisonPanel });
        detailsTabs.Items.Add(new TabItem { Header="Uzunluk ve koordinatlar",Content=GeometryText });
        if(interaction is { Status:HpglPieceInteraction.State.Found,FirstPiece:Guid firstId,SecondPiece:Guid secondId })
        {
            var first=rows.Single(r=>r.Report.PieceId==firstId);var second=rows.Single(r=>r.Report.PieceId==secondId);
            string EdgeDescription(Guid id,int index)
            {
                var geometry=prepared.GetPieceGeometry(id);var a=geometry.Points[index];var b=geometry.Points[(index+1)%geometry.Points.Count];
                return $"Kenar {index+1}: ({a.PlotX}, {a.PlotY}) → ({b.PlotX}, {b.PlotY}) HPGL birimi";
            }
            var detail=interaction.Kind switch {
                HpglPieceInteraction.Finding.Boundary when interaction.FirstEdge is int a && interaction.SecondEdge is int b =>
                    "Kenarlar kesişiyor veya temas ediyor.\nİlk parça: "+EdgeDescription(firstId,a)+"\nİkinci parça: "+EdgeDescription(secondId,b),
                HpglPieceInteraction.Finding.SecondInsideFirst=>"İkinci yol, ilk kapalı yolun içinde.",
                HpglPieceInteraction.Finding.FirstInsideSecond=>"İlk yol, ikinci kapalı yolun içinde.",
                _=>"Bulgunun türü belirlenemedi; iki yolu kontrol edin." };
            InteractionDetailsForVerification.Text=$"İlk parça: {SafeText(first.FullName)}\nKimlik: {first.Identity}\nİkinci parça: {SafeText(second.FullName)}\nKimlik: {second.Identity}\n\n{detail}\n\nYalnız ilk bulgu gösterilir. Koordinatlar yuvarlanmış çıktıdır; mesafe veya üretim onayı değildir.";
            AutomationProperties.SetName(InteractionDetailsForVerification,"İlk yol ilişkisi ve parça kimlikleri");
            AutomationProperties.SetHelpText(InteractionDetailsForVerification,"Kimlikleri ve varsa kenar koordinatlarını okuyabilir ve kopyalayabilirsiniz.");
            var interactionPanel=new DockPanel();var interactionActions=new WrapPanel();
            interactionActions.Children.Add(FirstInteractionButtonForVerification);interactionActions.Children.Add(SecondInteractionButtonForVerification);
            DockPanel.SetDock(interactionActions,Dock.Top);interactionPanel.Children.Add(interactionActions);interactionPanel.Children.Add(InteractionDetailsForVerification);
            void SelectInteraction(Row row)
            {
                ChangedOnlyFilter.IsChecked=false;detailsTabs.SelectedIndex=0;ReportsGrid.SelectedItem=row;ReportsGrid.ScrollIntoView(row);ReportsGrid.Focus();
            }
            FirstInteractionButtonForVerification.Click+=(_,_)=>SelectInteraction(first);
            SecondInteractionButtonForVerification.Click+=(_,_)=>SelectInteraction(second);
            AutomationProperties.SetHelpText(FirstInteractionButtonForVerification,"Dönüş filtresini kaldırır ve bulunan çiftin ilk parçasını tabloda seçer.");
            AutomationProperties.SetHelpText(SecondInteractionButtonForVerification,"Dönüş filtresini kaldırır ve bulunan çiftin ikinci parçasını tabloda seçer.");
            detailsTabs.Items.Add(new TabItem { Header="Yol ilişkisi",Content=interactionPanel });
        }
        SampleSelector.SelectionChanged+=(_,_)=>
        {
            if(updatingSamples || currentGeometry is null || currentPage is null || currentLengths is null)return;
            if(SampleSelector.SelectedItem is SampleChoice choice)
            {
                GeometryComparison.ShowLocal(choice.Sample.SourceVertexIndex);
                UpdateZoomControls();
                GeometryText.Text=GeometryDescription(currentGeometry,currentLengths,[choice.Sample]);
            }
            else { GeometryComparison.ShowWhole();UpdateZoomControls();GeometryText.Text=GeometryDescription(currentGeometry,currentLengths,currentPage.Report.Samples); }
        };
        ShowWholeButton.Click+=(_,_)=> { SampleSelector.SelectedIndex=-1; };
        ZoomInButton.Click+=(_,_)=> { GeometryComparison.SetZoom(GeometryComparison.ZoomFactorForVerification*2);UpdateZoomControls(); };
        ZoomOutButton.Click+=(_,_)=> { GeometryComparison.SetZoom(GeometryComparison.ZoomFactorForVerification/2);UpdateZoomControls(); };
        ResetZoomButton.Click+=(_,_)=> { GeometryComparison.SetZoom(1);UpdateZoomControls(); };
        Grid.SetRow(detailsTabs,3);root.Children.Add(detailsTabs);
        SelectedDetails.Text="Parça seçerek ayrıntıları inceleyin.";
        void ApplyFilter()
        {
            var selected=ReportsGrid.SelectedItem as Row;
            var changedOnly=ChangedOnlyFilter.IsChecked==true;
            view.Filter=changedOnly ? item=>item is Row { Changed: >0 } : null;
            var count=changedOnly ? rows.Count(row=>row.Changed>0) : rows.Length;
            if(selected is not null && (!changedOnly || selected.Changed>0))ReportsGrid.SelectedItem=selected;
            else if(selected is not null)ReportsGrid.SelectedItem=null;
            if(ReportsGrid.SelectedItem is not Row) { SelectedDetails.Text="Parça seçerek ayrıntıları inceleyin.";PageStatus.Text="Parça seçin."; }
            filterStatus.Text=$"{count} / {rows.Length} parça gösteriliyor."+(count==0 && changedOnly ? " Değişen parça bulunmadı; tüm parçalar için filtreyi kaldırın." : "");
            empty.Visibility=count==0 ? Visibility.Visible : Visibility.Collapsed;
        }
        ChangedOnlyFilter.Checked+=(_,_)=>ApplyFilter();ChangedOnlyFilter.Unchecked+=(_,_)=>ApplyFilter();ApplyFilter();
        Closed+=(_,_)=> { closed=true;pieceReviewCancellation?.Cancel(); };
    }
    private static string Details(Row row,bool paged=false)
    {
        var text=new StringBuilder();
        text.Append(SafeText(row.FullName)).Append("\nKimlik: ").Append(row.Identity).Append("\nİncelenen: ").Append(row.Examined)
            .Append(" · Değişen: ").Append(row.Changed).Append(" · Düzleşen: ").Append(row.Flattened)
            .Append(" · Yeni dönüş: ").Append(row.Introduced).Append(" · Yönü değişen: ").Append(row.Reversed);
        foreach(var sample in row.Report.Samples)
        {
            var type=sample.SourceSign!=0 && sample.PlotSign==0 ? "düzleşen" : sample.SourceSign==0 ? "yeni dönüş" : "yönü değişen";
            text.Append("\nKaynak nokta ").Append(sample.SourceVertexIndex+1).Append(": ").Append(type);
        }
        if(!paged && row.Report.SamplesTruncated)text.Append("\nÖrneklerin tamamı gösterilmiyor; yukarıdaki sayılar tüm incelenen dönüşleri kapsar.");
        return text.ToString();
    }
    private static string PageSummary(HpglTurnReview.Page page)
    {
        var total=page.Report.ChangedTurns;var count=page.Report.Samples.Count;
        return (count==0 ? $"Bu sayfada örnek yok. Toplam değişen dönüş: {total}." : $"Örnekler: {page.Offset+1}–{page.Offset+count} / {total}.")+
            (page.NextOffset is null ? " Son sayfa." : " Devamı için Sonraki düğmesini kullanın.");
    }
    private static string GeometryDescription(HpglPieceGeometryPreview geometry,HpglLengthReview.Result lengths,IReadOnlyList<HpglTurnReview.Sample> samples)
    {
        var text=new StringBuilder("Koordinatlar ve farklar mm cinsindedir. Kaynak, simülasyonda seçilen ölçeğin uygulanmış halidir; HPGL dosya ızgarasındaki değerdir. Çizimdeki işaretler semboliktir; boyutları fiziksel ölçü değildir. Önizleme makine uygunluğu onayı değildir.");
        text.Append("\nKenar uzunlukları (yaklaşık): ").Append(lengths.EdgeCount).Append(" kenar; kaynak toplam ").Append(MeasurementInput.Format(lengths.SourceLengthMm))
            .Append(" mm; HPGL toplam ").Append(MeasurementInput.Format(lengths.EncodedLengthMm)).Append(" mm; toplam fark ")
            .Append(MeasurementInput.Format(lengths.EncodedLengthMm-lengths.SourceLengthMm)).Append(" mm.")
            .Append(" En büyük mutlak kenar farkı: nokta ").Append(lengths.WorstStartIndex+1).Append(" → ").Append(lengths.WorstEndIndex+1)
            .Append("; HPGL − kaynak ").Append(MeasurementInput.Format(lengths.WorstSignedDifferenceMm)).Append(" mm.")
            .Append(" Toplam fark kenar değişikliklerini gizleyebilir; fiziksel çıktı veya ayrıntı korunumu doğrulanmaz.");
        if(samples.Count==0)return text.Append("\nBu sayfada işaretli örnek yok.").ToString();
        var points=geometry.Points;var indices=new List<int>(points.Count);
        bool SameSource(int a,int b)=>points[a].SourceX==points[b].SourceX && points[a].SourceY==points[b].SourceY;
        for(var i=0;i<points.Count;i++)if(i==0 || !SameSource(i,i-1))indices.Add(i);
        if(geometry.Closed && indices.Count>1 && SameSource(indices[0],indices[^1]))indices.RemoveAt(indices.Count-1);
        var positions=indices.Select((index,position)=>(index,position)).ToDictionary(x=>x.index,x=>x.position);
        string Coordinates(int i)
        {
            var p=points[i];var plotX=p.PlotX/geometry.UnitsPerMillimeter;var plotY=p.PlotY/geometry.UnitsPerMillimeter;
            return $"{i+1}: kaynak ({MeasurementInput.Format(p.SourceX)}, {MeasurementInput.Format(p.SourceY)}) mm; HPGL ({MeasurementInput.Format(plotX)}, {MeasurementInput.Format(plotY)}) mm; fark ({MeasurementInput.Format(plotX-p.SourceX)}, {MeasurementInput.Format(plotY-p.SourceY)}) mm";
        }
        foreach(var sample in samples)
        {
            var i=sample.SourceVertexIndex;if(!positions.TryGetValue(i,out var position))continue;
            text.Append("\nÖrnek kaynak nokta ").Append(i+1).Append(": ").Append(Coordinates(i));
            var previous=position-1;var next=position+1;
            if(geometry.Closed) { previous=(position+indices.Count-1)%indices.Count;next=(position+1)%indices.Count; }
            if(previous>=0)text.Append("\n  Önceki dönüş noktası ").Append(Coordinates(indices[previous]));
            if(next<indices.Count)text.Append("\n  Sonraki dönüş noktası ").Append(Coordinates(indices[next]));
        }
        return text.ToString();
    }
    private async Task ReviewPieceAsync(Row row,int offset)
    {
        var cancellation=new CancellationTokenSource();pieceReviewCancellation=cancellation;
        var token=cancellation.Token;var generation=selectionGeneration;
        pendingRow=row;pendingGeneration=generation;
        var before=BeforePieceReviewForVerification;var after=AfterPieceReviewForVerification;
        MoreSamplesButton.IsEnabled=false;PreviousPageButton.IsEnabled=NextPageButton.IsEnabled=false;CancelReviewButton.Visibility=Visibility.Visible;CancelReviewButton.IsEnabled=true;
        ClearComparison();GeometryText.Text="Karşılaştırma hazırlanıyor.";
        PageStatus.Text="Seçili parça inceleniyor; isterseniz iptal isteyebilirsiniz.";
        try
        {
            var result=await Task.Run(()=> { before?.Invoke();token.ThrowIfCancellationRequested();var page=preparedForReview.ReviewPiecePage(row.Report.PieceId,offset,PieceReviewPageSize,token);var geometry=preparedForReview.GetPieceGeometry(row.Report.PieceId,token);var lengths=HpglLengthReview.Analyze(geometry,token);after?.Invoke();token.ThrowIfCancellationRequested();return (Page:page,Geometry:geometry,Lengths:lengths); });
            token.ThrowIfCancellationRequested();
            if(!closed && generation==selectionGeneration && ReferenceEquals(ReportsGrid.SelectedItem,row))
            { currentPage=result.Page;pageRow=row;SelectedDetails.Text=Details(row with { Report=result.Page.Report },true);PageStatus.Text=PageSummary(result.Page);
                currentGeometry=result.Geometry;currentLengths=result.Lengths;GeometryComparison.SetData(result.Geometry,result.Page.Report.Samples.Select(s=>s.SourceVertexIndex));
                updatingSamples=true;SampleSelector.ItemsSource=result.Page.Report.Samples.Select(s=>new SampleChoice(s)).ToArray();SampleSelector.SelectedIndex=-1;updatingSamples=false;
                SampleSelector.IsEnabled=result.Page.Report.Samples.Count>0;ShowWholeButton.IsEnabled=true;UpdateZoomControls();
                GeometryText.Text=GeometryDescription(result.Geometry,result.Lengths,result.Page.Report.Samples); }
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested)
        { if(!closed && generation==selectionGeneration && ReferenceEquals(ReportsGrid.SelectedItem,row)) { SelectedDetails.Text=currentPage is not null && ReferenceEquals(pageRow,row) ? Details(row with { Report=currentPage.Report },true) : Details(row);PageStatus.Text=(currentPage is not null && ReferenceEquals(pageRow,row) ? PageSummary(currentPage)+" " : "")+"Ayrıntılı inceleme iptal edildi.";GeometryText.Text="Karşılaştırma iptal edildi; yeniden inceleyebilirsiniz."; } }
        catch(Exception ex)
        { if(!closed && generation==selectionGeneration && ReferenceEquals(ReportsGrid.SelectedItem,row)) { SelectedDetails.Text=(currentPage is not null && ReferenceEquals(pageRow,row) ? Details(row with { Report=currentPage.Report },true) : Details(row))+"\nAyrıntılı inceleme hatası: "+ex.Message;PageStatus.Text=(currentPage is not null && ReferenceEquals(pageRow,row) ? PageSummary(currentPage)+" " : "")+"İnceleme tamamlanamadı; ayrıntıları kontrol edin.";GeometryText.Text="Karşılaştırma oluşturulamadı: "+ex.Message; } }
        finally
        {
            if(ReferenceEquals(pieceReviewCancellation,cancellation)) { pieceReviewCancellation=null;pendingRow=null; }
            cancellation.Dispose();
            if(!closed)
            {
                CancelReviewButton.Visibility=Visibility.Collapsed;CancelReviewButton.IsEnabled=false;MoreSamplesButton.IsEnabled=ReportsGrid.SelectedItem is Row;
                var hasPage=currentPage is not null && ReferenceEquals(pageRow,ReportsGrid.SelectedItem);
                PreviousPageButton.Visibility=NextPageButton.Visibility=hasPage ? Visibility.Visible : Visibility.Collapsed;
                PreviousPageButton.IsEnabled=hasPage && currentPage!.Offset>0;
                NextPageButton.IsEnabled=hasPage && currentPage!.NextOffset.HasValue;
            }
        }
    }
    private void ClearComparison()
    {
        currentGeometry=null;currentLengths=null;GeometryComparison.Clear();updatingSamples=true;SampleSelector.ItemsSource=null;SampleSelector.SelectedIndex=-1;updatingSamples=false;
        SampleSelector.IsEnabled=false;ShowWholeButton.IsEnabled=false;UpdateZoomControls();
        GeometryText.Text="Karşılaştırma için bir parça seçip ayrıntılı incelemeyi başlatın.";
    }
    private void UpdateZoomControls()
    {
        var selected=SampleSelector.SelectedItem is SampleChoice;
        var factor=GeometryComparison.ZoomFactorForVerification;
        ZoomInButton.IsEnabled=selected && factor<8;ZoomOutButton.IsEnabled=selected && factor>1;ResetZoomButton.IsEnabled=selected && factor!=1;
        comparisonMode.Text=selected ? $"{MeasurementInput.Format(factor)}× · yakın" : "Tüm parça";
    }
    private static string ShortName(string value)
    {
        if(value.Length<=80)return SafeText(value);
        var length=char.IsHighSurrogate(value[79]) ? 79 : 80;
        return SafeText(value[..length])+"…";
    }
    private static string SafeText(string value)
    {
        var text=new StringBuilder();
        for(var i=0;i<value.Length;i++)
        {
            var c=value[i];
            if(char.IsHighSurrogate(c) && i+1<value.Length && char.IsLowSurrogate(value[i+1])) { text.Append(c);text.Append(value[++i]);continue; }
            var category=char.GetUnicodeCategory(c);
            if(char.IsSurrogate(c) || category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)text.Append($"\\u{(int)c:X4}");
            else text.Append(c);
        }
        return text.Length==0?"(boş)":text.ToString();
    }
}
