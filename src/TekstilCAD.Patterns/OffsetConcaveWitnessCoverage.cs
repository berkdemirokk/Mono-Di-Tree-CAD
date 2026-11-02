using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental exact coverage check for source concave vertex indices. A pass
/// does not identify garment features, validate simplicity or prove that a
/// candidate preserves any notch. Inputs must remain stable while copied.
/// </summary>
internal static class OffsetConcaveWitnessCoverage
{
    internal static bool TryVerify(IReadOnlyList<GridPoint> source, IReadOnlyList<int> witnesses,
        out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(witnesses);
        token.ThrowIfCancellationRequested();
        int n = source.Count;
        reason = "Invalid source or witness count.";
        if (n < 3 || n > 1024 || witnesses.Count > n) return false;
        var points = new GridPoint[n]; var seen = new bool[n];
        for (int i = 0; i < n; i++) { token.ThrowIfCancellationRequested(); points[i] = source[i]; }
        for (int i = 0; i < witnesses.Count; i++)
        {
            token.ThrowIfCancellationRequested(); int index = witnesses[i];
            if (index < 0 || index >= n || seen[index]) { reason = "Invalid or duplicate witness."; return false; }
            seen[index] = true;
        }
        BigInteger area = 0;
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested(); var a = points[i]; var b = points[(i+1)%n];
            area += (BigInteger)a.X*b.Y - (BigInteger)a.Y*b.X;
        }
        if (area.IsZero) { reason = "Zero or ambiguous signed area."; return false; }
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            var a = points[(i+n-1)%n]; var b = points[i]; var c = points[(i+1)%n];
            var cross = ((BigInteger)b.X-a.X)*((BigInteger)c.Y-b.Y) - ((BigInteger)b.Y-a.Y)*((BigInteger)c.X-b.X);
            if (cross.IsZero) { reason = $"Turn {i} is collinear or degenerate."; return false; }
            if ((cross.Sign != area.Sign) != seen[i])
            { reason = $"Witness set does not exactly cover source concave turn {i}."; return false; }
        }
        reason = "All source concave turn indices covered only; feature identity, candidate and production fitness are not certified.";
        return true;
    }
}
