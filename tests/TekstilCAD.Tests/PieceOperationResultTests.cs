using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using static TekstilCAD.Tests.PieceOperationTestData;

namespace TekstilCAD.Tests;

public sealed class PieceOperationResultTests
{
    [Fact]
    public void AFailedResultCarriesNoDocumentAndCannotBeApplied()
    {
        var result = PieceOperations.MoveBy(OneRect(), Guid.NewGuid(), 1, 1);
        Assert.False(result.Succeeded);
        Assert.Null(result.Document);
        Assert.Null(result.Piece);
        Assert.Null(result.PieceId);
        Assert.False(result.ChangesDocument);
        Assert.Equal("", result.Summary);
        Assert.Empty(result.Warnings);
        var ex = Assert.Throws<InvalidOperationException>(() => result.ApplyTo(OneRect()));
        Assert.Equal(result.Error, ex.Message);
    }

    [Fact]
    public void ASuccessfulResultHasNoError()
    {
        var document = OneRect();
        var ok = PieceOperations.MoveBy(document, document.Pieces[0].Id, 1, 1);
        Assert.True(ok.Succeeded);
        Assert.Equal(PieceOperationError.None, ok.ErrorCode);
        Assert.Null(ok.Error);
        Assert.Null(ok.ErrorDetail);
        Assert.False(string.IsNullOrWhiteSpace(ok.Summary));
    }

    [Fact]
    public void ComputingAResultNeverChangesTheSessionUntilItIsApplied()
    {
        var session = new DocumentSession(OneRect());
        var before = session.Document.Clone();
        var result = PieceOperations.MoveBy(session.Document, session.Document.Pieces[0].Id, 50, 50);
        Assert.True(result.ChangesDocument);
        Assert.True(DocumentSession.ContentEquals(before, session.Document));
        Assert.False(session.IsDirty);
        Assert.Equal(0, session.UndoCount);
    }

    [Fact]
    public void ApplyingThroughExecuteMakesTheDocumentEqualToThePreview()
    {
        var session = new DocumentSession(OneRect());
        var result = PieceOperations.MoveBy(session.Document, session.Document.Pieces[0].Id, 50, 50);
        Assert.True(session.Execute(result.ApplyTo, ProjectStorage.Validate));
        Assert.True(session.IsDirty);
        Assert.True(DocumentSession.ContentEquals(result.Document!, session.Document));
    }

    [Fact]
    public void TheSessionDocumentDoesNotShareObjectsWithThePreview()
    {
        var session = new DocumentSession(OneRect());
        var result = PieceOperations.MoveBy(session.Document, session.Document.Pieces[0].Id, 50, 50);
        session.Execute(result.ApplyTo, ProjectStorage.Validate);
        var preview = result.Document!.Clone();
        session.Execute(d => d.Pieces[0].Name = "Başka");
        Assert.True(DocumentSession.ContentEquals(preview, result.Document));
    }

    [Fact]
    public void ApplyingAnOutOfDatePreviewIsRefusedAndRollsBack()
    {
        var session = new DocumentSession(OneRect());
        var preview = PieceOperations.MoveBy(session.Document, session.Document.Pieces[0].Id, 50, 50);
        session.Execute(d => d.Pieces[0].Name = "Başka ad");
        var current = session.Document.Clone();
        var undoCount = session.UndoCount;
        var ex = Assert.Throws<InvalidOperationException>(() => session.Execute(preview.ApplyTo, ProjectStorage.Validate));
        Assert.Equal("Proje, işlem önizlemesinden sonra değişti; işlem uygulanmadı. Komutu yeniden verin.", ex.Message);
        Assert.True(DocumentSession.ContentEquals(current, session.Document));
        Assert.Equal(undoCount, session.UndoCount);
    }

    [Fact]
    public void ThePreviewRemembersTheContentItWasComputedFromEvenIfTheCallerKeepsEditing()
    {
        var document = OneRect();
        var original = document.Clone();
        var preview = PieceOperations.MoveBy(document, document.Pieces[0].Id, 5, 5);
        document.Pieces[0].Name = "Sonradan değişti";
        Assert.Throws<InvalidOperationException>(() => preview.ApplyTo(document));
        var target = original.Clone();
        preview.ApplyTo(target);
        Assert.True(DocumentSession.ContentEquals(preview.Document!, target));
    }

    [Fact]
    public void ApplyingTwiceToTheSameLiveDocumentIsRefusedTheSecondTime()
    {
        var session = new DocumentSession(OneRect());
        var result = PieceOperations.MoveBy(session.Document, session.Document.Pieces[0].Id, 5, 5);
        Assert.True(session.Execute(result.ApplyTo, ProjectStorage.Validate));
        Assert.Throws<InvalidOperationException>(() => session.Execute(result.ApplyTo, ProjectStorage.Validate));
        Assert.Equal(1, session.UndoCount);
    }

    [Fact]
    public void AnUnchangedResultAddsNoUndoStep()
    {
        var session = new DocumentSession(OneRect());
        var result = PieceOperations.Rotate(session.Document, session.Document.Pieces[0].Id, 360);
        Assert.False(result.ChangesDocument);
        Assert.False(session.Execute(result.ApplyTo, ProjectStorage.Validate));
        Assert.Equal(0, session.UndoCount);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void LayersNestingSnapshotAndNameSurviveEveryApply()
    {
        var layer = new Layer { Name = "Astar", Visible = false };
        var document = WithLayers(OneRect(), layer);
        document.Name = "Proje adı";
        var session = new DocumentSession(document);
        var result = PieceOperations.AddRectangle(session.Document, 10, 10);
        session.Execute(result.ApplyTo, ProjectStorage.Validate);
        Assert.Equal("Proje adı", session.Document.Name);
        Assert.Equal(2, session.Document.Layers!.Count);
        Assert.False(session.Document.Layers[1].Visible);
    }

    [Fact]
    public void ADocumentWithACurrentNestingSnapshotStaysValidThroughEveryEdit()
    {
        var document = Doc(Rect("A", 0, 0, 100, 50), Rect("B", 120, 0, 100, 50));
        document.NestingSnapshot = new NestingSnapshot
        {
            FabricWidth = 1500, UsedLength = 300, Gap = 20, EdgeMargin = 0,
            LayoutFingerprint = NestingSnapshot.ComputeLayoutFingerprint(document),
            Placements = [.. document.Pieces.Select((p, i) => new NestingPlacementSnapshot(p.Id, i + 1, p.Id))]
        };
        ProjectStorage.Validate(document);
        var operations = new Func<ProjectDocument, PieceOperationResult>[]
        {
            d => PieceOperations.AddRectangle(d, 10, 10),
            d => PieceOperations.MoveBy(d, d.Pieces[0].Id, 5, 5),
            d => PieceOperations.Rename(d, d.Pieces[0].Id, "Yeni"),
            d => PieceOperations.Delete(d, d.Pieces[1].Id),
            d => PieceOperations.Duplicate(d, d.Pieces[0].Id),
            d => PieceOperations.Rotate(d, d.Pieces[0].Id, 90)
        };
        foreach (var operation in operations) AssertUndoRoundTrip(document, operation);
    }

    [Fact]
    public void ErrorTextsAreTurkishSentencesThatStateWhatToDo()
    {
        var document = OneRect();
        var id = document.Pieces[0].Id;
        var refusals = new[]
        {
            PieceOperations.AddRectangle(document, 0, 1), PieceOperations.AddLine(document, double.NaN), PieceOperations.MoveBy(document, id, double.NaN, 0),
            PieceOperations.MoveTo(document, id, 1e9, 0), PieceOperations.Resize(document, id, null, null), PieceOperations.Rotate(document, id, 45),
            PieceOperations.Rename(document, id, ""), PieceOperations.Duplicate(document, Guid.NewGuid()), PieceOperations.Delete(document, Guid.NewGuid())
        };
        Assert.All(refusals, r =>
        {
            Assert.False(r.Succeeded);
            Assert.False(string.IsNullOrWhiteSpace(r.Error));
            Assert.Matches("[ığüşöçİĞÜŞÖÇ]|\\b(olmalı|girin|yazın|verin|seçin)\\b", r.Error);
        });
    }

    [Fact]
    public void NoOperationChangesItsInputDocument()
    {
        var document = Doc(Rect("A", 0, 0, 10, 10), Rect("B", 50, 0, 10, 10));
        var snapshot = document.Clone();
        var id = document.Pieces[0].Id;
        _ = PieceOperations.AddRectangle(document, 5, 5);
        _ = PieceOperations.AddLine(document, 5);
        _ = PieceOperations.MoveBy(document, id, 1, 1);
        _ = PieceOperations.MoveTo(document, id, 9, 9);
        _ = PieceOperations.Resize(document, id, 3, 3);
        _ = PieceOperations.Rotate(document, id, 90);
        _ = PieceOperations.MirrorVertical(document, id);
        _ = PieceOperations.MirrorHorizontal(document, id);
        _ = PieceOperations.Rename(document, id, "Z");
        _ = PieceOperations.Duplicate(document, id);
        _ = PieceOperations.Delete(document, id);
        Assert.True(DocumentSession.ContentEquals(snapshot, document));
    }

    [Fact]
    public void NullDocumentIsAProgrammingErrorNotARefusal()
    {
        Assert.Throws<ArgumentNullException>(() => PieceOperations.AddRectangle(null!, 1, 1));
        Assert.Throws<ArgumentNullException>(() => PieceOperations.MoveBy(null!, Guid.NewGuid(), 1, 1));
        Assert.Throws<ArgumentNullException>(() => PieceNameSearch.FindPiecesByName(null!, "a"));
    }

    [Fact]
    public void DefaultConstantsMatchTheDocumentedValues()
    {
        Assert.Equal(20, PieceOperations.DefaultGapMm);
        Assert.Equal(20, PieceOperations.DefaultDuplicateOffsetMm);
        Assert.Equal(0.001, PieceOperations.MinimumSideMm);
        Assert.Equal(128, PieceOperations.MaximumNameLength);
        Assert.Equal(20000, PieceOperations.MaximumSideMm);
    }
}
