namespace FittedResearch;

// Research-only composition of the waist-side and quadratic side experiments.
// The result is not a garment graph and cannot be exported or manufactured.
internal static class CombinedWaistCurvatureExperiment
{
    internal sealed record Result(string Id,double WaistTarget,BezierLengthBounds.Bounds WaistBefore,BezierLengthBounds.Bounds WaistAfter,BezierLengthBounds.Bounds FrontSide,BezierLengthBounds.Bounds BackBefore,BezierLengthBounds.Bounds BackAfter,double WaistShift,double WaistShapeBound,double SideLowerShapeBound,double SideUpperShapeBound,BodyCurveStudy.Result FrontBeforeTopology,BodyCurveStudy.Result FrontAfterTopology,BodyCurveStudy.Result BackBeforeTopology,BodyCurveStudy.Result BackAfterTopology)
    {
        public string Status=>"unresolved"; public bool ProductionAllowed=>false; public bool ExportAllowed=>false;
        public BackWaistCandidate.ReferenceState ReferenceAssessment=>BackWaistCandidate.ReferenceState.Unverified;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Combined candidate cannot be composed.");
    }
    static BezierLengthBounds.Bounds Length(BodyCurveStudy.Edge edge)
    {
        var p=BodyCurveStudy.BezierControls(edge) ?? throw new ArgumentException("Unsupported length edge");
        return BezierLengthBounds.Enclose(p.Select(x=>new BezierLengthBounds.Point(x.X,x.Y)).ToArray(),p.Length==2?0:10);
    }
    static BezierLengthBounds.Bounds Sum(params BodyCurveStudy.Edge[] edges)=>edges.Select(Length).Aggregate(BezierLengthBounds.Bounds.Add);
    static double Mid(BezierLengthBounds.Bounds a)=>(a.ApproximateLowerCm+a.ApproximateUpperCm)/2;
    static bool Good(BodyCurveStudy.Result x)=>x.Closure==BodyCurveStudy.ClosureEvidence.ClosedRepresented&&x.Unresolved==0&&x.NonMonotone==0;
    // Convex-hull bound on same-parameter deviation between represented
    // Beziers of equal degree; floating-point diagnostic, not outward-rounded.
    static double ControlDifference(BodyCurveStudy.Edge a,BodyCurveStudy.Edge b)
    {
        var x=BodyCurveStudy.BezierControls(a) ?? throw new ArgumentException("Unsupported original deviation");
        var y=BodyCurveStudy.BezierControls(b) ?? throw new ArgumentException("Unsupported proposed deviation");
        if(x.Length!=y.Length)throw new ArgumentException("Unequal degrees");
        return x.Zip(y,(p,q)=>Math.Sqrt((p.X-q.X)*(p.X-q.X)+(p.Y-q.Y)*(p.Y-q.Y))).Max();
    }
    internal static Result Create(string id,double waistLimitCm,double curvatureLimitCm)
    {
        if(!double.IsFinite(waistLimitCm)||waistLimitCm<0||waistLimitCm>3||!double.IsFinite(curvatureLimitCm)||curvatureLimitCm<0||curvatureLimitCm>3)throw new ArgumentException("Unsupported assumed shape limit");
        var waist=BackWaistSideExperiment.Create(id); // accepted IDs and waist length certificate
        if(Math.Abs(waist.ShiftCm)>waistLimitCm)throw new ArgumentException("Waist endpoint limit exceeded");
        var body=SyntheticBodyCatalog.Create(id);var f=FrontBodiceCandidate.CreateFromBody(body,ResearchDesign.PinnedFitted);var b=BackBodiceCandidate.CreateFromBody(body,ResearchDesign.PinnedFitted,false);
        var front=f.Edges.Select(e=>new BodyCurveStudy.Edge("right_ftorso",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>(c.X,c.Y)).ToArray(),e.ArcControlY)).ToArray();
        var original=b.Edges.Select(e=>new BodyCurveStudy.Edge("right_btorso",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>(c.X,c.Y)).ToArray(),e.ArcControlY)).ToArray();
        if(front[4].Kind!="L"||front[7].Kind!="L"||original[7].Kind!="L"||original[8].Kind!="L"||original[6].Kind!="Q")throw new ArgumentException("Unsupported contour family");
        var frontBefore=BodyCurveStudy.Inspect("right_ftorso",front,12);var backBefore=BodyCurveStudy.Inspect("right_btorso",original,13);
        if(!Good(frontBefore)||!Good(backBefore))throw new ArgumentException("Baseline topology not established");
        var changed=original.ToArray();changed[6]=waist.TopologyWaistEdge;BackWaistSideExperiment.ValidateTopologyWaistEdge(waist.Edges[6],changed[6]);
        changed[7]=changed[7] with {Ax=waist.Edges[7].Start.X,Ay=waist.Edges[7].Start.Y};
        var o=original[8];double dx=o.Bx-o.Ax,dy=o.By-o.Ay,len=Math.Sqrt(dx*dx+dy*dy);
        if(!double.IsFinite(len)||len<=0)throw new ArgumentException("Degenerate side");
        BodyCurveStudy.Edge Curve(double h)=>o with {Kind="Q",RelativeControls=new[]{(0.5,2*h/len)}};
        var frontSide=Sum(front[4],front[7]);var beforeSide=Sum(original[7],original[8]);var target=Mid(frontSide);var lowerSide=Mid(Length(changed[7]));
        double Estimate(double h) {const int n=128;double sum=0;for(int i=0;i<=n;i++){double t=(double)i/n;double v=Math.Sqrt(len*len+16*h*h*(1-2*t)*(1-2*t));sum+=(i==0||i==n?1:i%2==0?2:4)*v;}return lowerSide+sum/(3*n);}
        double lo=0,hi=curvatureLimitCm;
        if(Estimate(lo)>target||Estimate(hi)<target)throw new ArgumentException("Approximate curvature search found no candidate in assumed range");
        for(int i=0;i<45;i++){double m=(lo+hi)/2;if(Estimate(m)<target)lo=m;else hi=m;}
        changed[8]=Curve((lo+hi)/2);
        var waistAfter=Sum(changed[0],changed[3],changed[6]);var waistBefore=Sum(original[0],original[3],original[6]);
        double waistTarget=(double)body["waist_back_width"]/2;
        var targetBound=BezierLengthBounds.Enclose([new(0,0),new(waistTarget,0)],0);
        if(!BackWaistJointSideExperiment.WithinDiagnosticTolerance(waistAfter,targetBound,1e-5))throw new ArgumentException("Combined waist length rejected");
        var afterSide=Sum(changed[7],changed[8]);
        if(!BackWaistJointSideExperiment.WithinDiagnosticTolerance(frontSide,afterSide,1e-4))throw new ArgumentException("Combined side length rejected");
        // All other represented edges, including both darts, remain unchanged.
        if(Enumerable.Range(0,original.Length).Where(i=>i is not (6 or 7 or 8)).Any(i=>!ReferenceEquals(changed[i],original[i]))||changed[6].Ax!=original[6].Ax||changed[6].Ay!=original[6].Ay||changed[7].Bx!=original[7].Bx||changed[7].By!=original[7].By||changed[8].Ax!=o.Ax||changed[8].Ay!=o.Ay||changed[8].Bx!=o.Bx||changed[8].By!=o.By)throw new ArgumentException("Unexpected endpoint or dart change");
        double waistShape=ControlDifference(original[6],changed[6]);double lowerShape=ControlDifference(original[7],changed[7]);
        var c=BodyCurveStudy.BezierControls(changed[8])!;double mx=(o.Ax+o.Bx)/2,my=(o.Ay+o.By)/2;
        double upperShape=Math.Sqrt(Math.Pow((c[1].X-mx)/2,2)+Math.Pow((c[1].Y-my)/2,2));
        if(!double.IsFinite(waistShape)||!double.IsFinite(lowerShape)||!double.IsFinite(upperShape)||waistShape>waistLimitCm+1e-12||lowerShape>waistLimitCm+1e-12||upperShape>curvatureLimitCm+1e-12)throw new ArgumentException("Assumed represented shape limit exceeded");
        var frontAfter=BodyCurveStudy.Inspect("right_ftorso",front,12);var backAfter=BodyCurveStudy.Inspect("right_btorso",changed,13);
        if(!Good(frontAfter)||!Good(backAfter))throw new ArgumentException("Combined contour topology not established");
        return new(id,waistTarget,waistBefore,waistAfter,frontSide,beforeSide,afterSide,waist.ShiftCm,waistShape,lowerShape,upperShape,frontBefore,frontAfter,backBefore,backAfter);
    }
}
