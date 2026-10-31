using System.Text.Json;
using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public class ProductionNestingAdversarialTests
{
    private static PatternPiece Piece(int quantity = 1, string material = "Pamuk") => new()
    {
        Name = "Parça", Points = [new(0, 0), new(20, 0), new(20, 10), new(0, 10)],
        Metadata = new() { Role = "Kesim", Quantity = quantity, Material = material, Size = "M" }
    };

    [Fact]
    public void MultipleSourcesMapEveryPlacedCopyAndLeaveTheWholeDocumentUnchanged()
    {
        var a = Piece(2); var b = Piece(3); var ignored = Piece();
        var document = new ProjectDocument { Pieces = [a, b, ignored] };
        var before = JsonSerializer.Serialize(document);
        var result = ProductionNestingPlan.Arrange(document, [a.Id, b.Id], 200, 2);
        Assert.Equal(before, JsonSerializer.Serialize(document));
        Assert.Equal(5, result.Placements.Count);
        Assert.Equal(5, result.Placements.Select(p => p.PlacedPieceId).Distinct().Count());
        Assert.Equal(new[] { 1, 2 }, result.Placements.Where(p => p.SourcePieceId == a.Id).Select(p => p.CopyNumber));
        Assert.Equal(new[] { 1, 2, 3 }, result.Placements.Where(p => p.SourcePieceId == b.Id).Select(p => p.CopyNumber));
        Assert.Equal(result.Placements.Select(p => p.PlacedPieceId).Order(), result.Nesting.Layout.Pieces.Select(p => p.Id).Order());
        Assert.DoesNotContain(result.Placements, p => p.SourcePieceId == ignored.Id);
        result.Nesting.Layout.Pieces[0].Metadata!.Material = "Değiştirildi";
        Assert.Equal(before, JsonSerializer.Serialize(document));
    }

    [Fact]
    public void InvalidSelectionAndDuplicateSourceIdentityAreRejectedWithoutMutation()
    {
        var a = Piece(); var duplicate = Piece(); duplicate.Id = a.Id;
        var document = new ProjectDocument { Pieces = [a, duplicate] };
        var before = JsonSerializer.Serialize(document);
        Assert.Throws<ArgumentException>(() => ProductionNestingPlan.Arrange(document, [Guid.Empty], 100));
        Assert.Throws<ArgumentException>(() => ProductionNestingPlan.Arrange(document, [a.Id], 100));
        Assert.Throws<ArgumentException>(() => ProductionNestingPlan.Arrange(document, [a.Id, Guid.NewGuid()], 100));
        Assert.Equal(before, JsonSerializer.Serialize(document));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100001)]
    public void InvalidQuantitiesAreRejected(int quantity)
    {
        var piece = Piece(quantity); var document = new ProjectDocument { Pieces = [piece] };
        var before = JsonSerializer.Serialize(document);
        Assert.Throws<ArgumentException>(() => ProductionNestingPlan.Arrange(document, [piece.Id], 100));
        Assert.Equal(before, JsonSerializer.Serialize(document));
    }

    [Fact]
    public void MixedMaterialAndInvalidContourAreRejectedWithoutMutation()
    {
        var a = Piece(); var b = Piece(material: "Yün"); var document = new ProjectDocument { Pieces = [a, b] };
        var before = JsonSerializer.Serialize(document);
        Assert.Throws<ArgumentException>(() => ProductionNestingPlan.Arrange(document, [a.Id, b.Id], 100));
        Assert.Equal(before, JsonSerializer.Serialize(document));
        a.Points = [new(0, 0), new(10, 10), new(0, 10), new(10, 0)];
        before = JsonSerializer.Serialize(document);
        Assert.Throws<ArgumentException>(() => ProductionNestingPlan.Arrange(document, [a.Id], 100));
        Assert.Equal(before, JsonSerializer.Serialize(document));
    }
}
