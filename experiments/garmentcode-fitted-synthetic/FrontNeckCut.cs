namespace FittedResearch;

// Analytic source CircleNeckHalf geometry for the two fixed synthetic cases.
// It does not compute armhole cuts or certify the resulting contour.
internal static class FrontNeckCut
{
    internal readonly record struct P(double X,double Y);
    internal sealed record Arc(P Start,P End,double RelativeControlY);
    internal sealed record Result(string Snapshot,Arc Right,Arc Left,P CenterBottom)
    {
        public string Status=>"unresolved";
        public bool ProductionAllowed=>false;
        public bool ExportAllowed=>false;
    }
    internal static Result Create(string id)
    {
        if(id is not ("synthetic-base" or "synthetic-distinct"))throw new ArgumentException("Unsupported synthetic fixture");
        var body=SyntheticBodyCatalog.Create(id);
        return Calculate(id,body);
    }
    internal sealed class UnverifiedResult
    {
        internal UnverifiedResult(Result result) { Snapshot=result.Snapshot;Right=result.Right;Left=result.Left;CenterBottom=result.CenterBottom; }
        public string Snapshot { get; } public Arc Right { get; } public Arc Left { get; } public P CenterBottom { get; }
        public BackWaistCandidate.ReferenceState ReferenceAssessment=>BackWaistCandidate.ReferenceState.Unverified;
        public string Status=>"unresolved"; public bool ExportAllowed=>false; public bool ProductionAllowed=>false;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Unverified input; composition blocked.");
    }
    internal static UnverifiedResult CreateFromBody(IResearchPatternBody body,ResearchDesign design)
    {
        ResearchBody.RequireSupported(body);
        ArgumentNullException.ThrowIfNull(body);ArgumentNullException.ThrowIfNull(design);
        return new(Calculate(body.Snapshot(design),body));
    }
    private static Result Calculate(string id,IResearchPatternBody body)
    {
        ResearchBody.RequireSupported(body);
        double frontLength=(double)body["waist_over_bust_line"],shoulderWidth=(double)body["shoulder_w"],neckWidth=(double)body["neck_w"],shoulderAngle=(double)body["shoulder_incl"];
        if(neckWidth>=shoulderWidth-4)throw new ArgumentException("Unsupported neck and shoulder geometry");
        const double collarWidthFactor=.2,frontDepthFactor=.4;
        double maxWidth=shoulderWidth-4, collarWidth=neckWidth+(maxWidth-neckWidth)*collarWidthFactor;
        double half=collarWidth/2, tan=Math.Tan(shoulderAngle*Math.PI/180);
        double weightedBust=body.Derive().BustLine;
        double depth=frontDepthFactor*weightedBust+tan*(shoulderWidth/2-half);
        if(!double.IsFinite(depth)||depth<=0||collarWidth<=0)throw new ArgumentException("Invalid neck geometry");
        double top=frontLength+tan*shoulderWidth/2;
        double shoulderY=top-tan*half, centerY=shoulderY-depth;
        if(centerY<=0)throw new ArgumentException("Neck cut leaves front panel");
        // Full circle through (0,0), (width,0), (width/2,-depth).
        double centerYCircle=(half*half-depth*depth)/(2*depth);
        double radius=(half*half+depth*depth)/(2*depth);
        double startAngle=Math.Atan2(-centerYCircle,-half), endAngle=-Math.PI/2;
        double sweep=(endAngle-startAngle)%(2*Math.PI); if(sweep<0)sweep+=2*Math.PI;
        double midAngle=startAngle+sweep/2;
        double mx=half+radius*Math.Cos(midAngle),my=centerYCircle+radius*Math.Sin(midAngle);
        double dx=half,dy=-depth,denom=dx*dx+dy*dy;
        // Source right arc runs opposite the construction above, reversing sign.
        double control=-((-dy*mx+dx*my)/denom);
        if(!double.IsFinite(control))throw new ArgumentException("Invalid neck control");
        return new(id,new(new(0,centerY),new(-half,shoulderY),control),
            new(new(half,shoulderY),new(0,centerY),control),new(0,0));
    }
}
