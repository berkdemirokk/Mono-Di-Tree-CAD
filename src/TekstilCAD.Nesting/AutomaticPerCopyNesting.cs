using TekstilCAD.Core;

namespace TekstilCAD.Nesting;

/// <summary>Deterministic bounded angle heuristic; no global optimality is claimed.</summary>
public static class AutomaticPerCopyNesting
{
    private sealed record Candidate(int Angle, double Width, double Height);

    public static PerCopyNestingResult Arrange(ProjectDocument source, IReadOnlyCollection<Guid> selectedIds,
        IReadOnlyDictionary<Guid, IReadOnlyList<int>> allowedAngles, double fabricWidth, double gap,
        double maxLength, double edgeMargin, GrainDirectionPolicy.Direction direction, double toleranceDegrees,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selectedIds);
        ArgumentNullException.ThrowIfNull(allowedAngles);
        GrainDirectionPolicy.ValidateParameters(direction, toleranceDegrees);
        if (!double.IsFinite(fabricWidth) || fabricWidth <= 0 || fabricWidth > NestingEngine.MaximumDimension) throw new ArgumentOutOfRangeException(nameof(fabricWidth));
        if (!double.IsFinite(gap) || gap < 0 || gap > NestingEngine.MaximumDimension) throw new ArgumentOutOfRangeException(nameof(gap));
        if (!double.IsFinite(edgeMargin) || edgeMargin < 0 || edgeMargin * 2 >= fabricWidth) throw new ArgumentOutOfRangeException(nameof(edgeMargin));
        if (double.IsNaN(maxLength) || maxLength <= 0 || double.IsNegativeInfinity(maxLength)) throw new ArgumentOutOfRangeException(nameof(maxLength));
        if (source.Pieces is not null && source.Pieces.Count > NestingEngine.MaximumTotalVertices) throw new ArgumentException("Kaynak proje parça sayısı sınırını aşıyor.", nameof(source));
        var expansion = ProductionNestingPlan.Expand(source, selectedIds, cancellationToken);
        var originals = new Dictionary<Guid, PatternPiece>();
        var counts = new Dictionary<Guid, int>();
        foreach (var mapping in expansion.Placements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            counts.TryGetValue(mapping.SourcePieceId, out var count);
            counts[mapping.SourcePieceId] = count + 1;
        }
        for (int i = 0; i < expansion.Placements.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = expansion.Placements[i].SourcePieceId;
            if (!originals.ContainsKey(id))
            {
                var p = expansion.Pieces[i].Clone(); p.Id = id; p.Metadata!.Quantity = counts[id]; originals.Add(id, p);
            }
        }
        // Use detached expanded copies even if a caller mutates the source while angles are read.
        var snapshot = new ProjectDocument { Pieces = originals.Values.ToList() };
        var ids = originals.Keys.ToArray();
        if (allowedAngles.Count != ids.Length)
            throw new ArgumentException("Her seçili parça için tam bir açı listesi gereklidir.", nameof(allowedAngles));
        var lists = new Dictionary<Guid, int[]>();
        long work = 0;
        void Charge(long amount)
        {
            if (amount < 0 || amount > NestingEngine.MaximumValidationWork - work)
                throw new ArgumentException("Pastal doğrulama kaynak sınırı aşıldı.", nameof(source));
            work += amount;
        }
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!allowedAngles.TryGetValue(id, out var list) || list is null)
                throw NestingPieceError.Create(id, "İzin verilen açılar eksik veya geçersiz.", nameof(allowedAngles));
            int angleCount = list.Count;
            if (angleCount is < 1 or > 4)
                throw NestingPieceError.Create(id, "İzin verilen açılar eksik veya geçersiz.", nameof(allowedAngles));
            var angles = new int[angleCount]; var seen = new HashSet<int>();
            for (int i = 0; i < angles.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int angle = list[i];
                if (angle is not (0 or 90 or 180 or 270) || !seen.Add(angle))
                    throw NestingPieceError.Create(id, "İzin verilen açılar geçersiz veya yinelenmiş.", nameof(allowedAngles));
                angles[i] = angle;
            }
            lists.Add(id, angles);
            long v = originals[id].Points.Count;
            long per = 2L * v * v;
            // Charge candidate validation and downstream per-copy validation up front.
            Charge(per * angles.Length); Charge(v * v * counts[id]);
        }
        var candidates = new Dictionary<Guid, List<Candidate>>();
        double usable = fabricWidth - 2 * edgeMargin;
        foreach (var id in ids)
        {
            var options = new List<Candidate>();
            foreach (var angle in lists[id])
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var rotated = OrthogonalRotation.Rotate(originals[id], angle, cancellationToken);
                    GrainDirectionPolicy.Validate(rotated, direction, toleranceDegrees);
                    var bounds = Geometry.Bounds(rotated.Points);
                    if (bounds.Width <= usable) options.Add(new(angle, bounds.Width, bounds.Height));
                }
                catch (ArgumentException) { }
            }
            if (options.Count == 0) throw NestingPieceError.Create(id, "Uygun geometri ve iplik yönüne sahip açı yok.", nameof(allowedAngles));
            candidates.Add(id, options);
        }
        var chosen = new Dictionary<(Guid, int), int>();
        double x = 0, rowHeight = 0, completed = 0;
        foreach (var mapping in expansion.Placements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Candidate? best = null; double bestScore = double.PositiveInfinity; bool bestFits = false;
            foreach (var candidate in candidates[mapping.SourcePieceId])
            {
                bool fits = x == 0 || x + gap + candidate.Width <= usable;
                double score = fits ? completed + Math.Max(rowHeight, candidate.Height) : completed + rowHeight + gap + candidate.Height;
                if (best is null || score < bestScore || (score == bestScore && fits && !bestFits))
                { best = candidate; bestScore = score; bestFits = fits; }
            }
            var selected = best!;
            if (!bestFits) { completed += rowHeight + gap; x = 0; rowHeight = 0; }
            if (x > 0) x += gap;
            x += selected.Width; rowHeight = Math.Max(rowHeight, selected.Height);
            chosen.Add((mapping.SourcePieceId, mapping.CopyNumber), selected.Angle);
        }
        var unique = new HashSet<(Guid, int)>();
        foreach (var entry in chosen)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (unique.Add((entry.Key.Item1, entry.Value)))
            {
                long v = originals[entry.Key.Item1].Points.Count;
                Charge(3L * v * v);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        PerCopyNestingResult? first = null;
        Exception? firstError = null;
        try
        {
            first = PerCopyOrthogonalNesting.Arrange(snapshot, ids, chosen, fabricWidth, gap, maxLength, edgeMargin,
                direction, toleranceDegrees, cancellationToken);
        }
        catch (InvalidOperationException error) when (NestingEngine.IsLengthExceeded(error))
        {
            firstError = error;
        }
        catch (InvalidOperationException error) when (NestingEngine.IsClearancePrecision(error))
        {
            firstError = error;
        }
        catch (ArgumentException error) when (NestingPieceError.TryGet(error, out _, out _))
        {
            firstError = error;
        }

        // The estimator uses source order, while the shelf engine sorts by height.
        // Compare bounded alternatives using the actual verified layout length.
        var alternate = new Dictionary<(Guid, int), int>();
        var alternateUnique = new HashSet<(Guid, int)>();
        bool differs = false;
        long extra = 0;
        foreach (var mapping in expansion.Placements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = (mapping.SourcePieceId, mapping.CopyNumber);
            int angle = candidates[mapping.SourcePieceId][0].Angle;
            alternate.Add(key, angle);
            if (chosen[key] != angle) differs = true;
            long v = originals[mapping.SourcePieceId].Points.Count;
            extra += v * v;
            if (alternateUnique.Add((mapping.SourcePieceId, angle))) extra += 3L * v * v;
        }
        var narrow = new Dictionary<(Guid, int), int>();
        var narrowUnique = new HashSet<(Guid, int)>();
        long narrowWork = 0;
        foreach (var mapping in expansion.Placements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = (mapping.SourcePieceId, mapping.CopyNumber);
            var option = candidates[mapping.SourcePieceId].OrderBy(c => c.Width).ThenBy(c => c.Height).First();
            narrow.Add(key, option.Angle);
            long v = originals[mapping.SourcePieceId].Points.Count;
            narrowWork += v * v;
            if (narrowUnique.Add((mapping.SourcePieceId, option.Angle))) narrowWork += 3L * v * v;
        }
        // Narrow pieces are not always the shortest marker: a wider, shorter
        // orientation can reduce the height of every shelf. Verify that policy
        // with the same actual layout and resource budget as the other choices.
        var shortRows = new Dictionary<(Guid, int), int>();
        var shortUnique = new HashSet<(Guid, int)>();
        long shortWork = 0;
        foreach (var mapping in expansion.Placements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = (mapping.SourcePieceId, mapping.CopyNumber);
            var option = candidates[mapping.SourcePieceId].OrderBy(c => c.Height).ThenBy(c => c.Width).First();
            shortRows.Add(key, option.Angle);
            long v = originals[mapping.SourcePieceId].Points.Count;
            shortWork += v * v;
            if (shortUnique.Add((mapping.SourcePieceId, option.Angle))) shortWork += 3L * v * v;
        }
        // A consistent second allowed orientation can pack better than choosing
        // each piece independently by width or height. It remains a bounded
        // heuristic and goes through the same placement and validation path.
        var second = new Dictionary<(Guid, int), int>();
        var secondUnique = new HashSet<(Guid, int)>();
        long secondWork = 0;
        foreach (var mapping in expansion.Placements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = (mapping.SourcePieceId, mapping.CopyNumber);
            var options = candidates[mapping.SourcePieceId];
            int angle = options[Math.Min(1, options.Count - 1)].Angle;
            second.Add(key, angle);
            long v = originals[mapping.SourcePieceId].Points.Count;
            secondWork += v * v;
            if (secondUnique.Add((mapping.SourcePieceId, angle))) secondWork += 3L * v * v;
        }
        // Alternate the first two independently permitted orientations per
        // source copy. Both phases are bounded heuristics, not an optimality
        // claim, and each goes through the full per-copy placement verifier.
        var alternating = new Dictionary<(Guid, int), int>();
        var alternatingReverse = new Dictionary<(Guid, int), int>();
        var alternatingUnique = new HashSet<(Guid, int)>();
        var reverseUnique = new HashSet<(Guid, int)>();
        long alternatingWork = 0, reverseWork = 0;
        foreach (var mapping in expansion.Placements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key=(mapping.SourcePieceId,mapping.CopyNumber);var options=candidates[mapping.SourcePieceId];
            var firstIndex=options.Count>1 && mapping.CopyNumber%2==1 ? 1 : 0;
            var otherIndex=options.Count>1 ? 1-firstIndex : 0;
            var angle=options[firstIndex].Angle;var reverseAngle=options[otherIndex].Angle;
            alternating.Add(key,angle);alternatingReverse.Add(key,reverseAngle);
            long v=originals[mapping.SourcePieceId].Points.Count;
            alternatingWork+=v*v;reverseWork+=v*v;
            if(alternatingUnique.Add((mapping.SourcePieceId,angle)))alternatingWork+=3L*v*v;
            if(reverseUnique.Add((mapping.SourcePieceId,reverseAngle)))reverseWork+=3L*v*v;
        }
        foreach (var (angles, cost, needed) in new[]
        {
            (alternate, extra, differs),
            (narrow, narrowWork, narrow.Any(pair => chosen[pair.Key] != pair.Value) &&
                narrow.Any(pair => alternate[pair.Key] != pair.Value)),
            (shortRows, shortWork, shortRows.Any(pair => chosen[pair.Key] != pair.Value) &&
                shortRows.Any(pair => alternate[pair.Key] != pair.Value) &&
                shortRows.Any(pair => narrow[pair.Key] != pair.Value)),
            (second, secondWork, second.Any(pair => chosen[pair.Key] != pair.Value) &&
                second.Any(pair => alternate[pair.Key] != pair.Value) &&
                second.Any(pair => narrow[pair.Key] != pair.Value) &&
                second.Any(pair => shortRows[pair.Key] != pair.Value)),
            (alternating, alternatingWork, alternating.Any(pair => chosen[pair.Key] != pair.Value) &&
                alternating.Any(pair => alternate[pair.Key] != pair.Value) &&
                alternating.Any(pair => narrow[pair.Key] != pair.Value) &&
                alternating.Any(pair => shortRows[pair.Key] != pair.Value) &&
                alternating.Any(pair => second[pair.Key] != pair.Value)),
            (alternatingReverse, reverseWork, alternatingReverse.Any(pair => chosen[pair.Key] != pair.Value) &&
                alternatingReverse.Any(pair => alternate[pair.Key] != pair.Value) &&
                alternatingReverse.Any(pair => narrow[pair.Key] != pair.Value) &&
                alternatingReverse.Any(pair => shortRows[pair.Key] != pair.Value) &&
                alternatingReverse.Any(pair => second[pair.Key] != pair.Value) &&
                alternatingReverse.Any(pair => alternating[pair.Key] != pair.Value))
        })
        {
            if (!needed) continue;
            if (cost > NestingEngine.MaximumValidationWork - work)
            {
                if (first is not null) break;
                throw new InvalidOperationException("İlk yerleşim başarısız oldu; alternatif açı düzeni doğrulama kaynak sınırı nedeniyle denenemedi.", firstError);
            }
            work += cost;
            try
            {
                var result = PerCopyOrthogonalNesting.Arrange(snapshot, ids, angles, fabricWidth, gap, maxLength,
                    edgeMargin, direction, toleranceDegrees, cancellationToken);
                if (first is null || result.Plan.Nesting.UsedLength < first.Plan.Nesting.UsedLength) first = result;
            }
            catch (ArgumentException error) when (NestingPieceError.TryGet(error, out _, out _)) { firstError ??= error; }
            catch (InvalidOperationException error) when (NestingEngine.IsLengthExceeded(error) || NestingEngine.IsClearancePrecision(error)) { firstError ??= error; }
        }
        return first ?? throw firstError!;
    }
}
