using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public sealed class HistoryTests
{
    private static ProjectDocument Document(string name, int points=3) => new() { Name=name, Pieces=[new() { Points=Enumerable.Range(0,points).Select(i=>new Point2(i,0)).ToList() }] };
    [Fact]
    public void UndoRedoPreserveChronologyAndCloneSource()
    {
        var history=new DocumentHistory(); var first=Document("A"); history.Record(first);
        first.Name="mutated"; first.Pieces[0].Points[0]=new(99,99);
        var restored=history.Undo(Document("B"))!;
        Assert.Equal("A",restored.Name); Assert.Equal(new Point2(0,0),restored.Pieces[0].Points[0]);
        Assert.Equal("B",history.Redo(restored)!.Name);
    }
    [Fact]
    public void NewEditClearsRedoBranch()
    {
        var history=new DocumentHistory(); history.Record(Document("A")); var restored=history.Undo(Document("B"))!;
        history.Record(restored); Assert.Equal(0,history.RedoCount); Assert.Null(history.Redo(Document("C")));
    }
    [Fact]
    public void SnapshotCountDropsOldestCheckpoint()
    {
        var history=new DocumentHistory(maximumSnapshots:2);
        history.Record(Document("A"));history.Record(Document("B"));history.Record(Document("C"));
        Assert.Equal(2,history.UndoCount);
        var c=history.Undo(Document("D"))!; Assert.Equal("C",c.Name);
        var b=history.Undo(c)!; Assert.Equal("B",b.Name); Assert.Null(history.Undo(b));
    }
    [Fact]
    public void PointBudgetAppliesAcrossUndoAndRedo()
    {
        var history=new DocumentHistory(maximumPoints:9);
        history.Record(Document("A"));history.Record(Document("B"));history.Record(Document("C"));
        var current=history.Undo(Document("D",6))!;
        Assert.InRange(history.StoredPointCount,0,9);
        Assert.Equal(1,history.UndoCount);Assert.Equal(1,history.RedoCount);
        history.Record(current); Assert.InRange(history.StoredPointCount,0,9);
    }
    [Fact]
    public void OversizedSnapshotClearsDiscontinuousHistory()
    {
        var history=new DocumentHistory(maximumPoints:3);history.Record(Document("A"));history.Record(Document("large",4));
        Assert.Equal(0,history.UndoCount);Assert.Equal(0,history.StoredPointCount);
    }
    [Fact]
    public void ClearReleasesAllCheckpoints()
    {
        var history=new DocumentHistory();history.Record(Document("A"));history.Undo(Document("B"));history.Clear();
        Assert.Equal(0,history.StoredPointCount);Assert.Equal(0,history.UndoCount);Assert.Equal(0,history.RedoCount);
    }
}
