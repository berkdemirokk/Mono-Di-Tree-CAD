using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public sealed class RotatedTraceHistoryBudgetTests
{
    private static ProjectDocument Document()
    {
        static PatternPiece Piece() => new()
        {
            Points=[new(0,0),new(10,0),new(0,10)],
            Notches=[new NotchMark { VertexIndex=1,Position=new(10,0) }],
            Grainline=new Grainline { Start=new(1,1),End=new(2,2) }
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
    public void BudgetIncludesBothHistoricalContoursMarksAndGrainlines()
    {
        var document=Document(); // 3 points, 1 mark and 2 grainline ends in each visible/source/layout piece.
        var exact=new DocumentHistory(maximumPoints:18);
        Assert.True(exact.CanRetainSnapshot(document));
        exact.Record(document);
        Assert.Equal(18,exact.StoredPointCount);
        var smaller=new DocumentHistory(maximumPoints:17);
        Assert.False(smaller.CanRetainSnapshot(document));
        smaller.Record(document);
        Assert.Equal(0,smaller.UndoCount);
        Assert.Equal(1,smaller.CapacityDiscardedSnapshots);
    }

    [Fact]
    public void VisibleGrainlineEndsCountWithoutRotationHistory()
    {
        var source=Document();source.NestingSnapshot=null;
        var exact=new DocumentSession(source,maximumPoints:6);
        var tooSmall=new DocumentSession(source,maximumPoints:5);
        Assert.True(exact.CanRetainCurrentUndoSnapshot);
        Assert.False(tooSmall.CanRetainCurrentUndoSnapshot);
        Assert.True(exact.Execute(d=>d.Name="changed"));
        Assert.True(exact.Undo());Assert.True(DocumentSession.ContentEquals(source,exact.Document));
        Assert.True(exact.Redo());Assert.Equal("changed",exact.Document.Name);
    }

    [Fact]
    public void HistoryEvictsOldestWhenDuplicatedTraceExceedsBudget()
    {
        var original=Document();var session=new DocumentSession(original,maximumPoints:35);
        Assert.True(session.Execute(d=>d.Name="first"));
        Assert.True(session.Execute(d=>d.Name="second"));
        Assert.Equal(1,session.UndoCount);
        Assert.Equal(18,session.StoredPointCount);
        Assert.Equal(1,session.CapacityDiscardedSnapshots);
        Assert.True(session.Undo());Assert.Equal("first",session.Document.Name);
        Assert.True(session.Redo());Assert.Equal("second",session.Document.Name);
        Assert.True(RotatedNestingTrace.ContentEquals(original.NestingSnapshot!.RotationTrace!,session.Document.NestingSnapshot!.RotationTrace!));
    }
}
