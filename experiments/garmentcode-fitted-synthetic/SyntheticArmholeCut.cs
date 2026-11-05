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
        var body=id switch { "synthetic-base"=>(Back:48d,Waist:37d,Front:41d), "synthetic-distinct"=>(Back:43d,Waist:39d,Front:42d), _=>throw new ArgumentException("Unknown synthetic case") };
        const double shoulderHalf=18, armscyeDepth=13,connectFactor=.2,shoulderAngle=22;
        var precut=PrecutFrontBodice.Create(id).Vertices;
        P frontLow=new(precut[7].X,precut[7].Y),frontTop=new(precut[8].X,precut[8].Y),frontCenter=new(precut[9].X,precut[9].Y);
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
        P frontSide=frontLow+sideVector*u,frontShoulder=frontTop+shoulderVector*t;
        double backWidth=body.Back/2,tan=Math.Tan(shoulderAngle*Math.PI/180);
        double backTop=body.Waist-tan*(backWidth-shoulderHalf);
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
}
