namespace FittedResearch;

// Synthetic diagnostic, not a source-optimizer reconstruction or production geometry.
internal static class ShoulderSeamStudy
{
    internal sealed record Result(BezierLengthBounds.Bounds Before,BezierLengthBounds.Bounds Back,BezierLengthBounds.Bounds After,double Displacement,BodyCurveStudy.Result RightTopology,BodyCurveStudy.Result LeftTopology,ExactBezierSeparation.Verdict DeepRightArm,ExactBezierSeparation.Verdict DeepLeftArm,bool RightArcCrossing,bool LeftArcCrossing,CircleSeparation.Verdict RightArcProof,CircleSeparation.Verdict LeftArcProof)
    {
        // Diagnostic values only; there is no contour or accepted-candidate
        // property and this result can never authorize composition or output.
        public string Status=>"unresolved";public bool Accepted=>false;public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Shoulder trial cannot be composed");
    }
    static double Mid(BezierLengthBounds.Bounds x)=>(x.ApproximateLowerCm+x.ApproximateUpperCm)/2;
    static BezierLengthBounds.Bounds Length(BodyCurveStudy.Edge edge) {
        var p=BodyCurveStudy.BezierControls(edge) ?? throw new ArgumentException("Unsupported shoulder");
        return BezierLengthBounds.Enclose(p.Select(v=>new BezierLengthBounds.Point(v.X,v.Y)).ToArray(),p.Length==2?0:10);
    }
    static BodyCurveStudy.Edge Mirror(BodyCurveStudy.Edge e,int index)=>new("left_ftorso",index,e.Kind,-e.Bx,e.By,-e.Ax,e.Ay,Array.AsReadOnly(e.RelativeControls.Reverse().Select(c=>(1-c.X,c.Y)).ToArray()),e.ArcY);
    static bool Eq(BodyCurveStudy.Edge a,BodyCurveStudy.Edge b)=>a.Panel==b.Panel&&a.Id==b.Id&&a.Kind==b.Kind&&a.Ax==b.Ax&&a.Ay==b.Ay&&a.Bx==b.Bx&&a.By==b.By&&a.ArcY==b.ArcY&&a.RelativeControls.SequenceEqual(b.RelativeControls);
    internal static Result Create(string id,double limit,int direction)
        =>CreateCore(id,limit,direction).Diagnostics;
    // Internal fixed-family token: no caller-supplied contours and no export.
    internal sealed class CertifiedPanelPair
    {
        private CertifiedPanelPair(string snapshot,IReadOnlyList<BodyCurveStudy.Edge> right,IReadOnlyList<BodyCurveStudy.Edge> left) {Snapshot=snapshot;Right=right;Left=left;}
        internal string Snapshot { get; } internal IReadOnlyList<BodyCurveStudy.Edge> Right { get; } internal IReadOnlyList<BodyCurveStudy.Edge> Left { get; }
        internal static CertifiedPanelPair Create(string id) {
            var c=CreateCore(id,1,-1);var d=c.Diagnostics;
            if(d.RightTopology.Unresolved!=0||d.LeftTopology.Unresolved!=0||d.RightTopology.NonMonotone!=0||d.LeftTopology.NonMonotone!=0||d.RightTopology.Closure!=BodyCurveStudy.ClosureEvidence.ClosedRepresented||d.LeftTopology.Closure!=BodyCurveStudy.ClosureEvidence.ClosedRepresented||d.RightArcProof!=CircleSeparation.Verdict.OnlySharedEndpoint||d.LeftArcProof!=CircleSeparation.Verdict.OnlySharedEndpoint)throw new ArgumentException("Shoulder topology not certified");
            BodyCurveStudy.Edge Freeze(BodyCurveStudy.Edge e)=>e with {RelativeControls=Array.AsReadOnly(e.RelativeControls.ToArray())};
            return new(SyntheticBodyCatalog.Create(id).Snapshot(ResearchDesign.PinnedFitted),Array.AsReadOnly(c.Right.Select(Freeze).ToArray()),Array.AsReadOnly(c.Left.Select(Freeze).ToArray()));
        }
    }
    static (Result Diagnostics,IReadOnlyList<BodyCurveStudy.Edge> Right,IReadOnlyList<BodyCurveStudy.Edge> Left) CreateCore(string id,double limit,int direction)
    {
        if(id is not ("synthetic-base" or "synthetic-distinct")||!double.IsFinite(limit)||limit<0||limit>2||direction is not (-1 or 1))throw new ArgumentException("Unsupported shoulder experiment");
        var body=SyntheticBodyCatalog.Create(id);var design=ResearchDesign.PinnedFitted;
        var fr=FrontBodiceCandidate.CreateFromBody(body,design);var fl=FrontBodiceCandidate.CreateLeftFromBody(body,design);var br=BackBodiceCandidate.CreateFromBody(body,design,false);
        var right=fr.Edges.Select(e=>new BodyCurveStudy.Edge("right_ftorso",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,Array.AsReadOnly(e.Controls.Select(c=>(c.X,c.Y)).ToArray()),e.ArcControlY)).ToArray();
        var source=right[2];var b=br.Edges[10];
        if(source.Kind!="L"||b.Kind!="L")throw new ArgumentException("Unsupported shoulder seam");
        var back=BezierLengthBounds.Enclose([new(b.Start.X,b.Start.Y),new(b.End.X,b.End.Y)],0);var before=Length(source);
        double dx=source.Bx-source.Ax,dy=source.By-source.Ay,len=Math.Sqrt(dx*dx+dy*dy),target=Mid(back);
        if(!double.IsFinite(len)||len<=0)throw new ArgumentException("Degenerate shoulder");
        double Estimate(double h){const int n=128;double sum=0;for(int i=0;i<=n;i++){double t=(double)i/n;double v=Math.Sqrt(len*len+16*h*h*(1-2*t)*(1-2*t));sum+=(i==0||i==n?1:i%2==0?2:4)*v;}return sum/(3*n);}
        double lo=0,hi=limit;if(Estimate(lo)>target||Estimate(hi)<target)throw new ArgumentException("Approximate shoulder search found no candidate");
        for(int i=0;i<45;i++){double h=(lo+hi)/2;if(Estimate(h)<target)lo=h;else hi=h;}
        right[2]=source with {Kind="Q",RelativeControls=Array.AsReadOnly(new[]{(.5,direction*2*((lo+hi)/2)/len)})};
        var after=Length(right[2]);if(!BackWaistJointSideExperiment.WithinDiagnosticTolerance(after,back,1e-4))throw new ArgumentException("Shoulder numerical enclosure rejected");
        var cp=BodyCurveStudy.BezierControls(right[2])!;double shift=Math.Sqrt(Math.Pow((cp[1].X-(source.Ax+source.Bx)/2)/2,2)+Math.Pow((cp[1].Y-(source.Ay+source.By)/2)/2,2));
        if(!double.IsFinite(shift)||shift>limit+1e-12)throw new ArgumentException("Shoulder shape limit exceeded");
        var left=new BodyCurveStudy.Edge[12];for(int i=0;i<12;i++) {
            var a=fr.Edges[11-i];var l=fl.Edges[i];var original=new BodyCurveStudy.Edge("right_ftorso",a.Id,a.Kind,a.Start.X,a.Start.Y,a.End.X,a.End.Y,a.Controls.Select(c=>(c.X,c.Y)).ToArray(),a.ArcControlY);
            var expected=new BodyCurveStudy.Edge("left_ftorso",l.Id,l.Kind,l.Start.X,l.Start.Y,l.End.X,l.End.Y,l.Controls.Select(c=>(c.X,c.Y)).ToArray(),l.ArcControlY);
            if(!Eq(Mirror(original,i),expected))throw new ArgumentException("Front mirror mismatch");left[i]=Mirror(right[11-i],i);
            if(i!=9&&!Eq(left[i],expected))throw new ArgumentException("Unexpected front change");
        }
        var rt=BodyCurveStudy.Inspect("right_ftorso",right,12);var lt=BodyCurveStudy.Inspect("left_ftorso",left,12);
        ExactBezierSeparation.Verdict Deep(BodyCurveStudy.Edge a,BodyCurveStudy.Edge b) {
            var first=BodyCurveStudy.BezierControls(a) ?? throw new ArgumentException("Unsupported deep shoulder curve");
            var second=BodyCurveStudy.BezierControls(b) ?? throw new ArgumentException("Unsupported deep shoulder curve");
            return ExactBezierSeparation.Compare(first,second,true,24,100000);
        }
        bool Crossing(BodyCurveStudy.Edge arc,BodyCurveStudy.Edge curve) {
            var p=BodyCurveStudy.BezierControls(curve) ?? throw new ArgumentException("Unsupported shoulder curve");
            return CircleSeparation.ProveMinorArcCrossing(new(arc.Ax,arc.Ay),new(arc.Bx,arc.By),arc.ArcY ?? throw new ArgumentException("Missing neck arc"),p.Select(x=>new CircleSeparation.Point(x.X,x.Y)).ToArray());
        }
        var deepRight=Deep(right[2],right[3]);var deepLeft=Deep(left[8],left[9]);
        var crossRight=Crossing(right[1],right[2]);var crossLeft=Crossing(left[10],left[9]);
        CircleSeparation.Verdict Proof(BodyCurveStudy.Edge arc,BodyCurveStudy.Edge curve) {
            var p=BodyCurveStudy.BezierControls(curve) ?? throw new ArgumentException("Unsupported shoulder curve");
            return CircleSeparation.QuadraticEndpointProof(new(arc.Ax,arc.Ay),new(arc.Bx,arc.By),arc.ArcY ?? throw new ArgumentException("Missing neck arc"),p.Select(x=>new CircleSeparation.Point(x.X,x.Y)).ToArray(),20,10000);
        }
        // The public diagnostic result contains no contours. The separate
        // internal token factory checks all represented topology first.
        var result=new Result(before,back,after,shift,rt,lt,deepRight,deepLeft,crossRight,crossLeft,Proof(right[1],right[2]),Proof(left[10],left[9]));
        return (result,Array.AsReadOnly(right),Array.AsReadOnly(left));
    }
}
