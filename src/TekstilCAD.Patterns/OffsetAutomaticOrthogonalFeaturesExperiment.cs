using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental, deliberately incomplete discovery of symmetric orthogonal
/// recesses. Every concave corner must have exactly one directed matching
/// concave corner; ambiguous, unmatched and non-orthogonal inputs are rejected.
/// Indexed candidate matching is delegated to the existing ideal-miter proof.
/// Passing does not certify discovery of all garment features or production use.
/// Callers must keep inputs stable while they are copied.
/// </summary>
internal static class OffsetAutomaticOrthogonalFeaturesExperiment
{
    internal static bool TryVerify(IReadOnlyList<GridPoint> source,IReadOnlyList<GridPoint> candidate,
        long delta,double widthToleranceMm,double depthToleranceMm,out int featureCount,
        out string reason,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(candidate);
        token.ThrowIfCancellationRequested();featureCount=0;reason="Invalid contour or tolerance.";
        int n=source.Count;
        if(n<4 || n>707 || candidate.Count!=n || !double.IsFinite(widthToleranceMm) || widthToleranceMm<0 ||
           !double.IsFinite(depthToleranceMm) || depthToleranceMm<0)return false;
        var s=new GridPoint[n];var c=new GridPoint[n];
        for(int i=0;i<n;i++){token.ThrowIfCancellationRequested();s[i]=source[i];c[i]=candidate[i];}
        BigInteger area=0;var dx=new BigInteger[n];var dy=new BigInteger[n];
        for(int i=0;i<n;i++)
        {
            token.ThrowIfCancellationRequested();var p=s[i];var q=s[(i+1)%n];
            dx[i]=(BigInteger)q.X-p.X;dy[i]=(BigInteger)q.Y-p.Y;
            if(dx[i].IsZero==dy[i].IsZero){reason="Non-orthogonal or zero edge; discovery is unsupported.";return false;}
            area+=(BigInteger)p.X*q.Y-(BigInteger)p.Y*q.X;
        }
        if(area.IsZero){reason="Degenerate area.";return false;}
        var concave=new List<int>();
        for(int i=0;i<n;i++)
        {
            int prev=(i+n-1)%n;BigInteger cross=dx[prev]*dy[i]-dy[prev]*dx[i];
            if(cross.IsZero){reason="Collinear corner; discovery is ambiguous.";return false;}
            if(cross.Sign!=area.Sign)concave.Add(i);
        }
        if(concave.Count==0 || concave.Count>128){reason="No supported recesses or feature budget exceeded.";return false;}
        var matches=new List<(int A,int B)>();var degree=new int[n];
        foreach(int a in concave)foreach(int b in concave)
        {
            token.ThrowIfCancellationRequested();if(a==b)continue;
            int before=(a+n-1)%n,after=(b+1)%n;
            if(before==after || dx[before]!=-dx[b] || dy[before]!=-dy[b])continue;
            BigInteger mx=(BigInteger)s[after].X-s[before].X,my=(BigInteger)s[after].Y-s[before].Y;
            if(mx.IsZero && my.IsZero || mx*dx[before]+my*dy[before]!=0)continue;
            matches.Add((a,b));degree[a]++;degree[b]++;
        }
        if(concave.Any(i=>degree[i]!=1) || matches.Count>64)
        {reason="A concave corner is unmatched or has ambiguous matching recesses.";return false;}
        var features=matches.Select(m=>((m.A+n-1)%n,m.A,(m.B+1)%n,widthToleranceMm,depthToleranceMm)).ToArray();
        if(!OffsetLaminarFeatureEvidence.TryVerify(s,c,delta,features,out reason,token))return false;
        featureCount=features.Length;
        reason="Unique symmetric orthogonal recess matches passed indexed checks; discovery completeness and production suitability are not certified.";
        return true;
    }
}
