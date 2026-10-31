using System.Collections.Immutable;
using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// A source-indexed leaf pocket. One leaf may contain multiple original
/// concave vertices; neither a vertex nor a leaf is an individually identified
/// garment feature. This is not an offset or a preservation proof.
/// </summary>
internal sealed record OffsetPocket(ImmutableArray<int> SourceIndices, int Depth,
    ImmutableArray<int> OriginalConcaveVertices);

/// <summary>
/// Experimental exact-integer hull decomposition. This identifies bounded
/// candidate source pockets only; it does not classify garment notches, compare
/// an offset contour, guarantee feature dimensions or authorize production.
/// Ambiguous collinearity, contact and exhausted budgets are rejected.
/// </summary>
internal static class OffsetPocketDiscovery
{
    private const int MaxVertices = 1024;
    private const int MaxDepth = 64;
    private const int MaxNodes = 4096;
    private const long MaxWork = 2_000_000;

    internal static bool TryDiscover(IReadOnlyList<GridPoint> source,
        out ImmutableArray<OffsetPocket> pockets, out string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        pockets = ImmutableArray<OffsetPocket>.Empty;
        reason = "";
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Count is < 3 or > MaxVertices)
        { reason = "Kontur nokta sayısı desteklenen sınır dışında."; return false; }

        var points = new GridPoint[source.Count];
        var distinct = new HashSet<GridPoint>();
        for (int i = 0; i < points.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            points[i] = source[i];
            if (!distinct.Add(points[i]))
            { reason = "Yinelenen kontur noktası belirsiz."; return false; }
        }
        long work = 0;
        bool Spend()
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ++work <= MaxWork;
        }
        var ring = Enumerable.Range(0, points.Length).ToList();
        var area = Area(ring);
        if (area.IsZero) { reason = "Kontur alanı sıfır."; return false; }
        int orientation = area.Sign;
        var originalConcave = new HashSet<int>();
        for (int i = 0; i < ring.Count; i++)
        {
            var cross = Cross(points[ring[(i + ring.Count - 1) % ring.Count]], points[ring[i]], points[ring[(i + 1) % ring.Count]]);
            if (cross.IsZero) { reason = "Doğrusal komşu noktalar belirsiz."; return false; }
            if (cross.Sign != orientation) originalConcave.Add(ring[i]);
        }
        // Inclusive intersection rejects both crossings and non-adjacent touch.
        for (int i = 0; i < ring.Count; i++)
            for (int j = i + 1; j < ring.Count; j++)
            {
                if (!Spend()) { reason = "Kontur kontrolü iş sınırını aştı."; return false; }
                if (j == i + 1 || (i == 0 && j == ring.Count - 1)) continue;
                if (Intersects(points[ring[i]], points[ring[(i + 1) % ring.Count]],
                    points[ring[j]], points[ring[(j + 1) % ring.Count]]))
                { reason = "Kontur kesişiyor veya kendisine temas ediyor."; return false; }
            }
        if (orientation < 0) ring.Reverse();
        var leaves = ImmutableArray.CreateBuilder<OffsetPocket>();
        int nodes = 0;
        string failure = "";
        bool Walk(List<int> polygon, int depth, bool isRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++nodes > MaxNodes || depth > MaxDepth)
            { failure = "Cep ayrıştırma sınırı aşıldı."; return false; }
            if (Area(polygon).Sign < 0) polygon.Reverse();
            var ordered = polygon.OrderBy(i => points[i].X).ThenBy(i => points[i].Y).ToArray();
            var lower = new List<int>();
            var upper = new List<int>();
            bool Push(List<int> hull, int index)
            {
                while (hull.Count >= 2)
                {
                    if (!Spend()) { failure = "Cep ayrıştırma iş sınırı aşıldı."; return false; }
                    if (Cross(points[hull[^2]], points[hull[^1]], points[index]).Sign > 0) break;
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(index); return true;
            }
            foreach (int i in ordered) if (!Push(lower, i)) return false;
            for (int i = ordered.Length - 1; i >= 0; i--) if (!Push(upper, ordered[i])) return false;
            lower.RemoveAt(lower.Count - 1); upper.RemoveAt(upper.Count - 1); lower.AddRange(upper);
            if (lower.Count < 3) { failure = "Cep dış sınırı belirsiz."; return false; }
            var hull = lower;
            var hullSet = new HashSet<int>(hull);
            // A point lying on a hull edge is an ambiguous tangent or shared
            // boundary: do not invent a pocket boundary for it.
            foreach (int index in polygon)
            {
                if (hullSet.Contains(index)) continue;
                for (int h = 0; h < hull.Count; h++)
                {
                    if (!Spend()) { failure = "Cep ayrıştırma iş sınırı aşıldı."; return false; }
                    var a = points[hull[h]]; var b = points[hull[(h + 1) % hull.Count]];
                    if (Cross(a, b, points[index]).IsZero && OnSegment(a, b, points[index]))
                    { failure = "Cep sınırında doğrusal temas belirsiz."; return false; }
                }
            }
            var positions = new Dictionary<int, int>(polygon.Count);
            for (int i = 0; i < polygon.Count; i++) positions.Add(polygon[i], i);
            var children = new List<List<int>>();
            for (int h = 0; h < hull.Count; h++)
            {
                if (!Spend()) { failure = "Cep ayrıştırma iş sınırı aşıldı."; return false; }
                int start = positions[hull[h]], end = positions[hull[(h + 1) % hull.Count]];
                int distance = (end - start + polygon.Count) % polygon.Count;
                if (distance == 0) { failure = "Cep sınır sırası geçersiz."; return false; }
                if (distance == 1) continue;
                var child = new List<int>(distance + 1);
                for (int step = 0; step <= distance; step++)
                {
                    if (!Spend()) { failure = "Cep ayrıştırma iş sınırı aşıldı."; return false; }
                    child.Add(polygon[(start + step) % polygon.Count]);
                }
                if (child.Count >= polygon.Count || Area(child).IsZero)
                { failure = "İç içe cep sınırı belirsiz."; return false; }
                children.Add(child);
            }
            if (children.Count == 0 && !isRoot)
            {
                var concave = polygon.Where(originalConcave.Contains).ToImmutableArray();
                leaves.Add(new(polygon.ToImmutableArray(), depth, concave));
            }
            foreach (var child in children) if (!Walk(child, depth + 1, false)) return false;
            return true;
        }
        if (!Walk(ring, 0, true)) { reason = failure; return false; }
        var covered = new HashSet<int>();
        foreach (var leaf in leaves) foreach (var index in leaf.OriginalConcaveVertices) covered.Add(index);
        if (!originalConcave.IsSubsetOf(covered))
        { reason = "Her içbükey köşe en az bir yaprak cepte bulunamadı."; return false; }
        pockets = leaves.ToImmutable();
        reason = "Deneysel kaynak cep tespiti; ölçü veya üretim uygunluğu kanıtı değildir.";
        return true;

        BigInteger Area(IReadOnlyList<int> indices)
        {
            BigInteger sum = 0;
            for (int i = 0; i < indices.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var a = points[indices[i]]; var b = points[indices[(i + 1) % indices.Count]];
                sum += (BigInteger)a.X * b.Y - (BigInteger)a.Y * b.X;
            }
            return sum;
        }
    }

    private static BigInteger Cross(GridPoint a, GridPoint b, GridPoint c) =>
        ((BigInteger)b.X - a.X) * ((BigInteger)c.Y - a.Y) -
        ((BigInteger)b.Y - a.Y) * ((BigInteger)c.X - a.X);

    private static bool OnSegment(GridPoint a, GridPoint b, GridPoint p) =>
        p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) &&
        p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y);

    private static bool Intersects(GridPoint a, GridPoint b, GridPoint c, GridPoint d)
    {
        var abC = Cross(a, b, c); var abD = Cross(a, b, d);
        var cdA = Cross(c, d, a); var cdB = Cross(c, d, b);
        if (abC.IsZero && OnSegment(a, b, c) || abD.IsZero && OnSegment(a, b, d) ||
            cdA.IsZero && OnSegment(c, d, a) || cdB.IsZero && OnSegment(c, d, b)) return true;
        return abC.Sign * abD.Sign < 0 && cdA.Sign * cdB.Sign < 0;
    }
}
