using TekstilCAD.Core;
using static TekstilCAD.Tests.PieceOperationTestData;

namespace TekstilCAD.Tests;

public sealed class PieceRenameTests
{
    [Theory]
    [InlineData("Arka beden", "Arka beden")]
    [InlineData("  Kol  ", "Kol")]
    [InlineData("\tÖn yaka\n", "Ön yaka")]
    [InlineData("İşlem ığ", "İşlem ığ")]
    [InlineData("a", "a")]
    public void NameIsTrimmedAndStored(string typed, string stored)
    {
        var document = OneRect();
        var id = document.Pieces[0].Id;
        var result = Run(document, d => PieceOperations.Rename(d, id, typed));
        Assert.Equal(stored, Find(result, id).Name);
    }

    [Fact]
    public void RenameChangesOnlyTheName()
    {
        var piece = Rect("Ön beden", 5, 6, 7, 8);
        piece.Metadata = new PieceMetadata { Role = "Kesim", Quantity = 2 };
        var document = Doc(piece, Rect("Diğer", 50, 0, 1, 1));
        var result = Run(document, d => PieceOperations.Rename(d, piece.Id, "Arka beden"));
        var renamed = result.Piece!;
        Assert.Equal(piece.Points, renamed.Points);
        Assert.Equal(piece.Id, renamed.Id);
        Assert.Equal("Kesim", renamed.Metadata!.Role);
        Assert.Equal("Diğer", result.Document!.Pieces[1].Name);
        Assert.Equal("“Ön beden” parçasının adı “Arka beden” yapıldı.", result.Summary);
    }

    [Fact]
    public void ExactlyOneHundredTwentyEightCharactersAreAccepted()
    {
        var name = new string('a', 128);
        var document = OneRect();
        Assert.Equal(name, Run(document, d => PieceOperations.Rename(d, d.Pieces[0].Id, name)).Piece!.Name);
    }

    [Fact]
    public void OneHundredTwentyNineCharactersAreRefused()
    {
        var document = OneRect();
        AssertRefused(PieceOperations.Rename(document, document.Pieces[0].Id, new string('a', 129)), PieceOperationError.InvalidValue,
            "Ad en çok 128 karakter olabilir (girilen: 129). Daha kısa bir ad yazın.");
    }

    [Fact]
    public void SpacesAroundALongNameDoNotCountTowardsTheLimit()
    {
        var document = OneRect();
        var result = Run(document, d => PieceOperations.Rename(d, d.Pieces[0].Id, "  " + new string('b', 128) + "  "));
        Assert.Equal(128, result.Piece!.Name.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    [InlineData(null)]
    public void EmptyNamesAreRefused(string? name)
    {
        var document = OneRect();
        AssertRefused(PieceOperations.Rename(document, document.Pieces[0].Id, name), PieceOperationError.InvalidValue,
            "Ad boş olamaz. 1–128 karakterlik bir ad yazın.");
    }

    [Theory]
    [InlineData("Ön\nbeden")]
    [InlineData("A\u0000B")]
    [InlineData("Kol\u0007")]
    public void ControlCharactersInsideTheNameAreRefused(string name)
    {
        var document = OneRect();
        AssertRefused(PieceOperations.Rename(document, document.Pieces[0].Id, name), PieceOperationError.InvalidValue,
            "Ad satır sonu veya denetim karakteri içeremez. Düz bir ad yazın.");
    }

    [Theory]
    [InlineData("A\ud800")]
    [InlineData("\udc00B")]
    public void UnpairedSurrogatesAreRefused(string name)
    {
        var document = OneRect();
        AssertRefused(PieceOperations.Rename(document, document.Pieces[0].Id, name), PieceOperationError.InvalidValue,
            "Ad geçersiz bir Unicode karakteri içeriyor. Başka bir ad yazın.");
    }

    [Fact]
    public void PairedSurrogatesAreAccepted()
    {
        var document = OneRect();
        Assert.Equal("Kol 🧵", Run(document, d => PieceOperations.Rename(d, d.Pieces[0].Id, "Kol 🧵")).Piece!.Name);
    }

    [Fact]
    public void RenamingToTheSameNameChangesNothing()
    {
        var document = OneRect();
        var result = Run(document, d => PieceOperations.Rename(d, d.Pieces[0].Id, "  Ön beden "));
        Assert.False(result.ChangesDocument);
        Assert.Equal("“Ön beden” zaten bu adı taşıyor.", result.Summary);
    }

    [Fact]
    public void TwoPiecesMayShareAName()
    {
        var document = Doc(Rect("A", 0, 0, 1, 1), Rect("B", 5, 0, 1, 1));
        var result = Run(document, d => PieceOperations.Rename(d, d.Pieces[1].Id, "A"));
        Assert.Equal(["A", "A"], result.Document!.Pieces.Select(p => p.Name));
    }

    [Fact]
    public void UnknownPieceIsRefused()
    {
        AssertRefused(PieceOperations.Rename(OneRect(), Guid.NewGuid(), "X"), PieceOperationError.PieceNotFound,
            "Parça bulunamadı; silinmiş olabilir. Parçayı yeniden seçip komutu tekrar verin.");
    }

    [Fact]
    public void PieceOnALockedLayerIsRefused()
    {
        var layer = new Layer { Name = "Kilitli", Locked = true };
        var document = WithLayers(OneRect(), layer);
        document.Pieces[0].LayerId = layer.Id;
        AssertRefused(PieceOperations.Rename(document, document.Pieces[0].Id, "X"), PieceOperationError.LayerLocked,
            "“Ön beden” kilitli bir katmanda; değiştirilemez. Önce katmanın kilidini açın.");
    }

    [Fact]
    public void LongOldNamesAreShortenedInMessages()
    {
        var piece = Rect(new string('x', 60), 0, 0, 5, 5);
        var document = Doc(piece);
        var result = PieceOperations.Rename(document, piece.Id, "Kısa");
        Assert.Equal($"“{new string('x', 40)}…” parçasının adı “Kısa” yapıldı.", result.Summary);
    }

    [Fact]
    public void ControlCharactersInOldNamesNeverReachTheMessage()
    {
        var piece = Rect("Ön\nbeden", 0, 0, 5, 5);
        var result = PieceOperations.Rename(Doc(piece), piece.Id, "Kısa");
        Assert.DoesNotContain('\n', result.Summary);
        Assert.Equal("“Ön·beden” parçasının adı “Kısa” yapıldı.", result.Summary);
    }

    [Fact]
    public void RenameUndoesInOneStep()
    {
        AssertUndoRoundTrip(OneRect(), d => PieceOperations.Rename(d, d.Pieces[0].Id, "Yeni"));
    }
}
