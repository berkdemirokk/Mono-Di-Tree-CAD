using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DartToolsTests
{
    private static PatternPiece Rectangle(bool clockwise = false)
    {
        var piece = new PatternPiece { Name = "Test", Points = [new(0, 0), new(100, 0), new(100, 100), new(0, 100)] };
        if (clockwise) piece.Points.Reverse();
        return piece;
    }

    [Fact]
    public void AddDartPreservesFabricContourAndAddsInternalLegs()
    {
        var source = Rectangle();
        var result = DartTools.AddDart(source, 0, .4, .6, 20);

        Assert.Equal(source.Points, result.Points);
        Assert.Null(source.Darts);
        Assert.NotNull(result.Darts);
        Assert.Single(result.Darts);
        Assert.Equal((new Point2(40, 0), new Point2(50, 20), new Point2(60, 0)), DartTools.GetLegs(result, result.Darts![0]));
        Assert.Equal(10000, Geometry.SignedArea(result.Points));
    }

    [Fact]
    public void DartRoundTripsInVersionTenAndParticipatesInDocumentEquality()
    {
        var piece = DartTools.AddDart(Rectangle(), 0, .4, .6, 20);
        var document = new ProjectDocument { Pieces = [piece] };
        var json = ProjectStorage.SerializeProject(document);
        Assert.Equal(10,System.Text.Json.JsonDocument.Parse(json).RootElement[0].GetInt32());
        var reopened = ProjectStorage.DeserializeProject(System.Text.Encoding.UTF8.GetBytes(json));
        Assert.True(DocumentSession.ContentEquals(document, reopened));
        Assert.Single(reopened.Pieces.Single().Darts!);
    }

    [Fact]
    public void ProjectWithoutDartsKeepsItsOlderAppleReadableEnvelope()
    {
        var text = ProjectStorage.SerializeProject(new ProjectDocument { Pieces = [Rectangle()] });
        Assert.DoesNotContain("[10,", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Darts\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TranslationKeepsDartAttachedAndUndoRestoresIt()
    {
        var piece = DartTools.AddDart(Rectangle(), 0, .4, .6, 20);
        var document = new ProjectDocument { Pieces = [piece] };
        var moved = PieceOperations.MoveBy(document, piece.Id, 10, 20);
        Assert.True(moved.Succeeded);
        Assert.Equal((new Point2(50, 20), new Point2(60, 40), new Point2(70, 20)),
            DartTools.GetLegs(moved.Piece!, moved.Piece!.Darts![0]));
        var history = new DocumentHistory(); history.Record(document);
        var undone = history.Undo(moved.Document!);
        Assert.NotNull(undone);
        Assert.Equal((new Point2(40, 0), new Point2(50, 20), new Point2(60, 0)),
            DartTools.GetLegs(undone!.Pieces[0], undone.Pieces[0].Darts![0]));
    }

    [Fact]
    public void RotationAndMirrorKeepInternalDartAnchoredToItsEdge()
    {
        var piece=DartTools.AddDart(Rectangle(),0,.4,.6,20);
        var document=new ProjectDocument { Pieces=[piece] };
        var rotated=PieceOperations.Rotate(document,piece.Id,90);
        Assert.True(rotated.Succeeded);
        Assert.Single(rotated.Piece!.Darts!);
        var rotatedLegs=DartTools.GetLegs(rotated.Piece,rotated.Piece.Darts![0]);
        Assert.True(rotatedLegs.Start.IsFinite&&rotatedLegs.Apex.IsFinite&&rotatedLegs.End.IsFinite);
        var mirrored=PieceOperations.MirrorVertical(rotated.Document!,piece.Id);
        Assert.True(mirrored.Succeeded);
        Assert.Single(mirrored.Piece!.Darts!);
        ProjectStorage.Validate(mirrored.Document!);
    }

    [Fact]
    public void DartLegsPointInsideForEitherContourWinding()
    {
        var piece = Rectangle(clockwise: true);
        var result = DartTools.AddDart(piece, 0, .4, .6, 20);
        Assert.Equal((new Point2(40, 100), new Point2(50, 80), new Point2(60, 100)), DartTools.GetLegs(result, result.Darts![0]));
    }

    [Theory]
    [InlineData(.5, .5, 10)]
    [InlineData(.7, .4, 10)]
    [InlineData(.1, .2, 120)]
    public void InvalidOrOutsideDartIsRejected(double start, double end, double depth)
        => Assert.ThrowsAny<ArgumentException>(() => DartTools.AddDart(Rectangle(), 0, start, end, depth));

    [Fact]
    public void CancellationDoesNotAddPartialDart()
    {
        var piece = Rectangle();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => DartTools.AddDart(piece, 0, .4, .6, 20, cancellation.Token));
        Assert.Null(piece.Darts);
    }
}
