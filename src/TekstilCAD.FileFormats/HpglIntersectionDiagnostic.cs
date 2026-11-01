namespace TekstilCAD.FileFormats;

/// <summary>
/// Intersection check in already-quantized plotter units used by HPGL export.
/// It does not by itself validate source topology, quantization loss, area or
/// physical-machine compatibility. Pair iteration remains quadratic.
/// </summary>
public static class HpglIntersectionDiagnostic
{
    public readonly record struct Point(int X, int Y);
    public sealed record Path(IReadOnlyList<Point> Points, bool Closed);
    public sealed record Statistics(long PairCount, long BroadphaseRejected, long PredicateCalls);
    private const int MaximumPoints = 10_000;
    private const long MaximumPairs = 50_000_000;
    private const int MaximumPaths = 10_000;
    private const int MaximumJobPoints = 200_000;

    /// <summary>
    /// Applies one nonzero-edge pair budget across all paths in a job. As with the
    /// current exporter, intersections between distinct paths are not checked.
    /// All input must remain stable during snapshot creation. This method does not
    /// validate quantization loss, area, or physical-machine compatibility.
    /// </summary>
    public static Statistics ValidateJob(IReadOnlyList<Path> paths, long maxPairs = MaximumPairs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        cancellationToken.ThrowIfCancellationRequested();
        if (maxPairs < 0 || maxPairs > MaximumPairs) throw new ArgumentOutOfRangeException(nameof(maxPairs));
        if (paths.Count is < 1 or > MaximumPaths) throw new InvalidDataException("HPGL tanısı iş parça sınırını aşıyor.");
        var snapshots = new List<(Point[] Points, bool Closed)>(paths.Count);
        long totalPairs = 0; int totalPoints = 0;
        for (var i = 0; i < paths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = paths[i] ?? throw new InvalidDataException("HPGL tanısı için parça gerekli.");
            var input = path.Points ?? throw new InvalidDataException("HPGL tanısı için noktalar gerekli.");
            if (input.Count < 2 || input.Count > MaximumPoints || input.Count > MaximumJobPoints - totalPoints)
                throw new InvalidDataException("HPGL tanısı iş nokta sınırını aşıyor.");
            var snapshot = new Point[input.Count];
            for (var j = 0; j < snapshot.Length; j++)
            {
                if ((j & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                snapshot[j] = input[j];
            }
            totalPoints += snapshot.Length;
            var count = path.Closed ? snapshot.Length : snapshot.Length - 1;
            long edges = 0;
            for (var j = 0; j < count; j++)
                if (snapshot[j] != snapshot[(j + 1) % snapshot.Length]) edges++;
            totalPairs += edges * (edges - 1) / 2;
            if (totalPairs > maxPairs) throw new InvalidDataException("HPGL tanısı toplam kenar çifti iş sınırını aşıyor.");
            snapshots.Add((snapshot, path.Closed));
        }
        long pairs = 0, rejected = 0, calls = 0;
        foreach (var snapshot in snapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = Validate(snapshot.Points, snapshot.Closed, maxPairs - pairs, cancellationToken);
            pairs += result.PairCount; rejected += result.BroadphaseRejected; calls += result.PredicateCalls;
        }
        return new Statistics(pairs, rejected, calls);
    }

    /// <summary>
    /// Checks the same nonzero edges and adjacency rules as the HPGL exporter.
    /// Distinct source points that quantize together must be rejected separately.
    /// Caller must keep input stable until the snapshot is copied. No file or device I/O.
    /// </summary>
    public static Statistics Validate(IReadOnlyList<Point> points, bool closed,
        long maxPairs = MaximumPairs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        cancellationToken.ThrowIfCancellationRequested();
        if (points.Count < 2 || points.Count > MaximumPoints)
            throw new ArgumentException("HPGL tanısı 2–10000 nokta gerektirir.", nameof(points));
        if (maxPairs < 0 || maxPairs > MaximumPairs)
            throw new ArgumentOutOfRangeException(nameof(maxPairs));

        var snapshot = new Point[points.Count];
        for (var i = 0; i < snapshot.Length; i++)
        {
            if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            snapshot[i] = points[i];
        }
        var edges = new List<(Point A, Point B)>();
        var edgeCount = closed ? snapshot.Length : snapshot.Length - 1;
        for (var i = 0; i < edgeCount; i++)
        {
            var a = snapshot[i]; var b = snapshot[(i + 1) % snapshot.Length];
            if (a != b) edges.Add((a, b));
        }
        long totalPairs = (long)edges.Count * (edges.Count - 1) / 2;
        if (totalPairs > maxPairs)
            throw new InvalidDataException("HPGL tanısı kenar çifti iş sınırını aşıyor.");

        long pairs = 0, rejected = 0, predicateCalls = 0;
        for (var i = 0; i < edges.Count; i++)
        for (var j = i + 1; j < edges.Count; j++)
        {
            if ((pairs++ & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            var (a, b) = edges[i]; var (c, d) = edges[j];
            if (j == i + 1 || (closed && i == 0 && j == edges.Count - 1))
            {
                var shared = j == i + 1 ? b : a;
                var first = j == i + 1 ? a : b;
                var second = j == i + 1 ? d : c;
                var dot = ((Int128)first.X - shared.X) * ((Int128)second.X - shared.X)
                    + ((Int128)first.Y - shared.Y) * ((Int128)second.Y - shared.Y);
                if (Cross(first, shared, second) == 0 && dot > 0)
                    throw new InvalidDataException("HPGL konturu aynı kenarda geri dönüyor.");
                continue;
            }
            // Integer AABBs are exact; strict separation cannot hide a touch.
            if (Separated(a, b, c, d)) { rejected++; continue; }
            predicateCalls++;
            var abC = Cross(a, b, c); var abD = Cross(a, b, d);
            var cdA = Cross(c, d, a); var cdB = Cross(c, d, b);
            if ((Opposite(abC, abD) && Opposite(cdA, cdB)) || OnSegment(a, b, c)
                || OnSegment(a, b, d) || OnSegment(c, d, a) || OnSegment(c, d, b))
                throw new InvalidDataException("HPGL konturunda kesişen kenarlar var.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new Statistics(pairs, rejected, predicateCalls);
    }

    private static bool Separated(Point a, Point b, Point c, Point d) =>
        Math.Max(a.X, b.X) < Math.Min(c.X, d.X) || Math.Max(c.X, d.X) < Math.Min(a.X, b.X)
        || Math.Max(a.Y, b.Y) < Math.Min(c.Y, d.Y) || Math.Max(c.Y, d.Y) < Math.Min(a.Y, b.Y);
    private static Int128 Cross(Point a, Point b, Point c) =>
        ((Int128)b.X - a.X) * ((Int128)c.Y - a.Y) - ((Int128)b.Y - a.Y) * ((Int128)c.X - a.X);
    private static bool OnSegment(Point a, Point b, Point p) => Cross(a, b, p) == 0
        && p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X)
        && p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y);
    private static bool Opposite(Int128 a, Int128 b) => (a < 0 && b > 0) || (a > 0 && b < 0);
}
