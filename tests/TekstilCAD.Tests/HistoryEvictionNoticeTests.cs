using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public sealed class HistoryEvictionNoticeTests
{
    private static ProjectDocument Document(string name, int points = 3) => new()
    {
        Name = name,
        Pieces = [new PatternPiece { Points = Enumerable.Range(0, points).Select(i => new Point2(i, 0)).ToList() }]
    };

    [Fact]
    public void SnapshotLimitCountsOnlyDiscardedOldestEntries()
    {
        var history = new DocumentHistory(maximumSnapshots: 2);
        Assert.Equal(0, history.CapacityDiscardedSnapshots);
        history.Record(Document("A"));
        history.Record(Document("B"));
        Assert.Equal(0, history.CapacityDiscardedSnapshots);
        history.Record(Document("C"));
        Assert.Equal(1, history.CapacityDiscardedSnapshots);
        Assert.Equal("C", history.Undo(Document("D"))!.Name);
        Assert.Equal("B", history.Undo(Document("C"))!.Name);
        Assert.Null(history.Undo(Document("B")));
        Assert.Equal(1, history.CapacityDiscardedSnapshots);
    }

    [Fact]
    public void PointLimitCountsActualEvictionAndKeepsRetainedHistoryUsable()
    {
        var history = new DocumentHistory(maximumPoints: 6);
        history.Record(Document("A"));
        history.Record(Document("B"));
        history.Record(Document("C"));
        Assert.Equal(1, history.CapacityDiscardedSnapshots);
        Assert.Equal(6, history.StoredPointCount);
        Assert.Equal("C", history.Undo(Document("D"))!.Name);
        Assert.Equal("B", history.Undo(Document("C"))!.Name);
    }

    [Fact]
    public void TransferCountsBudgetEvictionButNotOrdinaryMovement()
    {
        var history = new DocumentHistory(maximumPoints: 9);
        history.Record(Document("A")); history.Record(Document("B")); history.Record(Document("C"));
        var restored = history.Undo(Document("D", 6))!;
        Assert.Equal("C", restored.Name);
        Assert.Equal(1, history.CapacityDiscardedSnapshots);
        Assert.Equal(1, history.UndoCount);
        Assert.Equal(1, history.RedoCount);
        Assert.Equal("D", history.Redo(restored)!.Name);
        Assert.Equal(1, history.CapacityDiscardedSnapshots);
    }

    [Fact]
    public void NormalRedoBranchClearAndExplicitClearAreNotCapacityEviction()
    {
        var history = new DocumentHistory();
        history.Record(Document("A"));
        var restored = history.Undo(Document("B"))!;
        history.Record(restored);
        Assert.Equal(0, history.RedoCount);
        Assert.Equal(0, history.CapacityDiscardedSnapshots);
        history.Clear();
        Assert.Equal(0, history.CapacityDiscardedSnapshots);
    }

    [Fact]
    public void OversizeSnapshotCountsLostExistingAndUnstoredIncomingSnapshot()
    {
        var history = new DocumentHistory(maximumPoints: 6);
        history.Record(Document("A")); history.Record(Document("B"));
        history.Record(Document("too large", 7));
        Assert.Equal(3, history.CapacityDiscardedSnapshots);
        Assert.Equal(0, history.UndoCount);
        Assert.Equal(0, history.RedoCount);
        Assert.Equal(0, history.StoredPointCount);
    }

    [Fact]
    public void OversizeTransferCountsLostEntriesAndUnavailableOppositeSnapshot()
    {
        var history = new DocumentHistory(maximumPoints: 6);
        history.Record(Document("A")); history.Record(Document("B"));
        var restored = history.Undo(Document("large current", 7));
        Assert.Equal("B", restored!.Name);
        Assert.Equal(2, history.CapacityDiscardedSnapshots);
        Assert.Equal(0, history.UndoCount);
        Assert.Equal(0, history.RedoCount);
    }

    [Fact]
    public void SessionIgnoresNoOpFailureAndLoadForMonotonicCapacityCount()
    {
        var session = new DocumentSession(Document("A"), maximumSnapshots: 1);
        Assert.Equal(0, session.CapacityDiscardedSnapshots);
        Assert.False(session.Execute(_ => { }));
        Assert.Throws<InvalidOperationException>(() => session.Execute(d => { d.Name = "failed"; throw new InvalidOperationException(); }));
        Assert.Equal("A", session.Document.Name);
        Assert.Equal(0, session.CapacityDiscardedSnapshots);
        Assert.True(session.Execute(d => d.Name = "B"));
        Assert.True(session.Execute(d => d.Name = "C"));
        Assert.Equal(1, session.CapacityDiscardedSnapshots);
        session.Load(Document("new"));
        Assert.Equal(1, session.CapacityDiscardedSnapshots);
        Assert.Equal(0, session.UndoCount);
        Assert.True(session.Execute(d => d.Name = "next"));
        Assert.Equal(1, session.CapacityDiscardedSnapshots);
    }
}
