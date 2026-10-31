using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>A caller-supplied closed interval of positive distances in grid units.</summary>
internal readonly record struct OffsetDepthRange(long LowerNumerator, long LowerDenominator,
    long UpperNumerator, long UpperDenominator);

/// <summary>Separate admissible source and output depths for one selected index.</summary>
internal readonly record struct OffsetRelativeFeatureTarget(int Index, OffsetDepthRange SourceDepth,
    OffsetDepthRange CandidateDepth);

/// <summary>
/// Experimental, exact local checks for explicitly selected vertex triples. The
/// caller supplies both admissible depth intervals: an offset need not retain
/// the original depth. This does not discover features, establish corresponding
/// garment semantics, validate contour topology, or authorize production.
/// </summary>
internal static class OffsetRelativeFeatureProof
{
    private const int MaximumVertices = 1024;

    /// <remarks>Callers must not mutate input collections during snapshot copying.</remarks>
    internal static bool TryValidate(IReadOnlyList<GridPoint> source, IReadOnlyList<GridPoint> candidate,
        IReadOnlyList<OffsetRelativeFeatureTarget> targets, out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(targets);
        reason = "";
        token.ThrowIfCancellationRequested();
        int count = source.Count;
        if (count < 3 || count > MaximumVertices || candidate.Count != count || targets.Count < 1 || targets.Count > count)
            return Reject(out reason, "Invalid contour or selected feature count.");

        var original = new GridPoint[count];
        var result = new GridPoint[count];
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            original[i] = source[i];
            result[i] = candidate[i];
        }

        var selected = new OffsetRelativeFeatureTarget[targets.Count];
        var indices = new HashSet<int>();
        for (int i = 0; i < selected.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var target = targets[i];
            if (target.Index < 0 || target.Index >= count || !indices.Add(target.Index) ||
                !ValidRange(target.SourceDepth) || !ValidRange(target.CandidateDepth))
                return Reject(out reason, "Feature index or explicit depth interval is invalid.");
            selected[i] = target;
        }

        int orientation = SignedArea(original, token).Sign;
        if (orientation == 0 || SignedArea(result, token).Sign != orientation)
            return Reject(out reason, "Contour orientation is zero or differs.");

        foreach (var target in selected)
        {
            token.ThrowIfCancellationRequested();
            if (!DepthWithin(original, target.Index, orientation, target.SourceDepth) ||
                !DepthWithin(result, target.Index, orientation, target.CandidateDepth))
                return Reject(out reason, $"Selected feature {target.Index} is ambiguous or outside its explicit depth interval.");
        }
        token.ThrowIfCancellationRequested();
        reason = "Selected local depths satisfy the supplied intervals only; feature correspondence and production fitness are not certified.";
        return true;
    }

    private static bool ValidRange(OffsetDepthRange range)
    {
        if (range.LowerNumerator <= 0 || range.UpperNumerator <= 0 ||
            range.LowerDenominator <= 0 || range.UpperDenominator <= 0) return false;
        return (BigInteger)range.LowerNumerator * range.UpperDenominator <=
            (BigInteger)range.UpperNumerator * range.LowerDenominator;
    }

    private static bool DepthWithin(GridPoint[] points, int index, int orientation, OffsetDepthRange range)
    {
        int count = points.Length;
        var a = points[(index + count - 1) % count];
        var b = points[index];
        var c = points[(index + 1) % count];
        BigInteger ux = (BigInteger)b.X - a.X, uy = (BigInteger)b.Y - a.Y;
        BigInteger vx = (BigInteger)c.X - b.X, vy = (BigInteger)c.Y - b.Y;
        BigInteger dx = (BigInteger)c.X - a.X, dy = (BigInteger)c.Y - a.Y;
        BigInteger cross = ux * vy - uy * vx;
        BigInteger chordSquared = dx * dx + dy * dy;
        BigInteger projection = ux * dx + uy * dy;
        if (cross.Sign * orientation >= 0 || chordSquared.IsZero || projection < 0 || projection > chordSquared)
            return false;

        // All interval endpoints and the height are positive, so squaring
        // preserves order and avoids approximate square roots entirely.
        BigInteger squaredCross = cross * cross;
        BigInteger lowerN = range.LowerNumerator, lowerD = range.LowerDenominator;
        BigInteger upperN = range.UpperNumerator, upperD = range.UpperDenominator;
        return squaredCross * lowerD * lowerD >= lowerN * lowerN * chordSquared &&
            squaredCross * upperD * upperD <= upperN * upperN * chordSquared;
    }

    private static BigInteger SignedArea(GridPoint[] points, CancellationToken token)
    {
        BigInteger area = 0;
        for (int i = 0; i < points.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var a = points[i]; var b = points[(i + 1) % points.Length];
            area += (BigInteger)a.X * b.Y - (BigInteger)a.Y * b.X;
        }
        return area;
    }

    private static bool Reject(out string reason, string message) { reason = message; return false; }
}
