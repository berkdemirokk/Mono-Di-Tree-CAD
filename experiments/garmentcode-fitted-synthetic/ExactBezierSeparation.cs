using System.Numerics;

namespace FittedResearch;

// Exact dyadic convex-hull separation for represented binary64 Bezier
// controls. No float bounds or sampling; budget exhaustion fails closed.
internal static class ExactBezierSeparation
{
    internal readonly record struct Point(double X,double Y);
    internal enum Verdict { Separated, OnlySharedEndpoint, Unresolved }
    readonly record struct Q(BigInteger N,int Scale) : IComparable<Q>
    {
        public int CompareTo(Q other) => (N << Math.Max(0,other.Scale-Scale)).CompareTo(other.N << Math.Max(0,Scale-other.Scale));
        public static Q Mid(Q a,Q b) { int s=Math.Max(a.Scale,b.Scale);return new((a.N<<(s-a.Scale))+(b.N<<(s-b.Scale)),s+1); }
    }
    readonly record struct V(Q X,Q Y);
    sealed record Curve(V[] C,int Depth);
    static BigInteger Integer(double x)
    {
        long bits=BitConverter.DoubleToInt64Bits(x);int e=(int)((bits>>52)&0x7ff);ulong f=(ulong)bits & 0x000f_ffff_ffff_ffffUL;
        var n=new BigInteger(e==0?f:f|(1UL<<52)) << (e==0?0:e-1);
        return bits<0?-n:n;
    }
    static Curve Build(IReadOnlyList<Point> p)
    {
        if(p is null || p.Count is <2 or >4 || p.Any(x=>!double.IsFinite(x.X)||!double.IsFinite(x.Y)||Math.Abs(x.X)>1_000_000||Math.Abs(x.Y)>1_000_000))throw new ArgumentException("Invalid Bezier controls");
        return new(p.Select(v=>new V(new Q(Integer(v.X),0),new Q(Integer(v.Y),0))).ToArray(),0);
    }
    static (Q minX,Q maxX,Q minY,Q maxY) Box(Curve c)
    {
        var x=c.C.Select(p=>p.X).ToArray();var y=c.C.Select(p=>p.Y).ToArray();
        return (x.Min(),x.Max(),y.Min(),y.Max());
    }
    static bool Disjoint(Curve a,Curve b)
    {
        var x=Box(a);var y=Box(b);
        return x.maxX.CompareTo(y.minX)<0 || y.maxX.CompareTo(x.minX)<0 || x.maxY.CompareTo(y.minY)<0 || y.maxY.CompareTo(x.minY)<0;
    }
    static (Curve left,Curve right) Split(Curve c)
    {
        int n=c.C.Length;var work=c.C.ToArray();var left=new V[n];var right=new V[n];left[0]=work[0];right[n-1]=work[n-1];
        for(int r=1;r<n;r++) { for(int i=0;i<n-r;i++) work[i]=new(Q.Mid(work[i].X,work[i+1].X),Q.Mid(work[i].Y,work[i+1].Y));left[r]=work[0];right[n-r-1]=work[n-r-1]; }
        return(new(left,c.Depth+1),new(right,c.Depth+1));
    }
    // Sufficient only: one coordinate is strictly monotone in the interior.
    internal static bool HasMonotoneCoordinate(IReadOnlyList<Point> controls)
    {
        var c=Build(controls);
        bool Axis(Func<V,Q> select)
        {
            var p=c.C.Select(select).ToArray();int sign=p[^1].CompareTo(p[0]);
            if(sign==0)return false;
            // Include every consecutive pair, especially the last control
            // point and the endpoint; a backwards step fails closed.
            for(int i=1;i<p.Length;i++)
                if(sign*p[i].CompareTo(p[i-1])<0)return false;
            return true;
        }
        return Axis(p=>p.X)||Axis(p=>p.Y);
    }
    // Exact represented-coordinate proof. Each curve must leave the shared
    // point strictly along some monotone control axis, so neither can return
    // to it at a different parameter. A separating axis may have one curve
    // entirely on the boundary provided the other is strictly away from it.
    static bool EndpointOnly(Curve a,Curve b)
    {
        var p=a.C[^1];if(p.X.CompareTo(b.C[0].X)!=0||p.Y.CompareTo(b.C[0].Y)!=0)return false;
        int scale=a.C.Concat(b.C).SelectMany(v=>new[]{v.X.Scale,v.Y.Scale}).Max();
        BigInteger N(Q q)=>q.N<<(scale-q.Scale);
        // For two represented line segments, exact integer cross/dot products
        // prove the common endpoint is their sole intersection. Build() uses
        // one common dyadic scale; these predicates never convert to double.
        if(a.C.Length==2 && b.C.Length==2)
        {
            var u=a.C[0];var v=b.C[1];
            BigInteger ux=N(u.X)-N(p.X),uy=N(u.Y)-N(p.Y),vx=N(v.X)-N(p.X),vy=N(v.Y)-N(p.Y);
            if((!ux.IsZero||!uy.IsZero) && (!vx.IsZero||!vy.IsZero))
            {
                BigInteger cross=ux*vy-uy*vx,dot=ux*vx+uy*vy;
                if(!cross.IsZero || dot.Sign<0)return true;
            }
        }

        bool Away(Curve curve,bool sharedAtEnd,Func<V,Q> axis)
        {
            var controls=sharedAtEnd?curve.C[..^1]:curve.C[1..];var value=axis(p);
            var signs=controls.Select(v=>axis(v).CompareTo(value)).ToArray();
            int far=sharedAtEnd?signs[0]:signs[^1];
            return far<0&&signs.All(x=>x<=0) || far>0&&signs.All(x=>x>=0);
        }
        if(!(Away(a,true,v=>v.X)||Away(a,true,v=>v.Y)) || !(Away(b,false,v=>v.X)||Away(b,false,v=>v.Y)))return false;
        bool Axis(Func<V,Q> axis)
        {
            Q value=axis(p);var l=a.C[..^1].Select(axis).Select(x=>x.CompareTo(value)).ToArray();var r=b.C[1..].Select(axis).Select(x=>x.CompareTo(value)).ToArray();
            return l.All(x=>x<=0)&&r.All(x=>x>=0)&&(l[0]<0||r[^1]>0) || l.All(x=>x>=0)&&r.All(x=>x<=0)&&(l[0]>0||r[^1]<0);
        }
        if(Axis(v=>v.X)||Axis(v=>v.Y))return true;
        // A separator through the shared point need not align with X or Y.
        // Align represented dyadic controls to one integer scale, including
        // controls created by subdivision. Use exact integer cross products
        // against each control-point ray: both
        // control polygons must lie in opposite closed half-planes, and one
        // far endpoint must be strictly off the separator. The earlier Away
        // test ensures neither curve can return to the shared point.
        var left=a.C[..^1];var right=b.C[1..];
        foreach(var ray in left.Concat(right)) {
            var dx=N(ray.X)-N(p.X);var dy=N(ray.Y)-N(p.Y);
            if(dx.IsZero&&dy.IsZero)continue;
            BigInteger Cross(V v)=>dx*(N(v.Y)-N(p.Y))-dy*(N(v.X)-N(p.X));
            var l=left.Select(Cross).ToArray();var r=right.Select(Cross).ToArray();
            if(l.All(x=>x.Sign<=0)&&r.All(x=>x.Sign>=0)&&(l[0].Sign<0||r[^1].Sign>0) ||
               l.All(x=>x.Sign>=0)&&r.All(x=>x.Sign<=0)&&(l[0].Sign>0||r[^1].Sign<0))return true;
        }
        return false;
    }
    internal static Verdict Compare(IReadOnlyList<Point> first,IReadOnlyList<Point> second,bool allowedEndpoint,int maxDepth=18,int maxPairs=10000)
    {
        if(maxDepth is <0 or >24 || maxPairs is <1 or >100000)throw new ArgumentOutOfRangeException(nameof(maxDepth));
        var a=Build(first);var b=Build(second);
        if(allowedEndpoint && EndpointOnly(a,b))return Verdict.OnlySharedEndpoint;
        var pending=new Stack<(Curve a,Curve b)>();pending.Push((a,b));int visited=0;bool shared=false;
        while(pending.Count>0)
        {
            if(++visited>maxPairs)return Verdict.Unresolved;
            var pair=pending.Pop();if(Disjoint(pair.a,pair.b))continue;
            if(allowedEndpoint&&pair.a.C[^1].X.CompareTo(a.C[^1].X)==0&&pair.a.C[^1].Y.CompareTo(a.C[^1].Y)==0&&EndpointOnly(pair.a,pair.b)) {shared=true;continue;}
            if(pair.a.Depth>=maxDepth && pair.b.Depth>=maxDepth)return Verdict.Unresolved;
            if(pair.a.Depth<=pair.b.Depth && pair.a.Depth<maxDepth || pair.b.Depth>=maxDepth)
            { var (l,r)=Split(pair.a);pending.Push((l,pair.b));pending.Push((r,pair.b)); }
            else { var (l,r)=Split(pair.b);pending.Push((pair.a,l));pending.Push((pair.a,r)); }
        }
        return shared?Verdict.OnlySharedEndpoint:Verdict.Separated;
    }
}
