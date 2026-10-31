using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental exact indexed-edge evidence, not a production acceptance test.
/// For each source edge, the corresponding candidate edge endpoints must lie on
/// the outward side in a caller-specified distance band around its supporting
/// line, and its direction must make forward progress. Convexity of that band
/// makes the same distance bound apply to every point on the candidate edge.
/// This does not prove endpoint coverage, miter correctness, contour simplicity,
/// feature preservation, or correspondence to a separately generated boundary.
/// </summary>
internal static class OffsetIndexedEdgeEvidence
{
    private const int MaximumVertices=4096;

    /// <remarks>Inputs are cyclic, equally indexed integer-grid vertices. Callers
    /// must not mutate either collection while the snapshots are copied. Delta and
    /// error are in grid units; no threshold is selected by this helper.</remarks>
    internal static bool TryVerify(IReadOnlyList<GridPoint> source,IReadOnlyList<GridPoint> candidate,
        long delta,long error,out string reason,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(candidate);
        reason="";token.ThrowIfCancellationRequested();
        var n=source.Count;
        if(n<3 || n>MaximumVertices || candidate.Count!=n || delta<=0 || error<0 || error>=delta)
        { reason="Invalid vertex count, offset, or error band.";return false; }
        var s=new GridPoint[n];var c=new GridPoint[n];
        BigInteger area=0;
        for(var i=0;i<n;i++) { token.ThrowIfCancellationRequested();s[i]=source[i];c[i]=candidate[i]; }
        for(var i=0;i<n;i++)
        {
            token.ThrowIfCancellationRequested();var next=(i+1)%n;
            area+=(BigInteger)s[i].X*s[next].Y-(BigInteger)s[i].Y*s[next].X;
        }
        if(area.IsZero) { reason="Source has zero signed area.";return false; }
        var direction=area.Sign;
        var lower=(BigInteger)delta-error;var upper=(BigInteger)delta+error;
        for(var i=0;i<n;i++)
        {
            token.ThrowIfCancellationRequested();var j=(i+1)%n;
            BigInteger dx=(BigInteger)s[j].X-s[i].X,dy=(BigInteger)s[j].Y-s[i].Y;
            BigInteger ex=(BigInteger)c[j].X-c[i].X,ey=(BigInteger)c[j].Y-c[i].Y;
            var lengthSquared=dx*dx+dy*dy;
            if(lengthSquared.IsZero || ex*ex+ey*ey==0 || dx*ex+dy*ey<=0)
            { reason=$"Edge {i} is zero or does not progress in source direction.";return false; }
            bool InBand(GridPoint point)
            {
                BigInteger px=(BigInteger)point.X-s[i].X,py=(BigInteger)point.Y-s[i].Y;
                var outward=-direction*(dx*py-dy*px);
                return outward.Sign>0 && outward*outward>=lower*lower*lengthSquared && outward*outward<=upper*upper*lengthSquared;
            }
            if(!InBand(c[i]) || !InBand(c[j]))
            { reason=$"Candidate edge {i} leaves the outward distance band.";return false; }
        }
        token.ThrowIfCancellationRequested();
        reason="Every indexed edge passes the supplied line-distance band; topology, coverage and feature preservation remain unverified.";
        return true;
    }
}
