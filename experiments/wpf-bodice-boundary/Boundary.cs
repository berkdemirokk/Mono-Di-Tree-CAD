using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TekstilCAD.Patterns;

namespace BodiceResearch;

internal sealed class SyntheticBoundary
{
    private readonly JsonDocument fixture;
    internal SyntheticBoundary()
    {
        var assembly=Assembly.GetExecutingAssembly();
        var name=assembly.GetManifestResourceNames().Single(n=>n.EndsWith("synthetic-fixture.json",StringComparison.Ordinal));
        using var stream=assembly.GetManifestResourceStream(name)??throw new InvalidOperationException("Sentetik örnek yok.");
        fixture=JsonDocument.Parse(stream);
    }
    internal Dictionary<string,decimal> Expected => fixture.RootElement.GetProperty("measurements").EnumerateObject().ToDictionary(x=>x.Name,x=>x.Value.GetDecimal(),StringComparer.Ordinal);
    internal JsonElement Evaluate(IReadOnlyDictionary<string,decimal> values,bool confirmed)
    {
        if(!confirmed || values.Count!=Expected.Count || Expected.Any(x=>!values.TryGetValue(x.Key,out var v)||v!=x.Value))
            throw new InvalidOperationException("Bu araştırma sürümü yalnız belirtilen sentetik örneği kabul eder; gerçek kişisel ölçü girmeyin.");
        var output=fixture.RootElement.GetProperty("output");
        if(output.GetProperty("approved").GetBoolean() || output.GetProperty("unit").GetString()!="mm" || output.GetProperty("coordinateSpace").GetString()!="part-local")throw new InvalidOperationException("Bilinmeyen örnek formatı.");
        return output;
    }
    internal static Dictionary<string,decimal> Parse(IReadOnlyDictionary<string,string> inputs,string? unit)
    {
        if(unit is not ("mm" or "cm"))throw new InvalidOperationException("Birim seçin.");
        if(inputs.Count!=BreannaMeasurementGate.Definitions.Count)throw new InvalidOperationException("15 ölçü gerekir.");
        var entries=new List<BreannaMeasurementInput>();
        foreach(var d in BreannaMeasurementGate.Definitions)
        {
            if(!inputs.TryGetValue(d.Key,out var s)||!decimal.TryParse(s.Replace(',','.'),NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var n))throw new InvalidOperationException("Tüm ölçüleri açıkça girin: "+d.Label);
            entries.Add(new(d.Key,n,d.IsAngle?BreannaUnit.Degree:unit=="mm"?BreannaUnit.Millimeter:BreannaUnit.Centimeter));
        }
        var result=BreannaMeasurementGate.Review(entries);
        if(!result.IsValid)throw new InvalidOperationException(string.Join("; ",result.Errors.Select(e=>e.Message)));
        return result.Values!.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);
    }
}

internal sealed class Preview : FrameworkElement
{
    internal JsonElement? Data {get;set;}
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);dc.DrawRectangle(Brushes.White,null,new Rect(RenderSize));
        if(Data is not { } data || RenderSize.Width<100 || RenderSize.Height<100)return;
        var parts=data.GetProperty("parts").EnumerateArray().ToArray();if(parts.Length!=2)return;
        for(int i=0;i<2;i++)
        {
            var segs=parts[i].GetProperty("segments").EnumerateArray().ToArray();var points=new List<Point>();
            foreach(var s in segs)foreach(var k in new[]{"to","cp1","cp2"})if(s.TryGetProperty(k,out var p))points.Add(Read(p));
            if(points.Count==0)continue;double minx=points.Min(p=>p.X),maxx=points.Max(p=>p.X),miny=points.Min(p=>p.Y),maxy=points.Max(p=>p.Y);
            double width=RenderSize.Width/2, scale=Math.Min((width-50)/Math.Max(1,maxx-minx),(RenderSize.Height-70)/Math.Max(1,maxy-miny));
            Point Map(JsonElement p){var q=Read(p);return new(i*width+25+(q.X-minx)*scale,40+(q.Y-miny)*scale);}
            var g=new StreamGeometry();using(var c=g.Open())foreach(var s in segs){switch(s.GetProperty("type").GetString()) {case "move":c.BeginFigure(Map(s.GetProperty("to")),false,true);break;case "line":c.LineTo(Map(s.GetProperty("to")),true,false);break;case "cubic":c.BezierTo(Map(s.GetProperty("cp1")),Map(s.GetProperty("cp2")),Map(s.GetProperty("to")),true,false);break;case "close":break;default:throw new InvalidOperationException("Bilinmeyen geometri.");}}
            dc.DrawGeometry(null,new Pen(Brushes.SteelBlue,2),g);
            dc.DrawText(new FormattedText(i==0?"Ön gövde":"Arka gövde",CultureInfo.GetCultureInfo("tr-TR"),FlowDirection.LeftToRight,new Typeface("Segoe UI"),14,Brushes.Black,1),new Point(i*width+25,12));
        }
    }
    private static Point Read(JsonElement p)=>new(p.GetProperty("x").GetDouble(),p.GetProperty("y").GetDouble());
}
