namespace FittedResearch;

// Experimental closed front-skirt path, based solely on synthetic body
// formulas. Waist split and curve controls are candidates, not source-exact.
internal static class FrontSkirtCandidate
{
    internal sealed record Edge(int Id,string Kind,PartialFittedEngine.P Start,PartialFittedEngine.P End,TangentCandidate.Point? Control);
    internal sealed record Candidate(string Snapshot,IReadOnlyList<Edge> Edges)
    {
        public string Status=>"unresolved";
        public bool ProductionAllowed=>false;
        public bool ExportAllowed=>false;
    }
    internal sealed class UnverifiedCandidate
    {
        internal UnverifiedCandidate(string snapshot,IReadOnlyList<Edge> edges) { Snapshot=snapshot;Edges=edges; }
        public string Snapshot { get; } public IReadOnlyList<Edge> Edges { get; }
        public BackWaistCandidate.ReferenceState ReferenceAssessment=>BackWaistCandidate.ReferenceState.Unverified;
        public string Status=>"unresolved"; public bool ExportAllowed=>false; public bool ProductionAllowed=>false;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Unverified skirt; composition blocked.");
    }
    // Pinned pencil-skirt design, before any waist-interface matching.
    internal static UnverifiedCandidate CreateFromBody(ResearchBody body,ResearchDesign design)
    {
        ArgumentNullException.ThrowIfNull(body);ArgumentNullException.ThrowIfNull(design);
        double V(string key)=>(double)body[key];
        double hips=(V("hips")-V("hip_back_width"))/2, waist=(V("waist")-V("waist_back_width"))/2;
        double depth=V("hips_line"),length=.4*body.Derive().LegLength;
        double shift=Math.Min(Math.Tan(body.Derive().HipInclination*Math.PI/180)*depth,hips-waist);
        double width=hips-waist-shift,position=V("bust_points")/2,leg=depth*.8;
        double px=Math.Truncate(hips),py=Math.Truncate(length+depth),center=hips-px;
        double left=-px,right=2*hips-px,bottom=-py,hip=length-py,top=length+depth-py;
        if(hips<=0 || waist<=0 || width<=0 || shift<0 || length<=0 || position<=0 ||
           center+position+width>=right-shift || center-position-width<=left+shift)
            throw new ArgumentException("Unsupported skirt dart placement");
        var plus=DartCut.Horizontal(center+position+width,center+position,top,width,leg,false);
        var minus=DartCut.Horizontal(center-position,center-position-width,top,width,leg,false);
        PartialFittedEngine.P P(double x,double y)=>new(x,y);
        var points=new[]{P(left,bottom),P(right,bottom),P(right,hip),P(right-shift,top),P(plus.Start.X,top),P(plus.Tip.X,plus.Tip.Y),P(plus.End.X,top),P(center,top),P(minus.Start.X,top),P(minus.Tip.X,minus.Tip.Y),P(minus.End.X,top),P(left+shift,top),P(left,hip)};
        if(!DartCut.SimpleClosed(points.Select(p=>new DartCut.P(p.X,p.Y)).ToArray()))throw new ArgumentException("Unsupported skirt contour");
        var edges=new List<Edge>();
        for(int i=0;i<points.Length;i++) {
            var start=points[i];var end=points[(i+1)%points.Length];bool curve=i is 2 or 11;
            var control=curve?(TangentCandidate.Point?)TangentCandidate.RelativeControl(new(start.X,start.Y),new(end.X,end.Y),i==2):null;
            edges.Add(new(i,curve?"Q":"L",start,end,control));
        }
        return new(body.Snapshot(design),edges.AsReadOnly());
    }
    internal static Candidate Create(string id) => CreateCore(id,null);
    private static Candidate CreateCore(string id,double? splitX)
    {
        var parts=PartialFittedEngine.Create(id).Segments.Where(e=>e.Panel=="skirt_front").ToDictionary(e=>e.Edge);
        PartialFittedEngine.Segment Required(int i)=>parts.TryGetValue(i,out var value)?value:throw new InvalidOperationException("Missing required edge.");
        var a=Required(2);var b=Required(4);var c=Required(5);var d=Required(8);var e=Required(9);var f=Required(11);
        // Central split is an explicit symmetric candidate. The source uses
        // interface matching, which can place this point elsewhere.
        double split=splitX ?? (c.End.X+d.Start.X)/2;
        if(!double.IsFinite(split) || split<=Math.Min(c.End.X,d.Start.X) || split>=Math.Max(c.End.X,d.Start.X)) throw new ArgumentException("Invalid waist split");
        var middle=new PartialFittedEngine.P(split,(c.End.Y+d.Start.Y)/2);
        parts.Add(3,new("skirt_front",3,a.End,b.Start,"Edge"));
        parts.Add(6,new("skirt_front",6,c.End,middle,"Edge"));
        parts.Add(7,new("skirt_front",7,middle,d.Start,"Edge"));
        parts.Add(10,new("skirt_front",10,e.End,f.Start,"Edge"));
        var result=new List<Edge>();
        for(int i=0;i<13;i++)
        {
            var part=Required(i); bool curve=part.SourceKind=="CurveEdgeEndpointsOnly";
            TangentCandidate.Point? control=curve?TangentCandidate.RelativeControl(new(part.Start.X,part.Start.Y),new(part.End.X,part.End.Y),i==2):null;
            result.Add(new(i,curve?"Q":"L",part.Start,part.End,control));
        }
        return new(id,result.AsReadOnly());
    }
    // Matching inputs currently come from the source's synthetic pre-seam
    // fixture, not from a complete C# bodice generator.
    internal static Candidate CreateFromMatchedInterface(string id,InterfaceMatcher.Result matched)
    {
        InterfaceMatcher.ValidateBound(id,matched);
        var front=matched.Lower.Where(e=>e.Panel=="skirt_front").ToArray();
        if(front.Length!=4 || !front[1].Flipped || !front[2].Flipped || (front[1].Start-front[2].End).Norm()>1e-8) throw new ArgumentException("Unsupported front interface topology");
        return CreateCore(id,front[1].Start.X);
    }
    internal static Candidate CreateFromDerived(string id,InterfaceMatcher.Result matched)
    {
        InterfaceMatcher.ValidateDerived(id,matched);
        var front=matched.Lower.Where(e=>e.Panel=="skirt_front").ToArray();
        if(front.Length!=4 || !front[1].Flipped || !front[2].Flipped || (front[1].Start-front[2].End).Norm()>1e-8)throw new ArgumentException("Unsupported derived front interface topology");
        return CreateCore(id,front[1].Start.X);
    }
}
