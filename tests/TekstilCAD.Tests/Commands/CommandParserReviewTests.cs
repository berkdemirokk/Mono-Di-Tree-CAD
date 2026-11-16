using TekstilCAD.Core.Commands;

namespace TekstilCAD.Tests;

/// <summary>Regression tests for the findings of the independent review of the parser.</summary>
public sealed class CommandParserReviewTests
{
    private static readonly string[] B1 = { "Ön beden", "Arka beden", "Kol" };

    private static ParseResult Run(string input, params string[] pieces) =>
        CommandParser.Parse(input, new ParserContext(pieces));

    // A lost minus sign must never run the command with a positive number.
    [Theory]
    [InlineData("- 25 sağa")]
    [InlineData("25 sağa - 10 yukarı")]
    [InlineData("-+25 sağa")]
    [InlineData("sağa - 25")]
    [InlineData("- 100 çizgi")]
    [InlineData("genişliği - 95 yap")]
    [InlineData("- 3 adım geri")]
    [InlineData("adet - 2 yap")]
    [InlineData("−25 sağa")]
    [InlineData("− 25 sağa")]
    public void ALooseMinusSignIsRefused(string input) => Assert.False(Run(input, B1).IsCommand, input);

    [Theory]
    [InlineData("-25 sağa")]
    [InlineData("--5 sağa")]
    [InlineData("eksi 25 sağa")]
    public void AGluedMinusSignIsTheNegativeNumberRefusal(string input) => Assert.Equal("R26", Run(input, B1).Code);

    [Fact]
    public void AHyphenBetweenTwoLettersIsWhiteSpace() => Assert.True(Run("sol-kol seç", "Sol kol").IsCommand);

    // A leading decimal separator must not be dropped (".5 cm" is not 5 cm).
    [Theory]
    [InlineData(".5 cm çizgi")]
    [InlineData(",5 cm çizgi")]
    [InlineData(",5 cm sağa")]
    [InlineData("-.5 cm çizgi")]
    [InlineData(".1 sağa")]
    [InlineData("çizgi ,5")]
    public void ALeadingDecimalSeparatorIsRefused(string input) => Assert.False(Run(input, B1).IsCommand, input);

    // Letters after an apostrophe are an ending only when they are a Turkish ending.
    [Theory]
    [InlineData("100'foo çizgi")]
    [InlineData("100'sil çizgi")]
    [InlineData("çizgi 100'sağa")]
    [InlineData("25 sağa'sil")]
    [InlineData("eni 95'foo yap")]
    [InlineData("dikdörtgen'foo 120 80")]
    public void UnknownLettersAfterAnApostropheAreNotSwallowed(string input) => Assert.False(Run(input, B1).IsCommand, input);

    [Theory]
    [InlineData("100'e 150 dikdörtgen")]
    [InlineData("100'lük kare")]
    [InlineData("10 mm'lik çizgi")]
    [InlineData("ön bedeni'ni seç")]
    public void RealEndingsAfterAnApostropheStillWork(string input) => Assert.True(Run(input, B1).IsCommand, input);

    // "ön" folds to "on", but only "on" is ten.
    [Theory]
    [InlineData("ön sağa")]
    [InlineData("ön sağa taşı")]
    [InlineData("ön yukarı")]
    [InlineData("ön geri")]
    [InlineData("ön adım geri")]
    [InlineData("yap ön 120")]
    [InlineData("dikdörtgen ön 80")]
    public void AnUmlautOnIsNotTheNumberTen(string input) => Assert.False(Run(input, B1).IsCommand, input);

    [Fact]
    public void PlainOnIsStillTen()
    {
        Assert.Equal(new MoveByCommand(10, 0), Run("on sağa", B1).Command);
        Assert.Equal(new MoveByCommand(10, 0), Run("ON SAĞA TAŞI", B1).Command);
        Assert.Equal(new UndoCommand(10), Run("on adım geri", B1).Command);
    }

    // The ending of a name must not eat a letter of the name.
    [Theory]
    [InlineData("payı sil", "pay", "Pay")]
    [InlineData("boyu sil", "boy", "Boy")]
    [InlineData("cebi seç", "cep", "Cep")]
    [InlineData("eteği seç", "etek", "Etek")]
    [InlineData("kapağı sil", "kapak", "Kapak")]
    [InlineData("kolu seç", "kol", "Kol")]
    public void EndingsOfPieceNamesAreStrippedCarefully(string input, string query, string piece)
    {
        var result = Run(input, piece, "Palto", "Pantolon");
        Assert.True(result.IsCommand, result.ToString());
        var name = result.Command switch { DeleteCommand d => d.Name, SelectCommand s => s.Name, _ => null };
        Assert.Equal(query, name);
        Assert.Equal(new[] { piece }, CommandParser.FindPieces(name, new[] { piece, "Palto", "Pantolon" }));
    }

    [Theory]
    [InlineData("ön beden 2'yi seç")]
    [InlineData("ön beden 2yi seç")]
    [InlineData("ön beden 2 seç")]
    public void ANumberedPieceNameResolvesWithAnEnding(string input) =>
        Assert.Equal("ön beden 2", Assert.IsType<SelectCommand>(Run(input, "Ön beden", "Ön beden 2").Command).Name);

    [Fact]
    public void OneLetterNamesAreNotMatched()
    {
        Assert.False(Run("i sil", "İç Kol").IsCommand);
    }

    [Fact]
    public void FindPiecesUsesTheParsersRule()
    {
        Assert.Equal(new[] { "Ön beden" }, CommandParser.FindPieces("ön beden", new[] { "Ön beden 2", "Ön beden" })); // an exactly equal name wins
        Assert.Equal(2, CommandParser.FindPieces("beden", new[] { "Ön beden 2", "Ön beden" }).Count);
        Assert.Empty(CommandParser.FindPieces("", B1));
        Assert.Empty(CommandParser.FindPieces("kol", null));
        Assert.Equal(new[] { "İç Kol" }, CommandParser.FindPieces("IÇ KOL", new[] { "İç Kol", "Dış Kol" }));
    }

    // Rename needs its verb, and a rename never swallows a piece command.
    [Theory]
    [InlineData("isim kartını sil", "İsim Kartı")]
    [InlineData("ad etiketi seç", "Ad Etiketi")]
    public void APieceCalledLikeARenameLabelIsStillAPieceCommand(string input, string piece)
    {
        var result = Run(input, piece);
        Assert.True(result.IsCommand, result.ToString());
        Assert.IsNotType<RenameCommand>(result.Command);
    }

    [Theory]
    [InlineData("adı Ön Beden olsun lütfen", "Ön Beden")]
    [InlineData("parçanın adı kol olsun", "kol")]
    [InlineData("lütfen adını Kol 2 yap", "Kol 2")]
    public void RenameIgnoresPoliteWordsAroundIt(string input, string name) =>
        Assert.Equal(name, Assert.IsType<RenameCommand>(Run(input, B1).Command).Name);

    [Theory]
    [InlineData("adı kol olsun mu")]
    [InlineData("adı kol")]
    public void ARenameWithoutItsVerbAtTheEndIsNotARename(string input) => Assert.False(Run(input, B1).Command is RenameCommand);

    // Two distances with one unit, and number words that could be split in two ways, are refused.
    [Theory]
    [InlineData("25 sağa 1 cm yukarı")]
    [InlineData("1 cm sağa 5 yukarı")]
    [InlineData("yüz yirmi seksen dikdörtgen")]
    [InlineData("25 yüz dikdörtgen")]
    [InlineData("bir iki sağa")]
    public void AmbiguousUnitsAndWordSplitsAreRefused(string input) => Assert.False(Run(input, B1).IsCommand, input);

    [Fact]
    public void ARectanglePairStillSharesItsUnit()
    {
        var rect = Assert.IsType<RectangleCommand>(Run("eni 100 boyu 150 cm dikdörtgen").Command);
        Assert.Equal(1000, rect.WidthMm);
        Assert.Equal(1500, rect.HeightMm);
    }

    // The unit buttons must lead to a command.
    [Theory]
    [InlineData("10lük kare")]
    [InlineData("beşlik kare")]
    [InlineData("1e çizgi")]
    [InlineData("10'luk kare")]
    [InlineData("on iki çarpı sekiz dikdörtgen")]
    [InlineData("12 x 8 dikdörtgen")]
    public void EveryUnitQuestionButtonParses(string input)
    {
        var result = Run(input);
        Assert.Equal(ParseKind.Question, result.Kind);
        foreach (var button in result.Notice!.Buttons)
            Assert.True(Run(button.FillText).IsCommand, $"{input} -> {button.FillText}");
    }

    [Fact]
    public void ForbiddenWordsAreNotEchoedByTheNameRefusalEither()
    {
        var result = Run("model seç", B1);
        Assert.Equal("R15a", result.Code);
        Assert.False(TurkishText.ContainsForbiddenWord(result.Notice!.Message));
    }

    [Theory]
    [InlineData("kolu sil yap")]
    [InlineData("kolu silin")]
    [InlineData("kolu seçiniz")]
    [InlineData("kolu seçin")]
    public void PoliteAndTrailingVerbsAfterANamedVerbAreAccepted(string input) => Assert.True(Run(input, B1).IsCommand, input);

    [Theory]
    [InlineData("adı kol \uD83D olsun")]
    [InlineData("\uD800")]
    [InlineData("\uDC00 120 x 80 dikdörtgen")]
    public void UnpairedSurrogatesNeverReachAMessageOrAName(string input)
    {
        var result = Run(input, B1);
        var text = result.IsCommand ? (result.Command as RenameCommand)?.Name ?? string.Empty : result.Notice!.Message;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i])) Assert.True(i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]));
            if (char.IsLowSurrogate(text[i])) Assert.True(i > 0 && char.IsHighSurrogate(text[i - 1]));
        }
    }

    [Fact]
    public void PairedEmojiSurviveInNames() =>
        Assert.Equal("kol 😀", Assert.IsType<RenameCommand>(Run("adı kol 😀 olsun", B1).Command).Name);

    // Natural phrasing around "the piece".
    [Theory]
    [InlineData("parçayı 20 sağa taşı")]
    [InlineData("seçili parçayı döndür")]
    [InlineData("parçayı aynala")]
    [InlineData("seçili parçanın genişliğini 95 yap")]
    public void ThePieceAsAnObjectIsAccepted(string input) => Assert.True(Run(input, B1).IsCommand, input);

    [Fact]
    public void BlankPieceNamesNeverBecomeButtons()
    {
        var result = Run("yakayı seç", " ", "", "Kol");
        Assert.Equal("R15a", result.Code);
        Assert.All(result.Notice!.Buttons, b => Assert.False(string.IsNullOrWhiteSpace(b.Label)));
    }
}
