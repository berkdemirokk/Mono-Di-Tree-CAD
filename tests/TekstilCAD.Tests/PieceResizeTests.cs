using TekstilCAD.Core;
using static TekstilCAD.Tests.PieceOperationTestData;

namespace TekstilCAD.Tests;

public sealed class PieceResizeTests
{
    private const string RangeText = "Ölçü 1 ile 20000 mm arasında olmalı. Örnek: 120 x 80 dikdörtgen";
    private const string OutOfRange = "Sonuç çalışma alanının dışına taşıyor (her eksende en çok ±100.000.000 mm). Daha küçük bir ölçü veya konum girin.";

    [Fact]
    public void RectangleGetsExactlyTheRequestedSidesAndKeepsItsTopLeft()
    {
        var document = Doc(Rect("Ön beden", 10, 20, 120, 80));
        var id = document.Pieces[0].Id;
        var result = Run(document, d => PieceOperations.Resize(d, id, 60, 40));
        Assert.Equal(Pts((10, 20), (70, 20), (70, 60), (10, 60)), Find(result, id).Points);
        Assert.Equal("“Ön beden” yeniden boyutlandırıldı: 60 mm × 40 mm (sol üst köşe sabit).", result.Summary);
    }

    [Fact]
    public void ReachingASmallerRectangleAndComingBackIsExactForIntegers()
    {
        var document = Doc(Rect("A", 10, 20, 120, 80));
        var id = document.Pieces[0].Id;
        var small = Run(document, d => PieceOperations.Resize(d, id, 30, 20));
        var back = Run(small.Document!, d => PieceOperations.Resize(d, id, 120, 80));
        Assert.Equal(document.Pieces[0].Points, back.Document!.Pieces[0].Points);
    }

    [Fact]
    public void NullWidthKeepsTheWidthAndLeavesTheXCoordinatesAlone()
    {
        var document = Doc(Rect("A", 10, 20, 120, 80));
        var result = Run(document, d => PieceOperations.Resize(d, d.Pieces[0].Id, null, 40));
        Assert.Equal(Pts((10, 20), (130, 20), (130, 60), (10, 60)), result.Piece!.Points);
    }

    [Fact]
    public void NullHeightKeepsTheHeight()
    {
        var document = Doc(Rect("A", 10, 20, 120, 80));
        var result = Run(document, d => PieceOperations.Resize(d, d.Pieces[0].Id, 240, null));
        Assert.Equal(Pts((10, 20), (250, 20), (250, 100), (10, 100)), result.Piece!.Points);
    }

    [Fact]
    public void AnyContourIsScaledAboutTheTopLeftOfItsBoundingBox()
    {
        var triangle = new PatternPiece { Name = "Üçgen", Points = [new(0, 0), new(100, 0), new(0, 100)] };
        var result = Run(Doc(triangle), d => PieceOperations.Resize(d, triangle.Id, 50, 200));
        Assert.Equal(Pts((0, 0), (50, 0), (0, 200)), result.Piece!.Points);

        var offset = new PatternPiece { Name = "Kaymış", Points = [new(10, 10), new(110, 10), new(60, 60)] };
        var second = Run(Doc(offset), d => PieceOperations.Resize(d, offset.Id, 50, 25));
        Assert.Equal(Pts((10, 10), (60, 10), (35, 35)), second.Piece!.Points);
    }

    [Fact]
    public void OpenPolylineKeepsItsOrderAndStaysOpen()
    {
        var path = new PatternPiece { Name = "Yol", Closed = false, Points = [new(0, 0), new(10, 10), new(20, 0)] };
        var result = Run(Doc(path), d => PieceOperations.Resize(d, path.Id, 40, null));
        Assert.Equal(Pts((0, 0), (20, 10), (40, 0)), result.Piece!.Points);
        Assert.False(result.Piece.Closed);
    }

    [Fact]
    public void ExplicitClosingVertexStaysEqualToTheFirstPoint()
    {
        var piece = new PatternPiece { Name = "K", Points = [new(0, 0), new(30, 0), new(30, 30), new(0, 30), new(0, 0)] };
        var result = Run(Doc(piece), d => PieceOperations.Resize(d, piece.Id, 90, 10));
        Assert.Equal(result.Piece!.Points[0], result.Piece.Points[^1]);
        Assert.Equal(5, result.Piece.Points.Count);
    }

    [Fact]
    public void FractionalTargetKeepsTheSidesWithinOneTrillionthOfAMillimetre()
    {
        var document = Doc(Rect("A", 0.1, 0.2, 0.3, 0.4));
        var result = Run(document, d => PieceOperations.Resize(d, d.Pieces[0].Id, 0.7, 0.9));
        var box = Geometry.Bounds(result.Piece!.Points);
        Assert.Equal(0.7, box.Width, 12);
        Assert.Equal(0.9, box.Height, 12);
        Assert.Equal(0.1, box.MinX);
        Assert.Equal(0.2, box.MinY);
    }

    [Fact]
    public void ResizingToTheCurrentSizeChangesNothing()
    {
        var document = Doc(Rect("Ön beden", 0, 0, 120, 80));
        var result = Run(document, d => PieceOperations.Resize(d, d.Pieces[0].Id, 120, 80));
        Assert.False(result.ChangesDocument);
        Assert.Equal("Ölçü zaten 120 × 80 mm. Değişiklik yok.", result.Summary);
    }

    [Fact]
    public void AllSidesStayInOrderAfterAnEnlargement()
    {
        var document = Doc(Rect("A", 0, 0, 10, 10));
        var result = Run(document, d => PieceOperations.Resize(d, d.Pieces[0].Id, 10000, 20000));
        Assert.Equal(Pts((0, 0), (10000, 0), (10000, 20000), (0, 20000)), result.Piece!.Points);
    }

    [Fact]
    public void ADraftContourThatWasAlreadyInvalidIsNotJudgedAgain()
    {
        var bowTie = new PatternPiece { Name = "Kelebek", Points = [new(0, 0), new(10, 10), new(10, 0), new(0, 10)] };
        var result = Run(Doc(bowTie), d => PieceOperations.Resize(d, bowTie.Id, 20, 20));
        Assert.Equal(Pts((0, 0), (20, 20), (20, 0), (0, 20)), result.Piece!.Points);
    }

    [Fact]
    public void WithoutAnyDimensionTheCallIsRefused()
    {
        var document = OneRect();
        AssertRefused(PieceOperations.Resize(document, document.Pieces[0].Id, null, null), PieceOperationError.InvalidValue,
            "Genişlik veya yükseklikten en az birini belirtin.");
    }

    [Theory]
    [InlineData(0, 40)]
    [InlineData(-3, 40)]
    [InlineData(60, 0.001)]
    [InlineData(20001, 40)]
    [InlineData(60, 1e9)]
    public void DimensionsOutsideTheRangeAreRefusedWithTheRangeText(double width, double height)
    {
        var document = OneRect();
        var result = PieceOperations.Resize(document, document.Pieces[0].Id, width, height);
        Assert.Contains(result.ErrorCode, new[] { PieceOperationError.InvalidValue, PieceOperationError.OutOfRange });
        AssertRefused(result, result.ErrorCode, RangeText);
    }

    [Theory]
    [InlineData(double.NaN, 40, "Genişlik geçerli bir sayı olmalı (örneğin 120); girilen değer kullanılamadı.")]
    [InlineData(60, double.PositiveInfinity, "Yükseklik geçerli bir sayı olmalı (örneğin 120); girilen değer kullanılamadı.")]
    public void NonFiniteDimensionsAreRefusedWithTheirLabel(double width, double height, string message)
    {
        var document = OneRect();
        AssertRefused(PieceOperations.Resize(document, document.Pieces[0].Id, width, height), PieceOperationError.InvalidValue, message);
    }

    [Fact]
    public void ResultBeyondTheCoordinateLimitIsRefused()
    {
        var document = Doc(Rect("A", 1e8 - 100, 0, 50, 50));
        AssertRefused(PieceOperations.Resize(document, document.Pieces[0].Id, 5000, null), PieceOperationError.OutOfRange, OutOfRange);
    }

    [Fact]
    public void ResultEndingExactlyOnTheLimitIsAccepted()
    {
        var document = Doc(Rect("A", 1e8 - 20000, 0, 10, 10));
        var result = Run(document, d => PieceOperations.Resize(d, d.Pieces[0].Id, 20000, null));
        Assert.Equal(1e8, result.Piece!.Points[1].X);
    }

    [Fact]
    public void AHorizontalLineCannotGainHeight()
    {
        var line = Segment("Çizgi", 0, 0, 100);
        AssertRefused(PieceOperations.Resize(Doc(line), line.Id, null, 10), PieceOperationError.InvalidValue,
            "“Çizgi” parçasının yüksekliği 0; yükseklik değiştirilemez. Yalnızca genişliği belirtin.");
    }

    [Fact]
    public void AHorizontalLineCanChangeItsLength()
    {
        var line = Segment("Çizgi", 5, 5, 100);
        var result = Run(Doc(line), d => PieceOperations.Resize(d, line.Id, 50, null));
        Assert.Equal(Pts((5, 5), (55, 5)), result.Piece!.Points);
    }

    [Fact]
    public void AVerticalLineCannotGainWidth()
    {
        var line = new PatternPiece { Name = "Dikey", Closed = false, Points = [new(0, 0), new(0, 100)] };
        AssertRefused(PieceOperations.Resize(Doc(line), line.Id, 10, null), PieceOperationError.InvalidValue,
            "“Dikey” parçasının genişliği 0; genişlik değiştirilemez. Yalnızca yüksekliği belirtin.");
    }

    [Fact]
    public void PieceWithCurvesIsRefused()
    {
        var curved = PieceOperationCurveTests.CurvedPiece("Eğri");
        AssertRefused(PieceOperations.Resize(Doc(curved), curved.Id, 20, 20), PieceOperationError.Unsupported,
            "“Eğri” yay veya eğri içeriyor; yeniden boyutlandırma eğrilerin kontrol noktalarını bozacağı için uygulanmadı. Eğri içeren parçalarda yalnızca taşıma ve çoğaltma desteklenir.");
    }

    [Fact]
    public void PieceOnALockedLayerIsRefused()
    {
        var layer = new Layer { Name = "Kilitli", Locked = true };
        var document = WithLayers(OneRect(), layer);
        document.Pieces[0].LayerId = layer.Id;
        AssertRefused(PieceOperations.Resize(document, document.Pieces[0].Id, 10, 10), PieceOperationError.LayerLocked,
            "Parça kilitli bir katmanda. Ayrıntılı görünümde katmanın kilidini açın.");
    }

    [Fact]
    public void UnknownPieceIsRefused()
    {
        AssertRefused(PieceOperations.Resize(OneRect(), Guid.NewGuid(), 10, 10), PieceOperationError.PieceNotFound,
            "Parça bulunamadı; silinmiş olabilir. Parçayı yeniden seçip komutu tekrar verin.");
    }

    [Fact]
    public void ResizeUndoesInOneStep()
    {
        var document = Doc(Rect("A", 0, 0, 10, 10), Rect("B", 50, 0, 10, 10));
        AssertUndoRoundTrip(document, d => PieceOperations.Resize(d, d.Pieces[0].Id, 33, 77));
    }

    [Fact]
    public void ANotchMarkWithAStrayVertexIndexIsRefusedNotMisreported()
    {
        var piece = Rect("A", 0, 0, 10, 10);
        piece.Notches = [new NotchMark { VertexIndex = 99, Position = new(1, 1) }];
        var result = PieceOperations.Resize(Doc(piece), piece.Id, 20, 20);
        Assert.False(result.Succeeded);
        Assert.NotEqual(PieceOperationError.OutOfRange, result.ErrorCode);
    }
}
