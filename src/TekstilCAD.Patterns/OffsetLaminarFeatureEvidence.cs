using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental proof for explicitly supplied feature spans. Each witness is
/// unique and lies strictly inside its directed cyclic source interval. Edge
/// spans may be disjoint or strictly nested; crossing/equal spans are rejected.
/// The indexed candidate and selected width/depth tolerances are verified by
/// the existing boundary/measurement experiments. This does not discover all
/// features, prove supplied garment semantics, or authorize production.
/// Inputs must be stable while copied.
/// </summary>
internal static class OffsetLaminarFeatureEvidence
{
    private const long MaxSpanPairChecks=2_000_000;
    internal static bool TryVerify(IReadOnlyList<GridPoint> source,IReadOnlyList<GridPoint> candidate,long delta,
        IReadOnlyList<(int Start,int Witness,int End,double WidthMm,double DepthMm)> features,
        out string reason,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(candidate);ArgumentNullException.ThrowIfNull(features);
        token.ThrowIfCancellationRequested();int n=source.Count,count=features.Count;
        reason="Invalid contour or feature budget.";
        if(n<3 || n>707 || candidate.Count!=n || count<1 || count>64 || (long)count*(count-1)/2*n>MaxSpanPairChecks)return false;
        var s=new GridPoint[n];var c=new GridPoint[n];
        for(int i=0;i<n;i++){token.ThrowIfCancellationRequested();s[i]=source[i];c[i]=candidate[i];}
        var selected=new (int Start,int Witness,int End,double WidthMm,double DepthMm)[count];
        var edges=new bool[count][];var unique=new HashSet<int>();
        for(int i=0;i<count;i++)
        {
            token.ThrowIfCancellationRequested();var f=features[i];
            if(f.Start<0 || f.Start>=n || f.End<0 || f.End>=n || f.Witness<0 || f.Witness>=n ||
                !double.IsFinite(f.WidthMm) || f.WidthMm<0 || !double.IsFinite(f.DepthMm) || f.DepthMm<0 || !unique.Add(f.Witness))
            {reason=$"Feature {i} has invalid input or duplicate witness.";return false;}
            int span=(f.End-f.Start+n)%n, witness=(f.Witness-f.Start+n)%n;
            if(span<2 || witness==0 || witness>=span)
            {reason=$"Feature {i} witness is not strictly inside its directed span.";return false;}
            edges[i]=new bool[n];
            for(int j=0;j<span;j++){token.ThrowIfCancellationRequested();edges[i][(f.Start+j)%n]=true;}
            selected[i]=f;
        }
        for(int i=0;i<count;i++)for(int j=i+1;j<count;j++)
        {
            bool overlap=false,onlyI=false,onlyJ=false;
            for(int k=0;k<n;k++)
            {
                token.ThrowIfCancellationRequested();bool x=edges[i][k],y=edges[j][k];
                overlap|=x&&y;onlyI|=x&&!y;onlyJ|=y&&!x;
            }
            if(overlap && (onlyI==onlyJ))
            {reason=$"Feature spans {i} and {j} cross or coincide.";return false;}
        }
        if(!OffsetSelectedFeaturesEvidenceExperiment.TryVerify(s,c,delta,selected,out reason,token))return false;
        reason="Selected laminar indexed spans and their dimensions passed only; complete feature discovery, garment semantics and production suitability are not certified.";
        return true;
    }
}
