using TekstilCAD.Core;
using static TekstilCAD.Tests.PieceOperationTestData;

namespace TekstilCAD.Tests;

public sealed class PieceNameSearchTests
{
    private static ProjectDocument Named(params string[] names)
    {
        var document = new ProjectDocument();
        for (var i = 0; i < names.Length; i++) document.Pieces.Add(Rect(names[i], i * 20, 0, 10, 10));
        return document;
    }

    private static readonly string[] Names =
        ["Ön beden", "Arka beden", "ÖN yaka", "Kol", "ISIK", "Işık", "Sağa", "İPEK", "Parça 1", "Parça 10", "Parça 2"];

    private static int[] Indexes(IReadOnlyList<PieceMatch> matches) => matches.Select(m => m.Index).ToArray();

    [Theory]
    [InlineData("ön", new[] { 0, 2 })]
    [InlineData("ÖN", new[] { 0, 2 })]
    [InlineData("beden", new[] { 0, 1 })]
    [InlineData("BEDEN", new[] { 0, 1 })]
    [InlineData("kol", new[] { 3 })]
    [InlineData("  kol ", new[] { 3 })]
    [InlineData("Parça 1", new[] { 8, 9 })]
    [InlineData("parça 2", new[] { 10 })]
    [InlineData("ağa", new[] { 6 })]
    public void MainWindowRuleIsACaseInsensitiveSubstringOfTheName(string query, int[] expected)
    {
        Assert.Equal(expected, Indexes(PieceNameSearch.FindPiecesByName(Named(Names), query)));
    }

    [Theory]
    [InlineData("on")]
    [InlineData("saga")]
    [InlineData("zzz")]
    public void MainWindowRuleDoesNotFoldAccents(string query)
    {
        Assert.Empty(PieceNameSearch.FindPiecesByName(Named(Names), query));
    }

    [Fact]
    public void TurkishCasingPairsDotlessAndDottedLettersSeparately()
    {
        var document = Named(Names);
        // tr-TR: "ı" pairs with "I", "i" with "İ"; "i" does not match "I".
        Assert.Equal([4], Indexes(PieceNameSearch.FindPiecesByName(document, "ısık")));
        Assert.Empty(PieceNameSearch.FindPiecesByName(document, "isik"));
        Assert.Equal([7], Indexes(PieceNameSearch.FindPiecesByName(document, "ipek")));
        Assert.Equal([5], Indexes(PieceNameSearch.FindPiecesByName(document, "ışık")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankQueriesMatchNothingSoACommandNeverSelectsEverything(string? query)
    {
        Assert.Empty(PieceNameSearch.FindPiecesByName(Named(Names), query));
        Assert.Empty(PieceNameSearch.FindPiecesByNameFolded(Named(Names), query));
    }

    [Fact]
    public void PieceIdsAreNotSearched()
    {
        var piece = Rect("Kol", 0, 0, 5, 5);
        var document = Doc(piece);
        Assert.Empty(PieceNameSearch.FindPiecesByName(document, piece.Id.ToString("D")));
        Assert.Empty(PieceNameSearch.FindPiecesByNameFolded(document, piece.Id.ToString("D")));
    }

    [Fact]
    public void EveryMatchIsReturnedInDocumentOrderWithItsIdAndIndex()
    {
        var document = Doc(Rect("Kol", 0, 0, 1, 1), Rect("Ön", 5, 0, 1, 1), Rect("Kol", 10, 0, 1, 1), Rect("Yaka", 15, 0, 1, 1));
        var matches = PieceNameSearch.FindPiecesByName(document, "kol");
        Assert.Equal(2, matches.Count);
        Assert.Equal([document.Pieces[0].Id, document.Pieces[2].Id], matches.Select(m => m.Id));
        Assert.Equal([0, 2], Indexes(matches));
        Assert.All(matches, m => Assert.Equal("Kol", m.Name));
        Assert.NotEqual(matches[0].Id, matches[1].Id);
    }

    [Fact]
    public void NoMatchIsAnEmptyListNotAnError()
    {
        Assert.Empty(PieceNameSearch.FindPiecesByName(OneRect(), "yaka"));
        Assert.Empty(PieceNameSearch.FindPiecesByName(new ProjectDocument(), "yaka"));
    }

    [Fact]
    public void FoldedSearchFindsPiecesTypedWithoutTurkishLetters()
    {
        var document = Named(Names);
        Assert.Equal([6], Indexes(PieceNameSearch.FindPiecesByNameFolded(document, "saga")));
        Assert.Equal([0], Indexes(PieceNameSearch.FindPiecesByNameFolded(document, "on beden")));
        Assert.Equal([0, 2], Indexes(PieceNameSearch.FindPiecesByNameFolded(document, "on")));
        Assert.Equal([7], Indexes(PieceNameSearch.FindPiecesByNameFolded(document, "ipek")));
        Assert.Equal([7], Indexes(PieceNameSearch.FindPiecesByNameFolded(document, "IPEK")));
        Assert.Equal([7], Indexes(PieceNameSearch.FindPiecesByNameFolded(document, "İpek")));
        Assert.Equal([4, 5], Indexes(PieceNameSearch.FindPiecesByNameFolded(document, "isik")));
        Assert.Equal([4, 5], Indexes(PieceNameSearch.FindPiecesByNameFolded(document, "ışık")));
        Assert.Equal([8, 9], Indexes(PieceNameSearch.FindPiecesByNameFolded(document, "parca 1")));
    }

    [Fact]
    public void FoldedSearchStillFindsWhatTheStrictSearchFinds()
    {
        var document = Named(Names);
        foreach (var query in new[] { "ön", "beden", "kol", "ağa", "parça 1" })
        {
            var strict = PieceNameSearch.FindPiecesByName(document, query).Select(m => m.Id).ToHashSet();
            var folded = PieceNameSearch.FindPiecesByNameFolded(document, query).Select(m => m.Id).ToHashSet();
            Assert.True(strict.IsSubsetOf(folded), query);
        }
    }

    [Theory]
    [InlineData("İ", "i")]
    [InlineData("I", "i")]
    [InlineData("ı", "i")]
    [InlineData("i", "i")]
    [InlineData("Ö", "o")]
    [InlineData("ö", "o")]
    [InlineData("Ç", "c")]
    [InlineData("ç", "c")]
    [InlineData("Ğ", "g")]
    [InlineData("ğ", "g")]
    [InlineData("Ş", "s")]
    [InlineData("ş", "s")]
    [InlineData("Ü", "u")]
    [InlineData("ü", "u")]
    [InlineData("ÖĞRENCİ", "ogrenci")]
    [InlineData("ışık ILIK İLİK", "isik ilik ilik")]
    [InlineData("Parça 1-A", "parca 1-a")]
    [InlineData("", "")]
    public void FoldMapsTurkishLettersToPlainLowerCase(string text, string folded)
    {
        Assert.Equal(folded, PieceNameSearch.Fold(text));
    }

    [Theory]
    [InlineData("ş", "s")]
    [InlineData("ğ", "g")]
    [InlineData("ç", "c")]
    [InlineData("ö", "o")]
    [InlineData("ü", "u")]
    [InlineData("İ", "i")]
    public void FoldTreatsDecomposedLettersLikeTheComposedOnes(string text, string folded)
    {
        Assert.Equal(folded, PieceNameSearch.Fold(text));
    }

    [Fact]
    public void FoldedSearchMatchesComposedAndDecomposedNamesAlike()
    {
        var document = Doc(Rect("Sağa", 0, 0, 1, 1), Rect("Sağa", 5, 0, 1, 1));
        Assert.Equal([0, 1], Indexes(PieceNameSearch.FindPiecesByNameFolded(document, "saga")));
    }

    [Fact]
    public void FoldIsIdempotent()
    {
        foreach (var name in Names) Assert.Equal(PieceNameSearch.Fold(name), PieceNameSearch.Fold(PieceNameSearch.Fold(name)));
    }

    [Fact]
    public void OperationsClassForwardsBothSearches()
    {
        var document = Named(Names);
        Assert.Equal(Indexes(PieceNameSearch.FindPiecesByName(document, "ön")), Indexes(PieceOperations.FindPiecesByName(document, "ön")));
        Assert.Equal(Indexes(PieceNameSearch.FindPiecesByNameFolded(document, "on")), Indexes(PieceOperations.FindPiecesByNameFolded(document, "on")));
    }

    [Fact]
    public void SearchNeverChangesTheDocument()
    {
        var document = Named(Names);
        var snapshot = document.Clone();
        _ = PieceNameSearch.FindPiecesByName(document, "ön");
        _ = PieceNameSearch.FindPiecesByNameFolded(document, "on");
        Assert.True(DocumentSession.ContentEquals(snapshot, document));
    }

    [Theory]
    [InlineData("​")]
    [InlineData("­")]
    [InlineData(" ​ ")]
    public void QueriesOfOnlyInvisibleCharactersMatchNothing(string query)
    {
        Assert.Empty(PieceNameSearch.FindPiecesByName(Named(Names), query));
    }

    [Fact]
    public void FoldAndFoldedSearchDoNotThrowOnAnUnpairedSurrogate()
    {
        var lone = "A" + (char)0xD800;
        Assert.Equal("a" + (char)0xD800, PieceNameSearch.Fold(lone));
        Assert.Empty(PieceNameSearch.FindPiecesByNameFolded(Named(Names), lone));
    }
}
