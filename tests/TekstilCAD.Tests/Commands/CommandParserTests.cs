using System.Globalization;
using TekstilCAD.Core;
using TekstilCAD.Core.Commands;

namespace TekstilCAD.Tests;

public sealed class CommandParserTests
{
    [Theory]
    [InlineData("yamuk alt 65,5 cm üst 400 mm yükseklik 50 cm", true)]
    [InlineData("yamuk alt 60 cm üst 40 cm", false)]
    [InlineData("yamuk alt 60 cm üst -10 mm yükseklik 50 cm", false)]
    [InlineData("yamuk alt 60 üst 40 yükseklik 50", false)]
    public void TrapezoidRequiresThreePositiveDimensionsWithUnits(string text, bool valid)
    {
        var parsed = CommandParser.Parse(text);
        if (valid) Assert.Equal(new TrapezoidCommand(655, 400, 500), parsed.Command);
        else Assert.Null(parsed.Command);
    }

    private static readonly string[] B1 = { "Ön beden", "Arka beden", "Kol" };
    private static readonly string[] B8 = { "Ön beden", "Ön beden 2" };

    private static ParseResult Run(string input, params string[] pieces) =>
        CommandParser.Parse(input, new ParserContext(pieces));

    private static T Cmd<T>(string input, params string[] pieces) where T : Command
    {
        var result = Run(input, pieces);
        Assert.True(result.IsCommand, $"\"{input}\" -> {result}");
        return Assert.IsType<T>(result.Command);
    }

    private static void Refuses(string input, string code, params string[] pieces)
    {
        var result = Run(input, pieces);
        Assert.False(result.IsCommand, $"\"{input}\" -> {result}");
        Assert.Equal(code, result.Code);
    }

    // ---- The 60 phrases of FINAL_COMMANDS.md section 5 (parser level) ---------------------------------------------

    [Theory]
    [InlineData("120 x 80 dikdörtgen", 120, 80)]
    [InlineData("120x80 dikdörtgen", 120, 80)]
    [InlineData("dikdörtgen 120 80", 120, 80)]
    [InlineData("100'e 150 dikdörtgen", 100, 150)]
    [InlineData("12 x 8 cm dikdörtgen", 120, 80)]
    [InlineData("yüz yirmi çarpı seksen dikdörtgen", 120, 80)]
    [InlineData("DİKDÖRTGEN 120 X 80", 120, 80)]
    [InlineData("dikdortgen 120x80", 120, 80)]
    public void Phrases1To8DrawARectangle(string input, double width, double height)
    {
        var rect = Cmd<RectangleCommand>(input);
        Assert.Equal(width, rect.WidthMm);
        Assert.Equal(height, rect.HeightMm);
        Assert.False(rect.IsSquare);
    }

    [Theory]
    [InlineData("kare 100")]
    [InlineData("100'lük kare")]
    public void Phrases9And10DrawASquare(string input)
    {
        var rect = Cmd<RectangleCommand>(input);
        Assert.Equal(100, rect.WidthMm);
        Assert.Equal(100, rect.HeightMm);
        Assert.True(rect.IsSquare);
    }

    [Theory]
    [InlineData("150 çizgi", 150, LineOrientation.Horizontal)]
    [InlineData("15 cm yatay çizgi", 150, LineOrientation.Horizontal)]
    [InlineData("80 dikey çizgi", 80, LineOrientation.Vertical)]
    public void Phrases11To13DrawALine(string input, double length, LineOrientation orientation)
    {
        var line = Cmd<LineCommand>(input);
        Assert.Equal(length, line.LengthMm);
        Assert.Equal(orientation, line.Orientation);
    }

    [Theory]
    [InlineData("25 sağa taşı", 25, 0)]
    [InlineData("25 sağa", 25, 0)]
    [InlineData("1 cm sola kaydır", -10, 0)]
    [InlineData("yarım santim yukarı", 0, -5)]
    [InlineData("bir buçuk santim aşağı", 0, 15)]
    [InlineData("1 santim 2 milim sağa", 12, 0)]
    [InlineData("20 sağa 10 yukarı", 20, -10)]
    public void Phrases14To20MoveTheSelectedPiece(string input, double right, double down)
    {
        var move = Cmd<MoveByCommand>(input, B1);
        Assert.Equal(right, move.RightMm);
        Assert.Equal(down, move.DownMm);
    }

    [Fact]
    public void Phrase21MovesToAPoint()
    {
        var move = Cmd<MoveToCommand>("x 100 y 50 konumuna taşı", B1);
        Assert.Equal(100, move.XMm);
        Assert.Equal(50, move.YMm);
    }

    [Theory]
    [InlineData("döndür", 90, RotationDirection.Clockwise)]
    [InlineData("90 sağa döndür", 90, RotationDirection.Clockwise)]
    [InlineData("sola döndür", 90, RotationDirection.CounterClockwise)]
    [InlineData("yarım tur", 180, RotationDirection.Clockwise)]
    public void Phrases22To25Rotate(string input, int degrees, RotationDirection direction)
    {
        var rotate = Cmd<RotateCommand>(input, B1);
        Assert.Equal(degrees, rotate.Degrees);
        Assert.Equal(direction, rotate.Direction);
    }

    [Theory]
    [InlineData("aynala", MirrorAxis.Vertical)]
    [InlineData("alt üst aynala", MirrorAxis.Horizontal)]
    public void Phrases26And27Mirror(string input, MirrorAxis axis) =>
        Assert.Equal(axis, Cmd<MirrorCommand>(input, B1).Axis);

    [Theory]
    [InlineData("genişliği 95 yap", 95.0, null)]
    [InlineData("eni 9,5 cm yap", 95.0, null)]
    [InlineData("95 x 60 yap", 95.0, 60.0)]
    public void Phrases28To30Resize(string input, double width, double? height)
    {
        var resize = Cmd<ResizeCommand>(input, B1);
        Assert.Equal(width, resize.WidthMm);
        Assert.Equal(height, resize.HeightMm);
    }

    [Fact]
    public void Phrase31RenamesKeepingTheTypedLetters() =>
        Assert.Equal("Ön Beden", Cmd<RenameCommand>("adı Ön Beden olsun", B1).Name);

    [Fact]
    public void Phrase32DeletesTheSelectedPiece()
    {
        var delete = Cmd<DeleteCommand>("sil", B1);
        Assert.Equal(PieceTarget.Selected, delete.Target);
        Assert.Null(delete.Name);
    }

    [Fact]
    public void Phrase33DeletesANamedPiece()
    {
        var delete = Cmd<DeleteCommand>("kolu sil", B1);
        Assert.Equal(PieceTarget.Named, delete.Target);
        Assert.Equal("kol", delete.Name);
    }

    [Fact]
    public void Phrase34SelectsANamedPiece()
    {
        var select = Cmd<SelectCommand>("ön bedeni seç", B1);
        Assert.Equal(PieceTarget.Named, select.Target);
        Assert.Equal("ön beden", select.Name);
    }

    [Fact]
    public void Phrase35ClearsTheSelection() =>
        Assert.Equal(PieceTarget.None, Cmd<SelectCommand>("seçimi bırak", B1).Target);

    [Fact]
    public void Phrases36To38UndoAndRedo()
    {
        Assert.Equal(1, Cmd<UndoCommand>("geri al", B1).Steps);
        Assert.Equal(3, Cmd<UndoCommand>("üç adım geri", B1).Steps);
        Cmd<RedoCommand>("yinele", B1);
    }

    [Fact]
    public void Phrases39To43ViewToolFinishAndSave()
    {
        Assert.Equal(ViewAction.FitAll, Cmd<ViewCommand>("tümünü göster", B1).Action);
        Assert.Equal(ViewAction.PanLeft, Cmd<ViewCommand>("görünümü sola kaydır", B1).Action);
        Assert.Equal(DrawingTool.Rectangle, Cmd<ToolCommand>("dikdörtgen aracı", B1).Tool);
        Assert.Equal(FinishDrawingAction.Close, Cmd<FinishDrawingCommand>("çizimi kapat", B1).Action);
        Assert.Equal(FileAction.Save, Cmd<FileCommand>("kaydet", B1).Action);
    }

    [Fact]
    public void Phrases44To46HandOffToWindows()
    {
        Assert.Equal(HandoffTarget.ImportDxf, Cmd<HandoffCommand>("dxf al").Target);
        var seam = Cmd<HandoffCommand>("1 cm dikiş payı", B1);
        Assert.Equal(HandoffTarget.TextileTools, seam.Target);
        Assert.Equal(2, seam.Tab);
        Assert.Equal(10, seam.Value);
        var grading = Cmd<HandoffCommand>("bir beden büyüt", B1);
        Assert.Equal(HandoffTarget.TextileTools, grading.Target);
        Assert.Equal(1, grading.Tab);
        Assert.Null(grading.Value);
    }

    [Theory]
    [InlineData("makinada bastır")]
    [InlineData("plotterda bastır")]
    public void MachinePrintPhrasesPrepareReviewOnlyExport(string input)
    {
        var parsed = CommandParser.Parse(input);
        Assert.Equal(HandoffTarget.ExportHpgl, Assert.IsType<HandoffCommand>(parsed.Command).Target);

        var result = CommandPlanner.Plan(parsed, PlannerTestData.View(PlannerTestData.B1()), Selection.None);
        Assert.True(result.IsPlan, result.Refusal?.Message);
        Assert.Equal("HPGL / PLT dışa aktarma penceresi açılacak. Yalnız HPGL / PLT dosyası hazırlanır; dosya üretim onayı değildir ve makineye gönderilmez.", result.Plan!.Preview);
        Assert.True(result.Plan.Danger);
    }

    [Fact]
    public void Phrase47UnknownTextIsRefusedWithExamples()
    {
        var result = Run("zzz", B1);
        Assert.Equal("R01", result.Code);
        Assert.Contains("“zzz”", result.Notice!.Message);
        Assert.Contains("120 x 80 dikdörtgen", result.Notice.Message);
    }

    [Fact]
    public void Phrase48AsksForTheUnit()
    {
        var result = Run("12 x 8 dikdörtgen");
        Assert.Equal(ParseKind.Question, result.Kind);
        Assert.Equal("R06", result.Code);
        Assert.Equal("12 x 8 dikdörtgen: milimetre mi, santimetre mi?", result.Notice!.Message);
        Assert.Equal(new[] { "12 x 8 mm dikdörtgen", "12 x 8 cm dikdörtgen" }, result.Notice.Buttons.Select(b => b.Label));
    }

    [Fact]
    public void Phrase49RefusesTheAmbiguousNumber()
    {
        var result = Run("1.500 çizgi");
        Assert.Equal("R05", result.Code);
        Assert.Equal("1.500 iki türlü okunabilir. 1,5 mm için 1,5 yazın; 1500 mm için 1500 yazın.", result.Notice!.Message);
        Assert.Equal(new[] { "1,5 çizgi", "1500 çizgi" }, result.Notice.Buttons.Select(b => b.Label));
    }

    [Fact] public void Phrase50RectangleNeedsTwoMeasures() => Refuses("dikdörtgen", "R19");
    [Fact] public void Phrase51MoveNeedsANumber() => Refuses("taşı", "R02", B1);

    [Fact]
    public void Phrase52ParsesEvenWithoutASelection() =>
        Assert.Equal(25, Cmd<MoveByCommand>("25 sağa taşı").RightMm);

    [Fact] public void Phrase53RefusesOtherAngles() => Refuses("45 derece döndür", "R13", B1);
    [Fact] public void Phrase54RefusesOutOfRange() => Refuses("30000 x 80 dikdörtgen", "R04");
    [Fact] public void Phrase55RefusesTwoJobs() => Refuses("sil dikdörtgen 120 80", "R16", B1);

    [Fact]
    public void Phrase56RefusesTwoMeanings()
    {
        var result = Run("ters çevir", B1);
        Assert.Equal("R16b", result.Code);
        Assert.Contains("aynala", result.Notice!.Message);
        Assert.Contains("90 sağa döndür", result.Notice.Message);
    }

    [Fact] public void Phrase57RefusesMovingANamedPiece() => Refuses("ön bedeni taşı", "R27", B1);

    [Fact]
    public void Phrase58ParsesWhileDrawingIsPending() =>
        Assert.Equal(120, Cmd<RectangleCommand>("120 x 80 dikdörtgen", B1).WidthMm);

    [Fact]
    public void Phrase59ParsesAResizeThatThePlannerWillRefuse() =>
        Assert.Equal(95, Cmd<ResizeCommand>("genişliği 95 yap", B1).WidthMm);

    [Fact]
    public void Phrase60AsksWhichPieceWhenTwoMatch()
    {
        var result = Run("ön bedeni seç", B8);
        Assert.Equal("R15b", result.Code);
        Assert.Equal("2 parça eşleşti. Hangisi?", result.Notice!.Message);
        Assert.Equal(new[] { "Ön beden", "Ön beden 2" }, result.Notice.Buttons.Select(b => b.Label));
    }

    // ---- Decisions where the specification is loose --------------------------------------------------------------

    [Fact]
    public void AnExactlyEqualNameWinsOverLongerNames()
    {
        var select = Cmd<SelectCommand>("ön beden seç", B8);
        Assert.Equal("ön beden", select.Name);
        var second = Cmd<SelectCommand>("Ön beden 2 seç", B8);
        Assert.Equal("ön beden 2", second.Name);
    }

    [Fact]
    public void AButtonOfTheAmbiguityQuestionResolvesWhenTyped()
    {
        var result = Run("ön bedeni seç", B8);
        foreach (var button in result.Notice!.Buttons)
            Assert.True(Run(button.FillText, B8).IsCommand, button.FillText);
    }

    [Theory]
    [InlineData("90 sağa", 90, 0)]
    [InlineData("270 sola", -270, 0)]
    [InlineData("180 yukarı", 0, -180)]
    public void ANumberWithADirectionButWithoutARotationWordMovesInsteadOfRotating(string input, double right, double down)
    {
        var move = Cmd<MoveByCommand>(input, B1);
        Assert.Equal(right, move.RightMm);
        Assert.Equal(down, move.DownMm);
    }

    [Theory]
    [InlineData("saat yönünde 90", 90, RotationDirection.Clockwise)]
    [InlineData("90 derece", 90, RotationDirection.Clockwise)]
    [InlineData("180 derece", 180, RotationDirection.Clockwise)]
    [InlineData("270 sola döndür", 270, RotationDirection.CounterClockwise)]
    [InlineData("çeyrek tur", 90, RotationDirection.Clockwise)]
    [InlineData("baş aşağı döndür", 180, RotationDirection.Clockwise)]
    [InlineData("saat yönünün tersine döndür", 90, RotationDirection.CounterClockwise)]
    [InlineData("180 döndür", 180, RotationDirection.Clockwise)]
    public void RotationWordsRotate(string input, int degrees, RotationDirection direction)
    {
        var rotate = Cmd<RotateCommand>(input, B1);
        Assert.Equal(degrees, rotate.Degrees);
        Assert.Equal(direction, rotate.Direction);
    }

    [Theory]
    [InlineData("0 x 80 dikdörtgen")]
    [InlineData("0 x 8 dikdörtgen")]
    [InlineData("20001 çizgi")]
    [InlineData("0,5 mm çizgi")]
    [InlineData("99999999999 çizgi")]
    [InlineData("yüz bin çizgi")]
    [InlineData("yirmi bir bin çizgi")]
    public void OutOfRangeLengthsAreRefusedBeforeTheUnitQuestion(string input) => Refuses(input, "R04");

    [Theory]
    [InlineData("yirmi bin çizgi", 20000)]
    [InlineData("20000 çizgi", 20000)]
    [InlineData("1 mm çizgi", 1)]
    [InlineData("20 m çizgi", 20000)]
    public void TheLimitsThemselvesAreAccepted(string input, double length) =>
        Assert.Equal(length, Cmd<LineCommand>(input).LengthMm);

    [Theory]
    [InlineData("29 çizgi")]
    [InlineData("12,5 x 8 dikdörtgen")]
    [InlineData("kare 10")]
    [InlineData("genişliği 9 yap")]
    public void BareNumbersBelowThirtyAreAskedAbout(string input)
    {
        var result = Run(input, B1);
        Assert.Equal(ParseKind.Question, result.Kind);
        Assert.Equal("R06", result.Code);
    }

    [Theory]
    [InlineData("30 çizgi", 30)]
    [InlineData("12 x 80 dikdörtgen", 12)]
    [InlineData("12 mm x 8 dikdörtgen", 12)]
    public void ThirtyOrAnyWrittenUnitSkipsTheUnitQuestion(string input, double first)
    {
        var result = Run(input);
        Assert.True(result.IsCommand, result.ToString());
        _ = first;
    }

    [Fact]
    public void TheUnitQuestionPutsTheUnitAfterTheLastNumberOfWordsAndSuffixes()
    {
        var words = Run("on iki çarpı sekiz dikdörtgen");
        Assert.Equal("on iki çarpı sekiz mm dikdörtgen", words.Notice!.Buttons[0].Label);
        var suffix = Run("10'luk kare");
        Assert.Equal("10 cm kare", suffix.Notice!.Buttons[1].Label);
    }

    [Fact]
    public void TheLastUnitAppliesToEveryBareNumber()
    {
        var rect = Cmd<RectangleCommand>("10'a 15 santim dikdörtgen");
        Assert.Equal(100, rect.WidthMm);
        Assert.Equal(150, rect.HeightMm);
        var mixed = Cmd<RectangleCommand>("10 cm x 80 mm dikdörtgen");
        Assert.Equal(100, mixed.WidthMm);
        Assert.Equal(80, mixed.HeightMm);
        // Two move distances with a unit on only one of them could mean either unit for the other: refused.
        Assert.False(Run("20 sağa 10 santim yukarı", B1).IsCommand);
        var both = Cmd<MoveByCommand>("20 mm sağa 10 santim yukarı", B1);
        Assert.Equal(20, both.RightMm);
        Assert.Equal(-100, both.DownMm);
    }

    [Theory]
    [InlineData("1.500 çizgi", "R05")]
    [InlineData("1,500 çizgi", "R05")]
    [InlineData("2.050 çizgi", "R05")]
    [InlineData("10.000 sağa taşı", "R05")]
    [InlineData("1.234.567 çizgi", "R01")]
    [InlineData("1,234.5 çizgi", "R01")]
    [InlineData("15e-1 çizgi", "R01")]
    [InlineData("1e3 çizgi", "R01")]
    [InlineData("120-80 dikdörtgen", "R01")]
    [InlineData("-20 sağa", "R26")]
    [InlineData("eksi 20 sağa", "R26")]
    [InlineData("--5 sağa", "R26")]
    public void NumberHazardsAreRefused(string input, string code) => Refuses(input, code, B1);

    [Theory]
    [InlineData("0,125 mm çizgi", 0.125)]
    [InlineData("1,5 mm çizgi", 1.5)]
    [InlineData("1.5 mm çizgi", 1.5)]
    [InlineData("12,5 mm çizgi", 12.5)]
    [InlineData("0.125 mm çizgi", 0.125)]
    public void ShortDecimalsAreNotAmbiguous(string input, double value)
    {
        var result = Run(input);
        // Values below one millimetre are out of range (R04), but never ambiguous (R05) or unreadable (R01).
        if (value < 1) Assert.Equal("R04", result.Code);
        else Assert.Equal(value, Cmd<LineCommand>(input).LengthMm);
    }

    [Theory]
    [InlineData("on iki virgül beş mm çizgi", 12.5)]
    [InlineData("yirmi virgül yirmi beş mm çizgi", 20.25)]
    [InlineData("bin beş yüz çizgi", 1500)]
    [InlineData("iki bin yüz elli çizgi", 2150)]
    [InlineData("on iki bin çizgi", 12000)]
    [InlineData("yüz yirmi çizgi", 120)]
    [InlineData("iki yüz çizgi", 200)]
    [InlineData("bir buçuk mm çizgi", 1.5)]
    [InlineData("dört yüz çizgi", 400)]
    [InlineData("sıfır virgül beş mm çizgi", 0.5)]
    public void TurkishNumberWordsAreReadAsLengths(string input, double expected)
    {
        var result = Run(input);
        if (expected < 1) Assert.Equal("R04", result.Code);
        else Assert.Equal(expected, Cmd<LineCommand>(input).LengthMm, 9);
    }

    [Theory]
    [InlineData("eni 100 boyu 150 dikdörtgen", 100, 150)]
    [InlineData("boyu 150 eni 100 dikdörtgen", 100, 150)]
    [InlineData("100 çarpı 150 kutu", 100, 150)]
    [InlineData("100 × 150 dikdörtgen", 100, 150)]
    [InlineData("100 * 150 dikdörtgen", 100, 150)]
    [InlineData("100x150 dikdörtgen çiz", 100, 150)]
    [InlineData("lütfen şunu hemen 100 x 150 dikdörtgen çiz", 100, 150)]
    [InlineData("bana bir tane 100 x 150 dikdörtgen ver", 100, 150)]
    [InlineData("100 x 150 dikdörtgen rica ederim", 100, 150)]
    [InlineData("10 m x 2 m dikdörtgen", 10000, 2000)]
    [InlineData("100e 150 dikdörtgen", 100, 150)]
    [InlineData("100'ye 150 dikdörtgen", 100, 150)]
    [InlineData("yüze 150 dikdörtgen", 100, 150)]
    public void RectangleForms(string input, double width, double height)
    {
        var rect = Cmd<RectangleCommand>(input);
        Assert.Equal(width, rect.WidthMm);
        Assert.Equal(height, rect.HeightMm);
    }

    [Theory]
    [InlineData("100 x 120 kare")]
    [InlineData("1 2 3 dikdörtgen")]
    [InlineData("100 x 150 x 3 dikdörtgen")]
    public void WrongNumberCountsAreRefusedNotGuessed(string input) =>
        Assert.False(Run(input).IsCommand);

    [Theory]
    [InlineData("kare")]
    [InlineData("çizgi")]
    public void ShapesWithoutALengthAskForOne(string input) => Refuses(input, "R02");

    [Theory]
    [InlineData("dikdörtgen", "R19")]
    [InlineData("100 dikdörtgen", "R19")]
    [InlineData("eni 100 dikdörtgen", "R19")]
    public void RectanglesNeedTwoMeasures(string input, string code) => Refuses(input, code);

    [Theory]
    [InlineData("adı olsun")]
    [InlineData("adı")]
    [InlineData("ismi yap")]
    [InlineData("adını '' olsun")]
    public void AnEmptyNameIsRefused(string input) => Refuses(input, "R24", B1);

    [Theory]
    [InlineData("adını ön beden yap", "ön beden")]
    [InlineData("ismi Kol 2 olsun", "Kol 2")]
    [InlineData("adı \"Sağ Kol\" olsun", "Sağ Kol")]
    [InlineData("lütfen adını SOL KOL olarak değiştir", "SOL KOL")]
    public void RenameKeepsTheLettersAsTyped(string input, string expected) =>
        Assert.Equal(expected, Cmd<RenameCommand>(input, B1).Name);

    [Fact]
    public void ARenameLongerThan128CharactersIsNotAccepted()
    {
        Assert.False(Run("adı " + new string('a', 129) + " olsun").IsCommand);
        Assert.Equal(128, Cmd<RenameCommand>("adı " + new string('a', 128) + " olsun").Name.Length);
    }

    [Fact]
    public void InputOver200CharactersIsRefusedAndTheLimitItselfIsAccepted()
    {
        var ok = "120 x 80 dikdörtgen" + new string(' ', 200 - "120 x 80 dikdörtgen".Length) + "x";
        Assert.Equal(201, ok.Length);
        Refuses(ok, "R23");
        var exactly = ("120 x 80 dikdörtgen " + new string('a', 200)).Substring(0, 200);
        Assert.Equal(200, exactly.Length);
        Assert.NotEqual("R23", Run(exactly).Code);
        Assert.Equal("R23", Run(new string('a', 201)).Code);
        // White space around the text does not count.
        Assert.True(Run("   120 x 80 dikdörtgen   " + new string(' ', 400)).IsCommand);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    [InlineData("...")]
    [InlineData("!!!")]
    [InlineData("😀")]
    [InlineData("１２０ x ８０ dikdörtgen")]
    [InlineData("120 x 80 dikdörtgen 😀")]
    public void EmptyAndStrangeInputIsRefusedWithExamples(string input)
    {
        var result = Run(input);
        Assert.Equal("R01", result.Code);
        Assert.True(result.Notice!.HasExample);
    }

    [Fact]
    public void NullInputIsRefusedAndNeverThrows() => Assert.Equal("R01", CommandParser.Parse(null).Code);

    [Fact]
    public void ALoneQuestionMarkAsksForHelp() => Cmd<HelpCommand>("?");

    [Theory]
    [InlineData("yapay zeka ile çiz")]
    [InlineData("bir asistan gibi çiz")]
    [InlineData("model yap")]
    public void TheForbiddenWordsNeverAppearInAMessage(string input)
    {
        var result = Run(input);
        Assert.Equal("R01", result.Code);
        Assert.False(TurkishText.ContainsForbiddenWord(result.Notice!.Message));
        Assert.All(result.Notice.Buttons, b => Assert.False(TurkishText.ContainsForbiddenWord(b.Label + b.FillText)));
    }

    [Fact]
    public void CancelWordsOnlyClosePreviews()
    {
        Cmd<CancelPreviewCommand>("vazgeç");
        Cmd<CancelPreviewCommand>("iptal");
        Cmd<CancelPreviewCommand>("VAZGEÇ");
    }

    [Theory]
    [InlineData("dikdortgn 120 x 80")]
    [InlineData("dikdörtgenn 120 x 80")]
    public void ATypoOffersADidYouMeanButtonAndStillRefuses(string input)
    {
        var result = Run(input);
        Assert.Equal("R01", result.Code);
        var hint = result.Notice!.Buttons[0];
        Assert.Equal("Şunu mu demek istediniz: dikdörtgen", hint.Label);
        Assert.Contains("dikdörtgen", hint.FillText);
        Assert.True(Run(hint.FillText).IsCommand, hint.FillText);
    }

    [Fact]
    public void ShortWordsGetNoDidYouMean()
    {
        var result = Run("taş 5");
        Assert.Equal("R01", result.Code);
        Assert.DoesNotContain(result.Notice!.Buttons, b => b.Label.StartsWith("Şunu mu"));
    }

    // ---- Selection by name -----------------------------------------------------------------------------------------

    [Fact]
    public void NamesNeedPiecesInTheProject()
    {
        var none = Run("kolu sil");
        Assert.Equal("R03", none.Code);
        Assert.Contains("Henüz parça yok", none.Notice!.Message);
        var missing = Run("yakayı seç", B1);
        Assert.Equal("R15a", missing.Code);
        Assert.Equal("“yakayı” adında parça bulunamadı. Örnek: Ön beden seç", missing.Notice!.Message);
        Assert.Equal(new[] { "Ön beden", "Arka beden", "Kol" }, missing.Notice.Buttons.Select(b => b.Label));
    }

    [Theory]
    [InlineData("on beden seç", "on beden")]
    [InlineData("ON BEDENİ SEÇ", "on beden")]
    [InlineData("arka bedeni seç", "arka beden")]
    [InlineData("KOLU SEÇ", "kol")]
    [InlineData("kolu seç.", "kol")]
    [InlineData("seç kol", "kol")]
    public void NamesMatchWithFoldingAndWithoutTheObjectSuffix(string input, string query)
    {
        var select = Cmd<SelectCommand>(input, B1);
        Assert.Equal(PieceTarget.Named, select.Target);
        Assert.Equal(query, select.Name);
    }

    [Theory]
    [InlineData("parçayı sil")]
    [InlineData("bunu sil")]
    [InlineData("seçili parçayı sil")]
    [InlineData("kaldır")]
    [InlineData("SİL")]
    public void DeleteWithoutANameDeletesTheSelectedPiece(string input) =>
        Assert.Equal(PieceTarget.Selected, Cmd<DeleteCommand>(input, B1).Target);

    [Theory]
    [InlineData("kolu döndür")]
    [InlineData("arka bedeni aynala")]
    [InlineData("ön bedeni 25 sağa taşı")]
    [InlineData("kolu genişliği 95 yap")]
    public void AnotherCommandWithAPieceNameIsRefusedWithR27(string input) => Refuses(input, "R27", B1);

    [Theory]
    [InlineData("sol kolu seç")]
    [InlineData("SAĞ KOL SEÇ")]
    public void DirectionWordsInsideAPieceNameAreNotMoves(string input)
    {
        var select = Cmd<SelectCommand>(input, "Sol kol", "Sağ kol");
        Assert.Equal(PieceTarget.Named, select.Target);
    }

    // ---- Context never changes how a drawing command is read --------------------------------------------------------

    [Fact]
    public void DrawingInProgressDoesNotChangeTheParse()
    {
        var plain = CommandParser.Parse("120 x 80 dikdörtgen", new ParserContext(B1));
        var drawing = CommandParser.Parse("120 x 80 dikdörtgen", new ParserContext(B1, DrawingInProgress: true));
        Assert.Equal(plain.Command, drawing.Command);
    }

    [Fact]
    public void ANullPieceListIsTreatedAsEmpty() =>
        Assert.Equal("R03", CommandParser.Parse("kolu sil", new ParserContext(null!)).Code);

    // ---- Every command family answers its documented forms -----------------------------------------------------------

    [Theory]
    [InlineData("geri", 1)]
    [InlineData("son işlemi geri al", 1)]
    [InlineData("3 adım geri", 3)]
    [InlineData("iki geri", 2)]
    [InlineData("on adım geri al", 10)]
    public void UndoForms(string input, int steps) => Assert.Equal(steps, Cmd<UndoCommand>(input, B1).Steps);

    [Theory]
    [InlineData("11 adım geri")]
    [InlineData("0 adım geri")]
    [InlineData("1,5 adım geri")]
    public void UndoStepsOutsideOneToTenAreRefused(string input) => Assert.False(Run(input, B1).IsCommand);

    [Theory]
    [InlineData("yinele")]
    [InlineData("ileri al")]
    [InlineData("ileri")]
    [InlineData("tekrar yap")]
    public void RedoForms(string input) => Cmd<RedoCommand>(input, B1);

    [Theory]
    [InlineData("seç aracı", DrawingTool.Select)]
    [InlineData("çizgi aracı", DrawingTool.Line)]
    [InlineData("nokta nokta çiz", DrawingTool.Polyline)]
    [InlineData("çoklu çizgi aracı", DrawingTool.Polyline)]
    [InlineData("eğri aracı", DrawingTool.Bezier)]
    [InlineData("bézier aracı", DrawingTool.Bezier)]
    [InlineData("kavis çiz", DrawingTool.Bezier)]
    [InlineData("yay aracı", DrawingTool.Arc)]
    [InlineData("yay çiz", DrawingTool.Arc)]
    [InlineData("fareyle dikdörtgen", DrawingTool.Rectangle)]
    public void ToolForms(string input, DrawingTool tool) => Assert.Equal(tool, Cmd<ToolCommand>(input, B1).Tool);

    [Theory]
    [InlineData("kaydet", FileAction.Save)]
    [InlineData("farklı kaydet", FileAction.SaveAs)]
    [InlineData("yedeğini al", FileAction.SaveCopy)]
    [InlineData("aç", FileAction.Open)]
    [InlineData("dünkü işi getir", FileAction.Open)]
    [InlineData("yeni", FileAction.New)]
    [InlineData("sıfırdan başla", FileAction.New)]
    public void FileForms(string input, FileAction action) => Assert.Equal(action, Cmd<FileCommand>(input, B1).Action);

    [Theory]
    [InlineData("hepsini 20 sağa taşı", HandoffTarget.GroupMove, null)]
    [InlineData("parçaları taşı", HandoffTarget.GroupMove, null)]
    [InlineData("pastal yap", HandoffTarget.TextileTools, 3)]
    [InlineData("kumaş eni 150 santim pastal", HandoffTarget.TextileTools, 3)]
    [InlineData("dikiş payı 10", HandoffTarget.TextileTools, 2)]
    [InlineData("etrafına 1 cm pay", HandoffTarget.TextileTools, 2)]
    [InlineData("bir buçuk santim pay ver", HandoffTarget.TextileTools, 2)]
    [InlineData("L bedeni çıkar", HandoffTarget.TextileTools, 1)]
    [InlineData("serileme yap", HandoffTarget.TextileTools, 1)]
    [InlineData("adet 2 yap", HandoffTarget.PieceMetadata, null)]
    [InlineData("kumaşı astar yap", HandoffTarget.PieceMetadata, null)]
    [InlineData("çoğalt", HandoffTarget.Duplicate, null)]
    [InlineData("karşı parçasını çıkar", HandoffTarget.MirrorCopy, null)]
    [InlineData("yeni katman", HandoffTarget.Layers, null)]
    [InlineData("koordinat gir", HandoffTarget.CoordinateDrawing, null)]
    [InlineData("kesime gönder", HandoffTarget.Export, null)]
    [InlineData("makinada bastır", HandoffTarget.ExportHpgl, null)]
    [InlineData("plotterda bastır", HandoffTarget.ExportHpgl, null)]
    [InlineData("yazdır", HandoffTarget.ExportProjectA4, null)]
    [InlineData("kağıda yazdır", HandoffTarget.ExportProjectA4, null)]
    [InlineData("a4 çıktı", HandoffTarget.ExportSelectedA4, null)]
    [InlineData("seçili parçayı dxf yap", HandoffTarget.ExportSelectedDxf, null)]
    [InlineData("hakkında", HandoffTarget.About, null)]
    public void HandoffForms(string input, HandoffTarget target, int? tab)
    {
        var handoff = Cmd<HandoffCommand>(input, B1);
        Assert.Equal(target, handoff.Target);
        Assert.Equal(tab, handoff.Tab);
    }

    [Theory]
    [InlineData("kumaş eni 150 santim pastal", 1500)]
    [InlineData("dikiş payı 10", 10)]
    [InlineData("etrafına 1 cm pay", 10)]
    [InlineData("bir buçuk santim pay ver", 15)]
    [InlineData("adet 2 yap", 2)]
    public void HandoffValuesCarryTheTypedNumber(string input, double value) =>
        Assert.Equal(value, Cmd<HandoffCommand>(input, B1).Value);

    [Theory]
    [InlineData("tümünü göster", ViewAction.FitAll)]
    [InlineData("ekrana sığdır", ViewAction.FitAll)]
    [InlineData("seçiliyi göster", ViewAction.FitSelected)]
    [InlineData("yakınlaştır", ViewAction.ZoomIn)]
    [InlineData("uzaklaş", ViewAction.ZoomOut)]
    [InlineData("görünümü yukarı kaydır", ViewAction.PanUp)]
    [InlineData("görünümü sağa kaydır", ViewAction.PanRight)]
    [InlineData("ayrıntılı görünüm", ViewAction.DetailedView)]
    [InlineData("yazıyı büyüt", ViewAction.TextLarger)]
    [InlineData("koyu tema", ViewAction.ThemeDark)]
    public void ViewForms(string input, ViewAction action) => Assert.Equal(action, Cmd<ViewCommand>(input, B1).Action);

    [Theory]
    [InlineData("büyüt")]
    [InlineData("küçült")]
    [InlineData("cevir")]
    public void TwoMeaningWordsAreRefusedWithBothReadings(string input) => Refuses(input, "R16b", B1);

    [Theory]
    [InlineData("kare 100 ve 20 sağa taşı")]
    [InlineData("döndür ve aynala")]
    [InlineData("geri al kare 100")]
    [InlineData("kolu sil ve geri al")]
    public void TwoJobsAreRefused(string input) => Refuses(input, "R16", B1);

    [Theory]
    [InlineData("20 sağa 10 sola")]
    [InlineData("25 taşı")]
    [InlineData("sağa 20 yukarı")]
    [InlineData("20, 10 sağa")]
    [InlineData("25 sağa yukarı")]
    public void IncompleteMovesAreRefusedNotGuessed(string input) => Assert.False(Run(input, B1).IsCommand);

    [Theory]
    [InlineData("sağa 20 yukarı 10", 20, -10)]
    [InlineData("sağa 20 ve yukarı 10", 20, -10)]
    [InlineData("20 sağa ve 10 aşağı", 20, 10)]
    [InlineData("2 cm sola 5 mm yukarı", -20, -5)]
    [InlineData("10 mm aşağı al", 0, 10)]
    [InlineData("on milim sola", -10, 0)]
    [InlineData("sağa 25", 25, 0)]
    [InlineData("25 SAĞA TAŞI", 25, 0)]
    [InlineData("25 saga tasi", 25, 0)]
    [InlineData("25   sağa    taşı  ", 25, 0)]
    [InlineData("25 sağa taşı.", 25, 0)]
    public void MoreMoveForms(string input, double right, double down)
    {
        var move = Cmd<MoveByCommand>(input, B1);
        Assert.Equal(right, move.RightMm);
        Assert.Equal(down, move.DownMm);
    }

    [Theory]
    [InlineData("x 100 y 50 konumuna taşı", 100, 50)]
    [InlineData("100, 50 noktasına taşı", 100, 50)]
    [InlineData("soldan 100 yukarıdan 50 konuma taşı", 100, 50)]
    [InlineData("x 0 y 0 konumuna taşı", 0, 0)]
    [InlineData("x 10 cm y 5 cm konumuna taşı", 100, 50)]
    public void PositionForms(string input, double x, double y)
    {
        var move = Cmd<MoveToCommand>(input, B1);
        Assert.Equal(x, move.XMm);
        Assert.Equal(y, move.YMm);
    }

    [Theory]
    [InlineData("x 20001 y 5 konumuna taşı", "R04")]
    [InlineData("x 10 konumuna taşı", "R01")]
    [InlineData("x -10 y 5 konumuna taşı", "R26")]
    public void PositionRefusals(string input, string code) => Refuses(input, code, B1);

    [Theory]
    [InlineData("genişlik 95", 95.0, null)]
    [InlineData("eni 95", 95.0, null)]
    [InlineData("boyu 60 yap", null, 60.0)]
    [InlineData("yüksekliği 60", null, 60.0)]
    [InlineData("boy 6 cm yap", null, 60.0)]
    [InlineData("ölçüsünü 95 x 60 yap", 95.0, 60.0)]
    [InlineData("95'e 60 yap", 95.0, 60.0)]
    [InlineData("eni 95 boyu 60 yap", 95.0, 60.0)]
    [InlineData("genişliği yüz yirmi yap", 120.0, null)]
    public void ResizeForms(string input, double? width, double? height)
    {
        var resize = Cmd<ResizeCommand>(input, B1);
        Assert.Equal(width, resize.WidthMm);
        Assert.Equal(height, resize.HeightMm);
    }

    [Theory]
    [InlineData("genişliği yap")]
    [InlineData("ölçüsünü 95 yap")]
    [InlineData("eni 95 eni 60 yap")]
    [InlineData("genişliği 0 yap")]
    public void ResizeRefusals(string input) => Assert.False(Run(input, B1).IsCommand);

    [Theory]
    [InlineData("alt üst aynala", MirrorAxis.Horizontal)]
    [InlineData("yatay aynala", MirrorAxis.Horizontal)]
    [InlineData("yukarı aşağı aynala", MirrorAxis.Horizontal)]
    [InlineData("sağ sol aynala", MirrorAxis.Vertical)]
    [InlineData("dikey eksene göre aynala", MirrorAxis.Vertical)]
    [InlineData("AYNALA", MirrorAxis.Vertical)]
    public void MirrorForms(string input, MirrorAxis axis) => Assert.Equal(axis, Cmd<MirrorCommand>(input, B1).Axis);

    [Theory]
    [InlineData("100 derece döndür")]
    [InlineData("0 döndür")]
    [InlineData("360 döndür")]
    [InlineData("90 cm döndür")]
    public void RotationAnglesOtherThanQuarterTurnsAreRefused(string input) =>
        Assert.False(Run(input, B1).IsCommand);

    [Fact]
    public void ResultsAreEqualByValue()
    {
        Assert.Equal(new RectangleCommand(120, 80), Cmd<RectangleCommand>("120 x 80 dikdörtgen"));
        Assert.Equal(Run("kaydet").Command, Run("projeyi kaydet").Command);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    public void TheResultDoesNotDependOnTheCurrentCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal(new RectangleCommand(125, 80), Cmd<RectangleCommand>("DİKDÖRTGEN 12,5 CM X 8 CM"));
            Assert.Equal(new MoveByCommand(0, -5), Cmd<MoveByCommand>("YARIM SANTİM YUKARI", B1));
            Assert.Equal("R05", Run("1.500 ÇİZGİ").Code);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }
}
