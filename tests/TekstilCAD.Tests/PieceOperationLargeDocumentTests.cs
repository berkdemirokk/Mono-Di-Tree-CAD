using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using static TekstilCAD.Tests.PieceOperationTestData;

namespace TekstilCAD.Tests;

public sealed class PieceOperationLargeDocumentTests
{
    /// <summary>250 rectangles of 100 x 60 mm in a grid of 25 columns, named "Parça 1" .. "Parça 250".</summary>
    private static ProjectDocument Grid()
    {
        var document = new ProjectDocument();
        for (var i = 0; i < 250; i++)
            document.Pieces.Add(Rect($"Parça {i + 1}", 120 * (i % 25), 100 * (i / 25), 100, 60));
        return document;
    }

    private static void AssertOnlyOnePieceDiffers(ProjectDocument before, ProjectDocument after, int index)
    {
        Assert.Equal(before.Pieces.Count, after.Pieces.Count);
        for (var i = 0; i < before.Pieces.Count; i++)
        {
            if (i == index) continue;
            Assert.Equal(before.Pieces[i].Id, after.Pieces[i].Id);
            Assert.Equal(before.Pieces[i].Points, after.Pieces[i].Points);
            Assert.Equal(before.Pieces[i].Name, after.Pieces[i].Name);
        }
    }

    [Fact]
    public void AddingToTwoHundredFiftyPiecesGoesRightOfTheRightmostColumnAtItsTop()
    {
        var document = Grid();
        var result = Run(document, d => PieceOperations.AddRectangle(d, 50, 50));
        Assert.Equal(251, result.Document!.Pieces.Count);
        Assert.Equal(new Point2(3000, 0), result.Piece!.Points[0]);
        Assert.Equal("Parça 251", result.Piece.Name);
        AssertOnlyOnePieceDiffers(document, result.Document, -1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(137)]
    [InlineData(249)]
    public void MoveTouchesOnlyTheChosenPiece(int index)
    {
        var document = Grid();
        var id = document.Pieces[index].Id;
        var result = Run(document, d => PieceOperations.MoveBy(d, id, 7.5, -2.5));
        AssertOnlyOnePieceDiffers(document, result.Document!, index);
        Assert.Equal(document.Pieces[index].Points.Select(p => new Point2(p.X + 7.5, p.Y - 2.5)), result.Document!.Pieces[index].Points);
    }

    [Fact]
    public void EveryOperationWorksOnTheLargeDocumentAndUndoesInOneStep()
    {
        var document = Grid();
        var operations = new Func<ProjectDocument, PieceOperationResult>[]
        {
            d => PieceOperations.AddRectangle(d, 40, 40),
            d => PieceOperations.AddLine(d, 40),
            d => PieceOperations.MoveBy(d, d.Pieces[111].Id, 3, 3),
            d => PieceOperations.MoveTo(d, d.Pieces[111].Id, -50, -50),
            d => PieceOperations.Resize(d, d.Pieces[111].Id, 10, 20),
            d => PieceOperations.Rotate(d, d.Pieces[111].Id, 90),
            d => PieceOperations.MirrorVertical(d, d.Pieces[111].Id),
            d => PieceOperations.MirrorHorizontal(d, d.Pieces[111].Id),
            d => PieceOperations.Rename(d, d.Pieces[111].Id, "Yeni ad"),
            d => PieceOperations.Duplicate(d, d.Pieces[111].Id),
            d => PieceOperations.Delete(d, d.Pieces[111].Id)
        };
        foreach (var operation in operations) AssertUndoRoundTrip(document, operation);
    }

    [Fact]
    public void ManyOperationsInARowOnOneSessionStayValidAndFullyUndoable()
    {
        var session = new DocumentSession(Grid());
        var original = session.Document.Clone();
        var applied = 0;
        for (var i = 0; i < 40; i++)
        {
            var id = session.Document.Pieces[(i * 17) % session.Document.Pieces.Count].Id;
            var result = (i % 4) switch
            {
                0 => PieceOperations.MoveBy(session.Document, id, i, -i),
                1 => PieceOperations.Rotate(session.Document, id, 90),
                2 => PieceOperations.MirrorVertical(session.Document, id),
                _ => PieceOperations.Rename(session.Document, id, $"Ad {i}")
            };
            Assert.True(result.Succeeded, result.Error);
            if (session.Execute(result.ApplyTo, ProjectStorage.Validate)) applied++;
        }
        Assert.Equal(applied, session.UndoCount);
        while (session.Undo()) { }
        Assert.True(DocumentSession.ContentEquals(original, session.Document));
    }

    [Fact]
    public void TwoHundredFiftyRectanglesCanBeAddedOneByOne()
    {
        var document = new ProjectDocument();
        for (var i = 0; i < 250; i++) document = Run(document, d => PieceOperations.AddRectangle(d, 10, 10)).Document!;
        Assert.Equal(250, document.Pieces.Count);
        Assert.Equal(250, document.Pieces.Select(p => p.Id).Distinct().Count());
        Assert.Equal(250, document.Pieces.Select(p => p.Name).Distinct().Count());
        // Each new piece sits 20 mm right of the previous one.
        Assert.Equal(new Point2(30 * 249, 0), document.Pieces[249].Points[0]);
    }

    [Fact]
    public void SearchFindsAllMatchesInTheLargeDocumentInOrder()
    {
        var document = Grid();
        var expected = document.Pieces.Select((p, i) => (p, i)).Where(t => t.p.Name.Contains("Parça 2", StringComparison.Ordinal)).Select(t => t.i).ToArray();
        var found = PieceNameSearch.FindPiecesByName(document, "parça 2").Select(m => m.Index).ToArray();
        Assert.Equal(expected, found);
        Assert.Equal(expected, PieceNameSearch.FindPiecesByNameFolded(document, "PARCA 2").Select(m => m.Index).ToArray());
        Assert.Equal(62, found.Length);
        Assert.Single(PieceNameSearch.FindPiecesByName(document, "Parça 250"));
    }

    [Fact]
    public void AllIdsStayUniqueAfterManyDuplicates()
    {
        var document = Grid();
        for (var i = 0; i < 25; i++) document = Run(document, d => PieceOperations.Duplicate(d, d.Pieces[i * 10].Id)).Document!;
        Assert.Equal(275, document.Pieces.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void DefaultPlacementSkipsOverEveryPieceEvenWhenTheLastOneIsFarLeft()
    {
        var document = Grid();
        document.Pieces.Add(Rect("Sol uçta", -5000, -5000, 10, 10));
        var result = Run(document, d => PieceOperations.AddRectangle(d, 5, 5));
        Assert.Equal(3000, result.Piece!.Points[0].X);
    }
}
