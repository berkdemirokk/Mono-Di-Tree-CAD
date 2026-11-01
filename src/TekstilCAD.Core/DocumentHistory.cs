namespace TekstilCAD.Core;

public sealed record DocumentHistoryState(ProjectDocument Document, Guid? SelectedId);

/// <summary>Deep snapshot history bounded across undo and redo by count and stored geometry.</summary>
public sealed class DocumentHistory
{
    private sealed record Entry(ProjectDocument Document, Guid? SelectedId, long PointCount, long Sequence);
    private readonly LinkedList<Entry> undo = new(), redo = new();
    private readonly int maximumSnapshots;
    private readonly long maximumPoints;
    private long sequence;
    public int UndoCount => undo.Count;
    public int RedoCount => redo.Count;
    public long StoredPointCount { get; private set; }
    /// <summary>
    /// Cumulative snapshots lost to capacity since this history was created,
    /// including an incoming snapshot too large to retain. Explicit Clear and
    /// ordinary redo invalidation do not increase this value. Saturates at long.MaxValue.
    /// </summary>
    public long CapacityDiscardedSnapshots { get; private set; }
    public DocumentHistory(int maximumSnapshots = 50, long maximumPoints = 2_000_000)
    {
        if (maximumSnapshots < 1) throw new ArgumentOutOfRangeException(nameof(maximumSnapshots));
        if (maximumPoints < 1) throw new ArgumentOutOfRangeException(nameof(maximumPoints));
        this.maximumSnapshots = maximumSnapshots; this.maximumPoints = maximumPoints;
    }
    public void Record(ProjectDocument before, Guid? selectedId = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ClearList(redo);
        Push(undo, before, selectedId);
    }
    /// <summary>Whether one snapshot fits. Older snapshots may be evicted to make room.</summary>
    public bool CanRetainSnapshot(ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return CountPoints(document)<=maximumPoints;
    }
    private static long CountPoints(ProjectDocument document)
    {
        static long Pieces(IEnumerable<PatternPiece> pieces) => pieces.Sum(p => (long)p.Points.Count + (p.Notches?.Count ?? 0));
        var points = Pieces(document.Pieces) + document.GradingRules.Sum(r => (long)r.SourcePoints.Count + r.Deltas.Count);
        // Rotation history is deep-cloned with every undo entry. Count both
        // historical source and placed geometry, including their visual marks.
        if (document.NestingSnapshot?.RotationTrace is { } trace)
            points += Pieces(trace.SourcePieces) + Pieces(trace.LayoutPieces);
        return points;
    }
    public ProjectDocument? Undo(ProjectDocument current) => UndoState(current)?.Document;
    public ProjectDocument? Redo(ProjectDocument current) => RedoState(current)?.Document;
    public DocumentHistoryState? UndoState(ProjectDocument current, Guid? selectedId = null) => Transfer(undo, redo, current, selectedId);
    public DocumentHistoryState? RedoState(ProjectDocument current, Guid? selectedId = null) => Transfer(redo, undo, current, selectedId);
    private DocumentHistoryState? Transfer(LinkedList<Entry> source, LinkedList<Entry> destination, ProjectDocument current, Guid? selectedId)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (source.Last is null) return null;
        var entry = source.Last.Value;
        source.RemoveLast(); StoredPointCount -= entry.PointCount;
        Push(destination, current, selectedId);
        return new(entry.Document, entry.SelectedId);
    }
    private void Push(LinkedList<Entry> target, ProjectDocument document, Guid? selectedId)
    {
        long points = CountPoints(document);
        if (points > maximumPoints)
        {
            CountCapacityDiscard((long)undo.Count + redo.Count + 1);
            Clear(); return;
        }
        target.AddLast(new Entry(document.Clone(), selectedId, points, ++sequence));
        StoredPointCount += points;
        while (undo.Count + redo.Count > maximumSnapshots || StoredPointCount > maximumPoints)
        {
            var oldest = undo.First is null ? redo : redo.First is null ? undo
                : undo.First.Value.Sequence < redo.First.Value.Sequence ? undo : redo;
            StoredPointCount -= oldest.First!.Value.PointCount; oldest.RemoveFirst();
            CountCapacityDiscard(1);
        }
    }
    private void CountCapacityDiscard(long count)
    {
        CapacityDiscardedSnapshots = count > long.MaxValue - CapacityDiscardedSnapshots
            ? long.MaxValue : CapacityDiscardedSnapshots + count;
    }
    private void ClearList(LinkedList<Entry> list)
    {
        foreach (var entry in list) StoredPointCount -= entry.PointCount;
        list.Clear();
    }
    public void Clear() { undo.Clear(); redo.Clear(); StoredPointCount = 0; }
}
