using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DocumentSessionTests
{
    private static ProjectDocument Example() => new()
    {
        Name = "A",
        Pieces = [new() { Name = "Ön", Points = [new(0, 0), new(10, 0), new(0, 10)] }]
    };

    [Fact]
    public void LoadAndConstructorDetachCallerDocuments()
    {
        var input = Example();
        var session = new DocumentSession(input);
        input.Pieces[0].Points[0] = new(99, 99);
        Assert.Equal(new Point2(0, 0), session.Document.Pieces[0].Points[0]);
        session.Execute(d => d.Name = "edited");
        var replacement = Example();
        session.Load(replacement, ProjectStorage.Validate);
        replacement.Name = "outside";
        replacement.Pieces.Clear();
        Assert.Equal("A", session.Document.Name);
        Assert.Single(session.Document.Pieces);
        Assert.False(session.IsDirty);
        Assert.Equal(0, session.UndoCount);
    }

    [Fact]
    public void CommitDetachesAddedListsAndRetainedWorkingDocument()
    {
        var session = new DocumentSession();
        var points = new List<Point2> { new(0, 0), new(10, 0), new(0, 10) };
        ProjectDocument? retained = null;
        session.Execute(d => { retained = d; d.Pieces.Add(new() { Points = points }); }, ProjectStorage.Validate);
        points[0] = new(77, 77);
        retained!.Name = "outside";
        Assert.Equal(new Point2(0, 0), session.Document.Pieces[0].Points[0]);
        Assert.NotEqual("outside", session.Document.Name);
    }

    [Fact]
    public void SavepointTracksUndoRedoAndNewBranches()
    {
        var session = new DocumentSession(Example());
        session.MarkSaved();
        session.Execute(d => d.Name = "B");
        Assert.True(session.IsDirty);
        Assert.True(session.Undo()); Assert.False(session.IsDirty);
        Assert.True(session.Redo()); Assert.True(session.IsDirty);
        session.MarkSaved();
        Assert.False(session.IsDirty);
        session.Undo(); Assert.True(session.IsDirty);
        session.Redo(); Assert.False(session.IsDirty);
        session.Undo();
        session.Execute(d => d.Name = "C");
        Assert.True(session.IsDirty);
        Assert.False(session.Redo());
    }

    [Fact]
    public void UnchangedEditPreservesRedoAndCleanState()
    {
        var session = new DocumentSession(Example());
        session.Execute(d => d.Name = "B");
        session.Undo();
        Assert.False(session.Execute(d => d.Name = "A"));
        Assert.False(session.IsDirty);
        Assert.Equal(1, session.RedoCount);
        Assert.True(session.Redo());
        Assert.Equal("B", session.Document.Name);
    }

    [Fact]
    public void FailedValidationRestoresDocumentSelectionHistoryAndSavepoint()
    {
        var session = new DocumentSession(Example());
        var id = session.Document.Pieces[0].Id;
        session.SelectedId = id;
        Assert.Throws<InvalidDataException>(() => session.Execute(d =>
        {
            d.Pieces[0].Points[0] = new(double.NaN, 0);
            session.SelectedId = null;
        }, ProjectStorage.Validate));
        Assert.Equal(new Point2(0, 0), session.Document.Pieces[0].Points[0]);
        Assert.Equal(id, session.SelectedId);
        Assert.False(session.IsDirty);
        Assert.Equal(0, session.UndoCount);
    }

    [Fact]
    public void FailedEditDoesNotClearExistingRedo()
    {
        var session = new DocumentSession(Example());
        session.Execute(d => d.Name = "B"); session.Undo();
        Assert.Throws<InvalidOperationException>(() => session.Execute(d =>
        {
            d.Name = "partial";
            throw new InvalidOperationException("failed");
        }));
        Assert.Equal("A", session.Document.Name);
        Assert.Equal(1, session.RedoCount);
        session.Redo(); Assert.Equal("B", session.Document.Name);
    }

    [Fact]
    public void UndoRestoresDeletedSelectionAndRedoClearsIt()
    {
        var session = new DocumentSession(Example());
        var id = session.Document.Pieces[0].Id;
        session.SelectedId = id;
        session.Execute(d => d.Pieces.Clear());
        Assert.Null(session.SelectedId);
        session.Undo(); Assert.Equal(id, session.SelectedId);
        session.Redo(); Assert.Null(session.SelectedId);
        session.SelectedId = Guid.NewGuid(); Assert.Null(session.SelectedId);
    }

    [Fact]
    public void AddingUndoingAndRedoingRestoresAppropriateSelection()
    {
        var session = new DocumentSession();
        var piece = Example().Pieces[0];
        session.Execute(d => { d.Pieces.Add(piece); session.SelectedId = piece.Id; });
        session.Undo(); Assert.Null(session.SelectedId);
        session.Redo(); Assert.Equal(piece.Id, session.SelectedId);
    }

    [Fact]
    public void SavedContentSurvivesHistoryEvictionWithoutFalseCleanState()
    {
        var session = new DocumentSession(Example(), maximumSnapshots: 1);
        session.Execute(d => d.Name = "B"); session.Execute(d => d.Name = "C");
        session.Undo();
        Assert.Equal("B", session.Document.Name); Assert.True(session.IsDirty);
        Assert.False(session.Undo());
        session.Execute(d => d.Name = "A");
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void PointBudgetRemainsBoundedAcrossUndoRedo()
    {
        var session = new DocumentSession(Example(), maximumPoints: 6);
        session.Execute(d => d.Name = "B");
        session.Execute(d => d.Name = "C");
        session.Execute(d => d.Name = "D");
        session.Undo(); session.Undo(); session.Redo();
        Assert.InRange(session.StoredPointCount, 0, 6);
        Assert.InRange(session.UndoCount + session.RedoCount, 0, 2);
    }

    [Fact]
    public void ExplicitUnsavedStateLastsUntilDurableSaveIsMarked()
    {
        var session = new DocumentSession(Example());
        session.MarkUnsaved();
        session.Execute(d => d.Name = "B"); session.Undo();
        Assert.True(session.IsDirty);
        session.MarkSaved(); Assert.False(session.IsDirty);
    }

    [Fact]
    public void FailedLoadPreservesCurrentSession()
    {
        var session = new DocumentSession(Example());
        session.Execute(d => d.Name = "B");
        var invalid = Example(); invalid.Pieces[0].Points[0] = new(double.NaN, 0);
        Assert.Throws<InvalidDataException>(() => session.Load(invalid, ProjectStorage.Validate));
        Assert.Equal("B", session.Document.Name);
        Assert.True(session.IsDirty); Assert.Equal(1, session.UndoCount);
    }

    [Fact]
    public void LoadValidationCannotChangeExistingSavepointOrHistory()
    {
        var session = new DocumentSession(Example());
        var id = session.Document.Pieces[0].Id;
        session.SelectedId = id;
        session.Execute(d => d.Name = "edited");

        Assert.Throws<InvalidOperationException>(() => session.Load(Example(), _ => session.MarkSaved()));
        Assert.Equal("edited", session.Document.Name);
        Assert.Equal(id, session.SelectedId);
        Assert.True(session.IsDirty);
        Assert.Equal(1, session.UndoCount);
        Assert.True(session.Undo());
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void LoadValidationCannotRunNestedEditAndGuardResetsAfterFailure()
    {
        var session = new DocumentSession(Example());
        Assert.Throws<InvalidOperationException>(() => session.Load(Example(), _ =>
            session.Execute(d => d.Name = "nested")));
        Assert.Equal("A", session.Document.Name);
        Assert.True(session.Execute(d => d.Name = "later"));
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void NestedStateChangesAreRejectedAndRolledBack()
    {
        var session = new DocumentSession(Example());
        Assert.Throws<InvalidOperationException>(() => session.Execute(d =>
        {
            d.Name = "partial";
            session.MarkSaved();
        }));
        Assert.Equal("A", session.Document.Name);
        Assert.False(session.IsDirty); Assert.Equal(0, session.UndoCount);
    }

    [Fact]
    public void SubMillimeterContentChangeIsAnEdit()
    {
        var session = new DocumentSession(Example());
        Assert.True(session.Execute(d => d.Pieces[0].Points[0] = new(.00001, 0)));
        Assert.True(session.IsDirty);
        session.Undo(); Assert.False(session.IsDirty);
    }
}
