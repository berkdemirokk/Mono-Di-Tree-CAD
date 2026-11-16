using System.Text.RegularExpressions;
using TekstilCAD.Core.Commands;

namespace TekstilCAD.Tests;

/// <summary>
/// Data integrity and search behaviour of the "Tüm araçlar" catalogue. Pure Core tests; the coverage of the classic
/// main-window buttons is in <see cref="CommandCatalogueCoverageTests"/>.
/// </summary>
public sealed class CommandCatalogueTests
{
    private static readonly string[] ExpectedGroupTitles =
    [
        "Başlangıç ve dosya", "Çizim", "Parçayı değiştir", "Ölçü, beden ve pastal",
        "Dosya alma ve verme", "Görünüm", "Katman ve liste", "Yardım",
    ];

    /// <summary>Strings the headless verification looks up by content or automation name (map-verification-constraints 2.1-2.3).</summary>
    private static readonly string[] ReservedStrings =
    [
        "Kalıp isteği hazırla…", "Hakkında", "Kaydet", "Açık çizgiyi bitir", "Parça bilgileri…", "Parçayı sil",
        "90° sola", "90° sağa", "180° döndür", "Aynala", "Yatay aynala",
        "Koordinat gir…", "A4 PDF çıkar…", "Seçiliyi göster", "Poliline dönüştür…", "Güvenli kopya…", "Parçayı çoğalt…",
        "Simülatörü yeniden dene", "Simülatör yükleniyor…", "Simülatörü aç", "DXF okumayı iptal et",
        "Dosya aktarımı", "Tekstil ve makine",
        "Görünümü yakınlaştır", "Görünümü uzaklaştır", "Görünümü sola kaydır", "Görünümü sağa kaydır",
        "Görünümü yukarı kaydır", "Görünümü aşağı kaydır",
        "Koordinatla nokta gir…", "Seçili parçayı A4 PDF çıkar…", "Seçili parçayı göster",
        "Parametrik konturu poliline dönüştür…", "Yeni dosya olarak güvenli kopya…",
        "Proje parçaları", "Seçili parça işlem sınırları", "Dışa aktarma durumu",
        "Kurtarma taraması hatası ayrıntıları", "Kalıp çalışma alanı", "Mono Di Tree CAD",
        "Yeni", "Aç…", "Farklı kaydet…", "Kurtarma tara", "Parça ara", "Tekstil araçları…", "Makine simülatörü…",
        "DXF al…", "Tümünü dışa aktar…", "Tümünü göster", "↶", "↷", "Konumu ve adı uygula",
        "Alan taslaklarını incele…", "Alan taslaklarını temizle", "İplik yönü…", "Parametrik konturu düzenle…",
        "Yanına aynalı parça…", "Birden fazla parçayı taşı…", "Yeni katman", "Yeniden adlandır…",
        "Seçili parçayı katmana ata", "Temizle", "Çizim uyarısını kapat",
    ];

    private static IEnumerable<CommandCatalogueEntry> All => CommandCatalogue.Entries;

    [Fact]
    public void Groups_AreExactlyTheEightOfTheSpecInOrder()
    {
        Assert.Equal(ExpectedGroupTitles, CommandCatalogue.Groups.Select(g => g.Title));
        Assert.Equal(CommandCatalogue.Groups.Length, CommandCatalogue.Groups.Select(g => g.Id).Distinct().Count());
    }

    [Fact]
    public void Entries_AreListedGroupByGroupInGroupOrder()
    {
        var order = CommandCatalogue.Groups.ToList();
        var indexes = All.Select(e => order.IndexOf(e.Group)).ToList();
        Assert.DoesNotContain(-1, indexes); // every entry uses one of the shared group instances
        Assert.Equal(indexes.OrderBy(i => i), indexes);
        Assert.All(CommandCatalogue.Groups, g => Assert.NotEmpty(CommandCatalogue.EntriesIn(g)));
    }

    [Fact]
    public void Ids_AreUniqueStableAsciiNames()
    {
        var ids = All.Select(e => e.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.Matches(new Regex("^[a-z]+(\\.[a-z0-9]+(-[a-z0-9]+)*)+$"), id));
        // The id prefix is the group id; the piece list and its search box ("list.") live in the layer group.
        Assert.All(All, e =>
        {
            var prefix = e.Id[..e.Id.IndexOf('.')];
            Assert.True(prefix == e.Group.Id || (prefix == "list" && e.Group.Id == "layer"), $"{e.Id} is in group {e.Group.Id}");
        });
    }

    [Fact]
    public void EveryEntry_HasTitleDescriptionAndKindRequirements()
    {
        foreach (var e in All)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Title), e.Id);
            Assert.False(string.IsNullOrWhiteSpace(e.Description), e.Id);
            Assert.NotNull(e.Group);
            Assert.True(e.Title.Length <= 60, $"{e.Id}: title longer than 60 characters ({e.Title.Length})");
            Assert.True(e.Description.Length <= 170, $"{e.Id}: description longer than 170 characters ({e.Description.Length})");
            Assert.EndsWith(".", e.Description.TrimEnd());
            Assert.DoesNotContain('\n', e.Description);
            switch (e.Kind)
            {
                case CommandCatalogueKind.Fill: Assert.NotEmpty(e.Examples); break;
                case CommandCatalogueKind.Open: Assert.False(string.IsNullOrWhiteSpace(e.ClassicButtonLabel), e.Id); break;
                case CommandCatalogueKind.Detailed: Assert.False(string.IsNullOrWhiteSpace(e.DetailedControl), e.Id); break;
            }
        }
    }

    [Fact]
    public void NoEntryMentionsAnAssistant()
    {
        // FINAL_COMMANDS section 3: these words never appear in the interface.
        foreach (var e in All)
        {
            var text = CommandCatalogue.Fold(e.Title + " " + e.Description + " " + string.Join(" ", e.Examples));
            Assert.DoesNotContain("yapay zeka", text);
            Assert.DoesNotContain("asistan", text);
        }
    }

    [Fact]
    public void Titles_AreUnique()
    {
        var titles = All.Select(e => CommandCatalogue.Fold(e.Title)).ToList();
        Assert.Empty(titles.GroupBy(t => t).Where(g => g.Count() > 1).Select(g => g.Key));
    }

    [Fact]
    public void Synonyms_AreEverydayWordsUniqueAcrossEntries()
    {
        var seen = new Dictionary<string, string>();
        var titles = All.ToDictionary(e => CommandCatalogue.Fold(e.Title), e => e.Id);
        var problems = new List<string>();
        foreach (var e in All)
        {
            foreach (var synonym in e.Synonyms)
            {
                var folded = CommandCatalogue.Fold(synonym);
                if (folded.Length < 2) problems.Add($"{e.Id}: synonym '{synonym}' is too short");
                if (seen.TryGetValue(folded, out var other) && other != e.Id)
                    problems.Add($"'{synonym}' is a synonym of both {other} and {e.Id}");
                else if (other is null) seen[folded] = e.Id;
                if (titles.TryGetValue(folded, out var titleOwner))
                    problems.Add($"{e.Id}: synonym '{synonym}' equals the title of {titleOwner}");
            }
            Assert.Equal(e.Synonyms.Length, e.Synonyms.Select(CommandCatalogue.Fold).Distinct().Count());
        }
        Assert.Empty(problems);
    }

    [Fact]
    public void EveryEntry_OffersAtLeastOneEverydayWord()
    {
        // No padding with weak words: a generic verb must not make a destructive entry win a search.
        Assert.All(All, e => Assert.True(e.Synonyms.Length >= 1, $"{e.Id} has no synonyms"));
    }

    [Fact]
    public void Delete_IsTheTypedFlowThatAsksFirst_AndStillNamesTheClassicButton()
    {
        var delete = CommandCatalogue.Find("piece.delete")!;
        Assert.Equal(CommandCatalogueKind.Fill, delete.Kind);
        Assert.Equal("Parçayı sil", delete.ClassicButtonLabel);
        Assert.Contains("sil", delete.Examples);
        // Generic verbs must not rank the delete entry first.
        Assert.NotEqual("piece.delete", CommandCatalogue.Search("çıkar").First().Id);
        Assert.NotEqual("piece.delete", CommandCatalogue.Search("at").FirstOrDefault()?.Id);
        // FINAL_COMMANDS 1.6: vazgeç never means undo.
        Assert.DoesNotContain(CommandCatalogue.Find("view.undo")!.Synonyms, s => CommandCatalogue.Fold(s) == "vazgec");
    }

    [Fact]
    public void Search_FindsTheEntryByTheOldButtonName()
    {
        Assert.Contains(CommandCatalogue.Search("parametrik"), e => e.Id == "piece.curve-edit");
        Assert.Contains(CommandCatalogue.Search("poliline"), e => e.Id == "piece.curve-flatten");
        Assert.Contains(CommandCatalogue.Search("Farklı kaydet"), e => e.Id == "start.save-as");
    }

    [Fact]
    public void Search_NeverThrowsOnOddText()
    {
        Assert.Empty(CommandCatalogue.Search("\uD83D"));              // lone high surrogate (half-pasted emoji)
        Assert.Empty(CommandCatalogue.Search(new string('\uDE00', 5)));
        Assert.Empty(CommandCatalogue.Search(new string('q', 100000)));
        Assert.Equal("i", CommandCatalogue.Fold("İ"));
        Assert.Equal("i", CommandCatalogue.Fold("I"));
        Assert.Equal("i", CommandCatalogue.Fold("ı"));
        Assert.Equal("dxf'teki", CommandCatalogue.Fold("DXF’TEKİ"));
    }

    [Fact]
    public void Examples_AreUniqueAcrossEntries()
    {
        var seen = new Dictionary<string, string>();
        var problems = new List<string>();
        foreach (var e in All)
            foreach (var example in e.Examples)
            {
                var folded = CommandCatalogue.Fold(example);
                if (seen.TryGetValue(folded, out var other)) problems.Add($"'{example}' is an example of both {other} and {e.Id}");
                else seen[folded] = e.Id;
            }
        Assert.Empty(problems);
    }

    [Fact]
    public void Examples_AreShortTypedSentences()
    {
        foreach (var example in All.SelectMany(e => e.Examples))
        {
            Assert.True(example.Length is > 0 and <= 60, example);
            Assert.Equal(example.Trim(), example);
            Assert.DoesNotContain("  ", example);
        }
    }

    [Fact]
    public void ClassicLabels_AreUsedByOneEntryOnly_AndNeverAlsoInTheHeaderList()
    {
        var labels = All.Where(e => e.ClassicButtonLabel is not null).Select(e => e.ClassicButtonLabel!).ToList();
        Assert.Empty(labels.GroupBy(l => l, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key));
        var header = CommandCatalogue.HeaderButtons.Select(h => h.ClassicButtonLabel).ToList();
        Assert.Equal(header.Count, header.Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(header.Intersect(labels, StringComparer.Ordinal));
        Assert.All(CommandCatalogue.HeaderButtons, h => { Assert.False(string.IsNullOrWhiteSpace(h.Where)); Assert.False(string.IsNullOrWhiteSpace(h.Reason)); });
    }

    [Fact]
    public void EntriesPlusHeaderList_AccountForAll68ClassicButtons()
    {
        // FINAL_SPEC g.3: header 9 + command strip 18 + rail 18 + inspector 19 + canvas 4.
        var covered = All.Count(e => e.ClassicButtonLabel is not null) + CommandCatalogue.HeaderButtons.Length;
        Assert.Equal(68, covered);
    }

    [Fact]
    public void EveryEntryKind_IsUsed_AndGlyphButtonsKeepTheirAccessibleNames()
    {
        Assert.Contains(All, e => e.Kind == CommandCatalogueKind.Fill);
        Assert.Contains(All, e => e.Kind == CommandCatalogueKind.Open);
        Assert.Contains(All, e => e.Kind == CommandCatalogueKind.Detailed);
        var glyph = new[] { "+", "−", "←", "→", "↑", "↓", "↶", "↷" };
        foreach (var g in glyph)
            Assert.False(string.IsNullOrWhiteSpace(CommandCatalogue.FindByClassicLabel(g)?.ClassicAccessibleName), g);
    }

    [Fact]
    public void Texts_NeverUseAStringTheHeadlessVerificationLooksUp()
    {
        var reserved = new HashSet<string>(ReservedStrings, StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var e in All)
        {
            if (reserved.Contains(e.Title)) problems.Add($"{e.Id}: title '{e.Title}'");
            if (reserved.Contains(e.Description)) problems.Add($"{e.Id}: description");
        }
        foreach (var g in CommandCatalogue.Groups)
            if (reserved.Contains(g.Title)) problems.Add($"group '{g.Title}'");
        Assert.Empty(problems);
    }

    [Fact]
    public void FindAndFindByClassicLabel_ReturnTheEntry()
    {
        Assert.Same(CommandCatalogue.Find("piece.mirror"), CommandCatalogue.FindByClassicLabel("Aynala"));
        Assert.Null(CommandCatalogue.Find("nope"));
        Assert.Null(CommandCatalogue.FindByClassicLabel("Yok böyle düğme"));
        Assert.Equal("Yeni dosya olarak güvenli kopya…", CommandCatalogue.Find("start.save-copy")!.ClassicButtonLabel);
    }

    [Fact]
    public void Entries_AreImmutable()
    {
        var entry = CommandCatalogue.Find("piece.move")!;
        Assert.IsType<System.Collections.Immutable.ImmutableArray<string>>(entry.Examples);
        Assert.IsType<System.Collections.Immutable.ImmutableArray<string>>(entry.Synonyms);
        Assert.Throws<ArgumentException>(() => new CommandCatalogueEntry("x.y", CommandCatalogue.Groups[0], " ", "d.", CommandCatalogueKind.Detailed, detailedControl: "c"));
        Assert.Throws<ArgumentException>(() => new CommandCatalogueEntry("x.y", CommandCatalogue.Groups[0], "t", "d.", CommandCatalogueKind.Fill));
        Assert.Throws<ArgumentException>(() => new CommandCatalogueEntry("x.y", CommandCatalogue.Groups[0], "t", "d.", CommandCatalogueKind.Open));
        Assert.Throws<ArgumentException>(() => new CommandCatalogueEntry("x.y", CommandCatalogue.Groups[0], "t", "d.", CommandCatalogueKind.Detailed));
    }

    // ---- Search ----

    [Theory]
    [InlineData("İPLİK", "iplik")]
    [InlineData("IPLIK", "iplik")]
    [InlineData("ıplık", "iplik")]
    [InlineData("çoğalt", "cogalt")]
    [InlineData("ÇOĞALT", "cogalt")]
    [InlineData("dikdörtgen", "dikdortgen")]
    [InlineData("DİKDÖRTGEN", "dikdortgen")]
    [InlineData("kaydır", "kaydir")]
    [InlineData("  ölçü   ", "olcu")]
    [InlineData("Ayrıntılı Görünüm", "ayrintili gorunum")]
    [InlineData("şeffaf", "seffaf")]
    public void Search_FoldsTurkishLettersCaseAndSpaces(string typed, string plain)
    {
        Assert.Equal(CommandCatalogue.Search(plain).Select(e => e.Id), CommandCatalogue.Search(typed).Select(e => e.Id));
    }

    [Theory]
    [InlineData("kopyala", "piece.duplicate")]
    [InlineData("çoğalt", "piece.duplicate")]
    [InlineData("cogalt", "piece.duplicate")]
    [InlineData("ayna", "piece.mirror")]
    [InlineData("ölçü", "piece.resize")]
    [InlineData("olcu", "piece.resize")]
    [InlineData("pastal", "size.nest")]
    [InlineData("dikiş payı", "size.seam")]
    [InlineData("dikis payi", "size.seam")]
    [InlineData("iplik", "piece.grainline")]
    [InlineData("geri al", "view.undo")]
    [InlineData("kaydet", "start.save")]
    public void Search_FindsTheEntryFirstByEverydayWord(string typed, string expectedId)
    {
        Assert.Equal(expectedId, CommandCatalogue.Search(typed).First().Id);
    }

    [Fact]
    public void Search_EveryTitleFindsItsOwnEntryFirst()
    {
        foreach (var e in All)
            Assert.Equal(e.Id, CommandCatalogue.Search(e.Title).FirstOrDefault()?.Id);
    }

    [Fact]
    public void Search_EverySynonymFindsItsOwnEntryFirst()
    {
        foreach (var e in All)
            foreach (var synonym in e.Synonyms)
                Assert.True(CommandCatalogue.Search(synonym).FirstOrDefault()?.Id == e.Id, $"'{synonym}' should find {e.Id} first");
    }

    [Fact]
    public void Search_EveryExampleFindsItsOwnEntry()
    {
        foreach (var e in All)
            foreach (var example in e.Examples)
                Assert.Contains(CommandCatalogue.Search(example), r => r.Id == e.Id);
    }

    [Fact]
    public void Search_RanksExactSynonymAboveTitleWordAboveDescription()
    {
        var ids = CommandCatalogue.Search("ayna").Select(e => e.Id).ToList();
        Assert.Equal("piece.mirror", ids[0]);
        Assert.True(ids.IndexOf("piece.mirror-h") > 0);
        Assert.True(ids.IndexOf("piece.mirror-copy") > 0);
        // A match in the title of an earlier-listed entry beats the same word only in a later description.
        var sil = CommandCatalogue.Search("sil").Select(e => e.Id).ToList();
        Assert.Equal("piece.delete", sil[0]);
        Assert.True(sil.IndexOf("layer.delete") > 0);
    }

    [Fact]
    public void Search_AllWordsMustMatch_AndUnrelatedTextFindsNothing()
    {
        var a4 = CommandCatalogue.Search("seçili parça pdf").Select(e => e.Id).ToList();
        Assert.Contains("exchange.a4", a4);
        Assert.Empty(CommandCatalogue.Search("zzzzqqq"));
        Assert.Empty(CommandCatalogue.Search("dikdörtgen zzzzqqq"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("?")]
    [InlineData("...")]
    [InlineData(null)]
    public void Search_BlankQueryReturnsNothing(string? query) => Assert.Empty(CommandCatalogue.Search(query));

    [Fact]
    public void Search_IsDeterministicAndHasNoDuplicates()
    {
        foreach (var query in new[] { "a", "parça", "dxf", "kaydır", "sil", "yeni", "aç", "pdf", "döndür" })
        {
            var first = CommandCatalogue.Search(query).Select(e => e.Id).ToList();
            var second = CommandCatalogue.Search(query).Select(e => e.Id).ToList();
            Assert.Equal(first, second);
            Assert.Equal(first.Count, first.Distinct().Count());
        }
        // Equal scores keep page order: both rotation buttons contain "döndür" in their title.
        var rotate = CommandCatalogue.Search("döndür").Select(e => e.Id).ToList();
        Assert.True(rotate.IndexOf("piece.rot-left") < rotate.IndexOf("piece.rot-right"));
        Assert.True(rotate.IndexOf("piece.rot-right") < rotate.IndexOf("piece.rot-half"));
    }

    [Fact]
    public void Search_ByDescriptionWordFindsTheEntry()
    {
        // "milimetre" only appears in a description.
        Assert.Contains(CommandCatalogue.Search("milimetre"), e => e.Id == "draw.coordinate");
    }

    [Fact(Skip = "Waits for the command parser (branch feat/command-parser has no commits yet). Replace with a test that runs every CommandCatalogueEntry.Examples string through CommandParser against the B1 context of FINAL_COMMANDS.md section 5 and asserts it is understood (Anlaşıldı or Devir), never rejected.")]
    public void EveryExample_IsAcceptedByTheCommandParser()
    {
    }
}
