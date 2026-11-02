using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental composition of indexed ideal-boundary checks and per-turn
/// local measurements. It covers all source concave vertices, but does not
/// discover complete or nested garment features, validate their semantics,
/// select tolerances, or authorize production. Inputs must be stable while copied.
/// </summary>
internal static class OffsetConcaveMappedEvidence
{
    internal static bool TryVerify(IReadOnlyList<GridPoint> source, IReadOnlyList<GridPoint> candidate,
        long delta, IReadOnlyList<(int Start, int Witness, int End, double WidthMm, double DepthMm)> features,
        out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(features);
        token.ThrowIfCancellationRequested();
        int n = source.Count, count = features.Count;
        reason = "Invalid contour or feature count.";
        if (n < 3 || n > 707 || candidate.Count != n || count < 1 || count > n) return false;
        var s = new GridPoint[n]; var c = new GridPoint[n];
        for (int i = 0; i < n; i++) { token.ThrowIfCancellationRequested(); s[i] = source[i]; c[i] = candidate[i]; }
        var selected = new (int Start, int Witness, int End, double WidthMm, double DepthMm)[count];
        var indices = new int[count];
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            var f = features[i];
            if (f.Witness < 0 || f.Witness >= n || f.Start != (f.Witness+n-1)%n || f.End != (f.Witness+1)%n)
            { reason = $"Feature {i} is not an indexed adjacent-edge witness."; return false; }
            selected[i] = f; indices[i] = f.Witness;
        }
        if (!OffsetConcaveWitnessCoverage.TryVerify(s, indices, out reason, token)) return false;
        if (!OffsetSelectedFeaturesEvidenceExperiment.TryVerify(s, c, delta, selected, out reason, token)) return false;
        reason = "Indexed boundary and all concave adjacent triples passed; full feature identity, dimensions and production fitness are not certified.";
        return true;
    }
}
