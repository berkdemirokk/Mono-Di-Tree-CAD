using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>Explicit, optional associations in one captured DXF import; never mutates the import.</summary>
internal sealed class DxfGrainlineReviewWindow : Window
{
    private sealed record Candidate(PatternPiece Piece,string Label,DxfPieceSource? Source);
    private sealed record MappingRow(DxfGrainlineSelection Selection,string Label);
    private readonly ComboBox contours=Selector(),markers=Selector();
    private readonly ObservableCollection<MappingRow> mappings=[];
    private readonly ListBox mappingList=new() { MaxHeight=115,DisplayMemberPath=nameof(MappingRow.Label),HorizontalContentAlignment=HorizontalAlignment.Stretch };
    private readonly DxfPiecePreview contourPreview=new() { Height=125 },markerPreview=new() { Height=125 };
    private readonly DxfGrainlineOverlayPreview overlay=new() { Height=200,Margin=new Thickness(0,4,0,8) };
    private readonly TextBox contourDetails=Details(),markerDetails=Details();
    private readonly TextBlock status=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,4) };
    private readonly Button apply=new() { Content="Eşlemeleri onayla ve devam et",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,0) };
    internal IReadOnlyList<DxfGrainlineSelection> Selections { get; private set; }=Array.Empty<DxfGrainlineSelection>();

    internal DxfGrainlineReviewWindow(Window owner,DxfImportResult import)
    {
        ArgumentNullException.ThrowIfNull(import);
        var snapshot=import.Project.Clone();Owner=owner;Title="DXF iplik yönü eşlemelerini incele";
        Width=950;Height=800;MinWidth=650;MinHeight=520;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel { Margin=new Thickness(0,8,0,0),HorizontalAlignment=HorizontalAlignment.Right };
        var cancel=new Button { Content="Aktarımı iptal et",IsCancel=true,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,0) };
        var skip=new Button { Content="Eşleme yapmadan devam",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,0) };
        cancel.Click+=(_,_)=>DialogResult=false;
        skip.Click+=(_,_)=> { Selections=Array.Empty<DxfGrainlineSelection>();DialogResult=true; };
        apply.Click+=(_,_)=> { if(mappings.Count==0)return;Selections=Array.AsReadOnly(mappings.Select(row=>row.Selection).ToArray());DialogResult=true; };
        actions.Children.Add(cancel);actions.Children.Add(skip);actions.Children.Add(apply);DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var body=new StackPanel();root.Children.Add(new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });
        body.Children.Add(new TextBlock { Text="İplik yönü eşlemelerini incele",FontSize=20,FontWeight=FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text="Bu adım isteğe bağlıdır. Hiçbir açık çizginin iplik yönü veya kesim çizgisi olduğu otomatik varsayılmaz. Kaynak dosyayı inceleyip her kapalı kontur ve iki uçlu açık çizgiyi özellikle seçin. Onaylanan açık çizgi aktarılacak kopyada ayrı parça olmaktan çıkarılır; uçları seçilen parçanın kontrol bekleyen iplik yönü olur. Diğer çizgiler korunur. Bu işlem fiziksel yön veya üretim uygunluğunu doğrulamaz.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8) });
        var source=new TextBox { IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Text=$"Kaynak SHA-256: {import.SourceSha256 ?? "bilinmiyor"}\nKaynak boyutu: {(import.SourceByteLength?.ToString(CultureInfo.InvariantCulture) ?? "bilinmiyor")} bayt\nKimlikler bu aktarımın parçalarına aittir; DXF nesne tanıtıcısı oldukları varsayılmaz.",Margin=new Thickness(0,0,0,8) };
        AutomationProperties.SetName(source,"Aktarım kaynağı kimliği");body.Children.Add(source);
        // Historical DXF provenance is shown only when it is bound to this exact
        // geometry snapshot; otherwise a familiar layer name could mislead.
        var sources=new Dictionary<Guid,DxfPieceSource>();
        if(import.PieceSources is { } captured && !captured.IsDefault && import.PieceSourcesFingerprint is { } fingerprint &&
           string.Equals(fingerprint,NestingSnapshot.ComputeLayoutFingerprint(snapshot),StringComparison.Ordinal))
            foreach(var group in captured.GroupBy(item=>item.PieceId))if(group.Count()==1)sources.Add(group.Key,group.First());
        Candidate MakeCandidate(PatternPiece p) { sources.TryGetValue(p.Id,out var info);return new Candidate(p,Label(p),info); }
        var targetChoices=snapshot.Pieces.Where(p=>p.Closed && p.Points.Count>=3 && p.Grainline is null).Select(MakeCandidate).ToArray();
        var markerChoices=snapshot.Pieces.Where(p=>!p.Closed && p.Points.Count==2 && p.Points[0]!=p.Points[1] && p.Grainline is null && p.Metadata is null).Select(MakeCandidate).ToArray();
        contours.ItemsSource=targetChoices;markers.ItemsSource=markerChoices;
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition());
        AddColumn(grid,0,"Kapalı hedef kontur",contours,contourPreview,contourDetails);
        AddColumn(grid,1,"İki uçlu açık çizgi",markers,markerPreview,markerDetails);body.Children.Add(grid);
        contours.SelectionChanged+=(_,_)=> { ShowSelection(contours,contourPreview,contourDetails);UpdateOverlay(); };
        markers.SelectionChanged+=(_,_)=> { ShowSelection(markers,markerPreview,markerDetails);UpdateOverlay(); };
        body.Children.Add(new TextBlock { Text="Birlikte görünüm · ortak koordinat ve ölçek",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,8,0,0) });
        body.Children.Add(new TextBlock { Text="Kontur koyu gri, seçili çizgi kırmızıdır. Konum ve ölçek ortaktır; katman veya konum çizginin anlamını belirlemez.",TextWrapping=TextWrapping.Wrap });
        body.Children.Add(overlay);
        var rowActions=new WrapPanel { Margin=new Thickness(0,8,0,4) };
        var add=new Button { Content="Seçilen ikiliyi ekle",Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,0) };
        var remove=new Button { Content="Seçili eşlemeyi kaldır",Padding=new Thickness(12,7,12,7) };
        add.Click+=(_,_)=>
        {
            if(contours.SelectedItem is not Candidate c || markers.SelectedItem is not Candidate m) { status.Text="Önce bir kapalı kontur ve bir açık çizgi seçin.";return; }
            if(mappings.Any(row=>row.Selection.ContourId==c.Piece.Id || row.Selection.MarkerId==m.Piece.Id)) { status.Text="Her kontur ve açık çizgi yalnız bir eşlemede kullanılabilir.";return; }
            mappings.Add(new MappingRow(new DxfGrainlineSelection(c.Piece.Id,m.Piece.Id),$"Kontur {c.Label}  ←  çizgi {m.Label}"));
            status.Text=$"{mappings.Count} eşleme seçildi. Açık çizgilerin aktarılacak kopyadan kaldırılacağını ve uçların kontrol bekleyeceğini inceleyin.";apply.IsEnabled=true;
        };
        remove.Click+=(_,_)=> { if(mappingList.SelectedItem is MappingRow selected)mappings.Remove(selected);apply.IsEnabled=mappings.Count>0;status.Text=$"{mappings.Count} eşleme seçili."; };
        rowActions.Children.Add(add);rowActions.Children.Add(remove);body.Children.Add(rowActions);
        AutomationProperties.SetName(mappingList,"Seçilen iplik yönü eşlemeleri");mappingList.ItemsSource=mappings;body.Children.Add(mappingList);
        AutomationProperties.SetName(status,"İplik yönü eşleme durumu");AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);body.Children.Add(status);
        apply.IsEnabled=false;status.Text=$"{targetChoices.Length} kapalı kontur, {markerChoices.Length} iki uçlu açık çizgi seçilebilir. Hiçbir eşleme seçilmedi.";
    }
    private void UpdateOverlay()
    {
        var contour=(contours.SelectedItem as Candidate)?.Piece;
        var marker=(markers.SelectedItem as Candidate)?.Piece;
        overlay.ShowPair(contour,marker);
    }
    private static ComboBox Selector()
    {
        var box=new ComboBox { DisplayMemberPath=nameof(Candidate.Label),IsTextSearchEnabled=true,MaxDropDownHeight=220,Margin=new Thickness(0,4,0,4) };
        box.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        VirtualizingPanel.SetIsVirtualizing(box,true);VirtualizingPanel.SetVirtualizationMode(box,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(box,true);return box;
    }
    private static TextBox Details()=>new() { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=95,Margin=new Thickness(0,4,0,0) };
    private static void AddColumn(Grid grid,int column,string name,ComboBox box,DxfPiecePreview preview,TextBox details)
    {
        var panel=new StackPanel { Margin=new Thickness(column==0?0:8,0,column==0?8:0,0) };Grid.SetColumn(panel,column);grid.Children.Add(panel);
        panel.Children.Add(new TextBlock { Text=name,FontWeight=FontWeights.SemiBold });AutomationProperties.SetName(box,name);panel.Children.Add(box);
        AutomationProperties.SetName(details,name+" kimlik ve geometri ayrıntıları");panel.Children.Add(preview);panel.Children.Add(details);
    }
    private static void ShowSelection(ComboBox box,DxfPiecePreview preview,TextBox details)
    {
        if(box.SelectedItem is not Candidate c) { preview.Clear();details.Text="Seçim yapılmadı.";return; }
        var p=c.Piece;var source=p.Points.Count==2 ? $"\nBaşlangıç: X {MeasurementInput.Format(p.Points[0].X)}, Y {MeasurementInput.Format(p.Points[0].Y)} mm\nBitiş: X {MeasurementInput.Format(p.Points[1].X)}, Y {MeasurementInput.Format(p.Points[1].Y)} mm" : "";
        var origin=c.Source is { } record
            ? $"\nKaynak bölüm: {Safe(record.Section)} · blok: {Safe(record.Block)} · katman: {Safe(record.Layer)} · tür: {Safe(record.EntityType)}\nKaynak kayıt sıraları: {string.Join(", ",record.SourceOrdinals.Take(16))}{(record.SourceOrdinals.Length>16 ? " …" : "")} (semantik doğrulaması değildir)"
            : "\nKaynak katman/kayıt eşleşmesi doğrulanamadı veya mevcut değil.";
        details.Text=$"Ad: {Safe(p.Name)}\nAktarım parça kimliği: {p.Id:D}{origin}\n{preview.ShowPiece(p)}"+source;
    }
    private static string Label(PatternPiece p)=>$"{Safe(p.Name)} · {p.Id:D}";
    private static string Safe(string? value)
    {
        if(string.IsNullOrEmpty(value))return "(boş)";
        var result=new StringBuilder();foreach(char c in value)
        { var category=char.GetUnicodeCategory(c);if(char.IsControl(c) || category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator || char.IsSurrogate(c))result.Append($"\\u{(int)c:X4}");else result.Append(c); }
        return result.ToString();
    }
}
