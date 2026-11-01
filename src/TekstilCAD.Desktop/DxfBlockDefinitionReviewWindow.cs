using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>Explicit POINT-to-vertex selections across unplaced block definitions.</summary>
internal sealed class DxfBlockDefinitionReviewWindow : Window
{
    private sealed record BlockChoice(int Header,string Label);
    private sealed record PointChoice(int Ordinal,string Label);
    private sealed record PieceChoice(Guid Id,string Label);
    private sealed record VertexChoice(int Index,string Label);
    internal sealed record BlockSelection(int Header,DxfPointVertexSelection Selection);
    private sealed record Mapping(BlockSelection Item,string Label);
    private readonly ObservableCollection<Mapping> mappings=[];
    private readonly ListBox mappingList=new() { DisplayMemberPath=nameof(Mapping.Label),Height=110,Margin=new Thickness(0,4,0,8) };
    internal IReadOnlyList<BlockSelection> Selections { get; private set; }=Array.Empty<BlockSelection>();

    internal DxfBlockDefinitionReviewWindow(Window owner,DxfImportResult import)
    {
        Owner=owner;Title="Blok tanımlarından çalışma projesi";Width=720;Height=650;MinWidth=500;MinHeight=380;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right };
        var cancel=new Button { Content="Vazgeç",IsCancel=true,Margin=new Thickness(4),Padding=new Thickness(10,7,10,7) };
        var apply=new Button { Content="Çalışma kopyasını hazırla",Margin=new Thickness(4),Padding=new Thickness(10,7,10,7) };
        actions.Children.Add(cancel);actions.Children.Add(apply);DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var body=new StackPanel();var scroll=new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Focusable=true };AutomationProperties.SetName(scroll,"Blok tanımı eşleme seçenekleri");root.Children.Add(scroll);
        body.Children.Add(new TextBlock { Text="Bu işlem yalnız eşleme eklediğiniz yerleştirilmemiş blok parçalarından ayrı bir çalışma projesi hazırlar; diğer kaynak parçaları projeye aktarılmaz. Farklı blok ve parçalardan en fazla 32 POINT/köşe çifti ekleyin. Önizlemede yalnız hedef köşe gösterilir; POINT koordinatını listeden kontrol edin. Eşleşmeler kaynak dosyadan doğrulanır. Yerleşim, fiziksel çentik veya üretim uygunluğu doğrulanmaz. Kaynak DXF ve açık proje değiştirilmez.",TextWrapping=TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text="Kaynak okuma uyarıları",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,8,0,4) });
        foreach(var warning in import.CapturedWarnings)
            body.Children.Add(new TextBlock { Text="• "+SafeNotice(warning),TextWrapping=TextWrapping.Wrap });
        var blocks=new ComboBox { DisplayMemberPath=nameof(BlockChoice.Label),Margin=new Thickness(0,5,0,8) };
        var points=new ComboBox { DisplayMemberPath=nameof(PointChoice.Label),Margin=new Thickness(0,5,0,8) };
        var pieces=new ComboBox { DisplayMemberPath=nameof(PieceChoice.Label),Margin=new Thickness(0,5,0,8) };
        var vertices=new ComboBox { DisplayMemberPath=nameof(VertexChoice.Label),Margin=new Thickness(0,5,0,8) };
        var pointOrdinalText=new TextBox { Width=120,MaxLength=10,Margin=new Thickness(0,4,4,4) };
        AutomationProperties.SetName(pointOrdinalText,"Kaynak POINT kayıt sırasına git");
        AutomationProperties.SetHelpText(pointOrdinalText,"Seçili bloktaki kaynak POINT'in sıfırdan başlayan kayıt sırasını girin. Arama eşleme eklemez; sonucu ayrıca seçin ve inceleyin.");
        var pointSearchRow=new WrapPanel();pointSearchRow.Children.Add(pointOrdinalText);
        var pointGo=new Button { Content="POINT kaydına git",Margin=new Thickness(4) };pointSearchRow.Children.Add(pointGo);
        var status=new TextBlock { TextWrapping=TextWrapping.Wrap };AutomationProperties.SetName(status,"Blok eşleme durumu");AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);
        void SetStatus(string message)
        {
            status.Text=message;AutomationProperties.SetName(status,message.Length==0 ? "Blok eşleme durumu" : "Blok eşleme durumu: "+message);
            if(System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(status) is { } peer)
                peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
        foreach(var (label,box) in new[]{("Kaynak blok tanımı",blocks),("Kaynak POINT",points),("Hedef POLYLINE parçası",pieces),("Hedef köşe",vertices)})
        {
            body.Children.Add(new TextBlock { Text=label,FontWeight=FontWeights.SemiBold });AutomationProperties.SetName(box,label);box.MaxDropDownHeight=220;
            box.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
            VirtualizingPanel.SetIsVirtualizing(box,true);VirtualizingPanel.SetVirtualizationMode(box,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(box,true);body.Children.Add(box);
            if(ReferenceEquals(box,points)) { body.Children.Add(new TextBlock { Text="Kaynak POINT kayıt sırasıyla ara (0'dan başlar)",TextWrapping=TextWrapping.Wrap });body.Children.Add(pointSearchRow); }
        }
        var preview=new DxfPiecePreview { Height=180 };AutomationProperties.SetName(preview,"Seçilen blok konturu");body.Children.Add(preview);
        body.Children.Add(status);
        var indexText=new TextBox { Width=90,MaxLength=8,Margin=new Thickness(0,4,4,4) };AutomationProperties.SetName(indexText,"Köşe indeksine git");
        var links=import.PieceSources!.Value.Where(p=>p.Section=="BLOCKS" && p.EntityType=="POLYLINE" && p.BlockHeaderOrdinal.HasValue).ToArray();
        var pointRecords=import.SupplementalGeometry!.Records.Where(p=>p.Section=="BLOCKS" && p.EntityType=="POINT" && p.BlockHeaderOrdinal.HasValue).ToArray();
        bool restoringQueuedSelection=false;
        void ClearQueueFocus() { if(!restoringQueuedSelection)mappingList.SelectedIndex=-1; }
        blocks.ItemsSource=links.GroupBy(p=>p.BlockHeaderOrdinal!.Value).Select(g=>new BlockChoice(g.Key,$"Blok kaydı {g.Key} · {Safe(g.First().Block)}")).ToArray();
        blocks.SelectionChanged+=(_,_)=>
        {
            ClearQueueFocus();points.ItemsSource=null;pieces.ItemsSource=null;vertices.ItemsSource=null;indexText.Clear();pointOrdinalText.Clear();SetStatus("");
            if(blocks.SelectedItem is not BlockChoice block)return;
            points.ItemsSource=pointRecords.Where(p=>p.BlockHeaderOrdinal==block.Header).Select(p=>new PointChoice(p.SourceOrdinal,
                $"POINT {p.SourceOrdinal} · X {Safe(p.Groups.FirstOrDefault(g=>g.Code==10)?.RawValue)} · Y {Safe(p.Groups.FirstOrDefault(g=>g.Code==20)?.RawValue)} · Z {Safe(p.Groups.FirstOrDefault(g=>g.Code==30)?.RawValue)} · katman {Safe(p.Groups.FirstOrDefault(g=>g.Code==8)?.RawValue)}")).ToArray();
            var selectedIds=links.Where(p=>p.BlockHeaderOrdinal==block.Header).Select(p=>p.PieceId).ToHashSet();
            pieces.ItemsSource=import.Project.Pieces.Where(p=>selectedIds.Contains(p.Id) && p.Closed).Select(p=>new PieceChoice(p.Id,$"{Safe(p.Name)} · {p.Id:D}")).ToArray();
        };
        pieces.SelectionChanged+=(_,_)=>
        {
            ClearQueueFocus();vertices.ItemsSource=null;indexText.Clear();SetStatus("");
            if(pieces.SelectedItem is not PieceChoice selected) { preview.Clear();return; }
            var piece=import.Project.Pieces.Single(p=>p.Id==selected.Id);
            preview.ShowPiece(piece);
            vertices.ItemsSource=piece.Points.Select((p,i)=>new VertexChoice(i,$"{i} · X {p.X:R} · Y {p.Y:R} mm")).ToArray();
        };
        bool changingText=false,changingSelection=false,selectingBySearch=false;
        bool changingPointText=false,changingPointSelection=false,selectingPointBySearch=false;
        points.SelectionChanged+=(_,_ )=>
        {
            ClearQueueFocus();
            if(!changingPointSelection && !selectingPointBySearch)
            {
                changingPointText=true;try { pointOrdinalText.Clear(); } finally { changingPointText=false; }
                if(points.SelectedItem is PointChoice selected)SetStatus($"POINT kaydı {selected.Ordinal} seçildi; koordinatı ve katmanı inceleyin. Henüz eşleme eklenmedi.");
            }
        };
        pointOrdinalText.TextChanged+=(_,_ )=>
        {
            if(changingPointText)return;
            changingPointSelection=true;try { points.SelectedIndex=-1; } finally { changingPointSelection=false; }
            SetStatus(pointOrdinalText.Text.Length==0 ? "" : "Kayıt sırasını yazdınız; POINT kaydına git düğmesiyle seçin. Listedeki eşlemeler değişmedi.");
        };
        pointGo.Click+=(_,_ )=>
        {
            if(!int.TryParse(pointOrdinalText.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var ordinal) ||
               points.Items.Cast<PointChoice>().FirstOrDefault(p=>p.Ordinal==ordinal) is not PointChoice found)
            { changingPointSelection=true;try { points.SelectedIndex=-1; } finally { changingPointSelection=false; }SetStatus("Bu blokta belirtilen POINT kayıt sırası bulunamadı; önceki POINT seçimi temizlendi.");return; }
            selectingPointBySearch=true;try { points.SelectedItem=found; } finally { selectingPointBySearch=false; }
            SetStatus($"POINT kaydı {ordinal} seçildi; koordinatı ve katmanı inceleyin. Henüz eşleme eklenmedi.");
        };
        vertices.SelectionChanged+=(_,_)=>
        {
            ClearQueueFocus();
            preview.SelectVertex((vertices.SelectedItem as VertexChoice)?.Index);
            if(!changingSelection && !selectingBySearch)
            { changingText=true;try { indexText.Clear(); } finally { changingText=false; } }
        };
        var indexRow=new WrapPanel();
        indexText.TextChanged+=(_,_)=> { if(changingText)return;changingSelection=true;try { vertices.SelectedIndex=-1; } finally { changingSelection=false; } SetStatus(""); };
        var go=new Button { Content="Köşeye git",Margin=new Thickness(4) };go.Click+=(_,_)=> { if(int.TryParse(indexText.Text,out var index) && index>=0 && index<vertices.Items.Count) { selectingBySearch=true;try { vertices.SelectedIndex=index; } finally { selectingBySearch=false; } SetStatus(""); } else { changingSelection=true;try { vertices.SelectedIndex=-1; } finally { changingSelection=false; } SetStatus("Geçerli bir köşe indeksi girin."); } };indexRow.Children.Add(indexText);indexRow.Children.Add(go);body.Children.Add(indexRow);
        var mappingActions=new WrapPanel();var add=new Button { Content="Seçilen çifti ekle",Margin=new Thickness(4),Padding=new Thickness(8,5,8,5) };
        var remove=new Button { Content="Seçili çifti kaldır",Margin=new Thickness(4),Padding=new Thickness(8,5,8,5) };
        mappingActions.Children.Add(add);mappingActions.Children.Add(remove);body.Children.Add(mappingActions);
        mappingList.ItemsSource=mappings;AutomationProperties.SetName(mappingList,"Seçilen POINT ve köşe çiftleri");body.Children.Add(mappingList);
        add.Click+=(_,_)=>
        {
            if(blocks.SelectedItem is not BlockChoice b || points.SelectedItem is not PointChoice p || pieces.SelectedItem is not PieceChoice piece || vertices.SelectedItem is not VertexChoice v)
            { SetStatus("Önce blok, POINT, parça ve köşeyi seçin.");return; }
            if(mappings.Any(m=>m.Item.Selection.SourceOrdinal==p.Ordinal || (m.Item.Selection.PieceId==piece.Id && m.Item.Selection.VertexIndex==v.Index)))
            { SetStatus("Her POINT ve köşe yalnız bir kez eklenebilir.");return; }
            if(mappings.Count>=32) { SetStatus("Bir çalışma kopyasında en fazla 32 eşleme seçilebilir.");return; }
            mappings.Add(new(new(b.Header,new(p.Ordinal,piece.Id,v.Index)),$"{b.Label} · {piece.Label}\n{p.Label} → köşe {v.Label}"));
            SetStatus($"{mappings.Count} çift listede; kaynakla tam eşleşme henüz doğrulanmadı.");
        };
        mappingList.SelectionChanged+=(_,_ )=>
        {
            if(mappingList.SelectedItem is not Mapping selected)return;
            restoringQueuedSelection=true;
            try
            {
                blocks.SelectedItem=blocks.Items.Cast<BlockChoice>().FirstOrDefault(b=>b.Header==selected.Item.Header);
                pieces.SelectedItem=pieces.Items.Cast<PieceChoice>().FirstOrDefault(p=>p.Id==selected.Item.Selection.PieceId);
                points.SelectedItem=points.Items.Cast<PointChoice>().FirstOrDefault(p=>p.Ordinal==selected.Item.Selection.SourceOrdinal);
                vertices.SelectedItem=vertices.Items.Cast<VertexChoice>().FirstOrDefault(v=>v.Index==selected.Item.Selection.VertexIndex);
            }
            finally { restoringQueuedSelection=false; }
            SetStatus("Yalnız hedef köşe gösteriliyor; POINT koordinatını listeden kontrol edin. Eşleşme işlem sırasında doğrulanacak.");
        };
        remove.Click+=(_,_)=> { if(mappingList.SelectedItem is Mapping selected)mappings.Remove(selected);SetStatus($"{mappings.Count} çift listede."); };
        var confirm=new CheckBox { Content="Bunların yerleştirilmemiş blok tanımları olduğunu anladım",Margin=new Thickness(0,12,0,8) };body.Children.Add(confirm);
        apply.Click+=(_,_)=>
        {
            if((pointOrdinalText.Text.Length>0 && points.SelectedItem is null) || (indexText.Text.Length>0 && vertices.SelectedItem is null))
            { SetStatus("Yazılan POINT veya köşe aramasını tamamlayın ya da temizleyin; listedeki çiftler korunuyor.");return; }
            if(confirm.IsChecked!=true || mappings.Count==0)
            { SetStatus("En az bir çifti listeye ekleyin ve açık onay verin.");return; }
            Selections=Array.AsReadOnly(mappings.Select(m=>m.Item).ToArray());DialogResult=true;
        };
    }
    private static bool Escape(char c)=>char.IsControl(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Format or System.Globalization.UnicodeCategory.Surrogate or System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator;
    private static string Safe(string? value)=>string.IsNullOrEmpty(value) ? "(boş)" : string.Concat(value.Take(120).Select(c=>Escape(c) ? $"\\u{(int)c:X4}" : c.ToString()));
    private static string SafeNotice(string value)=>string.Concat(value.Select(c=>Escape(c) ? $"\\u{(int)c:X4}" : c.ToString()));
}
