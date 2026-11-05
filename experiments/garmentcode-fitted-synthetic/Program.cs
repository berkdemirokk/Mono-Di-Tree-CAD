using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Numerics;
using System.Globalization;
using System.Text;
using FittedResearch;
using TekstilCAD.Core;

if(args.Length!=1 || args[0]!="--verify") return 2;
try
{
    MeasurementContractTests.Verify();
    using var stream=typeof(Validator).Assembly.GetManifestResourceStream("FittedCheck.golden.json")!;
    using var reader=new StreamReader(stream); string content=(await reader.ReadToEndAsync()).TrimStart('\uFEFF');
    using var data=JsonDocument.Parse(content); var result=Validator.Check(data.RootElement); Validator.CheckInterfaceMatching(data.RootElement); Validator.CheckPrecutBodice(); Validator.CheckBackWaist(); Validator.CheckWaistConstraints(); Validator.CheckSideExperiment(); Validator.CheckTangentSweep(); Validator.CheckOptimizerTrace(); Validator.CheckSeamCausality();
    var changed=JsonNode.Parse(content)!; changed["cases"]![0]!["panels"]!.AsObject().First().Value!["edges"]![0]!["samples"]![0]![0]=12345;
    using var corrupt=JsonDocument.Parse(changed.ToJsonString());
    try { Validator.Check(corrupt.RootElement); throw new Exception("Tampered curve accepted"); } catch(InvalidDataException) { }
    var badLink=JsonNode.Parse(content)!; badLink["cases"]![0]!["stitches"]![0]!["a"]!["edges"]![0]!["edge"]=99999;
    using(var invalidLink=JsonDocument.Parse(badLink.ToJsonString())) { try { Validator.Check(invalidLink.RootElement); throw new Exception("Dangling stitch accepted"); } catch(InvalidDataException) { } }
    var badBody=JsonNode.Parse(content)!; badBody["cases"]![0]!["body_input_cm_degree"]!["waist"]=81;
    using(var invalidBody=JsonDocument.Parse(badBody.ToJsonString())) { try { Validator.Check(invalidBody.RootElement); throw new Exception("Changed body accepted"); } catch(InvalidDataException) { } }
    var decimalCollision=JsonNode.Parse(content)!; decimalCollision["cases"]![0]!["body_input_cm_degree"]!["bust"]=100.0000000000000001m;
    if((double)100.0000000000000001m!=100d) throw new Exception("collision fixture invalid");
    using(var invalidDecimal=JsonDocument.Parse(decimalCollision.ToJsonString())) { try { Validator.Check(invalidDecimal.RootElement); throw new Exception("Rounded decimal body accepted"); } catch(InvalidDataException) { } }
    // This JSON decimal is beyond System.Decimal precision and must not round to 100.
    string tooPrecise=content.Replace("\"bust\": 100", "\"bust\": 1000000000000000000000000000001");
    if(tooPrecise==content) throw new Exception("high precision fixture missing");
    using(var invalidPrecision=JsonDocument.Parse(tooPrecise)) { try { Validator.Check(invalidPrecision.RootElement); throw new Exception("Overprecision body accepted"); } catch(InvalidDataException) { } }
    void RejectSeam(JsonNode changed,string label) { using var invalid=JsonDocument.Parse(changed.ToJsonString()); try { Validator.Check(invalid.RootElement); throw new Exception(label+" accepted"); } catch(InvalidDataException) { } }
    var wrongEdge=JsonNode.Parse(content)!; var edgeNode=wrongEdge["cases"]![0]!["stitches"]![0]!["a"]!["edges"]![0]!;
    int original=edgeNode["edge"]!.GetValue<int>(); edgeNode["edge"]=original==0?1:0; RejectSeam(wrongEdge,"valid wrong edge");
    var wrongPanel=JsonNode.Parse(content)!; var panelNode=wrongPanel["cases"]![0]!["stitches"]![0]!["a"]!["edges"]![0]!;
    string panel=panelNode["panel"]!.GetValue<string>(); panelNode["panel"]=panel=="skirt_front"?"skirt_back":"skirt_front"; panelNode["edge"]=0; RejectSeam(wrongPanel,"valid wrong panel");
    var wrongOrder=JsonNode.Parse(content)!; var ordered=wrongOrder["cases"]![0]!["stitches"]!.AsArray().First(r=>r!["a"]!["edges"]!.AsArray().Count>=2)!["a"]!["edges"]!.AsArray();
    var first=ordered[0]!.DeepClone(); var second=ordered[1]!.DeepClone(); ordered[0]=second; ordered[1]=first; RejectSeam(wrongOrder,"reversed stitch order");
    var wrongFlip=JsonNode.Parse(content)!; var flip=wrongFlip["cases"]![0]!["stitches"]![0]!["a"]!["edges"]![0]!; flip["flipped"]=!flip["flipped"]!.GetValue<bool>(); RejectSeam(wrongFlip,"wrong seam orientation");
    try { PartialFittedEngine.Create("not-synthetic"); throw new Exception("Unknown case accepted"); } catch(ArgumentException) { }
    var baseParts=PartialFittedEngine.Create("synthetic-base"); var distinctParts=PartialFittedEngine.Create("synthetic-distinct");
    if(baseParts.Segments.Count!=38 || distinctParts.Segments.Count!=38 || baseParts.Segments[0].Start==distinctParts.Segments[0].Start) throw new Exception("Partial geometry input sensitivity");
    void RejectDart(Action action) { try { action(); throw new Exception("Invalid cut accepted"); } catch(ArgumentException) { } }
    RejectDart(()=>DartCut.Horizontal(0,2,0,2,.5,true)); RejectDart(()=>DartCut.Horizontal(0,2,0,3,5,true)); RejectDart(()=>DartCut.Horizontal(0,double.NaN,0,2,5,true));
    if(DartCut.SimpleClosed(new[]{new DartCut.P(0,0),new DartCut.P(1,0),new DartCut.P(2,0)})) throw new Exception("Degenerate polygon accepted");
    var shape=new[]{new DartCut.P(0,0),new DartCut.P(4,0),new DartCut.P(4,4),new DartCut.P(0,4)};
    if(!DartCut.SimpleClosed(shape) || DartCut.SimpleClosed(new[]{shape[0],shape[2],shape[1],shape[3]})) throw new Exception("Cut topology test");
    var crossingCurves=new[]{new BodyCurveStudy.Edge("crossing",0,"L",0,0,2,2,[],null),new BodyCurveStudy.Edge("crossing",1,"L",2,2,0,2,[],null),new BodyCurveStudy.Edge("crossing",2,"L",0,2,2,0,[],null),new BodyCurveStudy.Edge("crossing",3,"L",2,0,0,0,[],null)};
    var crossingStudy=BodyCurveStudy.Inspect("crossing",crossingCurves);
    if(crossingStudy.Unresolved==0 || crossingStudy.ExportAllowed || crossingStudy.Status!="unresolved")throw new Exception("Crossing curve contour incorrectly cleared");
    var square=new[]{new BodyCurveStudy.Edge("square",0,"L",0,0,2,0,[],null),new BodyCurveStudy.Edge("square",1,"L",2,0,2,2,[],null),new BodyCurveStudy.Edge("square",2,"L",2,2,0,2,[],null),new BodyCurveStudy.Edge("square",3,"L",0,2,0,0,[],null)};
    var closedStatus=ResearchViewSummary.ToPanelStatus(BodyCurveStudy.Inspect("square",square));
    if(closedStatus.Closure!=BodyCurveStudy.ClosureEvidence.ClosedRepresented)throw new Exception("Closed represented square not recognized");
    var broken=square.ToArray();broken[0]=broken[0] with { Bx=1.9 };var brokenStatus=ResearchViewSummary.ToPanelStatus(BodyCurveStudy.Inspect("square",broken));
    if(brokenStatus.Closure!=BodyCurveStudy.ClosureEvidence.Open || brokenStatus.CurvePairs!=0 || brokenStatus.Status!="unresolved")throw new Exception("Open contour incorrectly classified");
    var reversed=square.ToArray();reversed[1]=reversed[1] with { Ax=2,Ay=2,Bx=2,By=0 };var reverseStatus=ResearchViewSummary.ToPanelStatus(BodyCurveStudy.Inspect("square",reversed));
    if(reverseStatus.Closure!=BodyCurveStudy.ClosureEvidence.Open || reverseStatus.CurvePairs!=0)throw new Exception("Reversed contour incorrectly classified");
    var wrongId=square.ToArray();wrongId[1]=wrongId[1] with { Id=2 };if(BodyCurveStudy.Inspect("square",wrongId).Closure!=BodyCurveStudy.ClosureEvidence.InvalidIdentity)throw new Exception("Wrong edge ID accepted");
    var wrongContourPanel=square.ToArray();wrongContourPanel[1]=wrongContourPanel[1] with { Panel="other" };if(BodyCurveStudy.Inspect("square",wrongContourPanel).Closure!=BodyCurveStudy.ClosureEvidence.InvalidIdentity)throw new Exception("Wrong panel accepted");
    var zero=square.ToArray();zero[1]=zero[1] with { Bx=2,By=0 };if(BodyCurveStudy.Inspect("square",zero).Closure!=BodyCurveStudy.ClosureEvidence.Degenerate)throw new Exception("Zero edge accepted");
    if(BodyCurveStudy.Inspect("square",square.Take(3).ToArray(),4).Closure!=BodyCurveStudy.ClosureEvidence.InvalidIdentity)throw new Exception("Missing edge accepted");
    var nan=square.ToArray();nan[1]=nan[1] with { Bx=double.NaN };if(BodyCurveStudy.Inspect("square",nan).Closure!=BodyCurveStudy.ClosureEvidence.Degenerate)throw new Exception("Non-finite edge accepted");
    var bindRef=new SyntheticFullSeamGraph.Ref("test_panel",0,false,false);
    var bindControl=new[]{new InterfaceMatcher.P(.5,1)};
    var bindEdge=new InterfaceMatcher.Edge("test_panel","CurveEdge",new(0,0),new(1,0),System.Collections.Immutable.ImmutableArray.Create(bindControl[0]),false,false,1);
    var bindPoints=(0d,0d,1d,0d);
    BodySeamStudy.ValidateBinding(bindEdge,bindRef,bindPoints,bindControl);
    void RejectBinding(InterfaceMatcher.Edge edge) { try { BodySeamStudy.ValidateBinding(edge,bindRef,bindPoints,bindControl);throw new Exception("Altered waist binding accepted"); } catch(ArgumentException) { } }
    RejectBinding(bindEdge with { Coefficient=1.05 });RejectBinding(bindEdge with { Flipped=true });RejectBinding(bindEdge with { RightWrong=true });
    RejectBinding(bindEdge with { Start=new(double.NaN,0) });RejectBinding(bindEdge with { End=new(1.01,0) });
    RejectBinding(bindEdge with { Controls=System.Collections.Immutable.ImmutableArray.Create(new InterfaceMatcher.P(.5,1.01)) });
    RejectBinding(bindEdge with { Panel="other" });RejectBinding(bindEdge with { Kind="Edge" });
    var graphEdges=new[]{new GeneratedGraph.Edge("s","a",0,"L",new(0,0),new(2,0)),new GeneratedGraph.Edge("s","a",1,"L",new(2,0),new(1,2)),new GeneratedGraph.Edge("s","a",2,"L",new(1,2),new(0,0))};
    var seam=new GeneratedGraph.Seam("s","a",0,false,"a",1,true);
    var ok=GeneratedGraph.Inspect("s",graphEdges,[seam],[seam]);
    if(ok.Status!="unresolved"||ok.ExportAllowed||ok.Issues.Count!=1) throw new Exception("Graph baseline");
    if(!GeneratedGraph.Inspect("s",graphEdges,[seam with { EdgeA=2 }],[seam]).Issues.Contains("seam-identity-or-direction-mismatch")) throw new Exception("Graph wrong edge");
    if(!GeneratedGraph.Inspect("s",graphEdges,[seam with { FlipA=true }],[seam]).Issues.Contains("seam-identity-or-direction-mismatch")) throw new Exception("Graph wrong direction");
    if(!GeneratedGraph.Inspect("other",graphEdges,[seam],[seam]).Issues.Contains("snapshot-mismatch")) throw new Exception("Graph snapshot");
    var openEdges=graphEdges.ToArray(); openEdges[2]=openEdges[2] with { End=new(0,1) };
    if(!GeneratedGraph.Inspect("s",openEdges,[seam],[seam]).Issues.Any(x=>x.StartsWith("open-contour:"))) throw new Exception("Graph open path");
    var bow=new[]{new GeneratedGraph.Edge("s","a",0,"L",new(0,0),new(2,2)),new GeneratedGraph.Edge("s","a",1,"L",new(2,2),new(0,2)),new GeneratedGraph.Edge("s","a",2,"L",new(0,2),new(2,0)),new GeneratedGraph.Edge("s","a",3,"L",new(2,0),new(0,0))};
    if(!GeneratedGraph.Inspect("s",bow,[seam],[seam]).Issues.Any(x=>x.StartsWith("self-intersection-or-degenerate:"))) throw new Exception("Graph crossing path");
    var openCurved=graphEdges.ToArray();openCurved[0]=openCurved[0] with { Kind="C",End=new(2,.1) };
    if(!GeneratedGraph.Inspect("s",openCurved,[seam],[seam]).Issues.Any(x=>x.StartsWith("open-contour:"))) throw new Exception("Open curved graph accepted");
    try { InterfaceMatcher.MatchDerived("synthetic-small-difference");throw new Exception("Rejected body entered derived waist"); } catch(ArgumentException) { }
    try { SyntheticArmholeCut.Create("other"); throw new Exception("Unknown armhole input accepted"); } catch(ArgumentException) { }
    var curved=graphEdges.ToArray(); curved[0]=curved[0] with { Kind="Q" };
    if(!GeneratedGraph.Inspect("s",curved,[seam],[seam]).Issues.Any(x=>x.StartsWith("curve-topology-unproven:"))) throw new Exception("Graph curve gate");
    var incomplete=baseParts.Segments.Select(e=>new GeneratedGraph.Edge("s",e.Panel,e.Edge,e.SourceKind=="Edge"?"L":"Q",new(e.Start.X,e.Start.Y),new(e.End.X,e.End.Y))).ToArray();
    if(!GeneratedGraph.Inspect("s",incomplete,[],[]).Issues.Contains("incomplete-graph")) throw new Exception("Partial output graph gate");
    // Quadratic x=t²: quarter arc length occurs at t=0.5, not t=0.25.
    var parabola=new InterfaceMatcher.Edge("test","CurveEdge",new(0,0),new(1,0),[new(0,0)],false,false,1);
    var (curveA,curveB)=parabola.SplitNative(.25);
    if(Math.Abs(curveA.End.X-.25)>1e-9 || Math.Abs(curveB.Controls[0].X-.5)>1e-9) throw new Exception("Curve arc-length split");
    try { parabola.SplitNative(0); throw new Exception("Zero split accepted"); } catch(ArgumentException) { }
    try { InterfaceMatcher.Match("test",[],[]); throw new Exception("Empty interface accepted"); } catch(ArgumentException) { }
    var line=new InterfaceMatcher.Edge("test","Edge",new(0,0),new(10,0),[],true,false,1);
    var matched=InterfaceMatcher.Match("test",[line],[line with { End=new(4,0), Flipped=false },line with { Start=new(4,0),Flipped=false }]);
    if(matched.Upper.Length!=2 || Math.Abs(matched.Upper[0].Start.X-6)>1e-8 || !matched.Upper.All(e=>e.Flipped)) throw new Exception("Flipped split orientation");
    var firstCase=data.RootElement.GetProperty("cases")[0];
    var firstRule=PartialDartRelations.Create("synthetic-base")[0];
    var sourceRule=firstCase.GetProperty("stitches").EnumerateArray().Single(r=>r.GetProperty("owner").GetString()==firstRule.Owner && r.GetProperty("index").GetInt32()==firstRule.SourceRule);
    void RejectDartRule(Action<JsonNode> mutate)
    {
        var node=JsonNode.Parse(sourceRule.GetRawText())!; mutate(node);
        using var doc=JsonDocument.Parse(node.ToJsonString());
        try { DartRuleCheck.Validate(firstRule,doc.RootElement,baseParts.Segments); throw new Exception("Invalid dart rule accepted"); } catch(InvalidDataException) { }
    }
    RejectDartRule(n=>n["a"]!["edges"]!.AsArray().Add(n["a"]!["edges"]![0]!.DeepClone()));
    RejectDartRule(n=>n["a"]!["edges"]![0]!["right_wrong"]=true);
    RejectDartRule(n=>n["a"]!["ruffle"]![0]!["coeff"]=1.1);
    // Shared waist endpoints and equal lengths do not satisfy designated apex roles.
    var falseApex=baseParts.Segments.ToArray();
    int ia=Array.FindIndex(falseApex,e=>e.Panel==firstRule.Owner && e.Edge==firstRule.EdgeA);
    int ib=Array.FindIndex(falseApex,e=>e.Panel==firstRule.Owner && e.Edge==firstRule.EdgeB);
    var ea=falseApex[ia]; var eb=falseApex[ib];
    double legLength=Math.Sqrt(Math.Pow(ea.End.X-ea.Start.X,2)+Math.Pow(ea.End.Y-ea.Start.Y,2));
    falseApex[ib]=eb with { Start=new(ea.Start.X,ea.Start.Y+legLength), End=ea.Start };
    double falseLength=Math.Sqrt(Math.Pow(falseApex[ib].End.X-falseApex[ib].Start.X,2)+Math.Pow(falseApex[ib].End.Y-falseApex[ib].Start.Y,2));
    if(Math.Abs(falseLength-legLength)>1e-9 || falseApex[ib].End!=ea.Start) throw new Exception("Invalid false-apex fixture");
    try { DartRuleCheck.Validate(firstRule,sourceRule,falseApex); throw new Exception("False apex accepted"); } catch(InvalidDataException) { }
    var unequalLegs=baseParts.Segments.ToArray(); int legIndex=Array.FindIndex(unequalLegs,e=>e.Panel==firstRule.Owner && e.Edge==firstRule.EdgeA);
    unequalLegs[legIndex]=unequalLegs[legIndex] with { Start=new(unequalLegs[legIndex].Start.X+.1,unequalLegs[legIndex].Start.Y) };
    try { DartRuleCheck.Validate(firstRule,sourceRule,unequalLegs); throw new Exception("Unequal dart legs accepted"); } catch(InvalidDataException) { }
    var separate=ExactBezierSeparation.Compare([new(0,0),new(1,1),new(2,0)],[new(0,3),new(1,4),new(2,3)],false);
    if(separate!=ExactBezierSeparation.Verdict.Separated) throw new Exception("Bezier disjoint certificate");
    var touch=ExactBezierSeparation.Compare([new(0,0),new(1,1),new(2,0)],[new(2,0),new(3,-1),new(4,0)],true);
    if(touch!=ExactBezierSeparation.Verdict.OnlySharedEndpoint) throw new Exception("Bezier endpoint certificate");
    var cornerA=new[]{new ExactBezierSeparation.Point(0,0),new ExactBezierSeparation.Point(2,0)};
    var cornerB=new[]{new ExactBezierSeparation.Point(2,0),new ExactBezierSeparation.Point(2,2)};
    if(ExactBezierSeparation.Compare(cornerA,cornerB,true)!=ExactBezierSeparation.Verdict.OnlySharedEndpoint)throw new Exception("Orthogonal endpoint proof");
    var angledA=new[]{new ExactBezierSeparation.Point(2,3),new ExactBezierSeparation.Point(0,0)};
    var angledB=new[]{new ExactBezierSeparation.Point(0,0),new ExactBezierSeparation.Point(3,2)};
    if(ExactBezierSeparation.Compare(angledA,angledB,true)!=ExactBezierSeparation.Verdict.OnlySharedEndpoint)throw new Exception("Angled dart endpoint proof");
    var oppositeA=new[]{new ExactBezierSeparation.Point(-2,-2),new ExactBezierSeparation.Point(0,0)};
    var oppositeB=new[]{new ExactBezierSeparation.Point(0,0),new ExactBezierSeparation.Point(2,2)};
    if(ExactBezierSeparation.Compare(oppositeA,oppositeB,true)!=ExactBezierSeparation.Verdict.OnlySharedEndpoint)throw new Exception("Opposite collinear endpoint proof");
    var overlapB=new[]{new ExactBezierSeparation.Point(0,0),new ExactBezierSeparation.Point(-1,-1)};
    if(ExactBezierSeparation.Compare(oppositeA,overlapB,true)==ExactBezierSeparation.Verdict.OnlySharedEndpoint)throw new Exception("Overlapping lines falsely cleared");
    var zeroLine=new[]{new ExactBezierSeparation.Point(0,0),new ExactBezierSeparation.Point(0,0)};
    if(ExactBezierSeparation.Compare(zeroLine,oppositeB,true)==ExactBezierSeparation.Verdict.OnlySharedEndpoint)throw new Exception("Degenerate line falsely cleared");
    var loopA=new[]{new ExactBezierSeparation.Point(0,0),new ExactBezierSeparation.Point(-1,0),new ExactBezierSeparation.Point(0,1)};
    var loopB=new[]{new ExactBezierSeparation.Point(0,1),new ExactBezierSeparation.Point(1,0),new ExactBezierSeparation.Point(0,0)};
    if(ExactBezierSeparation.Compare(loopA,loopB,true)==ExactBezierSeparation.Verdict.OnlySharedEndpoint)throw new Exception("Two shared endpoints falsely certified");
    var tangent=ExactBezierSeparation.Compare([new(0,1),new(1,-1),new(2,1)],[new(0,0),new(2,0)],false,8,1000);
    if(tangent!=ExactBezierSeparation.Verdict.Unresolved) throw new Exception("Bezier tangency must stay unresolved");
    var overlap=ExactBezierSeparation.Compare([new(0,0),new(2,0)],[new(1,0),new(3,0)],false,8,1000);
    if(overlap!=ExactBezierSeparation.Verdict.Unresolved) throw new Exception("Bezier overlap must stay unresolved");
    var near=ExactBezierSeparation.Compare([new(0,0),new(1,0)],[new(0,double.Epsilon),new(1,double.Epsilon)],false);
    if(near!=ExactBezierSeparation.Verdict.Separated) throw new Exception("Exact dyadic near separation");
    try { ExactBezierSeparation.Compare([new(double.NaN,0),new(1,0)],[new(0,0),new(1,0)],false); throw new Exception("Invalid control accepted"); } catch(ArgumentException) { }
    var cross=ExactBezierSeparation.Compare([new(0,0),new(2,2)],[new(0,2),new(2,0)],false,8,1000);
    if(cross!=ExactBezierSeparation.Verdict.Unresolved) throw new Exception("Bezier crossing must stay unresolved");
    if(!ExactBezierSeparation.HasMonotoneCoordinate([new(0,0),new(1,2),new(2,0)]) || ExactBezierSeparation.HasMonotoneCoordinate([new(0,0),new(2,2),new(-2,2),new(0,0)])) throw new Exception("Bezier monotonicity");
    // Both axes fail monotonicity: the final 2 -> 1 step must be checked.
    if(ExactBezierSeparation.HasMonotoneCoordinate([new(0,0),new(2,0),new(1,0)]))throw new Exception("Quadratic final reversal accepted");
    if(ExactBezierSeparation.HasMonotoneCoordinate([new(0,0),new(.25,0),new(2,0),new(1,0)]))throw new Exception("Cubic final reversal accepted");
    if(!ExactBezierSeparation.HasMonotoneCoordinate([new(3,0),new(2,0),new(2,0),new(1,0)]))throw new Exception("Descending monotone curve rejected");
    if(!ExactBezierSeparation.HasMonotoneCoordinate([new(0,0),new(2,1),new(1,2)]))throw new Exception("Independent monotone axis rejected");
    var reversalContour=new[]{new BodyCurveStudy.Edge("reversal",0,"Q",0,0,1,0,[(2,0)],null),new BodyCurveStudy.Edge("reversal",1,"L",1,0,1,1,[],null),new BodyCurveStudy.Edge("reversal",2,"L",1,1,0,1,[],null),new BodyCurveStudy.Edge("reversal",3,"L",0,1,0,0,[],null)};
    var reversalStudy=BodyCurveStudy.Inspect("reversal",reversalContour,4);
    if(reversalStudy.Closure!=BodyCurveStudy.ClosureEvidence.ClosedRepresented || reversalStudy.NonMonotone!=1 || reversalStudy.Status!="unresolved" || reversalStudy.ExportAllowed || reversalStudy.ProductionAllowed)throw new Exception("Reversing contour not reported");
    if(ExactBezierSeparation.Compare([new(0,0),new(1,1),new(2,0)],[new(0,1),new(1,0),new(2,1)],false,0,1)!=ExactBezierSeparation.Verdict.Unresolved) throw new Exception("Bezier budget fail closed");
    foreach(string id in new[]{"synthetic-base","synthetic-distinct"}) foreach(bool left in new[]{false,true})
    {
        var audit=FrontBezierAudit.Inspect(FrontBodiceCandidate.Create(id,left));
        if(audit.Separated+audit.EndpointOnly+audit.Unresolved+audit.ArcPairs!=66 || audit.ArcPairs==0 || audit.ArcSeparated+audit.ArcEndpointOnly+audit.ArcUnresolved!=audit.ArcPairs || audit.Status!="unresolved" || audit.ExportAllowed) throw new Exception("Front Bezier audit coverage");
        Console.WriteLine($"{id} {(left?"left":"right")} represented controls: separated={audit.Separated}, endpoint-only={audit.EndpointOnly}, unresolved={audit.Unresolved}, arc-pairs={audit.ArcPairs} (separate={audit.ArcSeparated}, endpoint={audit.ArcEndpointOnly}, unresolved={audit.ArcUnresolved}), nonmonotone={audit.NonMonotone}.");
    }
    var upper=UpperSeamGraph.Create("synthetic-base");var firstUpper=upper.Rules[0];
    var firstUpperSource=firstCase.GetProperty("stitches").EnumerateArray().Single(r=>r.GetProperty("owner").GetString()==firstUpper.Owner && r.GetProperty("owner_type").GetString()==firstUpper.OwnerType && r.GetProperty("index").GetInt32()==firstUpper.Index);
    var badUpper=JsonNode.Parse(firstUpperSource.GetRawText())!;badUpper["a"]!["edges"]![0]!["edge"]=8;
    using(var wrongUpper=JsonDocument.Parse(badUpper.ToJsonString())) { try { UpperSeamGraph.Compare(firstUpper,wrongUpper.RootElement);throw new Exception("Wrong valid upper seam accepted"); } catch(InvalidDataException) { } }
    var flippedUpper=JsonNode.Parse(firstUpperSource.GetRawText())!;flippedUpper["b"]!["edges"]![0]!["flipped"]=true;
    using(var wrongUpperFlip=JsonDocument.Parse(flippedUpper.ToJsonString())) { try { UpperSeamGraph.Compare(firstUpper,wrongUpperFlip.RootElement);throw new Exception("Wrong upper orientation accepted"); } catch(InvalidDataException) { } }
    var arcA=new CircleSeparation.Point(-1,0);var arcB=new CircleSeparation.Point(1,0);
    if(CircleSeparation.Compare(arcA,arcB,-.5,[new(-2,2),new(2,2)],false)!=CircleSeparation.Verdict.Separated)throw new Exception("Circle exterior separation");
    if(CircleSeparation.Compare(arcA,arcB,-.5,[new(0,0),new(.5,0)],false)!=CircleSeparation.Verdict.Separated)throw new Exception("Circle interior separation");
    if(CircleSeparation.Compare(arcA,arcB,-.5,[new(-1,0),new(-2,0)],true)!=CircleSeparation.Verdict.OnlySharedEndpoint)throw new Exception("Circle shared endpoint");
    if(CircleSeparation.Compare(arcA,arcB,-.5,[new(-2,-1),new(2,-1)],false)!=CircleSeparation.Verdict.Unresolved)throw new Exception("Circle tangent must remain unresolved");
    if(CircleSeparation.Compare(arcA,arcB,-.5,[new(-2,0),new(2,0)],false)!=CircleSeparation.Verdict.Unresolved)throw new Exception("Circle crossing must remain unresolved");
    try { CircleSeparation.Compare(arcA,arcB,0,[new(0,0),new(1,0)],false);throw new Exception("Degenerate circle accepted"); } catch(ArgumentException) { }
    var separateA=new ProjectedLengthBounds.Interval(1,2,1);var separateB=new ProjectedLengthBounds.Interval(3,4,1);var overlapInterval=new ProjectedLengthBounds.Interval(2,3,1);
    if(!separateA.Disjoint(separateB) || separateA.Disjoint(overlapInterval))throw new Exception("Seam interval separation");
    var projectionBase=BezierLengthBounds.Enclose([new(0,0),new(3,4)],0);
    var projectionUnit=ProjectedLengthBounds.Project(projectionBase,1);
    var projectionNonUnit=ProjectedLengthBounds.Project(projectionBase,1.25);
    if(!projectionUnit.Contains(5) || !projectionNonUnit.Contains(4) || !projectionUnit.Disjoint(projectionNonUnit))throw new Exception("Non-unit ruffle projection");
    if(!ProjectedLengthBounds.Project(projectionBase,1.0005).Contains(5))throw new Exception("Near-one source behavior");
    if(!ProjectedLengthBounds.Project(projectionBase,.5).Contains(10))throw new Exception("Sub-unit ruffle projection");
    try { ProjectedLengthBounds.Project(new BezierLengthBounds.Bounds(2,1,0),1);throw new Exception("Invalid bound accepted"); } catch(ArgumentException) { }
    try { ProjectedLengthBounds.Interval.Add(new(0,1,0),ProjectedLengthBounds.Interval.Zero);throw new Exception("Zero denominator accepted"); } catch(ArgumentException) { }
    try { ProjectedLengthBounds.Project(projectionBase,0);throw new Exception("Zero ruffle accepted"); } catch(ArgumentException) { }
    try { ProjectedLengthBounds.Project(projectionBase,double.NaN);throw new Exception("NaN ruffle accepted"); } catch(ArgumentException) { }
    var exactLine=BezierLengthBounds.Enclose([new(0,0),new(3,4)],4);
    if(!exactLine.Contains(5) || exactLine.Contains(5.0001) || exactLine.Lower!=exactLine.Upper) throw new Exception("Exact line length enclosure");
    var straightQuadratic=BezierLengthBounds.Enclose([new(0,0),new(0,0),new(1,0)],5);
    if(!straightQuadratic.Contains(1))throw new Exception("Quadratic length enclosure");
    var bentCoarse=BezierLengthBounds.Enclose([new(0,0),new(1,2),new(2,0)],3);
    var bentFine=BezierLengthBounds.Enclose([new(0,0),new(1,2),new(2,0)],8);
    if(bentFine.ApproximateWidthCm>=bentCoarse.ApproximateWidthCm || bentFine.ApproximateWidthCm<=0)throw new Exception("Bezier bounds refinement");
    try { BezierLengthBounds.Enclose([new(double.NaN,0),new(1,0)]);throw new Exception("Invalid length geometry accepted"); } catch(ArgumentException) { }
    // Exercise the complete 26-field local contract using synthetic values only.
    var bodyPairs=ResearchBody.Fields.Select(k=>new KeyValuePair<string,decimal>(k,SyntheticBodyCatalog.Create("synthetic-base")[k])).ToArray();
    var auditPairs=bodyPairs.Select(p=>new KeyValuePair<string,decimal?>(p.Key,p.Value)).ToArray();
    MeasurementContractAudit.Report Audit(IEnumerable<KeyValuePair<string,decimal?>> values,string? len="cm",string? angle="degree")=>MeasurementContractAudit.Assess(values,len,angle);
    void HasCode(MeasurementContractAudit.Report report,string code) { if(!report.Issues.Any(i=>i.Code==code && i.Severity==MeasurementContractAudit.Kind.Blocking))throw new Exception("Missing audit issue: "+code); }
    var auditBase=Audit(auditPairs);
    if(auditBase.HasBlockingIssues || auditBase.Issues.Count(i=>i.Severity==MeasurementContractAudit.Kind.Unverified)!=3 || auditBase.PersonalDataCollectionEnabled || auditBase.ProductionAllowed || auditBase.ExportAllowed || auditBase.Status!="unresolved")throw new Exception("Audit baseline gate");
    HasCode(Audit(auditPairs,"mm","degree"),"length_unit_not_cm");HasCode(Audit(auditPairs,"cm",null),"angle_unit_not_degree");
    HasCode(Audit(auditPairs.Where(p=>p.Key!="bust")),"missing_measurement");
    HasCode(Audit(auditPairs.Select(p=>p.Key=="bust"?new KeyValuePair<string,decimal?>(p.Key,null):p)),"missing_measurement");
    HasCode(Audit(auditPairs.Append(new("_waist_level",1))),"unknown_or_derived_field");HasCode(Audit(auditPairs.Append(auditPairs[0])),"duplicate_field");
    HasCode(Audit(auditPairs.Select(p=>p.Key=="waist"?new KeyValuePair<string,decimal?>(p.Key,0):p)),"outside_technical_range");
    HasCode(Audit(auditPairs.Select(p=>p.Key=="wrist"?new KeyValuePair<string,decimal?>(p.Key,0):p)),"outside_technical_range");
    void AuditScope(string key,decimal value,string code) {
        var changed=auditPairs.Select(p=>p.Key==key?new KeyValuePair<string,decimal?>(p.Key,value):p).ToArray();HasCode(Audit(changed),code);
        var concrete=changed.Where(p=>p.Value.HasValue).Select(p=>new KeyValuePair<string,decimal>(p.Key,p.Value!.Value));
        try { ResearchMeasurements.Create(concrete,"cm","degree").BuildPinnedGarment();throw new Exception("Scope conflict accepted"); } catch(ArgumentException) { }
    }
    AuditScope("back_width",100,"unsupported_back_bust_relation");AuditScope("waist_back_width",80,"unsupported_back_waist_relation");AuditScope("hip_back_width",104,"unsupported_back_hip_relation");
    AuditScope("shoulder_incl",45,"unsupported_shoulder_angle");AuditScope("hip_inclination",89,"unsupported_hip_angle");AuditScope("shoulder_w",2,"unsupported_sleeve_balance");AuditScope("height",86,"unsupported_derived_length");
    var extremeAudit=Audit(auditPairs.Select(p=>p.Key=="bust_points"?new KeyValuePair<string,decimal?>(p.Key,1000):p));
    if(extremeAudit.HasBlockingIssues || extremeAudit.Status!="unresolved" || extremeAudit.PersonalDataCollectionEnabled)throw new Exception("Technical limit misrepresented as fit approval");
    var partialKeys=new[]{"back_width","waist_back_width","waist_line","shoulder_w","shoulder_incl","bum_points","bust_line","vert_bust_line"};
    var partial=ResearchMeasurements.Create(bodyPairs.Where(p=>partialKeys.Contains(p.Key)),"cm","degree");
    var backNeeds=partial.Check(ResearchMeasurements.Operation.PrecutBackWaist);
    var frontNeeds=partial.Check(ResearchMeasurements.Operation.PrecutFrontBodice);
    if(!backNeeds.AllFieldsPresent || !backNeeds.CanAttemptPartialCalculation || frontNeeds.AllFieldsPresent || !frontNeeds.Missing.Contains("bust") || partial.Check(ResearchMeasurements.Operation.CompleteCurrentBody).AllFieldsPresent)throw new Exception("Partial presence contract");
    try { partial.BuildComplete();throw new Exception("Incomplete body accepted"); } catch(ArgumentException) { }
    var partialBack=BackWaistCandidate.CreateFromMeasurements(partial,ResearchDesign.PinnedFitted);
    if(partialBack.ReferenceAssessment!=BackWaistCandidate.ReferenceState.Unverified || partialBack.ExportAllowed || partialBack.ProductionAllowed)throw new Exception("Partial back gate");
    try { partialBack.RequireResearchComposition();throw new Exception("Partial back composed"); } catch(InvalidOperationException) { }
    foreach(string key in partialKeys) {
        var missing=ResearchMeasurements.Create(bodyPairs.Where(p=>partialKeys.Contains(p.Key) && p.Key!=key),"cm","degree");
        if(!missing.Check(ResearchMeasurements.Operation.PrecutBackWaist).Missing.Contains(key))throw new Exception("Missing back field not reported");
        try { BackWaistCandidate.CreateFromMeasurements(missing,ResearchDesign.PinnedFitted);throw new Exception("Missing back field accepted"); } catch(ArgumentException) { }
    }
    var frontKeys=new[]{"bust","back_width","waist","waist_back_width","bust_points","shoulder_w","shoulder_incl","waist_line","waist_over_bust_line","bust_line","vert_bust_line"};
    var frontPartial=ResearchMeasurements.Create(bodyPairs.Where(p=>frontKeys.Contains(p.Key)),"cm","degree");
    var partialFront=PrecutFrontBodice.CreateFromMeasurements(frontPartial,ResearchDesign.PinnedFitted);
    if(partialFront.ReferenceAssessment!=BackWaistCandidate.ReferenceState.Unverified || partialFront.ExportAllowed || partialFront.ProductionAllowed)throw new Exception("Partial front gate");
    try { partialFront.RequireResearchComposition();throw new Exception("Partial front composed"); } catch(InvalidOperationException) { }
    foreach(string key in frontKeys) {
        var missing=ResearchMeasurements.Create(bodyPairs.Where(p=>frontKeys.Contains(p.Key) && p.Key!=key),"cm","degree");
        if(!missing.Check(ResearchMeasurements.Operation.PrecutFrontBodice).Missing.Contains(key))throw new Exception("Missing front field not reported");
        try { PrecutFrontBodice.CreateFromMeasurements(missing,ResearchDesign.PinnedFitted);throw new Exception("Missing front field accepted"); } catch(ArgumentException) { }
    }
    var reorderPartial=ResearchMeasurements.Create(bodyPairs.Where(p=>partialKeys.Contains(p.Key)).Reverse(),"cm","degree");
    if(BackWaistCandidate.CreateFromMeasurements(reorderPartial,ResearchDesign.PinnedFitted).Snapshot!=partialBack.Snapshot)throw new Exception("Partial input order changed identity");
    var unsupportedPartial=ResearchMeasurements.Create(bodyPairs.Where(p=>partialKeys.Contains(p.Key)).Select(p=>p.Key=="shoulder_incl"?new KeyValuePair<string,decimal>(p.Key,45):p),"cm","degree");
    try { BackWaistCandidate.CreateFromMeasurements(unsupportedPartial,ResearchDesign.PinnedFitted);throw new Exception("Unsupported partial back accepted"); } catch(ArgumentException) { }

    string[] unusedGarment=["arm_length","arm_pose_angle","crotch_hip_diff","leg_circ","underbust","wrist"];
    var garmentPairs=bodyPairs.Where(p=>!unusedGarment.Contains(p.Key)).ToArray();
    var garmentInput=ResearchMeasurements.Create(garmentPairs,"cm","degree");
    var garmentRequirements=garmentInput.Check(ResearchMeasurements.Operation.PinnedGarment);
    if(garmentPairs.Length!=20 || !garmentRequirements.AllFieldsPresent || !garmentRequirements.CanAttemptPartialCalculation || garmentInput.Check(ResearchMeasurements.Operation.CompleteCurrentBody).AllFieldsPresent)throw new Exception("Partial garment requirements");
    var garmentBody=garmentInput.BuildPinnedGarment();
    foreach(var removed in garmentPairs) {
        var missing=ResearchMeasurements.Create(garmentPairs.Where(p=>p.Key!=removed.Key),"cm","degree");
        if(!missing.Check(ResearchMeasurements.Operation.PinnedGarment).Missing.Contains(removed.Key))throw new Exception("Missing garment field not reported");
        try { missing.BuildPinnedGarment();throw new Exception("Missing garment field accepted"); } catch(ArgumentException) { }
    }
    var unsupportedGarment=ResearchMeasurements.Create(garmentPairs.Select(p=>p.Key=="height"?new KeyValuePair<string,decimal>(p.Key,86):p),"cm","degree");
    try { unsupportedGarment.BuildPinnedGarment();throw new Exception("Unsupported partial garment accepted"); } catch(ArgumentException e) { if(!e.Message.Contains("Unsupported research scope"))throw; }
    var reorderedGarment=ResearchMeasurements.Create(garmentPairs.Reverse(),"cm","degree").BuildPinnedGarment();
    if(garmentBody.Snapshot(ResearchDesign.PinnedFitted)!=reorderedGarment.Snapshot(ResearchDesign.PinnedFitted))throw new Exception("Garment input order changed identity");
    var allGarment=ResearchMeasurements.Create(bodyPairs,"cm","degree").BuildPinnedGarment();
    if(garmentBody.Snapshot(ResearchDesign.PinnedFitted)!=allGarment.Snapshot(ResearchDesign.PinnedFitted))throw new Exception("Unused measurements changed partial identity");
    try { _=garmentBody["arm_length"];throw new Exception("Missing measurement was fabricated"); } catch(ArgumentException) { }
    try { PrecutFrontBodice.CreateFromBody(new ForgedPatternBody(),ResearchDesign.PinnedFitted);throw new Exception("Forged body accepted"); } catch(ArgumentException) { }
    try { BodySeamStudy.Calculate(new ForgedPatternBody(),ResearchDesign.PinnedFitted);throw new Exception("Forged graph body accepted"); } catch(ArgumentException) { }
    var complete=ResearchMeasurements.Create(bodyPairs,"cm","degree");
    if(!complete.Check(ResearchMeasurements.Operation.CompleteCurrentBody).AllFieldsPresent || complete.BuildComplete().Snapshot(ResearchDesign.PinnedFitted)!=SyntheticBodyCatalog.Create("synthetic-base").Snapshot(ResearchDesign.PinnedFitted))throw new Exception("Complete presence contract");
    try { ResearchMeasurements.Create(bodyPairs.Append(new("_leg_length",1)),"cm","degree");throw new Exception("Derived input accepted"); } catch(ArgumentException) { }
    try { ResearchMeasurements.Create(bodyPairs.Append(bodyPairs[0]),"cm","degree");throw new Exception("Duplicate partial input accepted"); } catch(ArgumentException) { }
    try { ResearchMeasurements.Create(bodyPairs,"mm","degree");throw new Exception("Wrong partial units accepted"); } catch(ArgumentException) { }
    try { partial.Check((ResearchMeasurements.Operation)999);throw new Exception("Unknown operation accepted"); } catch(ArgumentException) { }
    var mutablePairs=bodyPairs.ToList();var copied=ResearchMeasurements.Create(mutablePairs,"cm","degree");mutablePairs.Clear();
    if(!copied.Check(ResearchMeasurements.Operation.CompleteCurrentBody).AllFieldsPresent)throw new Exception("Partial measurements not immutable");
    var extreme=bodyPairs.Select(p=>p.Key is "bust_points" or "wrist" or "underbust" or "arm_length" or "bust_line"?new KeyValuePair<string,decimal>(p.Key,1000):p).ToArray();
    var numericOnly=ResearchBody.Create(extreme,"cm","degree");
    if(numericOnly["wrist"]!=1000)throw new Exception("Technical limit not represented");
    try { ResearchBody.Create(bodyPairs.Select(p=>p.Key=="height"?new KeyValuePair<string,decimal>(p.Key,86):p),"cm","degree");throw new Exception("Unsupported model scope accepted"); }
    catch(ArgumentException e) { if(!e.Message.Contains("Unsupported research scope"))throw; }
    void RejectBody(IEnumerable<KeyValuePair<string,decimal>> values,string length="cm",string angle="degree") { try { ResearchBody.Create(values,length,angle);throw new Exception("Invalid body accepted"); } catch(ArgumentException) { } }
    RejectBody(bodyPairs.Skip(1)); RejectBody(bodyPairs.Append(bodyPairs[0])); RejectBody(bodyPairs.Append(new("_waist_level",1))); RejectBody(bodyPairs,"mm","degree");
    RejectBody(bodyPairs.Select(p=>p.Key=="waist"?new KeyValuePair<string,decimal>(p.Key,0):p));
    RejectBody(bodyPairs.Select(p=>p.Key=="back_width"?new KeyValuePair<string,decimal>(p.Key,100):p));
    RejectBody(bodyPairs.Select(p=>p.Key=="shoulder_incl"?new KeyValuePair<string,decimal>(p.Key,45):p));
    foreach(decimal shoulder in new[]{1m,2m,2.0000000000000000000000000001m})
        RejectBody(bodyPairs.Select(p=>p.Key=="shoulder_w"?new KeyValuePair<string,decimal>(p.Key,shoulder):p));
    RejectBody(bodyPairs.Select(p=>p.Key=="height"?new KeyValuePair<string,decimal>(p.Key,86):p));
    RejectBody(bodyPairs.Select(p=>p.Key=="armscye_depth"?new KeyValuePair<string,decimal>(p.Key,0):p));
    void CheckArmGuard(string expected,Action<Dictionary<string,decimal>> modify)
    {
        var input=bodyPairs.ToDictionary(p=>p.Key,p=>p.Value);modify(input);
        var body=ResearchBody.Create(input,"cm","degree");
        _=PrecutFrontBodice.CreateFromBody(body,ResearchDesign.PinnedFitted);
        try { _=SyntheticArmholeCut.CreateFromBody(body,ResearchDesign.PinnedFitted);throw new Exception("Armhole guard failed: "+expected); }
        catch(ArgumentException e) { NeedLocal(e.Message==expected,"wrong armhole guard: "+e.Message); }
    }
    CheckArmGuard("Unsupported armhole depth",p=>{p["shoulder_w"]=52;p["shoulder_incl"]=0;p["bust_line"]=.5m;p["vert_bust_line"]=.5m;});
    CheckArmGuard("Armhole leaves source corner",p=>{p["armscye_depth"]=91;p["back_width"]=25;p["bust_points"]=5;p["shoulder_incl"]=15;p["shoulder_w"]=8;p["waist_line"]=39;p["waist_over_bust_line"]=54;});
    try { SyntheticArmholeCut.SolveCorner(new(0,0),new(1,1),new(2,2),1,1);throw new Exception("Singular corner accepted"); }
    catch(ArgumentException e) { NeedLocal(e.Message=="Degenerate corner","wrong determinant guard"); }
    var invalidNeck=ResearchBody.Create(bodyPairs.Select(p=>p.Key=="neck_w"?new KeyValuePair<string,decimal>(p.Key,40):p),"cm","degree");
    try { FrontNeckCut.CreateFromBody(invalidNeck,ResearchDesign.PinnedFitted);throw new Exception("Invalid neck accepted"); } catch(ArgumentException) { }
    try { FrontBodiceCandidate.CreateFromBody(invalidNeck,ResearchDesign.PinnedFitted);throw new Exception("Invalid front panel accepted"); } catch(ArgumentException) { }
    try { BackBodiceCandidate.CreateFromBody(invalidNeck,ResearchDesign.PinnedFitted,false);throw new Exception("Invalid back panel accepted"); } catch(ArgumentException) { }
    try { FrontBodiceCandidate.CreateLeftFromBody(invalidNeck,ResearchDesign.PinnedFitted);throw new Exception("Invalid left front accepted"); } catch(ArgumentException) { }
    var invalidFront=ResearchBody.Create(bodyPairs.Select(p=>p.Key=="bust_points"?new KeyValuePair<string,decimal>(p.Key,100):p),"cm","degree");
    try { PrecutFrontBodice.CreateFromBody(invalidFront,ResearchDesign.PinnedFitted);throw new Exception("Invalid front cut accepted"); } catch(ArgumentException) { }
    var unsupportedCut=ResearchBody.Create(bodyPairs.Select(p=>p.Key=="bum_points"?new KeyValuePair<string,decimal>(p.Key,100):p),"cm","degree");
    try { BackWaistCandidate.CreateFromBody(unsupportedCut,ResearchDesign.PinnedFitted);throw new Exception("Out-of-range cut accepted"); } catch(ArgumentException) { }
    var changedBody=ResearchBody.Create(bodyPairs.Select(p=>p.Key=="waist"?new KeyValuePair<string,decimal>(p.Key,81):p),"cm","degree");
    NeedLocal(changedBody.Snapshot(ResearchDesign.PinnedFitted)!=SyntheticBodyCatalog.Create("synthetic-base").Snapshot(ResearchDesign.PinnedFitted),"changed body snapshot");
    foreach(string id in new[]{"synthetic-base","synthetic-distinct"}) {
        var skirt=FrontSkirtCandidate.CreateFromBody(SyntheticBodyCatalog.Create(id),ResearchDesign.PinnedFitted);
        var fixture=FrontSkirtCandidate.Create(id);
        NeedLocal(skirt.ReferenceAssessment==BackWaistCandidate.ReferenceState.Unverified && !skirt.ExportAllowed && !skirt.ProductionAllowed && !typeof(FrontSkirtCandidate.Candidate).IsAssignableFrom(skirt.GetType()),"typed skirt gate");
        NeedLocal(skirt.Edges.Count==fixture.Edges.Count && skirt.Edges.Zip(fixture.Edges).All(p=>p.First.Id==p.Second.Id && p.First.Kind==p.Second.Kind && Math.Abs(p.First.Start.X-p.Second.Start.X)<1e-9 && Math.Abs(p.First.Start.Y-p.Second.Start.Y)<1e-9 && Math.Abs(p.First.End.X-p.Second.End.X)<1e-9 && Math.Abs(p.First.End.Y-p.Second.End.Y)<1e-9),"typed skirt differs from synthetic candidate");
        try { skirt.RequireResearchComposition();throw new Exception("Unverified skirt composed"); } catch(InvalidOperationException) { }
    }
    var skirtBase=FrontSkirtCandidate.CreateFromBody(SyntheticBodyCatalog.Create("synthetic-base"),ResearchDesign.PinnedFitted);
    var changedBodiceBody=ResearchBody.Create(bodyPairs.Select(p=>p.Key=="waist_line"?new KeyValuePair<string,decimal>(p.Key,38):p),"cm","degree");
    var backPanelBase=BackBodiceCandidate.CreateFromBody(SyntheticBodyCatalog.Create("synthetic-base"),ResearchDesign.PinnedFitted,false);
    var backPanelChanged=BackBodiceCandidate.CreateFromBody(changedBodiceBody,ResearchDesign.PinnedFitted,false);
    NeedLocal(backPanelChanged.Snapshot!=backPanelBase.Snapshot && !backPanelChanged.Edges.SequenceEqual(backPanelBase.Edges),"back panel must depend on body values");
    var changedBackBody=ResearchBody.Create(bodyPairs.Select(p=>p.Key=="hips_line"?new KeyValuePair<string,decimal>(p.Key,24):p),"cm","degree");
    var backBase=BackSkirtCandidate.CreateFromBody(SyntheticBodyCatalog.Create("synthetic-base"),ResearchDesign.PinnedFitted);
    var backChanged=BackSkirtCandidate.CreateFromBody(changedBackBody,ResearchDesign.PinnedFitted);
    NeedLocal(backChanged.Snapshot!=backBase.Snapshot && !backChanged.Edges.SequenceEqual(backBase.Edges),"back skirt must depend on body values");
    var skirtChangedBody=ResearchBody.Create(bodyPairs.Select(p=>p.Key=="hips"?new KeyValuePair<string,decimal>(p.Key,105):p),"cm","degree");
    var skirtChanged=FrontSkirtCandidate.CreateFromBody(skirtChangedBody,ResearchDesign.PinnedFitted);
    var waistBaseline=BodyWaistStudy.Calculate(SyntheticBodyCatalog.Create("synthetic-base"),ResearchDesign.PinnedFitted);
    var waistChanged=BodyWaistStudy.Calculate(skirtChangedBody,ResearchDesign.PinnedFitted);
    NeedLocal(waistChanged.Snapshot!=waistBaseline.Snapshot && !waistChanged.Lower.SequenceEqual(waistBaseline.Lower),"waist match must depend on body values");
    NeedLocal(skirtBase.Snapshot!=skirtChanged.Snapshot && !skirtBase.Edges.SequenceEqual(skirtChanged.Edges),"skirt must depend on body values");
    var preview=ResearchViewSummary.Calculate(SyntheticBodyCatalog.Create("synthetic-base"),ResearchDesign.PinnedFitted);
    NeedLocal(preview.Panels.Count==6 && preview.RuleCount==23 && preview.Panels.All(p=>p.Closure==BodyCurveStudy.ClosureEvidence.ClosedRepresented) && preview.Panels.All(p=>p.UnresolvedPairs>=0) && preview.Status=="unresolved" && preview.ReferenceAssessment=="Unverified" && !preview.ExportAllowed && !preview.ProductionAllowed,"research preview gate");
    var invalidWaist=ResearchBody.Create(bodyPairs.Select(p=>p.Key=="bum_points"?new KeyValuePair<string,decimal>(p.Key,100):p),"cm","degree");
    try { BodyWaistStudy.Calculate(invalidWaist,ResearchDesign.PinnedFitted);throw new Exception("Invalid waist accepted"); } catch(ArgumentException) { }
    try { BodySeamStudy.Calculate(invalidWaist,ResearchDesign.PinnedFitted);throw new Exception("Invalid typed graph accepted"); } catch(ArgumentException) { }
    try { BodySeamProjection.Calculate(invalidWaist,ResearchDesign.PinnedFitted);throw new Exception("Invalid seam lengths accepted"); } catch(ArgumentException) { }
    try { ResearchViewSummary.Calculate(invalidWaist,ResearchDesign.PinnedFitted);throw new Exception("Invalid preview accepted"); } catch(ArgumentException) { }
    try { BackSkirtCandidate.CreateFromBody(invalidWaist,ResearchDesign.PinnedFitted);throw new Exception("Invalid back skirt accepted"); } catch(ArgumentException) { }
    var invalidSkirt=ResearchBody.Create(bodyPairs.Select(p=>p.Key=="bust_points"?new KeyValuePair<string,decimal>(p.Key,100):p),"cm","degree");
    try { FrontSkirtCandidate.CreateFromBody(invalidSkirt,ResearchDesign.PinnedFitted);throw new Exception("Invalid skirt accepted"); } catch(ArgumentException) { }
    var frontBase=PrecutFrontBodice.CreateFromBody(SyntheticBodyCatalog.Create("synthetic-base"),ResearchDesign.PinnedFitted);
    var frontChanged=PrecutFrontBodice.CreateFromBody(changedBody,ResearchDesign.PinnedFitted);
    var changedNeckBody=ResearchBody.Create(bodyPairs.Select(p=>p.Key=="neck_w"?new KeyValuePair<string,decimal>(p.Key,20):p),"cm","degree");
    var changedArmBody=ResearchBody.Create(bodyPairs.Select(p=>p.Key=="armscye_depth"?new KeyValuePair<string,decimal>(p.Key,14):p),"cm","degree");
    var armBase=SyntheticArmholeCut.CreateFromBody(SyntheticBodyCatalog.Create("synthetic-base"),ResearchDesign.PinnedFitted);
    var armChanged=SyntheticArmholeCut.CreateFromBody(changedArmBody,ResearchDesign.PinnedFitted);
    NeedLocal(!armBase.Curves.SequenceEqual(armChanged.Curves),"armhole must depend on body values");
    var neckBase=FrontNeckCut.CreateFromBody(SyntheticBodyCatalog.Create("synthetic-base"),ResearchDesign.PinnedFitted);
    var neckChanged=FrontNeckCut.CreateFromBody(changedNeckBody,ResearchDesign.PinnedFitted);
    NeedLocal(neckBase.Right!=neckChanged.Right && neckBase.Snapshot!=neckChanged.Snapshot,"neck must depend on body values");
    NeedLocal(frontChanged.Snapshot!=frontBase.Snapshot && !frontChanged.Vertices.SequenceEqual(frontBase.Vertices),"front must depend on body values");
    void NeedLocal(bool condition,string error) { if(!condition)throw new Exception(error); }
    Console.WriteLine($"PASS: {result.cases} synthetic cases; {result.panels} panels, {result.edges} edges, {result.rules} nested stitch rules; 68 reconstructed lines, 8 curve endpoint pairs and synthetic waist interface matching; production unresolved.");
    return 0;
}
catch(Exception e) { Console.Error.WriteLine("FAIL: "+e.Message); return 1; }

internal static class Validator
{
    // Compare the mathematical JSON number before any decimal or binary64 rounding.
    // Expected values for this fixed fixture are integers; equivalent exponent
    // and trailing-zero representations remain acceptable.
    internal static void ValidateTraceRuffles(JsonElement trace)
    {
        Need(trace.ValueKind==JsonValueKind.Object && trace.TryGetProperty("edge_count",out var countElement) && countElement.TryGetInt32(out var edgeCount) && edgeCount>0,"trace edge count");
        int expected=trace.GetProperty("edge_count").GetInt32();
        Need(trace.TryGetProperty("ruffle",out var segments) && segments.ValueKind==JsonValueKind.Array && segments.GetArrayLength()>0,"trace ruffle sections missing");
        int covered=0;
        foreach(var segment in segments.EnumerateArray()) {
            Need(segment.ValueKind==JsonValueKind.Object && segment.TryGetProperty("coefficient",out var c) && c.TryGetDouble(out var f) && double.IsFinite(f) && f>0,"trace ruffle coefficient");
            Need(segment.TryGetProperty("section",out var interval) && interval.ValueKind==JsonValueKind.Array && interval.GetArrayLength()==2,"trace ruffle section");
            Need(interval[0].TryGetInt32(out var start) && interval[1].TryGetInt32(out var end) && start==covered && end>start && end<=expected,"trace ruffle coverage");
            covered=interval[1].GetInt32();
        }
        Need(covered==expected,"trace ruffle incomplete");
    }
    internal static void CheckWaistConstraints()
    {
        var outerTest=new BackWaistConstraintStudy.Segment(0,0,10,0);var dartTest=new BackWaistConstraintStudy.Segment(0,0,1,0);
        Need(BackWaistRotationStudy.Assess(outerTest,dartTest,dartTest,Math.BitDecrement(8)).ImpossibleUnderConstraints && !BackWaistRotationStudy.Assess(outerTest,dartTest,dartTest,8).ImpossibleUnderConstraints,"rotation bound threshold");
        try { BackWaistRotationStudy.Assess(outerTest,new(0,0,0,0),dartTest,8);throw new InvalidDataException("Degenerate rotated dart accepted"); } catch(ArgumentException) { }
        try { BackWaistRotationStudy.Assess(outerTest,dartTest,dartTest,0);throw new InvalidDataException("Zero rotation target accepted"); } catch(ArgumentException) { }
        try { BackWaistRotationStudy.Assess(outerTest,dartTest,dartTest,double.NaN);throw new InvalidDataException("Non-finite rotation target accepted"); } catch(ArgumentException) { }
        var paths=new[]{new BackWaistConstraintStudy.Segment(0,0,1,0),new BackWaistConstraintStudy.Segment(10,10,11,10),new BackWaistConstraintStudy.Segment(0,-5,1,-5)};
        var exact=BackWaistConstraintStudy.AssessVectors(paths,3);
        var below=BackWaistConstraintStudy.AssessVectors(paths,Math.BitDecrement(3));
        var above=BackWaistConstraintStudy.AssessVectors(paths,Math.BitIncrement(3));
        Need(exact.Assessment==BackWaistConstraintStudy.Finding.Unresolved && above.Assessment==BackWaistConstraintStudy.Finding.Unresolved && below.Assessment==BackWaistConstraintStudy.Finding.ImpossibleUnderConstraints,"exact vector threshold");
        try { BackWaistConstraintStudy.RefuseCorrection(exact);throw new InvalidDataException("Unresolved correction returned"); }
        catch(InvalidOperationException ex) { Need(ex.Message.Contains("No certified correction"),"wrong unresolved correction error"); }
        try { BackWaistConstraintStudy.RefuseCorrection(below);throw new InvalidDataException("Impossible correction returned"); }
        catch(InvalidOperationException ex) { Need(ex.Message.Contains("Target impossible"),"wrong impossible correction error"); }
        var shifted=new[]{new BackWaistConstraintStudy.Segment(20,30,21,30),new BackWaistConstraintStudy.Segment(-10,8,-9,8),new BackWaistConstraintStudy.Segment(5,6,6,6)};
        Need(BackWaistConstraintStudy.AssessVectors(shifted,Math.BitDecrement(3)).Assessment==below.Assessment,"translated displacement changed bound");
        try { BackWaistConstraintStudy.AssessVectors(paths[..2],3);throw new InvalidDataException("Incomplete constraint accepted"); } catch(ArgumentException) { }
        var invalid=paths.ToArray();invalid[0]=invalid[0] with { Ax=double.NaN };
        try { BackWaistConstraintStudy.AssessVectors(invalid,3);throw new InvalidDataException("Invalid constraint accepted"); } catch(ArgumentException) { }
        using var stream=typeof(Validator).Assembly.GetManifestResourceStream("FittedCheck.back-waist-stages.json") ?? throw new InvalidDataException("Missing back stages");
        using var reader=new StreamReader(stream,Encoding.UTF8,true);string text=reader.ReadToEnd().Replace("\r\n","\n").Trim().TrimStart('\uFEFF');
        Need(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))=="6A8386924160425D49E48A88117419C3ECBD3CDA154413CD8EA2323579B1A856","constraint source hash");
        using var doc=JsonDocument.Parse(text);var cases=doc.RootElement.GetProperty("cases").EnumerateArray().ToArray();Need(cases.Length==5,"constraint source cases");
        foreach(var item in cases) {
            string id=item.GetProperty("id").GetString()!;var edges=item.GetProperty("edges");
            Need(edges.GetArrayLength()==11,"constraint source topology size");
            for(int n=0;n<11;n++)Need(Point(edges[n].GetProperty("end"))==Point(edges[(n+1)%11].GetProperty("start")),"constraint source continuity");
            foreach(int n in new[]{0,3,6})Need(edges[n].GetProperty("kind").GetString()=="CurveEdge","constraint source waist kind");
            foreach(int n in new[]{1,2,4,5})Need(edges[n].GetProperty("kind").GetString()=="Edge","constraint source dart kind");
            var source=new[]{0,3,6}.Select(n=>{ var a=Point(edges[n].GetProperty("start"));var b=Point(edges[n].GetProperty("end"));return new BackWaistConstraintStudy.Segment(a.X,a.Y,b.X,b.Y); }).ToArray();
            double target=item.GetProperty("body_input_cm_degree").GetProperty("waist_back_width").GetDouble()/2;
            var sourceProof=BackWaistConstraintStudy.AssessVectors(source,target);
            BackWaistConstraintStudy.Segment Segment(int a,bool aEnd,int b,bool bEnd) {
                var x=Point(edges[a].GetProperty(aEnd?"end":"start"));var y=Point(edges[b].GetProperty(bEnd?"end":"start"));return new(x.X,x.Y,y.X,y.Y);
            }
            var rotationSource=BackWaistRotationStudy.Assess(Segment(0,false,6,true),Segment(1,false,2,true),Segment(4,false,5,true),target);
            Need(rotationSource.ImpossibleUnderConstraints && !rotationSource.ExportAllowed && !rotationSource.ProductionAllowed,"source rotating-dart bound");
            var body=SyntheticBodyCatalog.Create(id);var candidate=BackWaistConstraintStudy.Inspect(body,ResearchDesign.PinnedFitted);
            var beforeTopology=BackBezierAudit.Inspect(BackWaistCandidate.Create(id));
            Need(beforeTopology.Separated==44 && beforeTopology.EndpointOnly==11 && beforeTopology.Unresolved==0 && beforeTopology.NonMonotone==0 && !beforeTopology.ExportAllowed,"constraint candidate baseline topology");
            var sourceInput=item.GetProperty("body_input_cm_degree");var partialFields=ResearchMeasurements.FieldsFor(ResearchMeasurements.Operation.PinnedGarment);
            var partial=ResearchMeasurements.Create(partialFields.Select(k=>new KeyValuePair<string,decimal>(k,sourceInput.GetProperty(k).GetDecimal())),"cm","degree").BuildPinnedGarment();
            var partialProof=BackWaistConstraintStudy.Inspect(partial,ResearchDesign.PinnedFitted);
            var represented=BackWaistCandidate.CreateFromBody(partial,ResearchDesign.PinnedFitted).Edges;
            BackWaistConstraintStudy.Segment Rep(int a,bool aEnd,int b,bool bEnd) { var x=aEnd?represented[a].End:represented[a].Start;var y=bEnd?represented[b].End:represented[b].Start;return new(x.X,x.Y,y.X,y.Y); }
            var rotationCandidate=BackWaistRotationStudy.Assess(Rep(0,false,6,true),Rep(1,false,2,true),Rep(4,false,5,true),target);
            Need(rotationCandidate.ImpossibleUnderConstraints,"candidate rotating-dart bound");
            Console.WriteLine($"{id} fixed ends, rigid rotated darts: source lower≈{rotationSource.ApproximateLowerBoundCm:G10} cm, candidate lower≈{rotationCandidate.ApproximateLowerBoundCm:G10} cm, target={target:G10} cm; impossible under constraints.");
            Need(partialProof.Assessment==candidate.Assessment && partialProof.TargetCm==candidate.TargetCm && partialProof.ApproximateVectorLowerBoundCm==candidate.ApproximateVectorLowerBoundCm,"partial constraint differs from full body");
            Need(sourceProof.Assessment==BackWaistConstraintStudy.Finding.ImpossibleUnderConstraints && candidate.Assessment==BackWaistConstraintStudy.Finding.ImpossibleUnderConstraints,"waist constraint result: "+id);
            Need(candidate.ReferenceAssessment==BackWaistCandidate.ReferenceState.Unverified && !candidate.ProductionAllowed && !candidate.ExportAllowed && candidate.Status=="unresolved","constraint output gate");
            try { BackWaistConstraintStudy.AttemptCorrection(body,ResearchDesign.PinnedFitted);throw new InvalidDataException("Infeasible waist correction returned"); } catch(InvalidOperationException) { }
            try { candidate.RequireResearchComposition();throw new InvalidDataException("Constraint result composed"); } catch(InvalidOperationException) { }
            var current=candidate.CurrentLength ?? throw new InvalidDataException("Missing candidate length bound");
            double sourceBefore=new[]{0,3,6}.Sum(n=>edges[n].GetProperty("length_cm").GetDouble());
            Console.WriteLine($"{id} fixed ends/translated darts: source before={sourceBefore:G10} cm, candidate before≈[{current.ApproximateLowerCm:G10},{current.ApproximateUpperCm:G10}] cm, source vector lower≈{sourceProof.ApproximateVectorLowerBoundCm:G10} cm, candidate vector lower≈{candidate.ApproximateVectorLowerBoundCm:G10} cm, target={target:G10} cm; no corrected candidate.");
        }
    }
    internal static void CheckSideExperiment()
    {
        foreach(string id in new[]{"synthetic-base","synthetic-distinct","synthetic-third","synthetic-fifth"}) {
            var experiment=BackWaistSideExperiment.Create(id);
            Need(experiment.ShiftCm>0 && Math.Abs(experiment.After.ApproximateLowerCm-experiment.TargetCm)<.000011 && Math.Abs(experiment.After.ApproximateUpperCm-experiment.TargetCm)<.000011,"side experiment waist bound");
            var original=BackWaistCandidate.CreateFromBody(SyntheticBodyCatalog.Create(id),ResearchDesign.PinnedFitted).Edges;
            Need(new[]{1,2,4,5}.All(i=>experiment.Edges[i]==original[i]) && experiment.Edges[0].Start==original[0].Start && experiment.Edges[7].End==original[7].End && experiment.Edges[6].End.X>original[6].End.X,"side experiment dart/landmark invariants");
            var proposedEdge=experiment.Edges[6];var topologyEdge=experiment.TopologyWaistEdge;
            BackWaistSideExperiment.ValidateTopologyWaistEdge(proposedEdge,topologyEdge);
            var topologyControls=BodyCurveStudy.BezierControls(topologyEdge) ?? throw new InvalidDataException("Missing topology quadratic controls");
            var oldTopology=BackWaistSideExperiment.ToTopologyWaistEdge(original[6]);
            var oldControls=BodyCurveStudy.BezierControls(oldTopology) ?? throw new InvalidDataException("Missing original quadratic controls");
            var relative=proposedEdge.RelativeControl ?? throw new InvalidDataException("Missing proposed relative control");
            double dx=proposedEdge.End.X-proposedEdge.Start.X,dy=proposedEdge.End.Y-proposedEdge.Start.Y;
            var independentlyComputed=new P(proposedEdge.Start.X+dx*relative.X-dy*relative.Y,proposedEdge.Start.Y+dy*relative.X+dx*relative.Y);
            Need(topologyControls.Length==3 && topologyControls[1].X==independentlyComputed.X && topologyControls[1].Y==independentlyComputed.Y,"topology absolute control differs from proposed");
            Need(topologyControls[1]!=oldControls[1],"side shift did not change absolute control");
            // Copying the old *relative* control with the new endpoint is the
            // previous representation and must reconstruct the same curve.
            var priorRelativeRepresentation=topologyEdge with { RelativeControls=oldTopology.RelativeControls };
            BackWaistSideExperiment.ValidateTopologyWaistEdge(proposedEdge,priorRelativeRepresentation);
            var priorControls=BodyCurveStudy.BezierControls(priorRelativeRepresentation)!;
            Need(priorControls.SequenceEqual(topologyControls),"relative-control reconstruction changed");
            var stale=topologyEdge with { RelativeControls=Array.AsReadOnly(new[]{(oldControls[1].X,oldControls[1].Y)}) };
            try { BackWaistSideExperiment.ValidateTopologyWaistEdge(proposedEdge,stale);throw new InvalidDataException("Stale absolute control accepted as relative"); } catch(ArgumentException) { }
            var changedTopology=topologyEdge with { RelativeControls=Array.AsReadOnly(new[]{(relative.X+.1,relative.Y)}) };
            try { BackWaistSideExperiment.ValidateTopologyWaistEdge(proposedEdge,changedTopology);throw new InvalidDataException("Changed topology control accepted"); } catch(ArgumentException) { }
            var changedCandidate=proposedEdge with { RelativeControl=new(relative.X+.1,relative.Y) };
            try { BackWaistSideExperiment.ValidateTopologyWaistEdge(changedCandidate,topologyEdge);throw new InvalidDataException("Changed candidate control accepted"); } catch(ArgumentException) { }

            Need(experiment.BeforeTopology.Closure==BodyCurveStudy.ClosureEvidence.ClosedRepresented && experiment.BeforeTopology.Unresolved==0 && experiment.BeforeTopology.NonMonotone==0,"side experiment baseline topology");
            Need(experiment.Topology.Closure==BodyCurveStudy.ClosureEvidence.ClosedRepresented && experiment.Topology.Unresolved==0 && experiment.Topology.NonMonotone==0,"side experiment topology");
            Need(experiment.ReferenceAssessment==BackWaistCandidate.ReferenceState.Unverified && experiment.Status=="unresolved" && !experiment.ExportAllowed && !experiment.ProductionAllowed,"side experiment gate");
            try { experiment.RequireResearchComposition();throw new InvalidDataException("Side experiment composed"); } catch(InvalidOperationException) { }
            Console.WriteLine($"{id} side experiment: half-waist before≈[{experiment.Before.ApproximateLowerCm:G10},{experiment.Before.ApproximateUpperCm:G10}] cm, after≈[{experiment.After.ApproximateLowerCm:G10},{experiment.After.ApproximateUpperCm:G10}] cm, target={experiment.TargetCm:G10} cm, side shift={experiment.ShiftCm:G8} cm, front side={experiment.FrontSideCm:G10} cm, back side before={experiment.BackSideBeforeCm:G10} cm, after={experiment.BackSideAfterCm:G10} cm, topology pairs before/after={experiment.BeforeTopology.Pairs}/{experiment.Topology.Pairs}, unresolved={experiment.BeforeTopology.Unresolved}/{experiment.Topology.Unresolved}; research only.");
        }
        foreach(string? id in new string?[]{"synthetic-small-difference","unknown",null})
            try { BackWaistSideExperiment.Create(id!);throw new InvalidDataException("Unsupported side experiment accepted"); } catch(ArgumentException) { }
    }
    internal static void CheckSeamCausality()
    {
        using var stream=typeof(Validator).Assembly.GetManifestResourceStream("FittedCheck.seam-causality.json") ?? throw new InvalidDataException("Missing seam trace");
        using var reader=new StreamReader(stream,Encoding.UTF8,true);string text=reader.ReadToEnd().Replace("\r\n","\n").Trim().TrimStart('\uFEFF');
        Need(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))=="D5EC8B4AF4CD1DC3E3EEEC0F6DC10196EB2C83405B6CA52DBE5D474DC4BA3C35","seam trace hash");
        using var doc=JsonDocument.Parse(text);var root=doc.RootElement;
        Need(root.GetProperty("source_commit").GetString()=="d449629979028123a5c4dc9e732a2ec19b7fce31" && root.GetProperty("numpy").GetString()=="1.26.4" && root.GetProperty("scipy").GetString()=="1.17.1","seam trace provenance");
        var cases=root.GetProperty("cases").EnumerateArray().ToDictionary(x=>x.GetProperty("id").GetString()!,StringComparer.Ordinal);
        Need(cases.Keys.ToHashSet().SetEquals(new[]{"synthetic-base","synthetic-distinct","synthetic-front-length-plus-one","synthetic-shoulder-18","synthetic-shoulder-30"}),"seam trace cases");
        double Projected(JsonElement e)=>e.GetProperty("projected_cm").GetDouble();
        bool UnitRuffle(JsonElement e) { ValidateTraceRuffles(e);return e.GetProperty("ruffle").EnumerateArray().All(r=>r.GetProperty("coefficient").GetDouble()==1); }
        var gaps=new Dictionary<string,double>();
        foreach(var (id,c) in cases) {
            double waist=c.GetProperty("body_waist_cm").GetDouble(),backWaist=c.GetProperty("body_back_waist_cm").GetDouble();
            var trace=c.GetProperty("stage_trace").EnumerateArray().ToArray();
            Need(trace.Length==2 && trace[0].GetProperty("panel").GetString()=="right" && trace[0].GetProperty("operation").GetString()=="add_sleeves" && trace[1].GetProperty("operation").GetString()=="add_collars","seam trace stages");
            var before=trace[0].GetProperty("before");var afterSleeve=trace[0].GetProperty("after");var afterCollar=trace[1].GetProperty("after");
            foreach(string side in new[]{"front","back"})foreach(string part in new[]{"outside","shoulder","bottom"}) {
                Need(UnitRuffle(before.GetProperty(side).GetProperty(part)) && UnitRuffle(afterSleeve.GetProperty(side).GetProperty(part)) && UnitRuffle(afterCollar.GetProperty(side).GetProperty(part)),"unencoded bodice ruffle");
                double initial=Projected(before.GetProperty(side).GetProperty("bottom"));
                Need(Math.Abs(initial-Projected(afterSleeve.GetProperty(side).GetProperty("bottom")))<1e-8 && Math.Abs(initial-Projected(afterCollar.GetProperty(side).GetProperty("bottom")))<1e-8,"bodice cuts altered waist interface");
            }
            var upper=c.GetProperty("upper_waist");var lower=c.GetProperty("lower_waist");double u=Projected(upper),l=Projected(lower);
            Need(UnitRuffle(upper)&&UnitRuffle(lower) && Math.Abs(l-waist)<1e-6,"waist source target or ruffle");
            double f=Projected(before.GetProperty("front").GetProperty("bottom")),b=Projected(before.GetProperty("back").GetProperty("bottom"));
            Need(Math.Abs(f-(waist-backWaist)/2)<1e-6 && b>backWaist/2 && Math.Abs(u-2*(f+b))<1e-6 && Math.Abs((u-l)-2*(b-backWaist/2))<1e-6,"source waist excess decomposition");
            gaps[id]=u-l;
            foreach(string part in new[]{"outside","shoulder"}) {
                double fBefore=Projected(before.GetProperty("front").GetProperty(part)),fAfter=Projected(afterSleeve.GetProperty("front").GetProperty(part));
                double bBefore=Projected(before.GetProperty("back").GetProperty(part)),bAfter=Projected(afterSleeve.GetProperty("back").GetProperty(part));
                Need(Math.Abs(fBefore-fAfter)>1 && Math.Abs(bBefore-bAfter)>1,"sleeve stage did not cut both interfaces");
                if(part=="outside")Need(Math.Abs(fAfter-Projected(afterCollar.GetProperty("front").GetProperty(part)))<1e-7 && Math.Abs(bAfter-Projected(afterCollar.GetProperty("back").GetProperty(part)))<1e-7,"collar altered outside seam");
            }
            var skirt=c.GetProperty("skirt_sides").EnumerateArray().ToArray();Need(skirt.Length==2,"skirt side count");
            for(int i=0;i<skirt.Length;i++) {
                var a=skirt[i].GetProperty("a");var bside=skirt[i].GetProperty("b");ValidateTraceRuffles(bside);
                var sections=bside.GetProperty("ruffle").EnumerateArray().ToArray();
                Need(UnitRuffle(a) && bside.GetProperty("edge_count").GetInt32()==2 && sections.Length==2 &&
                    sections[0].GetProperty("coefficient").GetDouble()==(i==0?1:1.05) && sections[1].GetProperty("coefficient").GetDouble()==(i==0?1.05:1) &&
                    sections[0].GetProperty("section")[0].GetInt32()==0 && sections[0].GetProperty("section")[1].GetInt32()==1 &&
                    sections[1].GetProperty("section")[0].GetInt32()==1 && sections[1].GetProperty("section")[1].GetInt32()==2 &&
                    Math.Abs(Projected(a)-Projected(bside))<1e-5,"encoded skirt side projection");
            }
        }
        var sample=cases["synthetic-base"].GetProperty("upper_waist");
        void RejectTrace(Action<JsonObject> mutate) {
            var obj=JsonNode.Parse(sample.GetRawText())!.AsObject();mutate(obj);
            using var changed=JsonDocument.Parse(obj.ToJsonString());
            try { ValidateTraceRuffles(changed.RootElement);throw new Exception("Malformed trace ruffle accepted"); } catch(InvalidDataException) { }
        }
        RejectTrace(x=>x["ruffle"]=new JsonArray());
        RejectTrace(x=>x.Remove("ruffle"));
        RejectTrace(x=>x.Remove("edge_count"));
        RejectTrace(x=>x["edge_count"]=0);
        RejectTrace(x=>x["ruffle"]![0]!.AsObject().Remove("coefficient"));
        RejectTrace(x=>x["ruffle"]![0]!.AsObject().Remove("section"));
        RejectTrace(x=>x["ruffle"]![0]!["section"]![1]=0);
        RejectTrace(x=>x["ruffle"]!.AsArray().RemoveAt(0));
        RejectTrace(x=>x["ruffle"]![0]!["section"]![1]=1);
        RejectTrace(x=>x["ruffle"]![1]!["section"]![0]=1);
        RejectTrace(x=>x["ruffle"]!.AsArray()[3]!["section"]![1]=10000);
        RejectTrace(x=>x["ruffle"]![0]!["coefficient"]=0);
        Need(Math.Abs(gaps["synthetic-base"]-gaps["synthetic-front-length-plus-one"])<1e-7 && gaps["synthetic-shoulder-18"]<gaps["synthetic-base"] && gaps["synthetic-base"]<gaps["synthetic-shoulder-30"],"synthetic waist sensitivity");
        using var backStream=typeof(Validator).Assembly.GetManifestResourceStream("FittedCheck.back-waist-stages.json") ?? throw new InvalidDataException("Missing back stages");
        using var backReader=new StreamReader(backStream,Encoding.UTF8,true);string backText=backReader.ReadToEnd().Replace("\r\n","\n").Trim().TrimStart('\uFEFF');
        Need(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(backText)))=="6A8386924160425D49E48A88117419C3ECBD3CDA154413CD8EA2323579B1A856","back stage hash");
        using var backDoc=JsonDocument.Parse(backText);
        foreach(var c in backDoc.RootElement.GetProperty("cases").EnumerateArray().Where(x=>x.GetProperty("id").GetString() is "synthetic-base" or "synthetic-distinct")) {
            string id=c.GetProperty("id").GetString()!;var e=c.GetProperty("edges");double len=0,horizontal=0;
            foreach(int n in new[]{0,3,6}) { var edge=e[n];len+=edge.GetProperty("length_cm").GetDouble();horizontal+=Math.Abs(edge.GetProperty("end")[0].GetDouble()-edge.GetProperty("start")[0].GetDouble()); }
            double target=c.GetProperty("body_input_cm_degree").GetProperty("waist_back_width").GetDouble()/2;
            double curveExcess=len-horizontal,remainingHorizontal=horizontal-target;
            Need(curveExcess>0 && remainingHorizontal>0 && Math.Abs(2*(curveExcess+remainingHorizontal)-gaps[id])<1e-6,"source back waist breakdown");
            Console.WriteLine($"{id} source half-back waist excess: curve-vs-horizontal={curveExcess:G8} cm, remaining horizontal={remainingHorizontal:G8} cm; source-only diagnostic.");
        }
    }
    internal static void CheckOptimizerTrace()
    {
        using var stream=typeof(Validator).Assembly.GetManifestResourceStream("FittedCheck.optimizer-trace.json") ?? throw new InvalidDataException("Missing optimizer trace");
        using var reader=new StreamReader(stream,Encoding.UTF8,true);string text=reader.ReadToEnd().Replace("\r\n","\n").Trim().TrimStart('\uFEFF');
        Need(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))=="0AA55A9F5E654C6BCC5F7F7080F719712983CD4382D2DB084CBEE47904307C65","optimizer trace hash");
        using var doc=JsonDocument.Parse(text);var root=doc.RootElement;
        Need(root.GetProperty("source_commit").GetString()=="d449629979028123a5c4dc9e732a2ec19b7fce31" && root.GetProperty("numpy").GetString()=="1.26.4" && root.GetProperty("scipy").GetString()=="1.17.1","optimizer trace provenance");
        var angles=new HashSet<int>();double maxDiff=0;
        foreach(var row in root.GetProperty("cases").EnumerateArray()) {
            int degrees=row.GetProperty("angle_degrees").GetInt32();Need(angles.Add(degrees),"duplicate optimizer angle");
            var ray=TangentRayStudy.Create("synthetic-angle-"+degrees);var c=Point(row.GetProperty("control"));double norm=c.Length();
            Need(norm>0 && row.GetProperty("success").GetBoolean() && row.GetProperty("status").GetInt32()==0 && row.GetProperty("iterations").GetInt32()>0 && row.GetProperty("evaluations").GetInt32()>0,"source optimizer result");
            double dx=c.X/norm-ray.Target.X,dy=c.Y/norm-ray.Target.Y,objective=dx*dx+dy*dy;
            Need(Math.Abs(objective-row.GetProperty("objective").GetDouble())<1e-12 && objective<2e-12,"source tangent objective");
            double diff=(c-new P(ray.Candidate.X,ray.Candidate.Y)).Length();maxDiff=Math.Max(maxDiff,diff);
            Need(row.GetProperty("gradient").EnumerateArray().All(x=>double.IsFinite(x.GetDouble())),"source optimizer gradient");
        }
        Need(angles.SetEquals(new[]{5,10,18,22,26,30,35,40}) && maxDiff>.04,"source optimizer degeneracy evidence");
        Console.WriteLine($"Synthetic source tangent optimizer: 8/8 success and small tangent objective, max control-ray difference={maxDiff:G7}; no equivalence guarantee.");
    }
    internal static void CheckTangentSweep()
    {
        using var stream=typeof(Validator).Assembly.GetManifestResourceStream("FittedCheck.tangent-sweep.json") ?? throw new InvalidDataException("Missing tangent sweep");
        using var reader=new StreamReader(stream);string text=reader.ReadToEnd().Replace("\r\n","\n").Trim().TrimStart('\uFEFF');
        Need(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))=="92C42F546CB24AFC4AD8BFD64A36EBC55FEDDEB39DCCF6936B8C7DE8CB5E4ECA","tangent sweep hash");
        using var doc=JsonDocument.Parse(text);Need(doc.RootElement.GetProperty("source_commit").GetString()=="d449629979028123a5c4dc9e732a2ec19b7fce31","tangent source");
        var cases=doc.RootElement.GetProperty("cases").EnumerateArray().ToArray();Need(cases.Length==8,"angle sweep count");
        int within=0,rejected=0;var seen=new HashSet<string>();
        foreach(var item in cases)
        {
            string id=item.GetProperty("id").GetString()!;Need(seen.Add(id),"duplicate angle");var result=TangentRayStudy.Create(id);
            Need(item.GetProperty("angle_degrees").GetInt32()==result.Degrees && result.Status=="unresolved" && !result.CanCompose && !result.ExportAllowed,"angle gate");
            P source=Point(item.GetProperty("relative_control")),candidate=new(result.Candidate.X,result.Candidate.Y),alt=new(result.Alternative.X,result.Alternative.Y),target=new(result.Target.X,result.Target.Y);
            double error=(source-candidate).Length();if(error<.0004)within++;else rejected++;
            double Cross(P x,P y)=>x.X*y.Y-x.Y*y.X;
            Need(Math.Abs(Cross(candidate,target))<1e-15 && Math.Abs(Cross(alt,target))<1e-15 && (alt-candidate).Length()>.24,"tangent objective ambiguity");
            Console.WriteLine($"{id}: source tangent control difference={error:G7}; {(error<.0004?"within diagnostic threshold":"outside diagnostic threshold; blocked")}.");
        }
        Need(within>0 && rejected>0,"angle sweep must show both outcomes");
    }
    internal static void CheckBackWaist()
    {
        using var stream=typeof(Validator).Assembly.GetManifestResourceStream("FittedCheck.back-waist-stages.json") ?? throw new InvalidDataException("Missing back waist reference");
        using var reader=new StreamReader(stream);string text=reader.ReadToEnd().Replace("\r\n","\n").Trim().TrimStart('\uFEFF');
        Need(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))=="6A8386924160425D49E48A88117419C3ECBD3CDA154413CD8EA2323579B1A856","back fixture hash");
        using var reference=JsonDocument.Parse(text);
        Need(reference.RootElement.GetProperty("source_commit").GetString()=="d449629979028123a5c4dc9e732a2ec19b7fce31","back source");
        var caseIds=new HashSet<string>();
        Need(reference.RootElement.GetProperty("cases").GetArrayLength()==5,"back case count");
        foreach(var item in reference.RootElement.GetProperty("cases").EnumerateArray())
        {
            string id=item.GetProperty("id").GetString()!;Need(caseIds.Add(id),"duplicate back fixture");var path=BackWaistCandidate.Create(id);var expected=item.GetProperty("edges");
            var inputs=item.GetProperty("body_input_cm_degree");
            var rawInputs=inputs.EnumerateObject().Select(p=>new KeyValuePair<string,decimal>(p.Name,p.Value.GetDecimal())).ToArray();
            var explicitBody=ResearchBody.Create(rawInputs,"cm","degree");
            var sourceCatalog=SyntheticBodyCatalog.Create(id);
            foreach(string field in ResearchBody.Fields)Need(explicitBody[field]==sourceCatalog[field],"catalog body mismatch: "+field);
            var calculated=explicitBody.Derive();var derivedSource=item.GetProperty("derived");
            foreach(var v in new[]{("_waist_level",calculated.WaistLevel),("_leg_length",calculated.LegLength),("_base_sleeve_balance",calculated.BaseSleeveBalance),("_bust_line",calculated.BustLine),("_hip_inclination",calculated.HipInclination),("_shoulder_incl",calculated.ShoulderInclination),("_armscye_depth",calculated.ArmscyeDepth)}) Need(Math.Abs(derivedSource.GetProperty(v.Item1).GetDouble()-v.Item2)<1e-10,"derived body mismatch: "+v.Item1);
            var generic=BackWaistCandidate.CreateFromBody(explicitBody,ResearchDesign.PinnedFitted);
            string[] usedBack=["back_width","waist_back_width","waist_line","shoulder_w","shoulder_incl","bum_points","bust_line","vert_bust_line"];
            var partialInput=ResearchMeasurements.Create(rawInputs.Where(p=>usedBack.Contains(p.Key)),"cm","degree");
            var partialCandidate=BackWaistCandidate.CreateFromMeasurements(partialInput,ResearchDesign.PinnedFitted);
            Need(partialCandidate.Edges.SequenceEqual(generic.Edges) && partialCandidate.ReferenceAssessment==BackWaistCandidate.ReferenceState.Unverified && !partialCandidate.ExportAllowed && !partialCandidate.ProductionAllowed,"partial back differs from full body");
            Need(!typeof(BackWaistCandidate.Path).IsAssignableFrom(generic.GetType()),"typed back can enter fixture consumer");
            Need(generic.ReferenceAssessment==BackWaistCandidate.ReferenceState.Unverified && generic.Status=="unresolved" && !generic.ExportAllowed && !generic.ProductionAllowed && generic.Snapshot==explicitBody.Snapshot(ResearchDesign.PinnedFitted),"explicit body gate");
            Need(generic.Edges.Count==path.Edges.Count,"explicit body edge count");
            for(int i=0;i<path.Edges.Count;i++) Need(generic.Edges[i]==path.Edges[i],"explicit body differs from fixture calculation");
            try { generic.RequireResearchComposition();throw new InvalidDataException("Unverified body composed"); } catch(InvalidOperationException) { }
            var reordered=ResearchBody.Create(rawInputs.Reverse(),"cm","degree");
            Need(reordered.Snapshot(ResearchDesign.PinnedFitted)==generic.Snapshot,"body order changed snapshot");
            var expectedInput=id switch {
                "synthetic-base"=>(Back:48m,WaistBack:36m,WaistLine:37m,Angle:22m,Bum:18m,Vert:21m,Bust:26m),
                "synthetic-distinct"=>(Back:43m,WaistBack:34m,WaistLine:39m,Angle:22m,Bum:18m,Vert:21m,Bust:26m),
                "synthetic-third"=>(Back:50m,WaistBack:38m,WaistLine:38m,Angle:18m,Bum:19m,Vert:22m,Bust:25m),
                "synthetic-small-difference"=>(Back:44m,WaistBack:38m,WaistLine:40m,Angle:30m,Bum:18m,Vert:22m,Bust:24m),
                "synthetic-fifth"=>(Back:46m,WaistBack:35m,WaistLine:38m,Angle:15m,Bum:17m,Vert:20m,Bust:25m),
                _=>throw new InvalidDataException("Unknown back fixture") };
            foreach(var p in new[]{("back_width",expectedInput.Back),("waist_back_width",expectedInput.WaistBack),("waist_line",expectedInput.WaistLine),("shoulder_incl",expectedInput.Angle),("bum_points",expectedInput.Bum),("vert_bust_line",expectedInput.Vert),("bust_line",expectedInput.Bust)}) Need(ExactDecimalEqual(inputs.GetProperty(p.Item1),p.Item2),"back input mismatch: "+p.Item1);
            Need(inputs.EnumerateObject().All(p=>!p.Name.StartsWith('_')),"derived field in body inputs");
            var stages=item.GetProperty("calls");Need(stages.GetArrayLength()==2,"back dart source stages");
            var trace=path.SourceSteps;var baseEdge=stages[0].GetProperty("base");
            Need((Point(baseEdge.GetProperty("start"))-new P(trace.BaseStart.X,trace.BaseStart.Y)).Length()<1e-12 && (Point(baseEdge.GetProperty("end"))-new P(trace.BaseEnd.X,trace.BaseEnd.Y)).Length()<1e-12,"back base endpoints");
            double baseControlError=(Point(baseEdge.GetProperty("relative_controls")[0])-new P(trace.BaseRelativeControl.X,trace.BaseRelativeControl.Y)).Length();
            if(id!="synthetic-small-difference") Need(baseControlError<.0004,"back base control candidate");
            Need(Math.Abs(stages[0].GetProperty("offset_cm").GetDouble()-trace.OuterOffset)<1e-12 && Math.Abs(stages[1].GetProperty("offset_cm").GetDouble()-trace.InnerOffset)<1e-12,"back cut offsets");
            Need(Math.Abs(stages[0].GetProperty("shape")[1].GetProperty("end")[0].GetDouble()-trace.DartWidth)<1e-12 && Math.Abs(stages[1].GetProperty("shape")[1].GetProperty("end")[0].GetDouble()-trace.DartWidth)<1e-12,"back dart width");
            Need(Math.Abs(stages[0].GetProperty("shape")[0].GetProperty("length_cm").GetDouble()-trace.OuterLeg)<1e-10 && Math.Abs(stages[1].GetProperty("shape")[0].GetProperty("length_cm").GetDouble()-trace.InnerLeg)<1e-10,"back dart leg source semantics");
            Need(path.Edges.Count==11 && expected.GetArrayLength()==11 && path.Status=="unresolved"&&!path.ExportAllowed&&!path.ProductionAllowed,"back candidate gate");
            double maximum=0,maxControl=0;
            for(int i=0;i<11;i++)
            {
                var actual=path.Edges[i];var edge=expected[i];Need(actual.Id==i && (actual.Kind=="Q"?"CurveEdge":"Edge")==edge.GetProperty("kind").GetString(),"back edge kind");
                var a=Point(edge.GetProperty("start"));var b=Point(edge.GetProperty("end"));
                maximum=Math.Max(maximum,Math.Max((a-new P(actual.Start.X,actual.Start.Y)).Length(),(b-new P(actual.End.X,actual.End.Y)).Length()));
                if(actual.Kind=="Q") { var control=actual.RelativeControl ?? throw new InvalidDataException("missing back control");maxControl=Math.Max(maxControl,(Point(edge.GetProperty("relative_controls")[0])-new P(control.X,control.Y)).Length()); }
            }
            Console.WriteLine($"{id} back candidate max endpoint error={maximum:G6} cm; max relative-control error={maxControl:G6}.");
            bool withinResearchBounds=baseControlError<.0004 && maximum<.001 && maxControl<.0002;
            if(id=="synthetic-small-difference")
            {
                Need(!withinResearchBounds && path.ReferenceAssessment==BackWaistCandidate.ReferenceState.KnownMismatch,"expected difficult case to remain unresolved");
                try { path.RequireResearchComposition(); throw new InvalidDataException("Known mismatch composed"); } catch(InvalidOperationException) { }
                try { BackBodiceCandidate.Create(id,false); throw new InvalidDataException("Known mismatch body composed"); } catch(InvalidOperationException) { }
                Console.WriteLine($"{id}: source approximation exceeds research bounds (initial control difference={baseControlError:G6}); rejected.");
            }
            else Need(withinResearchBounds && path.ReferenceAssessment==BackWaistCandidate.ReferenceState.Unassessed,"back approximation outside research bound");
            var sourceRules=item.GetProperty("stitches").EnumerateArray().Select(e=>new GeneratedGraph.Seam(id,"precut_back",e.GetProperty("a").GetInt32(),e.GetProperty("a_flip").GetBoolean(),"precut_back",e.GetProperty("b").GetInt32(),e.GetProperty("b_flip").GetBoolean())).ToArray();
            var graph=path.Edges.Select(e=>new GeneratedGraph.Edge(id,"precut_back",e.Id,e.Kind,new(e.Start.X,e.Start.Y),new(e.End.X,e.End.Y))).ToArray();
            var diagnosis=GeneratedGraph.Inspect(id,graph,path.DartRules,sourceRules);
            Need(path.DartRules.Count==2 && !diagnosis.Issues.Any(x=>x.StartsWith("open-contour:")) && !diagnosis.Issues.Contains("seam-identity-or-direction-mismatch") && diagnosis.Issues.Any(x=>x.StartsWith("curve-topology-unproven:")) && !diagnosis.ExportAllowed,"back graph unresolved");
            foreach(var seam in path.DartRules)
            {
                var a=path.Edges[seam.EdgeA];var b=path.Edges[seam.EdgeB];
                Need((new P(a.End.X,a.End.Y)-new P(b.Start.X,b.Start.Y)).Length()<1e-10,"back dart designated apex");
                double la=(new P(a.End.X,a.End.Y)-new P(a.Start.X,a.Start.Y)).Length(),lb=(new P(b.End.X,b.End.Y)-new P(b.Start.X,b.Start.Y)).Length();
                Need(Math.Abs(la-lb)<1e-9,"back dart numerical legs");
            }
            var altered=path.DartRules.ToArray();altered[0]=altered[0] with { EdgeA=3 };
            Need(GeneratedGraph.Inspect(id,graph,altered,sourceRules).Issues.Contains("seam-identity-or-direction-mismatch"),"wrong valid back seam");
            var audit=BackBezierAudit.Inspect(path);
            Need(audit.Separated+audit.EndpointOnly+audit.Unresolved==55 && audit.Status=="unresolved"&&!audit.ExportAllowed,"back audit coverage");
            Console.WriteLine($"{id} back represented controls: separated={audit.Separated}, endpoint-only={audit.EndpointOnly}, unresolved={audit.Unresolved}, nonmonotone={audit.NonMonotone}.");
        }
    }
    internal static void CheckPrecutBodice()
    {
        using var stream=typeof(Validator).Assembly.GetManifestResourceStream("FittedCheck.precut-bodice.json") ?? throw new InvalidDataException("Missing precut reference");
                using var reader=new StreamReader(stream);string text=reader.ReadToEnd().Replace("\r\n","\n").Trim().TrimStart('\uFEFF');
        Need(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))=="57C71A5F033A42BB7E519D3D104CA85751E090B3F672F937151DF1E68E7A23A6","precut fixture hash");
        using var document=JsonDocument.Parse(text);
        Need(document.RootElement.GetProperty("source_commit").GetString()=="d449629979028123a5c4dc9e732a2ec19b7fce31","precut source");
        Need(document.RootElement.GetProperty("cases").GetArrayLength()==2,"precut case count");
        foreach(var item in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            string id=item.GetProperty("id").GetString()!; var path=PrecutFrontBodice.Create(id);
            var edges=item.GetProperty("edges").EnumerateArray().ToArray();Need(path.Vertices.Count==10 && edges.Length==10,"precut contour count");
            var typed=PrecutFrontBodice.CreateFromBody(SyntheticBodyCatalog.Create(id),ResearchDesign.PinnedFitted);
            string[] usedFront=["bust","back_width","waist","waist_back_width","bust_points","shoulder_w","shoulder_incl","waist_line","waist_over_bust_line","bust_line","vert_bust_line"];
            var synthetic= SyntheticBodyCatalog.Create(id);
            var partialInput=ResearchMeasurements.Create(usedFront.Select(k=>new KeyValuePair<string,decimal>(k,synthetic[k])),"cm","degree");
            var partialCandidate=PrecutFrontBodice.CreateFromMeasurements(partialInput,ResearchDesign.PinnedFitted);
            Need(partialCandidate.Vertices.SequenceEqual(typed.Vertices) && partialCandidate.ReferenceAssessment==BackWaistCandidate.ReferenceState.Unverified && !partialCandidate.ExportAllowed && !partialCandidate.ProductionAllowed,"partial front differs from full body");
            Need(!typeof(PrecutFrontBodice.Path).IsAssignableFrom(typed.GetType()),"typed front can enter fixture consumer");
            try { typed.RequireResearchComposition();throw new InvalidDataException("Unverified front composed"); } catch(InvalidOperationException) { }
            Need(typed.Status=="unresolved" && !typed.ExportAllowed && !typed.ProductionAllowed && typed.ReferenceAssessment==BackWaistCandidate.ReferenceState.Unverified,"typed front gate");
            Need(typed.Vertices.Count==path.Vertices.Count && typed.Vertices.Zip(path.Vertices).All(pair=>Math.Abs(pair.First.X-pair.Second.X)<1e-10 && Math.Abs(pair.First.Y-pair.Second.Y)<1e-10),"typed front differs from fixture calculation");
            var graph=new List<GeneratedGraph.Edge>();
            for(int i=0;i<10;i++)
            {
                var a=path.Vertices[i]; var b=path.Vertices[(i+1)%10];var source=edges[i];
                Need(source.GetProperty("kind").GetString()=="Edge" && (Point(source.GetProperty("start"))-new P(a.X,a.Y)).Length()<1e-5 && (Point(source.GetProperty("end"))-new P(b.X,b.Y)).Length()<1e-5,"precut body edge "+i);
                graph.Add(new(id,"precut_front",i,"L",new(a.X,a.Y),new(b.X,b.Y)));
            }
            var expected=item.GetProperty("stitches").EnumerateArray().Select(e=>new GeneratedGraph.Seam(id,"precut_front",e.GetProperty("a").GetInt32(),e.GetProperty("a_flip").GetBoolean(),"precut_front",e.GetProperty("b").GetInt32(),e.GetProperty("b_flip").GetBoolean())).ToArray();
            var diagnostic=GeneratedGraph.Inspect(id,graph,path.DartRules,expected);
            Need(diagnostic.Status=="unresolved" && !diagnostic.ExportAllowed && diagnostic.Issues.SequenceEqual(new[]{"physical-sewing-and-production-unresolved"}),"precut topology");
            Need(path.Status=="unresolved"&&!path.ProductionAllowed&&!path.ExportAllowed,"precut output gate");
        }
    }
    internal static void CheckInterfaceMatching(JsonElement golden)
    {
        using var stream=typeof(Validator).Assembly.GetManifestResourceStream("FittedCheck.pre-seam.json") ?? throw new InvalidDataException("Missing pre-seam fixture");
        using var reader=new StreamReader(stream); using var pre=JsonDocument.Parse(reader.ReadToEnd().TrimStart('\uFEFF'));
        Need(pre.RootElement.GetProperty("source_commit").GetString()=="d449629979028123a5c4dc9e732a2ec19b7fce31","pre-seam source");
        foreach(var c in pre.RootElement.GetProperty("cases").EnumerateArray())
        {
            string id=c.GetProperty("id").GetString()!;
            var upperInput=c.GetProperty("upper").EnumerateArray().Select(InterfaceMatcher.Read).ToArray();
            var lowerInput=c.GetProperty("lower").EnumerateArray().Select(InterfaceMatcher.Read).ToArray();
            var alteredInput=upperInput.ToArray(); alteredInput[0]=alteredInput[0] with { Start=alteredInput[0].Start+new InterfaceMatcher.P(.001,0) };
            try { InterfaceMatcher.Match(id,alteredInput,lowerInput); throw new InvalidDataException("Changed same-ID input accepted"); } catch(ArgumentException) { }
            var alteredCurve=upperInput.ToArray(); int curveIndex=Array.FindIndex(alteredCurve,e=>e.Controls.Length>0);
            Need(curveIndex>=0,"missing source curve fixture");
            alteredCurve[curveIndex]=alteredCurve[curveIndex] with { Controls=alteredCurve[curveIndex].Controls.SetItem(0,alteredCurve[curveIndex].Controls[0]+new InterfaceMatcher.P(.001,0)) };
            try { InterfaceMatcher.Match(id,alteredCurve,lowerInput); throw new InvalidDataException("Changed same-ID control accepted"); } catch(ArgumentException) { }
            var alteredIdentity=lowerInput.ToArray(); alteredIdentity[0]=alteredIdentity[0] with { Panel="skirt_back" };
            try { InterfaceMatcher.Match(id,upperInput,alteredIdentity); throw new InvalidDataException("Changed same-ID identity accepted"); } catch(ArgumentException) { }
            var match=InterfaceMatcher.Match(id,upperInput,lowerInput);
            var forged=match with { Lower=match.Lower.SetItem(0,match.Lower[0] with { End=match.Lower[0].End+new InterfaceMatcher.P(.001,0) }) };
            try { FrontSkirtCandidate.CreateFromMatchedInterface(id,forged); throw new InvalidDataException("Changed result accepted"); } catch(ArgumentException) { }
            try { BackSkirtCandidate.Create(id,forged); throw new InvalidDataException("Changed back result accepted"); } catch(ArgumentException) { }
            var reference=golden.GetProperty("cases").EnumerateArray().Single(x=>x.GetProperty("id").GetString()==id);
            var waist=reference.GetProperty("stitches").EnumerateArray().Single(r=>r.GetProperty("owner_type").GetString()=="MetaGarment");
            CheckSyntheticSourceSeamLengths(reference);
            CheckGeneralControls(reference,SyntheticBodyCatalog.Create(id));
            string[] excluded=["arm_length","arm_pose_angle","crotch_hip_diff","leg_circ","underbust","wrist"];
            var fullBody=SyntheticBodyCatalog.Create(id);
            var measurements=ResearchMeasurements.Create(ResearchBody.Fields.Where(k=>!excluded.Contains(k)).Select(k=>new KeyValuePair<string,decimal>(k,fullBody[k])),"cm","degree");
            var limitedBody=measurements.BuildPinnedGarment();
            CheckGeneralControls(reference,limitedBody);
            var limitedFront=FrontBodiceCandidate.CreateFromBody(limitedBody,ResearchDesign.PinnedFitted);
            var fullFront=FrontBodiceCandidate.CreateFromBody(fullBody,ResearchDesign.PinnedFitted);
            Need(limitedFront.Edges.Zip(fullFront.Edges).All(p=>p.First.Kind==p.Second.Kind && p.First.Start==p.Second.Start && p.First.End==p.Second.End),"partial front differs from full");
            foreach(bool left in new[]{false,true}) {
                var limitedBack=BackBodiceCandidate.CreateFromBody(limitedBody,ResearchDesign.PinnedFitted,left);var fullBack=BackBodiceCandidate.CreateFromBody(fullBody,ResearchDesign.PinnedFitted,left);
                Need(limitedBack.Edges.Zip(fullBack.Edges).All(p=>p.First.Kind==p.Second.Kind && p.First.Start==p.Second.Start && p.First.End==p.Second.End),"partial back differs from full");
            }
            var limitedLeft=FrontBodiceCandidate.CreateLeftFromBody(limitedBody,ResearchDesign.PinnedFitted);var fullLeft=FrontBodiceCandidate.CreateLeftFromBody(fullBody,ResearchDesign.PinnedFitted);
            Need(limitedLeft.Edges.Zip(fullLeft.Edges).All(p=>p.First.Start==p.Second.Start && p.First.End==p.Second.End),"partial left differs from full");
            var limitedSf=FrontSkirtCandidate.CreateFromMatchedBody(limitedBody,ResearchDesign.PinnedFitted);var fullSf=FrontSkirtCandidate.CreateFromMatchedBody(fullBody,ResearchDesign.PinnedFitted);
            var limitedSb=BackSkirtCandidate.CreateFromBody(limitedBody,ResearchDesign.PinnedFitted);var fullSb=BackSkirtCandidate.CreateFromBody(fullBody,ResearchDesign.PinnedFitted);
            Need(limitedSf.Edges.SequenceEqual(fullSf.Edges) && limitedSb.Edges.SequenceEqual(fullSb.Edges),"partial skirt differs from full");
            var limitedGraph=BodySeamStudy.Calculate(limitedBody,ResearchDesign.PinnedFitted);
            Need(limitedGraph.Rules.Count==23 && !limitedGraph.ExportAllowed && !limitedGraph.ProductionAllowed && limitedGraph.ReferenceAssessment==BackWaistCandidate.ReferenceState.Unverified,"partial graph gate");
            var partialSourceRules=reference.GetProperty("stitches").EnumerateArray().ToArray();
            foreach(var candidateRule in limitedGraph.Rules) {
                var comparable=candidateRule.Kind=="MetaGarment"?candidateRule with { Owner=id }:candidateRule;
                var sourceRule=partialSourceRules.Single(r=>r.GetProperty("owner").GetString()==comparable.Owner && r.GetProperty("owner_type").GetString()==comparable.Kind && r.GetProperty("index").GetInt32()==comparable.Index);
                SyntheticFullSeamGraph.Compare(comparable,sourceRule);
            }

            var limitedProjection=BodySeamProjection.Calculate(limitedBody,ResearchDesign.PinnedFitted);
            Need(limitedProjection.Rules.Count==23 && limitedProjection.Status=="unresolved" && !limitedProjection.ExportAllowed,"partial seam projection gate");
            var fullProjection=BodySeamProjection.Calculate(fullBody,ResearchDesign.PinnedFitted);
            Need(limitedProjection.Rules.Zip(fullProjection.Rules).All(p=>p.First.Kind==p.Second.Kind && p.First.Index==p.Second.Index && p.First.A==p.Second.A && p.First.B==p.Second.B),"partial projected lengths differ from full");
            var limitedTopology=BodyCurveStudy.Calculate(limitedBody,ResearchDesign.PinnedFitted);
            Need(limitedTopology.Count==6 && limitedTopology.All(t=>t.Closure==BodyCurveStudy.ClosureEvidence.ClosedRepresented && t.Status=="unresolved" && !t.ExportAllowed),"partial topology gate");
            var fullTopology=BodyCurveStudy.Calculate(fullBody,ResearchDesign.PinnedFitted);
            Need(limitedTopology.Zip(fullTopology).All(p=>p.First.Panel==p.Second.Panel && p.First.Closure==p.Second.Closure && p.First.Pairs==p.Second.Pairs && p.First.Unresolved==p.Second.Unresolved && p.First.UnresolvedEdges.SequenceEqual(p.Second.UnresolvedEdges)),"partial topology differs from full");

            var independent=InterfaceMatcher.MatchDerived(id);
            var typedMatch=BodyWaistStudy.Calculate(SyntheticBodyCatalog.Create(id),ResearchDesign.PinnedFitted);
            Need(typedMatch.ReferenceAssessment==BackWaistCandidate.ReferenceState.Unverified && !typedMatch.ExportAllowed && !typedMatch.ProductionAllowed && !typeof(InterfaceMatcher.Result).IsAssignableFrom(typedMatch.GetType()),"typed waist gate");
            try { typedMatch.RequireResearchComposition();throw new InvalidDataException("Unverified waist composed"); } catch(InvalidOperationException) { }
            Need(typedMatch.Lower.Length==independent.Lower.Length && typedMatch.Upper.Length==independent.Upper.Length,"typed waist topology");
            Need(typedMatch.Lower.Zip(independent.Lower).All(p=>p.First.Panel==p.Second.Panel && (p.First.Start-p.Second.Start).Norm()<1e-8 && (p.First.End-p.Second.End).Norm()<1e-8),"typed waist differs from C# fixture result");
            var bodyForPanels=SyntheticBodyCatalog.Create(id);
            foreach(bool left in new[]{false,true}) {
                var back=BackBodiceCandidate.CreateFromBody(bodyForPanels,ResearchDesign.PinnedFitted,left);var fixedBack=BackBodiceCandidate.Create(id,left);
                Need(back.ReferenceAssessment==BackWaistCandidate.ReferenceState.Unverified && !back.ExportAllowed && !back.ProductionAllowed && !typeof(BackBodiceCandidate.Path).IsAssignableFrom(back.GetType()),"typed back panel gate");
                Need(back.Edges.Count==13 && back.Edges.Zip(fixedBack.Edges).All(p=>p.First.Kind==p.Second.Kind && Math.Abs(p.First.Start.X-p.Second.Start.X)<1e-9 && Math.Abs(p.First.Start.Y-p.Second.Start.Y)<1e-9 && Math.Abs(p.First.End.X-p.Second.End.X)<1e-9 && Math.Abs(p.First.End.Y-p.Second.End.Y)<1e-9 && p.First.ArcControlY==p.Second.ArcControlY && p.First.Controls.SequenceEqual(p.Second.Controls)),"typed back differs from C# fixture");
                var source=reference.GetProperty("panels").GetProperty(left?"left_btorso":"right_btorso").GetProperty("edges");
                double error=back.Edges.Max(e=>Math.Max((new P(e.Start.X,e.Start.Y)-Point(source[e.Id].GetProperty("start"))).Length(),(new P(e.End.X,e.End.Y)-Point(source[e.Id].GetProperty("end"))).Length()));
                Console.WriteLine($"{id} {(left?"left":"right")} back panel source endpoint error={error:G6} cm");
                Need(error<.001,"typed back source endpoint bound");
                try { back.RequireResearchComposition();throw new InvalidDataException("Unverified back panel composed"); } catch(InvalidOperationException) { }
            }
            var typedLeft=FrontBodiceCandidate.CreateLeftFromBody(bodyForPanels,ResearchDesign.PinnedFitted);var fixedLeft=FrontBodiceCandidate.Create(id,true);
            Need(typedLeft.ReferenceAssessment==BackWaistCandidate.ReferenceState.Unverified && !typedLeft.ExportAllowed && !typedLeft.ProductionAllowed && !typeof(FrontBodiceCandidate.Path).IsAssignableFrom(typedLeft.GetType()),"typed left front gate");
            Need(typedLeft.Edges.Count==fixedLeft.Edges.Count && typedLeft.Edges.Zip(fixedLeft.Edges).All(p=>p.First.Kind==p.Second.Kind && Math.Abs(p.First.Start.X-p.Second.Start.X)<1e-9 && Math.Abs(p.First.Start.Y-p.Second.Start.Y)<1e-9 && Math.Abs(p.First.End.X-p.Second.End.X)<1e-9 && Math.Abs(p.First.End.Y-p.Second.End.Y)<1e-9 && p.First.ArcControlY==p.Second.ArcControlY && p.First.Controls.Count==p.Second.Controls.Count && p.First.Controls.Zip(p.Second.Controls).All(c=>Math.Abs(c.First.X-c.Second.X)<1e-12 && Math.Abs(c.First.Y-c.Second.Y)<1e-12)),"typed left front differs from C# fixture");
            var sourceLeft=reference.GetProperty("panels").GetProperty("left_ftorso").GetProperty("edges");
            double leftError=typedLeft.Edges.Max(e=>Math.Max((new P(e.Start.X,e.Start.Y)-Point(sourceLeft[e.Id].GetProperty("start"))).Length(),(new P(e.End.X,e.End.Y)-Point(sourceLeft[e.Id].GetProperty("end"))).Length()));
            Need(leftError<.001,"typed left front source endpoint bound");
            try { typedLeft.RequireResearchComposition();throw new InvalidDataException("Unverified left panel composed"); } catch(InvalidOperationException) { }
            var typedFrontPanel=FrontBodiceCandidate.CreateFromBody(SyntheticBodyCatalog.Create(id),ResearchDesign.PinnedFitted);
            var fixedFrontPanel=FrontBodiceCandidate.Create(id,false);
            Need(typedFrontPanel.ReferenceAssessment==BackWaistCandidate.ReferenceState.Unverified && !typedFrontPanel.ExportAllowed && !typedFrontPanel.ProductionAllowed && !typeof(FrontBodiceCandidate.Path).IsAssignableFrom(typedFrontPanel.GetType()),"typed front panel gate");
            Need(typedFrontPanel.Edges.Count==fixedFrontPanel.Edges.Count && typedFrontPanel.Edges.Zip(fixedFrontPanel.Edges).All(p=>p.First.Kind==p.Second.Kind && Math.Abs(p.First.Start.X-p.Second.Start.X)<1e-9 && Math.Abs(p.First.Start.Y-p.Second.Start.Y)<1e-9 && Math.Abs(p.First.End.X-p.Second.End.X)<1e-9 && Math.Abs(p.First.End.Y-p.Second.End.Y)<1e-9),"typed front panel differs from fixture");
            Need(typedFrontPanel.Edges.Zip(fixedFrontPanel.Edges).All(p=>p.First.ArcControlY==p.Second.ArcControlY && p.First.Controls.SequenceEqual(p.Second.Controls)),"typed front panel control mismatch");
            var sourceFrontEdges=reference.GetProperty("panels").GetProperty("right_ftorso").GetProperty("edges");
            double maxFrontError=typedFrontPanel.Edges.Max(e=>Math.Max((new P(e.Start.X,e.Start.Y)-Point(sourceFrontEdges[e.Id].GetProperty("start"))).Length(),(new P(e.End.X,e.End.Y)-Point(sourceFrontEdges[e.Id].GetProperty("end"))).Length()));
            Console.WriteLine($"{id} body-derived front panel max source endpoint error={maxFrontError:G6} cm");
            Need(maxFrontError<.001,"typed front panel source endpoint bound");
            try { typedFrontPanel.RequireResearchComposition();throw new InvalidDataException("Unverified front panel composed"); } catch(InvalidOperationException) { }
            var typedBackSkirt=BackSkirtCandidate.CreateFromBody(SyntheticBodyCatalog.Create(id),ResearchDesign.PinnedFitted);
            var derivedBack=BackSkirtCandidate.CreateFromDerived(id,independent);
            Need(typedBackSkirt.ReferenceAssessment==BackWaistCandidate.ReferenceState.Unverified && !typedBackSkirt.ExportAllowed && !typedBackSkirt.ProductionAllowed && !typeof(BackSkirtCandidate.Candidate).IsAssignableFrom(typedBackSkirt.GetType()),"typed back skirt gate");
            Need(typedBackSkirt.Edges.Count==19 && typedBackSkirt.Edges.Zip(derivedBack.Edges).All(p=>p.First.Kind==p.Second.Kind && Math.Abs(p.First.Start.X-p.Second.Start.X)<1e-8 && Math.Abs(p.First.Start.Y-p.Second.Start.Y)<1e-8 && Math.Abs(p.First.End.X-p.Second.End.X)<1e-8 && Math.Abs(p.First.End.Y-p.Second.End.Y)<1e-8),"typed back skirt differs from derived fixture");
            Need(typedBackSkirt.Edges.Zip(derivedBack.Edges).All(p=>p.First.Control==p.Second.Control),"typed back skirt control mismatch");
            var sourceBackEdges=reference.GetProperty("panels").GetProperty("skirt_back").GetProperty("edges");
            double maxBackError=typedBackSkirt.Edges.Max(e=>Math.Max((new P(e.Start.X,e.Start.Y)-Point(sourceBackEdges[e.Id].GetProperty("start"))).Length(),(new P(e.End.X,e.End.Y)-Point(sourceBackEdges[e.Id].GetProperty("end"))).Length()));
            Console.WriteLine($"{id} body-matched back skirt max source endpoint error={maxBackError:G6} cm");
            Need(maxBackError<.001,"typed back skirt source endpoint bound");
            try { typedBackSkirt.RequireResearchComposition();throw new InvalidDataException("Unverified back skirt composed"); } catch(InvalidOperationException) { }
            var typedSkirt=FrontSkirtCandidate.CreateFromMatchedBody(SyntheticBodyCatalog.Create(id),ResearchDesign.PinnedFitted);
            var sourceSplit=Point(reference.GetProperty("panels").GetProperty("skirt_front").GetProperty("edges")[6].GetProperty("end"));
            var candidateSplit=new P(typedSkirt.Edges[6].End.X,typedSkirt.Edges[6].End.Y);
            var symmetric=FrontSkirtCandidate.CreateFromBody(SyntheticBodyCatalog.Create(id),ResearchDesign.PinnedFitted).Edges[6].End;
            var symmetricError=(new P(symmetric.X,symmetric.Y)-sourceSplit).Length();var matchedError=(candidateSplit-sourceSplit).Length();
            Console.WriteLine($"{id} front waist split source error: symmetric={symmetricError:G6} cm, body-matched={matchedError:G6} cm");
            Need(matchedError<symmetricError && matchedError<.001 && typedSkirt.ReferenceAssessment==BackWaistCandidate.ReferenceState.Unverified && !typedSkirt.ExportAllowed && !typedSkirt.ProductionAllowed,"typed waist split versus source");
            try { typedSkirt.RequireResearchComposition();throw new InvalidDataException("Unverified matched skirt composed"); } catch(InvalidOperationException) { }

            var independentReport=DerivedWaistAudit.Inspect(independent,reference);
            Console.WriteLine($"{id} C#-derived waist: max endpoint={independentReport.MaxEndpointCm:G6} cm, control={independentReport.MaxControlCm:G6} cm, length={independentReport.MaxLengthDifferenceCm:G6} cm, numerical disagreement={independentReport.MaxIntegrationDisagreementCm:G6} cm, enclosure max width≈{independentReport.MaxEnclosureWidthCm:G6} cm, source lengths inside={independentReport.SourceValuesInside}/{independentReport.EdgeCount}.");
            Console.WriteLine($"{id} candidate waist length upper≈[{independentReport.UpperLength.ApproximateLowerCm:G12},{independentReport.UpperLength.ApproximateUpperCm:G12}] cm; lower≈[{independentReport.LowerLength.ApproximateLowerCm:G12},{independentReport.LowerLength.ApproximateUpperCm:G12}] cm (display rounded).");
            Need(independentReport.EdgeCount==20 && independentReport.MaxEnclosureWidthCm<.0001 && independentReport.WithinDiagnosticBounds && independentReport.Status=="unresolved" && !independentReport.ProductionAllowed && !independentReport.ExportAllowed,"derived waist diagnostic bounds");
            try { InterfaceMatcher.ValidateBound(id,independent);throw new Exception("Derived result falsely treated as pinned source"); } catch(ArgumentException) { }
            int curvedIndex=Array.FindIndex(independent.Upper.ToArray(),e=>e.Controls.Length>0);
            Need(curvedIndex>=0,"derived waist curve missing");
            var alteredEdge=independent.Upper[curvedIndex];
            alteredEdge=alteredEdge with { Controls=alteredEdge.Controls.SetItem(0,alteredEdge.Controls[0]+new InterfaceMatcher.P(.1,0)) };
            var changed=independent with { Upper=independent.Upper.SetItem(curvedIndex,alteredEdge) };
            var wrongCoefficient=independent with { Upper=independent.Upper.SetItem(0,independent.Upper[0] with { Coefficient=1.05 }) };
            try { DerivedWaistAudit.Inspect(wrongCoefficient,reference);throw new Exception("Wrong waist coefficient accepted"); } catch(InvalidDataException) { }
            var changedReport=DerivedWaistAudit.Inspect(changed,reference);
            Need(!changedReport.WithinDiagnosticBounds && changedReport.MaxControlCm>.01 && changedReport.MaxLengthDifferenceCm>independentReport.MaxLengthDifferenceCm+.00001,"same-endpoint altered control and length must fail diagnostic");
            void Side(IReadOnlyList<InterfaceMatcher.Edge> actual,JsonElement expected)
            {
                var ids=expected.GetProperty("edges").EnumerateArray().ToArray(); Need(actual.Count==ids.Length,"matched edge count");
                for(int i=0;i<ids.Length;i++)
                {
                    var entry=ids[i];var edge=actual[i];string panel=entry.GetProperty("panel").GetString()!;int index=entry.GetProperty("edge").GetInt32();
                    Need(edge.Panel==panel && edge.Flipped==entry.GetProperty("flipped").GetBoolean() && edge.RightWrong==entry.GetProperty("right_wrong").GetBoolean(),"matched edge identity/orientation");
                    var referenceEdge=reference.GetProperty("panels").GetProperty(panel).GetProperty("edges")[index];
                    P a=Point(referenceEdge.GetProperty("start")),b=Point(referenceEdge.GetProperty("end"));
                    Need((a-new P(edge.Start.X,edge.Start.Y)).Length()<2e-5 && (b-new P(edge.End.X,edge.End.Y)).Length()<2e-5,"matched edge coordinates: "+panel+":"+index);
                }
            }
            Side(match.Upper,waist.GetProperty("a"));Side(match.Lower,waist.GetProperty("b"));
            var derivedGraph=SyntheticFullSeamGraph.CreateDerived(id,independent);
            Need(derivedGraph.Rules.Count==23 && derivedGraph.Origin=="CSharpDerived" && derivedGraph.Status=="unresolved" && !derivedGraph.ProductionAllowed && !derivedGraph.ExportAllowed,"derived full graph gate");
            foreach(var rule in derivedGraph.Rules)
            {
                var expectedRule=reference.GetProperty("stitches").EnumerateArray().Single(r=>r.GetProperty("owner").GetString()==rule.Owner && r.GetProperty("owner_type").GetString()==rule.Kind && r.GetProperty("index").GetInt32()==rule.Index);
                SyntheticFullSeamGraph.Compare(rule,expectedRule);
            }
            try { SyntheticFullSeamGraph.CreateDerived(id,changed);throw new Exception("Altered derived graph accepted"); } catch(ArgumentException) { }
            try { SyntheticFullSeamGraph.CreateDerived(id,match);throw new Exception("Source fixture passed as derived"); } catch(ArgumentException) { }
            try { SyntheticFullSeamGraph.Create(id,independent);throw new Exception("Derived passed as source fixture"); } catch(ArgumentException) { }
            var fullGraph=SyntheticFullSeamGraph.Create(id,match);
            Need(fullGraph.Rules.Count==23 && fullGraph.Status=="unresolved" && !fullGraph.ExportAllowed && !fullGraph.ProductionAllowed,"full seam candidate gate");
            var projections=DerivedGraphProjection.Calculate(derivedGraph,independent);
            var typedLengths=BodySeamProjection.Calculate(SyntheticBodyCatalog.Create(id),ResearchDesign.PinnedFitted);
            Need(typedLengths.ReferenceAssessment==BackWaistCandidate.ReferenceState.Unverified && typedLengths.Rules.Count==23 && typedLengths.Status=="unresolved" && !typedLengths.ExportAllowed && !typedLengths.ProductionAllowed,"typed seam length gate");
            foreach(var rule in typedLengths.Rules) {
                var fixedRule=projections.Single(p=>p.Kind==rule.Kind && p.Index==rule.Index && (rule.Kind=="MetaGarment" || p.Owner==rule.Owner));
                Need(Math.Abs(rule.A.ApproximateLower-fixedRule.A.ApproximateLower)<1e-7 && Math.Abs(rule.A.ApproximateUpper-fixedRule.A.ApproximateUpper)<1e-7 && Math.Abs(rule.B.ApproximateLower-fixedRule.B.ApproximateLower)<1e-7 && Math.Abs(rule.B.ApproximateUpper-fixedRule.B.ApproximateUpper)<1e-7,"typed projected lengths differ from C# fixture");
            }
            var waistLengths=typedLengths.Rules.Single(r=>r.Kind=="MetaGarment");
            Console.WriteLine($"{id} represented waist sides cm: A=[{waistLengths.A.ApproximateLower:G12},{waistLengths.A.ApproximateUpper:G12}], B=[{waistLengths.B.ApproximateLower:G12},{waistLengths.B.ApproximateUpper:G12}]");
            var separated=typedLengths.Rules.Where(r=>r.RepresentedIntervalsDisjoint).Select(r=>r.Kind+":"+r.Index).ToArray();
            Console.WriteLine($"{id} represented seam intervals disjoint={separated.Length}: {string.Join(",",separated)}; not a physical fit verdict");
            try { typedLengths.RequireResearchComposition();throw new InvalidDataException("Unverified lengths composed"); } catch(InvalidOperationException) { }

            Need(projections.Count==23 && projections.All(p=>p.Status=="unresolved"&&!p.ExportAllowed),"projected graph gate");
            var projectedSide=projections.Single(p=>p.Kind=="PencilSkirt"&&p.Index==0);
            Console.WriteLine($"{id} skirt side projected bounds: front≈[{projectedSide.A.ApproximateLower:G10},{projectedSide.A.ApproximateUpper:G10}] cm, back≈[{projectedSide.B.ApproximateLower:G10},{projectedSide.B.ApproximateUpper:G10}] cm; disjoint={projectedSide.A.Disjoint(projectedSide.B)} (display rounded, no fit verdict).");
            var alteredRule=derivedGraph.Rules.ToArray();int indexRule=Array.FindIndex(alteredRule,r=>r.Kind=="PencilSkirt"&&r.Index==0);
            var alteredSide=alteredRule[indexRule].B with { Ruffles=Array.AsReadOnly(new[]{new SyntheticFullSeamGraph.Ruffle(0,1,1),new SyntheticFullSeamGraph.Ruffle(1,2,1.04)}) };
            alteredRule[indexRule]=alteredRule[indexRule] with { B=alteredSide };
            var alteredGraph=derivedGraph with { Rules=Array.AsReadOnly(alteredRule) };
            try { DerivedGraphProjection.Calculate(alteredGraph,independent);throw new Exception("Altered derived ruffle graph accepted"); } catch(ArgumentException) { }
            var sourceGraph=reference.GetProperty("stitches").EnumerateArray().ToArray();Need(sourceGraph.Length==23,"source graph rule count");
            var topology=BodyCurveStudy.Calculate(SyntheticBodyCatalog.Create(id),ResearchDesign.PinnedFitted);
            Need(topology.Count==6 && topology.All(t=>t.Closure==BodyCurveStudy.ClosureEvidence.ClosedRepresented && t.UnresolvedEdges.Count==t.Unresolved && t.Pairs==t.Separated+t.EndpointOnly+t.Unresolved && t.Status=="unresolved" && !t.ExportAllowed && !t.ProductionAllowed),"typed curve topology gate");
            Need(topology.All(t=>t.Unresolved==0 && t.NonMonotone==0),"synthetic represented contour classification regression");
            foreach(var t in topology)Console.WriteLine($"{id} {t.Panel} represented curve pairs: separated={t.Separated}, endpoint={t.EndpointOnly}, unresolved={t.Unresolved}, nonmonotone={t.NonMonotone}, unresolved edge IDs={string.Join(";",t.UnresolvedEdges.Select(p=>$"{p.A}-{p.B}"))}");
            var typedGraph=BodySeamStudy.Calculate(SyntheticBodyCatalog.Create(id),ResearchDesign.PinnedFitted);
            Need(typedGraph.ReferenceAssessment==BackWaistCandidate.ReferenceState.Unverified && !typedGraph.ExportAllowed && !typedGraph.ProductionAllowed && typedGraph.Rules.Count==23 && !typeof(SyntheticFullSeamGraph.Graph).IsAssignableFrom(typedGraph.GetType()),"typed seam graph gate");
            foreach(var rule in typedGraph.Rules) {
                var comparable=rule.Kind=="MetaGarment"?rule with { Owner=id }:rule;
                var actual=sourceGraph.Single(r=>r.GetProperty("owner").GetString()==comparable.Owner && r.GetProperty("owner_type").GetString()==comparable.Kind && r.GetProperty("index").GetInt32()==comparable.Index);
                SyntheticFullSeamGraph.Compare(comparable,actual);
            }
            try { typedGraph.RequireResearchComposition();throw new InvalidDataException("Unverified typed graph composed"); } catch(InvalidOperationException) { }
            var typedSide=typedGraph.Rules.Single(r=>r.Kind=="PencilSkirt" && r.Index==0);
            var sourceSideForTyped=sourceGraph.Single(r=>r.GetProperty("owner_type").GetString()=="PencilSkirt" && r.GetProperty("index").GetInt32()==0);
            var alteredTypedSource=JsonNode.Parse(sourceSideForTyped.GetRawText())!;alteredTypedSource["b"]!["ruffle"]![1]!["coeff"]=1;
            using(var invalidTypedSource=JsonDocument.Parse(alteredTypedSource.ToJsonString())) { try { SyntheticFullSeamGraph.Compare(typedSide,invalidTypedSource.RootElement);throw new Exception("Changed typed graph ruffle accepted"); } catch(InvalidDataException) { } }


            foreach(var rule in fullGraph.Rules)
            {
                var actual=sourceGraph.Single(r=>r.GetProperty("owner").GetString()==rule.Owner && r.GetProperty("owner_type").GetString()==rule.Kind && r.GetProperty("index").GetInt32()==rule.Index);
                SyntheticFullSeamGraph.Compare(rule,actual);
            }
            var sourceSide=sourceGraph.Single(r=>r.GetProperty("owner_type").GetString()=="PencilSkirt" && r.GetProperty("index").GetInt32()==0);
            void RejectFullGraph(Action<JsonNode> change)
            {
                var mutated=JsonNode.Parse(sourceSide.GetRawText())!;change(mutated);
                using var wrong=JsonDocument.Parse(mutated.ToJsonString());
                try { SyntheticFullSeamGraph.Compare(fullGraph.Rules.Single(r=>r.Kind=="PencilSkirt" && r.Index==0),wrong.RootElement);throw new Exception("Invalid full seam accepted"); } catch(InvalidDataException) { }
            }
            RejectFullGraph(n=>n["a"]!["edges"]![0]!["edge"]=10);
            RejectFullGraph(n=>n["a"]!["edges"]![1]!["flipped"]=false);
            RejectFullGraph(n=>{ var list=n["a"]!["edges"]!.AsArray();var x=list[0]!.DeepClone();var y=list[1]!.DeepClone();list[0]=y;list[1]=x; });
            var changedRule=JsonNode.Parse(sourceGraph.Single(r=>r.GetProperty("owner_type").GetString()=="PencilSkirt" && r.GetProperty("index").GetInt32()==0).GetRawText())!;
            changedRule["b"]!["ruffle"]![1]!["coeff"]=1;
            using(var corruptRule=JsonDocument.Parse(changedRule.ToJsonString())) { try { SyntheticFullSeamGraph.Compare(fullGraph.Rules.Single(r=>r.Kind=="PencilSkirt" && r.Index==0),corruptRule.RootElement);throw new Exception("Wrong ruffle accepted"); } catch(InvalidDataException) { } }
            var front=FrontSkirtCandidate.CreateFromMatchedInterface(id,match);
            foreach(var edge in front.Edges)
            {
                var original=reference.GetProperty("panels").GetProperty("skirt_front").GetProperty("edges")[edge.Id];
                P a=Point(original.GetProperty("start")),b=Point(original.GetProperty("end"));
                Need((a-new P(edge.Start.X,edge.Start.Y)).Length()<2e-5 && (b-new P(edge.End.X,edge.End.Y)).Length()<2e-5,"matched front contour: "+edge.Id);
            }
            try { BackSkirtCandidate.Create(id=="synthetic-base"?"synthetic-distinct":"synthetic-base",match); throw new InvalidDataException("Wrong snapshot accepted"); } catch(ArgumentException) { }
            var backPath=BackSkirtCandidate.Create(id,match);
            Need(backPath.Edges.Count==19 && backPath.Status=="unresolved" && !backPath.ProductionAllowed && !backPath.ExportAllowed,"back path gate");
            foreach(var edge in backPath.Edges)
            {
                var original=reference.GetProperty("panels").GetProperty("skirt_back").GetProperty("edges")[edge.Id];
                P a=Point(original.GetProperty("start")),b=Point(original.GetProperty("end"));
                Need((a-new P(edge.Start.X,edge.Start.Y)).Length()<2e-5 && (b-new P(edge.End.X,edge.End.Y)).Length()<2e-5,"matched back contour: "+edge.Id);
                if(edge.Kind=="Q")
                {
                    var ctrl=edge.Control ?? throw new InvalidDataException("Missing back control candidate");
                    P source=Point(original.GetProperty("relative_controls")[0]);
                    Need((source-new P(ctrl.X,ctrl.Y)).Length()<.0002,"back tangent candidate");
                }
            }
            var graph=backPath.Edges.Select(e=>new GeneratedGraph.Edge(id,"skirt_back",e.Id,e.Kind,new(e.Start.X,e.Start.Y),new(e.End.X,e.End.Y))).ToArray();
            var status=GeneratedGraph.Inspect(id,graph,[],[]);
            Need(status.Issues.Contains("incomplete-graph") && status.Issues.Any(x=>x.StartsWith("curve-topology-unproven:")),"back graph must remain unresolved");
            Need(match.Status=="unresolved"&&!match.ExportAllowed,"matching gate");
        }
    }
    // Fixture-only diagnostic: the source numbers are recorded binary64
    // lengths, not certified physical lengths and not general user input.
    static void CheckSyntheticSourceSeamLengths(JsonElement reference)
    {
        double Side(JsonElement side) {
            var edges=side.GetProperty("edges").EnumerateArray().ToArray();var coefficients=new double[edges.Length];int covered=0;
            foreach(var part in side.GetProperty("ruffle").EnumerateArray()) {
                int start=part.GetProperty("sec")[0].GetInt32(),end=part.GetProperty("sec")[1].GetInt32();double factor=part.GetProperty("coeff").GetDouble();
                Need(start==covered && end>start && end<=edges.Length && double.IsFinite(factor) && factor>0,"source ruffle coverage");
                for(int i=start;i<end;i++)coefficients[i]=factor;covered=end;
            }
            Need(covered==edges.Length && covered>0,"source ruffle incomplete");double total=0;
            for(int i=0;i<edges.Length;i++) {
                var e=edges[i];string panel=e.GetProperty("panel").GetString()!;int index=e.GetProperty("edge").GetInt32();
                double length=reference.GetProperty("panels").GetProperty(panel).GetProperty("edges")[index].GetProperty("length").GetDouble();
                Need(double.IsFinite(length) && length>0,"source seam length");
                total+=Math.Abs(coefficients[i]-1)<.001?length:length/coefficients[i];
            }
            return total;
        }
        var rules=reference.GetProperty("stitches").EnumerateArray().ToArray();
        var waist=rules.Single(r=>r.GetProperty("owner_type").GetString()=="MetaGarment");
        double upper=Side(waist.GetProperty("a")),lower=Side(waist.GetProperty("b"));
        string id=reference.GetProperty("id").GetString()!;
        double expectedGap=id=="synthetic-base"?.357108754:.315570643;
        Need(Math.Abs((upper-lower)-expectedGap)<.000001,"source waist projected difference");
        var torso=rules.Where(r=>r.GetProperty("owner_type").GetString()=="BodiceHalf").Select(r=>(Owner:r.GetProperty("owner").GetString(),Index:r.GetProperty("index").GetInt32(),Gap:Side(r.GetProperty("a"))-Side(r.GetProperty("b")))).ToArray();
        Need(torso.Length==4 && torso.All(r=>Math.Abs(r.Gap)>.1),"source shoulder/side differences");
        Console.WriteLine($"{id} source projected differences: waist={upper-lower:G8} cm, left shoulder={torso.Single(r=>r.Owner=="left" && r.Index==0).Gap:G8} cm, left side={torso.Single(r=>r.Owner=="left" && r.Index==1).Gap:G8} cm (fixture only)");
    }
    static void CheckGeneralControls(JsonElement reference,IResearchPatternBody body)
    {
        var design=ResearchDesign.PinnedFitted;var panels=reference.GetProperty("panels");
        int count=0;double maxRelative=0,maxAbsolute=0,maxArc=0;
        void Curve(string panel,int index,string kind,double ax,double ay,double bx,double by,IReadOnlyList<P> controls,double? arc)
        {
            var source=panels.GetProperty(panel).GetProperty("edges")[index];string sourceKind=source.GetProperty("kind").GetString()!;
            if(kind=="L") { Need(sourceKind=="Edge","typed source line kind");return; }
            if(kind=="A") { Need(sourceKind=="CircleEdge" && arc.HasValue,"typed source arc kind");double d=Math.Abs(source.GetProperty("control_y").GetDouble()-arc.GetValueOrDefault());maxArc=Math.Max(maxArc,d);Need(d<1e-7,"typed source arc control");count++;return; }
            Need(kind is "Q" or "C" && sourceKind=="CurveEdge","typed source Bezier kind");var refs=source.GetProperty("relative_controls");Need(refs.GetArrayLength()==controls.Count,"typed source control count");
            var a=new P(ax,ay);var b=new P(bx,by);var delta=b-a;var sa=Point(source.GetProperty("start"));var sb=Point(source.GetProperty("end"));var sd=sb-sa;
            for(int j=0;j<controls.Count;j++) { var actual=controls[j];var expected=Point(refs[j]);double relative=(actual-expected).Length();
                var actualAbsolute=a+delta*actual.X+new P(-delta.Y,delta.X)*actual.Y;var sourceAbsolute=sa+sd*expected.X+new P(-sd.Y,sd.X)*expected.Y;
                maxRelative=Math.Max(maxRelative,relative);maxAbsolute=Math.Max(maxAbsolute,(actualAbsolute-sourceAbsolute).Length());
                Need(relative<.0002,"typed source Bezier control: "+panel+":"+index);count++;
            }
        }
        void Front(IReadOnlyList<FrontBodiceCandidate.Edge> edges,string panel) { foreach(var e in edges)Curve(panel,e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>new P(c.X,c.Y)).ToArray(),e.ArcControlY); }
        void Back(IReadOnlyList<BackBodiceCandidate.Edge> edges,string panel) { foreach(var e in edges)Curve(panel,e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>new P(c.X,c.Y)).ToArray(),e.ArcControlY); }
        Front(FrontBodiceCandidate.CreateFromBody(body,design).Edges,"right_ftorso");Front(FrontBodiceCandidate.CreateLeftFromBody(body,design).Edges,"left_ftorso");
        Back(BackBodiceCandidate.CreateFromBody(body,design,false).Edges,"right_btorso");Back(BackBodiceCandidate.CreateFromBody(body,design,true).Edges,"left_btorso");
        var frontSkirt=FrontSkirtCandidate.CreateFromMatchedBody(body,design);
        foreach(var e in frontSkirt.Edges)Curve("skirt_front",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Control is { } c ? [new P(c.X,c.Y)] : [],null);
        var backSkirt=BackSkirtCandidate.CreateFromBody(body,design);
        foreach(var e in backSkirt.Edges)Curve("skirt_back",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Control is { } c ? [new P(c.X,c.Y)] : [],null);
        Need(count==22,"typed curve control coverage");
        Console.WriteLine($"{reference.GetProperty("id").GetString()} general source controls: count={count}, relative max={maxRelative:G7}, absolute max={maxAbsolute:G7} cm, arc max={maxArc:G7}");
        // Same endpoints with a changed control must be rejected, not treated as equivalent.
        var changed=frontSkirt.Edges.First(e=>e.Kind=="Q");var ctrl=changed.Control!.Value;
        try { Curve("skirt_front",changed.Id,changed.Kind,changed.Start.X,changed.Start.Y,changed.End.X,changed.End.Y,[new P(ctrl.X+.01,ctrl.Y)],null);throw new InvalidDataException("Changed typed control accepted"); }
        catch(InvalidDataException e) when(e.Message.StartsWith("typed source Bezier control")) { }
    }
    static bool ExactDecimalEqual(JsonElement value,decimal expected)
    {
        if(value.ValueKind!=JsonValueKind.Number || expected!=decimal.Truncate(expected)) return false;
        string raw=value.GetRawText(); if(raw.Length>256) return false;
        int expPos=raw.IndexOfAny(['e','E']); string mantissa=expPos<0?raw:raw[..expPos];
        int exponent=0;
        if(expPos>=0 && (!int.TryParse(raw[(expPos+1)..],NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out exponent) || Math.Abs((long)exponent)>256)) return false;
        int dot=mantissa.IndexOf('.'); int fractional=dot<0?0:mantissa.Length-dot-1;
        string digits=dot<0?mantissa:mantissa.Remove(dot,1);
        if(!BigInteger.TryParse(digits,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var integer)) return false;
        int power=exponent-fractional; var target=new BigInteger(expected);
        return power>=0 ? integer*BigInteger.Pow(10,power)==target : integer==target*BigInteger.Pow(10,-power);
    }
    static void VerifySeams(string id,JsonElement actual)
    {
        using var stream=typeof(Validator).Assembly.GetManifestResourceStream("FittedCheck.expected-seams.json") ?? throw new InvalidDataException("Missing seam contract");
        using var reader=new StreamReader(stream,Encoding.UTF8,true); string text=reader.ReadToEnd().Trim();
        string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        Need(hash=="2C415A46FB25441142BCC7F0EF078CE227F9B9075F86ACD262D436CFC1F25976","seam contract hash mismatch");
        var expected=JsonNode.Parse(text)?[id] ?? throw new InvalidDataException("Missing seam contract case");
        Need(JsonNode.DeepEquals(expected,JsonNode.Parse(actual.GetRawText())),"source seam identity mismatch");
    }
    readonly record struct P(double X,double Y)
    {
        public static P operator +(P a,P b)=>new(a.X+b.X,a.Y+b.Y);
        public static P operator -(P a,P b)=>new(a.X-b.X,a.Y-b.Y);
        public static P operator *(P a,double t)=>new(a.X*t,a.Y*t);
        public double Length()=>Math.Sqrt(X*X+Y*Y);
    }
    static P Point(JsonElement e) { var p=new P(e[0].GetDouble(),e[1].GetDouble()); if(!double.IsFinite(p.X)||!double.IsFinite(p.Y)||Math.Abs(p.X)>10000||Math.Abs(p.Y)>10000) throw new InvalidDataException("Coordinate invalid"); return p; }
    static void Need(bool value,string error) { if(!value) throw new InvalidDataException(error); }
    static P Eval(JsonElement edge,double t)
    {
        P a=Point(edge.GetProperty("start")),b=Point(edge.GetProperty("end")); string kind=edge.GetProperty("kind").GetString()!;
        if(kind=="Edge") return a*(1-t)+b*t;
        if(kind=="CurveEdge")
        {
            var controls=edge.GetProperty("relative_controls").EnumerateArray().Select(Point).ToArray(); Need(controls.Length is 1 or 2,"control count");
            P d=b-a; P Absolute(P c)=>a+d*c.X+new P(-d.Y,d.X)*c.Y;
            P c1=Absolute(controls[0]); double u=1-t;
            return controls.Length==1 ? a*(u*u)+c1*(2*u*t)+b*(t*t) : a*(u*u*u)+c1*(3*u*u*t)+Absolute(controls[1])*(3*u*t*t)+b*(t*t*t);
        }
        if(kind=="CircleEdge")
        {
            P d=b-a; double cy=edge.GetProperty("control_y").GetDouble(); Need(double.IsFinite(cy)&&Math.Abs(cy)>1e-12,"degenerate arc");
            P mid=a+d*.5+new P(-d.Y,d.X)*cy;
            double cross=2*(a.X*(mid.Y-b.Y)+mid.X*(b.Y-a.Y)+b.X*(a.Y-mid.Y)); Need(Math.Abs(cross)>1e-12,"arc degeneracy");
            double aa=a.X*a.X+a.Y*a.Y,mm=mid.X*mid.X+mid.Y*mid.Y,bb=b.X*b.X+b.Y*b.Y;
            P center=new((aa*(mid.Y-b.Y)+mm*(b.Y-a.Y)+bb*(a.Y-mid.Y))/cross,(aa*(b.X-mid.X)+mm*(a.X-b.X)+bb*(mid.X-a.X))/cross);
            double a0=Math.Atan2(a.Y-center.Y,a.X-center.X),am=Math.Atan2(mid.Y-center.Y,mid.X-center.X),a1=Math.Atan2(b.Y-center.Y,b.X-center.X);
            double Positive(double x) { x%=2*Math.PI; return x<0?x+2*Math.PI:x; }
            double ccw=Positive(a1-a0), middle=Positive(am-a0); double sweep=middle<=ccw?ccw:ccw-2*Math.PI;
            double angle=a0+sweep*t,r=(a-center).Length(); return center+new P(Math.Cos(angle)*r,Math.Sin(angle)*r);
        }
        throw new InvalidDataException("Unsupported edge "+kind);
    }
    internal static (int cases,int panels,int edges,int rules) Check(JsonElement root)
    {
        Need(root.GetProperty("source_commit").GetString()=="d449629979028123a5c4dc9e732a2ec19b7fce31" && root.GetProperty("unit").GetString()=="cm","fixture identity");
        int count=0,panelCount=0,edgeCount=0,ruleCount=0; var names=new HashSet<string>();
        foreach(var item in root.GetProperty("cases").EnumerateArray())
        {
            count++; string id=item.GetProperty("id").GetString()!; Need((id=="synthetic-base"||id=="synthetic-distinct")&&names.Add(id),"fixture id");
            var expected=new Dictionary<string,decimal> { ["arm_length"]=54,["arm_pose_angle"]=45,["armscye_depth"]=13,["back_width"]=48,["bum_points"]=18,["bust"]=100,["bust_line"]=26,["bust_points"]=17,["crotch_hip_diff"]=9,["head_l"]=26,["height"]=172,["hip_back_width"]=55,["hip_inclination"]=10,["hips"]=104,["hips_line"]=23,["leg_circ"]=60,["neck_w"]=19,["shoulder_incl"]=22,["shoulder_w"]=36,["underbust"]=86,["vert_bust_line"]=21,["waist"]=80,["waist_back_width"]=36,["waist_line"]=37,["waist_over_bust_line"]=41,["wrist"]=17 };
            if(id=="synthetic-distinct") { expected["bust"]=96; expected["back_width"]=43; expected["waist"]=74; expected["waist_back_width"]=34; expected["hips"]=102; expected["hip_back_width"]=54; expected["waist_line"]=39; expected["waist_over_bust_line"]=42; }
            var body=item.GetProperty("body_input_cm_degree"); Need(body.EnumerateObject().Count()==expected.Count,"body field count");
            foreach(var pair in expected) Need(body.TryGetProperty(pair.Key,out var value)&&ExactDecimalEqual(value,pair.Value),"synthetic body mismatch: "+pair.Key);
            var expectedBinary=expected.ToDictionary(p=>p.Key,p=>(double)p.Value);
            var derived=item.GetProperty("derived");
            var predicted=new Dictionary<string,double> { ["_waist_level"]=expectedBinary["height"]-expectedBinary["head_l"]-expectedBinary["waist_line"], ["_leg_length"]=expectedBinary["height"]-expectedBinary["head_l"]-expectedBinary["waist_line"]-expectedBinary["hips_line"], ["_base_sleeve_balance"]=expectedBinary["shoulder_w"]-2, ["_bust_line"]=expectedBinary["vert_bust_line"]*2/3+expectedBinary["bust_line"]/3, ["_hip_inclination"]=expectedBinary["hip_inclination"]/2, ["_shoulder_incl"]=expectedBinary["shoulder_incl"], ["_armscye_depth"]=expectedBinary["armscye_depth"]+2.5 };
            Need(derived.EnumerateObject().Count()==predicted.Count,"derived field count");
            foreach(var pair in predicted) Need(derived.TryGetProperty(pair.Key,out var value)&&Math.Abs(value.GetDouble()-pair.Value)<1e-10,"derived mismatch: "+pair.Key);
            VerifySeams(id,item.GetProperty("stitches"));
            foreach(bool left in new[]{false,true})
            {
                var path=FrontBodiceCandidate.Create(id,left);
                Need(path.Edges.Count==12 && path.Darts.Count==2 && path.Status=="unresolved" && !path.ProductionAllowed && !path.ExportAllowed,"front body candidate gate");
                foreach(var edge in path.Edges)
                {
                    var source=item.GetProperty("panels").GetProperty(path.Panel).GetProperty("edges")[edge.Id];
                    P a=Point(source.GetProperty("start")),b=Point(source.GetProperty("end"));
                    Need((a-new P(edge.Start.X,edge.Start.Y)).Length()<1e-5 && (b-new P(edge.End.X,edge.End.Y)).Length()<1e-5,"front body candidate: "+path.Panel+":"+edge.Id);
                    string sourceKind=source.GetProperty("kind").GetString()!;
                    Need((edge.Kind,sourceKind) is ("L","Edge") or ("A","CircleEdge") or ("C","CurveEdge"),"front body edge kind");
                    if(edge.Kind=="A") Need(Math.Abs(source.GetProperty("control_y").GetDouble()-edge.ArcControlY!.Value)<1e-7,"front body arc");
                    if(edge.Kind=="C") { var cp=source.GetProperty("relative_controls"); Need(cp.GetArrayLength()==2 && (Point(cp[0])-new P(edge.Controls[0].X,edge.Controls[0].Y)).Length()<1e-12 && (Point(cp[1])-new P(edge.Controls[1].X,edge.Controls[1].Y)).Length()<1e-12,"front body cubic"); }
                }
                var graph=path.Edges.Select(e=>new GeneratedGraph.Edge(id,path.Panel,e.Id,e.Kind,new(e.Start.X,e.Start.Y),new(e.End.X,e.End.Y))).ToArray();
                var actualDarts=path.Darts.Select(d=>new GeneratedGraph.Seam(id,path.Panel,d.EdgeA,d.FlipA,path.Panel,d.EdgeB,d.FlipB)).ToArray();
                var expectedPanelDarts=item.GetProperty("stitches").EnumerateArray().Where(r=>r.GetProperty("owner").GetString()==path.Panel && r.GetProperty("owner_type").GetString()=="BodiceFrontHalf").OrderBy(r=>r.GetProperty("index").GetInt32()).Select(r=>new GeneratedGraph.Seam(id,path.Panel,r.GetProperty("a").GetProperty("edges")[0].GetProperty("edge").GetInt32(),r.GetProperty("a").GetProperty("edges")[0].GetProperty("flipped").GetBoolean(),path.Panel,r.GetProperty("b").GetProperty("edges")[0].GetProperty("edge").GetInt32(),r.GetProperty("b").GetProperty("edges")[0].GetProperty("flipped").GetBoolean())).ToArray();
                var diagnostic=GeneratedGraph.Inspect(id,graph,actualDarts,expectedPanelDarts);
                Need(actualDarts.Length==2 && diagnostic.Issues.Any(x=>x.StartsWith("curve-topology-unproven:")) && !diagnostic.Issues.Any(x=>x.StartsWith("open-contour:")) && !diagnostic.Issues.Contains("seam-identity-or-direction-mismatch") && !diagnostic.ExportAllowed,"front body topology remains unresolved");
            }
            var armholes=SyntheticArmholeCut.Create(id);
            var typedArm=SyntheticArmholeCut.CreateFromBody(SyntheticBodyCatalog.Create(id),ResearchDesign.PinnedFitted);
            Need(!typeof(SyntheticArmholeCut.Result).IsAssignableFrom(typedArm.GetType()) && typedArm.ReferenceAssessment==BackWaistCandidate.ReferenceState.Unverified && typedArm.Curves.Count==2 && !typedArm.ExportAllowed && !typedArm.ProductionAllowed,"typed armhole gate");
            var fixedFront=armholes.Curves.Where(c=>c.Panel.EndsWith("_ftorso",StringComparison.Ordinal)).ToArray();
            Need(typedArm.Curves.Zip(fixedFront).All(pair=>pair.First.Panel==pair.Second.Panel && pair.First.Index==pair.Second.Index && (pair.First.Start-pair.Second.Start).Norm()<1e-10 && (pair.First.End-pair.Second.End).Norm()<1e-10 && pair.First.Control1==pair.Second.Control1 && pair.First.Control2==pair.Second.Control2),"typed front armhole differs from source fixture calculation");
            try { typedArm.RequireResearchComposition();throw new InvalidDataException("Unverified armhole composed"); } catch(InvalidOperationException) { }
            foreach(var curve in armholes.Curves)
            {
                var source=item.GetProperty("panels").GetProperty(curve.Panel).GetProperty("edges")[curve.Index];
                P a=Point(source.GetProperty("start")),b=Point(source.GetProperty("end"));
                var controls=source.GetProperty("relative_controls"); P c1=Point(controls[0]),c2=Point(controls[1]);
                Need(source.GetProperty("kind").GetString()=="CurveEdge" && (a-new P(curve.Start.X,curve.Start.Y)).Length()<1e-5 && (b-new P(curve.End.X,curve.End.Y)).Length()<1e-5 && (c1-new P(curve.Control1.X,curve.Control1.Y)).Length()<1e-12 && (c2-new P(curve.Control2.X,curve.Control2.Y)).Length()<1e-12,"source armhole: "+curve.Panel);
            }
            Need(armholes.Status=="unresolved"&&!armholes.ProductionAllowed&&!armholes.ExportAllowed,"armhole gate");
            foreach(bool left in new[]{false,true})
            {
                var path=BackBodiceCandidate.Create(id,left);var refEdges=item.GetProperty("panels").GetProperty(path.Panel).GetProperty("edges");
                Need(path.Edges.Count==13 && path.Status=="unresolved"&&!path.ProductionAllowed&&!path.ExportAllowed,"back final candidate gate");
                foreach(var edge in path.Edges)
                {
                    var source=refEdges[edge.Id];P a=Point(source.GetProperty("start")),b=Point(source.GetProperty("end"));
                    Need((a-new P(edge.Start.X,edge.Start.Y)).Length()<.001 && (b-new P(edge.End.X,edge.End.Y)).Length()<.001,"back final candidate endpoint: "+path.Panel+":"+edge.Id);
                    string sourceKind=source.GetProperty("kind").GetString()!;
                    Need((edge.Kind,sourceKind) is ("L","Edge") or ("A","CircleEdge") or ("Q","CurveEdge") or ("C","CurveEdge"),"back final candidate kind");
                    if(edge.Kind=="A")Need(Math.Abs(source.GetProperty("control_y").GetDouble()-edge.ArcControlY!.Value)<1e-7,"back final arc");
                    if(edge.Kind is "Q" or "C")
                    {
                        var controls=source.GetProperty("relative_controls"); Need(controls.GetArrayLength()==edge.Controls.Count,"back final control count");
                        for(int i=0;i<edge.Controls.Count;i++) Need((Point(controls[i])-new P(edge.Controls[i].X,edge.Controls[i].Y)).Length()<(edge.Kind=="Q"?.0002:1e-12),"back final control");
                    }
                }
                var graph=path.Edges.Select(e=>new GeneratedGraph.Edge(id,path.Panel,e.Id,e.Kind,new(e.Start.X,e.Start.Y),new(e.End.X,e.End.Y))).ToArray();
                var diagnostic=GeneratedGraph.Inspect(id,graph,[],[]);
                Need(diagnostic.Issues.Contains("incomplete-graph") && !diagnostic.Issues.Any(x=>x.StartsWith("open-contour:")) && diagnostic.Issues.Any(x=>x.StartsWith("curve-topology-unproven:")) && !diagnostic.ExportAllowed,"back final topology gate");
            }
            var backNeck=BackNeckCut.Create(id);
            foreach(var pair in new[]{("right_btorso",11,backNeck.Right),("left_btorso",1,backNeck.Left)})
            {
                var source=item.GetProperty("panels").GetProperty(pair.Item1).GetProperty("edges")[pair.Item2];
                P a=Point(source.GetProperty("start")),b=Point(source.GetProperty("end"));
                Need(source.GetProperty("kind").GetString()=="CircleEdge" && (a-new P(pair.Item3.Start.X,pair.Item3.Start.Y)).Length()<1e-5 && (b-new P(pair.Item3.End.X,pair.Item3.End.Y)).Length()<1e-5 && Math.Abs(source.GetProperty("control_y").GetDouble()-pair.Item3.RelativeControlY)<1e-7,"back neck arc: "+pair.Item1);
            }
            var backCenter=item.GetProperty("panels").GetProperty("right_btorso").GetProperty("edges")[12];
            Need((Point(backCenter.GetProperty("start"))-new P(backNeck.Right.End.X,backNeck.Right.End.Y)).Length()<1e-5 && (Point(backCenter.GetProperty("end"))-new P(backNeck.CenterWaist.X,backNeck.CenterWaist.Y)).Length()<1e-5,"back center line");
            Need(backNeck.Status=="unresolved"&&!backNeck.ProductionAllowed&&!backNeck.ExportAllowed,"back neck gate");
            var neck=FrontNeckCut.Create(id);
            var typedNeck=FrontNeckCut.CreateFromBody(SyntheticBodyCatalog.Create(id),ResearchDesign.PinnedFitted);
            Need(!typeof(FrontNeckCut.Result).IsAssignableFrom(typedNeck.GetType()) && typedNeck.ReferenceAssessment==BackWaistCandidate.ReferenceState.Unverified && !typedNeck.ExportAllowed && !typedNeck.ProductionAllowed,"typed neck gate");
            Need(typedNeck.Right==neck.Right && typedNeck.Left==neck.Left,"typed neck mismatch");
            try { typedNeck.RequireResearchComposition();throw new InvalidDataException("Unverified neck composed"); } catch(InvalidOperationException) { }
            foreach(var pair in new[]{("right_ftorso",1,neck.Right),("left_ftorso",10,neck.Left)})
            {
                var source=item.GetProperty("panels").GetProperty(pair.Item1).GetProperty("edges")[pair.Item2];
                var a=Point(source.GetProperty("start"));var b=Point(source.GetProperty("end"));
                Need(source.GetProperty("kind").GetString()=="CircleEdge" && (a-new P(pair.Item3.Start.X,pair.Item3.Start.Y)).Length()<1e-5 && (b-new P(pair.Item3.End.X,pair.Item3.End.Y)).Length()<1e-5 && Math.Abs(source.GetProperty("control_y").GetDouble()-pair.Item3.RelativeControlY)<1e-7,"front neck arc");
            }
            var rightCenter=item.GetProperty("panels").GetProperty("right_ftorso").GetProperty("edges")[0];
            var leftCenter=item.GetProperty("panels").GetProperty("left_ftorso").GetProperty("edges")[11];
            Need((Point(rightCenter.GetProperty("start"))-new P(0,0)).Length()<1e-8 && (Point(rightCenter.GetProperty("end"))-new P(neck.Right.Start.X,neck.Right.Start.Y)).Length()<1e-5 && (Point(leftCenter.GetProperty("start"))-new P(neck.Left.End.X,neck.Left.End.Y)).Length()<1e-5 && (Point(leftCenter.GetProperty("end"))-new P(0,0)).Length()<1e-8,"neck center lines");
            Need(neck.Status=="unresolved"&&!neck.ProductionAllowed&&!neck.ExportAllowed,"neck gate");
            var upperGraph=UpperSeamGraph.Create(id);
            Need(upperGraph.Rules.Count==6 && upperGraph.Snapshot==id && upperGraph.Status=="unresolved" && !upperGraph.ExportAllowed && !upperGraph.ProductionAllowed,"upper seam graph gate");
            var seamLengths=UpperSeamGraph.Measure(upperGraph);
            Need(seamLengths.Count==6 && seamLengths.All(x=>x.Status=="unresolved"),"upper seam length gate");
            foreach(var rule in upperGraph.Rules)
            {
                var source=item.GetProperty("stitches").EnumerateArray().Single(r=>r.GetProperty("owner").GetString()==rule.Owner && r.GetProperty("owner_type").GetString()==rule.OwnerType && r.GetProperty("index").GetInt32()==rule.Index);
                UpperSeamGraph.Compare(rule,source);
                var length=seamLengths.Single(x=>x.Owner==rule.Owner && x.Index==rule.Index);
                double SourceLength(string side)=>source.GetProperty(side).GetProperty("edges").EnumerateArray().Sum(e=>item.GetProperty("panels").GetProperty(e.GetProperty("panel").GetString()!).GetProperty("edges")[e.GetProperty("edge").GetInt32()].GetProperty("length").GetDouble());
                Need(Math.Abs(length.SideA-SourceLength("a"))<.002 && Math.Abs(length.SideB-SourceLength("b"))<.002,"upper seam candidate lengths");
            }
            var generated=PartialFittedEngine.Create(id);
            var dartRules=PartialDartRelations.Create(id);
            var expectedDarts=item.GetProperty("stitches").EnumerateArray().Where(r=>r.GetProperty("owner_type").GetString() is "BodiceFrontHalf" or "FittedSkirtPanel").ToArray();
            Need(dartRules.Count==10 && expectedDarts.Length==10,"partial dart rule count");
            foreach(var rule in dartRules)
            {
                var source=expectedDarts.Single(r=>r.GetProperty("owner").GetString()==rule.Owner && r.GetProperty("index").GetInt32()==rule.SourceRule);
                var a=source.GetProperty("a").GetProperty("edges")[0]; var b=source.GetProperty("b").GetProperty("edges")[0];
                Need(rule.Snapshot==id && a.GetProperty("panel").GetString()==rule.Owner && b.GetProperty("panel").GetString()==rule.Owner && a.GetProperty("edge").GetInt32()==rule.EdgeA && b.GetProperty("edge").GetInt32()==rule.EdgeB && a.GetProperty("flipped").GetBoolean()==rule.FlipA && b.GetProperty("flipped").GetBoolean()==rule.FlipB,"source dart seam relation");
                DartRuleCheck.Validate(rule,source,generated.Segments);
                var sourceA=item.GetProperty("panels").GetProperty(rule.Owner).GetProperty("edges")[rule.EdgeA];
                var sourceB=item.GetProperty("panels").GetProperty(rule.Owner).GetProperty("edges")[rule.EdgeB];
                P sourceApexA=Point(sourceA.GetProperty(rule.ApexAtStartA?"start":"end"));
                P sourceApexB=Point(sourceB.GetProperty(rule.ApexAtStartB?"start":"end"));
                Need((sourceApexA-sourceApexB).Length()<1e-5,"source designated dart apex");
            }
            Need(generated.Status=="unresolved" && !generated.ProductionAllowed && !generated.ExportAllowed,"partial output gate");
            var frontPath=FrontSkirtCandidate.Create(id);
            Need(frontPath.Edges.Count==13 && frontPath.Status=="unresolved" && !frontPath.ProductionAllowed && !frontPath.ExportAllowed,"front path gate");
            var panels=item.GetProperty("panels");
            var frontReference=panels.GetProperty("skirt_front").GetProperty("edges");
            int different=0;
            foreach(var candidateEdge in frontPath.Edges)
            {
                var reference=frontReference[candidateEdge.Id];
                var refStart=Point(reference.GetProperty("start")); var refEnd=Point(reference.GetProperty("end"));
                double err=Math.Max((refStart-new P(candidateEdge.Start.X,candidateEdge.Start.Y)).Length(),(refEnd-new P(candidateEdge.End.X,candidateEdge.End.Y)).Length());
                if(err>1e-5) { different++; Need(candidateEdge.Id is 6 or 7 && err<0.12,"unexpected front candidate mismatch"); }
                if(candidateEdge.Kind=="Q")
                {
                    Need(reference.GetProperty("kind").GetString()=="CurveEdge" && candidateEdge.Control is not null,"curve role");
                    var ctl=Point(reference.GetProperty("relative_controls")[0]); var cand=candidateEdge.Control ?? throw new InvalidDataException("missing candidate control");
                    Need((ctl-new P(cand.X,cand.Y)).Length()<0.0002,"candidate curve control mismatch");
                }
                else Need(reference.GetProperty("kind").GetString()=="Edge","line role");
            }
            Need(different==2,"expected unresolved interface split discrepancy");
            var frontGraph=frontPath.Edges.Select(e=>new GeneratedGraph.Edge(id,"skirt_front",e.Id,e.Kind,new(e.Start.X,e.Start.Y),new(e.End.X,e.End.Y))).ToArray();
            var frontVerdict=GeneratedGraph.Inspect(id,frontGraph,[],[]);
            Need(frontVerdict.Issues.Contains("incomplete-graph") && frontVerdict.Issues.Any(x=>x.StartsWith("curve-topology-unproven:")) && !frontVerdict.ExportAllowed,"front path remains unresolved");
            foreach(var segment in generated.Segments)
            {
                var actual=panels.GetProperty(segment.Panel).GetProperty("edges")[segment.Edge];
                string expectedKind=segment.SourceKind=="CurveEdgeEndpointsOnly"?"CurveEdge":segment.SourceKind;
                Need(actual.GetProperty("kind").GetString()==expectedKind,"partial source kind");
                if(segment.SourceKind=="CurveEdgeEndpointsOnly")
                {
                    bool atStart=(segment.Panel=="skirt_front" && segment.Edge==2)||(segment.Panel=="skirt_back" && segment.Edge==1);
                    var c=TangentCandidate.RelativeControl(new(segment.Start.X,segment.Start.Y),new(segment.End.X,segment.End.Y),atStart);
                    var expectedControl=Point(actual.GetProperty("relative_controls")[0]);
                    Need((new P(c.X,c.Y)-expectedControl).Length()<0.0002,"analytic tangent candidate mismatch");
                }
                P start=Point(actual.GetProperty("start")),end=Point(actual.GetProperty("end"));
                Need((start-new P(segment.Start.X,segment.Start.Y)).Length()<1e-5 && (end-new P(segment.End.X,segment.End.Y)).Length()<1e-5,"partial regenerated segment mismatch: "+segment.Panel+":"+segment.Edge);
            } var indexed=new Dictionary<string,JsonElement[]>();
            foreach(var panel in panels.EnumerateObject())
            {
                panelCount++; var edges=panel.Value.GetProperty("edges").EnumerateArray().ToArray(); Need(edges.Length>2,"empty panel"); indexed.Add(panel.Name,edges);
                for(int i=0;i<edges.Length;i++)
                {
                    edgeCount++; var edge=edges[i]; var end=Point(edge.GetProperty("end")); var next=Point(edges[(i+1)%edges.Length].GetProperty("start")); Need((end-next).Length()<1e-8,"open contour");
                    var samples=edge.GetProperty("samples"); Need(samples.GetArrayLength()==3,"sample count");
                    for(int j=0;j<3;j++) Need((Eval(edge,(j+1)*.25)-Point(samples[j])).Length()<1e-7,"curve sample mismatch");
                    double length=edge.GetProperty("length").GetDouble(); Need(double.IsFinite(length)&&length>0,"length invalid");
                    double sum=0; P previous=Eval(edge,0); const int steps=2048;
                    for(int j=1;j<=steps;j++) { P current=Eval(edge,(double)j/steps);sum+=(current-previous).Length();previous=current; }
                    Need(Math.Abs(sum-length)<1e-4,"length mismatch");
                }
            }
            foreach(var rule in item.GetProperty("stitches").EnumerateArray())
            {
                ruleCount++; Need(!string.IsNullOrEmpty(rule.GetProperty("owner").GetString()),"owner missing");
                foreach(string sideName in new[]{"a","b"})
                {
                    var side=rule.GetProperty(sideName); var refs=side.GetProperty("edges").EnumerateArray().ToArray(); Need(refs.Length>0,"empty stitch side");
                    foreach(var r in refs) { string panel=r.GetProperty("panel").GetString()!; int index=r.GetProperty("edge").GetInt32(); Need(index>=0&&indexed.TryGetValue(panel,out var edges)&&index<edges.Length,"dangling stitch"); _=r.GetProperty("flipped").GetBoolean(); _=r.GetProperty("right_wrong").GetBoolean(); }
                    int end=0; foreach(var part in side.GetProperty("ruffle").EnumerateArray()) { double coef=part.GetProperty("coeff").GetDouble(); var range=part.GetProperty("sec"); Need(double.IsFinite(coef)&&coef>0&&range[0].GetInt32()==end,"invalid ruffle"); end=range[1].GetInt32(); Need(end<=refs.Length,"ruffle range"); } Need(end==refs.Length,"ruffle incomplete");
                }
            }
            var rules=item.GetProperty("stitches").EnumerateArray().ToArray();
            Need(indexed.Count==6 && indexed.Values.Sum(x=>x.Length)==82 && rules.Length==23,"unexpected source coverage");
            var ownerCounts=new Dictionary<string,int> { ["BodiceBackHalf"]=4,["BodiceFrontHalf"]=4,["BodiceHalf"]=4,["FittedShirt"]=2,["FittedSkirtPanel"]=6,["MetaGarment"]=1,["PencilSkirt"]=2 };
            foreach(var entry in ownerCounts) Need(rules.Count(r=>r.GetProperty("owner_type").GetString()==entry.Key)==entry.Value,"missing source rules: "+entry.Key);
            var waist=rules.Single(r=>r.GetProperty("owner_type").GetString()=="MetaGarment");
            var upper=waist.GetProperty("a").GetProperty("edges").EnumerateArray().ToArray(); var lower=waist.GetProperty("b").GetProperty("edges").EnumerateArray().ToArray();
            Need(upper.Length==10 && lower.Length==10 && upper.All(e=>e.GetProperty("panel").GetString()!.Contains("torso")) && lower.All(e=>e.GetProperty("panel").GetString()!.StartsWith("skirt_")),"waist source relation");
        }
        Need(count==2,"fixture cases"); return(count,panelCount,edgeCount,ruleCount);
    }
}

// Adversarial test only: an arbitrary interface implementation must not bypass validation.
internal sealed class ForgedPatternBody : IResearchPatternBody
{
    public decimal this[string key]=>1;
    public ResearchBody.DerivedValues Derive()=>throw new InvalidOperationException();
    public string Snapshot(ResearchDesign design)=>"forged";
}
