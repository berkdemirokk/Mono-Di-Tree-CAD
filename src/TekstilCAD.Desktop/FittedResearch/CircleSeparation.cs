using System.Numerics;

namespace FittedResearch;

// Exact rational circle vs represented binary64 Bezier hulls. Certificates
// cover the whole supporting circle (conservative for an arc).
internal static class CircleSeparation
{
    internal readonly record struct Point(double X,double Y);
    internal enum Verdict { Separated, OnlySharedEndpoint, Unresolved }
    readonly struct R : IComparable<R>
    {
        internal readonly BigInteger N,D;
        internal R(BigInteger n,BigInteger d)
        {
            if(d.IsZero)throw new DivideByZeroException();if(d.Sign<0){n=-n;d=-d;}
            var g=BigInteger.GreatestCommonDivisor(BigInteger.Abs(n),d);N=n/g;D=d/g;
        }
        internal static R From(double x)
        {
            if(!double.IsFinite(x) || Math.Abs(x)>1_000_000)throw new ArgumentException("Invalid circle input");
            long bits=BitConverter.DoubleToInt64Bits(x);int e=(int)((bits>>52)&0x7ff);ulong f=(ulong)bits&0x000f_ffff_ffff_ffffUL;
            BigInteger n=new BigInteger(e==0?f:f|(1UL<<52));if(bits<0)n=-n;int power=e==0?-1074:e-1075;
            return power>=0?new(n<<power,BigInteger.One):new(n,BigInteger.One<<(-power));
        }
        public int CompareTo(R r)=>(N*r.D).CompareTo(r.N*D);
        public static R operator +(R a,R b)=>new(a.N*b.D+b.N*a.D,a.D*b.D);
        public static R operator -(R a,R b)=>new(a.N*b.D-b.N*a.D,a.D*b.D);
        public static R operator *(R a,R b)=>new(a.N*b.N,a.D*b.D);
        public static R operator /(R a,R b)=>new(a.N*b.D,a.D*b.N);
        public static R operator -(R a)=>new(-a.N,a.D);
    }
    readonly record struct V(R X,R Y)
    {
        public static V operator +(V a,V b)=>new(a.X+b.X,a.Y+b.Y);
        public static V operator -(V a,V b)=>new(a.X-b.X,a.Y-b.Y);
        public static V operator *(V a,R k)=>new(a.X*k,a.Y*k);
    }
    static readonly R Zero=R.From(0),One=R.From(1),Two=R.From(2);
    static V Exact(Point p)=>new(R.From(p.X),R.From(p.Y));
    static R Dot(V a,V b)=>a.X*b.X+a.Y*b.Y;
    static bool Eq(V a,V b)=>a.X.CompareTo(b.X)==0 && a.Y.CompareTo(b.Y)==0;
    static (V center,R radius2,V start,V end) Circle(Point start,Point end,double relativeY)
    {
        V a=Exact(start),b=Exact(end),d=b-a;R cy=R.From(relativeY);
        if(Eq(a,b)||cy.CompareTo(Zero)==0)throw new ArgumentException("Degenerate circle");
        V mid=(a+b)*(One/Two)+new V(-d.Y,d.X)*cy;
        R cross=Two*(a.X*(mid.Y-b.Y)+mid.X*(b.Y-a.Y)+b.X*(a.Y-mid.Y));
        if(cross.CompareTo(Zero)==0)throw new ArgumentException("Degenerate circle");
        R aa=Dot(a,a),mm=Dot(mid,mid),bb=Dot(b,b);
        V c=new((aa*(mid.Y-b.Y)+mm*(b.Y-a.Y)+bb*(a.Y-mid.Y))/cross,(aa*(b.X-mid.X)+mm*(a.X-b.X)+bb*(mid.X-a.X))/cross);
        return(c,Dot(a-c,a-c),a,b);
    }
    internal static Verdict Compare(Point arcStart,Point arcEnd,double relativeY,IReadOnlyList<Point> controls,bool allowedEndpoint)
    {
        if(controls is null || controls.Count is <2 or >4)throw new ArgumentException("Invalid controls");
        var circle=Circle(arcStart,arcEnd,relativeY);var points=controls.Select(Exact).ToArray();
        R minX=points.Select(x=>x.X).Min(),maxX=points.Select(x=>x.X).Max(),minY=points.Select(x=>x.Y).Min(),maxY=points.Select(x=>x.Y).Max();
        R DistAxis(R v,R lo,R hi)=>v.CompareTo(lo)<0?lo-v:v.CompareTo(hi)>0?v-hi:Zero;
        R nearX=DistAxis(circle.center.X,minX,maxX),nearY=DistAxis(circle.center.Y,minY,maxY);
        if((nearX*nearX+nearY*nearY).CompareTo(circle.radius2)>0)return Verdict.Separated;
        R Far(R v,R lo,R hi) { R a=v-lo,b=v-hi;return (a*a).CompareTo(b*b)>=0?a*a:b*b; }
        if((Far(circle.center.X,minX,maxX)+Far(circle.center.Y,minY,maxY)).CompareTo(circle.radius2)<0)return Verdict.Separated;
        if(allowedEndpoint && points.Length==2)
        {
            V a=points[0],b=points[1];
            bool aCommon=Eq(a,circle.start)||Eq(a,circle.end),bCommon=Eq(b,circle.start)||Eq(b,circle.end);
            if(aCommon ^ bCommon)
            {
                V p=aCommon?a:b,q=aCommon?b:a,v=q-p;R A=Dot(v,v),B=Two*Dot(p-circle.center,v);
                if(A.CompareTo(Zero)>0 && (B.CompareTo(Zero)>=0 || (A+B).CompareTo(Zero)<0))return Verdict.OnlySharedEndpoint;
            }
        }
        return Verdict.Unresolved;
    }
}
