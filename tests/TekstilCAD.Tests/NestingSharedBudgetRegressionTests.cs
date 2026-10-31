using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class NestingSharedBudgetRegressionTests
{
    [Fact]
    public void ProductionRejectsExcessiveValidationBeforeExpansion()
    {
        var piece = new PatternPiece
        {
            Points = Enumerable.Range(0, 1000).Select(i => new Point2(i, i % 2)).ToList(),
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 50 }
        };
        var document = new ProjectDocument { Pieces = [piece] };

        // Invalid fabric width would be reported by the motor if expansion ran.
        var error = Assert.Throws<ArgumentException>(() =>
            ProductionNestingPlan.Arrange(document, [piece.Id], 0));
        Assert.Contains("kaynak sınırlarını", error.Message);
        Assert.Equal(50, piece.Metadata.Quantity);
    }
}
