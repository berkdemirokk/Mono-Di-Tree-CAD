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
        double frontLength=id switch { "synthetic-base"=>41,"synthetic-distinct"=>42,_=>throw new ArgumentException("Unsupported synthetic fixture") };
        const double shoulderWidth=36,neckWidth=19,vertBustLine=21,bustLine=26,shoulderAngle=22;
        const double collarWidthFactor=.2,frontDepthFactor=.4;
        double maxWidth=shoulderWidth-4, collarWidth=neckWidth+(maxWidth-neckWidth)*collarWidthFactor;
        double half=collarWidth/2, tan=Math.Tan(shoulderAngle*Math.PI/180);
        double weightedBust=vertBustLine*2/3+bustLine/3;
        double depth=frontDepthFactor*weightedBust+tan*(shoulderWidth/2-half);
        if(!double.IsFinite(depth)||depth<=0||collarWidth<=0)throw new ArgumentException("Invalid neck geometry");
        double top=frontLength+tan*shoulderWidth/2;
        double shoulderY=top-tan*half, centerY=shoulderY-depth;
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
