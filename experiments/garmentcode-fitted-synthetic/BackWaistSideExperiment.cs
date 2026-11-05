namespace FittedResearch;

// Explicitly synthetic, diagnostic-only side-end experiment. Never exported,
// composed into the garment graph, or interpreted as a physical correction.
internal static class BackWaistSideExperiment
{
    internal sealed class Result
    {
        internal Result(string id,IReadOnlyList<BackWaistCandidate.Edge> edges,double shift,double target,BezierLengthBounds.Bounds before,BezierLengthBounds.Bounds after,double frontSide,double backBefore,double backAfter,BodyCurveStudy.Result beforeTopology,BodyCurveStudy.Result topology)
        { Id=id;Edges=edges;ShiftCm=shift;TargetCm=target;Before=before;After=after;FrontSideCm=frontSide;BackSideBeforeCm=backBefore;BackSideAfterCm=backAfter;BeforeTopology=beforeTopology;Topology=topology; }
        public string Id { get; } public IReadOnlyList<BackWaistCandidate.Edge> Edges { get; }
        public double ShiftCm { get; } public double TargetCm { get; }
        public BezierLengthBounds.Bounds Before { get; } public BezierLengthBounds.Bounds After { get; }
        public double FrontSideCm { get; } public double BackSideBeforeCm { get; } public double BackSideAfterCm { get; }
        public BodyCurveStudy.Result BeforeTopology { get; } public BodyCurveStudy.Result Topology { get; }
        public string Status=>"unresolved";public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
        public BackWaistCandidate.ReferenceState ReferenceAssessment=>BackWaistCandidate.ReferenceState.Unverified;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Experimental side geometry cannot be composed.");
    }
    static BezierLengthBounds.Point[] Controls(BackWaistCandidate.Edge e)
    {
        if(e.Kind!="Q" || e.RelativeControl is not { } c)throw new ArgumentException("Unsupported waist curve");
        double dx=e.End.X-e.Start.X,dy=e.End.Y-e.Start.Y;
        return [new(e.Start.X,e.Start.Y),new(e.Start.X+dx*c.X-dy*c.Y,e.Start.Y+dy*c.X+dx*c.Y),new(e.End.X,e.End.Y)];
    }
    static double ApproximateLength(BackWaistCandidate.Edge e)
    {
        var p=Controls(e);const int n=256;double sum=0;
        double Speed(double t) { double dx=2*((1-t)*(p[1].X-p[0].X)+t*(p[2].X-p[1].X));double dy=2*((1-t)*(p[1].Y-p[0].Y)+t*(p[2].Y-p[1].Y));return Math.Sqrt(dx*dx+dy*dy); }
        for(int i=0;i<=n;i++)sum+=Speed((double)i/n)*(i==0||i==n?1:i%2==0?2:4);
        return sum/(3*n);
    }
    static BezierLengthBounds.Bounds Bound(IReadOnlyList<BackWaistCandidate.Edge> edges)
    {
        BezierLengthBounds.Bounds? total=null;
        foreach(int i in new[]{0,3,6}) { var item=BezierLengthBounds.Enclose(Controls(edges[i]),8);total=total is null?item:BezierLengthBounds.Bounds.Add(total,item); }
        return total!;
    }
    static double Line(double ax,double ay,double bx,double by)=>Math.Sqrt((bx-ax)*(bx-ax)+(by-ay)*(by-ay));
    internal static Result Create(string id)
    {
        if(id is not ("synthetic-base" or "synthetic-distinct" or "synthetic-third" or "synthetic-fifth"))throw new ArgumentException("Unsupported or known-mismatched synthetic fixture.");
        var body=SyntheticBodyCatalog.Create(id);var original=BackWaistCandidate.CreateFromBody(body,ResearchDesign.PinnedFitted);var source=original.Edges;
        var backPanel=BackBodiceCandidate.CreateFromBody(body,ResearchDesign.PinnedFitted,false);var frontPanel=FrontBodiceCandidate.CreateFromBody(body,ResearchDesign.PinnedFitted);
        double target=(double)body["waist_back_width"]/2, tolerance=1e-5;
        double rightLimit=(source[6].Start.X-source[6].End.X)*.9;
        if(!double.IsFinite(rightLimit)||rightLimit<=0)throw new ArgumentException("Unsupported side geometry.");
        BackWaistCandidate.Edge[] At(double shift) {
            var edges=source.ToArray();var end=new BackWaistCandidate.P(edges[6].End.X+shift,edges[6].End.Y);
            edges[6]=edges[6] with { End=end };edges[7]=edges[7] with { Start=end };return edges;
        }
        double Trial(double shift) { var e=At(shift);return ApproximateLength(e[0])+ApproximateLength(e[3])+ApproximateLength(e[6]); }
        double low=0,high=rightLimit;if(!(Trial(low)>target && Trial(high)<target))throw new ArgumentException("No numerical bracket within supported side range.");
        for(int i=0;i<56;i++) { double mid=(low+high)/2;if(Trial(mid)>target)low=mid;else high=mid; }
        double delta=(low+high)/2;var proposed=At(delta);var before=Bound(source);var after=Bound(proposed);
        // Exact bounds around represented control points; tolerance is solely
        // a numerical diagnostic, not a physical seam allowance or proof.
        var t=BezierLengthBounds.Enclose([new(0,0),new(target,0)],0);var eps=BezierLengthBounds.Enclose([new(0,0),new(tolerance,0)],0);
        int scale=after.Scale;var center=t.Lower<<scale;var allowed=eps.Lower<<scale;
        if(after.Lower<center-allowed || after.Upper>center+allowed)throw new ArgumentException("Length certificate outside numerical tolerance.");
        if(Enumerable.Range(0,source.Count).Where(i=>i is not (6 or 7)).Any(i=>proposed[i]!=source[i]) ||
           proposed[6].Start!=source[6].Start || proposed[6].RelativeControl!=source[6].RelativeControl ||
           proposed[0].Start!=source[0].Start || proposed[7].End!=source[7].End)
            throw new ArgumentException("Dart or fixed point changed.");
        var panelEdges=backPanel.Edges.Select(e=>new BodyCurveStudy.Edge("right_btorso",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>(c.X,c.Y)).ToArray(),e.ArcControlY)).ToArray();
        var beforeTopology=BodyCurveStudy.Inspect("right_btorso",panelEdges,13);
        if(beforeTopology.Closure!=BodyCurveStudy.ClosureEvidence.ClosedRepresented || beforeTopology.Unresolved!=0 || beforeTopology.NonMonotone!=0)throw new ArgumentException("Baseline contour not proven at represented coordinates.");
        var old6=panelEdges[6];var old7=panelEdges[7];
        panelEdges[6]=old6 with { Bx=proposed[6].End.X,By=proposed[6].End.Y };
        panelEdges[7]=old7 with { Ax=proposed[7].Start.X,Ay=proposed[7].Start.Y };
        var topology=BodyCurveStudy.Inspect("right_btorso",panelEdges,13);
        if(topology.Closure!=BodyCurveStudy.ClosureEvidence.ClosedRepresented || topology.Unresolved!=0 || topology.NonMonotone!=0)throw new ArgumentException("Experimental contour not proven at represented coordinates.");
        var b7=backPanel.Edges[7];var b8=backPanel.Edges[8];
        double beforeSide=Line(b7.Start.X,b7.Start.Y,b7.End.X,b7.End.Y)+Line(b8.Start.X,b8.Start.Y,b8.End.X,b8.End.Y);
        double afterSide=Line(proposed[7].Start.X,proposed[7].Start.Y,proposed[7].End.X,proposed[7].End.Y)+Line(b8.Start.X,b8.Start.Y,b8.End.X,b8.End.Y);
        var f4=frontPanel.Edges[4];var f7=frontPanel.Edges[7];
        double frontSide=Line(f4.Start.X,f4.Start.Y,f4.End.X,f4.End.Y)+Line(f7.Start.X,f7.Start.Y,f7.End.X,f7.End.Y);
        if(!double.IsFinite(delta)||!double.IsFinite(frontSide)||!double.IsFinite(beforeSide)||!double.IsFinite(afterSide))throw new ArgumentException("Non-finite side diagnostic.");
        return new(id,Array.AsReadOnly(proposed),delta,target,before,after,frontSide,beforeSide,afterSide,beforeTopology,topology);
    }
}
