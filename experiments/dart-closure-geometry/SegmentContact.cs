using System.Numerics;
using TekstilCAD.Core;

namespace DartClosureGeometry;

// Exact predicates over the supplied binary64 coordinates; not a bound on
// rotation error or evidence that two physical pieces touch.
public static class SegmentContact
{
    public enum Kind { Disjoint, AllowedEndpoint, Overlap, CrossingOrTouch, OpenEndpoint, Unresolved }
    private static BigInteger I(double v)
    {
        long bits = BitConverter.DoubleToInt64Bits(v); int e=(int)((bits>>52)&0x7ff);
        ulong f=(ulong)bits & 0x000f_ffff_ffff_ffffUL;
        var n=new BigInteger(e==0?f:f|(1UL<<52)) << (e==0?0:e-1);
        return bits<0?-n:n;
    }
    private static int Orient(Point2 a,Point2 b,Point2 c) => ((I(b.X)-I(a.X))*(I(c.Y)-I(a.Y))-(I(b.Y)-I(a.Y))*(I(c.X)-I(a.X))).Sign;
    private static bool On(Point2 p, Point2 a, Point2 b) => Orient(a,b,p)==0 && p.X>=Math.Min(a.X,b.X) && p.X<=Math.Max(a.X,b.X) && p.Y>=Math.Min(a.Y,b.Y) && p.Y<=Math.Max(a.Y,b.Y);
    public static Kind Classify(Point2 a,Point2 b,Point2 c,Point2 d,bool designatedJoin)
    {
        foreach(var p in new[]{a,b,c,d}) if(!p.IsFinite || Math.Abs(p.X)>1_000_000 || Math.Abs(p.Y)>1_000_000) return Kind.Unresolved;
        if(a==b || c==d) return Kind.Unresolved;
        int o1=Orient(a,b,c),o2=Orient(a,b,d),o3=Orient(c,d,a),o4=Orient(c,d,b);
        if(o1==0 && o2==0 && o3==0 && o4==0)
        {
            bool useX=a.X!=b.X;
            double lo=Math.Max(Math.Min(useX?a.X:a.Y,useX?b.X:b.Y),Math.Min(useX?c.X:c.Y,useX?d.X:d.Y));
            double hi=Math.Min(Math.Max(useX?a.X:a.Y,useX?b.X:b.Y),Math.Max(useX?c.X:c.Y,useX?d.X:d.Y));
            if(lo<hi) return Kind.Overlap;
            if(lo==hi) return designatedJoin && b==c ? Kind.AllowedEndpoint : Kind.CrossingOrTouch;
            return designatedJoin ? Kind.OpenEndpoint : Kind.Disjoint;
        }
        bool intersects=o1*o2<0 && o3*o4<0 || o1==0&&On(c,a,b) || o2==0&&On(d,a,b) || o3==0&&On(a,c,d) || o4==0&&On(b,c,d);
        if(intersects) return designatedJoin && b==c ? Kind.AllowedEndpoint : Kind.CrossingOrTouch;
        return designatedJoin ? Kind.OpenEndpoint : Kind.Disjoint;
    }
}
