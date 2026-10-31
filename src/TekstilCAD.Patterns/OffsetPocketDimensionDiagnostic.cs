using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>A strictly positive ratio interval, applied to candidate/source dimensions.</summary>
public readonly record struct PocketRatioRange(long LowerNumerator,long LowerDenominator,long UpperNumerator,long UpperDenominator);

/// <summary>Experimental exact evidence for one explicitly selected, four-vertex
/// pocket. It checks relative mouth and floor lengths and both perpendicular
/// depths with caller-supplied bounds. It does not discover pockets, prove a
/// complete contour, determine correct manufacturing tolerances or authorize
/// production. Callers must independently prove source/candidate correspondence.</summary>
public static class OffsetPocketDimensionDiagnostic
{
    private const int MaximumVertices=1024;
    public sealed record Measurement(BigInteger MouthSquared,BigInteger FloorSquared,
        BigInteger FirstDepthNumeratorSquared,BigInteger SecondDepthNumeratorSquared);
    public sealed record Evidence(int Start,Measurement Source,Measurement Candidate);

    /// <remarks>Four consecutive vertices are mouth start, floor start, floor end,
    /// mouth end, in contour order. Inputs use the same integer grid and must not
    /// mutate while copied. Ratios are lengths, not squared lengths; squaring and
    /// cross multiplication use arbitrary-precision integers with no square roots.</remarks>
    public static bool TryVerify(IReadOnlyList<GridPoint> source,IReadOnlyList<GridPoint> candidate,int start,
        PocketRatioRange mouth,PocketRatioRange floor,PocketRatioRange firstDepth,PocketRatioRange secondDepth,
        out Evidence? evidence,out string reason,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(candidate);cancellationToken.ThrowIfCancellationRequested();
        evidence=null;reason="";var count=source.Count;
        if(count<4 || count>MaximumVertices || candidate.Count!=count || start<0 || start>=count ||
           !Valid(mouth) || !Valid(floor) || !Valid(firstDepth) || !Valid(secondDepth))
        { reason="Invalid contour, pocket index or positive ratio interval.";return false; }
        var src=new GridPoint[4];var dst=new GridPoint[4];
        for(int i=0;i<4;i++) { cancellationToken.ThrowIfCancellationRequested();var index=(start+i)%count;src[i]=source[index];dst[i]=candidate[index]; }
        if(!TryMeasure(src,out var a,out var aSide) || !TryMeasure(dst,out var b,out var bSide))
        { reason="Pocket is degenerate, crosses its mouth, or floor projections are reversed/outside.";return false; }
        if(aSide!=bSide) { reason="Pocket changes side relative to its mouth.";return false; }
        if(!Within(b.MouthSquared,a.MouthSquared,mouth) || !Within(b.FloorSquared,a.FloorSquared,floor) ||
           !WithinDepth(b.FirstDepthNumeratorSquared,b.MouthSquared,a.FirstDepthNumeratorSquared,a.MouthSquared,firstDepth) ||
           !WithinDepth(b.SecondDepthNumeratorSquared,b.MouthSquared,a.SecondDepthNumeratorSquared,a.MouthSquared,secondDepth))
        { reason="A selected pocket dimension exceeds the explicit ratio interval.";return false; }
        cancellationToken.ThrowIfCancellationRequested();evidence=new(start,a,b);
        reason="Only the selected pocket dimensions meet the supplied bounds; correspondence, unselected features, global topology and production suitability remain unverified.";
        return true;
    }
    private static bool TryMeasure(GridPoint[] p,out Measurement measurement,out int side)
    {
        measurement=new(BigInteger.Zero,BigInteger.Zero,BigInteger.Zero,BigInteger.Zero);side=0;
        BigInteger mx=(BigInteger)p[3].X-p[0].X,my=(BigInteger)p[3].Y-p[0].Y;
        BigInteger bx=(BigInteger)p[1].X-p[0].X,by=(BigInteger)p[1].Y-p[0].Y;
        BigInteger cx=(BigInteger)p[2].X-p[0].X,cy=(BigInteger)p[2].Y-p[0].Y;
        BigInteger fx=(BigInteger)p[2].X-p[1].X,fy=(BigInteger)p[2].Y-p[1].Y;
        var mouth=mx*mx+my*my;var floor=fx*fx+fy*fy;var bCross=mx*by-my*bx;var cCross=mx*cy-my*cx;
        var bProjection=mx*bx+my*by;var cProjection=mx*cx+my*cy;
        if(mouth.IsZero || floor.IsZero || bCross.IsZero || cCross.IsZero || bCross.Sign!=cCross.Sign ||
           bProjection<0 || cProjection>mouth || bProjection>=cProjection)return false;
        side=bCross.Sign;measurement=new(mouth,floor,bCross*bCross,cCross*cCross);return true;
    }
    private static bool Valid(PocketRatioRange r)=>r.LowerNumerator>0 && r.LowerDenominator>0 && r.UpperNumerator>0 && r.UpperDenominator>0 &&
        (BigInteger)r.LowerNumerator*r.UpperDenominator <= (BigInteger)r.UpperNumerator*r.LowerDenominator;
    private static bool Within(BigInteger candidateSquared,BigInteger sourceSquared,PocketRatioRange r)
    {
        BigInteger ln=r.LowerNumerator,ld=r.LowerDenominator,un=r.UpperNumerator,ud=r.UpperDenominator;
        return candidateSquared*ld*ld>=sourceSquared*ln*ln && candidateSquared*ud*ud<=sourceSquared*un*un;
    }
    private static bool WithinDepth(BigInteger candidateNumeratorSquared,BigInteger candidateMouthSquared,
        BigInteger sourceNumeratorSquared,BigInteger sourceMouthSquared,PocketRatioRange r)
    {
        // depth² = cross² / mouthLength². Both are positive by TryMeasure.
        return Within(candidateNumeratorSquared*sourceMouthSquared,sourceNumeratorSquared*candidateMouthSquared,r);
    }
}
