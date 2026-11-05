using System.Collections.Immutable;
namespace FittedResearch;

// Numerical interface study only. Approximate back curves make the result unverified.
internal static class BodyWaistStudy
{
    internal sealed class UnverifiedMatch
    {
        internal UnverifiedMatch(string snapshot,ImmutableArray<InterfaceMatcher.Edge> upper,ImmutableArray<InterfaceMatcher.Edge> lower) { Snapshot=snapshot;Upper=upper;Lower=lower; }
        public string Snapshot { get; } public ImmutableArray<InterfaceMatcher.Edge> Upper { get; } public ImmutableArray<InterfaceMatcher.Edge> Lower { get; }
        public BackWaistCandidate.ReferenceState ReferenceAssessment=>BackWaistCandidate.ReferenceState.Unverified;
        public string Status=>"unresolved"; public bool ExportAllowed=>false; public bool ProductionAllowed=>false;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Unverified waist; composition blocked.");
    }
    internal static UnverifiedMatch Calculate(ResearchBody body,ResearchDesign design)
    {
        ArgumentNullException.ThrowIfNull(body);ArgumentNullException.ThrowIfNull(design);
        var f=PrecutFrontBodice.CreateFromBody(body,design);var b=BackWaistCandidate.CreateFromBody(body,design);
        var skirt=FrontSkirtCandidate.CreateFromBody(body,design);
        if(f.Snapshot!=b.Snapshot || f.Snapshot!=skirt.Snapshot)throw new ArgumentException("Body snapshot mismatch");
        InterfaceMatcher.P F(PartialFittedEngine.P p)=>new(p.X,p.Y);
        InterfaceMatcher.P B(BackWaistCandidate.P p)=>new(p.X,p.Y);
        InterfaceMatcher.P Mirror(InterfaceMatcher.P p)=>new(-p.X,p.Y);
        InterfaceMatcher.Edge Line(string panel,InterfaceMatcher.P a,InterfaceMatcher.P z,bool flip=false)=>new(panel,"Edge",a,z,[],flip,false,1);
        InterfaceMatcher.Edge Back(int index,bool left) {
            var e=b.Edges[index];if(e.Kind!="Q" || e.RelativeControl is not { } r)throw new ArgumentException("Unsupported back waist");
            var a=B(e.Start);var z=B(e.End);var d=z-a;var c=a+d*r.X+new InterfaceMatcher.P(-d.Y,d.X)*r.Y;
            return left?new("left_btorso","CurveEdge",Mirror(z),Mirror(a),[Mirror(c)],false,false,1):new("right_btorso","CurveEdge",a,z,[c],false,false,1);
        }
        var v=f.Vertices;
        var upper=new List<InterfaceMatcher.Edge>{Line("right_ftorso",F(v[4]),F(v[3])),Line("right_ftorso",F(v[1]),F(v[0])),Line("left_ftorso",Mirror(F(v[0])),Mirror(F(v[1]))),Line("left_ftorso",Mirror(F(v[3])),Mirror(F(v[4]))),Back(6,true),Back(3,true),Back(0,true),Back(0,false),Back(3,false),Back(6,false)};
        var e=skirt.Edges;
        var lower=new List<InterfaceMatcher.Edge>{Line("skirt_front",F(e[9].End),F(e[11].Start),true),Line("skirt_front",F(e[5].End),F(e[8].Start),true),Line("skirt_front",F(e[2].End),F(e[4].Start),true)};
        double V(string k)=>(double)body[k]; double hips=V("hip_back_width")/2,waist=V("waist_back_width")/2,depth=V("hips_line")*1.05,length=.4*body.Derive().LegLength;
        double shift=Math.Min(Math.Tan(body.Derive().HipInclination*Math.PI/180)*depth,hips-waist),width=hips-waist-shift;
        double px=Math.Truncate(hips),py=Math.Truncate(length+depth),center=hips-px,top=length+depth-py,left=-px,right=2*hips-px;
        double pos=V("bum_points")/2,dist=pos*.5,w=width/2;
        if(width<=0 || pos<=0 || length<=0)throw new ArgumentException("Unsupported back skirt waist");
        double[] c=[center-pos-dist/2-3*width/4,center-pos+dist/2-width/4,center+pos-dist/2+width/4,center+pos+dist/2+3*width/4];
        if(c[0]-w/2<=left+shift || c[3]+w/2>=right-shift || c[0]+w/2>=c[1]-w/2 || c[1]+w/2>=c[2]-w/2 || c[2]+w/2>=c[3]-w/2)throw new ArgumentException("Unsupported back skirt dart placement");
        InterfaceMatcher.P P(double x)=>new(x,top);
        lower.Add(Line("skirt_back",P(c[3]+w/2),P(right-shift),true));lower.Add(Line("skirt_back",P(c[2]+w/2),P(c[3]-w/2),true));lower.Add(Line("skirt_back",P(c[1]+w/2),P(c[2]-w/2),true));lower.Add(Line("skirt_back",P(c[0]+w/2),P(c[1]-w/2),true));lower.Add(Line("skirt_back",P(left+shift),P(c[0]-w/2),true));
        return InterfaceMatcher.MatchUnverified(f.Snapshot,upper,lower);
    }
}
