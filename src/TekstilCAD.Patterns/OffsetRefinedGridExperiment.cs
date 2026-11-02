using System.Numerics;
using Clipper2Lib;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Isolated candidate experiment. Translates a production-grid path, applies a
/// 10x integer refinement, and rounds back exactly. Acceptance additionally
/// requires the pre-existing exact indexed-boundary and topology experiments.
/// This does not run PatternTools.Offset or certify feature preservation,
/// physical tolerances or production suitability. No production caller uses it.
/// Inputs must remain stable while copied. Clipper itself is synchronous.
/// </summary>
internal static class OffsetRefinedGridExperiment
{
    private const long Factor=10;
    // Deliberately conservative arithmetic budget, not a proof of Clipper accuracy.
    private static readonly BigInteger Limit=BigInteger.One<<50;
    internal static bool TryCreate(IReadOnlyList<GridPoint> source,long delta,
        out IReadOnlyList<GridPoint> candidate,out string reason,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(source);token.ThrowIfCancellationRequested();
        candidate=Array.Empty<GridPoint>();reason="Invalid source, allowance, or vertex budget.";
        int n=source.Count;if(n<3 || n>707 || delta<=0)return false;
        var s=new GridPoint[n];for(int i=0;i<n;i++){token.ThrowIfCancellationRequested();s[i]=source[i];}
        BigInteger refinedDelta=(BigInteger)delta*Factor;
        if(refinedDelta*4>=Limit){reason="Refined allowance exceeds arithmetic budget.";return false;}
        var path=new Path64();var origin=s[0];var seen=new HashSet<GridPoint>();
        foreach(var p in s)
        {
            token.ThrowIfCancellationRequested();if(!seen.Add(p)){reason="Repeated source vertex.";return false;}
            BigInteger x=((BigInteger)p.X-origin.X)*Factor,y=((BigInteger)p.Y-origin.Y)*Factor;
            if(BigInteger.Abs(x)+4*refinedDelta>=Limit || BigInteger.Abs(y)+4*refinedDelta>=Limit)
            {reason="Refined coordinate exceeds arithmetic budget.";return false;}
            path.Add(new Point64((long)x,(long)y));
        }
        token.ThrowIfCancellationRequested();
        var paths=Clipper.InflatePaths(new Paths64{path},(double)(long)refinedDelta,JoinType.Miter,EndType.Polygon,2);
        token.ThrowIfCancellationRequested();
        if(paths.Count!=1 || paths[0].Count<3 || paths[0].Count>16_384)
        {reason="Refined result changes contour count or exceeds budget.";return false;}
        var down=new GridPoint[paths[0].Count];seen.Clear();
        for(int i=0;i<down.Length;i++)
        {
            token.ThrowIfCancellationRequested();var p=paths[0][i];
            BigInteger x=RoundAway(p.X,Factor)+origin.X,y=RoundAway(p.Y,Factor)+origin.Y;
            if(x<long.MinValue || x>long.MaxValue || y<long.MinValue || y>long.MaxValue)
            {reason="Restoring the coordinate origin overflows.";return false;}
            down[i]=new((long)x,(long)y);
            if(!seen.Add(down[i])){reason="Returning to the production grid merges vertices.";return false;}
        }
        if(!OffsetForwardBoundaryProof.TryVerify(s,delta,down,out var detail,token))
        {reason="Exact indexed boundary: "+detail;return false;}
        if(!OffsetCandidateProof.TryCreate(s,delta,out var indexed,out detail,token) ||
           !OffsetIdealBoundaryEvidenceExperiment.TryVerify(s,indexed,delta,out detail,token))
        {reason="Indexed topology: "+detail;return false;}
        candidate=Array.AsReadOnly(down);
        reason="Experimental refined candidate matches the indexed boundary and topology checks only; feature retention and production suitability are not certified.";
        return true;
    }
    private static BigInteger RoundAway(long value,long divisor)
    {
        BigInteger v=value,sign=v.Sign;v=BigInteger.Abs(v);
        return sign*((v+divisor/2)/divisor);
    }
}
