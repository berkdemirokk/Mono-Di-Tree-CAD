using TekstilCAD.Core;

namespace TekstilCAD.Nesting;

/// <summary>A historical result. Fingerprints detect changes to selected source and placed
/// piece content; they do not certify the rotation mapping or production suitability.</summary>
public sealed record OrthogonalNestingResult(ProductionNestingResult Plan, IReadOnlyDictionary<Guid, int> SourceRotations)
{
    public string LayoutFingerprint { get; init; } = "";
    /// <summary>Historical fingerprint of the selected source pieces in document order.</summary>
    public string SourceFingerprint { get; init; } = "";

    public bool IsLayoutUnchanged(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return !string.IsNullOrEmpty(LayoutFingerprint) &&
            string.Equals(LayoutFingerprint, NestingSnapshot.ComputeLayoutFingerprint(Plan.Nesting.Layout, cancellationToken), StringComparison.Ordinal);
    }
}

/// <summary>
/// Opt-in deterministic shelf placement. Each source uses the admissible quarter-turn
/// with a deterministic heuristic. It compares a per-source shelf estimate with
/// a narrower-orientation alternative using a bounding-box shelf simulation.
/// All copies of a source use the same orientation. This does not prove that no
/// other combination could fit the length or minimize fabric use.
/// </summary>
public static class OrthogonalGrainNesting
{
    public static OrthogonalNestingResult Arrange(ProjectDocument source, IReadOnlyCollection<Guid> selectedIds,
        double fabricWidth, double gap, double maxLength, double edgeMargin,
        IReadOnlyList<int> allowedRotations, GrainDirectionPolicy.Direction direction, double toleranceDegrees,
        CancellationToken cancellationToken = default)
        => ArrangeCore(source, selectedIds, fabricWidth, gap, maxLength, edgeMargin,
            allowedRotations, null, direction, toleranceDegrees, cancellationToken);

    /// <summary>Each selected source must have exactly one nonempty list of permitted quarter-turns.
    /// List order is significant for ties; all copies of a source share its chosen angle.</summary>
    public static OrthogonalNestingResult ArrangeWithPieceRotations(ProjectDocument source, IReadOnlyCollection<Guid> selectedIds,
        double fabricWidth, double gap, double maxLength, double edgeMargin,
        IReadOnlyDictionary<Guid, IReadOnlyList<int>> pieceRotations, GrainDirectionPolicy.Direction direction,
        double toleranceDegrees, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pieceRotations);
        return ArrangeCore(source, selectedIds, fabricWidth, gap, maxLength, edgeMargin,
            null, pieceRotations, direction, toleranceDegrees, cancellationToken);
    }

    private static OrthogonalNestingResult ArrangeCore(ProjectDocument source, IReadOnlyCollection<Guid> selectedIds,
        double fabricWidth, double gap, double maxLength, double edgeMargin,
        IReadOnlyList<int>? allowedRotations, IReadOnlyDictionary<Guid, IReadOnlyList<int>>? pieceRotations,
        GrainDirectionPolicy.Direction direction, double toleranceDegrees, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selectedIds);
        if (pieceRotations is null) ArgumentNullException.ThrowIfNull(allowedRotations);
        if (!double.IsFinite(fabricWidth) || fabricWidth <= 0 || fabricWidth > NestingEngine.MaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(fabricWidth));
        if (!double.IsFinite(gap) || gap < 0 || gap > NestingEngine.MaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(gap));
        if (double.IsNaN(maxLength) || maxLength <= 0 || double.IsNegativeInfinity(maxLength))
            throw new ArgumentOutOfRangeException(nameof(maxLength));
        if (!double.IsFinite(edgeMargin) || edgeMargin < 0 || edgeMargin * 2 >= fabricWidth)
            throw new ArgumentOutOfRangeException(nameof(edgeMargin));
        static int[] SnapshotAngles(IReadOnlyList<int>? list, string parameter)
        {
            if (list is null)
                throw new ArgumentException("İzin verilen açılar eksik veya geçersiz.", parameter);
            int count = list.Count;
            if (count is < 1 or > 4)
                throw new ArgumentException("İzin verilen açılar eksik veya geçersiz.", parameter);
            // IReadOnlyList.Count can be bounded while an arbitrary enumerator
            // is not; copy only the advertised (at most four) indexed values.
            var angles = new int[count];
            for (int i = 0; i < angles.Length; i++) angles[i] = list[i];
            if (angles.Any(a => a is not (0 or 90 or 180 or 270)) || angles.Distinct().Count() != angles.Length)
                throw new ArgumentException("İzin verilen açılar benzersiz ve 0, 90, 180 veya 270 derece olmalıdır.", parameter);
            return angles;
        }
        var globalAngles = pieceRotations is null ? SnapshotAngles(allowedRotations, nameof(allowedRotations)) : null;
        GrainDirectionPolicy.ValidateParameters(direction, toleranceDegrees);
        int declaredCount = selectedIds.Count;
        if (source.Pieces is null || declaredCount is < 1 or > NestingEngine.MaximumPieces)
            throw new ArgumentException("Yerleştirilecek kesim parçalarını seçin.", nameof(selectedIds));
        // Unselected identities are also needed for collision detection. Bound the
        // source traversal and retained list even when only one piece is selected.
        if (source.Pieces.Count > NestingEngine.MaximumTotalVertices)
            throw new ArgumentException("Kaynak proje parça sayısı sınırını aşıyor.", nameof(source));
        var selection = new HashSet<Guid>();
        int visited = 0;
        foreach (var id in selectedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++visited > declaredCount || id == Guid.Empty || !selection.Add(id)) throw new ArgumentException("Parça seçimi yinelenmiş veya geçersiz.", nameof(selectedIds));
        }
        if (visited != declaredCount) throw new ArgumentException("Parça seçimi yinelenmiş veya geçersiz.", nameof(selectedIds));
        var selectedSnapshot = selection.ToArray();
        var matches = new Dictionary<Guid, PatternPiece>();
        foreach (var piece in source.Pieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (piece is not null && selection.Contains(piece.Id) && !matches.TryAdd(piece.Id, piece))
                throw new ArgumentException("Seçili parça kimliği yinelenmiş.", nameof(source));
        }
        if (matches.Count != selection.Count) throw new ArgumentException("Seçili parça bulunamadı.", nameof(selectedIds));
        var permitted = new Dictionary<Guid, int[]>(selection.Count);
        if (pieceRotations is not null && pieceRotations.Count != selection.Count)
            throw new ArgumentException("Her seçili parça için tam bir açı listesi gereklidir.", nameof(pieceRotations));
        foreach (var id in selection)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pieceRotations is null) permitted.Add(id, globalAngles!);
            else if (!pieceRotations.TryGetValue(id, out var list))
                throw new ArgumentException("Her seçili parça için tam bir açı listesi gereklidir.", nameof(pieceRotations));
            else permitted.Add(id, SnapshotAngles(list, nameof(pieceRotations)));
        }

        // Budget candidate contour validation as well as the engine's two passes per copy.
        long work = 0, extraPlacementWork = 0, vertices = 0, copies = 0;
        foreach (var piece in matches.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (piece.Points is null || piece.Points.Count > NestingEngine.MaximumVerticesPerPiece)
                throw NestingPieceError.Create(piece.Id, "Parça noktaları eksik veya sınırı aşıyor.", nameof(source));
            if (piece.Metadata?.Quantity is not int n || n < 1 || n > 100000)
                throw NestingPieceError.Create(piece.Id, "Parça için pozitif adet belirtilmelidir.", nameof(source));
            int count = piece.Points.Count;
            copies += n; vertices += (long)n * count;
            work += (2L * n + 2L * permitted[piece.Id].Length) * count * count;
            extraPlacementWork += 2L * n * count * count;
            if (copies > NestingEngine.MaximumPieces || vertices > NestingEngine.MaximumTotalVertices || work > NestingEngine.MaximumValidationWork)
                throw new ArgumentException("Pastal doğrulama kaynak sınırı aşıldı.", nameof(source));
        }

        var rotated = new Dictionary<Guid, PatternPiece>();
        var angles = new Dictionary<Guid, int>();
        var narrowRotated = new Dictionary<Guid, PatternPiece>();
        var narrowAngles = new Dictionary<Guid, int>();
        var sourceSnapshots = new List<PatternPiece>(matches.Count);
        foreach (var original in matches.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Keep the source used by all candidate rotations and the historical
            // fingerprint detached from later caller edits.
            var piece = original.Clone();
            sourceSnapshots.Add(piece);
            PatternPiece? best = null;
            PatternPiece? narrow = null;
            var bestAngle = 0;
            var narrowAngle = 0;
            var bestHeight = double.PositiveInfinity;
            var bestScore = double.PositiveInfinity;
            var narrowWidth = double.PositiveInfinity;
            var narrowHeight = double.PositiveInfinity;
            foreach (var angle in permitted[piece.Id])
            {
                cancellationToken.ThrowIfCancellationRequested();
                PatternPiece candidate;
                try { candidate = OrthogonalRotation.Rotate(piece, angle, cancellationToken); }
                catch (ArgumentException error)
                { throw NestingPieceError.Create(piece.Id, "Parça dönüşüm için geçersiz.", nameof(source), error); }
                var bounds = Geometry.Bounds(candidate.Points);
                if (bounds.Width > fabricWidth - 2 * edgeMargin) continue;
                try { GrainDirectionPolicy.Validate(candidate, direction, toleranceDegrees); }
                catch (ArgumentException error) when (NestingPieceError.TryGet(error, out _, out _)) { continue; }
                // Estimate whole rows of this source in isolation. The actual
                // mixed-piece shelf layout is still calculated and checked below.
                double perRow = Math.Max(1, Math.Floor((fabricWidth - 2 * edgeMargin + gap) / (bounds.Width + gap)));
                double rows = Math.Ceiling(piece.Metadata!.Quantity!.Value / perRow);
                double score = rows * bounds.Height + (rows - 1) * gap;
                if (score < bestScore || (score == bestScore && bounds.Height < bestHeight))
                {
                    best = candidate;
                    bestAngle = angle;
                    bestHeight = bounds.Height;
                    bestScore = score;
                }
                if (bounds.Width < narrowWidth || (bounds.Width == narrowWidth && bounds.Height < narrowHeight))
                {
                    narrow = candidate;
                    narrowAngle = angle;
                    narrowWidth = bounds.Width;
                    narrowHeight = bounds.Height;
                }
            }
            if (best is null)
                throw NestingPieceError.Create(piece.Id, "İzin verilen açılarda parça kumaş enine ve iplik yönüne uymuyor.", nameof(source));
            rotated.Add(piece.Id, best);
            angles.Add(piece.Id, bestAngle);
            narrowRotated.Add(piece.Id, narrow!);
            narrowAngles.Add(piece.Id, narrowAngle);
        }
        if (EstimateLength(narrowRotated, fabricWidth, gap, edgeMargin, cancellationToken) <
            EstimateLength(rotated, fabricWidth, gap, edgeMargin, cancellationToken))
        {
            (rotated, narrowRotated) = (narrowRotated, rotated);
            (angles, narrowAngles) = (narrowAngles, angles);
        }
        ProductionNestingResult Run(Dictionary<Guid, PatternPiece> candidates)
        {
            var inputPieces = new List<PatternPiece>(source.Pieces.Count);
            foreach (var piece in source.Pieces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                inputPieces.Add(piece is not null && candidates.TryGetValue(piece.Id, out var r) ? r : piece!);
            }
            var input = new ProjectDocument { Pieces = inputPieces };
            return GrainConstrainedNesting.Arrange(input, selectedSnapshot, fabricWidth, gap, maxLength, edgeMargin,
                direction, toleranceDegrees, cancellationToken);
        }
        ProductionNestingResult plan;
        try { plan = Run(rotated); }
        catch (InvalidOperationException error) when (NestingEngine.IsLengthExceeded(error) &&
            work + extraPlacementWork <= NestingEngine.MaximumValidationWork &&
            angles.Any(pair => narrowAngles[pair.Key] != pair.Value))
        {
            // Retry only a known length failure. Every successful candidate still
            // passes the full engine, grain policy and independent box verifier.
            plan = Run(narrowRotated);
            angles = narrowAngles;
        }
        var fingerprint = NestingSnapshot.ComputeLayoutFingerprint(plan.Nesting.Layout, cancellationToken);
        var sourceFingerprint = NestingSnapshot.ComputeLayoutFingerprint(
            new ProjectDocument { Pieces = sourceSnapshots }, cancellationToken);
        return new(plan, new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, int>(angles))
            { LayoutFingerprint = fingerprint, SourceFingerprint = sourceFingerprint };
    }

    // A score only: real acceptance is always performed by the engine and verifier.
    private static double EstimateLength(Dictionary<Guid, PatternPiece> pieces, double width, double gap,
        double margin, CancellationToken cancellationToken)
    {
        var boxes = new List<(double Width, double Height)>();
        foreach (var piece in pieces.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bounds = Geometry.Bounds(piece.Points);
            for (int i = 0; i < piece.Metadata!.Quantity!.Value; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                boxes.Add((bounds.Width, bounds.Height));
            }
        }
        double x = 0, y = margin, rowHeight = 0;
        foreach (var box in boxes.OrderByDescending(b => b.Height))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (x > 0 && x + box.Width > width - 2 * margin)
            {
                y += rowHeight + gap;
                x = 0;
                rowHeight = 0;
            }
            rowHeight = Math.Max(rowHeight, box.Height);
            x += box.Width + gap;
        }
        return y + rowHeight + margin;
    }
}
