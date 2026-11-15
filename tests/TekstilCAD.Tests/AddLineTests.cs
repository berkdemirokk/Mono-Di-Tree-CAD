using TekstilCAD.Core;
using static TekstilCAD.Tests.PieceOperationTestData;

namespace TekstilCAD.Tests;

public sealed class AddLineTests
{
    [Fact]
    public void FirstLineIsAnOpenHorizontalTwoPointPieceAtTheOrigin()
    {
        var result = Run(new ProjectDocument(), d => PieceOperations.AddLine(d, 100));
        var piece = result.Piece!;
        Assert.Equal(Pts((0, 0), (100, 0)), piece.Points);
        Assert.False(piece.Closed);
        Assert.Equal("Parça 1", piece.Name);
        Assert.Equal("“Parça 1” çizgisi eklendi: 100 mm, sol uç X 0 mm, Y 0 mm.", result.Summary);
    }

    [Fact]
    public void LineIsPlacedRightOfTheRightmostPieceWithTheSameTop()
    {
        var document = Doc(Rect("Ön beden", 10, 40, 120, 80));
        var result = Run(document, d => PieceOperations.AddLine(d, 30.5));
        Assert.Equal(Pts((150, 40), (180.5, 40)), result.Piece!.Points);
    }

    [Fact]
    public void ExplicitOriginIsTheLeftEnd()
    {
        var result = Run(OneRect(), d => PieceOperations.AddLine(d, 10, new Point2(-5, 7)));
        Assert.Equal(Pts((-5, 7), (5, 7)), result.Piece!.Points);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(-1, "-1")]
    [InlineData(0.001, "0,001")]
    public void LengthAtOrBelowTheMinimumIsRefused(double length, string shown)
    {
        AssertRefused(PieceOperations.AddLine(new ProjectDocument(), length), PieceOperationError.InvalidValue,
            $"Uzunluk 0,001 mm değerinden büyük olmalı; girilen: {shown} mm. Daha büyük bir ölçü girin.");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonFiniteLengthIsRefused(double length)
    {
        AssertRefused(PieceOperations.AddLine(new ProjectDocument(), length), PieceOperationError.InvalidValue,
            "Uzunluk geçerli bir sayı olmalı (örneğin 120); girilen değer kullanılamadı.");
    }

    [Fact]
    public void LineBeyondTheCoordinateLimitIsRefused()
    {
        AssertRefused(PieceOperations.AddLine(new ProjectDocument(), 1.5e8), PieceOperationError.OutOfRange,
            "Sonuç çalışma alanının dışına taşıyor (her eksende en çok ±100.000.000 mm). Daha küçük bir ölçü veya konum girin.");
    }

    [Fact]
    public void LineSharesTheNumberingOfRectangles()
    {
        var document = Run(new ProjectDocument(), d => PieceOperations.AddRectangle(d, 10, 10)).Document!;
        Assert.Equal("Parça 2", Run(document, d => PieceOperations.AddLine(d, 10)).Piece!.Name);
    }

    [Fact]
    public void LineUndoesInOneStep()
    {
        AssertUndoRoundTrip(OneRect(), d => PieceOperations.AddLine(d, 25));
    }

    [Fact]
    public void LockedLayerRefusesANewLine()
    {
        var layer = new Layer { Name = "Kilitli", Locked = true };
        var document = WithLayers(OneRect(), layer);
        AssertRefused(PieceOperations.AddLine(document, 10, null, layer.Id), PieceOperationError.LayerLocked,
            "“Kilitli” katmanı kilitli; parça eklenmedi. Önce kilidi açın ya da başka bir katman seçin.");
    }
}
