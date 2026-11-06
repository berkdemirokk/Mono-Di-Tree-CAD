using System.Numerics;

namespace FittedResearch;

// Exact rational bounds on projected lengths for binary64 coefficients.
// This models the source coefficient division only, not cloth mechanics.
internal static class ProjectedLengthBounds
{
    internal sealed record Interval(BigInteger Lower,BigInteger Upper,BigInteger Denominator)
    {
        public static Interval Zero=>new(0,0,1);
        private void Validate() { if(Denominator<=0 || Lower<0 || Upper<Lower)throw new ArgumentException("Invalid projected interval"); }
        public static Interval Add(Interval a,Interval b)
        {
            a.Validate();b.Validate();
            var g=BigInteger.GreatestCommonDivisor(a.Denominator,b.Denominator);
            var am=b.Denominator/g;var bm=a.Denominator/g;
            return new(a.Lower*am+b.Lower*bm,a.Upper*am+b.Upper*bm,a.Denominator*am);
        }
        public bool Disjoint(Interval other) { Validate();other.Validate();return Lower*other.Denominator>other.Upper*Denominator || other.Lower*Denominator>Upper*other.Denominator; }
        // Approximate display only; the disjoint verdict remains integer exact.
        public double ApproximateGap(Interval other) {
            Validate();other.Validate();var gap=BigInteger.Max(BigInteger.Zero,BigInteger.Max(Lower*other.Denominator-other.Upper*Denominator,other.Lower*Denominator-Upper*other.Denominator));
            return Approx(gap,Denominator*other.Denominator);
        }
        public bool Contains(double value)
        {
            Validate();if(!double.IsFinite(value)||value<0)return false;var (n,d)=Fraction(value);
            return n*Denominator>=Lower*d && n*Denominator<=Upper*d;
        }
        public double ApproximateLower=>Approx(Lower,Denominator);
        public double ApproximateUpper=>Approx(Upper,Denominator);
        static double Approx(BigInteger n,BigInteger d)
        {
            if(n.IsZero)return 0;int a=(int)Math.Max(0,n.GetBitLength()-52),b=(int)Math.Max(0,d.GetBitLength()-52);
            return Math.ScaleB((double)(n>>a)/(double)(d>>b),a-b);
        }
    }
    static (BigInteger numerator,BigInteger denominator) Fraction(double value)
    {
        if(!double.IsFinite(value)||value<0)throw new ArgumentException("Invalid binary64 coefficient");
        long bits=BitConverter.DoubleToInt64Bits(value);int exp=(int)((bits>>52)&0x7ff);ulong frac=(ulong)bits&0x000f_ffff_ffff_ffffUL;
        BigInteger n=new BigInteger(exp==0?frac:frac|(1UL<<52));int power=exp==0?-1074:exp-1075;
        BigInteger d=BigInteger.One;if(power>=0)n<<=power;else d<<=-power;
        var gcd=BigInteger.GreatestCommonDivisor(n,d);return(n/gcd,d/gcd);
    }
    internal static Interval Project(BezierLengthBounds.Bounds length,double coefficient)
    {
        if(length is null || length.Lower<0 || length.Upper<length.Lower || length.Scale is <0 or >36)throw new ArgumentException("Invalid length interval");
        if(!double.IsFinite(coefficient)||coefficient<=0)throw new ArgumentException("Invalid ruffle coefficient");
        BigInteger n=1,d=1;
        // Match source Interface.projecting_lengths near-one handling.
        if(Math.Abs(coefficient-1)>=.001)(n,d)=Fraction(coefficient);
        return new(length.Lower*d,length.Upper*d,(BigInteger.One<<(1074+length.Scale))*n);
    }
}
