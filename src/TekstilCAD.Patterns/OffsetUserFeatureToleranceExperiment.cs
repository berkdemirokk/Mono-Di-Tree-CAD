using System.Numerics;
using TekstilCAD.Core;

namespace TekstilCAD.Patterns;

/// <summary>
/// Exact selected-feature dimensional comparison. The caller identifies a
/// corresponding source/output mouth and witness by indices, and supplies
/// absolute maximum allowed width and depth changes in millimeters. Passing
/// establishes only those two measurements; it does not discover other notches,
/// prove feature identity, validate contours, prove any offset/sweep, or
/// authorize production. Inputs must not be mutated while being read.
/// </summary>
internal static class OffsetUserFeatureToleranceExperiment
{
    private readonly record struct P(BigInteger X,BigInteger Y);
    internal static bool TryVerify(IReadOnlyList<Point2> source,IReadOnlyList<Point2> candidate,
        int start,int witness,int end,double widthTolerance,double depthTolerance,
        out string reason,CancellationToken token=default)
    {
        reason="Invalid source, selected indices, or tolerance.";
        token.ThrowIfCancellationRequested();
        int n=source.Count;
        if(n<3 || n>1024 || candidate.Count!=n || start<0 || start>=n || witness<0 || witness>=n ||
           end<0 || end>=n || start==witness || start==end || witness==end ||
           !double.IsFinite(widthTolerance) || widthTolerance<0 || !double.IsFinite(depthTolerance) || depthTolerance<0)
            return false;
        Point2[] points=[source[start],source[witness],source[end],candidate[start],candidate[witness],candidate[end]];
        if(points.Any(p=>!p.IsFinite))return false;
        int shift=Math.Min(Exponent(widthTolerance),Exponent(depthTolerance));
        foreach(var p in points) { token.ThrowIfCancellationRequested(); shift=Math.Min(shift,Math.Min(Exponent(p.X),Exponent(p.Y))); }
        var exact=points.Select(p=>new P(Exact(p.X)>>shift,Exact(p.Y)>>shift)).ToArray();
        var width=Exact(widthTolerance)>>shift;var depth=Exact(depthTolerance)>>shift;
        if(!Measure(exact[0],exact[1],exact[2],out var sourceCross,out var sourceChord) ||
           !Measure(exact[3],exact[4],exact[5],out var candidateCross,out var candidateChord) ||
           sourceCross.Sign!=candidateCross.Sign)
        { reason="Feature is flat, reversed, or witness projection is outside its mouth."; return false; }
        if(!Within(sourceChord,BigInteger.One,candidateChord,BigInteger.One,width))
        { reason="Mouth width changed more than the user-specified tolerance."; return false; }
        if(!Within(sourceCross*sourceCross,sourceChord,candidateCross*candidateCross,candidateChord,depth))
        { reason="Witness depth changed more than the user-specified tolerance."; return false; }
        reason="Selected width and depth satisfy the supplied tolerances only; no general feature or offset proof.";
        return true;
    }
    private static bool Measure(P start,P witness,P end,out BigInteger cross,out BigInteger chord2)
    {
        BigInteger dx=end.X-start.X,dy=end.Y-start.Y,px=witness.X-start.X,py=witness.Y-start.Y;
        cross=dx*py-dy*px;chord2=dx*dx+dy*dy;var projection=px*dx+py*dy;
        return !chord2.IsZero && !cross.IsZero && projection>=0 && projection<=chord2;
    }
    // Exact test |sqrt(A/B)-sqrt(C/D)| <= tolerance, for nonnegative
    // quantities and positive denominators. Squaring is done only after the
    // left side is known positive; no approximate square roots are used.
    private static bool Within(BigInteger a,BigInteger b,BigInteger c,BigInteger d,BigInteger tolerance)
    {
        BigInteger left=a*d+c*b-tolerance*tolerance*b*d;
        return left.Sign<=0 || left*left<=4*a*c*b*d;
    }
    private static int Exponent(double value)
    {
        long bits=BitConverter.DoubleToInt64Bits(value);int e=(int)((bits>>52)&0x7ff);
        if((bits & 0x7fff_ffff_ffff_ffffL)==0)return 1074;
        return e==0?0:e-1;
    }
    private static BigInteger Exact(double value)
    {
        long bits=BitConverter.DoubleToInt64Bits(value);int e=(int)((bits>>52)&0x7ff);
        long f=bits & 0x000f_ffff_ffff_ffffL;BigInteger m=e==0?f:f | (1L<<52);
        if(bits<0)m=-m;return m<<(e==0?0:e-1);
    }
}
