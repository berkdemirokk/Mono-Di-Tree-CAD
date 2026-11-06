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
    // Sufficient crossing witness for a minor arc: the entire Bezier control
    // hull lies on the arc's chord side, and two exact dyadic-parameter points
    // have strictly opposite signs of the supporting-circle equation. By
    // continuity an interior point lies on the circle and on the selected arc.
    // False means no proof, not separation.
    internal static bool ProveMinorArcCrossing(Point arcStart,Point arcEnd,double relativeY,IReadOnlyList<Point> controls)
    {
        if(!double.IsFinite(relativeY)||relativeY==0||Math.Abs(relativeY)>.5||controls is null||controls.Count is <2 or >4)throw new ArgumentException("Unsupported arc crossing input");
        var c=Circle(arcStart,arcEnd,relativeY);var p=controls.Select(Exact).ToArray();var chord=c.end-c.start;
        R Cross(V a,V b)=>a.X*b.Y-a.Y*b.X;
        int side=Math.Sign(relativeY);
        var sides=p.Select(v=>side*Cross(chord,v-c.start).CompareTo(Zero)).ToArray();
        if(sides.Any(s=>s<0)||!sides.Any(s=>s>0))return false;
        R Evaluate(int i) {
            var t=new R(i,256);var u=One-t;var values=p.ToArray();
            for(int n=values.Length-1;n>0;n--)for(int j=0;j<n;j++)values[j]=values[j]*u+values[j+1]*t;
            var v=values[0]-c.center;return Dot(v,v)-c.radius2;
        }
        bool positive=false,negative=false;
        for(int i=1;i<256;i++) {int sign=Evaluate(i).CompareTo(Zero);positive|=sign>0;negative|=sign<0;if(positive&&negative)return true;}
        return false;
    }
    // Exact Bernstein certificate for F(t)=|B(t)-center|²-radius².
    // It covers the entire supporting circle, hence is conservative for an
    // arc. Only the original common endpoint may be zero. Budget exhaustion,
    // an internal zero, or mixed signs fail closed.
    internal static Verdict QuadraticEndpointProof(Point arcStart,Point arcEnd,double relativeY,IReadOnlyList<Point> controls,int maxDepth=16,int maxNodes=4096)
    {
        if(controls is null||controls.Count!=3||maxDepth<0||maxDepth>24||maxNodes<1||maxNodes>100000)throw new ArgumentException("Unsupported quadratic proof input");
        var c=Circle(arcStart,arcEnd,relativeY);var p=controls.Select(Exact).ToArray();
        bool start=Eq(p[0],c.start)||Eq(p[0],c.end),end=Eq(p[2],c.start)||Eq(p[2],c.end);
        if(start==end)return Verdict.Unresolved;
        var q=p.Select(v=>v-c.center).ToArray();int[] w2=[1,2,1],w4=[1,4,6,4,1];var coeff=new R[5];
        for(int k=0;k<=4;k++) {
            R sum=Zero;
            for(int i=0;i<=2;i++) {int j=k-i;if(j is <0 or >2)continue;sum=sum+Dot(q[i],q[j])*new R(w2[i]*w2[j],w4[k]);}
            coeff[k]=sum-c.radius2;
        }
        int sign=(start?coeff[^1]:coeff[0]).CompareTo(Zero);
        if(sign==0 || (start?coeff[0]:coeff[^1]).CompareTo(Zero)!=0)return Verdict.Unresolved;
        var stack=new Stack<(R[] coefficients,int depth,bool start,bool end)>();stack.Push((coeff,0,true,true));int visited=0;
        while(stack.Count>0) {
            if(++visited>maxNodes)return Verdict.Unresolved;
            var (v,depth,first,last)=stack.Pop();
            int lo=sign*v[0].CompareTo(Zero),hi=sign*v[^1].CompareTo(Zero);
            if(lo<0||hi<0||lo==0&&!(start&&first)||hi==0&&!(end&&last))return Verdict.Unresolved;
            if(v.All(x=>sign*x.CompareTo(Zero)>=0) && v.Any(x=>sign*x.CompareTo(Zero)>0))continue;
            if(depth>=maxDepth)return Verdict.Unresolved;
            var work=v.ToArray();var l=new R[5];var r=new R[5];l[0]=work[0];r[4]=work[4];
            for(int level=1;level<5;level++) {for(int j=0;j<5-level;j++)work[j]=(work[j]+work[j+1])/Two;l[level]=work[0];r[4-level]=work[4-level];}
            stack.Push((r,depth+1,false,last));stack.Push((l,depth+1,first,false));
        }
        return Verdict.OnlySharedEndpoint;
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
        if(allowedEndpoint && points.Length==3) {
            var certified=QuadraticEndpointProof(arcStart,arcEnd,relativeY,controls);
            if(certified==Verdict.OnlySharedEndpoint)return certified;
        }
        return Verdict.Unresolved;
    }
}
