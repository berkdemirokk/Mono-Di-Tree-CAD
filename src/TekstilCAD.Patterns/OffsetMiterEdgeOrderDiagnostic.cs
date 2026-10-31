using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental edge-direction diagnostic for an ideal, unquantized miter path.
/// It does not inspect actual offset output, establish contour simplicity or
/// feature preservation, or authorize any production operation.
/// </summary>
public static class OffsetMiterEdgeOrderDiagnostic
{
    public const int DefaultMaximumVertices = 4096;

    public enum Finding { DirectionsPreserved, ReversedOrCollapsed, Unknown }
    public sealed record Result(Finding Finding, int? EdgeIndex, string? Reason);

    /// <summary>
    /// Compares each source edge with the corresponding ideal miter edge using
    /// directed integer intervals. Ambiguous direction or computation limits
    /// produce Unknown; no conclusion about other geometric properties follows.
    /// The orientation is supplied by the caller and is not itself verified.
    /// </summary>
    public static Result Analyze(IReadOnlyList<GridPoint> source, long delta, int orientation,
        int maximumVertices = DefaultMaximumVertices)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (maximumVertices < 3 || maximumVertices > DefaultMaximumVertices || source.Count < 3 ||
            source.Count > maximumVertices || delta <= 0 || orientation is not (1 or -1))
            return new(Finding.Unknown, null, "Invalid inputs or vertex budget exceeded.");

        var boxes = new MiterBox[source.Count];
        for (var i = 0; i < source.Count; i++)
        {
            if (!TryBound(source[(i + source.Count - 1) % source.Count], source[i],
                source[(i + 1) % source.Count], delta, orientation, out boxes[i]))
                return new(Finding.Unknown, i, "Miter interval unavailable.");
        }

        int? ambiguous = null;
        for (var i = 0; i < source.Count; i++)
        {
            var j = (i + 1) % source.Count;
            BigInteger dx = (BigInteger)source[j].X - source[i].X;
            BigInteger dy = (BigInteger)source[j].Y - source[i].Y;
            // Each box endpoint is already scaled by 2^80. Multiply by exact
            // integer source directions; no floating-point rounding is needed.
            var xLower = boxes[j].X.Lower - boxes[i].X.Upper;
            var xUpper = boxes[j].X.Upper - boxes[i].X.Lower;
            var yLower = boxes[j].Y.Lower - boxes[i].Y.Upper;
            var yUpper = boxes[j].Y.Upper - boxes[i].Y.Lower;
            static (BigInteger Lo, BigInteger Hi) Product(BigInteger coefficient, BigInteger lo, BigInteger hi)
                => coefficient.Sign >= 0 ? (coefficient * lo, coefficient * hi) : (coefficient * hi, coefficient * lo);
            var x = Product(dx, xLower, xUpper);
            var y = Product(dy, yLower, yUpper);
            var lower = x.Lo + y.Lo;
            var upper = x.Hi + y.Hi;
            if (upper <= 0) return new(Finding.ReversedOrCollapsed, i, "Ideal edge does not advance along its source edge.");
            if (lower <= 0) ambiguous ??= i;
        }
        return ambiguous is { } edge
            ? new(Finding.Unknown, edge, "Edge direction interval overlaps zero.")
            : new(Finding.DirectionsPreserved, null, null);
    }
}
