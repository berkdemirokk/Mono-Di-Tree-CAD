using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TekstilCAD.Patterns;

namespace BodiceResearch;
internal sealed class ResearchWindow:Window
{
    readonly SyntheticBoundary boundary=new();
    readonly Dictionary<string,TextBox> fields=new(StringComparer.Ordinal);
    readonly ComboBox unit=new(){ItemsSource=new[]{"mm","cm"},SelectedIndex=-1};
    readonly CheckBox confirmed=new(){Content="Yalnız gösterilen uydurma test değerlerini girdim; gerçek kişi ölçüsü kullanmıyorum."};
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
    readonly Preview preview=new(){Height=350,Visibility=Visibility.Collapsed};
    internal ResearchWindow()
    {
        Title="Deneysel gövde — yalnız sentetik örnek";Width=900;Height=800;
        var root=new StackPanel{Margin=new Thickness(16)};Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        root.Children.Add(new TextBlock{Text="YALNIZ SENTETİK ARAŞTIRMA: Gerçek kişisel ölçü girmeyin. Motor ve bağımlılıklarının kimliği, ağ/disk davranışı ve lisansı doğrulanmadığı için gerçek veriye kapalıdır. Yalnız önceden üretilmiş tek uydurma örnek gösterilir. Tam elbise, CAD, üretim, baskı veya dışa aktarma yoktur. Ölçüm yönergeleri uzman tarafından doğrulanmadı; uzmanla teyit ve numune prova gerekir.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DarkRed});
        root.Children.Add(new TextBlock{Text="Uzunluk birimini açıkça seçin. Sentetik örnek değerleri aşağıda mm, omuz eğimi derece cinsindendir; cm için uzunlukları 10'a bölün.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,4)});root.Children.Add(unit);
        foreach(var d in BreannaMeasurementGate.Definitions)
        {
            var sample=boundary.Expected[d.Key];root.Children.Add(new TextBlock{Text=$"{d.Label} — sentetik örnek: {sample} {(d.IsAngle?"derece":"mm")}\n{d.Instructions}",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,2)});
            var box=new TextBox();fields.Add(d.Key,box);root.Children.Add(box);box.TextChanged+=(_,_)=>Clear();
        }
        unit.SelectionChanged+=(_,_)=>Clear();confirmed.Unchecked+=(_,_)=>ClearPreview();root.Children.Add(confirmed);
        var button=new Button{Content="Sentetik ön ve arka gövdeyi göster",Margin=new Thickness(0,10,0,0),Padding=new Thickness(8)};button.Click+=(_,_)=>ShowCandidate();root.Children.Add(button);root.Children.Add(status);root.Children.Add(preview);
    }
    void ClearPreview(){preview.Data=null;preview.Visibility=Visibility.Collapsed;preview.InvalidateVisual();}
    void Clear(){ClearPreview();confirmed.IsChecked=false;status.Text="Girdi değişti; sentetik değerleri ve birimi yeniden kontrol edin.";}
    internal void ShowCandidate()
    {
        ClearPreview();try {var values=SyntheticBoundary.Parse(fields.ToDictionary(x=>x.Key,x=>x.Value.Text,StringComparer.Ordinal),unit.SelectedItem as string);var output=boundary.Evaluate(values,confirmed.IsChecked==true);preview.Data=output;preview.Visibility=Visibility.Visible;preview.InvalidateVisual();status.Text="Yalnız sabit sentetik örnek; dikiş eşleşmesi ve üretim doğrulanmadı.";}
        catch(InvalidOperationException ex){status.Text=ex.Message;}
    }
    internal void EnterSample(){unit.SelectedItem="mm";foreach(var (key,n) in boundary.Expected)fields[key].Text=n.ToString(System.Globalization.CultureInfo.InvariantCulture);confirmed.IsChecked=true;}
    internal void Alter(){fields["waist"].Text="741";}
    internal bool Shown=>preview.Visibility==Visibility.Visible;
    internal bool Confirmed=>confirmed.IsChecked==true;
    internal string Status=>status.Text;
    internal bool RendersInk()
    {
        preview.Measure(new Size(800,350));preview.Arrange(new Rect(0,0,800,350));preview.UpdateLayout();
        var bmp=new RenderTargetBitmap(800,350,96,96,PixelFormats.Pbgra32);bmp.Render(preview);
        var bytes=new byte[800*350*4];bmp.CopyPixels(bytes,800*4,0);
        for(int i=0;i<bytes.Length;i+=4)if(bytes[i]>120&&bytes[i+1]<180&&bytes[i+2]<110&&bytes[i]>bytes[i+1])return true;
        return false;
    }
}
internal static class Program
{
    [STAThread]static int Main(string[] args)
    {
        if(args.SequenceEqual(new[]{"--verify"}))return Verify();
        return new Application().Run(new ResearchWindow());
    }
    static int Verify()
    {
        try
        {
            var boundary=new SyntheticBoundary();var expected=boundary.Expected;
            if(expected.Count!=15)throw new Exception("missing fields");
            var raw=expected.ToDictionary(x=>x.Key,x=>x.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if(SyntheticBoundary.Parse(raw,"mm").Count!=15)throw new Exception("parse");
            foreach(var key in raw.Keys){var missing=new Dictionary<string,string>(raw);missing.Remove(key);try{SyntheticBoundary.Parse(missing,"mm");throw new Exception("missing accepted");}catch(InvalidOperationException){}}
            try{SyntheticBoundary.Parse(raw,null);throw new Exception("unit omitted");}catch(InvalidOperationException){}
            var cm=expected.ToDictionary(x=>x.Key,x=>(x.Key=="shoulderSlope"?x.Value:x.Value/10).ToString(System.Globalization.CultureInfo.InvariantCulture));
            if(SyntheticBoundary.Parse(cm,"cm").Any(x=>x.Value!=expected[x.Key]))throw new Exception("unit conversion");
            try{boundary.Evaluate(expected,false);throw new Exception("unconfirmed accepted");}catch(InvalidOperationException){}
            var invalid=new Dictionary<string,decimal>(expected){["waist"]=741};try{boundary.Evaluate(invalid,true);throw new Exception("nonfixture accepted");}catch(InvalidOperationException){}
            var output=boundary.Evaluate(expected,true);var parts=output.Parts;
            if(parts.Count!=2 || parts.Any(p=>!p.Segments.Any(s=>s.Type=="cubic")))throw new Exception("curves missing");
            foreach(var part in parts){var geo=Preview.Geometry(part);var fig=geo.Figures.Single();if(fig.StartPoint!=part.Segments[0].To)throw new Exception("start coordinates changed");var commands=part.Segments.Skip(1).Where(s=>s.Type!="close").ToArray();if(fig.Segments.Count!=commands.Length)throw new Exception("segment count changed");for(int i=0;i<commands.Length;i++){var source=commands[i];if(source.Type=="line" && (fig.Segments[i] is not LineSegment line || line.Point!=source.To))throw new Exception("line coordinates changed");if(source.Type=="cubic" && (fig.Segments[i] is not BezierSegment curve || curve.Point1!=source.Cp1 || curve.Point2!=source.Cp2 || curve.Point3!=source.To))throw new Exception("cubic coordinates changed");}}
            var assembly=System.Reflection.Assembly.GetExecutingAssembly();var resource=assembly.GetManifestResourceNames().Single(n=>n.EndsWith("synthetic-fixture.json",StringComparison.Ordinal));using var stream=assembly.GetManifestResourceStream(resource)!;using var reader=new StreamReader(stream);string fixture=reader.ReadToEnd();
            var swapped=JsonNode.Parse(fixture)!;var array=swapped["output"]!["parts"]!.AsArray();var a=array[0]!.DeepClone();var b=array[1]!.DeepClone();array[0]=b;array[1]=a;var reversed=SyntheticBoundary.ReadFixture(Encoding.UTF8.GetBytes(swapped.ToJsonString()));if(Preview.Label(reversed.Parts[0])!="Arka gövde"||Preview.Label(reversed.Parts[1])!="Ön gövde")throw new Exception("reversed labels");
            foreach(var bad in new[]{fixture.Replace("\"approved\": false","\"approved\": true"),fixture.Replace("\"biceps\": 300","\"biceps\": 301"),fixture.Replace("\"coordinateSpace\": \"part-local\"","\"coordinateSpace\": \"placed\""),fixture.Replace("\"name\": \"breanna.front\"","\"name\": \"breanna.back\""),fixture.Replace("\"type\": \"cubic\"","\"type\": \"invalid\"")}){if(bad==fixture)throw new Exception("test mutation did not apply");try{SyntheticBoundary.ReadFixture(Encoding.UTF8.GetBytes(bad));throw new Exception("invalid fixture accepted");}catch(InvalidDataException){}}
            var app=new Application();var w=new ResearchWindow();w.EnterSample();w.ShowCandidate();if(!w.Shown||!w.RendersInk())throw new Exception("preview missing or blank");w.Alter();if(w.Shown||w.Confirmed)throw new Exception("stale preview");
            Console.WriteLine("PASS: synthetic input and units, validated fixture rejection, reversed labels, exact geometry, rendering and stale clearing; no window shown");return 0;
        }catch(Exception e){Console.Error.WriteLine("FAIL: "+e.Message);return 1;}
    }
}
