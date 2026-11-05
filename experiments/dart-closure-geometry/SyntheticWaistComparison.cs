using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace DartClosureGeometry;

// Closed set of synthetic records: source waist chain and lower-panel input
// are bound by this local fixture mapping, not by any personal measurements.
public static class SyntheticWaistComparison
{
    public sealed record Comparison(string SnapshotId, double FrontUpperOuterPiecesMm,
        double FrontLowerWaistMm, double DifferenceMm, double NumericalThresholdMm,
        bool WithinNumericalThreshold, SegmentContact.Kind CandidateJoin,
        IReadOnlyList<string> Issues)
    {
        public string Status => "unresolved";
        public bool ProductionAllowed => false;
        public bool ExportAllowed => false;
    }
    public static Comparison Compare(string caseId, double numericalThresholdMm)
    {
        if (!double.IsFinite(numericalThresholdMm) || numericalThresholdMm < 0 || numericalThresholdMm > 1000)
            throw new ArgumentOutOfRangeException(nameof(numericalThresholdMm));
        ALinePanelInput input = caseId switch
        {
            "synthetic-base" => new(350,370,480,520,10,20,20,20,200,700,1600),
            "synthetic-scaled" => new(392,414.4,537.6,582.4,11.2,22.4,22.4,22.4,224,784,1792),
            _ => throw new ArgumentException("Only mapped synthetic records are supported.", nameof(caseId))
        };
        using var stream=typeof(SyntheticWaistComparison).Assembly.GetManifestResourceStream("DartClosureGeometry.front-source-cases.json") ?? throw new InvalidOperationException("Missing fixture.");
        using var bytes=new MemoryStream(); stream.CopyTo(bytes); byte[] fixture=bytes.ToArray();
        using var doc=JsonDocument.Parse(fixture);
        var root=doc.RootElement;
        if(root.GetProperty("sourceVersion").GetString()!="4.10.2") throw new InvalidOperationException("Source version mismatch.");
        var item=root.GetProperty("cases").EnumerateArray().Single(x=>x.GetProperty("id").GetString()==caseId);
        var options=item.GetProperty("options");
        if(item.GetProperty("variant").GetString()!="primary-only" || options.GetProperty("primaryBustDart").GetString()!="06:00" || options.GetProperty("secondaryBustDart").GetString()!="none" || options.GetProperty("waistDart").GetBoolean() || options.GetProperty("shoulderDart").GetBoolean()) throw new InvalidOperationException("Unsupported semantics.");
        var edges=item.GetProperty("edges").EnumerateArray().Where(e=>e.GetProperty("sourceOpIndex").GetInt32() is >= 2 and <= 5).OrderBy(e=>e.GetProperty("sourceOpIndex").GetInt32()).ToArray();
        string[] names=["cfWaist","primaryBustDart1","primaryBustDartTip","primaryBustDart2","waist"];
        Point2 P(JsonElement p)=>new(p.GetProperty("x").GetDouble(),p.GetProperty("y").GetDouble());
        if(edges.Length!=4) throw new InvalidOperationException("Incomplete chain.");
        for(int i=0;i<4;i++)
        {
            var e=edges[i];
            if(e.GetProperty("sourceOpIndex").GetInt32()!=i+2 || e.GetProperty("id").GetString()!=$"breanna.front/seam/op-{i+2}" || e.GetProperty("type").GetString()!="line" || !e.GetProperty("fromNames").EnumerateArray().Any(n=>n.GetString()==names[i]) || !e.GetProperty("toNames").EnumerateArray().Any(n=>n.GetString()==names[i+1]) || i>0 && P(edges[i-1].GetProperty("to"))!=P(e.GetProperty("from"))) throw new InvalidOperationException("Source edge identity, role, or continuity mismatch.");
        }
        var d=DartClosure.Analyze(P(edges[0].GetProperty("from")),P(edges[0].GetProperty("to")),P(edges[1].GetProperty("to")),P(edges[2].GetProperty("to")),P(edges[3].GetProperty("to")),numericalThresholdMm);
        var lower=ALinePanelPreview.Create(input);
        double bottom=Geometry.Distance(lower.Front[0],lower.Front[1]);
        // Snapshot includes fixture bytes, selected ID and exact binary64 lower input.
        using var buffer=new MemoryStream(); using(var writer=new BinaryWriter(buffer,Encoding.UTF8,true))
        {
            writer.Write("synthetic-waist-snapshot-v1"); writer.Write(fixture.Length); writer.Write(fixture); writer.Write(caseId);
            foreach(double v in new[]{input.FrontWaistArc,input.BackWaistArc,input.FrontHipArc,input.BackHipArc,input.FrontWaistEase,input.BackWaistEase,input.FrontHipEase,input.BackHipEase,input.HipDepth,input.WaistToHem,input.FinishedHemCircumference}) writer.Write(BitConverter.DoubleToInt64Bits(v));
        }
        string identity=Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
        var issues=new List<string>{"source-semantics-not-confirmed-as-sewn-waist", "full-contour-topology-unresolved", "physical-closure-and-rotation-error-unresolved", "sewing-tolerance-and-fitting-unresolved"};
        if(d.OutsideSegmentContact is SegmentContact.Kind.Overlap or SegmentContact.Kind.CrossingOrTouch or SegmentContact.Kind.Unresolved) issues.Add("candidate-segments-intersect-or-unresolved");
        if(!d.ExactBinarySquaredLegsEqual || d.EndpointGap!=0) issues.Add("dart-endpoint-unresolved");
        double difference=d.OutsideLength-bottom;
        return new(identity,d.OutsideLength,bottom,difference,numericalThresholdMm,Math.Abs(difference)<=numericalThresholdMm,d.OutsideSegmentContact,issues.AsReadOnly());
    }
}
