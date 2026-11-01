using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>Explicit review of the exact prepared DXF; acceptance does not certify the recipient or cutting workflow.</summary>
internal sealed class DxfGrainlineExportReviewWindow : Window
{
    private sealed record Row(string Piece,string Id,string Contour,string Marker,string Start,string End,string Review);
    internal DxfGrainlineExportReviewWindow(Window owner,DxfGrainlineReviewPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);Owner=owner;Title="DXF iplik işaretli inceleme çıktısı";
        Width=980;Height=720;MinWidth=680;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var buttons=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };
        var cancel=new Button { Content="İptal et",IsCancel=true,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,0) };
        var accept=new Button { Content="İnceleme dosyasını yaz",Padding=new Thickness(12,8,12,8) };
        cancel.Click+=(_,_)=>DialogResult=false;accept.Click+=(_,_)=>DialogResult=true;buttons.Children.Add(cancel);buttons.Children.Add(accept);
        DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        var heading=new StackPanel();DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text="İplik işaretli DXF · yalnız inceleme",FontSize=20,FontWeight=FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text="Bu dosya kesim veya üretim onayı değildir. İşaretler ayrı DXF çizgileridir; alıcı yazılım veya kesim aracı katmanları görmezden gelip bu çizgileri kesim yolu sayabilir. Katman adı iplik yönü semantiğini garanti etmez. Hedef CAD'de ölçeği, konturları ve tüm işaretleri ayrıca kontrol edin. İçe aktarırken eşlemeler yeniden elle seçilir.",Foreground=StudioTheme.Warning,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,7,0,8) });
        heading.Children.Add(new TextBlock { Text=$"Hazırlanan dosya: {plan.ByteLength} bayt · SHA-256 {plan.Sha256}",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6) });
        var details=new TextBox { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=105,Margin=new Thickness(0,8,0,0) };
        AutomationProperties.SetName(details,"Seçili parçanın tam katman ve işaret ayrıntıları");DockPanel.SetDock(details,Dock.Bottom);root.Children.Add(details);
        var omissionText=new TextBox { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=85,Margin=new Thickness(0,8,0,0),
            Text=plan.Omissions.Length==0 ? "Bildirilen ek metadata eksikliği yok." : "DXF'ye aktarılmayan bilgiler:\n"+string.Join("\n",plan.Omissions.Select(x=>$"{Omission(x.Kind)} · {(x.PieceId?.ToString("D") ?? "proje")}")) };
        AutomationProperties.SetName(omissionText,"DXF'ye aktarılmayan bilgiler");DockPanel.SetDock(omissionText,Dock.Bottom);root.Children.Add(omissionText);
        var grid=new DataGrid { IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,SelectionMode=DataGridSelectionMode.Single,EnableRowVirtualization=true,EnableColumnVirtualization=true };
        VirtualizingPanel.SetIsVirtualizing(grid,true);VirtualizingPanel.SetVirtualizationMode(grid,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(grid,true);
        AutomationProperties.SetName(grid,"Tüm parça, kontur ve işaret katmanları");
        AutomationProperties.SetHelpText(grid,"Bir satır seçerek tam kimlik, katman adları ve işaret uçlarını aşağıdaki alanda okuyun.");
        void Column(string title,string property,double width)=>grid.Columns.Add(new DataGridTextColumn { Header=title,Binding=new Binding(property),Width=width });
        Column("Parça",nameof(Row.Piece),150);Column("Kontur katmanı",nameof(Row.Contour),150);Column("İşaret katmanı",nameof(Row.Marker),150);Column("Kontrol",nameof(Row.Review),120);Column("Kimlik",nameof(Row.Id),250);
        var rows=plan.Items.Select(item=>new Row(Safe(item.PieceName),item.PieceId.ToString("D"),Safe(item.ContourLayer),
            item.MarkerLayer is null ? "İşaret yok" : Safe(item.MarkerLayer),
            item.Start is { } start ? $"X {MeasurementInput.Format(start.X)}, Y {MeasurementInput.Format(start.Y)} mm" : "—",
            item.End is { } end ? $"X {MeasurementInput.Format(end.X)}, Y {MeasurementInput.Format(end.Y)} mm" : "—",
            item.NeedsReview is null ? "İşaret yok" : item.NeedsReview.Value ? "Kontrol bekliyor" : "Kontrol edildi; alıcıda doğrulayın")).ToArray();
        grid.ItemsSource=rows;grid.SelectionChanged+=(_,_)=>
        {
            details.Text=grid.SelectedItem is Row row ? $"Parça: {row.Piece}\nKimlik: {row.Id}\nKontur katmanı: {row.Contour}\nİşaret katmanı: {row.Marker}\nBaşlangıç: {row.Start}\nBitiş: {row.End}\n{row.Review}" : "Ayrıntıları görmek için bir parça seçin.";
        };
        if(rows.Length>0)grid.SelectedIndex=0;root.Children.Add(grid);
    }
    private static string Omission(DxfExportOmissionKind kind)=>kind switch
    {
        DxfExportOmissionKind.PieceRole=>"Parça rolü",DxfExportOmissionKind.Size=>"Beden",
        DxfExportOmissionKind.Material=>"Malzeme",DxfExportOmissionKind.Quantity=>"Adet",
        DxfExportOmissionKind.GradingRules=>"Serileme kuralları",DxfExportOmissionKind.NestingSnapshot=>"Pastal geçmişi",
        DxfExportOmissionKind.ImportProvenance=>"Aktarım kökeni",DxfExportOmissionKind.PhotoTraceHistory=>"Fotoğraf taslağı geçmişi (kaynak .tcad dosyasını saklayın)",DxfExportOmissionKind.GrainlineReviewState=>"İplik yönü kontrol durumu",
        DxfExportOmissionKind.PieceIdentity=>"TekstilCAD parça kimliği",DxfExportOmissionKind.GrainlineUnsupported=>"İplik yönü semantiği",
        _=>kind.ToString()
    };
    private static string Safe(string? value)
    {
        if(string.IsNullOrEmpty(value))return "(boş)";
        var result=new StringBuilder();foreach(char c in value)
        { var category=char.GetUnicodeCategory(c);if(char.IsControl(c) || category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator || char.IsSurrogate(c))result.Append($"\\u{(int)c:X4}");else result.Append(c); }
        return result.ToString();
    }
}
