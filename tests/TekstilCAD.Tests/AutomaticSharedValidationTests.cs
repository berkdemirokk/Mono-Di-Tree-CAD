using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class AutomaticSharedValidationTests
{
    private static PatternPiece Circle(int quantity) => new()
    {
        Points = Enumerable.Range(0, 1000).Select(i => new Point2(
            10 * Math.Cos(i * 2 * Math.PI / 1000), 10 * Math.Sin(i * 2 * Math.PI / 1000))).ToList(),
        Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = quantity }
    };

    [Fact]
    public void RepeatedPartsFitSharedBudgetAndProduceIndependentPlacements()
    {
        var piece = Circle(26);
        var result = AutomaticPerCopyNesting.Arrange(new() { Pieces = [piece] }, [piece.Id],
            new Dictionary<Guid, IReadOnlyList<int>> { [piece.Id] = new[] { 0 } },
            1000, 1, 1000, 0, GrainDirectionPolicy.Direction.TwoWay, 90);
        Assert.Equal(26, result.Plan.Nesting.Layout.Pieces.Count);
        Assert.Equal(26, result.Plan.Placements.Count);
        Assert.All(result.PlacedRotations.Values, angle => Assert.Equal(0, angle));
        ShelfLayoutVerifier.Verify(result.Plan.Nesting);
        var copies = result.Plan.Nesting.Layout.Pieces;
        var point = copies[1].Points[0];
        copies[0].Points[0] = new(999, 999);
        Assert.Equal(point, copies[1].Points[0]);
        Assert.Equal(new Point2(10, 0), piece.Points[0]);
    }

    [Fact]
    public void AutomaticBudgetChargesEveryPlacementAndRotation()
    {
        var piece = Circle(46);
        piece.Closed = false;
        var error = Assert.Throws<ArgumentException>(() => AutomaticPerCopyNesting.Arrange(
            new() { Pieces = [piece] }, [piece.Id],
            new Dictionary<Guid, IReadOnlyList<int>> { [piece.Id] = new[] { 0 } },
            1000, 0, 1000, 0, GrainDirectionPolicy.Direction.TwoWay, 90));
        Assert.Contains("kaynak sınırı", error.Message);
    }

    [Fact]
    public void InvalidRepeatedContourIsNeverAcceptedFromCache()
    {
        var piece = new PatternPiece { Points = [new(0, 0), new(4, 4), new(0, 4), new(4, 0)],
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 3 } };
        Assert.Throws<ArgumentException>(() => AutomaticPerCopyNesting.Arrange(
            new() { Pieces = [piece] }, [piece.Id],
            new Dictionary<Guid, IReadOnlyList<int>> { [piece.Id] = new[] { 0, 90 } },
            100, 0, 100, 0, GrainDirectionPolicy.Direction.TwoWay, 90));
    }
}
