using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using static TekstilCAD.Tests.PieceOperationTestData;

namespace TekstilCAD.Tests;

public sealed class AddRectangleTests
{
    private const string OutOfRange = "Sonuç çalışma alanının dışına taşıyor (her eksende en çok ±100.000.000 mm). Daha küçük bir ölçü veya konum girin.";

    [Fact]
    public void FirstRectangleGoesToTheOriginWithExactCorners()
    {
        var result = Run(new ProjectDocument(), d => PieceOperations.AddRectangle(d, 120, 80));
        var piece = result.Piece!;
        Assert.Equal(Pts((0, 0), (120, 0), (120, 80), (0, 80)), piece.Points);
        Assert.True(piece.Closed);
        Assert.Equal("Parça 1", piece.Name);
        Assert.Equal(Layer.DefaultId, piece.LayerId);
        Assert.Equal(result.PieceId, piece.Id);
        Assert.NotEqual(Guid.Empty, piece.Id);
        Assert.Equal("“Parça 1” dikdörtgeni eklendi: 120 mm × 80 mm, sol üst köşe X 0 mm, Y 0 mm.", result.Summary);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void SecondRectangleIsPlacedTwentyMillimetresRightOfTheFirstWithTheSameTop()
    {
        var first = Run(new ProjectDocument(), d => PieceOperations.AddRectangle(d, 120, 80));
        var second = Run(first.Document!, d => PieceOperations.AddRectangle(d, 50, 30));
        Assert.Equal(Pts((140, 0), (190, 0), (190, 30), (140, 30)), second.Piece!.Points);
        Assert.Equal("Parça 2", second.Piece.Name);
        Assert.Equal(2, second.Document!.Pieces.Count);
    }

    [Fact]
    public void DefaultPlacementFollowsTheRightmostPieceNotTheLastOne()
    {
        var document = Doc(Rect("Uzak", 300, 20, 10, 10), Rect("Yakın", 10, 50, 100, 40));
        var result = Run(document, d => PieceOperations.AddRectangle(d, 5, 5));
        Assert.Equal(new Point2(330, 20), result.Piece!.Points[0]);
    }

    [Fact]
    public void WhenTwoPiecesReachTheSameRightEdgeTheHigherOneSetsTheTop()
    {
        var document = Doc(Rect("A", 0, 30, 100, 10), Rect("B", 50, 10, 50, 10));
        var result = Run(document, d => PieceOperations.AddRectangle(d, 5, 5));
        Assert.Equal(new Point2(120, 10), result.Piece!.Points[0]);
    }

    [Fact]
    public void DefaultPlacementWorksLeftOfTheOrigin()
    {
        var document = Doc(Rect("Sol", -500, -500, 100, 100));
        var result = Run(document, d => PieceOperations.AddRectangle(d, 10, 10));
        Assert.Equal(new Point2(-380, -500), result.Piece!.Points[0]);
    }

    [Fact]
    public void DefaultPlacementNeverOverlapsAnyBoundingBox()
    {
        var document = Doc(Rect("A", 0, 0, 100, 400), Rect("B", 130, 0, 40, 40), Rect("C", 60, 500, 30, 30));
        var result = Run(document, d => PieceOperations.AddRectangle(d, 70, 70));
        var box = Geometry.Bounds(result.Piece!.Points);
        foreach (var other in document.Pieces.Select(p => Geometry.Bounds(p.Points)))
            Assert.True(box.MinX >= other.MaxX + PieceOperations.DefaultGapMm || box.MaxX <= other.MinX || box.MinY >= other.MaxY || box.MaxY <= other.MinY);
    }

    [Fact]
    public void ExplicitOriginIsTheTopLeftCornerAndIsUsedAsGiven()
    {
        var document = Doc(Rect("A", 0, 0, 100, 100));
        var result = Run(document, d => PieceOperations.AddRectangle(d, 10, 20, new Point2(5, 6)));
        Assert.Equal(Pts((5, 6), (15, 6), (15, 26), (5, 26)), result.Piece!.Points);
    }

    [Fact]
    public void FractionalSidesAreKeptWithoutDrift()
    {
        var result = Run(new ProjectDocument(), d => PieceOperations.AddRectangle(d, 0.3, 0.4, new Point2(0.1, 0.2)));
        var box = Geometry.Bounds(result.Piece!.Points);
        Assert.Equal(0.3, box.Width, 12);
        Assert.Equal(0.4, box.Height, 12);
        Assert.Equal(0.1, box.MinX);
        Assert.Equal(0.2, box.MinY);
    }

    [Fact]
    public void PointsAreClockwiseOnScreen()
    {
        var piece = Run(new ProjectDocument(), d => PieceOperations.AddRectangle(d, 120, 80)).Piece!;
        // Y grows downward, so a positive signed area means clockwise on screen.
        Assert.True(Geometry.SignedArea(piece.Points) > 0);
        Assert.Equal(9600, Geometry.SignedArea(piece.Points));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(0.001)]
    public void WidthAtOrBelowOneMicrometreIsRefused(double width)
    {
        var message = $"Genişlik 0,001 mm değerinden büyük olmalı; girilen: {(width == 0 ? "0" : width < 0 ? "-5" : "0,001")} mm. Daha büyük bir ölçü girin.";
        AssertRefused(PieceOperations.AddRectangle(new ProjectDocument(), width, 10), PieceOperationError.InvalidValue, message);
    }

    [Fact]
    public void HeightAtTheMinimumIsRefusedWithTheHeightLabel()
    {
        AssertRefused(PieceOperations.AddRectangle(new ProjectDocument(), 10, 0.001), PieceOperationError.InvalidValue,
            "Yükseklik 0,001 mm değerinden büyük olmalı; girilen: 0,001 mm. Daha büyük bir ölçü girin.");
    }

    [Fact]
    public void SidesJustAboveTheMinimumAreAccepted()
    {
        var result = Run(new ProjectDocument(), d => PieceOperations.AddRectangle(d, 0.0011, 0.0011));
        Assert.Equal(4, result.Piece!.Points.Count);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteWidthIsRefused(double width)
    {
        AssertRefused(PieceOperations.AddRectangle(new ProjectDocument(), width, 10), PieceOperationError.InvalidValue,
            "Genişlik geçerli bir sayı olmalı (örneğin 120); girilen değer kullanılamadı.");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonFiniteHeightIsRefused(double height)
    {
        AssertRefused(PieceOperations.AddRectangle(new ProjectDocument(), 10, height), PieceOperationError.InvalidValue,
            "Yükseklik geçerli bir sayı olmalı (örneğin 120); girilen değer kullanılamadı.");
    }

    [Fact]
    public void WidthBeyondTheWholeWorkingAreaIsRefusedWithItsSize()
    {
        AssertRefused(PieceOperations.AddRectangle(new ProjectDocument(), 3e8, 10), PieceOperationError.OutOfRange,
            "Genişlik çalışma alanına sığmıyor: 300.000.000 mm. " + OutOfRange);
    }

    [Fact]
    public void RectangleThatWouldLeaveTheCoordinateLimitIsRefused()
    {
        AssertRefused(PieceOperations.AddRectangle(new ProjectDocument(), 1.5e8, 10), PieceOperationError.OutOfRange, OutOfRange);
    }

    [Fact]
    public void RectangleEndingExactlyOnTheLimitIsAccepted()
    {
        var result = Run(new ProjectDocument(), d => PieceOperations.AddRectangle(d, 100, 100, new Point2(1e8 - 100, 1e8 - 100)));
        Assert.Equal(new Point2(1e8, 1e8), result.Piece!.Points[2]);
    }

    [Theory]
    [InlineData(1e8 + 1, 0)]
    [InlineData(0, -1e8 - 1)]
    public void ExplicitOriginBeyondTheLimitIsRefused(double x, double y)
    {
        AssertRefused(PieceOperations.AddRectangle(new ProjectDocument(), 10, 10, new Point2(x, y)), PieceOperationError.OutOfRange, OutOfRange);
    }

    [Fact]
    public void ExplicitOriginWithNotANumberIsRefused()
    {
        AssertRefused(PieceOperations.AddRectangle(new ProjectDocument(), 10, 10, new Point2(double.NaN, 0)), PieceOperationError.InvalidValue,
            "Konum (X ve Y) geçerli bir sayı olmalı (örneğin 120); girilen değer kullanılamadı.");
    }

    [Fact]
    public void NoRoomRightOfTheRightmostPieceIsReportedInsteadOfOverlapping()
    {
        var document = Doc(Rect("Uzak", 1e8 - 50, 0, 50, 50));
        AssertRefused(PieceOperations.AddRectangle(document, 10, 10), PieceOperationError.OutOfRange,
            "Mevcut parçaların sağında yeni parça için yer kalmadı (çalışma alanı sınırı ±100.000.000 mm). Konumu kendiniz belirtin.");
    }

    [Fact]
    public void TinySideAtHugeCoordinatesIsRefusedAsNotRepresentable()
    {
        AssertRefused(PieceOperations.AddRectangle(new ProjectDocument(), 0.002, 50, new Point2(1e8 - 1, 0)), PieceOperationError.Precision,
            "Bu konumda 0,002 mm × 50 mm ölçüsü sayısal hassasiyetle temsil edilemiyor; parça eklenmedi. Orijine daha yakın bir konum girin.");
    }

    [Fact]
    public void NamesSkipNumbersThatAreAlreadyTaken()
    {
        var document = Doc(Rect("Parça 2", 0, 0, 10, 10));
        Assert.Equal("Parça 3", Run(document, d => PieceOperations.AddRectangle(d, 5, 5)).Piece!.Name);

        var gap = Doc(Rect("Parça 1", 0, 0, 10, 10), Rect("Parça 3", 20, 0, 10, 10));
        Assert.Equal("Parça 4", Run(gap, d => PieceOperations.AddRectangle(d, 5, 5)).Piece!.Name);

        var other = Doc(Rect("Kol", 0, 0, 10, 10));
        Assert.Equal("Parça 2", Run(other, d => PieceOperations.AddRectangle(d, 5, 5)).Piece!.Name);
    }

    [Fact]
    public void NewIdsNeverCollideWithExistingOnes()
    {
        var document = new ProjectDocument();
        for (var i = 0; i < 20; i++) document = Run(document, d => PieceOperations.AddRectangle(d, 10, 10)).Document!;
        Assert.Equal(20, document.Pieces.Select(p => p.Id).Distinct().Count());
        Assert.Equal(20, document.Pieces.Select(p => p.Name).Distinct().Count());
    }

    [Fact]
    public void AddingDoesNotTouchTheInputDocumentAndUndoesInOneStep()
    {
        AssertUndoRoundTrip(OneRect(), d => PieceOperations.AddRectangle(d, 10, 10));
    }

    [Fact]
    public void RectangleCanBeAddedToAnotherVisibleLayer()
    {
        var layer = new Layer { Name = "Astar" };
        var document = WithLayers(OneRect(), layer);
        var result = Run(document, d => PieceOperations.AddRectangle(d, 10, 10, null, layer.Id));
        Assert.Equal(layer.Id, result.Piece!.LayerId);
    }

    [Fact]
    public void LockedLayerRefusesANewRectangle()
    {
        var layer = new Layer { Name = "Kilitli", Locked = true };
        var document = WithLayers(OneRect(), layer);
        AssertRefused(PieceOperations.AddRectangle(document, 10, 10, null, layer.Id), PieceOperationError.LayerLocked,
            "“Kilitli” katmanı kilitli; parça eklenmedi. Önce kilidi açın ya da başka bir katman seçin.");
    }

    [Fact]
    public void HiddenLayerRefusesANewRectangle()
    {
        var layer = new Layer { Name = "Gizli", Visible = false };
        var document = WithLayers(OneRect(), layer);
        AssertRefused(PieceOperations.AddRectangle(document, 10, 10, null, layer.Id), PieceOperationError.LayerUnavailable,
            "“Gizli” katmanı gizli; parça eklenmedi. Önce katmanı görünür yapın ya da başka bir katman seçin.");
    }

    [Fact]
    public void UnknownLayerRefusesANewRectangle()
    {
        AssertRefused(PieceOperations.AddRectangle(OneRect(), 10, 10, null, Guid.NewGuid()), PieceOperationError.LayerUnavailable,
            "Hedef katman bulunamadı; parça eklenmedi. Var olan bir katman seçin.");
    }
}
