using TekstilCAD.Core;
using static TekstilCAD.Tests.PieceOperationTestData;

namespace TekstilCAD.Tests;

public sealed class PieceDuplicateTests
{
    [Fact]
    public void DefaultCopyIsTwentyMillimetresRightAndDownWithANewIdAndName()
    {
        var document = OneRect();
        var source = document.Pieces[0];
        var result = Run(document, d => PieceOperations.Duplicate(d, source.Id));
        var copy = result.Piece!;
        Assert.Equal(2, result.Document!.Pieces.Count);
        Assert.Equal(Pts((20, 20), (140, 20), (140, 100), (20, 100)), copy.Points);
        Assert.Equal("Ön beden kopya", copy.Name);
        Assert.NotEqual(source.Id, copy.Id);
        Assert.NotEqual(Guid.Empty, copy.Id);
        Assert.True(copy.Closed);
        Assert.Equal(source.Id, result.Document.Pieces[0].Id);
        Assert.Equal(copy.Id, result.Document.Pieces[1].Id);
        Assert.Equal(source.Points, result.Document.Pieces[0].Points);
        Assert.Equal("“Ön beden” çoğaltıldı: “Ön beden kopya”, 20 mm sağa, 20 mm aşağı kaydırıldı.", result.Summary);
    }

    [Theory]
    [InlineData(0, 0, "aynı konumda")]
    [InlineData(-10, 5, "10 mm sola, 5 mm aşağı kaydırıldı")]
    [InlineData(0, -7.5, "7,5 mm yukarı kaydırıldı")]
    public void ExplicitOffsetsAreAppliedAndDescribed(double dx, double dy, string described)
    {
        var document = OneRect();
        var result = Run(document, d => PieceOperations.Duplicate(d, d.Pieces[0].Id, dx, dy));
        Assert.Equal(Pts((dx, dy), (120 + dx, dy), (120 + dx, 80 + dy), (dx, 80 + dy)), result.Piece!.Points);
        Assert.EndsWith($", {described}.", result.Summary);
    }

    [Fact]
    public void SeveralCopiesGetDistinctIds()
    {
        var document = OneRect();
        for (var i = 0; i < 60; i++) document = Run(document, d => PieceOperations.Duplicate(d, d.Pieces[0].Id)).Document!;
        Assert.Equal(61, document.Pieces.Count);
        Assert.Equal(61, document.Pieces.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void CopyKeepsLayerClosureMetadataAndGrainlineAndDoesNotShareObjects()
    {
        var layer = new Layer { Name = "Astar" };
        var piece = Rect("Kol", 0, 0, 100, 50);
        piece.LayerId = layer.Id;
        piece.Metadata = new PieceMetadata { Role = "Kesim", Size = "M", Material = "Pamuk", Quantity = 2 };
        piece.Grainline = new Grainline { Start = new(10, 10), End = new(10, 40), NeedsReview = true };
        var document = WithLayers(Doc(piece), layer);
        var result = Run(document, d => PieceOperations.Duplicate(d, piece.Id, 200, 0));
        var copy = result.Piece!;
        Assert.Equal(layer.Id, copy.LayerId);
        Assert.Equal("Kesim", copy.Metadata!.Role);
        Assert.Equal(2, copy.Metadata.Quantity);
        Assert.NotSame(piece.Metadata, copy.Metadata);
        Assert.Equal(new Point2(210, 10), copy.Grainline!.Start);
        Assert.Equal(new Point2(210, 40), copy.Grainline.End);
        Assert.True(copy.Grainline.NeedsReview);
        Assert.Equal(["Üretim bilgisi kopyalandı; adedi kontrol edin."], result.Warnings);
    }

    [Fact]
    public void NotchMarksMoveWithTheCopyAndStayOnTheirVertices()
    {
        var triangle = new PatternPiece { Name = "Üçgen", Points = [new(0, 0), new(100, 0), new(0, 100)],
            Notches = [new NotchMark { VertexIndex = 1, Position = new(100, 0) }] };
        var result = Run(Doc(triangle), d => PieceOperations.Duplicate(d, triangle.Id, 5, 6));
        var copy = result.Piece!;
        Assert.Equal(new Point2(105, 6), copy.Notches![0].Position);
        Assert.Equal(copy.Points[1], copy.Notches[0].Position);
        Assert.Equal(new Point2(100, 0), result.Document!.Pieces[0].Notches![0].Position);
    }

    [Fact]
    public void GradingRuleIsNotCopiedAndTheWarningSaysSo()
    {
        var piece = Rect("Ön beden", 0, 0, 10, 10);
        var rule = new GradingRuleSet { SourcePieceId = piece.Id, SourceClosed = true, SourcePoints = [.. piece.Points],
            Deltas = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)] };
        var document = Doc(piece);
        document.GradingRules.Add(rule);
        var result = Run(document, d => PieceOperations.Duplicate(d, piece.Id));
        Assert.Single(result.Document!.GradingRules);
        Assert.Equal(piece.Id, result.Document.GradingRules[0].SourcePieceId);
        Assert.Equal(["Serileme kuralı kopyalanmadı; kopya için gerekirse yeniden tanımlayın."], result.Warnings);
    }

    [Fact]
    public void LongNamesAreShortenedSoTheCopyStillFitsTheStoredLimit()
    {
        var piece = Rect(new string('k', 1024), 0, 0, 10, 10);
        var result = Run(Doc(piece), d => PieceOperations.Duplicate(d, piece.Id));
        Assert.Equal(1024, result.Piece!.Name.Length);
        Assert.EndsWith(" kopya", result.Piece.Name);
    }

    [Fact]
    public void ShorteningNeverSplitsASurrogatePair()
    {
        // 1018 = 1024 - " kopya".Length: place a pair across that boundary.
        var name = new string('k', 1017) + "🧵" + "xyz";
        var piece = Rect(name, 0, 0, 10, 10);
        var result = Run(Doc(piece), d => PieceOperations.Duplicate(d, piece.Id));
        Assert.True(result.Piece!.Name.Length <= 1024);
        Assert.EndsWith(" kopya", result.Piece.Name);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    public void NonFiniteOffsetIsRefused(double dx, double dy)
    {
        var document = OneRect();
        AssertRefused(PieceOperations.Duplicate(document, document.Pieces[0].Id, dx, dy), PieceOperationError.InvalidValue,
            "Kaydırma miktarı geçerli bir sayı olmalı (örneğin 120); girilen değer kullanılamadı.");
    }

    [Fact]
    public void OffsetBeyondTheLimitIsRefused()
    {
        var document = OneRect();
        AssertRefused(PieceOperations.Duplicate(document, document.Pieces[0].Id, 1e8, 0), PieceOperationError.OutOfRange,
            "Sonuç çalışma alanının dışına taşıyor (her eksende en çok ±100.000.000 mm). Daha küçük bir ölçü veya konum girin.");
    }

    [Fact]
    public void UnknownPieceIsRefused()
    {
        AssertRefused(PieceOperations.Duplicate(OneRect(), Guid.NewGuid()), PieceOperationError.PieceNotFound,
            "Parça bulunamadı; silinmiş olabilir. Parçayı yeniden seçip komutu tekrar verin.");
    }

    [Fact]
    public void PieceOnALockedLayerIsNotCopiedIntoIt()
    {
        var layer = new Layer { Name = "Kilitli", Locked = true };
        var document = WithLayers(OneRect(), layer);
        document.Pieces[0].LayerId = layer.Id;
        AssertRefused(PieceOperations.Duplicate(document, document.Pieces[0].Id), PieceOperationError.LayerLocked,
            "Parça kilitli bir katmanda. Ayrıntılı görünümde katmanın kilidini açın.");
    }

    [Fact]
    public void DuplicateUndoesInOneStep()
    {
        AssertUndoRoundTrip(Doc(Rect("A", 0, 0, 10, 10), Rect("B", 50, 0, 10, 10)), d => PieceOperations.Duplicate(d, d.Pieces[1].Id));
    }
}
