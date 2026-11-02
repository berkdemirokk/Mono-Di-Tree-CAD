using System.Numerics;
using TekstilCAD.Core;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental caller-budgeted comparison of a single candidate against the
/// indexed miter. Passing is evidence for the stated checks only. It does not
/// discover or certify garment features, source-to-grid precision, independent
/// marked notches, manufacturing tolerances, or production suitability. The
/// caller-selected minimum is checked in integer grid units only for pairs of
/// non-adjacent candidate edges; it neither measures a V-notch's adjacent
/// arms/depth nor proves that binary64 millimeter distances meet the same bound.
/// Caller must keep all inputs stable while copied.
/// </summary>
internal static class OffsetBudgetEvidenceExperiment
{
    internal static bool TryVerify(IReadOnlyList<GridPoint> source,IReadOnlyList<IReadOnlyList<GridPoint>> candidates,
        long delta,long vertexBudget,long edgeBand,long chordBudget,long heightBudget,long minimumSeparation,
        out string reason,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(candidates);
        token.ThrowIfCancellationRequested();reason="Invalid contour count or caller budgets.";
        int n=source.Count;
        if(n<3 || n>707 || candidates.Count!=1 || candidates[0] is null || candidates[0].Count!=n ||
           delta<=0 || vertexBudget<0 || edgeBand<0 || edgeBand>=delta || chordBudget<0 || heightBudget<0 || minimumSeparation<=0)return false;
        var s=new GridPoint[n];var c=new GridPoint[n];
        for(int i=0;i<n;i++){token.ThrowIfCancellationRequested();s[i]=source[i];c[i]=candidates[0][i];}
        if(!OffsetCandidateProof.TryCreate(s,delta,out var ideal,out reason,token) ||
           !OffsetIdealBoundaryEvidenceExperiment.TryVerify(s,ideal,delta,out reason,token))return false;
        BigInteger limit=(BigInteger)vertexBudget*vertexBudget;int start=-1;
        for(int shift=0;shift<n;shift++)
        {
            token.ThrowIfCancellationRequested();bool match=true;
            for(int i=0;i<n;i++)
            {
                BigInteger dx=(BigInteger)c[(shift+i)%n].X-ideal[i].X,dy=(BigInteger)c[(shift+i)%n].Y-ideal[i].Y;
                if(dx*dx+dy*dy>limit){match=false;break;}
            }
            if(match){if(start>=0){reason="Candidate mapping is ambiguous within the budget.";return false;}start=shift;}
        }
        if(start<0){reason="No cyclic indexed mapping meets the vertex budget.";return false;}
        var mapped=new GridPoint[n];for(int i=0;i<n;i++){token.ThrowIfCancellationRequested();mapped[i]=c[(start+i)%n];}
        const long coordinateLimit=100_000_000L*1_000_000;
        var sm=new Point2[n];var cm=new Point2[n];
        for(int i=0;i<n;i++)
        {
            token.ThrowIfCancellationRequested();var p=mapped[i];
            if(p.X < -coordinateLimit || p.X > coordinateLimit || p.Y < -coordinateLimit || p.Y > coordinateLimit)
            {reason="Mapped candidate exceeds the supported coordinate range.";return false;}
            sm[i]=new Point2(s[i].X/1_000_000d,s[i].Y/1_000_000d);
            cm[i]=new Point2(p.X/1_000_000d,p.Y/1_000_000d);
        }
        if(!OffsetLinearTopologyExperiment.TryVerify(s,mapped,out reason,token) ||
           !OffsetIndexedEdgeEvidence.TryVerify(s,mapped,delta,edgeBand,out reason,token) ||
           !OffsetAllTurnsEvidence.TryVerify(s,mapped,chordBudget,heightBudget,out reason,token) ||
           !OffsetMillimeterProof.TryValidate(s,mapped,sm,cm,out reason,token))return false;
        BigInteger separationSquared=(BigInteger)minimumSeparation*minimumSeparation;
        for(int i=0;i<n;i++)for(int j=i+1;j<n;j++)
        {
            token.ThrowIfCancellationRequested();if(j==i+1 || (i==0 && j==n-1))continue;
            var a=mapped[i];var b=mapped[(i+1)%n];var x=mapped[j];var y=mapped[(j+1)%n];
            if(!AtLeast(a,x,y,separationSquared) || !AtLeast(b,x,y,separationSquared) ||
               !AtLeast(x,a,b,separationSquared) || !AtLeast(y,a,b,separationSquared))
            {reason=$"Candidate edges {i} and {j} violate the caller's minimum separation.";return false;}
        }
        reason="Caller-budgeted indexed vertex, edge, turn, topology and binary64 topology checks passed; non-adjacent candidate edges meet the caller-selected minimum in grid units only. Adjacent V-notch arms, physical millimeter separation, feature discovery, dimensional preservation and production suitability are not certified.";
        return true;
    }
    private static bool AtLeast(GridPoint p,GridPoint a,GridPoint b,BigInteger minimumSquared)
    {
        BigInteger dx=(BigInteger)b.X-a.X,dy=(BigInteger)b.Y-a.Y;
        BigInteger px=(BigInteger)p.X-a.X,py=(BigInteger)p.Y-a.Y;
        var length=dx*dx+dy*dy;var dot=px*dx+py*dy;
        if(length.IsZero || dot<=0)return px*px+py*py>=minimumSquared;
        if(dot>=length){var x=px-dx;var y=py-dy;return x*x+y*y>=minimumSquared;}
        var cross=dx*py-dy*px;return cross*cross>=minimumSquared*length;
    }
}
