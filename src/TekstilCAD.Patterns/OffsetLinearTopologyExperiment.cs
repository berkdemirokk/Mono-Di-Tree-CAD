using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Sufficient exact-arithmetic test of simplicity for every polygon in the
/// linear interpolation of two indexed integer contours. A false result is
/// inconclusive. This does not certify the ideal, unrounded miter path, offset
/// distances, feature dimensions, or any production boundary.
/// </summary>
internal static class OffsetLinearTopologyExperiment
{
    internal static bool TryVerify(IReadOnlyList<GridPoint> source, IReadOnlyList<GridPoint> candidate,
        out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidate);
        token.ThrowIfCancellationRequested();
        int n = source.Count;
        reason = "Invalid count or pair budget.";
        if (n < 3 || n > 1024 || candidate.Count != n) return false;
        var a = new GridPoint[n]; var b = new GridPoint[n];
        for (int i = 0; i < n; i++) { token.ThrowIfCancellationRequested(); a[i] = source[i]; b[i] = candidate[i]; }
        // Strict, unchanged turn signs throughout exclude collapsed edges and
        // overlap of adjacent edges. They are conservative for collinear turns.
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            if (TurnSign((i + n - 1) % n, i, (i + 1) % n) == 0)
            { reason = $"Adjacent edges at {i} cannot be certified."; return false; }
        }
        for (int i = 0; i < n; i++)
        for (int j = i + 1; j < n; j++)
        {
            token.ThrowIfCancellationRequested();
            if (j == i + 1 || (i == 0 && j == n - 1)) continue;
            int i2 = (i + 1) % n, j2 = (j + 1) % n;
            // Either both endpoints of one moving segment stay strictly on
            // one side of the other's supporting line for the entire motion.
            int s = TurnSign(i, i2, j), t = TurnSign(i, i2, j2);
            if (s != 0 && s == t) continue;
            s = TurnSign(j, j2, i); t = TurnSign(j, j2, i2);
            if (s != 0 && s == t) continue;
            reason = $"Edges {i} and {j} cannot be certified disjoint.";
            return false;
        }
        reason = "Indexed linear interpolation is simple; ideal miter motion and production fitness are not certified.";
        return true;

        int TurnSign(int p, int q, int r)
        {
            var u0 = Vector(a[p], a[q]); var v0 = Vector(a[p], a[r]);
            var u1 = Vector(b[p], b[q]); var v1 = Vector(b[p], b[r]);
            // The orientation is quadratic in t. Its Bernstein coefficients
            // have a strict common sign only if it keeps that sign for all t.
            var first = Cross(u0, v0); var last = Cross(u1, v1);
            var middleTwice = Cross(u0, v1) + Cross(u1, v0);
            return first.Sign != 0 && first.Sign == middleTwice.Sign && first.Sign == last.Sign ? first.Sign : 0;
        }
    }

    private static (BigInteger X, BigInteger Y) Vector(GridPoint p, GridPoint q) =>
        ((BigInteger)q.X - p.X, (BigInteger)q.Y - p.Y);
    private static BigInteger Cross((BigInteger X, BigInteger Y) a, (BigInteger X, BigInteger Y) b) =>
        a.X * b.Y - a.Y * b.X;
}
