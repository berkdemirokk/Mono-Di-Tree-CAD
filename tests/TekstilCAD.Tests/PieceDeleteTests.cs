using TekstilCAD.Core;
using static TekstilCAD.Tests.PieceOperationTestData;

namespace TekstilCAD.Tests;

public sealed class PieceDeleteTests
{
    [Fact]
    public void DeleteRemovesOnlyThatPieceAndKeepsTheOrderOfTheRest()
    {
        var document = Doc(Rect("A", 0, 0, 1, 1), Rect("B", 5, 0, 1, 1), Rect("C", 10, 0, 1, 1));
        var result = Run(document, d => PieceOperations.Delete(d, d.Pieces[1].Id));
        Assert.Equal(["A", "C"], result.Document!.Pieces.Select(p => p.Name));
        Assert.Null(result.PieceId);
        Assert.Null(result.Piece);
        Assert.Equal("“B” silindi.", result.Summary);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void DeletingTheLastPieceLeavesAnEmptyValidDocument()
    {
        var result = Run(OneRect(), d => PieceOperations.Delete(d, d.Pieces[0].Id));
        Assert.Empty(result.Document!.Pieces);
    }

    [Fact]
    public void DeleteTakesTheGradingRuleOfThePieceAndReportsIt()
    {
        var a = Rect("A", 0, 0, 10, 10);
        var b = Rect("B", 50, 0, 10, 10);
        var document = Doc(a, b);
        foreach (var piece in document.Pieces)
            document.GradingRules.Add(new GradingRuleSet { SourcePieceId = piece.Id, SourceClosed = true, SourcePoints = [.. piece.Points],
                Deltas = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)] });
        var result = Run(document, d => PieceOperations.Delete(d, a.Id));
        Assert.Single(result.Document!.GradingRules);
        Assert.Equal(b.Id, result.Document.GradingRules[0].SourcePieceId);
        Assert.Equal(["Parçaya bağlı 1 serileme kuralı da silindi."], result.Warnings);
    }

    [Fact]
    public void UndoBringsBackThePieceAndItsRule()
    {
        var piece = Rect("A", 0, 0, 10, 10);
        var document = Doc(piece, Rect("B", 50, 0, 10, 10));
        document.GradingRules.Add(new GradingRuleSet { SourcePieceId = piece.Id, SourceClosed = true, SourcePoints = [.. piece.Points],
            Deltas = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)] });
        AssertUndoRoundTrip(document, d => PieceOperations.Delete(d, piece.Id));
    }

    [Fact]
    public void APieceWithoutPointsCanStillBeDeleted()
    {
        var broken = new PatternPiece { Name = "Boş", Points = [] };
        var result = PieceOperations.Delete(Doc(broken, Rect("A", 0, 0, 1, 1)), broken.Id);
        Assert.True(result.Succeeded);
        Assert.Single(result.Document!.Pieces);
    }

    [Fact]
    public void UnknownPieceIsRefused()
    {
        AssertRefused(PieceOperations.Delete(OneRect(), Guid.NewGuid()), PieceOperationError.PieceNotFound,
            "Parça bulunamadı; silinmiş olabilir. Parçayı yeniden seçip komutu tekrar verin.");
    }

    [Fact]
    public void DeletingTwiceRefusesTheSecondTime()
    {
        var document = OneRect();
        var id = document.Pieces[0].Id;
        var first = Run(document, d => PieceOperations.Delete(d, id));
        Assert.False(PieceOperations.Delete(first.Document!, id).Succeeded);
    }

    [Fact]
    public void PieceOnALockedLayerIsRefused()
    {
        var layer = new Layer { Name = "Kilitli", Locked = true };
        var document = WithLayers(OneRect(), layer);
        document.Pieces[0].LayerId = layer.Id;
        AssertRefused(PieceOperations.Delete(document, document.Pieces[0].Id), PieceOperationError.LayerLocked,
            "“Ön beden” kilitli bir katmanda; değiştirilemez. Önce katmanın kilidini açın.");
    }
}
