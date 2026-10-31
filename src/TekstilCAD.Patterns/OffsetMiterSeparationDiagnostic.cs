using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental interval diagnostic of non-adjacent edges of an ideal miter path.
/// This does not check adjacent edges, orientation, edge order, actual offset
/// output or feature preservation and must not authorize production geometry.
/// </summary>
public static class OffsetMiterSeparationDiagnostic
{
    public const int MaximumVertices = 4096;
    public const long MaximumPairBudget = 1_000_000;
    public enum Finding { NonAdjacentPairsSeparated, Unknown }
    public sealed record Result(Finding Finding, int? FirstEdge, int? SecondEdge, string? Reason);
    private readonly record struct Range(BigInteger Lo, BigInteger Hi);

    /// <summary>
    /// Proves only pairwise separation when every non-adjacent pair is separated
    /// by its bounding boxes or strict orientation signs. Interval uncertainty,
    /// possible intersection, invalid input and exhausted budget all return Unknown.
    /// The caller-provided orientation is not verified.
    /// </summary>
    public static Result Analyze(IReadOnlyList<GridPoint> source, long delta, int orientation,
        long pairBudget = MaximumPairBudget)
    {
        ArgumentNullException.ThrowIfNull(source);
        var count = source.Count;
        if (count < 3 || count > MaximumVertices || delta <= 0 || orientation is not (1 or -1) ||
            pairBudget < 0 || pairBudget > MaximumPairBudget)
            return new(Finding.Unknown, null, null, "Invalid inputs or vertex budget exceeded.");
        long pairs = (long)count * (count - 3) / 2;
        if (pairs > pairBudget)
            return new(Finding.Unknown, null, null, "Non-adjacent edge pair budget exceeded.");

        var boxes = new MiterBox[count];
        for (var i = 0; i < count; i++)
            if (!TryBound(source[(i + count - 1) % count], source[i], source[(i + 1) % count],
                delta, orientation, out boxes[i]))
                return new(Finding.Unknown, i, null, "Miter interval unavailable.");

        for (var i = 0; i < count; i++)
        for (var j = i + 1; j < count; j++)
        {
            if (j == i + 1 || (i == 0 && j == count - 1)) continue;
            var a = boxes[i]; var b = boxes[(i + 1) % count];
            var c = boxes[j]; var d = boxes[(j + 1) % count];
            if (!AreDisjoint(a,b,c,d))
                return new(Finding.Unknown, i, j, "Edges may intersect or interval bounds overlap.");
        }
        return new(Finding.NonAdjacentPairsSeparated, null, null, null);
    }

    private static bool AreDisjoint(MiterBox a, MiterBox b, MiterBox c, MiterBox d)
    {
        static bool Separated(Interval a, Interval b, Interval c, Interval d)
            => BigInteger.Max(a.Upper,b.Upper) < BigInteger.Min(c.Lower,d.Lower)
            || BigInteger.Max(c.Upper,d.Upper) < BigInteger.Min(a.Lower,b.Lower);
        if (Separated(a.X,b.X,c.X,d.X) || Separated(a.Y,b.Y,c.Y,d.Y)) return true;
        var abc = Cross(a,b,c); var abd = Cross(a,b,d);
        if (SameStrictSign(abc,abd)) return true;
        return SameStrictSign(Cross(c,d,a), Cross(c,d,b));
    }

    private static bool SameStrictSign(Range a, Range b)
        => (a.Lo > 0 && b.Lo > 0) || (a.Hi < 0 && b.Hi < 0);
    private static Range Difference(Interval a, Interval b) => new(a.Lower-b.Upper,a.Upper-b.Lower);
    private static Range Multiply(Range a, Range b)
    {
        BigInteger[] values = [a.Lo*b.Lo,a.Lo*b.Hi,a.Hi*b.Lo,a.Hi*b.Hi];
        return new(values.Min(),values.Max());
    }
    private static Range Cross(MiterBox a, MiterBox b, MiterBox c)
    {
        var p = Multiply(Difference(b.X,a.X),Difference(c.Y,a.Y));
        var q = Multiply(Difference(b.Y,a.Y),Difference(c.X,a.X));
        return new(p.Lo-q.Hi,p.Hi-q.Lo);
    }
}
