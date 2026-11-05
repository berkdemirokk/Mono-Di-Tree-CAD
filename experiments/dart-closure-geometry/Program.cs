using System.Text.Json;
using DartClosureGeometry;
using TekstilCAD.Core;
using TekstilCAD.Patterns;

if (args.Length != 1 || args[0] != "--verify") { Console.Error.WriteLine("Usage: --verify (synthetic fixtures only)"); return 2; }
try
{
    void Check(bool value, string message) { if (!value) throw new Exception(message); }
    void Near(double actual, double expected, double tolerance = 1e-9) => Check(Math.Abs(actual - expected) <= tolerance, $"Expected {expected}, got {actual}");
    Point2 center = new(-2,10), a = new(0,10), tip = new(0,0), b = new(10,0), side = new(10,-3);
    var exact = DartClosure.Analyze(center,a,tip,b,side,1e-9);
    Near(exact.RotatedCenter.X,10); Near(exact.RotatedCenter.Y,2); Near(exact.EndpointGap,0);
    Near(exact.OutsideLength,5); Check(exact.ExactBinarySquaredLegsEqual, "exact squared distance");
    Check(exact.WithinNumericalThreshold && exact.SewingStatus == "unresolved" && !exact.ProductionAllowed && !exact.ExportAllowed, "status");
    var unequal = DartClosure.Analyze(center,a,tip,new(12,0),new(12,-3),1);
    Near(unequal.EndpointGap,2); Check(!unequal.ExactBinarySquaredLegsEqual && !unequal.WithinNumericalThreshold, "unequal legs");
    Check(DartClosure.Analyze(center,a,tip,new(12,0),new(12,-3),3).SewingStatus == "unresolved", "threshold is not approval");
    Point2 Move(Point2 p) => new(p.X+123,p.Y-87);
    var moved = DartClosure.Analyze(Move(center),Move(a),Move(tip),Move(b),Move(side),1e-9);
    Near(moved.OutsideLength,exact.OutsideLength); Near(moved.EndpointGap,exact.EndpointGap);
    var reverse = DartClosure.Analyze(side,b,tip,a,center,1e-9); Near(reverse.EndpointGap,0); Near(reverse.OutsideLength,5);
    void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected invalid input rejection"); }
    Reject(() => DartClosure.Analyze(center,tip,tip,b,side,0));
    Reject(() => DartClosure.Analyze(center,a,tip,b,side,double.NaN));
    Reject(() => DartClosure.Analyze(new(double.PositiveInfinity,0),a,tip,b,side,0));
    var assembly = typeof(DartClosure).Assembly;
    using var stream = assembly.GetManifestResourceStream("DartClosureGeometry.front-source-cases.json") ?? throw new Exception("Missing fixture");
    using var document = JsonDocument.Parse(stream);
    var cases = document.RootElement.GetProperty("cases");
    Point2 P(JsonElement value) => new(value.GetProperty("x").GetDouble(),value.GetProperty("y").GetDouble());
    DartAnalysis Fixture(string id)
    {
        var item = cases.EnumerateArray().Single(x => x.GetProperty("id").GetString() == id);
        Check(item.GetProperty("variant").GetString() == "primary-only", "variant");
        var o = item.GetProperty("options");
        Check(o.GetProperty("primaryBustDart").GetString() == "06:00" && o.GetProperty("secondaryBustDart").GetString() == "none" && !o.GetProperty("waistDart").GetBoolean() && !o.GetProperty("shoulderDart").GetBoolean(), "options");
        var edges = item.GetProperty("edges").EnumerateArray().Where(x => x.GetProperty("sourceOpIndex").GetInt32() is >= 2 and <= 5).OrderBy(x => x.GetProperty("sourceOpIndex").GetInt32()).ToArray();
        Check(edges.Length == 4, "four operations");
        string[] names = ["cfWaist", "primaryBustDart1", "primaryBustDartTip", "primaryBustDart2", "waist"];
        for (int i=0; i<4; i++)
        {
            Check(edges[i].GetProperty("sourceOpIndex").GetInt32() == i+2 && edges[i].GetProperty("type").GetString() == "line", "operation");
            Check(edges[i].GetProperty("fromNames").EnumerateArray().Any(n => n.GetString() == names[i]) && edges[i].GetProperty("toNames").EnumerateArray().Any(n => n.GetString() == names[i+1]), "point names");
            if (i>0) Check(P(edges[i-1].GetProperty("to")) == P(edges[i].GetProperty("from")), "chain continuity");
        }
        return DartClosure.Analyze(P(edges[0].GetProperty("from")),P(edges[0].GetProperty("to")),P(edges[1].GetProperty("to")),P(edges[2].GetProperty("to")),P(edges[3].GetProperty("to")),1e-9);
    }
    var sample = Fixture("synthetic-base");
    Near(sample.OutsideLength,203.5,1e-7); Near(sample.OpenChainLength,512.0108556058206,1e-7);
    var scaled = Fixture("synthetic-scaled"); Near(scaled.OutsideLength,227.92,1e-7);
    var lower = ALinePanelPreview.Create(new(350,370,480,520,10,20,20,20,200,700,1600));
    Near(Geometry.Distance(lower.Front[0],lower.Front[1]),180); Near(Geometry.Distance(lower.Back[0],lower.Back[1]),195);
    Check(!lower.CanApplyOrExport, "lower export");
    Console.WriteLine($"PASS: analytical and synthetic fixtures; base outside={sample.OutsideLength:F6} mm, gap={sample.EndpointGap:G17} mm, binary squared equality={sample.ExactBinarySquaredLegsEqual}.");
    Console.WriteLine("Numerical threshold is not sewing tolerance. Sewing status unresolved; no export.");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine($"FAIL: {ex.Message}"); return 1; }
