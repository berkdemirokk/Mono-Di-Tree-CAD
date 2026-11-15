using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using static TekstilCAD.Tests.PieceOperationTestData;

namespace TekstilCAD.Tests;

public sealed class PieceMoveTests
{
    private const string OutOfRange = "Sonuç çalışma alanının dışına taşıyor (her eksende en çok ±100.000.000 mm). Daha küçük bir ölçü veya konum girin.";

    [Theory]
    [InlineData(20, 0)]
    [InlineData(-20, 0)]
    [InlineData(0, 15)]
    [InlineData(0, -15)]
    [InlineData(7.5, -2.5)]
    [InlineData(1000000, 1000000)]
    [InlineData(-99999000, 0)]
    public void MoveByAddsTheOffsetToEveryPointInOrder(double dx, double dy)
    {
        var document = OneRect();
        var id = document.Pieces[0].Id;
        var result = Run(document, d => PieceOperations.MoveBy(d, id, dx, dy));
        var expected = document.Pieces[0].Points.Select(p => new Point2(p.X + dx, p.Y + dy)).ToList();
        Assert.Equal(expected, Find(result, id).Points);
        Assert.True(Find(result, id).Closed);
    }

    [Fact]
    public void MoveByKeepsTheIdNameAndOrderOfAllPieces()
    {
        var document = Doc(Rect("A", 0, 0, 10, 10), Rect("B", 50, 0, 10, 10), Rect("C", 100, 0, 10, 10));
        var id = document.Pieces[1].Id;
        var result = Run(document, d => PieceOperations.MoveBy(d, id, 5, 5));
        Assert.Equal(document.Pieces.Select(p => p.Id), result.Document!.Pieces.Select(p => p.Id));
        Assert.Equal(["A", "B", "C"], result.Document.Pieces.Select(p => p.Name));
        Assert.Equal(document.Pieces[0].Points, result.Document.Pieces[0].Points);
        Assert.Equal(document.Pieces[2].Points, result.Document.Pieces[2].Points);
        Assert.Equal(id, result.PieceId);
    }

    [Theory]
    [InlineData(20, 0, "“Ön beden” 20 mm sağa taşındı.")]
    [InlineData(-5, 10, "“Ön beden” 5 mm sola, 10 mm aşağı taşındı.")]
    [InlineData(0, -2.5, "“Ön beden” 2,5 mm yukarı taşındı.")]
    [InlineData(0, 0, "“Ön beden” zaten orada; taşıma gerekmedi.")]
    public void SummaryNamesTheDirectionsInTurkish(double dx, double dy, string expected)
    {
        var document = OneRect();
        Assert.Equal(expected, PieceOperations.MoveBy(document, document.Pieces[0].Id, dx, dy).Summary);
    }

    [Fact]
    public void MovingByZeroSucceedsWithoutChangingTheDocument()
    {
        var document = OneRect();
        var result = Run(document, d => PieceOperations.MoveBy(d, document.Pieces[0].Id, 0, 0));
        Assert.False(result.ChangesDocument);
        var session = new DocumentSession(document);
        Assert.False(session.Execute(result.ApplyTo, ProjectStorage.Validate));
        Assert.Equal(0, session.UndoCount);
    }

    [Fact]
    public void MovingAndMovingBackLeavesIntegerCoordinatesBitForBit()
    {
        var document = Doc(Rect("A", 3, 7, 120, 80));
        var id = document.Pieces[0].Id;
        var there = Run(document, d => PieceOperations.MoveBy(d, id, 1234, -987));
        var back = Run(there.Document!, d => PieceOperations.MoveBy(d, id, -1234, 987));
        Assert.True(DocumentSession.ContentEquals(document, back.Document!));
    }

    [Fact]
    public void MovingAndMovingBackWithDyadicFractionsIsExact()
    {
        var document = Doc(Rect("A", 0.5, 0.25, 12.5, 8.125));
        var id = document.Pieces[0].Id;
        var result = document;
        for (var i = 0; i < 50; i++) result = Run(result, d => PieceOperations.MoveBy(d, id, 0.375, -0.125)).Document!;
        for (var i = 0; i < 50; i++) result = Run(result, d => PieceOperations.MoveBy(d, id, -0.375, 0.125)).Document!;
        Assert.True(DocumentSession.ContentEquals(document, result));
    }

    [Fact]
    public void RepeatedDecimalMovesDoNotDriftBeyondOneTrillionthOfAMillimetre()
    {
        var document = Doc(Rect("A", 0.1, 0.2, 0.3, 0.4));
        var id = document.Pieces[0].Id;
        var result = document;
        for (var i = 0; i < 100; i++) result = Run(result, d => PieceOperations.MoveBy(d, id, 0.1, 0.2)).Document!;
        for (var i = 0; i < 100; i++) result = Run(result, d => PieceOperations.MoveBy(d, id, -0.1, -0.2)).Document!;
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(document.Pieces[0].Points[i].X, result.Pieces[0].Points[i].X, 12);
            Assert.Equal(document.Pieces[0].Points[i].Y, result.Pieces[0].Points[i].Y, 12);
        }
    }

    [Fact]
    public void MoveKeepsAnOpenPieceOpenAndTheExplicitClosingVertex()
    {
        var closedWithDuplicate = new PatternPiece { Name = "K", Points = [new(0, 0), new(10, 0), new(10, 10), new(0, 0)] };
        var line = Segment("Ç", 0, 0, 10);
        var document = Doc(closedWithDuplicate, line);
        var first = Run(document, d => PieceOperations.MoveBy(d, closedWithDuplicate.Id, 5, 5));
        Assert.Equal(first.Piece!.Points[0], first.Piece.Points[^1]);
        Assert.True(first.Piece.Closed);
        var second = Run(document, d => PieceOperations.MoveBy(d, line.Id, 5, 5));
        Assert.False(second.Piece!.Closed);
        Assert.Equal(Pts((5, 5), (15, 5)), second.Piece.Points);
    }

    [Theory]
    [InlineData(1e8, 0)]
    [InlineData(0, 1e8)]
    [InlineData(-1e8 - 1, 0)]
    [InlineData(0, -1e8 - 1)]
    public void MoveBeyondTheCoordinateLimitIsRefused(double dx, double dy)
    {
        var document = OneRect();
        AssertRefused(PieceOperations.MoveBy(document, document.Pieces[0].Id, dx, dy), PieceOperationError.OutOfRange, OutOfRange);
    }

    [Fact]
    public void MoveLandingExactlyOnTheLimitIsAccepted()
    {
        var document = OneRect();
        var id = document.Pieces[0].Id;
        var result = Run(document, d => PieceOperations.MoveBy(d, id, 1e8 - 120, 1e8 - 80));
        Assert.Equal(new Point2(1e8, 1e8), Find(result, id).Points[2]);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, 1)]
    public void NonFiniteMoveIsRefused(double dx, double dy)
    {
        var document = OneRect();
        AssertRefused(PieceOperations.MoveBy(document, document.Pieces[0].Id, dx, dy), PieceOperationError.InvalidValue,
            "Taşıma miktarı geçerli bir sayı olmalı (örneğin 120); girilen değer kullanılamadı.");
    }

    [Fact]
    public void ATinyMoveAtHugeCoordinatesIsRefusedAsNotRepresentable()
    {
        var document = Doc(Rect("Ön beden", 1e7, 1e7, 100, 100));
        var result = PieceOperations.MoveBy(document, document.Pieces[0].Id, 0.001, 0);
        Assert.False(result.Succeeded);
        Assert.Equal(PieceOperationError.Precision, result.ErrorCode);
        Assert.Equal("“Ön beden” üzerinde işlem sayısal hassasiyetle güvenle yapılamadı; hiçbir şey değişmedi. Daha küçük bir değer girin veya parçayı orijine yaklaştırın.", result.Error);
        Assert.NotNull(result.ErrorDetail);
    }

    [Fact]
    public void UnknownPieceIsRefused()
    {
        AssertRefused(PieceOperations.MoveBy(OneRect(), Guid.NewGuid(), 1, 1), PieceOperationError.PieceNotFound,
            "Parça bulunamadı; silinmiş olabilir. Parçayı yeniden seçip komutu tekrar verin.");
    }

    [Fact]
    public void PieceOnALockedLayerIsRefused()
    {
        var layer = new Layer { Name = "Kilitli", Locked = true };
        var document = WithLayers(OneRect(), layer);
        document.Pieces[0].LayerId = layer.Id;
        AssertRefused(PieceOperations.MoveBy(document, document.Pieces[0].Id, 1, 1), PieceOperationError.LayerLocked,
            "Parça kilitli bir katmanda. Ayrıntılı görünümde katmanın kilidini açın.");
    }

    [Theory]
    [InlineData(100, 200)]
    [InlineData(-30, -40.5)]
    [InlineData(0, 0)]
    [InlineData(10, 50)]
    public void MoveToPutsTheTopLeftCornerOfTheBoundingBoxAtTheTarget(double x, double y)
    {
        var document = Doc(Rect("A", 10, 50, 120, 80));
        var id = document.Pieces[0].Id;
        var result = Run(document, d => PieceOperations.MoveTo(d, id, x, y));
        var box = Geometry.Bounds(Find(result, id).Points);
        Assert.Equal(x, box.MinX);
        Assert.Equal(y, box.MinY);
        Assert.Equal(120, box.Width);
        Assert.Equal(80, box.Height);
    }

    [Fact]
    public void MoveToUsesTheBoundingBoxNotTheFirstPoint()
    {
        var triangle = new PatternPiece { Name = "Üçgen", Points = [new(50, 40), new(10, 90), new(80, 90)] };
        var document = Doc(triangle);
        var result = Run(document, d => PieceOperations.MoveTo(d, triangle.Id, 0, 0));
        Assert.Equal(Pts((40, 0), (0, 50), (70, 50)), result.Piece!.Points);
    }

    [Fact]
    public void MoveToTheCurrentPositionChangesNothing()
    {
        var document = Doc(Rect("A", 10, 50, 120, 80));
        var result = Run(document, d => PieceOperations.MoveTo(d, document.Pieces[0].Id, 10, 50));
        Assert.False(result.ChangesDocument);
    }

    [Fact]
    public void MoveToWithOnlyYChangingKeepsTheXBitsOfEveryPoint()
    {
        var document = Doc(Rect("A", 0.1, 0.2, 0.3, 0.4));
        var result = Run(document, d => PieceOperations.MoveTo(d, document.Pieces[0].Id, 0.1, 5));
        for (var i = 0; i < 4; i++)
            Assert.Equal(BitConverter.DoubleToInt64Bits(document.Pieces[0].Points[i].X), BitConverter.DoubleToInt64Bits(result.Piece!.Points[i].X));
    }

    [Fact]
    public void MoveToSummaryNamesThePosition()
    {
        var document = OneRect();
        Assert.Equal("“Ön beden” sol üst köşesi X 100 mm, Y 200,5 mm konumuna taşındı.", PieceOperations.MoveTo(document, document.Pieces[0].Id, 100, 200.5).Summary);
    }

    [Theory]
    [InlineData(1e8 + 1, 0)]
    [InlineData(0, -1e8 - 1)]
    [InlineData(1e8, 0)]
    public void MoveToBeyondTheLimitIsRefused(double x, double y)
    {
        var document = OneRect();
        AssertRefused(PieceOperations.MoveTo(document, document.Pieces[0].Id, x, y), PieceOperationError.OutOfRange, OutOfRange);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.NegativeInfinity)]
    public void MoveToNonFiniteIsRefused(double x, double y)
    {
        var document = OneRect();
        AssertRefused(PieceOperations.MoveTo(document, document.Pieces[0].Id, x, y), PieceOperationError.InvalidValue,
            "Konum (X ve Y) geçerli bir sayı olmalı (örneğin 120); girilen değer kullanılamadı.");
    }

    [Fact]
    public void MoveByUndoesInOneStep()
    {
        var document = Doc(Rect("A", 0, 0, 10, 10), Rect("B", 50, 0, 10, 10));
        AssertUndoRoundTrip(document, d => PieceOperations.MoveBy(d, d.Pieces[1].Id, 12, -3));
    }

    [Fact]
    public void MoveToUndoesInOneStep()
    {
        AssertUndoRoundTrip(OneRect(), d => PieceOperations.MoveTo(d, d.Pieces[0].Id, 500, 600));
    }

    [Theory]
    [InlineData(1000, 12.3, 45.6)]
    [InlineData(5000, 0.1, 0.7)]
    [InlineData(123456.789, 0.1, 0.2)]
    [InlineData(-5000, -0.3, 0.9)]
    public void MoveTowardsTheOriginToAFineTargetIsNotRefusedForRounding(double start, double x, double y)
    {
        var document = Doc(Rect("A", start, start, 100, 50));
        var result = Run(document, d => PieceOperations.MoveTo(d, d.Pieces[0].Id, x, y));
        var box = Geometry.Bounds(result.Piece!.Points);
        Assert.Equal(x, box.MinX, 9);
        Assert.Equal(y, box.MinY, 9);
        Assert.Equal(100, box.Width, 9);
    }

    [Fact]
    public void MoveToTheCurrentPositionSaysNothingMoved()
    {
        var document = Doc(Rect("A", 10, 50, 120, 80));
        var result = PieceOperations.MoveTo(document, document.Pieces[0].Id, 10, 50);
        Assert.Equal("“A” zaten orada; taşıma gerekmedi.", result.Summary);
    }
}
