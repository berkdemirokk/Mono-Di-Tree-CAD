using TekstilCAD.Core;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>Experimental combined ideal boundary and explicitly selected feature
/// width/depth checks. Indices identify corresponding points in both contours;
/// tolerances are per feature in millimeters. Passing does not discover features,
/// validate their semantic correspondence or certify production suitability.</summary>
/// <remarks>All input collections must remain stable during copying; snapshots
/// are not atomic. The combined boundary check accepts at most 707 vertices.</remarks>
internal static class OffsetSelectedFeaturesEvidenceExperiment
{
    internal static bool TryVerify(IReadOnlyList<GridPoint> source, IReadOnlyList<GridPoint> candidate,
        long delta, IReadOnlyList<(int Start, int Witness, int End, double WidthMm, double DepthMm)> features,
        out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(features); token.ThrowIfCancellationRequested();
        int n = source.Count, count = features.Count;
        reason = "Invalid contour or selected feature count.";
        if (n < 3 || n > 707 || candidate.Count != n || count < 1 || count > n) return false;
        var s = new GridPoint[n]; var c = new GridPoint[n];
        for (int i = 0; i < n; i++) { token.ThrowIfCancellationRequested(); s[i] = source[i]; c[i] = candidate[i]; }
        var selected = new (int Start, int Witness, int End, double WidthMm, double DepthMm)[count];
        var unique = new HashSet<(int, int, int)>();
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested(); var f = features[i];
            if (f.Start < 0 || f.Start >= n || f.Witness < 0 || f.Witness >= n || f.End < 0 || f.End >= n ||
                f.Start == f.Witness || f.Start == f.End || f.Witness == f.End ||
                !double.IsFinite(f.WidthMm) || f.WidthMm < 0 || !double.IsFinite(f.DepthMm) || f.DepthMm < 0 ||
                !unique.Add((f.Start, f.Witness, f.End)))
            { reason = $"Invalid or repeated feature at position {i}."; return false; }
            selected[i] = f;
        }
        if (!OffsetIdealBoundaryEvidenceExperiment.TryVerify(s, c, delta, out reason, token)) return false;
        const double scale = 1_000_000;
        var sm = new Point2[n]; var cm = new Point2[n];
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            sm[i] = new Point2(s[i].X / scale, s[i].Y / scale);
            cm[i] = new Point2(c[i].X / scale, c[i].Y / scale);
        }
        for (int i = 0; i < selected.Length; i++)
        {
            token.ThrowIfCancellationRequested(); var f = selected[i];
            if (!OffsetUserFeatureToleranceExperiment.TryVerify(sm, cm, f.Start, f.Witness, f.End,
                f.WidthMm, f.DepthMm, out var detail, token))
            { reason = $"Feature {i}: {detail}"; return false; }
        }
        reason = "Ideal boundary and all selected width/depth checks passed; discovery, feature identity and production fitness are not certified.";
        return true;
    }
}
