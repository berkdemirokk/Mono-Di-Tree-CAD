namespace FittedResearch;

// Analytic corner intersection and source ArmholeCurve control semantics for
// the pinned sleeveless synthetic design. Not a certified contour/topology.
internal static class SyntheticArmholeCut
{
    internal readonly record struct P(double X,double Y)
    {
        public static P operator +(P a,P b)=>new(a.X+b.X,a.Y+b.Y);
        public static P operator -(P a,P b)=>new(a.X-b.X,a.Y-b.Y);
        public static P operator *(P a,double t)=>new(a.X*t,a.Y*t);
        public double Norm()=>Math.Sqrt(X*X+Y*Y);
    }
    internal sealed record Curve(string Panel,int Index,P Start,P End,P Control1,P Control2);
    internal sealed record Result(string Snapshot,IReadOnlyList<Curve> Curves)
    {
        public string Status=>"unresolved"; public bool ProductionAllowed=>false; public bool ExportAllowed=>false;
    }
    internal static Result Create(string id)
    {
        if(id is not ("synthetic-base" or "synthetic-distinct"))throw new ArgumentException("Unknown synthetic case");
        var body=SyntheticBodyCatalog.Create(id);
        return Calculate(id,body,PrecutFrontBodice.Create(id).Vertices);
    }
    internal sealed class UnverifiedFront
    {
        internal UnverifiedFront(string snapshot,IReadOnlyList<Curve> curves) { Snapshot=snapshot;Curves=curves; }
        public string Snapshot { get; } public IReadOnlyList<Curve> Curves { get; }
        public BackWaistCandidate.ReferenceState ReferenceAssessment=>BackWaistCandidate.ReferenceState.Unverified;
        public string Status=>"unresolved"; public bool ExportAllowed=>false; public bool ProductionAllowed=>false;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Unverified armhole; composition blocked.");
    }
    internal static UnverifiedFront CreateFromBody(ResearchBody body,ResearchDesign design)
    {
        ArgumentNullException.ThrowIfNull(body);ArgumentNullException.ThrowIfNull(design);
        var front=PrecutFrontBodice.CreateFromBody(body,design);
        var result=Calculate(front.Snapshot,body,front.Vertices);
        return new(front.Snapshot,Array.AsReadOnly(result.Curves.Where(c=>c.Panel.EndsWith("_ftorso",StringComparison.Ordinal)).ToArray()));
    }
    internal sealed class UnverifiedBack
    {
        internal UnverifiedBack(string snapshot,IReadOnlyList<Curve> curves) { Snapshot=snapshot;Curves=curves; }
        public string Snapshot { get; } public IReadOnlyList<Curve> Curves { get; }
        public BackWaistCandidate.ReferenceState ReferenceAssessment=>BackWaistCandidate.ReferenceState.Unverified;
        public string Status=>"unresolved"; public bool ExportAllowed=>false; public bool ProductionAllowed=>false;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Unverified armhole; composition blocked.");
    }
    internal static UnverifiedBack CreateBackFromBody(ResearchBody body,ResearchDesign design)
    {
        ArgumentNullException.ThrowIfNull(body);ArgumentNullException.ThrowIfNull(design);
        var front=PrecutFrontBodice.CreateFromBody(body,design);
        var back=BackWaistCandidate.CreateFromBody(body,design);
        if(front.Snapshot!=back.Snapshot)throw new ArgumentException("Body snapshot mismatch");
        var result=Calculate(front.Snapshot,body,front.Vertices);
        return new(front.Snapshot,Array.AsReadOnly(result.Curves.Where(c=>c.Panel.EndsWith("_btorso",StringComparison.Ordinal)).ToArray()));
    }
    private static Result Calculate(string id,ResearchBody body,IReadOnlyList<PartialFittedEngine.P> precut)
    {
        double shoulderHalf=(double)body["shoulder_w"]/2, armscyeDepth=(double)body["armscye_depth"],shoulderAngle=(double)body["shoulder_incl"];
        P frontLow=new(precut[7].X,precut[7].Y),frontTop=new(precut[8].X,precut[8].Y),frontCenter=new(precut[9].X,precut[9].Y);
        var (frontSide,frontShoulder,connect)=SolveCorner(frontLow,frontTop,frontCenter,armscyeDepth,shoulderHalf);
        double sleeveBalance=(shoulderHalf*2-2)/2;
        double backWidth=(double)body["back_width"]/2,tan=Math.Tan(shoulderAngle*Math.PI/180);
        double backTop=(double)body["waist_line"]-tan*(backWidth-shoulderHalf);
        double backIncl=backWidth-sleeveBalance;
        P backShoulder=new(-backWidth+backIncl,backTop+tan*backIncl);
        P backSide=new(-backWidth,backShoulder.Y-connect);
        P Mirror(P p)=>new(-p.X,p.Y);
        return new(id,Array.AsReadOnly(new[]{
            new Curve("right_ftorso",3,frontShoulder,frontSide,new(.5,.2),new(.8,.35)),
            new Curve("left_ftorso",8,Mirror(frontSide),Mirror(frontShoulder),new(.2,.35),new(.5,.2)),
            new Curve("right_btorso",9,backSide,backShoulder,new(.2,-.35),new(.5,-.2)),
            new Curve("left_btorso",3,Mirror(backShoulder),Mirror(backSide),new(.5,-.2),new(.8,-.35))
        }));
    }
    internal static (P side,P shoulder,double connect) SolveCorner(P frontLow,P frontTop,P frontCenter,double armscyeDepth,double shoulderHalf)
    {
        const double connectFactor=.2;
        P sideVector=frontTop-frontLow,shoulderVector=frontCenter-frontTop;
        double connect=Math.Min((armscyeDepth+2.5)*(1+connectFactor),sideVector.Norm()-1);
        if(!double.IsFinite(connect)||connect<=0||Math.Abs(sideVector.Y)<1e-9)throw new ArgumentException("Unsupported armhole depth");
        double sleeveBalance=(shoulderHalf*2-2)/2;
        double frontWidth=shoulderHalf+connect*sideVector.X/sideVector.Y;
        double incl=frontWidth-sleeveBalance;
        // (frontLow + u*sideVector) - (frontTop + t*shoulderVector)
        // equals (-incl,-connect), the source cut-corner shortcut.
        P target=frontTop-frontLow+new P(-incl,-connect);
        double determinant=sideVector.X*(-shoulderVector.Y)-(-shoulderVector.X)*sideVector.Y;
        if(!double.IsFinite(determinant)||Math.Abs(determinant)<1e-9)throw new ArgumentException("Degenerate corner");
        double u=(target.X*(-shoulderVector.Y)-(-shoulderVector.X)*target.Y)/determinant;
        double t=(sideVector.X*target.Y-target.X*sideVector.Y)/determinant;
        if(u<=0||u>=1||t<=0||t>=1)throw new ArgumentException("Armhole leaves source corner");
        return (frontLow+sideVector*u,frontTop+shoulderVector*t,connect);
    }

}
