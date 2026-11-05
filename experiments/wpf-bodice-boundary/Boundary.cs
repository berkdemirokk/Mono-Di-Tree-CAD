using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using BodiceWpfReader;
using TekstilCAD.Patterns;
using BodiceSnapshot = BodiceWpfReader.Preview;

namespace BodiceResearch;

internal sealed class SyntheticBoundary
{
    private readonly BodiceSnapshot snapshot;
    private static readonly IReadOnlyDictionary<string,decimal> Sample = new Dictionary<string,decimal>(StringComparer.Ordinal) {
        ["biceps"]=300,["bustFront"]=500,["bustSpan"]=190,["highBust"]=900,["highBustFront"]=450,["hpsToBust"]=270,
        ["hpsToWaistBack"]=420,["hpsToWaistFront"]=450,["waist"]=740,["waistToHips"]=200,["neck"]=370,
        ["shoulderToShoulder"]=390,["shoulderSlope"]=15,["shoulderToWrist"]=580,["wrist"]=160 };
    internal SyntheticBoundary()
    {
        var assembly=Assembly.GetExecutingAssembly();
        var name=assembly.GetManifestResourceNames().Single(n=>n.EndsWith("synthetic-fixture.json",StringComparison.Ordinal));
        using var stream=assembly.GetManifestResourceStream(name)??throw new InvalidOperationException("Sentetik örnek yok.");
        if(stream.Length>1_100_000)throw new InvalidDataException("Fixture too large");
        using var buffer=new MemoryStream();stream.CopyTo(buffer);snapshot=ReadFixture(buffer.ToArray());
    }
    internal static BodiceSnapshot ReadFixture(ReadOnlyMemory<byte> input)
    {
        if(input.Length==0 || input.Length>1_100_000)throw new InvalidDataException("Invalid fixture size");
        try {
            using var doc=JsonDocument.Parse(input.ToArray(),new JsonDocumentOptions{MaxDepth=10});var root=doc.RootElement;
            if(root.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Invalid fixture");
            var fields=root.EnumerateObject().ToArray();
            if(fields.Length!=2 || fields.Count(x=>x.Name=="measurements")!=1 || fields.Count(x=>x.Name=="output")!=1)throw new InvalidDataException("Invalid fixture fields");
            var measurements=root.GetProperty("measurements");if(measurements.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Invalid measurements");
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var p in measurements.EnumerateObject())if(!seen.Add(p.Name)||!Sample.TryGetValue(p.Name,out var value)||p.Value.ValueKind!=JsonValueKind.Number||!p.Value.TryGetDecimal(out var actual)||actual!=value)throw new InvalidDataException("Non-synthetic fixture measurements");
            if(seen.Count!=Sample.Count)throw new InvalidDataException("Missing fixture measurements");
            return BodiceReader.Read(Encoding.UTF8.GetBytes(root.GetProperty("output").GetRawText()));
        }catch(JsonException ex){throw new InvalidDataException("Invalid fixture JSON",ex);}
    }
    internal Dictionary<string,decimal> Expected => new(Sample,StringComparer.Ordinal);
    internal BodiceSnapshot Evaluate(IReadOnlyDictionary<string,decimal> values,bool confirmed)
    {
        if(!confirmed || values.Count!=Sample.Count || Sample.Any(x=>!values.TryGetValue(x.Key,out var v)||v!=x.Value))
            throw new InvalidOperationException("Bu araştırma sürümü yalnız belirtilen sentetik örneği kabul eder; gerçek kişisel ölçü girmeyin.");
        return snapshot;
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
    internal BodiceSnapshot? Data {get;set;}
    internal static string Label(Part p)=>p.Name switch {"breanna.front"=>"Ön gövde","breanna.back"=>"Arka gövde",_=>throw new InvalidDataException("Unknown part")};
    internal static PathGeometry Geometry(Part part)
    {
        var figure=new PathFigure{StartPoint=part.Segments[0].To!.Value,IsClosed=true,IsFilled=false};
        foreach(var s in part.Segments.Skip(1))switch(s.Type){case "line":figure.Segments.Add(new LineSegment(s.To!.Value,true));break;case "cubic":figure.Segments.Add(new BezierSegment(s.Cp1!.Value,s.Cp2!.Value,s.To!.Value,true));break;case "close":break;default:throw new InvalidDataException("Invalid segment");}
        return new PathGeometry(new[]{figure});
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);dc.DrawRectangle(Brushes.White,null,new Rect(RenderSize));
        if(Data is not { } data || RenderSize.Width<100 || RenderSize.Height<100)return;
        for(int i=0;i<data.Parts.Count;i++)
        {
            var part=data.Parts[i];var g=Geometry(part);var bounds=g.Bounds;if(bounds.IsEmpty)continue;
            double width=RenderSize.Width/2, scale=Math.Min((width-50)/Math.Max(1,bounds.Width),(RenderSize.Height-70)/Math.Max(1,bounds.Height));
            dc.PushTransform(new MatrixTransform(scale,0,0,scale,i*width+25-bounds.X*scale,40-bounds.Y*scale));
            dc.DrawGeometry(null,new Pen(Brushes.SteelBlue,2/scale),g);dc.Pop();
            dc.DrawText(new FormattedText(Label(part),CultureInfo.GetCultureInfo("tr-TR"),FlowDirection.LeftToRight,new Typeface("Segoe UI"),14,Brushes.Black,1),new Point(i*width+25,12));
        }
    }
}
