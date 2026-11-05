using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using TekstilCAD.Patterns;
using BodiceWpfReader;
using FourPieceResearch;
namespace WaistInspection;

internal sealed record Measurement(bool Available,double UpperMm,double LowerMm,double DifferenceMm,string Text);
internal static class Compare
{
    const string MatrixHash="a68c9d9184ccbe40b0eaeb8e18c58ab75c4a16b5ef03fd9e26d2866837330c1b";
    const string SourceHash="2eb13d29c5549ef18cc8180ed751f4d50635a91e408526c9fbed3de3e5d9edb0";
    internal static byte[] Source(){var a=Assembly.GetExecutingAssembly();var name=a.GetManifestResourceNames().Single(x=>x.EndsWith(".source-cases.json",StringComparison.Ordinal));using var s=a.GetManifestResourceStream(name)??throw new InvalidDataException();using var m=new MemoryStream();s.CopyTo(m);return m.ToArray();}
    static Point Pt(JsonElement x)=>new(x.GetProperty("x").GetDouble(),x.GetProperty("y").GetDouble());
    static bool Has(JsonElement e,string key,string value)=>e.GetProperty(key).EnumerateArray().Any(x=>x.GetString()==value);
    static bool Same(Point? p,JsonElement q)=>p is Point a && a==Pt(q);
    internal static Measurement Inspect(string id,Preview upper,ALinePanelPreview lower,byte[] source)
    {
        try {
            using var doc=JsonDocument.Parse(source,new JsonDocumentOptions{MaxDepth=20});var root=doc.RootElement;
            if(root.GetProperty("sourceVersion").GetString()!="4.10.2"||root.GetProperty("matrixSha256").GetString()!=MatrixHash||root.GetProperty("backSourceSha256").GetString()!=SourceHash)throw new InvalidDataException();
            var c=root.GetProperty("cases").EnumerateArray().Single(x=>x.GetProperty("id").GetString()==id);var o=c.GetProperty("options");
            if(c.GetProperty("variant").GetString()!="primary-only"||o.GetProperty("primaryBustDart").GetString()!="06:00"||o.GetProperty("secondaryBustDart").GetString()!="none"||o.GetProperty("waistDart").GetBoolean()||o.GetProperty("shoulderDart").GetBoolean())throw new InvalidDataException();
            var edges=c.GetProperty("edges").EnumerateArray().ToArray();var p=upper.Parts.Single(x=>x.Name=="breanna.back");var ops=p.Segments;
            if(ops.Count!=edges.Length+1||ops[0].Type!="move")throw new InvalidDataException();
            Point current=ops[0].To!.Value,first=current;int found=0;Point candidateStart=default,candidateEnd=default;
            for(int i=0;i<edges.Length;i++){
                var e=edges[i];var op=ops[i+1];string kind=e.GetProperty("type").GetString()!;string mapped=kind=="curve"?"cubic":kind;
                if(op.Type!=mapped||current!=Pt(e.GetProperty("from")))throw new InvalidDataException();
                Point end=op.Type=="close"?first:op.To!.Value;if(end!=Pt(e.GetProperty("to")))throw new InvalidDataException();
                if(op.Type=="cubic"&&(!Same(op.Cp1,e.GetProperty("cp1"))||!Same(op.Cp2,e.GetProperty("cp2"))))throw new InvalidDataException();
                if(kind=="line" && Has(e,"fromNames","cbWaist") && Has(e,"toNames","waist")){
                    if(e.GetProperty("fromNames").GetArrayLength()!=1||e.GetProperty("toNames").GetArrayLength()!=1||e.GetProperty("exactZero").GetBoolean()||e.GetProperty("sourceOpIndex").GetInt32()!=3||e.GetProperty("id").GetString()!="breanna.back/seam/op-3")throw new InvalidDataException();found++;candidateStart=current;candidateEnd=end;}
                current=end;
            }
            var expectedLower=ALinePanelPreview.Create(Scenario.Get(id).Lower);
            if(found!=1||lower.CanApplyOrExport||lower.Back.Count<2||!lower.Back.SequenceEqual(expectedLower.Back)||!lower.Front.SequenceEqual(expectedLower.Front))throw new InvalidDataException();
            var l0=lower.Back[0];var l1=lower.Back[1];double up=(candidateEnd-candidateStart).Length,lo=Math.Sqrt(Math.Pow(l1.X-l0.X,2)+Math.Pow(l1.Y-l0.Y,2));if(!double.IsFinite(up)||!double.IsFinite(lo)||up<=0||lo<=0)throw new InvalidDataException();
            var msg=$"ARKA yarım bel incelemesi · yöntem içi kaynak adayları\nÜst: Breanna 4.10.2, {id}/primary-only, arka bel ve omuz pensi kapalı; kaynak cbWaist → waist, seam/op-3. Alt: ALinePanelPreview arka bel, merkez → yan, nokta 0 → 1. İki ayrı kaynak için yalnız karşılaştırma amacıyla seçildi; fiziksel eşleşme doğrulanmadı.\nYaklaşık üst {up:F3} mm; alt {lo:F3} mm; üst − alt {up-lo:+0.000;-0.000;0.000} mm. Eşik, dikiş uyumu veya üretim kararı verilmedi. Ön pensli bel, bağlantı yönü, kumaş, eğri/beden uyumu ve prova çözülmedi. Çalışan motor kimliği ve lisansları doğrulanmadı.";
            return new(true,up,lo,up-lo,msg);
        }catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException or InvalidDataException or ArgumentException or NullReferenceException or FormatException or OverflowException){return new(false,0,0,0,"Arka bel adayları bu senaryo ve kaynak verisinde doğrulanamadı; karşılaştırma yok. Dikiş ilişkisi çözümlenmedi.");}
    }
}
