using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>Explicit selection of one unplaced block definition, POINT and vertex.</summary>
internal sealed class DxfBlockDefinitionReviewWindow : Window
{
    private sealed record BlockChoice(int Header,string Label);
    private sealed record PointChoice(int Ordinal,string Label);
    private sealed record PieceChoice(Guid Id,string Label);
    private sealed record VertexChoice(int Index,string Label);
    internal DxfPointVertexSelection? Selection { get; private set; }
    internal int BlockHeaderOrdinal { get; private set; }

    internal DxfBlockDefinitionReviewWindow(Window owner,DxfImportResult import)
    {
        Owner=owner;Title="Blok tanımından çalışma kopyası";Width=720;Height=650;MinWidth=500;MinHeight=380;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right };
        var cancel=new Button { Content="Vazgeç",IsCancel=true,Margin=new Thickness(4),Padding=new Thickness(10,7,10,7) };
        var apply=new Button { Content="Çalışma kopyasını hazırla",Margin=new Thickness(4),Padding=new Thickness(10,7,10,7) };
        actions.Children.Add(cancel);actions.Children.Add(apply);DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var body=new StackPanel();var scroll=new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Focusable=true };AutomationProperties.SetName(scroll,"Blok tanımı eşleme seçenekleri");root.Children.Add(scroll);
        body.Children.Add(new TextBlock { Text="Bu işlem yerleştirilmemiş bir blok tanımından tek parça çalışma kopyası hazırlar. Yerleşim, fiziksel çentik veya üretim uygunluğu doğrulanmaz. Kaynak DXF ve açık proje değiştirilmez.",TextWrapping=TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text="Kaynak okuma uyarıları",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,8,0,4) });
        foreach(var warning in import.CapturedWarnings)
            body.Children.Add(new TextBlock { Text="• "+SafeNotice(warning),TextWrapping=TextWrapping.Wrap });
        var blocks=new ComboBox { DisplayMemberPath=nameof(BlockChoice.Label),Margin=new Thickness(0,5,0,8) };
        var points=new ComboBox { DisplayMemberPath=nameof(PointChoice.Label),Margin=new Thickness(0,5,0,8) };
        var pieces=new ComboBox { DisplayMemberPath=nameof(PieceChoice.Label),Margin=new Thickness(0,5,0,8) };
        var vertices=new ComboBox { DisplayMemberPath=nameof(VertexChoice.Label),Margin=new Thickness(0,5,0,8) };
        var status=new TextBlock { TextWrapping=TextWrapping.Wrap };AutomationProperties.SetName(status,"Blok eşleme durumu");AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);
        void SetStatus(string message)
        {
            status.Text=message;AutomationProperties.SetName(status,message.Length==0 ? "Blok eşleme durumu" : "Blok eşleme durumu: "+message);
            if(System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(status) is { } peer)
                peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
        foreach(var (label,box) in new[]{("Kaynak blok tanımı",blocks),("Kaynak POINT",points),("Hedef POLYLINE parçası",pieces),("Hedef köşe",vertices)})
        { body.Children.Add(new TextBlock { Text=label,FontWeight=FontWeights.SemiBold });AutomationProperties.SetName(box,label);box.MaxDropDownHeight=220;VirtualizingPanel.SetIsVirtualizing(box,true);VirtualizingPanel.SetVirtualizationMode(box,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(box,true);body.Children.Add(box); }
        var preview=new DxfPiecePreview { Height=180 };AutomationProperties.SetName(preview,"Seçilen blok konturu");body.Children.Add(preview);
        body.Children.Add(status);
        var indexText=new TextBox { Width=90,MaxLength=8,Margin=new Thickness(0,4,4,4) };AutomationProperties.SetName(indexText,"Köşe indeksine git");
        var links=import.PieceSources!.Value.Where(p=>p.Section=="BLOCKS" && p.EntityType=="POLYLINE" && p.BlockHeaderOrdinal.HasValue).ToArray();
        var pointRecords=import.SupplementalGeometry!.Records.Where(p=>p.Section=="BLOCKS" && p.EntityType=="POINT" && p.BlockHeaderOrdinal.HasValue).ToArray();
        blocks.ItemsSource=links.GroupBy(p=>p.BlockHeaderOrdinal!.Value).Select(g=>new BlockChoice(g.Key,$"Blok kaydı {g.Key} · {Safe(g.First().Block)}")).ToArray();
        blocks.SelectionChanged+=(_,_)=>
        {
            points.ItemsSource=null;pieces.ItemsSource=null;vertices.ItemsSource=null;indexText.Clear();SetStatus("");
            if(blocks.SelectedItem is not BlockChoice block)return;
            points.ItemsSource=pointRecords.Where(p=>p.BlockHeaderOrdinal==block.Header).Select(p=>new PointChoice(p.SourceOrdinal,
                $"POINT {p.SourceOrdinal} · X {Safe(p.Groups.FirstOrDefault(g=>g.Code==10)?.RawValue)} · Y {Safe(p.Groups.FirstOrDefault(g=>g.Code==20)?.RawValue)}")).ToArray();
            var selectedIds=links.Where(p=>p.BlockHeaderOrdinal==block.Header).Select(p=>p.PieceId).ToHashSet();
            pieces.ItemsSource=import.Project.Pieces.Where(p=>selectedIds.Contains(p.Id) && p.Closed).Select(p=>new PieceChoice(p.Id,$"{Safe(p.Name)} · {p.Id:D}")).ToArray();
        };
        pieces.SelectionChanged+=(_,_)=>
        {
            vertices.ItemsSource=null;indexText.Clear();SetStatus("");
            if(pieces.SelectedItem is not PieceChoice selected) { preview.Clear();return; }
            var piece=import.Project.Pieces.Single(p=>p.Id==selected.Id);
            preview.ShowPiece(piece);
            vertices.ItemsSource=piece.Points.Select((p,i)=>new VertexChoice(i,$"{i} · X {p.X:R} · Y {p.Y:R} mm")).ToArray();
        };
        vertices.SelectionChanged+=(_,_)=>preview.SelectVertex((vertices.SelectedItem as VertexChoice)?.Index);
        var indexRow=new WrapPanel();
        var go=new Button { Content="Köşeye git",Margin=new Thickness(4) };go.Click+=(_,_)=> { if(int.TryParse(indexText.Text,out var index) && index>=0 && index<vertices.Items.Count) { vertices.SelectedIndex=index;SetStatus(""); } else { vertices.SelectedIndex=-1;SetStatus("Geçerli bir köşe indeksi girin."); } };indexRow.Children.Add(indexText);indexRow.Children.Add(go);body.Children.Add(indexRow);
        var confirm=new CheckBox { Content="Bunun yerleştirilmemiş blok tanımı olduğunu anladım",Margin=new Thickness(0,12,0,8) };body.Children.Add(confirm);
        apply.Click+=(_,_)=>
        {
            if(confirm.IsChecked!=true || blocks.SelectedItem is not BlockChoice b || points.SelectedItem is not PointChoice p || pieces.SelectedItem is not PieceChoice piece || vertices.SelectedItem is not VertexChoice v)
            { SetStatus("Blok, POINT, parça ve köşeyi seçin ve açık onay verin.");return; }
            BlockHeaderOrdinal=b.Header;Selection=new(p.Ordinal,piece.Id,v.Index);DialogResult=true;
        };
    }
    private static string Safe(string? value)=>string.IsNullOrEmpty(value) ? "(boş)" : string.Concat(value.Take(120).Select(c=>char.IsControl(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Format or System.Globalization.UnicodeCategory.Surrogate ? $"\\u{(int)c:X4}" : c.ToString()));
    private static string SafeNotice(string value)=>string.Concat(value.Select(c=>char.IsControl(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Format or System.Globalization.UnicodeCategory.Surrogate ? $"\\u{(int)c:X4}" : c.ToString()));
}
