using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

/// <summary>Checks that shelf translations do not silently alter source shapes.</summary>
public sealed class NestingTranslationPrecisionTests
{
    private static PatternPiece Rectangle(double width, double height, double x = 0, double y = 0) => new()
    {
        Name = "Kalıp",
        Closed = true,
        Points = [new(x, y), new(x + width, y), new(x + width, y + height), new(x, y + height)]
    };

    private static void AssertPrecisionRejection(Action arrange, PatternPiece affected)
    {
        var error = Assert.Throws<ArgumentException>(arrange);
        Assert.True(NestingPieceError.TryGet(error, out var id, out var reason));
        Assert.Equal(affected.Id, id);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Theory]
    [InlineData(0.000000011)]
    [InlineData(0.0000011)]
    public void LargeHorizontalPlacementCannotStretchNarrowPiece(double width)
    {
        var first = Rectangle(90_000_000, 100);
        var narrow = Rectangle(width, 10);
        var before = new[] { first.Clone(), narrow.Clone() };
        // The requested source is a valid simple shape, not a degenerate-input test.
        Assert.True(Math.Abs(Geometry.SignedArea(narrow.Points)) > Geometry.Epsilon);
        Assert.False(Geometry.HasSelfIntersections(narrow.Points));
        var roundedWidth = (90_000_000d + width) - 90_000_000d;
        Assert.True(Math.Abs(roundedWidth - width) / width > 0.001);
        if (width == 0.000000011) Assert.True(Math.Abs(roundedWidth - width) / width > 0.35);

        AssertPrecisionRejection(() => NestingEngine.Arrange([first, narrow], 100_000_000, 0), narrow);
        Assert.Equal(before[0].Points, first.Points);
        Assert.Equal(before[1].Points, narrow.Points);
    }

    [Theory]
    [InlineData(0.000000011)]
    [InlineData(0.0000011)]
    public void LargeNextShelfCannotStretchShortPieceVertically(double height)
    {
        var first = Rectangle(50, 90_000_000);
        var narrow = Rectangle(20, height);
        var before = new[] { first.Clone(), narrow.Clone() };
        Assert.True(Math.Abs(Geometry.SignedArea(narrow.Points)) > Geometry.Epsilon);
        Assert.False(Geometry.HasSelfIntersections(narrow.Points));
        var roundedHeight = (90_000_000d + height) - 90_000_000d;
        Assert.True(Math.Abs(roundedHeight - height) / height > 0.001);
        if (height == 0.000000011) Assert.True(Math.Abs(roundedHeight - height) / height > 0.35);

        AssertPrecisionRejection(() => NestingEngine.Arrange([first, narrow], 50, 0), narrow);
        Assert.Equal(before[0].Points, first.Points);
        Assert.Equal(before[1].Points, narrow.Points);
    }

    [Fact]
    public void LargeNextShelfCannotDistortHeightOfWideThinTriangle()
    {
        var first = Rectangle(50_000, 90_000_000);
        var triangle = new PatternPiece
        {
            Name = "Yatay ince üçgen",
            Closed = true,
            Points = [new(0, 0), new(20_000, 0), new(10_000, 0.0000011)]
        };
        var beforeFirst = first.Clone();
        var beforeTriangle = triangle.Clone();
        Assert.True(Math.Abs(Geometry.SignedArea(triangle.Points)) > Geometry.Epsilon);
        Assert.False(Geometry.HasSelfIntersections(triangle.Points));
        Assert.True(Math.Abs((90_000_000d + 0.0000011) - 90_000_000d - 0.0000011) / 0.0000011 > 0.002);

        AssertPrecisionRejection(() => NestingEngine.Arrange([first, triangle], 50_000, 0), triangle);
        Assert.Equal(beforeFirst.Points, first.Points);
        Assert.Equal(beforeTriangle.Points, triangle.Points);
    }

    [Fact]
    public void LargeHorizontalPlacementCannotDistortWidthOfTallThinTriangle()
    {
        var first = Rectangle(90_000_000, 30_000);
        var triangle = new PatternPiece
        {
            Name = "Dikey ince üçgen",
            Closed = true,
            Points = [new(0, 0), new(0.0000011, 10_000), new(0, 20_000)]
        };
        var beforeFirst = first.Clone();
        var beforeTriangle = triangle.Clone();
        Assert.True(Math.Abs(Geometry.SignedArea(triangle.Points)) > Geometry.Epsilon);
        Assert.False(Geometry.HasSelfIntersections(triangle.Points));

        AssertPrecisionRejection(() => NestingEngine.Arrange([first, triangle], 100_000_000, 0), triangle);
        Assert.Equal(beforeFirst.Points, first.Points);
        Assert.Equal(beforeTriangle.Points, triangle.Points);
    }

    [Fact]
    public void NegativeOriginCopiesKeepTheirOwnIdentityWhenFirstCopyCannotBePlacedExactly()
    {
        var first = Rectangle(90_000_000, 100);
        var copyOne = Rectangle(0.0000011, 10, -100, -50);
        var copyTwo = copyOne.Clone();
        copyTwo.Id = Guid.NewGuid();
        var firstBefore = first.Clone();
        var oneBefore = copyOne.Clone();
        var twoBefore = copyTwo.Clone();

        Assert.False(Geometry.HasSelfIntersections(copyOne.Points));
        AssertPrecisionRejection(() => NestingEngine.Arrange([first, copyOne, copyTwo], 100_000_000, 0), copyOne);
        Assert.Equal(firstBefore.Points, first.Points);
        Assert.Equal(oneBefore.Points, copyOne.Points);
        Assert.Equal(twoBefore.Points, copyTwo.Points);
        Assert.NotEqual(copyOne.Id, copyTwo.Id);
    }

    [Fact]
    public void RegularDecimalPiecesRemainAcceptedAndKeepTheirMeasurements()
    {
        var first = Rectangle(180.25, 90.75, -40.125, -21.625);
        var second = Rectangle(40.1, 30.2, -17.25, -19.5);
        var copies = new[] { first.Clone(), second.Clone() };
        var result = NestingEngine.Arrange([first, second], 400, 5.5, edgeMargin: 2.5);

        Assert.Equal(2, result.Layout.Pieces.Count);
        foreach (var original in copies)
        {
            var placed = Assert.Single(result.Layout.Pieces, piece => piece.Id == original.Id);
            var sourceBounds = Geometry.Bounds(original.Points);
            var placedBounds = Geometry.Bounds(placed.Points);
            Assert.Equal(sourceBounds.Width, placedBounds.Width, 8);
            Assert.Equal(sourceBounds.Height, placedBounds.Height, 8);
            Assert.Equal(Math.Abs(Geometry.SignedArea(original.Points)), Math.Abs(Geometry.SignedArea(placed.Points)), 6);
        }
        Assert.Equal(copies[0].Points, first.Points);
        Assert.Equal(copies[1].Points, second.Points);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(2000)]
    public void RealisticCircularContoursRemainAccepted(int vertices)
    {
        var circle = new PatternPiece
        {
            Name = "Yuvarlak kontur",
            Closed = true,
            Points = Enumerable.Range(0, vertices).Select(i =>
            {
                var angle = 2 * Math.PI * i / vertices;
                return new Point2(-12.3 + 30 * Math.Cos(angle), -23.7 + 30 * Math.Sin(angle));
            }).ToList()
        };
        var before = circle.Clone();

        var result = NestingEngine.Arrange([circle], 150, 0, edgeMargin: 3.75);

        var placed = Assert.Single(result.Layout.Pieces);
        Assert.Equal(circle.Id, placed.Id);
        Assert.Equal(vertices, placed.Points.Count);
        Assert.Equal(Geometry.Bounds(before.Points).Width, Geometry.Bounds(placed.Points).Width, 8);
        Assert.Equal(Geometry.Bounds(before.Points).Height, Geometry.Bounds(placed.Points).Height, 8);
        Assert.Equal(before.Points, circle.Points);
    }

    [Fact]
    public void DecimalContourWithNearlyHorizontalAndVerticalEdgesRemainsAccepted()
    {
        var piece = new PatternPiece
        {
            Name = "Hafif eğimli kenarlar",
            Closed = true,
            Points = [new(-17.25, -19.5), new(82.75, -19.4999999),
                new(82.7500001, 80.5), new(-17.25, 80.5)]
        };
        var before = piece.Clone();

        var result = NestingEngine.Arrange([piece], 150, 0, edgeMargin: 2.5);

        var placed = Assert.Single(result.Layout.Pieces);
        Assert.Equal(piece.Id, placed.Id);
        Assert.Equal(Geometry.Bounds(before.Points).Width, Geometry.Bounds(placed.Points).Width, 8);
        Assert.Equal(Geometry.Bounds(before.Points).Height, Geometry.Bounds(placed.Points).Height, 8);
        Assert.Equal(before.Points, piece.Points);
    }

    [Fact]
    public void CancellationKeepsAllSourcePiecesUnchanged()
    {
        var first = Rectangle(50, 30, -10, -20);
        var second = Rectangle(40, 20, 25, 30);
        var originals = new[] { first.Clone(), second.Clone() };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            NestingEngine.Arrange([first, second], 200, 5, double.PositiveInfinity, 0, cancellation.Token));
        Assert.Equal(originals[0].Points, first.Points);
        Assert.Equal(originals[1].Points, second.Points);
    }
}
