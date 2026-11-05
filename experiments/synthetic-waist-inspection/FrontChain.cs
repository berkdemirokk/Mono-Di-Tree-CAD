using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using BodiceWpfReader;
using FourPieceResearch;
using TekstilCAD.Patterns;
namespace WaistInspection;

internal sealed record FrontReport(bool Available,double OuterMm,double LowerMm,double RawPathMm,double ChordMm,double Leg1Mm,double Leg2Mm,string Text);
internal static class FrontChain
{
    const string MatrixHash="a68c9d9184ccbe40b0eaeb8e18c58ab75c4a16b5ef03fd9e26d2866837330c1b";
    internal static byte[] Source(){var a=Assembly.GetExecutingAssembly();string name=a.GetManifestResourceNames().Single(x=>x.EndsWith("front-source-cases.json",StringComparison.Ordinal));using var stream=a.GetManifestResourceStream(name)??throw new InvalidDataException();using var m=new MemoryStream();stream.CopyTo(m);return m.ToArray();}
    static Point Pt(JsonElement e)=>new(e.GetProperty("x").GetDouble(),e.GetProperty("y").GetDouble());
    static bool MatchNames(JsonElement e,string key,params string[] names){var actual=e.GetProperty(key).EnumerateArray().Select(x=>x.GetString()).OrderBy(x=>x,StringComparer.Ordinal).ToArray();return actual.SequenceEqual(names.OrderBy(x=>x,StringComparer.Ordinal),StringComparer.Ordinal);}
    static bool Same(Point? p,JsonElement q)=>p is Point a && a==Pt(q);
    internal static FrontReport Inspect(string id,Preview upper,ALinePanelPreview lower,byte[] source)
    {
        try{using var doc=JsonDocument.Parse(source,new JsonDocumentOptions{MaxDepth=20});var root=doc.RootElement;
            if(root.GetProperty("sourceVersion").GetString()!="4.10.2"||root.GetProperty("matrixSha256").GetString()!=MatrixHash)throw new InvalidDataException();var hashes=root.GetProperty("sourceHashes");
            if(hashes.GetProperty("front.mjs").GetString()!="95dbf0b0fddcc2d81b7aa6a85bf09fcf2543f19e643ba09c60132367eb90ec67"||hashes.GetProperty("front-primary-only.mjs").GetString()!="401b29397eeb3c5e43ab137168cfe046719009a17f97f6ac8df05fe88f89d469"||hashes.GetProperty("dart-utils.mjs").GetString()!="f8e08b1f9c84087fb7366e64c97d80b5de0558c2b0a58ed69ab378975d9e1bb6")throw new InvalidDataException();
            var c=root.GetProperty("cases").EnumerateArray().Single(x=>x.GetProperty("id").GetString()==id);var o=c.GetProperty("options");if(c.GetProperty("variant").GetString()!="primary-only"||o.GetProperty("primaryBustDart").GetString()!="06:00"||o.GetProperty("secondaryBustDart").GetString()!="none"||o.GetProperty("waistDart").GetBoolean()||o.GetProperty("shoulderDart").GetBoolean())throw new InvalidDataException();
            var edges=c.GetProperty("edges").EnumerateArray().ToArray();var ops=upper.Parts.Single(p=>p.Name=="breanna.front").Segments;if(ops.Count!=edges.Length+1||ops[0].Type!="move")throw new InvalidDataException();Point cur=ops[0].To!.Value,first=cur;
            var picked=new Dictionary<int,(Point From,Point To)>();
            for(int i=0;i<edges.Length;i++){var e=edges[i];var op=ops[i+1];string kind=e.GetProperty("type").GetString()!;if(op.Type!=(kind=="curve"?"cubic":kind)||cur!=Pt(e.GetProperty("from")))throw new InvalidDataException();Point end=op.Type=="close"?first:op.To!.Value;if(end!=Pt(e.GetProperty("to")))throw new InvalidDataException();if(op.Type=="cubic"&&(!Same(op.Cp1,e.GetProperty("cp1"))||!Same(op.Cp2,e.GetProperty("cp2"))))throw new InvalidDataException();int index=e.GetProperty("sourceOpIndex").GetInt32();if(index is >=2 and <=5){if(kind!="line"||e.GetProperty("exactZero").GetBoolean()||e.GetProperty("id").GetString()!=$"breanna.front/seam/op-{index}"||!picked.TryAdd(index,(cur,end)))throw new InvalidDataException();}cur=end;}
            if(picked.Count!=4)throw new InvalidDataException();var named=edges.Where(e=>e.GetProperty("sourceOpIndex").GetInt32() is >=2 and <=5).ToDictionary(e=>e.GetProperty("sourceOpIndex").GetInt32());
            if(!MatchNames(named[2],"fromNames","cfWaist")||!MatchNames(named[2],"toNames","origBustDart1","primaryBustDart1")||!MatchNames(named[3],"fromNames","origBustDart1","primaryBustDart1")||!MatchNames(named[3],"toNames","primaryBustDartTip")||!MatchNames(named[4],"fromNames","primaryBustDartTip")||!MatchNames(named[4],"toNames","origBustDart2","primaryBustDart2")||!MatchNames(named[5],"fromNames","origBustDart2","primaryBustDart2")||!MatchNames(named[5],"toNames","waist"))throw new InvalidDataException();
            var expected=ALinePanelPreview.Create(Scenario.Get(id).Lower);if(lower.CanApplyOrExport||!lower.Front.SequenceEqual(expected.Front)||!lower.Back.SequenceEqual(expected.Back)||lower.Front.Count<2)throw new InvalidDataException();
            double Len(int k)=>(picked[k].To-picked[k].From).Length;double outer=Len(2)+Len(5),leg1=Len(3),leg2=Len(4),raw=outer+leg1+leg2,chord=(picked[5].To-picked[2].From).Length;var lo0=lower.Front[0];var lo1=lower.Front[1];double low=Math.Sqrt(Math.Pow(lo1.X-lo0.X,2)+Math.Pow(lo1.Y-lo0.Y,2));if(new[]{outer,leg1,leg2,raw,chord,low}.Any(x=>!double.IsFinite(x)||x<=0))throw new InvalidDataException();
            string text=$"ÖN yarım bel · kaynak zinciri (sentetik {id}, 06:00 tek göğüs pensi)\nKaynak yolu: cfWaist → pens 1 → pens ucu → pens 2 → waist. İki dış çizgi toplamı yaklaşık {outer:F3} mm; alt ön merkez–yan çizgisi {low:F3} mm; fark {outer-low:+0.000;-0.000;0.000} mm. Dış çizgi toplamı yalnız kaynak geometrisi adayıdır; pensin katlandığı veya alt bel ile dikilebildiği kanıtlanmadı.\nAçık yolun dört çizgisi pens bacakları dahil {raw:F3} mm; düz uçtan uca mesafe {chord:F3} mm; pens bacakları {leg1:F3} ve {leg2:F3} mm. Bunlar farklı geometrik ölçülerdir, otomatik dikiş boyu/uyum kararı değildir. Ön/arka bel şekli, katlama, yön, ortak referans, dikiş toleransı ve prova çözülmedi.";
            return new(true,outer,low,raw,chord,leg1,leg2,text);
        }catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException or InvalidDataException or ArgumentException or NullReferenceException or FormatException or OverflowException){return new(false,0,0,0,0,0,0,"Ön bel kaynak zinciri bu senaryoda doğrulanamadı; karşılaştırma yok.");}
    }
}
