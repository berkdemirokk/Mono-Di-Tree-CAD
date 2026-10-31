using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental check of one explicitly selected feature on two indexed grid
/// contours. It does not find features, establish their correspondence, prove
/// their dimensions are equal, or authorize production geometry. Thresholds are
/// caller-provided integer grid units, with no assumed global tolerance.
/// </summary>
internal static class OffsetNotchWitness
{
    private const int MaximumVertices = 1024;
    private const long MaximumPairChecks = 1_100_000;

    internal static bool TryValidate(IReadOnlyList<GridPoint> source, IReadOnlyList<GridPoint> candidate,
        int startIndex, int endIndex, long minSourceDepth, long minCandidateDepth,
        out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidate);
        reason = "";
        token.ThrowIfCancellationRequested();
        var n = source.Count;
        if (n < 4 || n > MaximumVertices || candidate.Count != n ||
            startIndex < 0 || startIndex >= n || endIndex < 0 || endIndex >= n ||
            minSourceDepth <= 0 || minCandidateDepth <= 0)
        { reason = "Invalid witness, depth, or contour size."; return false; }
        int interiorCount = (endIndex - startIndex - 1 + n) % n;
        if (interiorCount == 0 || interiorCount >= n - 2)
        { reason = "The witness needs an interior chain and at least one complementary edge."; return false; }

        // Snapshot inputs: the validated features must not change while walking.
        var a = new GridPoint[n]; var b = new GridPoint[n];
        for (int i = 0; i < n; i++) { token.ThrowIfCancellationRequested(); a[i] = source[i]; b[i] = candidate[i]; }
        long pairs = 0;
        if (!Simple(a, ref pairs, token) || !Simple(b, ref pairs, token))
        { reason = "A contour is degenerate, self-intersecting, or exceeds the work budget."; return false; }
        int direction = Area(a, token).Sign;
        if (direction == 0 || Area(b, token).Sign != direction)
        { reason = "Contour orientation is zero or differs."; return false; }
        if (!Chain(a, startIndex, interiorCount, direction, minSourceDepth, token) ||
            !Chain(b, startIndex, interiorCount, direction, minCandidateDepth, token))
        { reason = "The selected feature is ambiguous or shallower than the requested depth."; return false; }
        reason = "The selected source and candidate chains meet their respective minimum depths; other dimensions and production fitness are not certified.";
        return true;
    }

    private static bool Chain(GridPoint[] contour, int start, int count, int orientation, long minDepth, CancellationToken token)
    {
        int n = contour.Length; var p = contour[start]; var q = contour[(start + count + 1) % n];
        BigInteger dx = (BigInteger)q.X - p.X, dy = (BigInteger)q.Y - p.Y;
        BigInteger lengthSquared = dx * dx + dy * dy;
        if (lengthSquared.IsZero) return false;
        BigInteger previous = 0, maximumHeight = 0; bool hasConcaveTurn = false;
        for (int k = 1; k <= count; k++)
        {
            token.ThrowIfCancellationRequested();
            var v = contour[(start + k) % n];
            BigInteger vx = (BigInteger)v.X - p.X, vy = (BigInteger)v.Y - p.Y;
            BigInteger projection = dx * vx + dy * vy;
            BigInteger height = orientation * (dx * vy - dy * vx);
            var prior = contour[(start + k - 1) % n];
            var next = contour[(start + k + 1) % n];
            hasConcaveTurn |= orientation * Cross(prior, v, next).Sign < 0;
            // Backtracking or crossing the supporting chord makes this simple
            // witness ambiguous. A zero height at an intermediate point is
            // rejected so the claimed feature is one contiguous indentation.
            if (projection < previous || projection > lengthSquared || height <= 0) return false;
            previous = projection;
            maximumHeight = BigInteger.Max(maximumHeight, height);
        }
        BigInteger depth = minDepth;
        return hasConcaveTurn && maximumHeight * maximumHeight >= depth * depth * lengthSquared;
    }

    private static bool Simple(GridPoint[] points, ref long checks, CancellationToken token)
    {
        var seen = new HashSet<GridPoint>(); int n = points.Length;
        foreach (var p in points) { token.ThrowIfCancellationRequested(); if (!seen.Add(p)) return false; }
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            var a = points[(i + n - 1) % n]; var b = points[i]; var c = points[(i + 1) % n];
            if (Cross(a,b,c).IsZero && (((BigInteger)b.X-a.X)*((BigInteger)c.X-b.X) +
                ((BigInteger)b.Y-a.Y)*((BigInteger)c.Y-b.Y)).Sign < 0) return false;
        }
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                token.ThrowIfCancellationRequested();
                if (++checks > MaximumPairChecks) return false;
                if (j == i + 1 || (i == 0 && j == n - 1)) continue;
                if (Intersects(points[i], points[(i + 1) % n], points[j], points[(j + 1) % n])) return false;
            }
        return true;
    }

    private static BigInteger Area(GridPoint[] points, CancellationToken token)
    {
        BigInteger area = 0;
        for (int i = 0; i < points.Length; i++)
        { token.ThrowIfCancellationRequested(); var p = points[i]; var q = points[(i + 1) % points.Length]; area += (BigInteger)p.X * q.Y - (BigInteger)p.Y * q.X; }
        return area;
    }
    private static BigInteger Cross(GridPoint a, GridPoint b, GridPoint c) =>
        ((BigInteger)b.X - a.X) * ((BigInteger)c.Y - a.Y) - ((BigInteger)b.Y - a.Y) * ((BigInteger)c.X - a.X);
    private static bool On(GridPoint a, GridPoint b, GridPoint p) =>
        Math.Min(a.X,b.X) <= p.X && p.X <= Math.Max(a.X,b.X) && Math.Min(a.Y,b.Y) <= p.Y && p.Y <= Math.Max(a.Y,b.Y);
    private static bool Intersects(GridPoint a, GridPoint b, GridPoint c, GridPoint d)
    {
        var abC = Cross(a,b,c).Sign; var abD = Cross(a,b,d).Sign;
        var cdA = Cross(c,d,a).Sign; var cdB = Cross(c,d,b).Sign;
        return (abC == 0 && On(a,b,c)) || (abD == 0 && On(a,b,d)) ||
            (cdA == 0 && On(c,d,a)) || (cdB == 0 && On(c,d,b)) ||
            (abC * abD < 0 && cdA * cdB < 0);
    }
}
