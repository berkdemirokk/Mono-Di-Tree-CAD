using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental directed edge-interval comparison of two already constructed grid
/// boundaries. It is diagnostic only: a match does not prove feature preservation,
/// offset correctness, topology, or fitness for production.
/// </summary>
public static class OffsetOrderedIntervalDiagnostic
{
    public sealed record Result(bool Compared, double ReferenceUncovered, double ObservedUncovered,
        bool ReferenceOrderPreserved, bool ObservedOrderPreserved, string Reason);

    /// <summary>
    /// Distances and uncovered lengths are grid units. Each directed edge receives
    /// intervals from parallel, similarly directed edges whose endpoints lie within
    /// tolerance of its supporting line. Intervals are unioned, not summed. The
    /// order check follows assigned edge indices cyclically. Partial overlap,
    /// tolerance, sampling and assignment ambiguity make this an observation only.
    /// </summary>
    public static Result Compare(IReadOnlyList<GridPoint> reference, IReadOnlyList<GridPoint> observed,
        double tolerance, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(reference); ArgumentNullException.ThrowIfNull(observed);
        token.ThrowIfCancellationRequested();
        if (!double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        if (reference.Count < 3 || observed.Count < 3 || (long)reference.Count * observed.Count > 4_000_000)
            return new(false, 0, 0, false, false, "Invalid boundary size or comparison work budget exceeded.");
        if (HasZeroEdge(reference) || HasZeroEdge(observed))
            return new(false, 0, 0, false, false, "A boundary contains a zero-length edge.");
        var a = Measure(reference, observed, tolerance, token);
        var b = Measure(observed, reference, tolerance, token);
        return new(true, a.Missing, b.Missing, a.Order, b.Order,
            "Experimental interval measurements only; they do not certify feature preservation or production suitability.");
    }

    private static bool HasZeroEdge(IReadOnlyList<GridPoint> p)
    {
        for (int i = 0; i < p.Count; i++) if (p[i] == p[(i + 1) % p.Count]) return true;
        return false;
    }

    private static (double Missing, bool Order) Measure(IReadOnlyList<GridPoint> from,
        IReadOnlyList<GridPoint> to, double tolerance, CancellationToken token)
    {
        double missing = 0;
        var assignment = new int[to.Count]; Array.Fill(assignment, -1);
        var best = new double[to.Count];
        for (int i = 0; i < from.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var a = from[i]; var b = from[(i + 1) % from.Count];
            double dx = (double)((decimal)b.X - a.X), dy = (double)((decimal)b.Y - a.Y);
            double len = Math.Sqrt(dx * dx + dy * dy), ux = dx / len, uy = dy / len;
            var intervals = new List<(double Start, double End)>();
            for (int j = 0; j < to.Count; j++)
            {
                if ((j & 255) == 0) token.ThrowIfCancellationRequested();
                var p = to[j]; var q = to[(j + 1) % to.Count];
                // Decimal subtraction occurs before conversion, preserving small
                // coordinate differences at large integer origins without overflow.
                double px = (double)((decimal)p.X - a.X), py = (double)((decimal)p.Y - a.Y);
                double qx = (double)((decimal)q.X - a.X), qy = (double)((decimal)q.Y - a.Y);
                if (Math.Abs(px * uy - py * ux) > tolerance || Math.Abs(qx * uy - qy * ux) > tolerance) continue;
                double start = px * ux + py * uy, end = qx * ux + qy * uy;
                if (end <= start) continue;
                start = Math.Max(0, start); end = Math.Min(len, end);
                if (end <= start) continue;
                intervals.Add((start, end));
                if (end - start > best[j]) { best[j] = end - start; assignment[j] = i; }
            }
            double covered = 0, cursor = 0;
            foreach (var interval in intervals.OrderBy(x => x.Start))
            {
                if (interval.End > cursor) covered += interval.End - Math.Max(cursor, interval.Start);
                cursor = Math.Max(cursor, interval.End);
            }
            missing += Math.Max(0, len - covered);
        }
        // A cyclic sequence is ordered only if it makes at most one revolution.
        var indices = assignment.Where(i => i >= 0).ToArray();
        long travel = 0;
        for (int i = 1; i < indices.Length; i++) travel += (indices[i] - indices[i - 1] + from.Count) % from.Count;
        return (missing, indices.Length > 0 && travel < from.Count);
    }
}

