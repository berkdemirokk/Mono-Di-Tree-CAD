using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using Geometry = TekstilCAD.Core.Geometry;

namespace TekstilCAD.Desktop;

/// <summary>Operator-selected visual vertex marks only; no machine or physical notch semantics.</summary>
internal sealed class DxfNotchPointReviewWindow : Window
{
    private sealed record LayerChoice(string Name) { public override string ToString()=>Safe(Name); }
    private sealed record PointChoice(int Ordinal,string Layer,string X,string Y)
    { public override string ToString()=>$"Kayıt {Ordinal} · X {Safe(X)} · Y {Safe(Y)}"; }
    private sealed record PieceChoice(PatternPiece Piece) { public override string ToString()=>$"{Safe(Piece.Name)} · {Piece.Id:D}"; }
    private sealed record VertexChoice(int Index,Point2 Position)
    { public override string ToString()=>$"Nokta {Index} · X {MeasurementInput.Format(Position.X)} · Y {MeasurementInput.Format(Position.Y)} mm"; }
    private sealed record Mapping(DxfNotchPointSelection Selection,string Text) { public override string ToString()=>Text; }
    private readonly DxfImportResult snapshot;
    private readonly string fingerprint;
    private readonly ComboBox layer=new() { Margin=new Thickness(0,4,0,8),MaxDropDownHeight=200 };
    private readonly ComboBox point=new() { Margin=new Thickness(0,4,0,8),MaxDropDownHeight=200 };
    private readonly ComboBox contour=new() { Margin=new Thickness(0,4,0,8),MaxDropDownHeight=200 };
    private readonly ComboBox vertex=new() { Margin=new Thickness(0,4,0,8),MaxDropDownHeight=200 };
    private readonly ObservableCollection<Mapping> mappings=[];
    private readonly ListBox selected=new() { Height=100 };
    private readonly NotchPreview preview=new() { Height=160 };
    private readonly TextBlock status=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,6) };
    private readonly Button accept=new() { Content="Eşlemeleri onayla",IsEnabled=false,Padding=new Thickness(12,8,12,8) };
    private readonly Button add=new() { Content="Seçimi doğrula ve ekle",Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,4,8,4) };
    private readonly Button remove=new() { Content="Seçili eşlemeyi kaldır",Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,4,0,4) };
    internal IReadOnlyList<DxfNotchPointSelection> Selections { get; private set; }=Array.Empty<DxfNotchPointSelection>();
    internal string? ApprovedLayer { get; private set; }

    internal DxfNotchPointReviewWindow(Window owner,DxfImportResult import,string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(import);ArgumentNullException.ThrowIfNull(fingerprint);
        snapshot=import with { Project=import.Project.Clone() };this.fingerprint=fingerprint;
        Owner=owner;Title="DXF nokta işaretlerini incele";Width=820;Height=780;MinWidth=530;MinHeight=450;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };
        var cancel=new Button { Content="Aktarımı iptal et",IsCancel=true,IsDefault=true,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,0) };
        var skip=new Button { Content="Eşleme yapmadan devam",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,0) };
        cancel.Click+=(_,_)=>DialogResult=false;
        skip.Click+=(_,_)=> { Selections=Array.Empty<DxfNotchPointSelection>();ApprovedLayer=null;DialogResult=true; };
        accept.Click+=(_,_)=> { if(layer.SelectedItem is not LayerChoice chosen || mappings.Count==0)return;ApprovedLayer=chosen.Name;Selections=Array.AsReadOnly(mappings.Select(m=>m.Selection).ToArray());DialogResult=true; };
        actions.Children.Add(cancel);actions.Children.Add(skip);actions.Children.Add(accept);DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var body=new StackPanel();root.Children.Add(new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto });
        body.Children.Add(new TextBlock { Text="DXF noktalarını köşe işareti olarak incele",FontSize=20,FontWeight=FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text="İsteğe bağlıdır: katmanın anlamını kaynakta doğrulayın, sonra her POINT kaydını ve kontur köşesini kendiniz seçin. Yalnız mm ve tam koordinat eşleşmesi kabul edilir; yinelenen veya birden fazla köşeyle eşleşen noktalar reddedilir. İşaret görsel bir referanstır; fiziksel çentik, kesim komutu veya üretim onayı değildir. Kaynak DXF değiştirilmez.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,8,0,8) });
        var points=(snapshot.SupplementalGeometry?.Records ?? []).Where(r=>r.EntityType=="POINT" && r.Section=="ENTITIES" && r.Block is null)
            .Select(r=>new { Record=r, Layers=r.Groups.Where(g=>g.Code==8).ToArray(),X=r.Groups.Where(g=>g.Code==10).ToArray(),Y=r.Groups.Where(g=>g.Code==20).ToArray() })
            .Where(x=>x.Layers.Length==1 && x.X.Length==1 && x.Y.Length==1)
            .Select(x=>new PointChoice(x.Record.SourceOrdinal,x.Layers[0].RawValue.Trim(),x.X[0].RawValue,x.Y[0].RawValue)).ToArray();
        layer.ItemsSource=points.Select(p=>p.Layer).Distinct(StringComparer.OrdinalIgnoreCase).Select(x=>new LayerChoice(x)).ToArray();
        contour.ItemsSource=snapshot.Project.Pieces.Select(p=>new PieceChoice(p)).ToArray();
        AutomationProperties.SetName(layer,"Kullanıcının onayladığı DXF nokta katmanı");AutomationProperties.SetName(point,"DXF POINT kaynak kaydı");
        AutomationProperties.SetName(contour,"İşaretin bağlanacağı kontur");AutomationProperties.SetName(vertex,"Sıfırdan başlayan kontur köşe indeksi");
        foreach(var box in new[] { layer,point,contour,vertex })
        {
            box.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
            VirtualizingPanel.SetIsVirtualizing(box,true);VirtualizingPanel.SetVirtualizationMode(box,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(box,true);
        }
        AutomationProperties.SetName(selected,"Onay için seçilen nokta eşlemeleri");AutomationProperties.SetName(status,"Nokta eşleme durumu");AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);
        void Label(string text,UIElement control) { body.Children.Add(new TextBlock { Text=text,FontWeight=FontWeights.SemiBold });body.Children.Add(control); }
        Label("1. Kaynak POINT katmanı",layer);Label("2. Kaynak POINT kaydı ve ham koordinatı",point);Label("3. Hedef kontur",contour);Label("4. Hedef köşe indeksi",vertex);
        layer.SelectionChanged+=(_,_)=> { mappings.Clear();preview.Clear();accept.IsEnabled=false;point.ItemsSource=layer.SelectedItem is LayerChoice l ? points.Where(p=>string.Equals(p.Layer,l.Name,StringComparison.OrdinalIgnoreCase)).ToArray() : [];status.Text="Katman değişti; eşlemeleri yeniden seçin."; };
        point.SelectionChanged+=(_,_)=>preview.Clear();
        vertex.SelectionChanged+=(_,_)=>preview.Clear();
        contour.SelectionChanged+=(_,_)=>
        {
            preview.Clear();
            if(contour.SelectedItem is PieceChoice p)
            {
                vertex.ItemsSource=p.Piece.Points.Select((position,index)=>new VertexChoice(index,position)).ToArray();
            }
            else vertex.ItemsSource=null;
        };
        body.Children.Add(new TextBlock { Text="Doğrulanmış seçimden sonra kontur ve işaret önizlemesi gösterilir. Görünüm otomatik sığdırılır, fiziksel ölçek değildir.",TextWrapping=TextWrapping.Wrap });body.Children.Add(preview);
        var buttons=new WrapPanel();
        add.Click+=(_,_)=>
        {
            try
            {
                if(layer.SelectedItem is not LayerChoice l || point.SelectedItem is not PointChoice p || contour.SelectedItem is not PieceChoice c ||
                   vertex.SelectedItem is not VertexChoice v)throw new ArgumentException("Katman, POINT, kontur ve geçerli köşeyi seçin.");
                if(mappings.Count>=100)throw new ArgumentException("Bir incelemede en fazla 100 eşleme yapılabilir.");
                var next=new DxfNotchPointSelection(p.Ordinal,c.Piece.Id,v.Index);
                var proposals=mappings.Select(m=>m.Selection).Append(next).ToArray();
                var checkedResult=DxfNotchPointMapping.Apply(snapshot,proposals,l.Name,this.fingerprint);
                var added=checkedResult.Applied[^1];var derived=checkedResult.Project.Pieces.Single(x=>x.Id==added.PieceId);
                preview.ShowPiece(derived);
                mappings.Add(new Mapping(next,$"POINT {p.Ordinal} → {Safe(c.Piece.Name)} · {c.Piece.Id:D} · köşe {added.VertexIndex} · X {MeasurementInput.Format(added.Position.X)} Y {MeasurementInput.Format(added.Position.Y)} mm"));
                status.Text=$"{mappings.Count} eşleme ayrık kopyada doğrulandı. Görsel işaret; üretim onayı değildir.";accept.IsEnabled=true;
            }
            catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or System.IO.InvalidDataException)
            { status.Text="Eşleme eklenmedi: "+ex.Message; }
        };
        remove.Click+=(_,_)=> { if(selected.SelectedItem is Mapping m)mappings.Remove(m);preview.Clear();accept.IsEnabled=mappings.Count>0;status.Text=$"{mappings.Count} eşleme seçili. Önizleme temizlendi; yeni bir seçim doğrulayın."; };
        buttons.Children.Add(add);buttons.Children.Add(remove);body.Children.Add(buttons);selected.ItemsSource=mappings;body.Children.Add(selected);body.Children.Add(status);
    }
    private static string Safe(string? value)
    {
        if(string.IsNullOrEmpty(value))return "(boş)";
        var chars=value.Take(128).Select(c=>char.IsControl(c)||char.IsSurrogate(c)||char.GetUnicodeCategory(c)==UnicodeCategory.Format ? '?' : c).ToArray();
        return new string(chars)+(value.Length>chars.Length ? "…" : "");
    }
    private sealed class NotchPreview : FrameworkElement
    {
        private PatternPiece? piece;
        internal NotchPreview()
        {
            Focusable=true;ClipToBounds=true;
            AutomationProperties.SetName(this,"Ayrık kontur ve köşe işaretleri önizlemesi");
            AutomationProperties.SetHelpText(this,"Kontur otomatik sığdırılır; mor halkalar işaretli köşeleri gösterir. Fiziksel ölçüm değildir.");
        }
        internal void ShowPiece(PatternPiece value) { piece=value.Clone();InvalidateVisual(); }
        internal void Clear() { piece=null;InvalidateVisual(); }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);dc.DrawRectangle(Brushes.WhiteSmoke,null,new Rect(RenderSize));
            if(piece?.Points is not { Count:>1 } points || ActualWidth<30 || ActualHeight<30)return;
            var bounds=Geometry.Bounds(points);var width=bounds.Width;var height=bounds.Height;
            var scale=Math.Min(width>0 ? (ActualWidth-24)/width : double.PositiveInfinity,height>0 ? (ActualHeight-24)/height : double.PositiveInfinity);
            if(!double.IsFinite(scale))return;
            Point Map(Point2 p)=>new(12+(p.X-bounds.MinX)*scale,ActualHeight-12-(p.Y-bounds.MinY)*scale);
            var geometry=new StreamGeometry();using(var context=geometry.Open())
            { context.BeginFigure(Map(points[0]),false,piece.Closed);foreach(var p in points.Skip(1))context.LineTo(Map(p),true,false); }
            geometry.Freeze();dc.DrawGeometry(null,new Pen(Brushes.DarkSlateGray,1.5),geometry);
            if(piece.Notches is { } marks)foreach(var mark in marks)dc.DrawEllipse(null,new Pen(Brushes.DarkMagenta,2),Map(mark.Position),5,5);
        }
    }
}
