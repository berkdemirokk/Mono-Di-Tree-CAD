using System.Text.Json;
using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public class ProductionNestingResourceLimitTests
{
    private static PatternPiece Piece(int quantity = 1) => new()
    {
        Name = "Kalıp", Points = [new(0,0), new(10,0), new(10,10), new(0,10)],
        Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = quantity }
    };

    [Fact]
    public void HugeDeclaredQuantityIsRejectedBeforeExpansionAndDoesNotAlterSource()
    {
        var piece = Piece(100000);
        var document = new ProjectDocument { Pieces = [piece] };
        var before = JsonSerializer.Serialize(document);
        var error = Assert.Throws<ArgumentException>(() => ProductionNestingPlan.Arrange(document, [piece.Id], 100));
        Assert.Contains("sınır", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, JsonSerializer.Serialize(document));
    }

    [Fact]
    public void QuadraticValidationBudgetIsCheckedBeforeCopyingOrGeometryValidation()
    {
        var piece = Piece(13);
        // The 2,000-point input is intentionally degenerate: the budget must reject it
        // before generating 13 copies or reaching contour validation.
        piece.Points = Enumerable.Repeat(new Point2(0, 0), NestingEngine.MaximumVerticesPerPiece).ToList();
        var document = new ProjectDocument { Pieces = [piece] };
        var error = Assert.Throws<ArgumentException>(() => ProductionNestingPlan.Arrange(document, [piece.Id], 100));
        Assert.Contains("kaynak sınır", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(13, piece.Metadata!.Quantity);
        Assert.Equal(NestingEngine.MaximumVerticesPerPiece, piece.Points.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Yardımcı")]
    [InlineData("kesim")]
    public void SelectedNonCutOrUnspecifiedRoleIsNotSilentlyExpanded(string? role)
    {
        var piece = Piece(); piece.Metadata!.Role = role;
        var document = new ProjectDocument { Pieces = [piece] };
        Assert.Throws<ArgumentException>(() => ProductionNestingPlan.Arrange(document, [piece.Id], 100));
        Assert.Equal(role, piece.Metadata.Role);
    }

    [Fact]
    public void CopyMetadataAndGeometryAreIndependentAcrossResultsAndSource()
    {
        var piece = Piece(2);
        var document = new ProjectDocument { Pieces = [piece] };
        var result = ProductionNestingPlan.Arrange(document, [piece.Id], 100, 0);
        Assert.Equal(2, result.Placements.Count);
        var first = result.Nesting.Layout.Pieces.Single(p => p.Id == result.Placements[0].PlacedPieceId);
        var second = result.Nesting.Layout.Pieces.Single(p => p.Id == result.Placements[1].PlacedPieceId);
        Assert.NotEqual(first.Id, second.Id);
        first.Metadata!.Material = "Değişti";
        first.Points[0] = new Point2(9, 9);
        Assert.Equal("Pamuk", second.Metadata!.Material);
        Assert.Equal("Pamuk", piece.Metadata!.Material);
        Assert.Equal(new Point2(0,0), piece.Points[0]);
        Assert.NotEqual(first.Points[0], second.Points[0]);
    }
}
