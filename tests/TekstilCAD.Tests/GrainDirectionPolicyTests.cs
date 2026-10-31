using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class GrainDirectionPolicyTests
{
    private static PatternPiece Piece(double x, double y) => new()
    { Grainline = new Grainline { Start = new(0, 0), End = new(x, y) } };

    [Fact]
    public void OneWayAndTwoWayRespectArrowDirection()
    {
        var forward = Piece(0, 1); var reverse = Piece(0, -1);
        GrainDirectionPolicy.Validate(forward, GrainDirectionPolicy.Direction.OneWay, 0);
        GrainDirectionPolicy.Validate(reverse, GrainDirectionPolicy.Direction.TwoWay, 0);
        var error = Assert.Throws<ArgumentException>(() => GrainDirectionPolicy.Validate(reverse, GrainDirectionPolicy.Direction.OneWay, 90));
        Assert.True(NestingPieceError.TryGet(error, out var id, out _)); Assert.Equal(reverse.Id, id);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(-1, 0)]
    public void NinetyDegreesIsInclusiveForBothModes(double x, double y)
    {
        foreach (var mode in new[] { GrainDirectionPolicy.Direction.OneWay, GrainDirectionPolicy.Direction.TwoWay })
        {
            GrainDirectionPolicy.Validate(Piece(x, y), mode, 90);
            Assert.Throws<ArgumentException>(() => GrainDirectionPolicy.Validate(Piece(x, y), mode, 89));
        }
    }

    [Fact]
    public void InteriorAnglesPassAndBoundaryIsConservative()
    {
        var piece = Piece(1, 1);
        GrainDirectionPolicy.Validate(piece, GrainDirectionPolicy.Direction.OneWay, 46);
        Assert.Throws<ArgumentException>(() => GrainDirectionPolicy.Validate(piece, GrainDirectionPolicy.Direction.OneWay, 44));
        Assert.Throws<ArgumentException>(() => GrainDirectionPolicy.Validate(piece, GrainDirectionPolicy.Direction.OneWay, 45));
    }

    [Fact]
    public void MissingReviewAndInvalidLinesFailWithIdentity()
    {
        var piece = Piece(0, 1);
        foreach (var line in new Grainline?[] { null, new() { Start = new(0, 0), End = new(0, 1), NeedsReview = true },
            new() { Start = new(0, 0), End = new(0, 0) }, new() { Start = new(double.NaN, 0), End = new(0, 1) },
            new() { Start = new(0, 0), End = new(100_000_001, 1) } })
        {
            piece.Grainline = line;
            var error = Assert.Throws<ArgumentException>(() => GrainDirectionPolicy.Validate(piece, GrainDirectionPolicy.Direction.TwoWay, 90));
            Assert.True(NestingPieceError.TryGet(error, out var id, out _)); Assert.Equal(piece.Id, id);
        }
    }

    [Theory]
    [InlineData(-1)] [InlineData(91)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidTolerancesAreRejected(double tolerance)
    {
        foreach (var mode in new[] { GrainDirectionPolicy.Direction.OneWay, GrainDirectionPolicy.Direction.TwoWay })
            Assert.Throws<ArgumentOutOfRangeException>(() => GrainDirectionPolicy.Validate(Piece(0, 1), mode, tolerance));
    }
}
