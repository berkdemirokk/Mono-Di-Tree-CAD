using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental closed-world proof for exclusively discoverable rectangular
/// pockets. Matching uses the same four source indices and compares actual
/// mouth widths and depths against explicit integer-grid error budgets. It is
/// not garment-semantic discovery, a general offset proof, or production approval.
/// Inputs must remain unchanged during copying.
/// </summary>
internal static class OffsetRectangularNotchPipeline
{
    internal static bool TryVerify(IReadOnlyList<GridPoint> source, IReadOnlyList<GridPoint> candidate,
        long delta, long widthTolerance, long depthTolerance, out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(candidate);
        token.ThrowIfCancellationRequested();
        int n = source.Count; reason = "Invalid contour or tolerance.";
        if (n < 4 || n > 707 || candidate.Count != n || delta <= 1 || widthTolerance < 0 || depthTolerance < 0) return false;
        var s = new GridPoint[n]; var c = new GridPoint[n];
        for (int i=0;i<n;i++) { token.ThrowIfCancellationRequested(); s[i]=source[i]; c[i]=candidate[i]; }
        if (!OffsetRectangularNotchDiscovery.TryDiscover(s, out var sourceFeatures, out reason, token)) return false;
        if (!OffsetRectangularNotchDiscovery.TryDiscover(c, out var candidateFeatures, out reason, token)) return false;
        if (sourceFeatures.Length == 0 || sourceFeatures.Length != candidateFeatures.Length)
        { reason = "No rectangular features or differing feature counts."; return false; }
        var byIndex = new Dictionary<(int,int,int,int), RectangularNotch>();
        foreach (var f in candidateFeatures)
        {
            token.ThrowIfCancellationRequested();
            if (!byIndex.TryAdd((f.MouthStart,f.BottomStart,f.BottomEnd,f.MouthEnd), f))
            { reason = "Duplicate candidate feature."; return false; }
        }
        var local = new List<(int Start,int Witness,int End,double WidthMm,double DepthMm)>();
        // Round down when converting a positive grid tolerance to binary64 so
        // an imprecise conversion cannot enlarge a caller's requested budget.
        static double Mm(long grid) => grid == 0 ? 0 : Math.BitDecrement(grid / 1_000_000.0);
        foreach (var f in sourceFeatures)
        {
            token.ThrowIfCancellationRequested();
            if (!byIndex.Remove((f.MouthStart,f.BottomStart,f.BottomEnd,f.MouthEnd), out var g))
            { reason = "Source feature lacks a unique indexed candidate."; return false; }
            if (!Within(f.WidthSquared,g.WidthSquared,widthTolerance) || !Within(f.DepthSquared,g.DepthSquared,depthTolerance))
            { reason = "Feature width or depth exceeds its grid tolerance."; return false; }
            local.Add(((f.BottomStart+n-1)%n,f.BottomStart,(f.BottomStart+1)%n,Mm(widthTolerance),Mm(depthTolerance)));
            local.Add(((f.BottomEnd+n-1)%n,f.BottomEnd,(f.BottomEnd+1)%n,Mm(widthTolerance),Mm(depthTolerance)));
        }
        if (byIndex.Count != 0) { reason = "Unmatched candidate features."; return false; }
        if (!OffsetConcaveMappedEvidence.TryVerify(s,c,delta,local,out reason,token)) return false;
        reason = "Rectangular features matched and measured under explicit tolerances only; garment semantics and production suitability are not certified.";
        return true;
    }

    private static bool Within(BigInteger a, BigInteger b, long tolerance)
    {
        BigInteger t=tolerance;
        var left=a+b-t*t;
        return left.Sign<=0 || left*left<=4*a*b;
    }
}
