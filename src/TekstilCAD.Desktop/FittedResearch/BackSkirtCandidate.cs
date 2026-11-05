namespace FittedResearch;

// Back skirt assembled from body-derived partial geometry and the matched
// source-derived synthetic interface. Curves are tangent candidates only.
internal static class BackSkirtCandidate
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
    // Chord topology and tangent controls are diagnostics, not a curve proof.
    internal static UnverifiedCandidate CreateFromBody(IResearchPatternBody body,ResearchDesign design)
    {
        ResearchBody.RequireSupported(body);
        ArgumentNullException.ThrowIfNull(body);ArgumentNullException.ThrowIfNull(design);
        var match=BodyWaistStudy.Calculate(body,design);
        var lower=match.Lower.Where(e=>e.Panel=="skirt_back").ToArray();
        if(lower.Length!=6 || !lower[2].Flipped || !lower[3].Flipped || (lower[2].Start-lower[3].End).Norm()>1e-8)throw new ArgumentException("Unsupported back waist match");
        double V(string k)=>(double)body[k];double hips=V("hip_back_width")/2,waist=V("waist_back_width")/2;
        double depth=V("hips_line")*1.05,length=.4*body.Derive().LegLength;
        double shift=Math.Min(Math.Tan(body.Derive().HipInclination*Math.PI/180)*depth,hips-waist),width=hips-waist-shift;
        double px=Math.Truncate(hips),py=Math.Truncate(length+depth),center=hips-px,top=length+depth-py,hip=length-py,bottom=-py,left=-px,right=2*hips-px;
        double pos=V("bum_points")/2,dist=pos*.5,w=width/2,leg=V("hips_line")*.85-(V("hips_line")-depth),split=lower[2].Start.X;
        if(width<=0 || pos<=0 || length<=0 || shift<0 || !double.IsFinite(split))throw new ArgumentException("Unsupported skirt geometry");
        double[] c=[center-pos-dist/2-3*width/4,center-pos+dist/2-width/4,center+pos-dist/2+width/4,center+pos+dist/2+3*width/4];
        if(c[0]-w/2<=left+shift || c[3]+w/2>=right-shift || c[0]+w/2>=c[1]-w/2 || c[1]+w/2>=split || split>=c[2]-w/2 || c[2]+w/2>=c[3]-w/2)throw new ArgumentException("Unsupported skirt dart placement");
        var darts=new DartCut.Triangle[4];
        for(int i=0;i<4;i++)darts[i]=DartCut.Horizontal(c[i]-w/2,c[i]+w/2,top,w,leg*(i==0||i==3?.9:1),false);
        PartialFittedEngine.P P(double x,double y)=>new(x,y);
        var points=new[]{P(left,bottom),P(left,hip),P(left+shift,top),P(darts[0].Start.X,top),P(darts[0].Tip.X,darts[0].Tip.Y),P(darts[0].End.X,top),P(darts[1].Start.X,top),P(darts[1].Tip.X,darts[1].Tip.Y),P(darts[1].End.X,top),P(split,top),P(darts[2].Start.X,top),P(darts[2].Tip.X,darts[2].Tip.Y),P(darts[2].End.X,top),P(darts[3].Start.X,top),P(darts[3].Tip.X,darts[3].Tip.Y),P(darts[3].End.X,top),P(right-shift,top),P(right,hip),P(right,bottom)};
        if(!DartCut.SimpleClosed(points.Select(p=>new DartCut.P(p.X,p.Y)).ToArray()))throw new ArgumentException("Unsupported skirt chord contour");
        var edges=new List<Edge>();
        for(int i=0;i<points.Length;i++) {
            var start=points[i];var end=points[(i+1)%points.Length];bool curve=i is 1 or 16;
            var control=curve?(TangentCandidate.Point?)TangentCandidate.RelativeControl(new(start.X,start.Y),new(end.X,end.Y),i==1):null;
            edges.Add(new(i,curve?"Q":"L",start,end,control));
        }
        return new(match.Snapshot,edges.AsReadOnly());
    }
    internal static Candidate Create(string id,InterfaceMatcher.Result matched) { InterfaceMatcher.ValidateBound(id,matched);return CreateCore(id,matched); }
    internal static Candidate CreateFromDerived(string id,InterfaceMatcher.Result matched) { InterfaceMatcher.ValidateDerived(id,matched);return CreateCore(id,matched); }
    private static Candidate CreateCore(string id,InterfaceMatcher.Result matched)
    {
        var pieces=PartialFittedEngine.Create(id).Segments.Where(e=>e.Panel=="skirt_back").ToDictionary(e=>e.Edge);
        PartialFittedEngine.Segment Required(int n)=>pieces.TryGetValue(n,out var p)?p:throw new InvalidOperationException("Missing back edge");
        var back=matched.Lower.Where(e=>e.Panel=="skirt_back").ToArray();
        if(back.Length!=6 || !back[2].Flipped || !back[3].Flipped || (back[2].Start-back[3].End).Norm()>1e-8) throw new ArgumentException("Unsupported back interface topology");
        double split=back[2].Start.X;
        var p1=Required(1);var p3=Required(3);var p4=Required(4);var p6=Required(6);var p7=Required(7);
        var p10=Required(10);var p11=Required(11);var p13=Required(13);var p14=Required(14);var p16=Required(16);
        if(!double.IsFinite(split)||split<=p7.End.X||split>=p10.Start.X) throw new ArgumentException("Invalid back split");
        var middle=new PartialFittedEngine.P(split,(p7.End.Y+p10.Start.Y)/2);
        void Add(int n,PartialFittedEngine.P a,PartialFittedEngine.P b)=>pieces.Add(n,new("skirt_back",n,a,b,"Edge"));
        Add(2,p1.End,p3.Start);Add(5,p4.End,p6.Start);Add(8,p7.End,middle);Add(9,middle,p10.Start);Add(12,p11.End,p13.Start);Add(15,p14.End,p16.Start);
        var result=new List<Edge>();
        for(int i=0;i<19;i++)
        {
            var piece=Required(i); bool curve=piece.SourceKind=="CurveEdgeEndpointsOnly";
            var control=curve?(TangentCandidate.Point?)TangentCandidate.RelativeControl(new(piece.Start.X,piece.Start.Y),new(piece.End.X,piece.End.Y),i==1):null;
            result.Add(new(i,curve?"Q":"L",piece.Start,piece.End,control));
        }
        return new(id,result.AsReadOnly());
    }
}
