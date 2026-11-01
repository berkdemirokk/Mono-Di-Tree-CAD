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
        DxfExportOmissionKind.NestingSnapshot=>"Pastal geçmişi",DxfExportOmissionKind.ImportProvenance=>"Kaynak geçmişi",
        DxfExportOmissionKind.PieceIdentity=>"TekstilCAD parça kimliği",_=>"Diğer proje bilgisi"
    };
    private sealed record Row(string Piece,string Id,string Contour,string Layer,string Index,string Source,string Output);
    internal DxfNotchReviewWindow(Window owner,DxfNotchReviewPlan plan)
    {
        Owner=owner;Title="Köşe işaretli DXF · yalnız inceleme";Width=980;Height=720;MinWidth=680;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var cancel=new Button { Content="İptal",IsCancel=true,IsDefault=true,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,0) };
        var accept=new Button { Content="İnceleme dosyasını yaz",Padding=new Thickness(12,8,12,8) };cancel.Click+=(_,_)=>DialogResult=false;accept.Click+=(_,_)=>DialogResult=true;actions.Children.Add(cancel);actions.Children.Add(accept);
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock { Text="Köşe işaretli DXF · yalnız inceleme",FontSize=20,FontWeight=FontWeights.SemiBold });
        top.Children.Add(new TextBlock { Text="Üretim veya kesim dosyası değildir. İşaretler ayrı katmanlarda DXF POINT nesneleridir; kesim yönü, biçimi ve derinliği içermez. Dosya ekran boyutuna göre işaret gösterimi ister; alıcı CAD bunu yok sayabilir veya noktaları kesim sayabilir. Görünürlüğü, ölçeği ve her işareti hedefte kontrol edin. TekstilCAD'e geri aktarımda işaret anlamı korunmaz; .tcad kaynak dosyasını saklayın.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,8,0,8) });
        top.Children.Add(new TextBlock { Text=$"Y yönü: {(plan.FlipY ? "ekran yönüne göre ters çevrildi" : "kaynak koordinatları korundu")} · İşaret: {plan.Items.Length} · SHA-256 {plan.Sha256}",TextWrapping=TextWrapping.Wrap });
        var omissions=new TextBox { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=80,Margin=new Thickness(0,8,0,0),Text="DXF'ye aktarılmayan bilgiler (kimlikler eşleme için burada gösterilir):\n"+string.Join("\n",plan.Omissions.Select(o=>$"{Omission(o.Kind)} · {o.PieceId?.ToString("D") ?? "proje"}")) };
        AutomationProperties.SetName(omissions,"DXF'ye aktarılmayan bilgiler");DockPanel.SetDock(omissions,Dock.Bottom);root.Children.Add(omissions);
        var grid=new DataGrid { IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,EnableRowVirtualization=true,EnableColumnVirtualization=true };
        VirtualizingPanel.SetIsVirtualizing(grid,true);ScrollViewer.SetCanContentScroll(grid,true);AutomationProperties.SetName(grid,"Tüm köşe işaretleri ve katmanları");
        void Column(string title,string field,double width)=>grid.Columns.Add(new DataGridTextColumn { Header=title,Binding=new Binding(field),Width=width });
        Column("Parça",nameof(Row.Piece),150);Column("Kimlik",nameof(Row.Id),230);Column("Kontur katmanı",nameof(Row.Contour),150);
        Column("İşaret katmanı",nameof(Row.Layer),150);Column("Nokta indeksi",nameof(Row.Index),100);Column("Kaynak X, Y · mm",nameof(Row.Source),180);Column("DXF X, Y · mm",nameof(Row.Output),180);
        grid.ItemsSource=plan.Items.Select(i=>new Row(Safe(i.PieceName),i.PieceId.ToString("D"),i.ContourLayer,i.MarkerLayer,i.VertexIndex.ToString(),
            $"{MeasurementInput.Format(i.Position.X)}, {MeasurementInput.Format(i.Position.Y)}",$"{MeasurementInput.Format(i.Position.X)}, {MeasurementInput.Format(plan.FlipY ? -i.Position.Y : i.Position.Y)}")).ToArray();
        root.Children.Add(grid);
    }
}
