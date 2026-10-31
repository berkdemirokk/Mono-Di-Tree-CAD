using TekstilCAD.Core;

namespace TekstilCAD.Nesting;

/// <summary>Independently checks the historical source, copy and rotation mapping against
/// the recorded layout. This is not a production or machine-suitability certificate.</summary>
public static class OrthogonalTraceVerifier
{
    private static bool ValidText(string? value) => value is null ||
        (value.Length is > 0 and <= 128 && value == value.Trim() && !value.Any(char.IsControl));

    private static bool ValidMetadata(PieceMetadata? metadata) => metadata is not null &&
        metadata.Role == "Kesim" && !string.IsNullOrWhiteSpace(metadata.Material) &&
        ValidText(metadata.Material) && ValidText(metadata.Size);

    /// <summary>Rechecks a trace against supplied per-source angle permissions, grain
    /// policy and fabric settings. This verifies bounding boxes, not machine readiness.</summary>
    public static bool IsValidForConstraints(ProjectDocument source, IReadOnlyCollection<Guid> selectedIds,
        OrthogonalNestingResult result, IReadOnlyDictionary<Guid, IReadOnlyList<int>> permittedRotations,
        GrainDirectionPolicy.Direction direction, double toleranceDegrees, double fabricWidth,
        double gap, double edgeMargin, double maxLength, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (permittedRotations is null || selectedIds is null || result?.Plan?.Nesting is null ||
            permittedRotations.Count != selectedIds.Count || !double.IsFinite(fabricWidth) || fabricWidth <= 0 ||
            fabricWidth > NestingEngine.MaximumDimension || !double.IsFinite(gap) || gap < 0 ||
            gap > NestingEngine.MaximumDimension || !double.IsFinite(edgeMargin) || edgeMargin < 0 ||
            edgeMargin * 2 >= fabricWidth ||
            double.IsNaN(maxLength) || maxLength <= 0 || double.IsNegativeInfinity(maxLength)) return false;
        var nesting = result.Plan.Nesting;
        if (nesting.FabricWidth != fabricWidth || nesting.Gap != gap || nesting.EdgeMargin != edgeMargin ||
            nesting.UsedLength > Math.Min(maxLength, NestingEngine.MaximumDimension)) return false;
        int selectedCount = selectedIds.Count;
        if (selectedCount is < 1 or > NestingEngine.MaximumPieces) return false;
        var ids = new List<Guid>(selectedCount);
        foreach (var id in selectedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ids.Count >= selectedCount) return false;
            ids.Add(id);
        }
        if (ids.Count != selectedCount) return false;
        try
        {
            GrainDirectionPolicy.ValidateParameters(direction, toleranceDegrees);
            if (!IsValid(source, ids, result, cancellationToken)) return false;
            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!permittedRotations.TryGetValue(id, out var angles) || angles is null)
                    return false;
                int angleCount = angles.Count;
                if (angleCount is < 1 or > 4) return false;
                var seen = new HashSet<int>();
                bool chosenIsAllowed = false;
                for (int i = 0; i < angleCount; i++)
                {
                    var angle = angles[i];
                    if (angle is not (0 or 90 or 180 or 270) || !seen.Add(angle)) return false;
                    if (angle == result.SourceRotations[id]) chosenIsAllowed = true;
                }
                if (!chosenIsAllowed) return false;
            }
            foreach (var piece in source.Pieces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (piece is null || !result.SourceRotations.TryGetValue(piece.Id, out var angle)) continue;
                if (piece.Grainline is not { } line) return false;
                static Point2 Turn(Point2 p, int degrees) => degrees switch
                {
                    0 => p, 90 => new(-p.Y, p.X), 180 => new(-p.X, -p.Y), 270 => new(p.Y, -p.X),
                    _ => throw new ArgumentOutOfRangeException(nameof(degrees))
                };
                var rotatedLine = new PatternPiece { Id = piece.Id,
                    Grainline = new Grainline { Start = Turn(line.Start, angle), End = Turn(line.End, angle), NeedsReview = line.NeedsReview } };
                GrainDirectionPolicy.Validate(rotatedLine, direction, toleranceDegrees);
            }
            foreach (var placed in nesting.Layout.Pieces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GrainDirectionPolicy.Validate(placed, direction, toleranceDegrees);
            }
            ShelfLayoutVerifier.Verify(nesting, cancellationToken);
            return true;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    public static bool IsValid(ProjectDocument source, IReadOnlyCollection<Guid> selectedIds,
        OrthogonalNestingResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source?.Pieces is null || selectedIds is null || result?.Plan?.Nesting?.Layout?.Pieces is null ||
            result.Plan.Placements is null || result.SourceRotations is null ||
            source.Pieces.Count > NestingEngine.MaximumTotalVertices || selectedIds.Count is < 1 or > NestingEngine.MaximumPieces)
            return false;
        var selected = new HashSet<Guid>();
        int declaredCount = selectedIds.Count;
        if (declaredCount is < 1 or > NestingEngine.MaximumPieces) return false;
        int visited = 0;
        foreach (var id in selectedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++visited > declaredCount || id == Guid.Empty || !selected.Add(id)) return false;
        }
        if (visited != declaredCount) return false;
        if (result.SourceRotations.Count != selected.Count) return false;
        var sources = new Dictionary<Guid, PatternPiece>();
        var sourceIds = new HashSet<Guid>();
        foreach (var piece in source.Pieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (piece is null) continue;
            sourceIds.Add(piece.Id);
            if (selected.Contains(piece.Id) && !sources.TryAdd(piece.Id, piece)) return false;
        }
        if (sources.Count != selected.Count) return false;
        long copies = 0, vertices = 0, work = 0;
        string? material = null;
        foreach (var (id, piece) in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!result.SourceRotations.TryGetValue(id, out int angle) || angle is not (0 or 90 or 180 or 270) ||
                !ValidMetadata(piece.Metadata) || piece.Name is null || piece.Name.Length > 1024 ||
                piece.Metadata!.Quantity is not int quantity || quantity < 1 || quantity > 100000 || piece.Points is null ||
                piece.Points.Count is < 3 or > NestingEngine.MaximumVerticesPerPiece) return false;
            material ??= piece.Metadata.Material;
            if (!string.Equals(material, piece.Metadata.Material, StringComparison.Ordinal)) return false;
            int count = piece.Points.Count;
            copies += quantity;
            vertices += (long)quantity * count;
            work += (2L + 2L * quantity) * count * count;
            if (copies > NestingEngine.MaximumPieces || vertices > NestingEngine.MaximumTotalVertices || work > NestingEngine.MaximumValidationWork) return false;
        }
        var layout = result.Plan.Nesting.Layout.Pieces;
        if (layout.Count != copies || result.Plan.Placements.Count != copies) return false;
        var placed = new Dictionary<Guid, PatternPiece>();
        foreach (var piece in layout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (piece is null || piece.Id == Guid.Empty || sourceIds.Contains(piece.Id) || piece.Points is null ||
                piece.Name is null || piece.Name.Length > 1024 || !ValidMetadata(piece.Metadata) ||
                piece.Points.Count > NestingEngine.MaximumVerticesPerPiece || !placed.TryAdd(piece.Id, piece)) return false;
        }
        try
        {
            if (!result.IsLayoutUnchanged(cancellationToken)) return false;
            // The relative placement guard permits small rounding changes. Keep a
            // separate historical fingerprint so even a one-bit source edit is
            // not mistaken for the source used to calculate this layout.
            if (string.IsNullOrEmpty(result.SourceFingerprint) || !string.Equals(result.SourceFingerprint,
                NestingSnapshot.ComputeLayoutFingerprint(new ProjectDocument { Pieces = sources.Values.ToList() }, cancellationToken),
                StringComparison.Ordinal)) return false;
            var rotated = new Dictionary<Guid, PatternPiece>();
            foreach (var (id, piece) in sources)
                rotated.Add(id, OrthogonalRotation.Rotate(piece, result.SourceRotations[id], cancellationToken));
            var seenCopies = new HashSet<(Guid, int)>();
            foreach (var placement in result.Plan.Placements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (placement is null || !rotated.TryGetValue(placement.SourcePieceId, out var original) ||
                    placement.CopyNumber < 1 || placement.CopyNumber > original.Metadata!.Quantity ||
                    placement.PlacedPieceId != ProductionNestingPlan.CopyId(placement.SourcePieceId, placement.CopyNumber) ||
                    !seenCopies.Add((placement.SourcePieceId, placement.CopyNumber)) ||
                    !placed.Remove(placement.PlacedPieceId, out var copy) || !copy.Closed ||
                    copy.Name != original.Name || copy.Metadata is null || original.Metadata is null ||
                    copy.Metadata.Quantity != 1 || copy.Metadata.Role != original.Metadata.Role ||
                    copy.Metadata.Size != original.Metadata.Size || copy.Metadata.Material != original.Metadata.Material)
                    return false;
                NestingEngine.ValidateTranslation(original, copy.Points, cancellationToken);
                if (!NestingEngine.GrainlinesEqualAfterTranslation(original, copy)) return false;
            }
            return placed.Count == 0 && seenCopies.Count == copies;
        }
        catch (ArgumentException) { return false; }
    }
}
