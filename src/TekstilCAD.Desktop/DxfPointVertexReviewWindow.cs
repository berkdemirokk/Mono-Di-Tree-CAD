using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>Explicit optional POINT-to-vertex choices; source data is never mutated.</summary>
internal sealed class DxfPointVertexReviewWindow : Window
{
    private sealed record PointChoice(int Ordinal,string Label);
    private sealed record PieceChoice(PatternPiece Piece,string Label);
    private sealed record VertexChoice(int Index,string Label);
    private sealed record Mapping(DxfPointVertexSelection Selection,string Label);
    private readonly ComboBox points=new() { DisplayMemberPath=nameof(PointChoice.Label),MaxDropDownHeight=220,Margin=new Thickness(0,4,0,8) };
    private readonly ComboBox pieces=new() { DisplayMemberPath=nameof(PieceChoice.Label),MaxDropDownHeight=220,Margin=new Thickness(0,4,0,8) };
    private readonly ComboBox vertices=new() { DisplayMemberPath=nameof(VertexChoice.Label),MaxDropDownHeight=220,Margin=new Thickness(0,4,0,8) };
    private readonly TextBox vertexIndex=new() { MaxLength=5,Margin=new Thickness(0,2,0,8) };
    private readonly ObservableCollection<Mapping> mappings=[];
    private readonly ListBox mappingList=new() { ItemsSource=null,DisplayMemberPath=nameof(Mapping.Label),Height=110 };
    private readonly DxfPiecePreview preview=new() { Height=180 };
    private readonly TextBlock status=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8) };
    internal IReadOnlyList<DxfPointVertexSelection> Selections { get; private set; }=Array.Empty<DxfPointVertexSelection>();

    internal DxfPointVertexReviewWindow(Window owner,DxfImportResult import,ProjectDocument display)
    {
        Owner=owner;Title="DXF POINT köşe işaretlerini incele";Width=760;Height=700;MinWidth=560;MinHeight=480;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };
        var cancel=new Button { Content="Aktarımı iptal et",IsCancel=true,Padding=new Thickness(10,7,10,7),Margin=new Thickness(4) };
        var skip=new Button { Content="İşaret eklemeden devam",Padding=new Thickness(10,7,10,7),Margin=new Thickness(4) };
        var apply=new Button { Content="Seçimleri onayla",Padding=new Thickness(10,7,10,7),Margin=new Thickness(4),IsEnabled=false };
        cancel.Click+=(_,_)=>DialogResult=false;skip.Click+=(_,_)=> { Selections=Array.Empty<DxfPointVertexSelection>();DialogResult=true; };
        apply.Click+=(_,_)=> { Selections=Array.AsReadOnly(mappings.Select(m=>m.Selection).ToArray());DialogResult=true; };
        actions.Children.Add(cancel);actions.Children.Add(skip);actions.Children.Add(apply);DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var body=new StackPanel();var scroll=new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Focusable=true };
        AutomationProperties.SetName(scroll,"DXF POINT eşleme seçenekleri");root.Children.Add(scroll);
        body.Children.Add(new TextBlock { Text="Her kaynak POINT ve hedef köşeyi açıkça seçin. Yalnız desteklenen doğrudan kapalı çoklu çizgi köşeleri tam eşleşirse kabul edilir. Bu sarı görsel işarettir; fiziksel çentik veya üretim onayı değildir. Kaynak POINT projeye ayrıca aktarılmaz.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) });
        var candidates=import.SupplementalGeometry?.Records.Where(r=>r.EntityType=="POINT" && r.Section=="ENTITIES" && r.Block is null)
            .Select(r=>new PointChoice(r.SourceOrdinal,$"Kayıt {r.SourceOrdinal} · X {Safe(r.Groups.FirstOrDefault(g=>g.Code==10)?.RawValue)} · Y {Safe(r.Groups.FirstOrDefault(g=>g.Code==20)?.RawValue)} · katman {Safe(r.Groups.FirstOrDefault(g=>g.Code==8)?.RawValue)}" )).ToArray() ?? [];
        points.ItemsSource=candidates;pieces.ItemsSource=display.Pieces.Where(p=>p.Closed).Select(p=>new PieceChoice(p,$"{Safe(p.Name)} · {p.Id:D}")).ToArray();
        foreach(var (label,box) in new[] { ("Kaynak POINT kaydı",points),("Hedef kapalı parça",pieces),("Hedef köşe indeksi ve konumu · mm",vertices) })
        {
            body.Children.Add(new TextBlock { Text=label,FontWeight=FontWeights.SemiBold });AutomationProperties.SetName(box,label);
            box.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
            VirtualizingPanel.SetIsVirtualizing(box,true);VirtualizingPanel.SetVirtualizationMode(box,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(box,true);body.Children.Add(box);
        }
        body.Children.Add(new TextBlock { Text="Köşeye indeksle git (0'dan başlar)",TextWrapping=TextWrapping.Wrap });
        AutomationProperties.SetName(vertexIndex,"Hedef köşe indeksine git");
        AutomationProperties.SetHelpText(vertexIndex,"Seçili parçanın sıfırdan başlayan köşe indeksini girin. Geçersiz indeks önceki köşe seçimini temizler.");body.Children.Add(vertexIndex);
        body.Children.Add(new TextBlock { Text="Seçilen köşe sarı halkayla gösterilir; kaynak POINT ile eşleşme onay sonrası doğrulanır.",TextWrapping=TextWrapping.Wrap });
        AutomationProperties.SetName(preview,"Seçilen hedef konturun önizlemesi");body.Children.Add(preview);
        pieces.SelectionChanged+=(_,_)=>
        {
            vertices.ItemsSource=null;vertexIndex.Clear();
            if(pieces.SelectedItem is not PieceChoice choice) { preview.Clear();status.Text="Hedef parça ve köşe seçimi temizlendi.";return; }
            preview.ShowPiece(choice.Piece);
            vertices.ItemsSource=choice.Piece.Points.Select((p,i)=>new VertexChoice(i,$"{i} · X {p.X.ToString("R",CultureInfo.InvariantCulture)} · Y {p.Y.ToString("R",CultureInfo.InvariantCulture)}")).ToArray();
            status.Text="Hedef parça seçildi; köşe seçin. Kaynak POINT ile eşleşme henüz doğrulanmadı.";
        };
        vertexIndex.TextChanged+=(_,_ )=>
        {
            if(vertexIndex.Text.Length==0) { vertices.SelectedIndex=-1;status.Text="Köşe seçimi temizlendi.";return; }
            if(!int.TryParse(vertexIndex.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var index) || index<0 || index>=vertices.Items.Count)
            { vertices.SelectedIndex=-1;status.Text="Geçerli bir köşe indeksi girin; önceki köşe seçimi temizlendi.";return; }
            vertices.SelectedIndex=index;status.Text=$"Köşe {index} seçildi; konumunu ve kaynak POINT kaydını karşılaştırın.";
        };
        vertices.SelectionChanged+=(_,_ )=>
        {
            var selected=(vertices.SelectedItem as VertexChoice)?.Index;preview.SelectVertex(selected);
            if(selected is int index)
            {
                var text=index.ToString(CultureInfo.InvariantCulture);if(vertexIndex.Text!=text)vertexIndex.Text=text;
            }
        };
        var row=new WrapPanel();var add=new Button { Content="Seçilen eşlemeyi ekle",Padding=new Thickness(10,7,10,7),Margin=new Thickness(4) };
        var remove=new Button { Content="Seçili eşlemeyi kaldır",Padding=new Thickness(10,7,10,7),Margin=new Thickness(4) };
        add.Click+=(_,_)=>
        {
            if(points.SelectedItem is not PointChoice point || pieces.SelectedItem is not PieceChoice piece || vertices.SelectedItem is not VertexChoice vertex)
            { status.Text="POINT, parça ve köşe seçin.";return; }
            if(mappings.Any(m=>m.Selection.SourceOrdinal==point.Ordinal || (m.Selection.PieceId==piece.Piece.Id && m.Selection.VertexIndex==vertex.Index)))
            { status.Text="Her POINT ve hedef köşe yalnız bir kez seçilebilir.";return; }
            mappings.Add(new(new(point.Ordinal,piece.Piece.Id,vertex.Index),$"{point.Label} → {piece.Label} · köşe {vertex.Label}"));
            status.Text=$"{mappings.Count} eşleme seçildi; kaynak ve tam eşleşme uygulamadan önce doğrulanacak.";apply.IsEnabled=true;
        };
        remove.Click+=(_,_)=> { if(mappingList.SelectedItem is Mapping selected)mappings.Remove(selected);apply.IsEnabled=mappings.Count>0;status.Text=$"{mappings.Count} eşleme seçili."; };
        row.Children.Add(add);row.Children.Add(remove);body.Children.Add(row);mappingList.ItemsSource=mappings;AutomationProperties.SetName(mappingList,"Seçilen POINT köşe eşlemeleri");body.Children.Add(mappingList);
        AutomationProperties.SetName(status,"POINT eşleme durumu");AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);body.Children.Add(status);
        status.Text=$"{candidates.Length} kaynak POINT kaydı listelendi; hiçbiri seçili değil.";
    }
    private static string Safe(string? text)
    {
        if(string.IsNullOrEmpty(text))return "(boş)";
        return string.Concat(text.Take(120).Select(c=>char.IsControl(c) || char.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.Surrogate ? $"\\u{(int)c:X4}" : c.ToString()));
    }
}
