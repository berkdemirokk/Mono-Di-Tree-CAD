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
    using var stream=typeof(Validator).Assembly.GetManifestResourceStream("FittedCheck.golden.json")!;
    using var reader=new StreamReader(stream); string content=(await reader.ReadToEndAsync()).TrimStart('\uFEFF');
    using var data=JsonDocument.Parse(content); var result=Validator.Check(data.RootElement); Validator.CheckInterfaceMatching(data.RootElement); Validator.CheckPrecutBodice();
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
    var unequalLegs=baseParts.Segments.ToArray(); int legIndex=Array.FindIndex(unequalLegs,e=>e.Panel==firstRule.Owner && e.Edge==firstRule.EdgeA);
    unequalLegs[legIndex]=unequalLegs[legIndex] with { Start=new(unequalLegs[legIndex].Start.X+.1,unequalLegs[legIndex].Start.Y) };
    try { DartRuleCheck.Validate(firstRule,sourceRule,unequalLegs); throw new Exception("Unequal dart legs accepted"); } catch(InvalidDataException) { }
    Console.WriteLine($"PASS: {result.cases} synthetic cases; {result.panels} panels, {result.edges} edges, {result.rules} nested stitch rules; 68 reconstructed lines, 8 curve endpoint pairs and synthetic waist interface matching; production unresolved.");
    return 0;
}
catch(Exception e) { Console.Error.WriteLine("FAIL: "+e.Message); return 1; }

internal static class Validator
{
    // Compare the mathematical JSON number before any decimal or binary64 rounding.
    // Expected values for this fixed fixture are integers; equivalent exponent
    // and trailing-zero representations remain acceptable.
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


























