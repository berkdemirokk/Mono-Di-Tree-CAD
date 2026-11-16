using TekstilCAD.Core;
using static TekstilCAD.Tests.PieceOperationTestData;

namespace TekstilCAD.Tests;

/// <summary>The strict resize used by the command box: rectangles only, refusals R10 and R11 of the command design.</summary>
public sealed class PieceResizeRectangleTests
{
    private const string NotRectangle = "Bu parça dikdörtgen değil; ölçüsü yazıyla değiştirilemez. Yeni bir dikdörtgen için şunu yazın: 95 x 80 dikdörtgen";
    private const string Extras = "Bu parçada iplik yönü, köşe işareti, serileme kuralı ya da eğri ayarı var; ölçüsü yazıyla değiştirilemez. Parçayı silip yeni ölçüyle çizebilirsiniz.";

    [Fact]
    public void PlainRectangleIsResizedKeepingTheTopLeft()
    {
        var document = Doc(Rect("A", 20, 20, 120, 80));
        var result = Run(document, d => PieceOperations.ResizeRectangle(d, d.Pieces[0].Id, 95, null));
        Assert.Equal(Pts((20, 20), (115, 20), (115, 100), (20, 100)), result.Piece!.Points);
    }

    [Fact]
    public void BothDimensionsCanChangeAtOnce()
    {
        var document = Doc(Rect("A", 0, 0, 120, 80));
        var result = Run(document, d => PieceOperations.ResizeRectangle(d, d.Pieces[0].Id, 60, 160));
        Assert.Equal(Pts((0, 0), (60, 0), (60, 160), (0, 160)), result.Piece!.Points);
    }

    [Fact]
    public void ARectangleDrawnAnticlockwiseOrFromAnotherCornerStillCounts()
    {
        var piece = new PatternPiece { Name = "A", Points = [new(10, 10), new(10, 50), new(40, 50), new(40, 10)] };
        var result = Run(Doc(piece), d => PieceOperations.ResizeRectangle(d, piece.Id, 60, null));
        Assert.Equal(Pts((10, 10), (10, 50), (70, 50), (70, 10)), result.Piece!.Points);
    }

    [Fact]
    public void ATriangleIsRefusedAsNotARectangle()
    {
        var triangle = new PatternPiece { Name = "Üçgen", Points = [new(0, 0), new(100, 0), new(0, 100)] };
        AssertRefused(PieceOperations.ResizeRectangle(Doc(triangle), triangle.Id, 50, null), PieceOperationError.Unsupported, NotRectangle);
    }

    [Fact]
    public void ATiltedFourPointShapeIsRefusedAsNotARectangle()
    {
        var diamond = new PatternPiece { Name = "Baklava", Points = [new(50, 0), new(100, 50), new(50, 100), new(0, 50)] };
        AssertRefused(PieceOperations.ResizeRectangle(Doc(diamond), diamond.Id, 50, null), PieceOperationError.Unsupported, NotRectangle);
    }

    [Fact]
    public void AnOpenLineIsRefusedAsNotARectangle()
    {
        var line = Segment("Çizgi", 0, 0, 100);
        AssertRefused(PieceOperations.ResizeRectangle(Doc(line), line.Id, 50, null), PieceOperationError.Unsupported, NotRectangle);
    }

    [Fact]
    public void ARectangleWithAnExplicitClosingVertexIsNotTheFourPointRectangle()
    {
        var piece = new PatternPiece { Name = "K", Points = [new(0, 0), new(30, 0), new(30, 30), new(0, 30), new(0, 0)] };
        AssertRefused(PieceOperations.ResizeRectangle(Doc(piece), piece.Id, 50, null), PieceOperationError.Unsupported, NotRectangle);
    }

    [Fact]
    public void AFlatFourPointShapeIsRefusedAsNotARectangle()
    {
        var flat = new PatternPiece { Name = "Düz", Points = [new(0, 0), new(10, 0), new(20, 0), new(30, 0)] };
        AssertRefused(PieceOperations.ResizeRectangle(Doc(flat), flat.Id, 50, null), PieceOperationError.Unsupported, NotRectangle);
    }

    [Fact]
    public void AGrainlineMakesTheResizeRefused()
    {
        var piece = Rect("A", 0, 0, 100, 50);
        piece.Grainline = new Grainline { Start = new(10, 10), End = new(10, 40) };
        AssertRefused(PieceOperations.ResizeRectangle(Doc(piece), piece.Id, 50, null), PieceOperationError.Unsupported, Extras);
    }

    [Fact]
    public void ANotchMarkMakesTheResizeRefused()
    {
        var piece = Rect("A", 0, 0, 100, 50);
        piece.Notches = [new NotchMark { VertexIndex = 1, Position = piece.Points[1] }];
        AssertRefused(PieceOperations.ResizeRectangle(Doc(piece), piece.Id, 50, null), PieceOperationError.Unsupported, Extras);
    }

    [Fact]
    public void AGradingRuleMakesTheResizeRefused()
    {
        var piece = Rect("A", 0, 0, 100, 50);
        var document = Doc(piece);
        document.GradingRules.Add(new GradingRuleSet { SourcePieceId = piece.Id, SourceClosed = true, SourcePoints = [.. piece.Points],
            Deltas = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)] });
        AssertRefused(PieceOperations.ResizeRectangle(document, piece.Id, 50, null), PieceOperationError.Unsupported, Extras);
    }

    [Fact]
    public void ACurvePieceIsRefusedWithTheExtrasText()
    {
        var curved = PieceOperationCurveTests.CurvedPiece("Eğri");
        AssertRefused(PieceOperations.ResizeRectangle(Doc(curved), curved.Id, 50, null), PieceOperationError.Unsupported, Extras);
    }

    [Fact]
    public void LockedLayerWinsOverEverythingElse()
    {
        var layer = new Layer { Name = "Kilitli", Locked = true };
        var document = WithLayers(OneRect(), layer);
        document.Pieces[0].LayerId = layer.Id;
        AssertRefused(PieceOperations.ResizeRectangle(document, document.Pieces[0].Id, 50, null), PieceOperationError.LayerLocked,
            "Parça kilitli bir katmanda. Ayrıntılı görünümde katmanın kilidini açın.");
    }

    [Fact]
    public void SameSizeSaysThereIsNothingToChange()
    {
        var document = OneRect();
        var result = Run(document, d => PieceOperations.ResizeRectangle(d, d.Pieces[0].Id, 120, 80));
        Assert.False(result.ChangesDocument);
        Assert.Equal("Ölçü zaten 120 × 80 mm. Değişiklik yok.", result.Summary);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20001)]
    public void OutOfRangeWidthIsRefusedWithTheRangeText(double width)
    {
        var document = OneRect();
        var result = PieceOperations.ResizeRectangle(document, document.Pieces[0].Id, width, null);
        Assert.False(result.Succeeded);
        Assert.Equal("Ölçü 1 ile 20000 mm arasında olmalı. Örnek: 120 x 80 dikdörtgen", result.Error);
    }

    [Fact]
    public void UnknownPieceIsRefused()
    {
        AssertRefused(PieceOperations.ResizeRectangle(OneRect(), Guid.NewGuid(), 10, 10), PieceOperationError.PieceNotFound,
            "Parça bulunamadı; silinmiş olabilir. Parçayı yeniden seçip komutu tekrar verin.");
    }

    [Fact]
    public void UndoesInOneStep()
    {
        AssertUndoRoundTrip(Doc(Rect("A", 0, 0, 10, 10), Rect("B", 50, 0, 10, 10)), d => PieceOperations.ResizeRectangle(d, d.Pieces[0].Id, 33, 77));
    }
}
