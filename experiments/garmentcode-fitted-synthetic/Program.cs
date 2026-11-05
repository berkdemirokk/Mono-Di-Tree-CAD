using System.Text.Json;
using System.Text.Json.Nodes;

if(args.Length!=1 || args[0]!="--verify") return 2;
try
{
    using var stream=typeof(Validator).Assembly.GetManifestResourceStream("FittedCheck.golden.json")!;
    using var reader=new StreamReader(stream); string content=(await reader.ReadToEndAsync()).TrimStart('\uFEFF');
    using var data=JsonDocument.Parse(content); var result=Validator.Check(data.RootElement);
    var changed=JsonNode.Parse(content)!; changed["cases"]![0]!["panels"]!.AsObject().First().Value!["edges"]![0]!["samples"]![0]![0]=12345;
    using var corrupt=JsonDocument.Parse(changed.ToJsonString());
    try { Validator.Check(corrupt.RootElement); throw new Exception("Tampered curve accepted"); } catch(InvalidDataException) { }
    var badLink=JsonNode.Parse(content)!; badLink["cases"]![0]!["stitches"]![0]!["a"]!["edges"]![0]!["edge"]=99999;
    using(var invalidLink=JsonDocument.Parse(badLink.ToJsonString())) { try { Validator.Check(invalidLink.RootElement); throw new Exception("Dangling stitch accepted"); } catch(InvalidDataException) { } }
    var badBody=JsonNode.Parse(content)!; badBody["cases"]![0]!["body_input_cm_degree"]!["waist"]=81;
    using(var invalidBody=JsonDocument.Parse(badBody.ToJsonString())) { try { Validator.Check(invalidBody.RootElement); throw new Exception("Changed body accepted"); } catch(InvalidDataException) { } }
    Console.WriteLine($"PASS: {result.cases} synthetic cases; {result.panels} panels, {result.edges} edges, {result.rules} nested stitch rules; production unresolved.");
    return 0;
}
catch(Exception e) { Console.Error.WriteLine("FAIL: "+e.Message); return 1; }

internal static class Validator
{
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
            var expected=new Dictionary<string,double> { ["arm_length"]=54,["arm_pose_angle"]=45,["armscye_depth"]=13,["back_width"]=48,["bum_points"]=18,["bust"]=100,["bust_line"]=26,["bust_points"]=17,["crotch_hip_diff"]=9,["head_l"]=26,["height"]=172,["hip_back_width"]=55,["hip_inclination"]=10,["hips"]=104,["hips_line"]=23,["leg_circ"]=60,["neck_w"]=19,["shoulder_incl"]=22,["shoulder_w"]=36,["underbust"]=86,["vert_bust_line"]=21,["waist"]=80,["waist_back_width"]=36,["waist_line"]=37,["waist_over_bust_line"]=41,["wrist"]=17 };
            if(id=="synthetic-distinct") { expected["bust"]=96; expected["back_width"]=43; expected["waist"]=74; expected["waist_back_width"]=34; expected["hips"]=102; expected["hip_back_width"]=54; expected["waist_line"]=39; expected["waist_over_bust_line"]=42; }
            var body=item.GetProperty("body_input_cm_degree"); Need(body.EnumerateObject().Count()==expected.Count,"body field count");
            foreach(var pair in expected) Need(body.TryGetProperty(pair.Key,out var value)&&value.GetDouble()==pair.Value,"synthetic body mismatch: "+pair.Key);
            var derived=item.GetProperty("derived");
            var predicted=new Dictionary<string,double> { ["_waist_level"]=expected["height"]-expected["head_l"]-expected["waist_line"], ["_leg_length"]=expected["height"]-expected["head_l"]-expected["waist_line"]-expected["hips_line"], ["_base_sleeve_balance"]=expected["shoulder_w"]-2, ["_bust_line"]=expected["vert_bust_line"]*2/3+expected["bust_line"]/3, ["_hip_inclination"]=expected["hip_inclination"]/2, ["_shoulder_incl"]=expected["shoulder_incl"], ["_armscye_depth"]=expected["armscye_depth"]+2.5 };
            Need(derived.EnumerateObject().Count()==predicted.Count,"derived field count");
            foreach(var pair in predicted) Need(derived.TryGetProperty(pair.Key,out var value)&&Math.Abs(value.GetDouble()-pair.Value)<1e-10,"derived mismatch: "+pair.Key);
            var panels=item.GetProperty("panels"); var indexed=new Dictionary<string,JsonElement[]>();
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


