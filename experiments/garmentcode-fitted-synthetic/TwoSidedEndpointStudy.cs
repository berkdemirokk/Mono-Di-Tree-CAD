using System.Numerics;
namespace FittedResearch;

// Fixed-design synthetic feasibility study. Only horizontal displacement of
// front and back waist-side endpoints is allowed; no candidate is emitted.
internal static class TwoSidedEndpointStudy
{
    internal readonly record struct Point(double X,double Y);
    internal readonly record struct Segment(Point A,Point B);
    internal sealed record Interval(BigInteger Lower,BigInteger Upper)
    {
        // The exact bounds are multiples of 2^-1074 cm; display is approximate.
        static double Approx(BigInteger value) { if(value.IsZero)return 0;int shift=(int)Math.Max(0,value.GetBitLength()-52);return Math.ScaleB((double)(value>>shift),shift-1074); }
        public double ApproximateLowerCm=>Approx(Lower);public double ApproximateUpperCm=>Approx(Upper);
        public bool Disjoint(Interval other)=>Lower>other.Upper || other.Lower>Upper;
    }
    internal sealed record Result(string Id,double AssumedLimitCm,Interval Front,Interval Back,double ProvenMaxDisplacementLowerBoundCm)
    {
        public bool SideEqualityImpossibleWithinLimit=>Front.Disjoint(Back);
        public string Status=>"unresolved";public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Unverified endpoint study cannot be composed.");
    }
    static BigInteger Scaled(double x)
    {
        if(!double.IsFinite(x)||Math.Abs(x)>1_000_000)throw new ArgumentException("Unsupported coordinate.");
        long bits=BitConverter.DoubleToInt64Bits(x);int exponent=(int)((bits>>52)&0x7ff);ulong fraction=(ulong)bits&0x000f_ffff_ffff_ffffUL;
        BigInteger value=new BigInteger(exponent==0?fraction:fraction|(1UL<<52)) << (exponent==0?0:exponent-1);
        return bits<0?-value:value;
    }
    static BigInteger SqrtFloor(BigInteger n)
    {
        if(n.Sign<0)throw new ArgumentOutOfRangeException(nameof(n));if(n.IsZero)return 0;
        BigInteger x=BigInteger.One<<(int)((n.GetBitLength()+1)/2);
        while(true) { BigInteger y=(x+n/x)>>1;if(y>=x)return x;x=y; }
    }
    static (BigInteger lower,BigInteger upper) Distance(BigInteger x,BigInteger y)
    {
        var square=x*x+y*y;var root=SqrtFloor(square);return(root,root*root==square?root:root+1);
    }
    internal static Interval SideBounds(Segment stationary,Segment variable,double limitCm)
    {
        if(!double.IsFinite(limitCm)||limitCm<0||limitCm>1000)throw new ArgumentException("Unsupported displacement limit.");
        var limit=Scaled(limitCm);var fixedX=Scaled(stationary.B.X)-Scaled(stationary.A.X);var fixedY=Scaled(stationary.B.Y)-Scaled(stationary.A.Y);
        var dx=BigInteger.Abs(Scaled(variable.B.X)-Scaled(variable.A.X));var dy=Scaled(variable.B.Y)-Scaled(variable.A.Y);
        if((fixedX.IsZero&&fixedY.IsZero)||(dx.IsZero&&dy.IsZero))throw new ArgumentException("Degenerate side edge.");
        var lowX=BigInteger.Max(BigInteger.Zero,dx-limit);var highX=dx+limit;
        var fixedLength=Distance(fixedX,fixedY);var low=Distance(lowX,dy);var high=Distance(highX,dy);
        return new(fixedLength.lower+low.lower,fixedLength.upper+high.upper);
    }
    internal static (Interval front,Interval back) AssessSegments(Segment frontFixed,Segment frontVariable,Segment backFixed,Segment backVariable,double limitCm)
        =>(SideBounds(frontFixed,frontVariable,limitCm),SideBounds(backFixed,backVariable,limitCm));
    internal static Result Create(string id,double assumedLimitCm)
    {
        if(id is not ("synthetic-base" or "synthetic-distinct" or "synthetic-third" or "synthetic-fifth"))throw new ArgumentException("Unsupported or known-mismatched synthetic fixture.");
        var body=SyntheticBodyCatalog.Create(id);var front=FrontBodiceCandidate.CreateFromBody(body,ResearchDesign.PinnedFitted);var back=BackBodiceCandidate.CreateFromBody(body,ResearchDesign.PinnedFitted,false);
        Segment F(int i) { var e=front.Edges[i];if(e.Kind!="L")throw new ArgumentException("Unsupported front seam");return new(new(e.Start.X,e.Start.Y),new(e.End.X,e.End.Y)); }
        Segment B(int i) { var e=back.Edges[i];if(e.Kind!="L")throw new ArgumentException("Unsupported back seam");return new(new(e.Start.X,e.Start.Y),new(e.End.X,e.End.Y)); }
        var ff=F(4);var fv=F(7);var bf=B(8);var bv=B(7);
        var range=AssessSegments(ff,fv,bf,bv,assumedLimitCm);
        // For every M at which the ranges remain disjoint, every solution has
        // max(|front shift|,|back shift|)>M; sum of absolute shifts is also >M.
        double lo=0,hi=20;var zero=AssessSegments(ff,fv,bf,bv,0);
        if(!zero.front.Disjoint(zero.back))throw new ArgumentException("Original represented side intervals overlap.");
        for(int i=0;i<48;i++) { double mid=(lo+hi)/2;var test=AssessSegments(ff,fv,bf,bv,mid);if(test.front.Disjoint(test.back))lo=mid;else hi=mid; }
        return new(id,assumedLimitCm,range.front,range.back,lo);
    }
    internal static void AttemptWithinLimit(string id,double assumedLimitCm)
    {
        var result=Create(id,assumedLimitCm);
        if(result.SideEqualityImpossibleWithinLimit)throw new InvalidOperationException("Side equality impossible in this endpoint-only bounded family.");
        throw new InvalidOperationException("No certified optimizer for the remaining endpoint-only family.");
    }
}
