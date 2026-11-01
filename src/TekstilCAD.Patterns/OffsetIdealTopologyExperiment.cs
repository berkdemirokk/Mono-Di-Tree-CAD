using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>Experimental sufficient test for simplicity of the ideal indexed
/// miter path. It does not verify Clipper output, feature dimensions, or
/// production suitability. Rejection includes inconclusive cases.</summary>
internal static class OffsetIdealTopologyExperiment
{
    internal static bool TryVerify(IReadOnlyList<GridPoint> source, IReadOnlyList<GridPoint> candidate,
        long delta, out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(candidate);
        token.ThrowIfCancellationRequested();
        reason = "Invalid contour size.";
        int n = source.Count;
        if (n < 3 || n > 1024 || candidate.Count != n) return false;
        // Private snapshots ensure all dependent checks examine the same inputs.
        var s = new GridPoint[n]; var c = new GridPoint[n];
        BigInteger movementL1 = 0;
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested(); s[i] = source[i]; c[i] = candidate[i];
            movementL1 = BigInteger.Max(movementL1,
                BigInteger.Abs((BigInteger)c[i].X - s[i].X) + BigInteger.Abs((BigInteger)c[i].Y - s[i].Y));
        }
        if (!OffsetIdealPathBoundExperiment.TryVerify(s, c, delta, out reason, token) ||
            !OffsetLinearTopologyExperiment.TryVerify(s, c, out reason, token)) return false;

        // Ideal and grid-path vertices differ by <=1 per axis. Therefore each
        // incident vector differs by <=2 per axis. Bound every cross-product
        // perturbation term over the full interval, including the quadratic term.
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            int p = (i + n - 1) % n, q = (i + 1) % n;
            var u0 = Vector(s[p], s[i]); var v0 = Vector(s[i], s[q]);
            var u1 = Vector(c[p], c[i]); var v1 = Vector(c[i], c[q]);
            int sign = Cross(u0, v0).Sign;
            BigInteger bound = 2 * (BigInteger.Max(BigInteger.Abs(u0.X), BigInteger.Abs(u1.X)) +
                BigInteger.Max(BigInteger.Abs(u0.Y), BigInteger.Abs(u1.Y)) +
                BigInteger.Max(BigInteger.Abs(v0.X), BigInteger.Abs(v1.X)) +
                BigInteger.Max(BigInteger.Abs(v0.Y), BigInteger.Abs(v1.Y))) + 8;
            if (sign == 0 || Cross(u0, v0) * sign <= bound || Cross(u1, v1) * sign <= bound ||
                (Cross(u0, v1) + Cross(u1, v0)) * sign <= 2 * bound)
            { reason = $"Adjacent edges at {i} lack the perturbation margin."; return false; }
        }

        // Each ideal moving vertex is within movementL1+2 of its source in
        // Euclidean distance; L1 is an upper bound. Every point of a moving
        // edge has a corresponding source-edge point within the same bound.
        // If original non-adjacent segments are farther than twice that bound,
        // the triangle inequality excludes contact throughout the motion.
        var distance = movementL1 + 2;
        var thresholdSquared = 4 * distance * distance;
        for (int i = 0; i < n; i++) for (int j = i + 1; j < n; j++)
        {
            token.ThrowIfCancellationRequested();
            if (j == i + 1 || (i == 0 && j == n - 1)) continue;
            var a = s[i]; var b = s[(i + 1) % n]; var d = s[j]; var e = s[(j + 1) % n];
            if (!Far(a, d, e, thresholdSquared) || !Far(b, d, e, thresholdSquared) ||
                !Far(d, a, b, thresholdSquared) || !Far(e, a, b, thresholdSquared))
            { reason = $"Edges {i} and {j} lack the source separation margin."; return false; }
        }
        reason = "Ideal indexed miter path is simple under sufficient bounds; Clipper equivalence and feature dimensions are not certified.";
        return true;
    }

    // For disjoint planar segments their minimum distance is attained by an
    // endpoint-to-segment pair. Source simplicity has already been checked.
    private static bool Far(GridPoint point, GridPoint a, GridPoint b, BigInteger threshold)
    {
        var v = Vector(a, b); var w = Vector(a, point);
        var length = Dot(v, v); var projection = Dot(w, v);
        if (length.IsZero) return false;
        if (projection <= 0) return Dot(w, w) > threshold;
        if (projection >= length) { var e = Vector(b, point); return Dot(e, e) > threshold; }
        var cross = Cross(v, w);
        return cross * cross > threshold * length;
    }
    private static (BigInteger X, BigInteger Y) Vector(GridPoint a, GridPoint b) => ((BigInteger)b.X - a.X, (BigInteger)b.Y - a.Y);
    private static BigInteger Dot((BigInteger X, BigInteger Y) a, (BigInteger X, BigInteger Y) b) => a.X * b.X + a.Y * b.Y;
    private static BigInteger Cross((BigInteger X, BigInteger Y) a, (BigInteger X, BigInteger Y) b) => a.X * b.Y - a.Y * b.X;
}
