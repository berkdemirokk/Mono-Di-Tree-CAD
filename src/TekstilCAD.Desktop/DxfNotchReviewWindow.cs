using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

internal sealed class DxfNotchReviewWindow : Window
{
    private static string Safe(string value)
    {
        var output=new StringBuilder();
        foreach(char c in value)
        {
            var category=char.GetUnicodeCategory(c);
            if(char.IsControl(c) || char.IsSurrogate(c) || category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)output.Append($"\\u{(int)c:X4}");
            else output.Append(c);
        }
        return output.Length==0 ? "(boş)" : output.ToString();
    }
    private static string Omission(DxfExportOmissionKind kind)=>kind switch
    {
        DxfExportOmissionKind.PieceRole=>"Rol",DxfExportOmissionKind.Size=>"Beden",DxfExportOmissionKind.Material=>"Malzeme",
        DxfExportOmissionKind.Quantity=>"Adet",DxfExportOmissionKind.GradingRules=>"Serileme kuralları",
        DxfExportOmissionKind.NestingSnapshot=>"Pastal geçmişi",DxfExportOmissionKind.ImportProvenance=>"Kaynak geçmişi",DxfExportOmissionKind.PhotoTraceHistory=>"Fotoğraf taslağı geçmişi (kaynak .tcad dosyasını saklayın)",
        DxfExportOmissionKind.PieceIdentity=>"TekstilCAD parça kimliği",DxfExportOmissionKind.GrainlineReviewState=>"İplik yönü kontrol durumu ve ilişkisi",_=>"Diğer proje bilgisi"
    };
    private sealed record ContourRow(string Piece,string Id,string Layer,int MarkCount,string Grain);
    private sealed record Row(string Piece,string Id,string Contour,string Layer,string Index,string Source,string Output);
    private sealed record GrainRow(string Piece,string Id,string Contour,string Layer,string Source,string Output,string Review);
    internal DxfNotchReviewWindow(Window owner,DxfNotchReviewPlan plan,bool selectedScope=false)
    {
        Owner=owner;Title="Köşe işaretli DXF · yalnız inceleme";Width=980;Height=720;MinWidth=680;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var cancel=new Button { Content="İptal",IsCancel=true,IsDefault=true,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,0) };
        var accept=new Button { Content="İnceleme dosyasını yaz",Padding=new Thickness(12,8,12,8) };cancel.Click+=(_,_)=>DialogResult=false;accept.Click+=(_,_)=>DialogResult=true;actions.Children.Add(cancel);actions.Children.Add(accept);
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock { Text="Köşe işaretli DXF · yalnız inceleme",FontSize=20,FontWeight=FontWeights.SemiBold });
        if(selectedScope)top.Children.Add(new TextBlock { Text=$"Yalnız seçilen {plan.Contours.Length} parça aktarılacak; diğer parçalar kaynakta kalır. İşaretli ve işaretsiz tüm seçili konturlar, Konturlar sekmesinde kimlikleriyle gösterilir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,0) });
        top.Children.Add(new TextBlock { Text="Üretim veya kesim dosyası değildir. Köşe işaretleri ayrı katmanlarda DXF POINT, iplik yönleri ayrı katmanlarda LINE nesneleridir; alıcı CAD bunları kesim yolu sayabilir. Köşe işareti fiziksel çentik derinliği içermez. Ölçeği, görünürlüğü ve tüm işaretleri hedefte kontrol edin. Parça ilişkisi ve iplik yönü kontrol durumu DXF'te korunmaz; .tcad kaynak dosyasını saklayın.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,8,0,8) });
        top.Children.Add(new TextBlock { Text=$"Y yönü: {(plan.FlipY ? "ekran yönüne göre ters çevrildi" : "kaynak koordinatları korundu")} · Köşe işareti: {plan.Items.Length} · İplik yönü: {plan.Grainlines.Length} · SHA-256 {plan.Sha256}",TextWrapping=TextWrapping.Wrap });
        var omissions=new TextBox { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=80,Margin=new Thickness(0,8,0,0),Text="DXF'ye aktarılmayan bilgiler (kimlikler eşleme için burada gösterilir):\n"+string.Join("\n",plan.Omissions.Select(o=>$"{Omission(o.Kind)} · {o.PieceId?.ToString("D") ?? "proje"}")) };
        AutomationProperties.SetName(omissions,"DXF'ye aktarılmayan bilgiler");DockPanel.SetDock(omissions,Dock.Bottom);root.Children.Add(omissions);
        var grid=new DataGrid { IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,EnableRowVirtualization=true,EnableColumnVirtualization=true };
        VirtualizingPanel.SetIsVirtualizing(grid,true);ScrollViewer.SetCanContentScroll(grid,true);AutomationProperties.SetName(grid,"Tüm köşe işaretleri ve katmanları");
        void Column(string title,string field,double width)=>grid.Columns.Add(new DataGridTextColumn { Header=title,Binding=new Binding(field),Width=width });
        Column("Parça",nameof(Row.Piece),150);Column("Kimlik",nameof(Row.Id),230);Column("Kontur katmanı",nameof(Row.Contour),150);
        Column("İşaret katmanı",nameof(Row.Layer),150);Column("Nokta indeksi",nameof(Row.Index),100);Column("Kaynak X, Y · mm",nameof(Row.Source),180);Column("DXF X, Y · mm",nameof(Row.Output),180);
        grid.ItemsSource=plan.Items.Select(i=>new Row(Safe(i.PieceName),i.PieceId.ToString("D"),i.ContourLayer,i.MarkerLayer,i.VertexIndex.ToString(),
            $"{MeasurementInput.Format(i.Position.X)}, {MeasurementInput.Format(i.Position.Y)}",$"{MeasurementInput.Format(i.Position.X)}, {MeasurementInput.Format(plan.FlipY ? -i.Position.Y : i.Position.Y)}")).ToArray();
        var grainGrid=new DataGrid { IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,EnableRowVirtualization=true,EnableColumnVirtualization=true };
        VirtualizingPanel.SetIsVirtualizing(grainGrid,true);ScrollViewer.SetCanContentScroll(grainGrid,true);AutomationProperties.SetName(grainGrid,"İplik yönleri ve katmanları");
        void GrainColumn(string title,string field,double width)=>grainGrid.Columns.Add(new DataGridTextColumn { Header=title,Binding=new Binding(field),Width=width });
        GrainColumn("Parça",nameof(GrainRow.Piece),150);GrainColumn("Kimlik",nameof(GrainRow.Id),230);GrainColumn("Kontur katmanı",nameof(GrainRow.Contour),150);
        GrainColumn("İplik katmanı",nameof(GrainRow.Layer),150);GrainColumn("Kaynak uçlar · mm",nameof(GrainRow.Source),220);GrainColumn("DXF uçlar · mm",nameof(GrainRow.Output),220);GrainColumn("Kaynak kontrolü",nameof(GrainRow.Review),150);
        string Coordinates(Point2 a,Point2 b,bool flip)=>$"{MeasurementInput.Format(a.X)}, {MeasurementInput.Format(flip ? -a.Y : a.Y)} → {MeasurementInput.Format(b.X)}, {MeasurementInput.Format(flip ? -b.Y : b.Y)}";
        grainGrid.ItemsSource=plan.Grainlines.Select(i=>new GrainRow(Safe(i.PieceName),i.PieceId.ToString("D"),i.ContourLayer,i.MarkerLayer,Coordinates(i.Start,i.End,false),Coordinates(i.Start,i.End,plan.FlipY),i.NeedsReview ? "Kontrol bekliyor" : "Kontrol beklemiyor")).ToArray();
        var contourGrid=new DataGrid { IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,EnableRowVirtualization=true,EnableColumnVirtualization=true };
        VirtualizingPanel.SetIsVirtualizing(contourGrid,true);ScrollViewer.SetCanContentScroll(contourGrid,true);AutomationProperties.SetName(contourGrid,"Aktarılacak tüm konturlar ve katmanları");
        void ContourColumn(string title,string field,double width)=>contourGrid.Columns.Add(new DataGridTextColumn { Header=title,Binding=new Binding(field),Width=width });
        ContourColumn("Parça",nameof(ContourRow.Piece),180);ContourColumn("Kimlik",nameof(ContourRow.Id),245);ContourColumn("Kontur katmanı",nameof(ContourRow.Layer),190);
        ContourColumn("Görsel işaret sayısı",nameof(ContourRow.MarkCount),140);ContourColumn("İplik yönü",nameof(ContourRow.Grain),110);
        contourGrid.ItemsSource=plan.Contours.Select(c=>new ContourRow(Safe(c.PieceName),c.PieceId.ToString("D"),c.ContourLayer,c.MarkerCount,c.HasGrainline ? "Var" : "Yok")).ToArray();
        var tabs=new TabControl();tabs.Items.Add(new TabItem { Header="Köşe işaretleri",Content=grid });tabs.Items.Add(new TabItem { Header="İplik yönleri",Content=grainGrid });
        tabs.Items.Add(new TabItem { Header="Konturlar",Content=contourGrid });if(selectedScope)tabs.SelectedIndex=2;root.Children.Add(tabs);
    }
}
