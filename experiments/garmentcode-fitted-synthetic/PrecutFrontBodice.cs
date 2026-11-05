namespace FittedResearch;

// Source BodiceFrontHalf before collar/armhole corner operations. This is not
// the final garment panel. Coordinates are reconstructed in centimetres.
internal static class PrecutFrontBodice
{
    internal sealed record Path(string Snapshot,IReadOnlyList<PartialFittedEngine.P> Vertices,IReadOnlyList<GeneratedGraph.Seam> DartRules)
    {
        public string Status=>"unresolved";
        public bool ProductionAllowed=>false;
        public bool ExportAllowed=>false;
    }
    internal sealed class UnverifiedPath
    {
        internal UnverifiedPath(string snapshot,IReadOnlyList<PartialFittedEngine.P> vertices) { Snapshot=snapshot;Vertices=vertices; }
        public string Snapshot { get; }
        public IReadOnlyList<PartialFittedEngine.P> Vertices { get; }
        public BackWaistCandidate.ReferenceState ReferenceAssessment=>BackWaistCandidate.ReferenceState.Unverified;
        public string Status=>"unresolved"; public bool ProductionAllowed=>false; public bool ExportAllowed=>false;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Unverified input; composition blocked.");
    }
    internal static Path Create(string id)
    {
        var generated=PartialFittedEngine.Create(id).Segments.Where(e=>e.Panel=="right_ftorso").ToDictionary(e=>e.Edge);
        PartialFittedEngine.Segment Edge(int i)=>generated.TryGetValue(i,out var edge)?edge:throw new InvalidOperationException("Missing reconstructed edge");
        var v=new List<PartialFittedEngine.P>();
        foreach(int n in new[]{11,10,9,8,7,6,5}) v.Add(Edge(n).End);
        v.Add(Edge(5).Start);
        double shoulder=18, angle=22*Math.PI/180;
        double frontLength=id switch { "synthetic-base"=>41,"synthetic-distinct"=>42,_=>throw new ArgumentException("Unknown synthetic case") };
        v.Add(new(-shoulder,frontLength)); v.Add(new(0,frontLength+Math.Tan(angle)*shoulder)); v.Add(new(0,0));
        // The first vertex is (0,0); last appended duplicate is not part of loop.
        v.RemoveAt(v.Count-1);
        var seams=new[]{new GeneratedGraph.Seam(id,"precut_front",5,false,"precut_front",6,false),new GeneratedGraph.Seam(id,"precut_front",1,false,"precut_front",2,false)};
        return new(id,v.AsReadOnly(),Array.AsReadOnly(seams));
    }
    // General local measurement path. No fixture or identifier is consulted.
    // The result remains diagnostic and cannot enter a stitched/production path.
    internal static UnverifiedPath CreateFromBody(ResearchBody body, ResearchDesign design)
    {
        ArgumentNullException.ThrowIfNull(body); ArgumentNullException.ThrowIfNull(design);
        return Calculate(body.Snapshot(design),key=>(double)body[key]);
    }
    internal static UnverifiedPath CreateFromMeasurements(ResearchMeasurements measurements,ResearchDesign design)
    {
        ArgumentNullException.ThrowIfNull(measurements);ArgumentNullException.ThrowIfNull(design);
        var operation=ResearchMeasurements.Operation.PrecutFrontBodice;
        if(!measurements.Check(operation).AllFieldsPresent)throw new ArgumentException("Missing front measurements.");
        return Calculate(measurements.SnapshotFor(operation,design),measurements.Value);
    }
    static UnverifiedPath Calculate(string snapshot,Func<string,double> V)
    {
        if(V("shoulder_w")<=2 || V("shoulder_incl")>=45 || V("back_width")>=V("bust") || V("waist_back_width")>=V("waist"))throw new ArgumentException("Unsupported front research scope.");
        double width=(V("bust")-V("back_width"))/2, waist=(V("waist")-V("waist_back_width"))/2;
        double bustPoint=V("bust_points")/2, shoulder=V("shoulder_w")/2;
        double tan=Math.Tan(V("shoulder_incl")*Math.PI/180), sideY=V("waist_line")-((1-1d/3)*V("vert_bust_line")+V("bust_line")/3);
        double bottomWidth=(width-waist)*2/3;
        double maxLen=V("waist_over_bust_line")-tan*(width-shoulder);
        double fraction=width/V("bust"), backAdjustment=tan*(V("back_width")/2-shoulder);
        double sideLength=V("waist_line")-backAdjustment-tan*(2*fraction-.5)*V("bust");
        double sideWidth=maxLen-sideLength, sideLeg=.75*(width-bustPoint);
        if(width<=0 || waist<=0 || sideY<=0 || maxLen<=0 || bustPoint<=0 ||
           bustPoint+bottomWidth>=width || sideY+sideWidth>=maxLen || sideY+sideWidth<=sideY)
            throw new ArgumentException("Unsupported front dart placement.");
        var bottom=DartCut.Horizontal(-bustPoint-bottomWidth,-bustPoint,0,bottomWidth,.9*sideY,true);
        var side=DartCut.Horizontal(sideY,sideY+sideWidth,0,sideWidth,sideLeg,true);
        var points=new PartialFittedEngine.P[] {
            new(0,0), new(-bustPoint,0), new(bottom.Tip.X,bottom.Tip.Y), new(bottom.Start.X,0),
            new(-(waist+bottomWidth),0), new(-width,sideY), new(-width+side.Tip.Y,side.Tip.X),
            new(-width,side.End.X), new(-shoulder,V("waist_over_bust_line")),
            new(0,V("waist_over_bust_line")+tan*shoulder)
        };
        if(!DartCut.SimpleClosed(points.Select(p=>new DartCut.P(p.X,p.Y)).ToArray()))
            throw new ArgumentException("Unsupported front contour.");
        // No seam rules are authorized for unverified user-shaped candidates.
        return new(snapshot,Array.AsReadOnly(points));
    }

}
