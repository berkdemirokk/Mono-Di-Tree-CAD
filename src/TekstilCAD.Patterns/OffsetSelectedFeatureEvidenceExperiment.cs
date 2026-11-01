using TekstilCAD.Core;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>Combines experimental ideal-boundary checks with width and witness
/// depth comparisons for one explicitly indexed feature. The caller defines
/// correspondence and tolerances in millimeters. This does not discover other
/// features, prove semantic identity or authorize production.</summary>
/// <remarks>The caller must keep both collections stable during snapshot copying.
/// The combined boundary proof is subject to its 707-vertex effective limit.</remarks>
internal static class OffsetSelectedFeatureEvidenceExperiment
{
    internal static bool TryVerify(IReadOnlyList<GridPoint> source, IReadOnlyList<GridPoint> candidate,
        long delta, int start, int witness, int end, double widthToleranceMm, double depthToleranceMm,
        out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(candidate);
        token.ThrowIfCancellationRequested();
        int n = source.Count;
        reason = "Invalid contour size, selected indices, or tolerances.";
        if (n < 3 || n > 707 || candidate.Count != n || start < 0 || start >= n || witness < 0 || witness >= n ||
            end < 0 || end >= n || start == witness || start == end || witness == end ||
            !double.IsFinite(widthToleranceMm) || widthToleranceMm < 0 || !double.IsFinite(depthToleranceMm) || depthToleranceMm < 0)
            return false;
        var s = new GridPoint[n]; var c = new GridPoint[n];
        for (int i = 0; i < n; i++) { token.ThrowIfCancellationRequested(); s[i] = source[i]; c[i] = candidate[i]; }
        if (!OffsetIdealBoundaryEvidenceExperiment.TryVerify(s, c, delta, out reason, token)) return false;
        const double scale = 1_000_000;
        var sourceMm = new Point2[n]; var candidateMm = new Point2[n];
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            sourceMm[i] = new Point2(s[i].X / scale, s[i].Y / scale);
            candidateMm[i] = new Point2(c[i].X / scale, c[i].Y / scale);
        }
        if (!OffsetUserFeatureToleranceExperiment.TryVerify(sourceMm, candidateMm, start, witness, end,
            widthToleranceMm, depthToleranceMm, out reason, token)) return false;
        reason = "Ideal boundary and selected width/depth checks passed; other features, feature identity and production fitness are not certified.";
        return true;
    }
}
