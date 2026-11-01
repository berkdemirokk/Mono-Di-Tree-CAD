using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public sealed class RotatedTraceHistoryBudgetTests
{
    private static ProjectDocument Document()
    {
        static PatternPiece Piece() => new()
        {
            Points=[new(0,0),new(10,0),new(0,10)],
            Notches=[new NotchMark { VertexIndex=1,Position=new(10,0) }]
        };
        return new ProjectDocument
        {
            Pieces=[Piece()],
            NestingSnapshot=new NestingSnapshot
            {
                RotationTrace=new RotatedNestingTrace { SourcePieces=[Piece()],LayoutPieces=[Piece()] }
            }
        };
    }

    [Fact]
    public void BudgetIncludesBothHistoricalContoursAndMarks()
    {
        var document=Document(); // 3 points and 1 mark each in visible/source/layout.
        var exact=new DocumentHistory(maximumPoints:12);
        Assert.True(exact.CanRetainSnapshot(document));
        exact.Record(document);
        Assert.Equal(12,exact.StoredPointCount);
        var smaller=new DocumentHistory(maximumPoints:11);
        Assert.False(smaller.CanRetainSnapshot(document));
        smaller.Record(document);
        Assert.Equal(0,smaller.UndoCount);
        Assert.Equal(1,smaller.CapacityDiscardedSnapshots);
    }

    [Fact]
    public void HistoryEvictsOldestWhenDuplicatedTraceExceedsBudget()
    {
        var original=Document();var session=new DocumentSession(original,maximumPoints:23);
        Assert.True(session.Execute(d=>d.Name="first"));
        Assert.True(session.Execute(d=>d.Name="second"));
        Assert.Equal(1,session.UndoCount);
        Assert.Equal(12,session.StoredPointCount);
        Assert.Equal(1,session.CapacityDiscardedSnapshots);
        Assert.True(session.Undo());Assert.Equal("first",session.Document.Name);
        Assert.True(session.Redo());Assert.Equal("second",session.Document.Name);
        Assert.True(RotatedNestingTrace.ContentEquals(original.NestingSnapshot!.RotationTrace!,session.Document.NestingSnapshot!.RotationTrace!));
    }
}
