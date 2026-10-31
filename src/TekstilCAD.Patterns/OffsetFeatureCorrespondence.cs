using System.Collections.Immutable;
using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>Exact local measurements for one caller-selected vertex in two indexed contours.</summary>
internal readonly record struct OffsetFeatureEvidence(int Index, BigInteger SourceSignedCross,
    BigInteger CandidateSignedCross, BigInteger SourceChordSquared, BigInteger CandidateChordSquared);

/// <summary>
/// Experimental local checks only. Passing does not establish contour simplicity,
/// offset accuracy, a feature's width, equality of depths, complete feature
/// discovery, or fitness for production. The caller must establish correspondence
/// and global contour validity independently and supply its own thresholds.
/// </summary>
internal static class OffsetFeatureCorrespondence
{
    private const int MaximumVertices = 1024;

    /// <remarks>The caller must not mutate inputs during snapshot copying.</remarks>
    internal static bool TryEvaluate(IReadOnlyList<GridPoint> source, IReadOnlyList<GridPoint> candidate,
        IReadOnlyList<int> concaveVertexIndices, long minSourceDepth, long minCandidateDepth, long minEdgeLength,
        out ImmutableArray<OffsetFeatureEvidence> evidence, out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(concaveVertexIndices);
        evidence = ImmutableArray<OffsetFeatureEvidence>.Empty;
        reason = "";
        token.ThrowIfCancellationRequested();
        int n = source.Count;
        if (n < 3 || n > MaximumVertices || candidate.Count != n || concaveVertexIndices.Count == 0 ||
            concaveVertexIndices.Count > n || minSourceDepth <= 0 || minCandidateDepth <= 0 || minEdgeLength <= 0)
            return Reject(out reason, "Invalid contour size, witness count or threshold.");

        var original = new GridPoint[n];
        var result = new GridPoint[n];
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            original[i] = source[i];
            result[i] = candidate[i];
        }
        var selected = new int[concaveVertexIndices.Count];
        var unique = new HashSet<int>();
        for (int i = 0; i < selected.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            int index = concaveVertexIndices[i];
            if (index < 0 || index >= n || !unique.Add(index))
                return Reject(out reason, "A selected vertex is invalid or repeated.");
            selected[i] = index;
        }

        int direction = SignedArea(original, token).Sign;
        if (direction == 0 || SignedArea(result, token).Sign != direction)
            return Reject(out reason, "Contour orientation is zero or differs.");

        var measurements = ImmutableArray.CreateBuilder<OffsetFeatureEvidence>(selected.Length);
        foreach (int index in selected)
        {
            token.ThrowIfCancellationRequested();
            if (!Measure(original, index, direction, minSourceDepth, minEdgeLength, out var sourceCross, out var sourceChord) ||
                !Measure(result, index, direction, minCandidateDepth, minEdgeLength, out var candidateCross, out var candidateChord))
                return Reject(out reason, $"Selected vertex {index} has ambiguous geometry or does not meet a local threshold.");
            measurements.Add(new(index, sourceCross, candidateCross, sourceChord, candidateChord));
        }
        token.ThrowIfCancellationRequested();
        evidence = measurements.MoveToImmutable();
        reason = "Selected vertices satisfy local thresholds only; global shape and production fitness are not certified.";
        return true;
    }

    private static bool Measure(GridPoint[] points, int index, int direction, long minDepth, long minEdge,
        out BigInteger signedCross, out BigInteger chordSquared)
    {
        int n = points.Length;
        GridPoint a = points[(index + n - 1) % n], b = points[index], c = points[(index + 1) % n];
        BigInteger ux = (BigInteger)b.X - a.X, uy = (BigInteger)b.Y - a.Y;
        BigInteger vx = (BigInteger)c.X - b.X, vy = (BigInteger)c.Y - b.Y;
        BigInteger dx = (BigInteger)c.X - a.X, dy = (BigInteger)c.Y - a.Y;
        signedCross = ux * vy - uy * vx;
        chordSquared = dx * dx + dy * dy;
        var edgeSquared = (BigInteger)minEdge * minEdge;
        var depthSquared = (BigInteger)minDepth * minDepth;
        var projection = ux * dx + uy * dy;
        return signedCross.Sign * direction < 0 && !chordSquared.IsZero &&
            ux * ux + uy * uy >= edgeSquared && vx * vx + vy * vy >= edgeSquared &&
            projection >= 0 && projection <= chordSquared &&
            signedCross * signedCross >= depthSquared * chordSquared;
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
