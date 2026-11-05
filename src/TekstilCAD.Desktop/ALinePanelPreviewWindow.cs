using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;

namespace TekstilCAD.Desktop;

/// <summary>Isolated snapshot. No CAD document, persistence, printer, or export reference.</summary>
internal sealed class ALinePanelPreviewWindow : Window
{
    internal ALinePanelPreviewWindow(Window owner,string promptSnapshot,string measurementSnapshot)
    {
        Owner=owner;Title="A kesim · deneysel bel-altı kesiti";Width=700;Height=800;MinWidth=500;MinHeight=450;
        StudioTheme.Apply(this);
        var body=new StackPanel { Margin=new Thickness(16) };
        Content=new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        body.Children.Add(new TextBlock { Text="Tam elbise kalıbı değildir. Ön/arka bel-altı yarım panellerin ölçü kesitlerini gösterir. Tüm değerler mm; hiçbir ölçü veya bolluk varsayılmaz. Ön/arka yay ölçülerini yan dikiş konumları arasında ayrı ölçün. Elbise boyu yerine BELDEN etek boyunu girin. Pencere kapanınca girdiler saklanmaz.",TextWrapping=TextWrapping.Wrap });
        string[] labels=["Ön bel yayı","Arka bel yayı","Ön kalça yayı","Arka kalça yayı","Ön bel yayı bolluğu","Arka bel yayı bolluğu","Ön kalça yayı bolluğu","Arka kalça yayı bolluğu","Belden kalçaya düşüklük","Belden etek boyu","Bitmiş etek çevresi (bolluk dahil)"];
        var fields=new List<TextBox>();
        foreach(var label in labels)
        {
            var box=new TextBox { MaxLength=24,Margin=new Thickness(0,0,0,6) };fields.Add(box);
            body.Children.Add(new Label { Content=label+" (mm)",Target=box });body.Children.Add(box);
        }
        var agreement=new CheckBox { Content="Etek çevresini ön ve arkaya eşit bölen, düz bel ve kalça kesitli deneysel gösterimi kabul ediyorum.",Margin=new Thickness(0,8,0,8) };
        body.Children.Add(agreement);
        var button=new Button { Content="Kesitleri hesapla ve önizle",Padding=new Thickness(10) };body.Children.Add(button);
        var status=new TextBlock { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8) };body.Children.Add(status);
        var canvas=new Canvas { Width=600,Height=340,Background=Brushes.White };
        body.Children.Add(new Viewbox { Child=canvas,MaxHeight=340,Stretch=Stretch.Uniform });
        var blockers=new TextBlock { TextWrapping=TextWrapping.Wrap };body.Children.Add(blockers);
        void Clear() { canvas.Children.Clear();blockers.Text="";status.Text="Girdiler değişti; yeniden hesaplayın. Çıktı kapalı."; }
        foreach(var field in fields)field.TextChanged+=(_,_)=>Clear();
        agreement.Checked+=(_,_)=>Clear();agreement.Unchecked+=(_,_)=>Clear();
        button.Click+=(_,_)=>
        {
            Clear();
            try
            {
                if(agreement.IsChecked!=true)throw new ArgumentException("Gösterim varsayımını okuyup seçin.");
                var values=new double[fields.Count];
                for(int i=0;i<fields.Count;i++)
                    if(!double.TryParse(fields[i].Text.Trim().Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out values[i]))
                        throw new ArgumentException(labels[i]+": açık mm değeri gerekli (bolluk için sıfır yazılabilir).");
                var input=new ALinePanelInput(values[0],values[1],values[2],values[3],values[4],values[5],values[6],values[7],values[8],values[9],values[10]);
                var review=DressMeasurementParser.Review(promptSnapshot,measurementSnapshot);
                foreach(var (label,total) in new[]{("bel çevresi",values[0]+values[1]),("kalça çevresi",values[2]+values[3])})
                {
                    if(review.Questions.Any(q=>q.StartsWith(label+":",StringComparison.Ordinal) && !q.Contains("açık ölçü ve birim")))
                        throw new ArgumentException(label+": istek notlarındaki belirsizliği önce düzeltin.");
                    var existing=review.Measurements.FirstOrDefault(m=>m.Label==label);
                    if(existing is not null && Math.Abs((double)existing.Millimeters-total)>0.000001)
                        throw new ArgumentException(label+": ön ve arka yay toplamı kayıtlı istek ölçüsünden farklı; önce kaynağı düzeltin.");
                }
                var preview=ALinePanelPreview.Create(input);
                var scale=Math.Min(240/(values[10]/4),280/values[9]);
                void Draw(IReadOnlyList<Point2> points,double x,string label,Brush stroke)
                {
                    var polygon=new Polygon { Stroke=stroke,StrokeThickness=2,Fill=Brushes.Transparent,Points=new PointCollection(points.Select(p=>new Point(x+p.X*scale,35+p.Y*scale))) };
                    canvas.Children.Add(polygon);var text=new TextBlock { Text=label,Foreground=Brushes.Black };Canvas.SetLeft(text,x);Canvas.SetTop(text,5);canvas.Children.Add(text);
                }
                Draw(preview.Front,20,"Ön yarım · merkez sol",Brushes.DarkBlue);Draw(preview.Back,320,"Arka yarım · merkez sol",Brushes.DarkRed);
                status.Text=$"Ön yan çizgi {preview.FrontSideLengthMm:F3} mm; arka {preview.BackSideLengthMm:F3} mm; fark {preview.SideMismatchMm:F3} mm. Dikiş eşleşmesi onaylanmadı. Görünüm ölçekli baskı değildir.";
                blockers.Text=string.Join(Environment.NewLine,preview.Unresolved);
            }
            catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or OverflowException or System.Text.RegularExpressions.RegexMatchTimeoutException)
            { status.Text=ex.Message; }
        };
    }
}
