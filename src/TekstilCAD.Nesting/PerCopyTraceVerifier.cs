using TekstilCAD.Core;

namespace TekstilCAD.Nesting;

/// <summary>Rechecks per-copy history and constraints; this is not a production or machine certificate.</summary>
public static class PerCopyTraceVerifier
{
    private static bool ValidText(string? value) => value is null ||
        (value.Length is > 0 and <= 128 && value == value.Trim() && !value.Any(char.IsControl));

    /// <summary>Checks the recorded per-copy angles against each selected source's permitted
    /// angles, then verifies the trace and fabric/grain constraints. Not a production certificate.</summary>
    public static bool IsValidForAllowedAngles(ProjectDocument source, IReadOnlyCollection<Guid> selectedIds,
        PerCopyNestingResult result, IReadOnlyDictionary<Guid, IReadOnlyList<int>> allowedAngles,
        GrainDirectionPolicy.Direction direction, double toleranceDegrees, double fabricWidth,
        double gap, double edgeMargin, double maxLength, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (selectedIds is null || allowedAngles is null || result?.Plan?.Placements is null ||
            result.PlacedRotations is null || selectedIds.Count is < 1 or > NestingEngine.MaximumPieces ||
            allowedAngles.Count != selectedIds.Count || result.Plan.Placements.Count > NestingEngine.MaximumPieces)
            return false;
        var allowed = new Dictionary<Guid, HashSet<int>>();
        int declaredCount = selectedIds.Count;
        if (declaredCount is < 1 or > NestingEngine.MaximumPieces || allowedAngles.Count != declaredCount) return false;
        int visited = 0;
        foreach (var id in selectedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++visited > declaredCount || id == Guid.Empty || allowed.ContainsKey(id) || !allowedAngles.TryGetValue(id, out var list) || list is null)
                return false;
            int count = list.Count;
            if (count is < 1 or > 4) return false;
            var values = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                int angle = list[i];
                if (angle is not (0 or 90 or 180 or 270) || !values.Add(angle)) return false;
            }
            allowed.Add(id, values);
        }
        if (visited != declaredCount) return false;
        var expected = new Dictionary<(Guid, int), int>();
        int placementCount = result.Plan.Placements.Count;
        if (placementCount > NestingEngine.MaximumPieces) return false;
        for (int i = 0; i < placementCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var placement = result.Plan.Placements[i];
            if (placement is null || !allowed.TryGetValue(placement.SourcePieceId, out var values) ||
                !result.PlacedRotations.TryGetValue(placement.PlacedPieceId, out int angle) || !values.Contains(angle) ||
                !expected.TryAdd((placement.SourcePieceId, placement.CopyNumber), angle)) return false;
        }
        return IsValidForConstraints(source, allowed.Keys.ToArray(), result, expected, direction, toleranceDegrees,
            fabricWidth, gap, edgeMargin, maxLength, cancellationToken);
    }

    public static bool IsValidForConstraints(ProjectDocument source, IReadOnlyCollection<Guid> selectedIds,
        PerCopyNestingResult result, IReadOnlyDictionary<(Guid SourcePieceId, int CopyNumber), int> expectedRotations,
        GrainDirectionPolicy.Direction direction, double toleranceDegrees, double fabricWidth,
        double gap, double edgeMargin, double maxLength, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source?.Pieces is null || selectedIds is null || expectedRotations is null ||
            result?.Plan?.Nesting?.Layout?.Pieces is null || result.Plan.Placements is null ||
            result.PlacedRotations is null || source.Pieces.Count > NestingEngine.MaximumTotalVertices ||
            selectedIds.Count is < 1 or > NestingEngine.MaximumPieces ||
            !double.IsFinite(fabricWidth) || fabricWidth <= 0 || fabricWidth > NestingEngine.MaximumDimension ||
            !double.IsFinite(gap) || gap < 0 || gap > NestingEngine.MaximumDimension ||
            !double.IsFinite(edgeMargin) || edgeMargin < 0 || edgeMargin * 2 >= fabricWidth ||
            double.IsNaN(maxLength) || maxLength <= 0 || double.IsNegativeInfinity(maxLength)) return false;
        var nesting = result.Plan.Nesting;
        if (nesting.FabricWidth != fabricWidth || nesting.Gap != gap || nesting.EdgeMargin != edgeMargin ||
            !double.IsFinite(nesting.UsedLength) || nesting.UsedLength < 0 ||
            nesting.UsedLength > Math.Min(maxLength, NestingEngine.MaximumDimension)) return false;
        try
        {
            GrainDirectionPolicy.ValidateParameters(direction, toleranceDegrees);
            var expansion = ProductionNestingPlan.Expand(source, selectedIds, cancellationToken);
            int count = expansion.Pieces.Count;
            if (expectedRotations.Count != count || result.PlacedRotations.Count != count ||
                result.Plan.Placements.Count != count || nesting.Layout.Pieces.Count != count) return false;
            // Validate the complete mapping and angle tables before budgeting or rotating.
            var angles = new int[count];
            var unique = new HashSet<(Guid SourcePieceId, int Angle)>();
            long work = 0;
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mapping = expansion.Placements[i];
                if (result.Plan.Placements[i] != mapping ||
                    !expectedRotations.TryGetValue((mapping.SourcePieceId, mapping.CopyNumber), out int angle) ||
                    angle is not (0 or 90 or 180 or 270) ||
                    !result.PlacedRotations.TryGetValue(mapping.PlacedPieceId, out int recorded) || recorded != angle)
                    return false;
                angles[i] = angle;
                long vertices = expansion.Pieces[i].Points.Count;
                // Translation checks scan each point; rotation validates both contours once per key.
                work += 2L * vertices;
                if (unique.Add((mapping.SourcePieceId, angle))) work += 2L * vertices * vertices;
                if (work > NestingEngine.MaximumValidationWork) return false;
            }
            var selected = expansion.Placements.Select(p => p.SourcePieceId).ToHashSet();
            var sources = new List<PatternPiece>();
            foreach (var piece in source.Pieces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (piece is not null && selected.Contains(piece.Id))
                {
                    if (piece.Name is null || piece.Name.Length > 1024) return false;
                    sources.Add(piece);
                }
            }
            if (string.IsNullOrEmpty(result.SourceFingerprint) || string.IsNullOrEmpty(result.LayoutFingerprint) ||
                !string.Equals(result.SourceFingerprint, NestingSnapshot.ComputeLayoutFingerprint(
                    new ProjectDocument { Pieces = sources }, cancellationToken), StringComparison.Ordinal)) return false;
            var placed = new Dictionary<Guid, PatternPiece>();
            foreach (var piece in nesting.Layout.Pieces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (piece?.Points is null || piece.Id == Guid.Empty || piece.Name is null || piece.Name.Length > 1024 ||
                    piece.Metadata is null || piece.Metadata.Role != "Kesim" ||
                    string.IsNullOrWhiteSpace(piece.Metadata.Material) || !ValidText(piece.Metadata.Material) ||
                    !ValidText(piece.Metadata.Size) ||
                    piece.Points.Count is < 3 or > NestingEngine.MaximumVerticesPerPiece ||
                    !placed.TryAdd(piece.Id, piece)) return false;
            }
            if (!string.Equals(result.LayoutFingerprint, NestingSnapshot.ComputeLayoutFingerprint(nesting.Layout, cancellationToken),
                StringComparison.Ordinal)) return false;
            var cache = new Dictionary<(Guid SourcePieceId, int Angle), PatternPiece>();
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mapping = expansion.Placements[i];
                int angle = angles[i];
                if (!placed.Remove(mapping.PlacedPieceId, out var copy)) return false;
                var key = (mapping.SourcePieceId, angle);
                if (!cache.TryGetValue(key, out var rotated))
                {
                    rotated = OrthogonalRotation.Rotate(expansion.Pieces[i], angle, cancellationToken);
                    GrainDirectionPolicy.Validate(rotated, direction, toleranceDegrees);
                    cache.Add(key, rotated);
                }
                if (!copy.Closed || copy.Name != rotated.Name || copy.Metadata is null || rotated.Metadata is null ||
                    copy.Metadata.Quantity != 1 || copy.Metadata.Role != rotated.Metadata.Role ||
                    copy.Metadata.Size != rotated.Metadata.Size || copy.Metadata.Material != rotated.Metadata.Material) return false;
                NestingEngine.ValidateTranslation(rotated, copy.Points, cancellationToken);
                if (!NestingEngine.GrainlinesEqualAfterTranslation(rotated, copy)) return false;
                if (!NestingEngine.NotchesEqualAfterTranslation(rotated, copy, cancellationToken)) return false;
                GrainDirectionPolicy.Validate(copy, direction, toleranceDegrees);
            }
            if (placed.Count != 0) return false;
            ShelfLayoutVerifier.Verify(nesting, cancellationToken);
            return true;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
