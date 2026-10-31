using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class GrainConstrainedNestingTests
{
    [Fact]
    public void InvalidPolicyIsRejectedBeforePlanning()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => GrainConstrainedNesting.Arrange(
            new ProjectDocument(), [], 100, 0, 100, 0, GrainDirectionPolicy.Direction.OneWay, double.NaN));
        Assert.Equal("toleranceDegrees", error.ParamName);
    }

    [Fact]
    public void TranslationCannotTurnOutOfToleranceSourceIntoAnAcceptedPlan()
    {
        var piece = new PatternPiece
        {
            Points = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)],
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 1 },
            Grainline = new() { Start = new(0, 0), End = new(0.50000000005, 1) }
        };
        var error = Assert.Throws<ArgumentException>(() => GrainConstrainedNesting.Arrange(
            new ProjectDocument { Pieces = [piece] }, [piece.Id], 2_000_010, 0, 2_000_010, 1_000_000,
            GrainDirectionPolicy.Direction.OneWay, 26.56505117717799));
        Assert.True(NestingPieceError.TryGet(error, out var id, out _));
        Assert.Equal(piece.Id, id);
    }

    [Fact]
    public void PolicyChecksEveryCopyAndReportsSourceIdentity()
    {
        var piece = new PatternPiece
        {
            Points = [new(0, 0), new(10, 0), new(10, 20), new(0, 20)],
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 2 },
            Grainline = new() { Start = new(0, 0), End = new(0, -1) }
        };
        var document = new ProjectDocument { Pieces = [piece] };
        var result = GrainConstrainedNesting.Arrange(document, [piece.Id], 100, 5, 100, 0,
            GrainDirectionPolicy.Direction.TwoWay, 0);
        Assert.Equal(2, result.Nesting.Layout.Pieces.Count);
        var error = Assert.Throws<ArgumentException>(() => GrainConstrainedNesting.Arrange(document, [piece.Id],
            100, 5, 100, 0, GrainDirectionPolicy.Direction.OneWay, 0));
        Assert.True(NestingPieceError.TryGet(error, out var id, out _));
        Assert.Equal(piece.Id, id);
        Assert.Equal(new Point2(0, -1), piece.Grainline.End);
    }
}
