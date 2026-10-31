using System.Security.Cryptography;
using System.Text;
using TekstilCAD.Core;

namespace TekstilCAD.Nesting;

public sealed record ProductionPlacement(Guid SourcePieceId, int CopyNumber, Guid PlacedPieceId);
public sealed record ProductionNestingResult(NestingResult Nesting, IReadOnlyList<ProductionPlacement> Placements);

/// <summary>Explicit quantity expansion for selected cut pieces. Shelf placement preserves orientation;
/// material grouping, grain, fabric defects and machine readiness are not enforced.</summary>
public static class ProductionNestingPlan
{
    internal static Guid CopyId(Guid sourceId, int copyNumber)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"TekstilCAD.production-copy.v1:{sourceId:D}:{copyNumber}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    public static ProductionNestingResult Arrange(ProjectDocument source, IReadOnlyCollection<Guid> selectedIds,
        double fabricWidth, double gap = 5, double maxLength = double.PositiveInfinity, double edgeMargin = 0)
        => Arrange(source, selectedIds, fabricWidth, gap, maxLength, edgeMargin, CancellationToken.None);

    /// <summary>Expand and arrange without modifying the source, observing cancellation throughout.</summary>
    public static ProductionNestingResult Arrange(ProjectDocument source, IReadOnlyCollection<Guid> selectedIds,
        double fabricWidth, double gap, double maxLength, double edgeMargin, CancellationToken cancellationToken)
    {
        var expansion = Expand(source, selectedIds, cancellationToken);
        var expanded = expansion.Pieces;
        var mapping = expansion.Placements;
        NestingResult result;
        try { result=NestingEngine.Arrange(expanded,fabricWidth,gap,maxLength,edgeMargin,cancellationToken); }
        catch(ArgumentException error) when(NestingPieceError.TryGet(error,out _,out _))
        {
            NestingPieceError.TryGet(error,out var placedId,out var reason);
            var placement=mapping.FirstOrDefault(item=>item.PlacedPieceId==placedId);
            if(placement is null)throw;
            throw NestingPieceError.Create(placement.SourcePieceId,reason,error.ParamName,error);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(result, mapping);
    }

    internal sealed record Expansion(IReadOnlyList<PatternPiece> Pieces, IReadOnlyList<ProductionPlacement> Placements);

    // Rebuild the selected source snapshot from detached expansion copies, not
    // from a source document that the caller might have edited meanwhile.
    internal static string FingerprintSources(Expansion expansion, CancellationToken cancellationToken)
    {
        var sources = new List<PatternPiece>();
        var byId = new Dictionary<Guid, PatternPiece>();
        for (int i = 0; i < expansion.Placements.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mapping = expansion.Placements[i];
            if (!byId.TryGetValue(mapping.SourcePieceId, out var source))
            {
                source = expansion.Pieces[i].Clone();
                source.Id = mapping.SourcePieceId;
                source.Metadata!.Quantity = 0;
                byId.Add(source.Id, source);
                sources.Add(source);
            }
            source.Metadata!.Quantity++;
        }
        return NestingSnapshot.ComputeLayoutFingerprint(new ProjectDocument { Pieces = sources }, cancellationToken);
    }

    internal static Expansion Expand(ProjectDocument source, IReadOnlyCollection<Guid> selectedIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selectedIds);
        int declaredCount = selectedIds.Count;
        if (source.Pieces is null || declaredCount is < 1 or > NestingEngine.MaximumPieces) throw new ArgumentException("Yerleştirilecek kesim parçalarını seçin.");
        var selection = new HashSet<Guid>();
        int visited = 0;
        foreach(var id in selectedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++visited > declaredCount || id == Guid.Empty || !selection.Add(id))
                throw new ArgumentException("Parça seçimi yinelenmiş veya geçersiz.");
        }
        if (visited != declaredCount) throw new ArgumentException("Parça seçimi yinelenmiş veya geçersiz.");
        var matches = new List<PatternPiece>();
        foreach(var piece in source.Pieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(piece is not null && selection.Contains(piece.Id))matches.Add(piece);
        }
        if (matches.Count != selection.Count || matches.Select(p => p.Id).Distinct().Count() != selection.Count)
            throw new ArgumentException("Seçili parça bulunamadı veya kimliği yinelenmiş.");
        long copies = 0, vertices = 0, validation = 0;
        string? material = null;
        foreach (var p in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (p.Metadata?.Role != "Kesim" || p.Metadata.Quantity is not int n || n < 1 || n > 100000)
                throw NestingPieceError.Create(p.Id,"Parça için kesim rolü ve pozitif adet belirtilmelidir.");
            if (string.IsNullOrWhiteSpace(p.Metadata.Material)) throw NestingPieceError.Create(p.Id,"Parça için malzeme belirtilmelidir.");
            static bool ValidText(string? value) => value is null || (value.Length is > 0 and <= 128 && value == value.Trim() && !value.Any(char.IsControl));
            if (!ValidText(p.Metadata.Material) || !ValidText(p.Metadata.Size)) throw NestingPieceError.Create(p.Id,"Parçanın malzeme veya beden bilgisi geçersiz.");
            material ??= p.Metadata.Material;
            if (!string.Equals(material, p.Metadata.Material, StringComparison.Ordinal)) throw NestingPieceError.Create(p.Id,"Parçanın malzemesi diğer seçili parçalardan farklı; farklı malzemeler aynı pastalda yerleştirilemez.");
            if (p.Points is null || p.Points.Count > NestingEngine.MaximumVerticesPerPiece) throw NestingPieceError.Create(p.Id,"Parça noktaları eksik veya sınırı aşıyor.");
            // Each placed copy is checked; identical source snapshots share validation.
            copies += n; vertices += (long)n * p.Points.Count; validation += (n + 1L) * p.Points.Count * p.Points.Count;
            if (copies > NestingEngine.MaximumPieces || vertices > NestingEngine.MaximumTotalVertices || validation > NestingEngine.MaximumValidationWork)
                throw new ArgumentException("Adetler pastal kaynak sınırlarını aşıyor.");
        }
        var expanded = new List<PatternPiece>((int)copies);
        var mapping = new List<ProductionPlacement>((int)copies);
        var ids = new HashSet<Guid>();
        foreach(var piece in source.Pieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(piece is not null)ids.Add(piece.Id);
        }
        foreach (var p in matches)
        for (int i = 1; i <= p.Metadata!.Quantity!.Value; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = CopyId(p.Id, i);
            if (id == Guid.Empty || !ids.Add(id)) throw new InvalidOperationException("Yerleşim kimliği çakıştı.");
            var copy = p.Clone(); copy.Id = id; copy.Metadata!.Quantity = 1;
            expanded.Add(copy); mapping.Add(new(p.Id, i, id));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(expanded.AsReadOnly(), mapping.AsReadOnly());
    }

}
