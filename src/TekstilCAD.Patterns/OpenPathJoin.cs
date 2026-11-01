using TekstilCAD.Core;

namespace TekstilCAD.Patterns;

/// <summary>A detached result. Source IDs and names are retained for the caller's review;
/// the caller remains responsible for project references, grading rules and provenance.</summary>
public sealed record OpenPathJoinResult(PatternPiece Piece, Guid FirstSourceId, Guid SecondSourceId,
    string FirstSourceName, string SecondSourceName, double BridgeLength)
{
    /// <summary>Zero-based endpoint index in the original first source snapshot.</summary>
    public int FirstEndpointIndex { get; init; } = -1;
    /// <summary>Zero-based endpoint index in the original second source snapshot.</summary>
    public int SecondEndpointIndex { get; init; } = -1;
}

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
        if(first.Notches is not null)_=MarkedPieceTranslation.Translate(first,0,0,cancellationToken);
        if(second.Notches is not null)_=MarkedPieceTranslation.Translate(second,0,0,cancellationToken);
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
            if (EndpointTolerance.Contains(endA, startB, tolerance)) candidates.Add((reverseA, reverseB, distance));
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
        var marks=new List<NotchMark>();var used=new HashSet<int>();
        static bool SameBits(Point2 x,Point2 y) => BitConverter.DoubleToInt64Bits(x.X)==BitConverter.DoubleToInt64Bits(y.X) && BitConverter.DoubleToInt64Bits(x.Y)==BitConverter.DoubleToInt64Bits(y.Y);
        void AddMarks(PatternPiece source,bool reversed,int offset)
        {
            if(source.Notches is null)return;
            foreach(var mark in source.Notches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mapped=offset+(reversed ? source.Points.Count-1-mark.VertexIndex : mark.VertexIndex);
                if(mapped<0 || mapped>=points.Count || !SameBits(mark.Position,points[mapped]))
                    throw new ArgumentException("Birleştirmede işaretin tam koordinatı korunamıyor; kaynaklar değiştirilmedi.");
                if(!used.Add(mapped))throw new ArgumentException("İki kaynak işareti aynı birleşim noktasına karşılık geliyor; ayrı işaretler korunamıyor.");
                marks.Add(new NotchMark { VertexIndex=mapped,Position=mark.Position });
            }
        }
        AddMarks(first,match.ReverseA,0);
        AddMarks(second,match.ReverseB,a.Length-(shared ? 1 : 0));
        var piece = new PatternPiece { Name = OpenPathResultName.Joined(first, second), Closed = false, Points = points, Notches=marks.Count==0 ? null : marks };
        return new(piece, first.Id, second.Id, first.Name, second.Name, match.Distance)
        {
            FirstEndpointIndex = match.ReverseA ? 0 : a.Length - 1,
            SecondEndpointIndex = match.ReverseB ? b.Length - 1 : 0
        };
    }
}
