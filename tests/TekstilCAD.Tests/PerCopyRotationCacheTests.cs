using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class PerCopyRotationCacheTests
{
    [Fact]
    public void RepeatedAndDifferentAnglesKeepIndependentCopiesAndSource()
    {
        var piece = new PatternPiece { Name = "Parça", Points = [new(0, 0), new(6, 0), new(6, 4), new(0, 4)],
            Grainline = new() { Start = new(1, 1), End = new(1, 2) },
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Size = "M", Quantity = 3 } };
        var angles = new Dictionary<(Guid, int), int> { [(piece.Id, 1)] = 0, [(piece.Id, 2)] = 0, [(piece.Id, 3)] = 90 };
        var result = PerCopyOrthogonalNesting.Arrange(new() { Pieces = [piece] }, [piece.Id], angles,
            100, 0, 100, 0, GrainDirectionPolicy.Direction.TwoWay, 90);
        var copies = result.Plan.Placements.Select(m => result.Plan.Nesting.Layout.Pieces.Single(p => p.Id == m.PlacedPieceId)).ToArray();
        Assert.All(copies, p => { Assert.Equal(1, p.Metadata!.Quantity); Assert.Equal("M", p.Metadata.Size); });
        Assert.Equal(3, copies.Select(p => p.Id).Distinct().Count());
        Assert.Equal(90, result.PlacedRotations[copies[2].Id]);
        Assert.Equal(6, copies[0].Points.Max(p => p.X) - copies[0].Points.Min(p => p.X));
        Assert.Equal(4, copies[2].Points.Max(p => p.X) - copies[2].Points.Min(p => p.X));
        var other = copies[1].Points[0];
        copies[0].Points[0] = new(999, 999);
        copies[0].Metadata!.Size = "changed";
        Assert.Equal(other, copies[1].Points[0]);
        Assert.Equal("M", copies[1].Metadata!.Size);
        Assert.Equal(new Point2(0, 0), piece.Points[0]);
        Assert.Equal(3, piece.Metadata!.Quantity);
        Assert.Equal("M", piece.Metadata.Size);
    }

    [Fact]
    public void CachedRotationWorkIsCountedOnceButDifferentAnglesCountSeparately()
    {
        // Engine work is 48 million; one rotation adds 2 million, a second exceeds the limit.
        const int vertices = 1000, quantity = 24;
        var piece = new PatternPiece { Points = Enumerable.Range(0, vertices).Select(i =>
            new Point2(Math.Cos(i * 2 * Math.PI / vertices) * 10, Math.Sin(i * 2 * Math.PI / vertices) * 10)).ToList(),
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = quantity } };
        var angles = Enumerable.Range(1, quantity).ToDictionary(i => (piece.Id, i), _ => 0);
        piece.Closed = false; // At the exact limit, contour validation is reached.
        var atLimit = Assert.Throws<ArgumentException>(() => PerCopyOrthogonalNesting.Arrange(
            new() { Pieces = [piece] }, [piece.Id], angles, 1000, 0, 1000, 0,
            GrainDirectionPolicy.Direction.TwoWay, 90));
        Assert.True(NestingPieceError.TryGet(atLimit, out var sourceId, out _));
        Assert.Equal(piece.Id, sourceId);
        angles[(piece.Id, quantity)] = 90;
        var error = Assert.Throws<ArgumentException>(() => PerCopyOrthogonalNesting.Arrange(
            new() { Pieces = [piece] }, [piece.Id], angles, 1000, 0, 1000, 0,
            GrainDirectionPolicy.Direction.TwoWay, 90));
        Assert.Equal("source", error.ParamName);
        Assert.Contains("kaynak sınırı", error.Message);
    }
}
