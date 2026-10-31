using TekstilCAD.Core;

namespace TekstilCAD.Nesting;

/// <summary>Opt-in shelf planning with a required grain policy. No rotation is performed.</summary>
public static class GrainConstrainedNesting
{
    public static ProductionNestingResult Arrange(ProjectDocument source, IReadOnlyCollection<Guid> selectedIds,
        double fabricWidth, double gap, double maxLength, double edgeMargin,
        GrainDirectionPolicy.Direction direction, double toleranceDegrees, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GrainDirectionPolicy.ValidateParameters(direction, toleranceDegrees);
        // Run the regular selection, identity, geometry and resource checks first.
        var result = ProductionNestingPlan.Arrange(source, selectedIds, fabricWidth, gap, maxLength, edgeMargin, cancellationToken);
        // Check the source as well as every placed copy. Translation rounding can
        // otherwise move a barely out-of-tolerance line inside the tolerance.
        var selected = result.Placements.Select(p => p.SourcePieceId).ToHashSet();
        foreach (var sourcePiece in source.Pieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sourcePiece is not null && selected.Contains(sourcePiece.Id))
                GrainDirectionPolicy.Validate(sourcePiece, direction, toleranceDegrees);
        }
        // Recheck actual output independently using exact comparisons of the
        // represented coordinates. The verifier has its own fail-closed work limit.
        ShelfLayoutVerifier.Verify(result.Nesting, cancellationToken);
        var sourceByPlaced = result.Placements.ToDictionary(p => p.PlacedPieceId, p => p.SourcePieceId);
        foreach (var piece in result.Nesting.Layout.Pieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { GrainDirectionPolicy.Validate(piece, direction, toleranceDegrees); }
            catch (ArgumentException error) when (NestingPieceError.TryGet(error, out _, out _))
            {
                NestingPieceError.TryGet(error, out var placedId, out var reason);
                if (!sourceByPlaced.TryGetValue(placedId, out var sourceId)) throw;
                throw NestingPieceError.Create(sourceId, reason, error.ParamName, error);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}
