using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental exact comparison of a grid-rounded miter candidate with a
/// supplied forward boundary. This is not connected to production offsetting.
/// A match proves only that these two integer polygonal paths describe the same
/// oriented boundary; it does not certify ideal continuous geometry, feature
/// dimensions, manufacturing tolerance, or the interval constructor itself.
/// </summary>
internal static class OffsetForwardBoundaryProof
{
    private const int MaximumForwardVertices = 16_384;

    /// <summary>
    /// Inputs use the same integer grid as OffsetMiterIntervalDiagnostic.
    /// The forward path may start at another vertex and may split a straight
    /// edge into forward collinear segments. Reversal, backtracking, repeated
    /// vertices, ambiguity and every nonzero geometric difference are rejected.
    /// An explicitly repeated final closing point is accepted once.
    /// </summary>
    internal static bool TryVerify(IReadOnlyList<GridPoint> source, long delta,
        IReadOnlyList<GridPoint> forward, out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(forward);
        token.ThrowIfCancellationRequested();
        if (forward.Count < 3 || forward.Count > MaximumForwardVertices)
        { reason = "Forward boundary is empty or exceeds the vertex budget."; return false; }
        if (!OffsetCandidateProof.TryCreate(source, delta, out var candidate, out var candidateReason, token))
        { reason = "Candidate is not proven: " + candidateReason; return false; }
        if (!TryNormalize(forward, out var actual, out reason, token) ||
            !TryNormalize(candidate, out var ideal, out reason, token)) return false;
        if (actual.Count != ideal.Count)
        { reason = "Boundary corners differ after exact collinear subdivision removal."; return false; }
        var start = actual.IndexOf(ideal[0]);
        if (start < 0)
        { reason = "Candidate corner is absent from the forward boundary."; return false; }
        for (int i = 0; i < ideal.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            if (actual[(start+i)%actual.Count] != ideal[i])
            { reason = "Forward boundary differs in coordinate, order, or orientation."; return false; }
        }
        reason = "Experimental exact grid-boundary match only; feature preservation and production suitability are not certified.";
        return true;
    }

    private static bool TryNormalize(IReadOnlyList<GridPoint> input, out List<GridPoint> normalized,
        out string reason, CancellationToken token)
    {
        normalized = [];
        reason = "";
        var count = input.Count;
        if (count > 1 && input[0] == input[count-1]) count--;
        if (count < 3 || count > MaximumForwardVertices)
        { reason = "Boundary has an invalid number of vertices."; return false; }
        var list = new LinkedList<GridPoint>();
        var pending = new Queue<LinkedListNode<GridPoint>>();
        var unique = new HashSet<GridPoint>();
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            if (!unique.Add(input[i]))
            { reason = "Boundary repeats a vertex."; return false; }
            pending.Enqueue(list.AddLast(input[i]));
        }
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var node = pending.Dequeue();
            if (node.List != list) continue;
            if (list.Count < 3)
            { reason = "Boundary collapses after removing collinear vertices."; return false; }
            var previous = node.Previous ?? list.Last!;
            var next = node.Next ?? list.First!;
            if (!Cross(previous.Value,node.Value,next.Value).IsZero) continue;
            if (Dot(previous.Value,node.Value,next.Value) <= BigInteger.Zero)
            { reason = "Boundary contains a collinear reversal or zero edge."; return false; }
            list.Remove(node);
            pending.Enqueue(previous); pending.Enqueue(next);
        }
        if (list.Count < 3)
        { reason = "Boundary has fewer than three corners."; return false; }
        normalized = list.ToList();
        return true;
    }

    private static BigInteger Cross(GridPoint a, GridPoint b, GridPoint c) =>
        ((BigInteger)b.X-a.X)*((BigInteger)c.Y-b.Y)-((BigInteger)b.Y-a.Y)*((BigInteger)c.X-b.X);
    private static BigInteger Dot(GridPoint a, GridPoint b, GridPoint c) =>
        ((BigInteger)b.X-a.X)*((BigInteger)c.X-b.X)+((BigInteger)b.Y-a.Y)*((BigInteger)c.Y-b.Y);
}
