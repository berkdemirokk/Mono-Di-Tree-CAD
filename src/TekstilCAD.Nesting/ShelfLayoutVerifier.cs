using System.Numerics;
using TekstilCAD.Core;

namespace TekstilCAD.Nesting;

/// <summary>
/// Checks actual placed bounding boxes, fabric edges and pairwise clearance. This is a
/// conservative box check, not a certificate for dense polygon nesting or contour validity.
/// </summary>
public static class ShelfLayoutVerifier
{
    public const long MaximumPairChecks = 2_000_000;
    public const int MaximumVertices = 200_000;

    /// <exception cref="InvalidOperationException">The layout cannot be verified safely.</exception>
    public static void Verify(NestingResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(result);
        if (result.Layout?.Pieces is null || !double.IsFinite(result.FabricWidth) || result.FabricWidth <= 0 ||
            !double.IsFinite(result.UsedLength) || result.UsedLength < 0 ||
            !double.IsFinite(result.Gap) || result.Gap < 0 ||
            !double.IsFinite(result.EdgeMargin) || result.EdgeMargin < 0)
            throw new InvalidOperationException("Invalid layout dimensions.");
        var pieces = result.Layout.Pieces;
        if (pieces.Count > NestingEngine.MaximumPieces)
            throw new InvalidOperationException("Layout verification resource limit exceeded.");
        var boxes = new List<Bounds2>(pieces.Count);
        long vertices = 0;
        foreach (var piece in pieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (piece?.Points is null || piece.Points.Count < 3 || piece.Points.Count > NestingEngine.MaximumVerticesPerPiece ||
                (vertices += piece.Points.Count) > MaximumVertices)
                throw new InvalidOperationException("Invalid contour or layout verification resource limit exceeded.");
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            foreach (var point in piece.Points)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!point.IsFinite) throw new InvalidOperationException("Nonfinite contour coordinate.");
                minX = Math.Min(minX, point.X); maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y); maxY = Math.Max(maxY, point.Y);
            }
            if (minX >= maxX || minY >= maxY || minX < result.EdgeMargin || minY < result.EdgeMargin ||
                !DifferenceAtLeast(result.FabricWidth, maxX, result.EdgeMargin) ||
                !DifferenceAtLeast(result.UsedLength, maxY, result.EdgeMargin))
                throw new InvalidOperationException("A placed box violates the fabric boundary.");
            boxes.Add(new Bounds2(minX, minY, maxX, maxY));
        }
        // Count candidate intervals on both axes before enumerating any pairs.
        var xOrder = Enumerable.Range(0, boxes.Count).OrderBy(i => boxes[i].MinX).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        var yOrder = Enumerable.Range(0, boxes.Count).OrderBy(i => boxes[i].MinY).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        long xCandidates = CountCandidates(boxes, xOrder, true, result.Gap, cancellationToken);
        long yCandidates = CountCandidates(boxes, yOrder, false, result.Gap, cancellationToken);
        if (Math.Min(xCandidates, yCandidates) > MaximumPairChecks)
            throw new InvalidOperationException("Layout verification resource limit exceeded.");

        bool useX = xCandidates <= yCandidates;
        var queue = new PriorityQueue<int, double>();
        var active = new HashSet<int>();
        foreach (int index in useX ? xOrder : yOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = boxes[index];
            double minimum = useX ? current.MinX : current.MinY;
            while (queue.TryPeek(out int expired, out double maximum) &&
                   DifferenceAtLeast(minimum, maximum, result.Gap))
            {
                cancellationToken.ThrowIfCancellationRequested();
                queue.Dequeue();
                active.Remove(expired);
            }
            foreach (int other in active)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var a = boxes[other]; var b = current;
                if (!DifferenceAtLeast(b.MinX, a.MaxX, result.Gap) &&
                    !DifferenceAtLeast(a.MinX, b.MaxX, result.Gap) &&
                    !DifferenceAtLeast(b.MinY, a.MaxY, result.Gap) &&
                    !DifferenceAtLeast(a.MinY, b.MaxY, result.Gap))
                    throw new InvalidOperationException("Placed boxes overlap or violate the requested gap.");
            }
            active.Add(index);
            queue.Enqueue(index, useX ? current.MaxX : current.MaxY);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static long CountCandidates(List<Bounds2> boxes, int[] order, bool useX, double gap,
        CancellationToken cancellationToken)
    {
        var queue = new PriorityQueue<int, double>();
        long count = 0;
        foreach (int index in order)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var box = boxes[index];
            double minimum = useX ? box.MinX : box.MinY;
            while (queue.TryPeek(out _, out double maximum) && DifferenceAtLeast(minimum, maximum, gap))
            {
                cancellationToken.ThrowIfCancellationRequested();
                queue.Dequeue();
            }
            count += queue.Count;
            if (count > MaximumPairChecks) return count;
            queue.Enqueue(index, useX ? box.MaxX : box.MaxY);
        }
        return count;
    }
    // Compare represented binary64 values exactly, avoiding subtraction rounding up a short gap.
    internal static bool DifferenceAtLeast(double a, double b, double clearance)
    {
        if (!double.IsFinite(a) || !double.IsFinite(b) || !double.IsFinite(clearance))
            throw new ArgumentOutOfRangeException(nameof(a), "Karşılaştırılan değerler sonlu olmalıdır.");
        var x = Binary(a); var y = Binary(b); var z = Binary(clearance);
        int exponent = Math.Min(x.Exponent, Math.Min(y.Exponent, z.Exponent));
        return (x.Significand << (x.Exponent - exponent)) - (y.Significand << (y.Exponent - exponent)) >=
               (z.Significand << (z.Exponent - exponent));
    }

    private static (BigInteger Significand, int Exponent) Binary(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        int exponent = (int)((bits >> 52) & 0x7ff);
        long fraction = bits & 0x000f_ffff_ffff_ffffL;
        var significand = new BigInteger(exponent == 0 ? fraction : fraction | (1L << 52));
        if (bits < 0) significand = -significand;
        return (significand, exponent == 0 ? -1074 : exponent - 1075);
    }
}

