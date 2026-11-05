using System.Text.Json;
using GarmentCodeSlice;
using TekstilCAD.Core;
if(args.Length!=1 || args[0]!="--verify") return 2;
try
{
    void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
    using var stream=typeof(Engine).Assembly.GetManifestResourceStream("GarmentCodeSlice.golden.json")!;
    using var reader=new StreamReader(stream); using var json=JsonDocument.Parse((await reader.ReadToEndAsync()).TrimStart('\uFEFF'));
    Check(json.RootElement.GetProperty("source_commit").GetString()=="d449629979028123a5c4dc9e732a2ec19b7fce31" && json.RootElement.GetProperty("unit").GetString()=="cm","fixture identity");
    foreach(var item in json.RootElement.GetProperty("cases").EnumerateArray())
    {
        var b=item.GetProperty("body_cm"); var shirt=item.GetProperty("shirt"); var skirt=item.GetProperty("skirt");
        double B(string n)=>b.GetProperty(n).GetDouble(); double S(string n)=>shirt.GetProperty(n).GetDouble();
        var input=new Input(B("bust"),B("back_width"),B("waist"),B("waist_back_width"),B("waist_line"),B("_shoulder_incl"),S("width"),S("flare"),S("length"),skirt.GetProperty("length_cm").GetDouble(),skirt.GetProperty("flare_cm").GetDouble());
        var slice=Engine.Create(input); Check(slice.Status=="unresolved"&&!slice.ProductionAllowed&&!slice.ExportAllowed,"gate");
        foreach(var panel in slice.Panels)
        {
            var expected=item.GetProperty("panels").GetProperty(panel.Name);
            Check(!expected.GetProperty("self_intersecting").GetBoolean(),"reference contour");
            var edges=expected.GetProperty("edges").EnumerateArray().ToArray(); Check(edges.Length==panel.Vertices.Count,"edge count");
            for(int i=0;i<edges.Length;i++)
            {
                Check(edges[i].GetProperty("type").GetString()=="Edge","unsupported curve");
                var start=edges[i].GetProperty("start");var end=edges[i].GetProperty("end");
                double dx=end[0].GetDouble()-start[0].GetDouble(),dy=end[1].GetDouble()-start[1].GetDouble();
                var a=panel.Vertices[i];var z=panel.Vertices[(i+1)%panel.Vertices.Count];
                Check(Math.Abs(z.X-a.X-dx)<1e-9 && Math.Abs(z.Y-a.Y-dy)<1e-9 && Math.Abs(Geometry.Distance(a,z)-edges[i].GetProperty("length").GetDouble())<1e-9,"edge mismatch: "+panel.Name);
            }
        }
        var references=item.GetProperty("panels");
        foreach(var tuple in new[]{("front_half",(input.Waist-input.WaistBackWidth)/2), ("back_half",input.WaistBackWidth/2)})
        {
            var bottom=references.GetProperty(tuple.Item1).GetProperty("interfaces").GetProperty("bottom");
            Check(Math.Abs(bottom.GetProperty("projected_lengths")[0].GetDouble()-tuple.Item2)<1e-9,"gathered projection");
        }
        var stitches=item.GetProperty("source_stitches").EnumerateArray().ToArray();
        string[] asides=["skirt_front:right","skirt_front:left"], bsides=["skirt_back:right","skirt_back:left"];
        for(int i=0;i<2;i++)
        {
            Check(stitches[i].GetProperty("a").GetString()==asides[i] && stitches[i].GetProperty("b").GetString()==bsides[i],"stitch identity");
            var lengths=stitches[i].GetProperty("projected_cm");
            double computed=Math.Sqrt(input.SkirtLength*input.SkirtLength+input.SkirtFlare*input.SkirtFlare);
            Check(Math.Abs(lengths[0].GetDouble()-computed)<1e-9 && Math.Abs(lengths[1].GetDouble()-computed)<1e-9,"skirt stitch lengths");
        }
        Check(slice.SourceSkirtStitches.Count==2 && item.GetProperty("source_stitches").GetArrayLength()==2 && item.GetProperty("upper_lower_stitches").GetString()=="not_generated","stitch scope");
    }
    void Reject(Input x) { try { Engine.Create(x); } catch(ArgumentException) { return; } throw new Exception("Invalid input accepted"); }
    var valid=new Input(100,44,80,36,40,12,1.1,1,1,60,12);
    Reject(valid with { Bust=double.NaN }); Reject(valid with { BackWidth=100 }); Reject(valid with { WaistBackWidth=80 }); Reject(valid with { SkirtLength=0 }); Reject(valid with { ShoulderDegrees=90 });
    Console.WriteLine("PASS: two synthetic Python fixtures, four panels each, line vectors and lengths; invalid inputs rejected; production unresolved."); return 0;
}
catch(Exception ex) { Console.Error.WriteLine("FAIL: "+ex.Message); return 1; }

