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
    var overlap = DartClosure.Analyze(new(0,5),new(0,10),new(0,0),new(10,0),new(0,0),0);
    Check(overlap.ExactBinarySquaredLegsEqual && overlap.OutsideSegmentContact == SegmentContact.Kind.Overlap, "counterexample: equal legs and overlapping outside pieces");
    Check(exact.OutsideSegmentContact == SegmentContact.Kind.AllowedEndpoint, "allowed dart endpoint");
    Check(SegmentContact.Classify(new(0,0),new(1,0),new(1,0),new(2,0),true) == SegmentContact.Kind.AllowedEndpoint, "straight endpoint");
    Check(SegmentContact.Classify(new(0,0),new(2,0),new(2,0),new(1,0),true) == SegmentContact.Kind.Overlap, "overlap category");
    Check(SegmentContact.Classify(new(0,0),new(1,0),new(1.1,0),new(2,0),true) == SegmentContact.Kind.OpenEndpoint, "open endpoint category");
    Check(SegmentContact.Classify(new(0,0),new(2,2),new(0,2),new(2,0),false) == SegmentContact.Kind.CrossingOrTouch, "crossing category");
    Point2[] contour = [new(-2,10),new(0,10),new(0,0),new(10,0),new(10,-3),new(-3,-3),new(-3,10)];
    bool[] partition = [true,true,false,false,false,false,true];
    string[] roles = ["center","legA","tip","legB","side","other1","other2"];
    var snapshot = ContourDiagnostic.Capture("front",contour,partition,roles);
    string identity = snapshot.Identity();
    var diagnostic = ContourDiagnostic.Inspect(snapshot,identity);
    Check(diagnostic.Status == "blocked" && !diagnostic.ProductionAllowed && diagnostic.Issues.Any(x=>x.StartsWith("AllowedEndpoint:0:3")), "allowed dart join in contour");
    Check(ContourDiagnostic.Inspect(snapshot,"wrong").Issues.Contains("identity-mismatch"), "identity mismatch");
    partition[0]=false; roles[0]="wrong"; contour[0]=new(999,999);
    Check(snapshot.Identity()==identity, "snapshot independent of later input mutation");
    var changed = ContourDiagnostic.Capture("front",contour,partition,roles);
    Check(changed.Identity()!=identity && ContourDiagnostic.Inspect(changed,identity).Issues.Contains("identity-mismatch"), "identity binds values roles and partition");
    var otherPart = ContourDiagnostic.Capture("back",snapshotCopyPoints(),snapshotCopyPartition(),snapshotCopyRoles());
    Check(otherPart.Identity()!=identity, "identity binds part");
    Point2[] snapshotCopyPoints() => [new(-2,10),new(0,10),new(0,0),new(10,0),new(10,-3),new(-3,-3),new(-3,10)];
    bool[] snapshotCopyPartition() => [true,true,false,false,false,false,true];
    string[] snapshotCopyRoles() => ["center","legA","tip","legB","side","other1","other2"];
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
    var comparison = SyntheticWaistComparison.Compare("synthetic-base", 0.01);
    Near(comparison.FrontUpperOuterPiecesMm,203.5,1e-7); Near(comparison.FrontLowerWaistMm,180); Near(comparison.DifferenceMm,23.5,1e-7);
    Check(comparison.Status=="unresolved" && !comparison.WithinNumericalThreshold && !comparison.ProductionAllowed && !comparison.ExportAllowed && comparison.Issues.Contains("full-contour-topology-unresolved"), "comparison blocked");
    var lenient = SyntheticWaistComparison.Compare("synthetic-base",100);
    Check(lenient.WithinNumericalThreshold && lenient.Status=="unresolved" && lenient.SnapshotId==comparison.SnapshotId, "threshold is not approval or snapshot input");
    var scaledComparison=SyntheticWaistComparison.Compare("synthetic-scaled",0);
    Near(scaledComparison.FrontLowerWaistMm,201.6,1e-7); Near(scaledComparison.DifferenceMm,26.32,1e-7);
    Check(scaledComparison.SnapshotId!=comparison.SnapshotId, "different synthetic snapshots");
    Reject(()=>SyntheticWaistComparison.Compare("synthetic-other",0));
    Reject(()=>SyntheticWaistComparison.Compare("synthetic-base",double.NaN));
    Reject(()=>SyntheticWaistComparison.Compare("synthetic-base",-1));
    var lower = ALinePanelPreview.Create(new(350,370,480,520,10,20,20,20,200,700,1600));
    Near(Geometry.Distance(lower.Front[0],lower.Front[1]),180); Near(Geometry.Distance(lower.Back[0],lower.Back[1]),195);
    Check(!lower.CanApplyOrExport, "lower export");
    Console.WriteLine($"PASS: analytical and synthetic fixtures; base outside={sample.OutsideLength:F6} mm, gap={sample.EndpointGap:G17} mm, binary squared equality={sample.ExactBinarySquaredLegsEqual}.");
    Console.WriteLine("Numerical threshold is not sewing tolerance. Sewing status unresolved; no export.");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine($"FAIL: {ex.Message}"); return 1; }



