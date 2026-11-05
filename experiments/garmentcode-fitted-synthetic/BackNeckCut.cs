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
        var b=id switch { "synthetic-base"=>(Back:48d,Waist:37d),"synthetic-distinct"=>(Back:43d,Waist:39d),_=>throw new ArgumentException("Unsupported synthetic fixture") };
        const double shoulder=36,neck=19,angle=22,widthFactor=.2;
        double back=b.Back/2,half=(neck+((shoulder-4)-neck)*widthFactor)/2;
        double tan=Math.Tan(angle*Math.PI/180), depth=tan*(back-half);
        if(!double.IsFinite(depth)||depth<=0)throw new ArgumentException("Invalid neck geometry");
        double top=b.Waist-tan*(back-shoulder/2)+tan*back;
        double shoulderY=top-tan*half,centerY=shoulderY-depth;
        double centerYCircle=(half*half-depth*depth)/(2*depth),radius=(half*half+depth*depth)/(2*depth);
        double a=Math.Atan2(-centerYCircle,-half),end=-Math.PI/2,sweep=(end-a)%(2*Math.PI);if(sweep<0)sweep+=2*Math.PI;
        double mid=a+sweep/2,mx=half+radius*Math.Cos(mid),my=centerYCircle+radius*Math.Sin(mid);
        double dx=half,dy=-depth,control=(-dy*mx+dx*my)/(dx*dx+dy*dy);
        if(!double.IsFinite(control))throw new ArgumentException("Invalid neck arc");
        return new(id,new(new(-half,shoulderY),new(0,centerY),control),new(new(0,centerY),new(half,shoulderY),control),new(0,tan*back/4));
    }
}
