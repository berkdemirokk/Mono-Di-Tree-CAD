using System.Collections.ObjectModel;
using TekstilCAD.Core;

namespace TekstilCAD.Nesting;

/// <summary>Historical layout and selected angle for each placed copy. This is not a production certificate.</summary>
public sealed record PerCopyNestingResult(ProductionNestingResult Plan, IReadOnlyDictionary<Guid, int> PlacedRotations)
{
    public string SourceFingerprint { get; init; } = "";
    public string LayoutFingerprint { get; init; } = "";
}

/// <summary>Places explicitly specified orthogonal orientations per copy. This is a deterministic
/// shelf placement, not an automatic search over all orientation combinations.</summary>
public static class PerCopyOrthogonalNesting
{
    public static PerCopyNestingResult Arrange(ProjectDocument source, IReadOnlyCollection<Guid> selectedIds,
        IReadOnlyDictionary<(Guid SourcePieceId, int CopyNumber), int> copyRotations,
        double fabricWidth, double gap, double maxLength, double edgeMargin,
        GrainDirectionPolicy.Direction direction, double toleranceDegrees, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selectedIds);
        ArgumentNullException.ThrowIfNull(copyRotations);
        GrainDirectionPolicy.ValidateParameters(direction, toleranceDegrees);
        if (!double.IsFinite(fabricWidth) || fabricWidth <= 0 || fabricWidth > NestingEngine.MaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(fabricWidth));
        if (!double.IsFinite(gap) || gap < 0 || gap > NestingEngine.MaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(gap));
        if (!double.IsFinite(edgeMargin) || edgeMargin < 0 || edgeMargin * 2 >= fabricWidth)
            throw new ArgumentOutOfRangeException(nameof(edgeMargin));
        if (double.IsNaN(maxLength) || maxLength <= 0 || double.IsNegativeInfinity(maxLength))
            throw new ArgumentOutOfRangeException(nameof(maxLength));
        if (source.Pieces is not null && source.Pieces.Count > NestingEngine.MaximumTotalVertices)
            throw new ArgumentException("Kaynak proje parça sayısı sınırını aşıyor.", nameof(source));

        var expansion = ProductionNestingPlan.Expand(source, selectedIds, cancellationToken);
        if (copyRotations.Count != expansion.Placements.Count)
            throw new ArgumentException("Her kopya için tam bir açı gereklidir.", nameof(copyRotations));
        // Capture all angles and budget all validation before validating contours.
        var angles = new int[expansion.Placements.Count];
        for (int i = 0; i < expansion.Placements.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mapping = expansion.Placements[i];
            if (!copyRotations.TryGetValue((mapping.SourcePieceId, mapping.CopyNumber), out int angle) ||
                angle is not (0 or 90 or 180 or 270))
                throw NestingPieceError.Create(mapping.SourcePieceId, "Kopyanın dönüş açısı eksik veya geçersiz.", nameof(copyRotations));
            angles[i] = angle;
        }
        var unique = new HashSet<(Guid SourcePieceId, int Angle)>();
        long work = 0;
        for (int i = 0; i < expansion.Placements.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mapping = expansion.Placements[i];
            int angle = angles[i];
            long vertices = expansion.Pieces[i].Points.Count;
            work += 2L * vertices * vertices;
            if (work > NestingEngine.MaximumValidationWork)
                throw new ArgumentException("Pastal doğrulama kaynak sınırı aşıldı.", nameof(source));
            if (unique.Add((mapping.SourcePieceId, angle)))
            {
                work += 2L * vertices * vertices;
                if (work > NestingEngine.MaximumValidationWork)
                    throw new ArgumentException("Pastal doğrulama kaynak sınırı aşıldı.", nameof(source));
            }
        }

        var rotated = new List<PatternPiece>(expansion.Pieces.Count);
        var rotations = new Dictionary<Guid, int>(expansion.Pieces.Count);
        var sourceByCopy = new Dictionary<Guid, Guid>(expansion.Pieces.Count);
        var cache = new Dictionary<(Guid SourcePieceId, int Angle), PatternPiece>();
        for (int i = 0; i < expansion.Pieces.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mapping = expansion.Placements[i];
            int angle = angles[i];
            var key = (mapping.SourcePieceId, angle);
            if (!cache.TryGetValue(key, out var template))
            {
                try
                {
                    template = OrthogonalRotation.Rotate(expansion.Pieces[i], angle, cancellationToken);
                    GrainDirectionPolicy.Validate(template, direction, toleranceDegrees);
                }
                catch (ArgumentException error)
                { throw NestingPieceError.Create(mapping.SourcePieceId, "Kopyanın geometrisi veya iplik yönü geçersiz.", nameof(source), error); }
                cache.Add(key, template);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var piece = template.Clone();
            piece.Id = mapping.PlacedPieceId;
            piece.Metadata!.Quantity = 1;
            rotated.Add(piece);
            rotations.Add(piece.Id, angle);
            sourceByCopy.Add(piece.Id, mapping.SourcePieceId);
        }
        NestingResult layout;
        try { layout = NestingEngine.Arrange(rotated, fabricWidth, gap, maxLength, edgeMargin, cancellationToken); }
        catch (ArgumentException error) when (NestingPieceError.TryGet(error, out _, out _))
        {
            NestingPieceError.TryGet(error, out var copyId, out var reason);
            if (!sourceByCopy.TryGetValue(copyId, out var sourceId)) throw;
            throw NestingPieceError.Create(sourceId, reason, error.ParamName, error);
        }
        ShelfLayoutVerifier.Verify(layout, cancellationToken);
        foreach (var placed in layout.Layout.Pieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { GrainDirectionPolicy.Validate(placed, direction, toleranceDegrees); }
            catch (ArgumentException error) when (NestingPieceError.TryGet(error, out _, out _))
            {
                NestingPieceError.TryGet(error, out var copyId, out var reason);
                if (!sourceByCopy.TryGetValue(copyId, out var sourceId)) throw;
                throw NestingPieceError.Create(sourceId, reason, error.ParamName, error);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var sourceFingerprint = ProductionNestingPlan.FingerprintSources(expansion, cancellationToken);
        var layoutFingerprint = NestingSnapshot.ComputeLayoutFingerprint(layout.Layout, cancellationToken);
        return new(new ProductionNestingResult(layout, expansion.Placements),
            new ReadOnlyDictionary<Guid, int>(rotations))
            { SourceFingerprint = sourceFingerprint, LayoutFingerprint = layoutFingerprint };
    }
}
