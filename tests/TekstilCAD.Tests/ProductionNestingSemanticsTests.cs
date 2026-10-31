using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public class ProductionNestingSemanticsTests
{
    private static PatternPiece Piece(string name, int count, double width, double height) => new()
    {
        Name = name, Points = [new(10, 30), new(10 + width, 30), new(10 + width, 30 + height), new(10, 30 + height)],
        Metadata = new() { Role = "Kesim", Quantity = count, Material = "Pamuk" }
    };

    [Fact]
    public void SelectionOrderDoesNotChangeCopiesOrPlacementAndResultsAreIndependent()
    {
        var first = Piece("Ön", 2, 10, 20);
        var second = Piece("Arka", 1, 15, 25);
        var document = new ProjectDocument { Name = "Model", Pieces = [first, second] };
        var original = document.Clone();
        var a = ProductionNestingPlan.Arrange(document, [first.Id, second.Id], 60, 5);
        var b = ProductionNestingPlan.Arrange(document, [second.Id, first.Id], 60, 5);

        Assert.Equal(a.Placements, b.Placements);
        Assert.Equal(3, a.Placements.Count);
        Assert.Equal(3, a.Placements.Select(x => x.PlacedPieceId).Distinct().Count());
        foreach (var mapping in a.Placements)
        {
            var left = Assert.Single(a.Nesting.Layout.Pieces, x => x.Id == mapping.PlacedPieceId);
            var right = Assert.Single(b.Nesting.Layout.Pieces, x => x.Id == mapping.PlacedPieceId);
            Assert.Equal(left.Points, right.Points);
            Assert.Equal(1, left.Metadata!.Quantity);
        }
        a.Nesting.Layout.Pieces[0].Points[0] = new(999, 999);
        a.Nesting.Layout.Pieces[0].Metadata!.Material = "Değişti";
        Assert.NotEqual(a.Nesting.Layout.Pieces[0].Points[0], b.Nesting.Layout.Pieces[0].Points[0]);
        Assert.True(DocumentSession.ContentEquals(original, document));
    }

    [Fact]
    public void CopiesStayInsideFabricAndBoundingBoxesHaveRequestedGap()
    {
        var first = Piece("Ön", 3, 10, 20);
        var second = Piece("Arka", 2, 15, 25);
        var document = new ProjectDocument { Pieces = [first, second] };
        const double width = 50, gap = 5;
        var result = ProductionNestingPlan.Arrange(document, [first.Id, second.Id], width, gap);
        Assert.Equal(5, result.Nesting.Layout.Pieces.Count);
        var boxes = result.Nesting.Layout.Pieces.Select(p => Geometry.Bounds(p.Points)).ToArray();
        foreach (var box in boxes)
        {
            Assert.InRange(box.MinX, 0, width);
            Assert.InRange(box.MaxX, 0, width);
            Assert.InRange(box.MinY, 0, result.Nesting.UsedLength);
            Assert.InRange(box.MaxY, 0, result.Nesting.UsedLength);
        }
        for (var i = 0; i < boxes.Length; i++)
        for (var j = i + 1; j < boxes.Length; j++)
            Assert.True(boxes[i].MaxX + gap <= boxes[j].MinX || boxes[j].MaxX + gap <= boxes[i].MinX
                || boxes[i].MaxY + gap <= boxes[j].MinY || boxes[j].MaxY + gap <= boxes[i].MinY);
    }
}
