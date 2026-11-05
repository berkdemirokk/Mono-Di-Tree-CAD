namespace FittedResearch;
// Analytic back neck on the fixed synthetic sleeveless source design.
internal static class BackNeckCut
{
    internal readonly record struct P(double X,double Y);
    internal sealed record Arc(P Start,P End,double RelativeControlY);
    internal sealed record Result(string Snapshot,Arc Right,Arc Left,P CenterWaist)
    {
        public string Status=>"unresolved"; public bool ProductionAllowed=>false; public bool ExportAllowed=>false;
    }
    internal static Result Create(string id)
    {
        if(id is not ("synthetic-base" or "synthetic-distinct"))throw new ArgumentException("Unsupported synthetic fixture");
        return Calculate(id,SyntheticBodyCatalog.Create(id));
    }
    internal sealed class UnverifiedResult
    {
        internal UnverifiedResult(Result result) { Snapshot=result.Snapshot;Right=result.Right;Left=result.Left;CenterWaist=result.CenterWaist; }
        public string Snapshot { get; } public Arc Right { get; } public Arc Left { get; } public P CenterWaist { get; }
        public BackWaistCandidate.ReferenceState ReferenceAssessment=>BackWaistCandidate.ReferenceState.Unverified;
        public string Status=>"unresolved"; public bool ExportAllowed=>false; public bool ProductionAllowed=>false;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Unverified neck; composition blocked.");
    }
    internal static UnverifiedResult CreateFromBody(ResearchBody body,ResearchDesign design)
    {
        ArgumentNullException.ThrowIfNull(body);ArgumentNullException.ThrowIfNull(design);
        return new(Calculate(body.Snapshot(design),body));
    }
    static Result Calculate(string id,ResearchBody b)
    {
        double shoulder=(double)b["shoulder_w"],neck=(double)b["neck_w"],angle=(double)b["shoulder_incl"],widthFactor=.2;
        if(neck>=shoulder-4)throw new ArgumentException("Unsupported neck and shoulder geometry");
        double back=(double)b["back_width"]/2,half=(neck+((shoulder-4)-neck)*widthFactor)/2;
        double tan=Math.Tan(angle*Math.PI/180), depth=tan*(back-half);
        if(!double.IsFinite(depth)||depth<=0)throw new ArgumentException("Invalid neck geometry");
        double top=(double)b["waist_line"]-tan*(back-shoulder/2)+tan*back;
        double shoulderY=top-tan*half,centerY=shoulderY-depth;
        double centerYCircle=(half*half-depth*depth)/(2*depth),radius=(half*half+depth*depth)/(2*depth);
        double a=Math.Atan2(-centerYCircle,-half),end=-Math.PI/2,sweep=(end-a)%(2*Math.PI);if(sweep<0)sweep+=2*Math.PI;
        double mid=a+sweep/2,mx=half+radius*Math.Cos(mid),my=centerYCircle+radius*Math.Sin(mid);
        double dx=half,dy=-depth,control=(-dy*mx+dx*my)/(dx*dx+dy*dy);
        if(!double.IsFinite(control))throw new ArgumentException("Invalid neck arc");
        return new(id,new(new(-half,shoulderY),new(0,centerY),control),new(new(0,centerY),new(half,shoulderY),control),new(0,tan*back/4));
    }
}
