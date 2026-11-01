using TekstilCAD.Core;

namespace TekstilCAD.Patterns;

/// <summary>A detached result. Source IDs and names are retained for the caller's review;
/// the caller remains responsible for project references, grading rules and provenance.</summary>
public sealed record OpenPathJoinResult(PatternPiece Piece, Guid FirstSourceId, Guid SecondSourceId,
    string FirstSourceName, string SecondSourceName, double BridgeLength);

public static class OpenPathJoin
{
    /// <summary>
    /// Joins two open paths only when exactly one pair of endpoints is within the given
    /// millimetre tolerance. Noncoincident endpoints remain unchanged and a straight
    /// bridge is inserted; no snapping, trimming or closing is performed. Inputs must
    /// remain unchanged while this method snapshots them. Metadata and grainlines require explicit omitSourceProductionData consent because
    /// their meanings cannot be combined automatically.
    /// The returned piece has a new identity and no project-level references.
    /// </summary>
    public static OpenPathJoinResult Join(PatternPiece first, PatternPiece second, double tolerance,
        CancellationToken cancellationToken = default, bool omitSourceProductionData = false)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        cancellationToken.ThrowIfCancellationRequested();
        NotchSafety.RequireNoNotches(first);NotchSafety.RequireNoNotches(second);
        if (!double.IsFinite(tolerance) || tolerance < 0 || tolerance > 100_000)
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        if (first.Id == Guid.Empty || second.Id == Guid.Empty || first.Id == second.Id)
            throw new ArgumentException("Kaynak parça kimlikleri dolu ve farklı olmalıdır.");
        if (first.Name is null || second.Name is null || first.Name.Length > 1024 || second.Name.Length > 1024)
            throw new ArgumentException("Kaynak parça adı geçersiz.");
        if (first.Closed || second.Closed || first.Points is null || second.Points is null)
            throw new ArgumentException("İki açık kontur gereklidir.");
        if (!omitSourceProductionData && (first.Metadata is not null || second.Metadata is not null || first.Grainline is not null || second.Grainline is not null))
            throw new ArgumentException("Üretim bilgisi veya iplik yönü olan parçalar otomatik birleştirilemez.");
        if (first.Points.Count < 2 || second.Points.Count < 2 ||
            first.Points.Count > LargeOpenPathValidator.MaximumStoredPoints || second.Points.Count > LargeOpenPathValidator.MaximumStoredPoints)
            throw new ArgumentException("Kaynak kontur nokta sayısı geçersiz.");
        if ((long)first.Points.Count + second.Points.Count - 1 > LargeOpenPathValidator.MaximumStoredPoints)
            throw new ArgumentException("Birleşik kontur nokta sınırını aşıyor.");
        var a = first.Points.ToArray(); var b = second.Points.ToArray();
        LargeOpenPathValidator.Validate(a, cancellationToken);
        LargeOpenPathValidator.Validate(b, cancellationToken);
        var candidates = new List<(bool ReverseA, bool ReverseB, double Distance)>();
        foreach (var reverseA in new[] { false, true })
        foreach (var reverseB in new[] { false, true })
        {
            var endA = reverseA ? a[0] : a[^1];
            var startB = reverseB ? b[^1] : b[0];
            var distance = Geometry.Distance(endA, startB);
            if (distance <= tolerance) candidates.Add((reverseA, reverseB, distance));
        }
        if (candidates.Count != 1)
            throw new ArgumentException(candidates.Count == 0 ? "Tolerans içinde uç bulunamadı." : "Birden fazla uç eşleşiyor; seçim belirsiz.");
        var match = candidates[0];
        if (match.ReverseA) Array.Reverse(a);
        if (match.ReverseB) Array.Reverse(b);
        var shared = a[^1] == b[0];
        var resultCount = (long)a.Length + b.Length - (shared ? 1 : 0);
        if (resultCount > LargeOpenPathValidator.MaximumStoredPoints)
            throw new ArgumentException("Birleşik kontur nokta sınırını aşıyor.");
        var points = new List<Point2>((int)resultCount);
        points.AddRange(a);
        points.AddRange(shared ? b.Skip(1) : b);
        LargeOpenPathValidator.Validate(points, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var piece = new PatternPiece { Name = OpenPathResultName.Joined(first, second), Closed = false, Points = points };
        return new(piece, first.Id, second.Id, first.Name, second.Name, match.Distance);
    }
}
