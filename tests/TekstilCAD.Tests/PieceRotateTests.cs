using TekstilCAD.Core;
using static TekstilCAD.Tests.PieceOperationTestData;

namespace TekstilCAD.Tests;

public sealed class PieceRotateTests
{
    private static readonly Point2[] Clockwise = Pts((100, -20), (100, 100), (20, 100), (20, -20));
    private static readonly Point2[] CounterClockwise = Pts((20, 100), (20, -20), (100, -20), (100, 100));
    private static readonly Point2[] HalfTurn = Pts((120, 80), (0, 80), (0, 0), (120, 0));

    [Theory]
    [InlineData(90)]
    [InlineData(-270)]
    [InlineData(450)]
    public void PositiveQuarterTurnIsClockwiseOnScreenAboutTheBoundingBoxCentre(double degrees)
    {
        var document = OneRect();
        var result = Run(document, d => PieceOperations.Rotate(d, d.Pieces[0].Id, degrees));
        Assert.Equal(Clockwise, result.Piece!.Points);
        Assert.Equal("“Ön beden” 90° saat yönünde döndürüldü.", result.Summary);
    }

    [Theory]
    [InlineData(-90)]
    [InlineData(270)]
    [InlineData(-450)]
    public void NegativeQuarterTurnIsCounterClockwise(double degrees)
    {
        var document = OneRect();
        var result = Run(document, d => PieceOperations.Rotate(d, d.Pieces[0].Id, degrees));
        Assert.Equal(CounterClockwise, result.Piece!.Points);
        Assert.Equal("“Ön beden” 90° saat yönünün tersine döndürüldü.", result.Summary);
    }

    [Theory]
    [InlineData(180)]
    [InlineData(-180)]
    [InlineData(540)]
    public void HalfTurnSwapsOppositeCorners(double degrees)
    {
        var document = OneRect();
        var result = Run(document, d => PieceOperations.Rotate(d, d.Pieces[0].Id, degrees));
        Assert.Equal(HalfTurn, result.Piece!.Points);
        Assert.Equal("“Ön beden” 180° döndürüldü.", result.Summary);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(360)]
    [InlineData(-720)]
    public void FullTurnsChangeNothing(double degrees)
    {
        var document = OneRect();
        var result = Run(document, d => PieceOperations.Rotate(d, d.Pieces[0].Id, degrees));
        Assert.False(result.ChangesDocument);
        Assert.Equal("“Ön beden” tam turla döndürüldü; değişiklik gerekmedi.", result.Summary);
    }

    [Fact]
    public void ARectangleTurnsIntoItsTransposedRectangleAroundTheSameCentre()
    {
        var document = Doc(Rect("A", 30, 50, 120, 80));
        var result = Run(document, d => PieceOperations.Rotate(d, d.Pieces[0].Id, 90));
        var before = Geometry.Bounds(document.Pieces[0].Points);
        var after = Geometry.Bounds(result.Piece!.Points);
        Assert.Equal(before.Width, after.Height);
        Assert.Equal(before.Height, after.Width);
        Assert.Equal(before.Center, after.Center);
    }

    [Fact]
    public void FourQuarterTurnsReturnTheExactOriginalPoints()
    {
        var document = Doc(Rect("A", 3, 7, 121, 80));
        var id = document.Pieces[0].Id;
        var current = document;
        for (var i = 0; i < 4; i++) current = Run(current, d => PieceOperations.Rotate(d, id, 90)).Document!;
        Assert.Equal(document.Pieces[0].Points, current.Pieces[0].Points);
    }

    [Fact]
    public void QuarterTurnThenOppositeQuarterTurnReturnsTheOriginalPoints()
    {
        var triangle = new PatternPiece { Name = "Üçgen", Points = [new(10, 10), new(110, 10), new(60, 70)] };
        var there = Run(Doc(triangle), d => PieceOperations.Rotate(d, triangle.Id, 90));
        var back = Run(there.Document!, d => PieceOperations.Rotate(d, triangle.Id, -90));
        Assert.Equal(triangle.Points, back.Piece!.Points);
    }

    [Fact]
    public void OpenPieceStaysOpenAndKeepsItsOrder()
    {
        var line = Segment("Çizgi", 0, 0, 100);
        var result = Run(Doc(line), d => PieceOperations.Rotate(d, line.Id, 90));
        Assert.False(result.Piece!.Closed);
        Assert.Equal(Pts((50, -50), (50, 50)), result.Piece.Points);
    }

    [Theory]
    [InlineData(45)]
    [InlineData(90.5)]
    [InlineData(1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnglesThatAreNotMultiplesOfNinetyAreRefused(double degrees)
    {
        var document = OneRect();
        AssertRefused(PieceOperations.Rotate(document, document.Pieces[0].Id, degrees), PieceOperationError.InvalidValue,
            "Şimdilik yalnız 90, 180 ve 270 derece döndürülebilir. Örnek: 90 sağa döndür");
    }

    [Fact]
    public void RotationThatLeavesTheWorkingAreaIsRefused()
    {
        // Centre at the right limit: a 100 x 2e8 piece turned 90 degrees is much wider than the area.
        var tall = new PatternPiece { Name = "Uzun", Points = [new(0, -1e8), new(100, -1e8), new(100, 1e8), new(0, 1e8)] };
        var result = PieceOperations.Rotate(Doc(tall), tall.Id, 90);
        Assert.False(result.Succeeded);
        Assert.Equal(PieceOperationError.OutOfRange, result.ErrorCode);
        Assert.Equal("Sonuç çalışma alanının dışına taşıyor (her eksende en çok ±100.000.000 mm). Daha küçük bir ölçü veya konum girin.", result.Error);
    }

    [Fact]
    public void PieceWithCurvesIsRefused()
    {
        var curved = PieceOperationCurveTests.CurvedPiece("Eğri");
        AssertRefused(PieceOperations.Rotate(Doc(curved), curved.Id, 90), PieceOperationError.Unsupported,
            "“Eğri” yay veya eğri içeriyor; döndürme eğrilerin kontrol noktalarını bozacağı için uygulanmadı. Eğri içeren parçalarda yalnızca taşıma ve çoğaltma desteklenir.");
    }

    [Fact]
    public void PieceOnALockedLayerIsRefused()
    {
        var layer = new Layer { Name = "Kilitli", Locked = true };
        var document = WithLayers(OneRect(), layer);
        document.Pieces[0].LayerId = layer.Id;
        AssertRefused(PieceOperations.Rotate(document, document.Pieces[0].Id, 90), PieceOperationError.LayerLocked,
            "Parça kilitli bir katmanda. Ayrıntılı görünümde katmanın kilidini açın.");
    }

    [Theory]
    [InlineData(90)]
    [InlineData(-90)]
    [InlineData(180)]
    public void RotationUndoesInOneStep(double degrees)
    {
        var document = Doc(Rect("A", 0, 0, 10, 20), Rect("B", 50, 0, 10, 10));
        AssertUndoRoundTrip(document, d => PieceOperations.Rotate(d, d.Pieces[0].Id, degrees));
    }
}
