using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental caller-budgeted comparison of a single candidate against the
/// indexed miter. Passing is evidence for the stated checks only. It does not
/// discover or certify garment features, source-to-grid precision, independent
/// marked notches, manufacturing tolerances, or production suitability.
/// Caller must keep all inputs stable while copied.
/// </summary>
internal static class OffsetBudgetEvidenceExperiment
{
    internal static bool TryVerify(IReadOnlyList<GridPoint> source,IReadOnlyList<IReadOnlyList<GridPoint>> candidates,
        long delta,long vertexBudget,long edgeBand,long chordBudget,long heightBudget,
        out string reason,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(candidates);
        token.ThrowIfCancellationRequested();reason="Invalid contour count or caller budgets.";
        int n=source.Count;
        if(n<3 || n>707 || candidates.Count!=1 || candidates[0] is null || candidates[0].Count!=n ||
           delta<=0 || vertexBudget<0 || edgeBand<0 || edgeBand>=delta || chordBudget<0 || heightBudget<0)return false;
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
        if(!OffsetLinearTopologyExperiment.TryVerify(s,mapped,out reason,token) ||
           !OffsetIndexedEdgeEvidence.TryVerify(s,mapped,delta,edgeBand,out reason,token) ||
           !OffsetAllTurnsEvidence.TryVerify(s,mapped,chordBudget,heightBudget,out reason,token))return false;
        reason="Caller-budgeted indexed vertex, edge, local turn and linear topology checks passed; feature discovery, dimensional preservation and production suitability are not certified.";
        return true;
    }
}
