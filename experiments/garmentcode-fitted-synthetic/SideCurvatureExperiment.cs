namespace FittedResearch;

// A restricted synthetic family: fixed endpoints and darts; one straight back
// side edge becomes a quadratic. This is not a garment graph or production input.
internal static class SideCurvatureExperiment
{
    internal sealed record Result(string Id,double LimitCm,double DisplacementCm,double VirtualNotchShiftCm,BezierLengthBounds.Bounds Front,BezierLengthBounds.Bounds Before,BezierLengthBounds.Bounds After,BodyCurveStudy.Result FrontTopology,BodyCurveStudy.Result BackTopology)
    {
        public string Status=>"unresolved";public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
    }
    static BezierLengthBounds.Bounds Length(BodyCurveStudy.Edge edge,int depth=10)
    {
        var p=BodyCurveStudy.BezierControls(edge) ?? throw new ArgumentException("Unsupported seam curve");
        return BezierLengthBounds.Enclose(p.Select(x=>new BezierLengthBounds.Point(x.X,x.Y)).ToArray(),p.Length==2?0:depth);
    }
    static BezierLengthBounds.Bounds Sum(BodyCurveStudy.Edge a,BodyCurveStudy.Edge b)=>BezierLengthBounds.Bounds.Add(Length(a),Length(b));
    internal static Result Create(string id,double limitCm)
    {
        if(id is not ("synthetic-base" or "synthetic-distinct" or "synthetic-third" or "synthetic-fifth"))throw new ArgumentException("Unsupported synthetic body");
        if(!double.IsFinite(limitCm)||limitCm<0||limitCm>3)throw new ArgumentException("Unsupported research limit");
        var body=SyntheticBodyCatalog.Create(id);var f=FrontBodiceCandidate.CreateFromBody(body,ResearchDesign.PinnedFitted);var b=BackBodiceCandidate.CreateFromBody(body,ResearchDesign.PinnedFitted,false);
        var front=f.Edges.Select(e=>new BodyCurveStudy.Edge("right_ftorso",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>(c.X,c.Y)).ToArray(),e.ArcControlY)).ToArray();
        var back=b.Edges.Select(e=>new BodyCurveStudy.Edge("right_btorso",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>(c.X,c.Y)).ToArray(),e.ArcControlY)).ToArray();
        if(front[4].Kind!="L"||front[7].Kind!="L"||back[7].Kind!="L"||back[8].Kind!="L")throw new ArgumentException("Unsupported source sides");
        var target=Sum(front[4],front[7]);var before=Sum(back[7],back[8]);
        var original=back[8];double dx=original.Bx-original.Ax,dy=original.By-original.Ay,len=Math.Sqrt(dx*dx+dy*dy);
        if(!double.IsFinite(len)||len<=0)throw new ArgumentException("Degenerate source side");
        BodyCurveStudy.Edge Curve(double h)=>original with {Kind="Q",RelativeControls=new[]{(0.5,2*h/len)}};
        double Mid(BezierLengthBounds.Bounds x)=>(x.ApproximateLowerCm+x.ApproximateUpperCm)/2;
        if(Mid(target)<Mid(before))throw new ArgumentException("Restricted family only lengthens back side");
        double lo=0,hi=limitCm;
        // Simpson integration selects a candidate only; the final represented
        // curve is independently checked with dyadic length enclosures.
        double Estimate(double h) {
            const int n=128;double sum=0;
            for(int i=0;i<=n;i++) {double t=(double)i/n;double speed=Math.Sqrt(len*len+16*h*h*(1-2*t)*(1-2*t));sum+=(i==0||i==n?1:i%2==0?2:4)*speed;}
            return Mid(Length(back[7]))+sum/(3*n);
        }
        if(Estimate(hi)<Mid(target))throw new ArgumentException("No solution found within curvature limit");
        for(int i=0;i<45;i++){double m=(lo+hi)/2;if(Estimate(m)<Mid(target))lo=m;else hi=m;}
        back[8]=Curve((lo+hi)/2);var after=Sum(back[7],back[8]);
        if(!BackWaistJointSideExperiment.WithinDiagnosticTolerance(target,after,0.0001))throw new ArgumentException("Numerical side-length enclosure exceeds diagnostic tolerance");
        var ft=BodyCurveStudy.Inspect("right_ftorso",front,12);var bt=BodyCurveStudy.Inspect("right_btorso",back,13);
        if(ft.Closure!=BodyCurveStudy.ClosureEvidence.ClosedRepresented||bt.Closure!=BodyCurveStudy.ClosureEvidence.ClosedRepresented||ft.Unresolved!=0||bt.Unresolved!=0||ft.NonMonotone!=0||bt.NonMonotone!=0)throw new ArgumentException("Topology not established");
        // Same-parameter quadratic displacement has its maximum at t=1/2.
        // The virtual midpoint is diagnostic only: no real notch is supplied.
        var controls=BodyCurveStudy.BezierControls(back[8])!;double mx=(original.Ax+original.Bx)/2,my=(original.Ay+original.By)/2;
        double shift=Math.Sqrt(Math.Pow((controls[1].X-mx)/2,2)+Math.Pow((controls[1].Y-my)/2,2));
        if(!double.IsFinite(shift)||shift>limitCm+1e-12)throw new ArgumentException("Represented curvature exceeds limit");
        return new(id,limitCm,shift,shift,target,before,after,ft,bt);
    }
}
