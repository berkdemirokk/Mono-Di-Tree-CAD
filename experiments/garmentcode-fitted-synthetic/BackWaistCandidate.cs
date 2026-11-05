namespace FittedResearch;

// Numerical candidate for the source's pre-neck/armhole back bodice.
// It does NOT reproduce scipy BFGS or certify optimization error.
internal static class BackWaistCandidate
{
    internal readonly record struct P(double X,double Y)
    {
        public static P operator +(P a,P b)=>new(a.X+b.X,a.Y+b.Y);
        public static P operator -(P a,P b)=>new(a.X-b.X,a.Y-b.Y);
        public static P operator *(P a,double v)=>new(a.X*v,a.Y*v);
        public double Norm()=>Math.Sqrt(X*X+Y*Y);
    }
    internal sealed record Edge(int Id,string Kind,P Start,P End,P? RelativeControl);
    internal sealed record Trace(P BaseStart,P BaseEnd,P BaseRelativeControl,double DartWidth,double OuterOffset,double InnerOffset,double OuterLeg,double InnerLeg);
    internal enum ReferenceState { Unassessed, KnownMismatch, Unverified }
    internal sealed class Path
    {
        public string Snapshot { get; }
        public IReadOnlyList<Edge> Edges { get; }
        public IReadOnlyList<GeneratedGraph.Seam> DartRules { get; }
        public Trace SourceSteps { get; }
        public ReferenceState ReferenceAssessment { get; }
        internal Path(string snapshot,IReadOnlyList<Edge> edges,IReadOnlyList<GeneratedGraph.Seam> rules,Trace sourceSteps,ReferenceState state)
        {
            Snapshot=snapshot;Edges=edges;DartRules=rules;SourceSteps=sourceSteps;
            ReferenceAssessment=state;
        }
        // Unassessed never means accepted, validated or suitable for production.
        public void RequireResearchComposition() { if(ReferenceAssessment!=ReferenceState.Unassessed) throw new InvalidOperationException("Source mismatch or unverified input; composition blocked."); }
        public string Status=>"unresolved";public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
    }
    // Deliberately distinct from fixture Path: consumers of Path cannot accept it.
    internal sealed class UnverifiedPath
    {
        internal UnverifiedPath(Path source) { Snapshot=source.Snapshot;Edges=source.Edges;SourceSteps=source.SourceSteps; }
        public string Snapshot { get; }
        public IReadOnlyList<Edge> Edges { get; }
        public Trace SourceSteps { get; }
        public ReferenceState ReferenceAssessment=>ReferenceState.Unverified;
        public string Status=>"unresolved"; public bool ExportAllowed=>false; public bool ProductionAllowed=>false;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Unverified input; composition blocked.");
    }
    readonly record struct Quad(P A,P C,P B)
    {
        public P Eval(double t)=>A*((1-t)*(1-t))+C*(2*t*(1-t))+B*(t*t);
        public P Derivative(double t)=>(C-A)*(2*(1-t))+(B-C)*(2*t);
        public double Length(double a,double b)
        {
            const int n=512;double h=(b-a)/n,sum=Derivative(a).Norm()+Derivative(b).Norm();
            for(int i=1;i<n;i++)sum+=(i%2==0?2:4)*Derivative(a+i*h).Norm();return sum*h/3;
        }
        public double ParamAtLength(double target,double end=1)
        {
            if(target<0||target>Length(0,end))throw new ArgumentException("Arc length outside edge");
            double lo=0,hi=end;for(int i=0;i<48;i++){double m=(lo+hi)/2;if(Length(0,m)<target)lo=m;else hi=m;}return(lo+hi)/2;
        }
        public (P start,P control,P end) Crop(double a,double b)
        {
            P start=Eval(a);return(start,start+Derivative(a)*((b-a)/2),Eval(b));
        }
    }
    static P Rel(P start,P end,P control)
    {
        P d=end-start,c=control-start;double den=d.X*d.X+d.Y*d.Y;
        if(den<1e-20)throw new ArgumentException("Degenerate curve");
        return new((c.X*d.X+c.Y*d.Y)/den,(-c.X*d.Y+c.Y*d.X)/den);
    }
    static P Abs(P start,P end,P control) { P d=end-start;return start+d*control.X+new P(-d.Y,d.X)*control.Y; }
    internal static Path Create(string id)
    {
        var body=SyntheticBodyCatalog.Create(id);
        var state=id=="synthetic-small-difference"?ReferenceState.KnownMismatch:ReferenceState.Unassessed;
        return Calculate(id,key=>(double)body[key],state);
    }
    // Explicit body/design input remains local, unverified and non-composable.
    // No fixture, process launch, networking, persistence or export occurs.
    internal static UnverifiedPath CreateFromBody(IResearchPatternBody body,ResearchDesign design)
    {
        ResearchBody.RequireSupported(body);
        ArgumentNullException.ThrowIfNull(body);ArgumentNullException.ThrowIfNull(design);
        return new UnverifiedPath(Calculate(body.Snapshot(design),key=>(double)body[key],ReferenceState.Unverified));
    }
    internal static UnverifiedPath CreateFromMeasurements(ResearchMeasurements measurements,ResearchDesign design)
    {
        ArgumentNullException.ThrowIfNull(measurements);ArgumentNullException.ThrowIfNull(design);
        var operation=ResearchMeasurements.Operation.PrecutBackWaist;
        if(!measurements.Check(operation).AllFieldsPresent)throw new ArgumentException("Missing back measurements.");
        return new UnverifiedPath(Calculate(measurements.SnapshotFor(operation,design),measurements.Value,ReferenceState.Unverified));
    }
    private static Path Calculate(string id,Func<string,double> V,ReferenceState state)
    {
        if(V("shoulder_w")<=2 || V("shoulder_incl")>=45)throw new ArgumentException("Unsupported back research scope.");
        double shoulder=V("shoulder_w")/2,angle=V("shoulder_incl"),bumPoints=V("bum_points");
        double width=V("back_width")/2,waist=V("waist_back_width")/2,tan=Math.Tan(angle*Math.PI/180),h=tan*width/4;
        double diff=width-waist,side=diff<4?0:diff/6,dartWidth=(diff-side)/2;
        double length=V("waist_line")-tan*(width-shoulder),bustLine=((1-1d/3)*V("vert_bust_line")+V("bust_line")/3);
        if(dartWidth<=0 || length<=bustLine)throw new ArgumentException("Unsupported dart geometry");
        P start=new(0,h),end=new(-width,0);double len=Math.Sqrt(width*width+h*h);
        // One feasible exact tangent direction, initialized at half-unit norm.
        // Source BFGS can converge to a nearby, different point on this ray.
        P rel=new(.5*width/len,-.5*h/len);var q=new Quad(start,Abs(start,end,rel),end);
        double total=q.Length(0,1),position=bumPoints/2,dist=position*.5;
        double outerOffset=position+dist/2+dartWidth*1.5,innerOffset=position-dist/2+dartWidth/2;
        (double lo,double hi,P tip) Cut(double offset,double sideLength,double maxT)
        {
            double limit=q.Length(0,maxT);if(offset<=0||offset>=limit||sideLength<=dartWidth/2)throw new ArgumentException("Invalid curved cut");
            double center=q.ParamAtLength(offset,maxT);P centerPoint=q.Eval(center);
            double maxRadius=Math.Min((q.Eval(0)-centerPoint).Norm(),(q.Eval(maxT)-centerPoint).Norm());
            double SideParameter(double radius,bool before)
            {
                double low=before?0:center,high=before?center:maxT;
                for(int i=0;i<48;i++)
                {
                    double mid=(low+high)/2,distance=(q.Eval(mid)-centerPoint).Norm();
                    if(before) { if(distance>radius)low=mid;else high=mid; }
                    else { if(distance<radius)low=mid;else high=mid; }
                }
                return (low+high)/2;
            }
            double Chord(double radius)=>(q.Eval(SideParameter(radius,false))-q.Eval(SideParameter(radius,true))).Norm();
            if(Chord(maxRadius)<dartWidth)throw new ArgumentException("Cut does not fit");
            double left=0,right=maxRadius;
            for(int i=0;i<48;i++) { double mid=(left+right)/2;if(Chord(mid)<dartWidth)left=mid;else right=mid; }
            double selected=(left+right)/2,lo=SideParameter(selected,true),hi=SideParameter(selected,false);
            P a=q.Eval(lo),b=q.Eval(hi),chord=b-a;double chordLen=chord.Norm(),height=Math.Sqrt(sideLength*sideLength-dartWidth*dartWidth/4);
            P tip=(a+b)*.5+new P(chord.Y,-chord.X)*(height/chordLen);
            return(lo,hi,tip);
        }
        var outer=Cut(outerOffset,.9*(length-bustLine),1);
        var inner=Cut(innerOffset,length-bustLine,outer.lo);
        var edges=new List<Edge>();
        void QEdge(int i,double a,double b,bool shiftEnd=false)
        {
            var crop=q.Crop(a,b);P relative=Rel(crop.start,crop.end,crop.control);P finish=shiftEnd?new(crop.end.X+side,crop.end.Y):crop.end;
            edges.Add(new(i,"Q",crop.start,finish,relative));
        }
        void L(int i,P a,P b)=>edges.Add(new(i,"L",a,b,null));
        QEdge(0,0,inner.lo);L(1,q.Eval(inner.lo),inner.tip);L(2,inner.tip,q.Eval(inner.hi));
        QEdge(3,inner.hi,outer.lo);L(4,q.Eval(outer.lo),outer.tip);L(5,outer.tip,q.Eval(outer.hi));QEdge(6,outer.hi,1,true);
        P sideBottom=new(-width+side,0),sideMiddle=new(-width,V("waist_line")-bustLine),sideTop=new(-width,length),top=new(0,length+tan*width);
        L(7,sideBottom,sideMiddle);L(8,sideMiddle,sideTop);L(9,sideTop,top);L(10,top,start);
        var seams=new[]{new GeneratedGraph.Seam(id,"precut_back",4,false,"precut_back",5,false),new GeneratedGraph.Seam(id,"precut_back",1,false,"precut_back",2,false)};
        return new(id,edges.AsReadOnly(),Array.AsReadOnly(seams),new(start,end,rel,dartWidth,outerOffset,innerOffset,.9*(length-bustLine),length-bustLine),state);
    }
}
