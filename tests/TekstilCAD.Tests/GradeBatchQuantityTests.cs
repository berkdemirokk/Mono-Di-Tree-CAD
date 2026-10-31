using TekstilCAD.Core;
using TekstilCAD.Grading;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public class GradeBatchQuantityTests
{
    [Fact]
    public void ExplicitPerSizeQuantitiesFlowIntoProductionWithoutCopyingSourceQuantity()
    {
        var source = new PatternPiece { Name = "Ön", Points = [new(0,0),new(10,0),new(10,20),new(0,20)],
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 99, Size = "M" } };
        var entries = GradeBatchInput.Parse("S; -1; 2\nL; 1; 3\nXL; 2");
        var pieces = GradingEngine.GenerateSizes(source, [new(0,0),new(1,0),new(1,0),new(0,0)], entries.Select(e => e.Size).ToArray());
        for (var i = 0; i < pieces.Count; i++) GradeBatchInput.ApplyMetadata(pieces[i], source, entries[i], true, true);
        Assert.Equal(new int?[] { 2, 3, null }, pieces.Select(p => p.Metadata!.Quantity));
        Assert.Equal(new[] { "S", "L", "XL" }, pieces.Select(p => p.Metadata!.Size));
        Assert.Equal(99, source.Metadata.Quantity);
        var project = new ProjectDocument { Pieces = pieces.Take(2).ToList() };
        var plan = ProductionNestingPlan.Arrange(project, project.Pieces.Select(p => p.Id).ToArray(), 100, 5);
        Assert.Equal(5, plan.Placements.Count);
    }

    [Theory]
    [InlineData("S; 1; 0")]
    [InlineData("S; 1; -1")]
    [InlineData("S; 1; 1.5")]
    [InlineData("S; 1; 100001")]
    [InlineData("S; 1; 4; 5")]
    public void RejectsInvalidQuantities(string input) => Assert.Throws<ArgumentException>(() => GradeBatchInput.Parse(input));

    [Fact]
    public void ExistingTwoFieldEntriesKeepQuantityUnset()
    {
        var entries = GradeBatchInput.Parse("S; -1\nL; 1; ");
        Assert.All(entries, e => Assert.Null(e.Quantity));
    }
}
