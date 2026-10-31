using System.Text.Json;
using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public class ProductionNestingBoundaryTests
{
    private static PatternPiece Piece(int count = 2, string material = "Pamuk") => new()
    {
        Name = "Ön", Points = [new(10,20),new(20,20),new(20,30),new(10,30)],
        Metadata = new() { Role = "Kesim", Quantity = count, Material = material, Size = "M" }
    };

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(100, -1)]
    public void InvalidFabricDimensionsCannotMutateTheSource(double width, double gap)
    {
        var piece = Piece(); var document = new ProjectDocument { Pieces = [piece] };
        var before = JsonSerializer.Serialize(document);
        Assert.ThrowsAny<ArgumentException>(() => ProductionNestingPlan.Arrange(document, [piece.Id], width, gap));
        Assert.Equal(before, JsonSerializer.Serialize(document));
    }

    [Fact]
    public void ExceedingMaximumLengthRejectsWholePlanWithoutChangingTheSource()
    {
        var piece = Piece(2); var document = new ProjectDocument { Pieces = [piece] };
        var before = JsonSerializer.Serialize(document);
        Assert.Throws<InvalidOperationException>(() => ProductionNestingPlan.Arrange(document, [piece.Id], 10, 0, 19));
        Assert.Equal(before, JsonSerializer.Serialize(document));
        var exact = ProductionNestingPlan.Arrange(document, [piece.Id], 10, 0, 20);
        Assert.Equal(20, exact.Nesting.UsedLength);
        Assert.Equal(before, JsonSerializer.Serialize(document));
    }

    [Fact]
    public void LaterSourceChangesCannotAlterAlreadyComputedPlanOrMappings()
    {
        var selected = Piece(); var other = Piece(1); var document = new ProjectDocument { Pieces = [selected, other] };
        var plan = ProductionNestingPlan.Arrange(document, [selected.Id], 100, 5);
        var before = JsonSerializer.Serialize(plan);
        selected.Points[0] = new(999, 999);
        selected.Metadata!.Material = "Yün";
        selected.Metadata.Quantity = 50;
        document.Pieces.Clear();
        Assert.Equal(before, JsonSerializer.Serialize(plan));
        Assert.Equal(2, plan.Placements.Count);
        Assert.All(plan.Placements, x => Assert.Equal(selected.Id, x.SourcePieceId));
        Assert.All(plan.Nesting.Layout.Pieces, x => Assert.Equal("Pamuk", x.Metadata!.Material));
    }

    [Fact]
    public void SourceMaterialMustMatchExactlyAndRejectionLeavesInputsIntact()
    {
        var a = Piece(1, "Pamuk"); var b = Piece(1, "pamuk");
        var document = new ProjectDocument { Pieces = [a,b] }; var before = JsonSerializer.Serialize(document);
        Assert.Throws<ArgumentException>(() => ProductionNestingPlan.Arrange(document, [a.Id,b.Id], 100));
        Assert.Equal(before, JsonSerializer.Serialize(document));
    }
}
