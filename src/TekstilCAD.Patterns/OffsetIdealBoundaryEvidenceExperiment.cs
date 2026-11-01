using TekstilCAD.Core;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>Combines existing conservative checks for an indexed ideal-miter
/// candidate. This is experimental evidence only; it does not establish garment
/// feature identity, acceptable dimensional changes, Clipper equivalence, or
/// production suitability.</summary>
/// <remarks>The caller must keep both input collections unchanged while they
/// are copied; the two snapshots are not atomic. Although the initial count
/// check allows up to 1024 vertices, the downstream millimeter topology proof
/// has a two-million-pair budget of 4*n*n - 2*n, so this combined experiment
/// can accept at most 707 vertices.</remarks>
internal static class OffsetIdealBoundaryEvidenceExperiment
{
    internal static bool TryVerify(IReadOnlyList<GridPoint> source, IReadOnlyList<GridPoint> candidate,
        long delta, out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(candidate);
        token.ThrowIfCancellationRequested();
        reason = "Invalid contour size or allowance.";
        int n = source.Count;
        if (n < 3 || n > 1024 || candidate.Count != n || delta <= 1) return false;
        var s = new GridPoint[n]; var c = new GridPoint[n];
        for (int i = 0; i < n; i++) { token.ThrowIfCancellationRequested(); s[i] = source[i]; c[i] = candidate[i]; }
        if (!OffsetCandidateProof.TryCreate(s, delta, out var expected, out reason, token)) return false;
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            if (expected[i] != c[i]) { reason = $"Candidate corner {i} differs from the proven grid miter."; return false; }
        }
        if (!OffsetIdealTopologyExperiment.TryVerify(s, c, delta, out reason, token)) return false;
        // One grid unit is a fixed numerical band, not a feature tolerance.
        if (!OffsetIndexedEdgeEvidence.TryVerify(s, c, delta, 1, out reason, token)) return false;
        const double scale = 1_000_000;
        var sourceMm = new Point2[n]; var candidateMm = new Point2[n];
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            sourceMm[i] = new Point2(s[i].X / scale, s[i].Y / scale);
            candidateMm[i] = new Point2(c[i].X / scale, c[i].Y / scale);
        }
        if (!OffsetMillimeterProof.TryValidate(s, c, sourceMm, candidateMm, out reason, token)) return false;
        reason = "Indexed grid miters, edge distance band, ideal-path topology and millimeter topology verified; feature dimensions and production fitness are not certified.";
        return true;
    }
}
