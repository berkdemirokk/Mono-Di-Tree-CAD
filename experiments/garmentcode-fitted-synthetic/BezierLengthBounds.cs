using System.Numerics;

namespace FittedResearch;

// Exact dyadic chord/control-polygon enclosure for the represented binary64
// control points. It does not enclose earlier floating-point computations.
internal static class BezierLengthBounds
{
    internal readonly record struct Point(double X,double Y);
    readonly record struct Q(BigInteger N,int Scale)
    {
        public static Q Mid(Q a,Q b) { int s=Math.Max(a.Scale,b.Scale);return new((a.N<<(s-a.Scale))+(b.N<<(s-b.Scale)),s+1); }
    }
    readonly record struct V(Q X,Q Y);
    internal sealed record Bounds(BigInteger Lower,BigInteger Upper,int Scale)
    {
        // Integers are in units 2^(-1074-Scale) cm.
        public bool Contains(double value)
        {
            if(!double.IsFinite(value)||value<0)return false;
            BigInteger n=Scaled(value)<<Scale;return n>=Lower&&n<=Upper;
        }
        public double ApproximateWidthCm=>Approx(Upper-Lower,Scale);
        public double ApproximateLowerCm=>Approx(Lower,Scale);
        public double ApproximateUpperCm=>Approx(Upper,Scale);
        public static Bounds Add(Bounds a,Bounds b) { int s=Math.Max(a.Scale,b.Scale);return new((a.Lower<<(s-a.Scale))+(b.Lower<<(s-b.Scale)),(a.Upper<<(s-a.Scale))+(b.Upper<<(s-b.Scale)),s); }
        private static double Approx(BigInteger x,int scale)
        {
            if(x.IsZero)return 0;int shift=(int)Math.Max(0,x.GetBitLength()-52);
            return Math.ScaleB((double)(x>>shift),shift-1074-scale);
        }
    }
    static BigInteger Scaled(double x)
    {
        if(!double.IsFinite(x))throw new ArgumentException("Invalid represented value");
        long bits=BitConverter.DoubleToInt64Bits(x);int e=(int)((bits>>52)&0x7ff);ulong f=(ulong)bits&0x000f_ffff_ffff_ffffUL;
        BigInteger n=new BigInteger(e==0?f:f|(1UL<<52)) << (e==0?0:e-1);
        return bits<0?-n:n;
    }
    static BigInteger SqrtFloor(BigInteger n)
    {
        if(n.Sign<0)throw new ArgumentOutOfRangeException(nameof(n));if(n.IsZero)return 0;
        BigInteger x=BigInteger.One << (int)((n.GetBitLength()+1)/2);
        while(true) { BigInteger y=(x+n/x)>>1;if(y>=x)return x;x=y; }
    }
    static (BigInteger lo,BigInteger hi) Distance(V a,V b,int scale)
    {
        BigInteger x=(a.X.N<<(scale-a.X.Scale))-(b.X.N<<(scale-b.X.Scale));
        BigInteger y=(a.Y.N<<(scale-a.Y.Scale))-(b.Y.N<<(scale-b.Y.Scale));
        BigInteger square=x*x+y*y,root=SqrtFloor(square);return(root,root*root==square?root:root+1);
    }
    static (V[] left,V[] right) Split(V[] controls)
    {
        int n=controls.Length;var work=controls.ToArray();var left=new V[n];var right=new V[n];left[0]=work[0];right[n-1]=work[n-1];
        for(int r=1;r<n;r++) { for(int i=0;i<n-r;i++) work[i]=new(Q.Mid(work[i].X,work[i+1].X),Q.Mid(work[i].Y,work[i+1].Y));left[r]=work[0];right[n-r-1]=work[n-r-1]; }
        return(left,right);
    }
    internal static Bounds Enclose(IReadOnlyList<Point> points,int depth=8)
    {
        if(points is null||points.Count is <2 or >4)throw new ArgumentException("Expected line, quadratic, or cubic controls");
        if(depth is <0 or >12)throw new ArgumentOutOfRangeException(nameof(depth));
        if(points.Any(p=>Math.Abs(p.X)>1_000_000||Math.Abs(p.Y)>1_000_000))throw new ArgumentException("Coordinate magnitude exceeds numeric bound");
        var initial=points.Select(p=>new V(new Q(Scaled(p.X),0),new Q(Scaled(p.Y),0))).ToArray();
        int scale=(points.Count-1)*depth;var pending=new Stack<(V[] c,int d)>();pending.Push((initial,0));
        BigInteger lo=0,hi=0;
        while(pending.Count>0)
        {
            var (c,d)=pending.Pop();
            if(d<depth) { var (a,b)=Split(c);pending.Push((a,d+1));pending.Push((b,d+1));continue; }
            lo+=Distance(c[0],c[^1],scale).lo;
            for(int i=1;i<c.Length;i++)hi+=Distance(c[i-1],c[i],scale).hi;
        }
        return new(lo,hi,scale);
    }
}
