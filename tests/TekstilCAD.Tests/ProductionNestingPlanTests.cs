using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public class ProductionNestingPlanTests
{
    private static PatternPiece Piece(int quantity = 2, string role = "Kesim", string? material = "Pamuk") => new()
    {
        Name = "Ön", Points = [new(0,0),new(10,0),new(10,20),new(0,20)],
        Metadata = new() { Role = role, Quantity = quantity, Material = material }
    };

    [Fact] public void QuantityExpandsIntoDistinctStableMappedCopiesWithoutChangingSource()
    {
        var piece = Piece(3); var source = new ProjectDocument { Pieces = [piece] };
        var first = ProductionNestingPlan.Arrange(source, [piece.Id], 100, 5);
        var second = ProductionNestingPlan.Arrange(source, [piece.Id], 100, 5);
        Assert.Equal(3, first.Nesting.Layout.Pieces.Count);
        Assert.Equal(3, first.Placements.Select(p => p.PlacedPieceId).Distinct().Count());
        Assert.Equal(first.Placements, second.Placements);
        Assert.Equal(new[] {1,2,3}, first.Placements.Select(p => p.CopyNumber));
        Assert.All(first.Placements, p => Assert.Equal(piece.Id, p.SourcePieceId));
        Assert.All(first.Nesting.Layout.Pieces, p => Assert.Equal(1, p.Metadata!.Quantity));
        Assert.Equal(3, piece.Metadata!.Quantity);
        Assert.Equal(new Point2(0,0), piece.Points[0]);
    }

    [Fact] public void AmbiguousOrUnselectedInputDoesNotBecomeProduction()
    {
        var piece = Piece(); var doc = new ProjectDocument { Pieces = [piece] };
        Assert.Throws<ArgumentException>(() => ProductionNestingPlan.Arrange(doc, [], 100));
        Assert.Throws<ArgumentException>(() => ProductionNestingPlan.Arrange(doc, [piece.Id, piece.Id], 100));
        Assert.Throws<ArgumentException>(() => ProductionNestingPlan.Arrange(doc, [Guid.NewGuid()], 100));
        piece.Metadata!.Role = "Yardımcı";
        Assert.Throws<ArgumentException>(() => ProductionNestingPlan.Arrange(doc, [piece.Id], 100));
        piece.Metadata.Role = "Kesim"; piece.Metadata.Quantity = null;
        Assert.Throws<ArgumentException>(() => ProductionNestingPlan.Arrange(doc, [piece.Id], 100));
        piece.Metadata.Quantity = 2; piece.Metadata.Material = null;
        Assert.Throws<ArgumentException>(() => ProductionNestingPlan.Arrange(doc, [piece.Id], 100));
    }

    [Fact] public void MixedMaterialsAndExcessiveCopiesAreRejected()
    {
        var a = Piece(); var b = Piece(material: "Yün"); var doc = new ProjectDocument { Pieces = [a,b] };
        Assert.Throws<ArgumentException>(() => ProductionNestingPlan.Arrange(doc, [a.Id,b.Id], 100));
        a.Metadata!.Quantity = 10001;
        Assert.Throws<ArgumentException>(() => ProductionNestingPlan.Arrange(doc, [a.Id], 100));
        Assert.Equal(10001, a.Metadata.Quantity);
    }
}
