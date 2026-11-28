using System.Text.Json;
using TekstilCAD.Core.Commands;

namespace TekstilCAD.Tests;

/// <summary>What the command box needs while someone types: assumed units, incomplete input, parseable examples.</summary>
public sealed class CommandParserTypingTests
{
    private static readonly string[] B1 = { "Ön beden", "Arka beden", "Kol" };

    private static ParseResult Run(string input, params string[] pieces) =>
        CommandParser.Parse(input, new ParserContext(pieces));

    [Fact]
    public void MeasuredMarkerRequestCarriesMillimetresAndExactPieceCounts()
    {
        var result=Run("150 cm kumaşa iki Ön, bir Arka yerleştir", "Ön", "Arka");
        var request=Assert.IsType<HandoffCommand>(result.Command);
        Assert.Equal(HandoffTarget.TextileTools,request.Target);
        Assert.Equal(1500,request.Value);
        Assert.Equal(1500,request.NestingRequest!.FabricWidthMm);
        Assert.Equal(new[]{new NestingPieceCount("Ön",2),new NestingPieceCount("Arka",1)},request.NestingRequest.Pieces);
    }

    [Fact]
    public void MeasuredMarkerRequestRefusesUnknownPieceName()
    {
        var result=Run("150 cm kumaşa iki Ön, bir Arka yerleştir", "Ön", "Kol");
        Assert.False(result.IsCommand);
    }

    [Fact]
    public void MeasuredMarkerRequestAcceptsCompactWidthAndExplicitCountNouns()
    {
        var result=Run("150cm kumaşa iki adet Ön, bir tane Arka diz", "Ön", "Arka");
        var request=Assert.IsType<HandoffCommand>(result.Command).NestingRequest!;
        Assert.Equal(1500,request.FabricWidthMm);
        Assert.Equal(new[]{new NestingPieceCount("Ön",2),new NestingPieceCount("Arka",1)},request.Pieces);
    }

    [Fact]
    public void MeasuredMarkerCountOnIsNotThePieceNameOn()
    {
        var result=Run("150 cm kumaşa on adet Ön yerleştir", "Ön");
        var request=Assert.IsType<HandoffCommand>(result.Command).NestingRequest!;
        Assert.Equal(10,Assert.Single(request.Pieces).Quantity);
    }

    [Fact]
    public void MeasuredMarkerRejectsDecimalOverflowWidthWithoutThrowing()
    {
        var result=Run("79228162514264337593543950335 m kumaşa bir Ön yerleştir", "Ön");
        Assert.False(result.IsCommand);
    }

    // ---- Assumed unit -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("60 x 45 dikdörtgen")]
    [InlineData("150 çizgi")]
    [InlineData("kare 100")]
    [InlineData("25 sağa taşı")]
    [InlineData("20 sağa 10 yukarı")]
    [InlineData("genişliği 95 yap")]
    [InlineData("x 100 y 50 konumuna taşı")]
    [InlineData("dikiş payı 10")]
    [InlineData("kumaş eni 150 pastal")]
    [InlineData("yüz yirmi çarpı seksen dikdörtgen")]
    public void ALengthWithoutAnyUnitCarriesTheAssumedUnitFlag(string input)
    {
        var result = Run(input, B1);
        Assert.True(result.IsCommand, result.ToString());
        Assert.True(result.UnitAssumed);
        Assert.Equal(LengthUnit.Millimeter, result.AssumedUnit);
    }

    [Theory]
    [InlineData("60 x 45 cm dikdörtgen")]
    [InlineData("10'a 15 santim dikdörtgen")]
    [InlineData("15 cm çizgi")]
    [InlineData("1 cm sola kaydır")]
    [InlineData("1 santim 2 milim sağa")]
    [InlineData("20 mm sağa 10 mm yukarı")]
    [InlineData("döndür")]
    [InlineData("90 sağa döndür")]
    [InlineData("geri al")]
    [InlineData("3 adım geri")]
    [InlineData("adet 2 yap")]
    [InlineData("kaydet")]
    [InlineData("aynala")]
    public void NoFlagWhenAUnitWasWrittenOrThereIsNoLength(string input)
    {
        var result = Run(input, B1);
        Assert.True(result.IsCommand, result.ToString());
        Assert.False(result.UnitAssumed);
        Assert.Null(result.AssumedUnit);
    }

    [Fact]
    public void RefusalsAndQuestionsNeverCarryTheFlag()
    {
        Assert.False(Run("12 x 8 dikdörtgen").UnitAssumed);
        Assert.False(Run("zzz").UnitAssumed);
    }

    [Fact]
    public void MovesAreNotAskedAboutTheirUnitBecauseThePreviewShowsBothUnits()
    {
        // FINAL_COMMANDS.md section 1.4 and phrases 14-20: moving an existing piece never asks; the flag lets the preview warn instead.
        var small = Run("5 sağa taşı", B1);
        Assert.True(small.IsCommand);
        Assert.True(small.UnitAssumed);
    }

    // ---- Natural resize phrasing ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("genişliğini 95 yap", 95.0, null)]
    [InlineData("boyunu 60 yap", null, 60.0)]
    [InlineData("yüksekliğini 60 yap", null, 60.0)]
    [InlineData("enini 95 yap", 95.0, null)]
    [InlineData("eni 95 olsun", 95.0, null)]
    [InlineData("boyu 60 olsun", null, 60.0)]
    [InlineData("genişlik 95", 95.0, null)]
    [InlineData("genişliği 95'e getir", 95.0, null)]
    [InlineData("boyunu 60'a getir", null, 60.0)]
    [InlineData("genişliği 95 mm yap", 95.0, null)]
    [InlineData("genişliğini 9,5 santim yap", 95.0, null)]
    [InlineData("95'e 60'a getir", 95.0, 60.0)]
    [InlineData("95 x 60 yap", 95.0, 60.0)]
    [InlineData("eni 95 boyunu 60 yap", 95.0, 60.0)]
    public void NaturalResizePhrasing(string input, double? width, double? height)
    {
        var result = Run(input, B1);
        Assert.True(result.IsCommand, result.ToString());
        var resize = Assert.IsType<ResizeCommand>(result.Command);
        Assert.Equal(width, resize.WidthMm);
        Assert.Equal(height, resize.HeightMm);
    }

    [Theory]
    [InlineData("95 mm yap")]
    [InlineData("95'e getir")]
    public void ANumberWithoutSaysWhichSideIsRefusedNotGuessed(string input) =>
        Assert.False(Run(input, B1).IsCommand);

    [Theory]
    [InlineData("adını ön beden yap", "ön beden")]
    [InlineData("ismini Kol 2 olsun", "Kol 2")]
    public void PossessiveEndingsOfTheNameLabelsAreAccepted(string input, string name) =>
        Assert.Equal(name, Assert.IsType<RenameCommand>(Run(input, B1).Command).Name);

    [Theory]
    [InlineData("dikdörtgeni 120 x 80 çiz")]
    [InlineData("120 x 80 kutuyu çiz")]
    public void AccusativeEndingsOfTheShapeWordsAreAccepted(string input) =>
        Assert.IsType<RectangleCommand>(Run(input).Command);

    [Theory]
    [InlineData("tüm araçlar")]
    [InlineData("TÜM ARAÇLAR")]
    [InlineData("araçlar")]
    public void AllToolsOpensTheToolsPage(string input) =>
        Assert.Equal(ViewAction.AllTools, Assert.IsType<ViewCommand>(Run(input).Command).Action);

    // ---- Two whole commands in one line ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("kaydet ve sil")]
    [InlineData("geri al yinele")]
    [InlineData("tümünü göster kaydet")]
    [InlineData("yakınlaştır uzaklaştır")]
    [InlineData("kaydet ve geri al ve yinele")]
    public void TwoWholePhrasesAreTwoJobs(string input) => Assert.Equal("R16", Run(input, B1).Code);

    // ---- Incomplete input ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("120 x")]
    [InlineData("120 x 80")]
    [InlineData("120")]
    [InlineData("dikdört")]
    [InlineData("dikdörtgen")]
    [InlineData("dikdörtgen 120")]
    [InlineData("100'")]
    [InlineData("100'e 150")]
    [InlineData("yüz yirmi çarpı")]
    [InlineData("sağa")]
    [InlineData("20 sağ")]
    [InlineData("kare")]
    [InlineData("adı")]
    [InlineData("eni")]
    [InlineData("12,")]
    [InlineData("x 100 y")]
    public void APlausiblePrefixIsFlaggedIncompleteButKeepsItsRefusal(string input)
    {
        var result = Run(input, B1);
        if (result.IsCommand || result.Kind == ParseKind.Question) return; // already complete enough
        Assert.True(result.IsIncomplete, input);
        Assert.NotNull(result.Notice);
        Assert.False(string.IsNullOrWhiteSpace(result.Notice!.Message));
    }

    [Theory]
    [InlineData("120 x")]
    [InlineData("dikdört")]
    [InlineData("dikdörtgen")]
    [InlineData("kare")]
    public void IncompleteInputKeepsTheCodeThatApplyIfEnterIsPressed(string input)
    {
        var result = Run(input);
        Assert.Equal(ParseKind.Refusal, result.Kind);
        Assert.True(result.IsIncomplete);
        Assert.NotNull(result.Code);
    }

    [Theory]
    [InlineData("zzz")]
    [InlineData("xyz 120")]
    [InlineData("yapay zeka ile çiz")]
    [InlineData("45 derece döndür")]
    [InlineData("30000 x 80 dikdörtgen")]
    [InlineData("1.500 çizgi")]
    [InlineData("sil dikdörtgen 120 80")]
    [InlineData("-20 sağa")]
    public void NonsenseAndFinalRefusalsAreNeverIncomplete(string input)
    {
        var result = Run(input, B1);
        Assert.False(result.IsCommand);
        Assert.False(result.IsIncomplete, input);
    }

    [Fact]
    public void CommandsAndQuestionsAreNeverIncomplete()
    {
        Assert.False(Run("120 x 80 dikdörtgen").IsIncomplete);
        Assert.False(Run("12 x 8 dikdörtgen").IsIncomplete);
    }

    private static IEnumerable<(string Input, string Context, JsonElement Expect)> SixtyPhrases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "command-vectors.json");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var list = new List<(string, string, JsonElement)>();
        var index = 0;
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (index++ >= 60) break;
            list.Add((c.GetProperty("input").GetString()!, c.GetProperty("context").GetString()!, c.GetProperty("expect").Clone()));
        }
        return list;
    }

    [Fact]
    public void EveryPrefixOfEveryPhraseThatIsACommandIsACommandAQuestionOrIncomplete()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "command-vectors.json");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var names = doc.RootElement.GetProperty("contexts");
        var failures = new List<string>();
        var checkedPrefixes = 0;
        foreach (var (input, contextName, expect) in SixtyPhrases())
        {
            if (!expect.TryGetProperty("kind", out _)) continue; // refusal phrases (zzz, ...) have no valid completion
            var pieces = names.GetProperty(contextName).EnumerateArray().Select(e => e.GetString()!).ToArray();
            for (var length = 1; length < input.Length; length++)
            {
                var prefix = input.Substring(0, length);
                if (prefix.Trim().Length == 0) continue;
                checkedPrefixes++;
                var result = CommandParser.Parse(prefix, new ParserContext(pieces));
                if (result.IsCommand || result.Kind == ParseKind.Question || result.IsIncomplete) continue;
                failures.Add($"\"{prefix}\" (of \"{input}\") -> {result.Code}");
            }
        }
        Assert.True(checkedPrefixes > 600, $"only {checkedPrefixes} prefixes checked");
        Assert.True(failures.Count == 0, "prefixes that would nag while typing:\n" + string.Join("\n", failures.Take(40)));
    }

    // ---- Every example offered by a refusal is itself understood ----------------------------------------------------------

    [Fact]
    public void EveryExampleOfEveryRefusalIsAParseableCommandOrQuestion()
    {
        var results = new List<ParseResult>();
        // Refusals the parser produces, in every variant.
        var inputs = new (string Input, string[] Pieces)[]
        {
            ("zzz", B1), ("kare", B1), ("çizgi", B1), ("taşı", B1), ("genişliği yap", B1), ("0 x 80 dikdörtgen", B1),
            ("30000 x 80 dikdörtgen", B1), ("1.500 çizgi", B1), ("12 x 8 dikdörtgen", B1), ("45 derece döndür", B1),
            ("yakayı seç", B1), ("ön bedeni seç", new[] { "Ön beden", "Ön beden 2" }), ("kolu sil", Array.Empty<string>()),
            ("sil dikdörtgen 120 80", B1), ("ters çevir", B1), ("büyüt", B1), ("dikdörtgen", B1), ("adı olsun", B1),
            ("-20 sağa", B1), ("ön bedeni taşı", B1), ("dikdortgn 120 x 80", B1), ("yapay zeka ile çiz", B1),
            (new string('a', 201), B1),
        };
        foreach (var (input, pieces) in inputs) results.Add(Run(input, pieces));

        // Examples the planner and executor will attach (all texts of the catalogue).
        var planner = new[]
        {
            Refusals.NoPieceSelected(B1), Refusals.NoPiecesYet(), Refusals.DrawingPending(), Refusals.Locked(), Refusals.NotARectangle(),
            Refusals.HasExtraData(), Refusals.HasCurves(), Refusals.SeveralSelected(), Refusals.TooManyPointsRefusal(),
            Refusals.NothingToUndoRefusal(), Refusals.AreaDraftsPending(), Refusals.RefreshFailedAfterApply(),
            Refusals.NegativeNumberRefusal(), Refusals.NamedPieceCannotMove(), Refusals.EmptyNameRefusal(), Refusals.TextTooLong(),
        };

        var notices = results.Where(r => r.Notice is not null).Select(r => r.Notice!).Concat(planner).ToList();
        var bad = new List<string>();
        foreach (var notice in notices)
        {
            foreach (var example in Examples(notice))
            {
                var parsed = Run(example, B1);
                if (parsed.Kind == ParseKind.Refusal) parsed = Run(example, B1.Append("Ön beden 2").ToArray());
                if (parsed.Kind == ParseKind.Refusal) bad.Add($"{notice.Code}: \"{example}\" -> {parsed.Code}");
            }
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    private static IEnumerable<string> Examples(Refusal notice)
    {
        foreach (var b in notice.Buttons) yield return b.FillText;
        foreach (var marker in new[] { "Örnek: ", "yazabilirsiniz: ", "yazın: ", "Sonra: " })
        {
            var at = notice.Message.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0) continue;
            var tail = notice.Message.Substring(at + marker.Length);
            foreach (var part in tail.Split(" · ")) yield return part.Trim().TrimEnd('.');
        }
    }
}
