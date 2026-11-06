namespace TekstilCAD.Core;

/// <summary>
/// Owns a document's editing transactions, selection, bounded history and saved-content checkpoint.
/// Consumers may read Document, but must perform mutations through Execute. This class is single-threaded.
/// Loaded inputs and committed results are cloned so callers cannot retain mutable input ownership.
/// </summary>
public sealed class DocumentSession
{
    private readonly DocumentHistory history;
    private ProjectDocument savedDocument;
    private Guid? selectedId;
    private bool explicitlyUnsaved;
    private bool executing;

    public ProjectDocument Document { get; private set; }
    public Guid? SelectedId
    {
        get => selectedId;
        set => selectedId = value is { } id && Document.Pieces.Any(p => p.Id == id) ? id : null;
    }
    public bool IsDirty => explicitlyUnsaved || !ContentEquals(Document, savedDocument);
    public int UndoCount => history.UndoCount;
    public int RedoCount => history.RedoCount;
    public long StoredPointCount => history.StoredPointCount;
    /// <summary>Snapshots discarded for history capacity during this session's lifetime; loading a document does not reset this monotonic counter.</summary>
    public long CapacityDiscardedSnapshots => history.CapacityDiscardedSnapshots;
    public bool CanRetainCurrentUndoSnapshot => history.CanRetainSnapshot(Document);
    /// <summary>Checks whether a prospective document fits a single undo or redo snapshot.</summary>
    public bool CanRetainUndoSnapshot(ProjectDocument document) => history.CanRetainSnapshot(document);

    public DocumentSession(ProjectDocument? initial = null, int maximumSnapshots = 50, long maximumPoints = 2_000_000)
    {
        history = new(maximumSnapshots, maximumPoints);
        Document = (initial ?? new ProjectDocument()).Clone();
        savedDocument = Document.Clone();
    }

    public void Load(ProjectDocument document, Action<ProjectDocument>? validate = null)
    {
        EnsureNotExecuting();
        ArgumentNullException.ThrowIfNull(document);
        executing = true;
        try
        {
            var candidate = document.Clone();
            validate?.Invoke(candidate);
            // The validator must not retain ownership of the new live document either.
            var owned = candidate.Clone();
            var saved = owned.Clone();
            Document = owned;
            savedDocument = saved;
            selectedId = null;
            explicitlyUnsaved = false;
            history.Clear();
        }
        finally { executing = false; }
    }

    /// <returns>True only when document content changed. Selection alone is not a document edit.</returns>
    public bool Execute(Action<ProjectDocument> action, Action<ProjectDocument>? validate = null)
    {
        EnsureNotExecuting();
        ArgumentNullException.ThrowIfNull(action);
        var before = Document.Clone();
        var beforeSelection = selectedId;
        executing = true;
        try
        {
            action(Document);
            validate?.Invoke(Document);
            if(before.PhotoTraceHistory is { } oldHistory && (Document.PhotoTraceHistory is not { } newHistory ||
                newHistory.Count<oldHistory.Count || !PhotoTraceEquals(oldHistory,newHistory.Take(oldHistory.Count).ToList())))
                throw new InvalidOperationException("Fotoğraf taslağı geçmişi işlem sırasında silinemez veya değiştirilemez.");
            var changed = !ContentEquals(before, Document);
            // Clone even on no-op to detach lists/references supplied by the action or validator.
            var committed = changed ? Document.Clone() : before;
            if (changed) history.Record(before, beforeSelection);
            Document = committed;
            NormalizeSelection();
            return changed;
        }
        catch
        {
            Document = before;
            selectedId = beforeSelection;
            NormalizeSelection();
            throw;
        }
        finally { executing = false; }
    }

    public void MarkSaved()
    {
        EnsureNotExecuting();
        savedDocument = Document.Clone();
        explicitlyUnsaved = false;
    }

    /// <summary>Marks a newly imported/recovered document as requiring its first durable save.</summary>
    public void MarkUnsaved()
    {
        EnsureNotExecuting();
        explicitlyUnsaved = true;
    }

    public bool Undo()
    {
        EnsureNotExecuting();
        return Restore(history.UndoState(Document, selectedId));
    }

    public bool Redo()
    {
        EnsureNotExecuting();
        return Restore(history.RedoState(Document, selectedId));
    }

    private bool Restore(DocumentHistoryState? state)
    {
        if (state is null) return false;
        Document = state.Document;
        selectedId = state.SelectedId;
        NormalizeSelection();
        return true;
    }

    private void NormalizeSelection() => SelectedId = selectedId;

    private void EnsureNotExecuting()
    {
        if (executing) throw new InvalidOperationException("An editing transaction cannot start another transaction or change its save/history state.");
    }

    /// <summary>Exact equality of persisted model fields; selection and object identity are excluded.</summary>
    private static bool MetadataEquals(PieceMetadata? a, PieceMetadata? b) => ReferenceEquals(a,b) || (a is not null && b is not null && a.Role == b.Role && a.Size == b.Size && a.Material == b.Material && a.Quantity == b.Quantity);
    // Coordinate bits are persisted, including signed zero. Snapshot equality
    // must notice changes that a later grade or save would otherwise discard.
    private static bool PointBitsEqual(Point2 a,Point2 b) =>
        BitConverter.DoubleToInt64Bits(a.X)==BitConverter.DoubleToInt64Bits(b.X) &&
        BitConverter.DoubleToInt64Bits(a.Y)==BitConverter.DoubleToInt64Bits(b.Y);
    private static bool PointListsEqual(IReadOnlyList<Point2> a,IReadOnlyList<Point2> b)
    {
        if(a.Count!=b.Count)return false;
        for(var i=0;i<a.Count;i++)if(!PointBitsEqual(a[i],b[i]))return false;
        return true;
    }
    private static bool GrainlineEquals(Grainline? a, Grainline? b) => ReferenceEquals(a,b) ||
        (a is not null && b is not null && PointBitsEqual(a.Start,b.Start) && PointBitsEqual(a.End,b.End) && a.NeedsReview == b.NeedsReview);
    private static bool NotchesEqual(List<NotchMark>? a,List<NotchMark>? b) => ReferenceEquals(a,b) ||
        (a is not null && b is not null && a.Count==b.Count && a.Zip(b).All(pair=>pair.First.VertexIndex==pair.Second.VertexIndex && PointBitsEqual(pair.First.Position,pair.Second.Position)));
    private static bool NestingEquals(NestingSnapshot? a, NestingSnapshot? b) => ReferenceEquals(a,b) ||
        (a is not null && b is not null && a.FabricWidth == b.FabricWidth && a.UsedLength == b.UsedLength &&
         BitConverter.DoubleToInt64Bits(a.Gap)==BitConverter.DoubleToInt64Bits(b.Gap) &&
         BitConverter.DoubleToInt64Bits(a.EdgeMargin)==BitConverter.DoubleToInt64Bits(b.EdgeMargin) && a.Algorithm == b.Algorithm &&
         a.LayoutFingerprint == b.LayoutFingerprint && a.Placements.SequenceEqual(b.Placements) &&
         (a.RotationTrace is null ? b.RotationTrace is null : b.RotationTrace is not null && RotatedNestingTrace.ContentEquals(a.RotationTrace,b.RotationTrace)));
    private static bool ProvenanceEquals(DxfImportProvenance? a, DxfImportProvenance? b) => ReferenceEquals(a,b) ||
        (a is not null && b is not null && a.SourceFileName==b.SourceFileName && a.SourceSha256==b.SourceSha256 &&
         a.SourceByteLength==b.SourceByteLength && a.MillimetersPerUnit==b.MillimetersPerUnit &&
         a.Warnings.SequenceEqual(b.Warnings) && a.PieceIds.SequenceEqual(b.PieceIds));
    private static bool PhotoPlaneEquals(PhotoPlaneCalibration? a,PhotoPlaneCalibration? b)=>ReferenceEquals(a,b) ||
        (a is not null && b is not null && PointListsEqual(a.Corners,b.Corners) &&
         BitConverter.DoubleToInt64Bits(a.WidthMm)==BitConverter.DoubleToInt64Bits(b.WidthMm) &&
         BitConverter.DoubleToInt64Bits(a.HeightMm)==BitConverter.DoubleToInt64Bits(b.HeightMm));
    private static bool PhotoTraceEquals(List<PhotoTraceProvenance>? a,List<PhotoTraceProvenance>? b) => ReferenceEquals(a,b) ||
        (a is not null && b is not null && a.Count==b.Count && a.Zip(b).All(p=>
            p.First.AlgorithmVersion==p.Second.AlgorithmVersion && p.First.PieceId==p.Second.PieceId && p.First.SourceSha256==p.Second.SourceSha256 &&
            p.First.SourceByteLength==p.Second.SourceByteLength && p.First.SourceOrientation==p.Second.SourceOrientation && p.First.SourceWidth==p.Second.SourceWidth && p.First.SourceHeight==p.Second.SourceHeight &&
            p.First.PreviewWidth==p.Second.PreviewWidth && p.First.PreviewHeight==p.Second.PreviewHeight &&
            PointBitsEqual(p.First.ReferenceStart,p.Second.ReferenceStart) && PointBitsEqual(p.First.ReferenceEnd,p.Second.ReferenceEnd) &&
            BitConverter.DoubleToInt64Bits(p.First.ReferenceLengthMm)==BitConverter.DoubleToInt64Bits(p.Second.ReferenceLengthMm) &&
            PhotoPlaneEquals(p.First.PlaneCalibration,p.Second.PlaneCalibration) &&
            PointListsEqual(p.First.TracePixels,p.Second.TracePixels) && PointListsEqual(p.First.GeneratedPoints,p.Second.GeneratedPoints)));
    public static bool ContentEquals(ProjectDocument left, ProjectDocument right)
    {
        if (!ProvenanceEquals(left.ImportProvenance,right.ImportProvenance)) return false;
        if (!PhotoTraceEquals(left.PhotoTraceHistory,right.PhotoTraceHistory)) return false;
        if (!NestingEquals(left.NestingSnapshot,right.NestingSnapshot)) return false;
        if (!string.Equals(left.Name, right.Name, StringComparison.Ordinal) || left.Pieces.Count != right.Pieces.Count) return false;
        for (var i = 0; i < left.Pieces.Count; i++)
        {
            var a = left.Pieces[i]; var b = right.Pieces[i];
            if (a.Id != b.Id || !string.Equals(a.Name, b.Name, StringComparison.Ordinal) || a.Closed != b.Closed || a.Points.Count != b.Points.Count || !MetadataEquals(a.Metadata,b.Metadata) || !GrainlineEquals(a.Grainline,b.Grainline) || !NotchesEqual(a.Notches,b.Notches) || a.ExperimentalALine!=b.ExperimentalALine) return false;
            for (var j = 0; j < a.Points.Count; j++)
                if (!PointBitsEqual(a.Points[j],b.Points[j])) return false;
        }
        if(left.ALineEvidence is null ? right.ALineEvidence is not null : right.ALineEvidence is null || left.ALineEvidence.Count!=right.ALineEvidence.Count ||
           !left.ALineEvidence.Zip(right.ALineEvidence).All(pair=>ALineProjectEvidence.ContentEquals(pair.First,pair.Second)))return false;
        if (left.GradingRules.Count != right.GradingRules.Count) return false;
        for (var i = 0; i < left.GradingRules.Count; i++)
        {
            var a = left.GradingRules[i]; var b = right.GradingRules[i];
            if (a.SourcePieceId != b.SourcePieceId || a.SourceClosed != b.SourceClosed ||
                !PointListsEqual(a.SourcePoints,b.SourcePoints) || !PointListsEqual(a.Deltas,b.Deltas)) return false;
        }
        return true;
    }
}
