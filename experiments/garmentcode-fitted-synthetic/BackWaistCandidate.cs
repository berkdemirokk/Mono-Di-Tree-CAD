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
    internal sealed record Path(string Snapshot,IReadOnlyList<Edge> Edges,IReadOnlyList<GeneratedGraph.Seam> DartRules,Trace SourceSteps)
    {
        public string Status=>"unresolved";public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
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
        var body=id switch { "synthetic-base"=>(Back:48d,WaistBack:36d,WaistLine:37d,Angle:22d,Bum:18d,Vert:21d,Bust:26d),"synthetic-distinct"=>(Back:43d,WaistBack:34d,WaistLine:39d,Angle:22d,Bum:18d,Vert:21d,Bust:26d),"synthetic-third"=>(Back:50d,WaistBack:38d,WaistLine:38d,Angle:18d,Bum:19d,Vert:22d,Bust:25d),"synthetic-small-difference"=>(Back:44d,WaistBack:38d,WaistLine:40d,Angle:30d,Bum:18d,Vert:22d,Bust:24d),_=>throw new ArgumentException("Unsupported synthetic case") };
        const double shoulder=18; double angle=body.Angle,bumPoints=body.Bum,vertBust=body.Vert,bust=body.Bust;
        double width=body.Back/2,waist=body.WaistBack/2,tan=Math.Tan(angle*Math.PI/180),h=tan*width/4;
        double diff=width-waist,side=diff<4?0:diff/6,dartWidth=(diff-side)/2;
        double length=body.WaistLine-tan*(width-shoulder),bustLine=2*vertBust/3+bust/3;
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
        P sideBottom=new(-width+side,0),sideMiddle=new(-width,body.WaistLine-bustLine),sideTop=new(-width,length),top=new(0,length+tan*width);
        L(7,sideBottom,sideMiddle);L(8,sideMiddle,sideTop);L(9,sideTop,top);L(10,top,start);
        var seams=new[]{new GeneratedGraph.Seam(id,"precut_back",4,false,"precut_back",5,false),new GeneratedGraph.Seam(id,"precut_back",1,false,"precut_back",2,false)};
        return new(id,edges.AsReadOnly(),Array.AsReadOnly(seams),new(start,end,rel,dartWidth,outerOffset,innerOffset,.9*(length-bustLine),length-bustLine));
    }
}
