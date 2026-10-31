using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental sufficient certificate for two cyclic polygonal paths. Each actual
/// vertex is paired with a nearest reference vertex; pairs must advance by zero
/// or one reference edge and complete exactly one circuit. Linear interpolation
/// of consecutive pairs proves the entire boundaries admit a monotone matching
/// within the supplied Euclidean distance. This does not prove offset correctness,
/// feature dimensions, simplicity, or the suitability of the distance threshold.
/// </summary>
internal static class OffsetOrderedBoundaryCertificate
{
    internal static bool TryVerify(IReadOnlyList<GridPoint> reference, IReadOnlyList<GridPoint> actual,
        long tolerance, out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(actual);
        reason = "Invalid size or tolerance.";
        if (reference.Count < 3 || actual.Count < 3 || reference.Count > 1024 || actual.Count > 2048 || tolerance < 0) return false;
        var r = reference.ToArray(); var a = actual.ToArray();
        var matches = new int[a.Length];
        var limit = (BigInteger)tolerance * tolerance;
        for (int i = 0; i < a.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            BigInteger? best = null;
            int index = -1;
            for (int j = 0; j < r.Length; j++)
            {
                BigInteger dx = (BigInteger)a[i].X - r[j].X, dy = (BigInteger)a[i].Y - r[j].Y;
                var distance = dx * dx + dy * dy;
                if (best is null || distance < best.Value) { best = distance; index = j; }
            }
            if (best!.Value > limit) { reason = $"Vertex {i} exceeds the distance bound."; return false; }
            matches[i] = index;
        }
        int steps = 0;
        for (int i = 0; i < matches.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var step = (matches[(i + 1) % matches.Length] - matches[i] + r.Length) % r.Length;
            if (step > 1) { reason = "Matching skips or reverses a reference edge."; return false; }
            steps += step;
        }
        if (steps != r.Length) { reason = "Matching does not traverse exactly one circuit."; return false; }
        reason = "Entire boundaries have an ordered distance-bounded matching; feature preservation is not certified.";
        return true;
    }
}
