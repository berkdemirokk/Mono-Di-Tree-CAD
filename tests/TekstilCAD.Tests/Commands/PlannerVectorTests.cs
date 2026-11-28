using System.Text.Json;
using TekstilCAD.Core;
using TekstilCAD.Core.Commands;

namespace TekstilCAD.Tests;

/// <summary>
/// Property-style checks of the whole path text -> <see cref="CommandParser"/> -> <see cref="CommandPlanner"/> in the nine
/// contexts B0-B8 of FINAL_COMMANDS.md section 5. Every phrase (the 60 specification sentences with typo, upper-case,
/// no-diacritic, spacing and full-stop variants, plus hostile extras) must give exactly one of plan and refusal, a one-line
/// preview or a message without placeholders, and none of the words the product never says. When the shared vector file
/// (Apple/Tests/Fixtures/command-vectors.json) can be found its inputs get the same treatment.
/// </summary>
public sealed class PlannerVectorTests
{
    private static readonly string[] ContextNames = ["B0", "B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8"];

    private const string UnitSentence = "Birim yazmadınız; milimetre sayıldı.";

    private sealed record Ctx(string Name, ProjectDocument Document, DocumentView View, Selection Selection)
    {
        public ParserContext Parser => new(Document.Pieces.Select(p => p.Name).ToList(), View.DrawingInProgress);
    }

    /// <summary>
    /// B0 empty project. B1 three pieces ("Ön beden" 480 x 620 selected, undo stack filled, no drawing). B2 B1 without a selection.
    /// B3 B1 with curves on "Ön beden". B4 B1 with "Ön beden" on a locked layer. B5 B1 while a mouse drawing is in progress.
    /// B6 B1 with an empty undo stack. B7 B1 with a grainline on "Ön beden". B8 two pieces "Ön beden" and "Ön beden 2".
    /// </summary>
    private static Ctx Build(string name)
    {
        ProjectDocument doc;
        Selection selection;
        var undo = 3;
        var redo = 1;
        var drawing = false;
        var points = 0;
        if (name == "B0")
        {
            doc = new ProjectDocument();
            selection = Selection.None;
            undo = 0;
            redo = 0;
        }
        else if (name == "B8")
        {
            doc = PieceOperationTestData.Doc(
                PlannerHostileTests.Rect("Ön beden", 20, 20, 480, 620),
                PlannerHostileTests.Rect("Ön beden 2", 520, 20, 480, 620));
            selection = Selection.Of(doc.Pieces[0].Id);
        }
        else
        {
            doc = PlannerHostileTests.Standard();
            selection = Selection.Of(doc.Pieces[0].Id);
        }

        switch (name)
        {
            case "B2":
                selection = Selection.None;
                break;
            case "B3":
                doc.Pieces[0].Curves =
                [
                    new CurveSegment.Line(new Point2(20, 20), new Point2(500, 20)),
                    new CurveSegment.Line(new Point2(500, 20), new Point2(500, 640)),
                    new CurveSegment.Line(new Point2(500, 640), new Point2(20, 640)),
                    new CurveSegment.Line(new Point2(20, 640), new Point2(20, 20)),
                ];
                break;
            case "B4":
                var locked = new Layer { Name = "Kilitli", Locked = true };
                PieceOperationTestData.WithLayers(doc, locked);
                doc.Pieces[0].LayerId = locked.Id;
                break;
            case "B5":
                drawing = true;
                points = 3;
                break;
            case "B6":
                undo = 0;
                break;
            case "B7":
                doc.Pieces[0].Grainline = new Grainline { Start = new Point2(60, 60), End = new Point2(60, 500) };
                break;
        }

        var view = new DocumentView(doc, PlannerHostileTests.Epoch)
        {
            UndoCount = undo,
            RedoCount = redo,
            DrawingInProgress = drawing,
            DrawingPointCount = points,
        };
        return new Ctx(name, doc, view, selection);
    }

    // ================================================================================================================
    // The 60 sentences of FINAL_COMMANDS.md section 5 with the outcome of the planner ("PlanKind/PlanClass" or a refusal code)
    // ================================================================================================================

    private static readonly (int Number, string Phrase, string Context, string Expect)[] Spec =
    [
        (1, "120 x 80 dikdörtgen", "B0", "AddRectangle/Geometry"),
        (2, "120x80 dikdörtgen", "B0", "AddRectangle/Geometry"),
        (3, "dikdörtgen 120 80", "B0", "AddRectangle/Geometry"),
        (4, "100'e 150 dikdörtgen", "B0", "AddRectangle/Geometry"),
        (5, "12 x 8 cm dikdörtgen", "B0", "AddRectangle/Geometry"),
        (6, "yüz yirmi çarpı seksen dikdörtgen", "B0", "AddRectangle/Geometry"),
        (7, "DİKDÖRTGEN 120 X 80", "B0", "AddRectangle/Geometry"),
        (8, "dikdortgen 120x80", "B0", "AddRectangle/Geometry"),
        (9, "kare 100", "B0", "AddRectangle/Geometry"),
        (10, "100'lük kare", "B0", "AddRectangle/Geometry"),
        (11, "150 çizgi", "B0", "AddLine/Geometry"),
        (12, "15 cm yatay çizgi", "B0", "AddLine/Geometry"),
        (13, "80 dikey çizgi", "B0", "AddLine/Geometry"),
        (14, "25 sağa taşı", "B1", "MovePiece/Geometry"),
        (15, "25 sağa", "B1", "MovePiece/Geometry"),
        (16, "1 cm sola kaydır", "B1", "MovePiece/Geometry"),
        (17, "yarım santim yukarı", "B1", "MovePiece/Geometry"),
        (18, "bir buçuk santim aşağı", "B1", "MovePiece/Geometry"),
        (19, "1 santim 2 milim sağa", "B1", "MovePiece/Geometry"),
        (20, "20 sağa 10 yukarı", "B1", "MovePiece/Geometry"),
        (21, "x 100 y 50 konumuna taşı", "B1", "MovePieceTo/Geometry"),
        (22, "döndür", "B1", "RotatePiece/Geometry"),
        (23, "90 sağa döndür", "B1", "RotatePiece/Geometry"),
        (24, "sola döndür", "B1", "RotatePiece/Geometry"),
        (25, "yarım tur", "B1", "RotatePiece/Geometry"),
        (26, "aynala", "B1", "MirrorPiece/Geometry"),
        (27, "alt üst aynala", "B1", "MirrorPiece/Geometry"),
        (28, "genişliği 95 yap", "B1", "ResizePiece/Geometry"),
        (29, "eni 9,5 cm yap", "B1", "ResizePiece/Geometry"),
        (30, "95 x 60 yap", "B1", "ResizePiece/Geometry"),
        (31, "adı Ön Beden olsun", "B1", "RenamePiece/Geometry"),
        (32, "sil", "B1", "DeletePiece/Dangerous"),
        (33, "kolu sil", "B1", "DeletePiece/Dangerous"),
        (34, "ön bedeni seç", "B1", "SelectPiece/NoChange"),
        (35, "seçimi bırak", "B1", "SelectPiece/NoChange"),
        (36, "geri al", "B1", "Undo/NoChange"),
        (37, "üç adım geri", "B1", "Undo/NoChange"),
        (38, "yinele", "B1", "Redo/NoChange"),
        (39, "tümünü göster", "B1", "View/NoChange"),
        (40, "görünümü sola kaydır", "B1", "View/NoChange"),
        (41, "dikdörtgen aracı", "B1", "Tool/NoChange"),
        (42, "çizimi kapat", "B5", "FinishDrawing/Geometry"),
        (43, "kaydet", "B1", "File/Window"),
        (44, "dxf al", "B0", "Window/Window"),
        (45, "1 cm dikiş payı", "B1", "Window/Window"),
        (46, "bir beden büyüt", "B1", "Window/Window"),
        (47, "zzz", "B1", "R01"),
        (48, "12 x 8 dikdörtgen", "B0", "R06"),
        (49, "1.500 çizgi", "B0", "R05"),
        (50, "dikdörtgen", "B0", "R19"),
        (51, "taşı", "B1", "R02"),
        (52, "25 sağa taşı", "B2", "R03"),
        (53, "45 derece döndür", "B1", "R13"),
        (54, "30000 x 80 dikdörtgen", "B0", "R04"),
        (55, "sil dikdörtgen 120 80", "B1", "R16"),
        (56, "ters çevir", "B1", "R16b"),
        (57, "ön bedeni taşı", "B1", "R27"),
        (58, "120 x 80 dikdörtgen", "B5", "R07"),
        (59, "genişliği 95 yap", "B3", "R11"),
        (60, "ön bedeni seç", "B8", "R15b"),
    ];

    private static PlanResult Run(string phrase, string contextName)
    {
        var ctx = Build(contextName);
        var parsed = CommandParser.Parse(phrase, ctx.Parser);
        return CommandPlanner.Plan(parsed, ctx.View, ctx.Selection);
    }

    private static Plan PlanOf(string phrase, string contextName)
    {
        var result = Run(phrase, contextName);
        Assert.True(result.IsPlan, $"\"{phrase}\" ({contextName}): {result}");
        return result.Plan!;
    }

    // ================================================================================================================
    // The built-in phrase list
    // ================================================================================================================

    private static readonly Lazy<IReadOnlyList<string>> Phrases = new(BuildPhrases);

    private static string Typo(string phrase)
    {
        var words = phrase.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            if (word.Length > 5 && word.All(char.IsLetter))
            {
                words[i] = word.Remove(word.Length / 2, 1);
                return string.Join(' ', words);
            }
        }
        return phrase + phrase.Substring(Math.Max(0, phrase.Length - 1));
    }

    private static IReadOnlyList<string> BuildPhrases()
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string text)
        {
            if (seen.Add(text)) list.Add(text);
        }

        foreach (var (_, phrase, _, _) in Spec)
        {
            Add(phrase);
            Add(TurkishText.ToUpper(phrase));
            Add(TurkishText.Fold(phrase));
            Add("  " + phrase.Replace(" ", "   ") + "  ");
            Add(phrase + ".");
            Add(Typo(phrase));
        }

        string[] extras =
        [
            "", " ", "?", "yardım", "vazgeç", "iptal", "çoğalt", "kopyala", "sil", "kolu sil", "ön bedeni sil", "ön beden seç",
            "arka bedeni seç", "sonraki parçayı seç", "önceki parçayı seç", "seçiliyi göster", "yakınlaştır", "uzaklaştır",
            "görünümü sağa kaydır", "görünümü yukarı kaydır", "görünümü aşağı kaydır", "ayrıntılı görünüm", "tüm düğmeler", "tüm araçlar",
            "yazıyı büyüt", "yazıyı küçült", "koyu tema", "açık tema", "seç aracı", "çizgi aracı", "nokta nokta çiz", "eğri aracı",
            "yay aracı", "çizimi açık bırak", "çizimi iptal et", "son noktayı sil", "koordinat gir", "farklı kaydet", "yedeğini al",
            "aç", "yeni", "dışa aktar", "seçili parçayı dxf yap", "a4 çıktı", "pastal yap", "kumaş eni 150 santim pastal",
            "makine dosyası hazırla", "iplik yönü", "parça bilgileri", "adet 2 yap", "karşı parçasını çıkar", "birden fazla parçayı taşı",
            "yeni katman", "kurtarma tara", "elbise isteği yaz", "hakkında", "boyu 60 yap", "yüksekliği 60", "180 derece", "270 sağa",
            "çeyrek tur", "hepsini 20 sağa taşı", "0 x 80 dikdörtgen", "30000 x 80 dikdörtgen", "1.500 çizgi", "1,500 çizgi",
            "0,125 çizgi", "1,5 çizgi", "15e-1 çizgi", "eksi 20 sağa", "-20 sağa", "20 sağa 20 sola", "yapay zeka ile çiz",
            "asistan sil", "model döndür", "{n} dikdörtgen", "{ad} seç", "120 x 80 dikdörtgen\n25 sağa taşı", "120\tx\t80 dikdörtgen",
            "\0\u0001 sil", "😀", "adı \uD800 olsun", "adı  olsun", "adı", "adı a\nb olsun", "kol seç", "KOL SİL", "ön beden 2 seç",
            "arka seç", "99999 çizgi", "yüz bin çizgi", "dokuz yüz doksan dokuz çizgi", "yüz kare", "1 x 1 dikdörtgen",
            "20000 x 20000 dikdörtgen", "20000 çizgi", "bir adım geri", "on adım geri", "on bir adım geri", "geri al geri al",
            "döndür döndür", "büyüt", "küçült", "ters çevir", "x 100 y 50", "100 50 konumuna taşı", "x -5 y 5 konumuna taşı",
            "1 beden büyüt", "dikiş payı 10", "pastal 1500", "kumaş eni 150 pastal",
        ];
        foreach (var text in extras) Add(text);
        Add("adı " + new string('x', 150) + " olsun");
        Add(new string('a', 201));
        Add(string.Concat(Enumerable.Repeat("sağa ", 60)));
        Add(string.Concat(Enumerable.Repeat("9", 40)) + " çizgi");
        return list;
    }

    // ================================================================================================================
    // Checks shared by the built-in phrases and the shared vector file
    // ================================================================================================================

    private static string Shorten(string text)
    {
        var shown = text.Length <= 60 ? text : text.Substring(0, 60) + "...";
        return shown.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t").Replace("\0", "\\0");
    }

    private static void CheckPhrase(Ctx ctx, ParserContext parserContext, string phrase)
    {
        var where = $"{ctx.Name} / \"{Shorten(phrase)}\"";
        ParseResult parsed;
        PlanResult first;
        PlanResult second;
        try
        {
            parsed = CommandParser.Parse(phrase, parserContext);
        }
        catch (Exception ex)
        {
            throw new Xunit.Sdk.XunitException($"{where}: the parser threw {ex.GetType().Name}: {ex.Message}");
        }
        try
        {
            first = CommandPlanner.Plan(parsed, ctx.View, ctx.Selection);
            second = CommandPlanner.Plan(parsed, ctx.View, ctx.Selection);
        }
        catch (Exception ex)
        {
            throw new Xunit.Sdk.XunitException($"{where}: the planner threw {ex.GetType().Name} for {parsed}: {ex.Message}");
        }

        PlannerHostileTests.AssertSound(first, ctx.View, ctx.Selection, phrase, where);
        PlannerHostileTests.AssertSameOutcome(first, second, where);

        if (!parsed.IsCommand)
        {
            Assert.False(first.IsPlan, $"{where}: a parser refusal became a plan");
            Assert.Same(parsed.Notice, first.Refusal);
            return;
        }

        if (first.Plan is { } plan && plan.Kind is PlanKind.AddRectangle or PlanKind.AddLine or PlanKind.MovePiece or PlanKind.MovePieceTo or PlanKind.ResizePiece)
        {
            if (parsed.UnitAssumed) Assert.True(plan.Preview.EndsWith(UnitSentence, StringComparison.Ordinal), $"{where}: no unit was written but the preview does not say so: {plan.Preview}");
            else Assert.True(!plan.Preview.Contains("Birim yazmadınız", StringComparison.Ordinal), $"{where}: a unit was written but the preview says it was assumed: {plan.Preview}");
        }
    }

    private static void CheckPhrases(IEnumerable<string> phrases, string source)
    {
        var all = phrases.ToList();
        foreach (var name in ContextNames)
        {
            var ctx = Build(name);
            var snapshot = ctx.Document.Clone();
            var selectionBefore = ctx.Selection.PieceIds.ToArray();
            var parserContext = ctx.Parser;
            foreach (var phrase in all) CheckPhrase(ctx, parserContext, phrase);
            Assert.True(DocumentSession.ContentEquals(snapshot, ctx.Document), $"{source} in {name}: planning changed the document");
            Assert.True(selectionBefore.SequenceEqual(ctx.Selection.PieceIds.ToArray()), $"{source} in {name}: planning changed the selection");
        }
    }

    // ================================================================================================================
    // Tests
    // ================================================================================================================

    [Fact]
    public void BuiltInPhraseListHasAtLeastOneHundredFiftyDistinctStrings()
    {
        var phrases = Phrases.Value;
        Assert.True(phrases.Count >= 150, $"only {phrases.Count} phrases");
        Assert.Equal(phrases.Count, phrases.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(60, Spec.Length);
        foreach (var (_, phrase, _, _) in Spec) Assert.Contains(phrase, phrases);
    }

    [Fact]
    public void ContextsB0ToB8AreBuiltAsTheSpecificationDescribesThem()
    {
        Assert.Empty(Build("B0").Document.Pieces);
        var b1 = Build("B1");
        Assert.Equal(new[] { "Ön beden", "Arka beden", "Kol" }, b1.Document.Pieces.Select(p => p.Name).ToArray());
        Assert.Equal(b1.Document.Pieces[0].Id, Assert.Single(b1.Selection.PieceIds));
        Assert.True(b1.View.UndoCount > 0);
        Assert.False(b1.View.DrawingInProgress);
        Assert.Empty(Build("B2").Selection.PieceIds);
        Assert.NotNull(Build("B3").Document.Pieces[0].Curves);
        var b4 = Build("B4");
        Assert.True(LayerTools.IsPieceLocked(b4.Document, b4.Document.Pieces[0]));
        Assert.True(Build("B5").View.DrawingInProgress);
        Assert.Equal(0, Build("B6").View.UndoCount);
        Assert.NotNull(Build("B7").Document.Pieces[0].Grainline);
        Assert.Equal(new[] { "Ön beden", "Ön beden 2" }, Build("B8").Document.Pieces.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void EveryBuiltInPhraseIsPlannedSoundlyInEveryContext() =>
        CheckPhrases(Phrases.Value, "built-in phrases");

    [Fact]
    public void TheSixtySpecificationSentencesGiveTheDocumentedOutcome()
    {
        var failures = new List<string>();
        foreach (var (number, phrase, contextName, expect) in Spec)
        {
            var result = Run(phrase, contextName);
            var actual = result.IsPlan ? $"{result.Plan!.Kind}/{result.Plan.Class}" : result.Code;
            if (actual != expect)
                failures.Add($"#{number} \"{phrase}\" ({contextName}): expected {expect}, got {actual}{(result.IsPlan ? "" : " - " + result.Refusal!.Message)}");
        }
        Assert.True(failures.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void UpperCaseAndDiacriticFreeWritingGivesTheSameOutcomeAsTheOriginalSentence()
    {
        var failures = new List<string>();
        foreach (var (number, phrase, contextName, _) in Spec)
        {
            var original = Run(phrase, contextName);
            foreach (var variant in new[] { TurkishText.ToUpper(phrase), TurkishText.Fold(phrase), "  " + phrase.Replace(" ", "   ") + "  ", phrase + "." })
            {
                var changed = Run(variant, contextName);
                var a = original.IsPlan ? original.Plan!.Preview : original.Code;
                var b = changed.IsPlan ? changed.Plan!.Preview : changed.Code;
                // A refusal echoes the typed text, so only the code is compared for refusals.
                if (original.IsPlan != changed.IsPlan || (!original.IsPlan && a != b) || (original.IsPlan && a != b && original.Plan!.Kind != changed.Plan!.Kind))
                    failures.Add($"#{number} \"{phrase}\" vs \"{Shorten(variant)}\" ({contextName}): {Describe(original)} / {Describe(changed)}");
            }
        }
        Assert.True(failures.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, failures));

        static string Describe(PlanResult r) => r.IsPlan ? r.Plan!.Kind + ": " + r.Plan.Preview : r.Code + ": " + r.Refusal!.Message;
    }

    [Fact]
    public void SpecificationSentencesCarryTheDocumentedNumbersIntoTheOperations()
    {

        void Move(string phrase, double dx, double dy)
        {
            var plan = PlanOf(phrase, "B1");
            var operation = Assert.IsType<MovePieceOperation>(plan.Operation);
            Assert.Equal(plan.TargetPieceId, operation.PieceId);
            Assert.Equal(dx, operation.DxMm, 9);
            Assert.Equal(dy, operation.DyMm, 9);
        }
        Move("25 sağa taşı", 25, 0);
        Move("25 sağa", 25, 0);
        Move("1 cm sola kaydır", -10, 0);
        Move("yarım santim yukarı", 0, -5);
        Move("bir buçuk santim aşağı", 0, 15);
        Move("1 santim 2 milim sağa", 12, 0);
        Move("20 sağa 10 yukarı", 20, -10);

        var to = Assert.IsType<MovePieceToOperation>(PlanOf("x 100 y 50 konumuna taşı", "B1").Operation);
        Assert.Equal(100, to.XMm, 9);
        Assert.Equal(50, to.YMm, 9);
        Assert.Equal(80, to.DxMm, 9);
        Assert.Equal(30, to.DyMm, 9);

        void Turn(string phrase, int degrees, string operationId)
        {
            var operation = Assert.IsType<RotatePieceOperation>(PlanOf(phrase, "B1").Operation);
            Assert.Equal(degrees, operation.ClockwiseDegrees);
            Assert.Equal(operationId, operation.OperationId);
        }
        Turn("döndür", 90, "rotate-cw");
        Turn("90 sağa döndür", 90, "rotate-cw");
        Turn("sola döndür", -90, "rotate-ccw");
        Turn("yarım tur", 180, "rotate-half");

        Assert.Equal("mirror-vertical", Assert.IsType<MirrorPieceOperation>(PlanOf("aynala", "B1").Operation).OperationId);
        Assert.Equal("mirror-horizontal", Assert.IsType<MirrorPieceOperation>(PlanOf("alt üst aynala", "B1").Operation).OperationId);

        void Size(string phrase, double width, double height)
        {
            var bounds = Geometry.Bounds(PlanOf(phrase, "B1").EditedDocument()!.Pieces[0].Points);
            Assert.Equal(width, bounds.Width, 9);
            Assert.Equal(height, bounds.Height, 9);
        }
        Size("genişliği 95 yap", 95, 620);
        Size("eni 9,5 cm yap", 95, 620);
        Size("95 x 60 yap", 95, 60);

        Assert.Equal("Ön Beden", PlanOf("adı Ön Beden olsun", "B1").EditedDocument()!.Pieces[0].Name);

        var rectangle = PlanOf("120 x 80 dikdörtgen", "B0").EditedDocument()!.Pieces.Single();
        var box = Geometry.Bounds(rectangle.Points);
        Assert.Equal(120, box.Width, 9);
        Assert.Equal(80, box.Height, 9);
        Assert.Equal(20, box.MinX, 9);
        Assert.Equal(20, box.MinY, 9);
        Assert.True(rectangle.Closed);
        Assert.Equal(1, PlanOf("100'lük kare", "B0").EditedDocument()!.Pieces.Count);
        var line = PlanOf("80 dikey çizgi", "B0").EditedDocument()!.Pieces.Single();
        Assert.False(line.Closed);
        Assert.Equal(0, Geometry.Bounds(line.Points).Width, 9);
        Assert.Equal(80, Geometry.Bounds(line.Points).Height, 9);
    }

    [Fact]
    public void KeyPreviewSentencesMatchTheSpecification()
    {
        var first = PlanOf("120 x 80 dikdörtgen", "B0").Preview;
        Assert.StartsWith("Dikdörtgen çizilecek: 120 × 80 mm (12 × 8 cm). Sol üst köşe:", first);
        Assert.EndsWith(UnitSentence, first);

        var withUnit = PlanOf("12 x 8 cm dikdörtgen", "B0").Preview;
        Assert.StartsWith("Dikdörtgen çizilecek: 120 × 80 mm (12 × 8 cm).", withUnit);
        Assert.DoesNotContain("Birim yazmadınız", withUnit);

        Assert.StartsWith("Kare çizilecek: 100 × 100 mm (10 × 10 cm).", PlanOf("kare 100", "B0").Preview);
        Assert.StartsWith("Çizgi çizilecek: 150 mm (15 cm), yatay.", PlanOf("150 çizgi", "B0").Preview);
        Assert.StartsWith("Çizgi çizilecek: 80 mm (8 cm), dikey.", PlanOf("80 dikey çizgi", "B0").Preview);

        var move = PlanOf("25 sağa taşı", "B1").Preview;
        Assert.Contains("‘Ön beden’", move);
        Assert.Contains("25 mm", move);
        Assert.Contains("2,5 cm", move);
        Assert.Contains("sağa", move);

        Assert.Equal("‘Ön beden’ silinecek (480 × 620 mm). Geri alabilirsiniz.", PlanOf("sil", "B1").Preview);
        Assert.Equal("‘Kol’ silinecek (200 × 300 mm). Geri alabilirsiniz.", PlanOf("kolu sil", "B1").Preview);
        Assert.Equal("‘Ön beden’ seçilecek.", PlanOf("ön bedeni seç", "B1").Preview);
        Assert.Equal("Seçim bırakılacak. Parçalar değişmez.", PlanOf("seçimi bırak", "B1").Preview);
        Assert.Equal("Son işlem geri alınacak.", PlanOf("geri al", "B1").Preview);
        Assert.Equal("Son 3 işlem geri alınacak.", PlanOf("üç adım geri", "B1").Preview);
        Assert.Equal("Geri alınan işlem yinelenecek.", PlanOf("yinele", "B1").Preview);
        Assert.Equal("Tüm parçalar ekrana sığdırılacak. Parçalar değişmez.", PlanOf("tümünü göster", "B1").Preview);
        Assert.Equal("Görünüm sola kaydırılacak. Parçalar değişmez.", PlanOf("görünümü sola kaydır", "B1").Preview);
        Assert.Equal("Fare aracı: Dikdörtgen. İki köşeye tıklayın.", PlanOf("dikdörtgen aracı", "B1").Preview);
        Assert.Equal("Çizim kapatılacak ve parça olacak (en az 3 nokta gerekir).", PlanOf("çizimi kapat", "B5").Preview);
        Assert.Equal("Proje kaydedilecek; dosya adı sorulacak.", PlanOf("kaydet", "B1").Preview);
        Assert.Equal("Başka bir programdan DXF dosyası alınacak. Dosyayı ve ölçü birimini siz seçersiniz; ölçüleri kontrol edene kadar projeniz değişmez.", PlanOf("dxf al", "B0").Preview);
        Assert.Equal("Beden oluşturmak için serileme kuralı gerekir. “Beden oluştur…” penceresi açılacak.", PlanOf("bir beden büyüt", "B1").Preview);
        Assert.Contains("10 mm (1 cm)", PlanOf("1 cm dikiş payı", "B1").Preview);
    }

    [Fact]
    public void MeasuredMarkerPreviewNamesTheValuesThatWillBePrefilled()
    {
        var preview=PlanOf("150 cm kumaşa iki Ön beden, bir Arka beden, parça arası boşluk 5 mm ve kumaş kenar payı 10 mm yerleştir","B1").Preview;
        Assert.Contains("Kumaş eni 1500 mm (150 cm)",preview);
        Assert.Contains("parça boşluğu 5 mm",preview);
        Assert.Contains("kenar payı 10 mm (1 cm)",preview);
        Assert.Contains("yerleşimi inceleyin",preview);
    }

    [Fact]
    public void RotationWithoutADirectionOffersTheOtherWayAndAHalfTurn()
    {
        var plan = PlanOf("döndür", "B1");
        Assert.Equal(new[] { "90 sola döndür", "180 derece" }, plan.Alternatives.Select(a => a.Label).ToArray());
    }

    [Fact]
    public void AmbiguousPieceNameListsBothPiecesAsButtons()
    {
        var result = Run("ön bedeni seç", "B8");
        Assert.False(result.IsPlan);
        Assert.Equal("R15b", result.Code);
        Assert.Equal(new[] { "Ön beden", "Ön beden 2" }, result.Refusal!.Buttons.Select(b => b.Label).ToArray());
    }

    [Fact]
    public void AnExactNameSelectsTheShorterPieceEvenWhenALongerNameContainsIt()
    {
        var plan = PlanOf("ön beden seç", "B8");
        Assert.Contains("‘Ön beden’", plan.Preview);
        Assert.DoesNotContain("‘Ön beden 2’", plan.Preview);
    }

    [Fact]
    public void ContextSpecificRefusalsFollowTheSpecification()
    {
        Assert.Equal("R03", Run("25 sağa taşı", "B0").Code);
        Assert.Contains("Henüz parça yok", Run("25 sağa taşı", "B0").Refusal!.Message);
        Assert.Equal("R03", Run("25 sağa taşı", "B2").Code);
        Assert.Equal("R08", Run("25 sağa taşı", "B4").Code);
        Assert.Equal("R11", Run("genişliği 95 yap", "B7").Code);
        Assert.Equal("R12", Run("25 sağa taşı", "B3").Code);
        Assert.Equal("R18", Run("geri al", "B6").Code);
        Assert.Equal("R07", Run("120 x 80 dikdörtgen", "B5").Code);
        Assert.Equal("R07", Run("geri al", "B5").Code);
        Assert.Equal("R07", Run("dikdörtgen aracı", "B5").Code);
        Assert.Equal("R03", Run("sil", "B2").Code);
        Assert.Equal("R03", Run("aynala", "B2").Code);
        Assert.Equal("R08", Run("sil", "B4").Code);
        Assert.Equal("R08", Run("ön bedeni sil", "B4").Code);
        Assert.True(Run("kolu sil", "B4").IsPlan);
        Assert.True(Run("ön bedeni seç", "B4").IsPlan);
    }

    [Fact]
    public void CancelPreviewAndHelpPhrasesNeverChangeTheDocumentOrNeedASelection()
    {
        foreach (var name in ContextNames)
        {
            foreach (var phrase in new[] { "vazgeç", "iptal", "yardım", "?" })
            {
                var result = Run(phrase, name);
                Assert.True(result.IsPlan, $"{phrase} in {name}: {result}");
                Assert.Equal(PlanClass.NoChange, result.Plan!.Class);
                Assert.False(result.Plan.AppliesInCore);
            }
        }
    }

    [Fact]
    public void WordsTheProductNeverSaysAreNotEchoedBackInRefusals()
    {
        foreach (var phrase in new[] { "yapay zeka ile çiz", "asistan sil", "model döndür", "YAPAY ZEKA", "Asistan", "MODEL" })
        {
            foreach (var name in ContextNames)
            {
                var result = Run(phrase, name);
                var text = result.IsPlan ? result.Plan!.Preview : result.Refusal!.Message;
                var folded = TurkishText.Fold(text);
                Assert.DoesNotContain("yapay zeka", folded);
                Assert.DoesNotContain("asistan", folded);
                Assert.DoesNotContain("model", folded);
            }
        }
    }

    [Fact]
    public void EveryRefusalButtonFillsTheBoxWithTextTheParserAtLeastRecognises()
    {
        // A button only fills the command box. If the parser answered R01 ("not understood") to a button's own text the user
        // would be stuck, so this walks every refusal reachable from the built-in phrases and parses each button's fill text.
        var failures = new List<string>();
        foreach (var name in ContextNames)
        {
            var ctx = Build(name);
            var parserContext = ctx.Parser;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var phrase in Phrases.Value)
            {
                var result = CommandPlanner.Plan(CommandParser.Parse(phrase, parserContext), ctx.View, ctx.Selection);
                if (result.IsPlan) continue;
                foreach (var button in result.Refusal!.Buttons)
                {
                    if (!seen.Add(result.Code + "|" + button.FillText)) continue;
                    var parsed = CommandParser.Parse(button.FillText, parserContext);
                    if (parsed.Code == "R01") failures.Add($"{name} / {result.Code}: the button \"{button.Label}\" fills \"{button.FillText}\", which the parser does not understand");
                }
            }
        }
        Assert.True(failures.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    // ================================================================================================================
    // The shared vector file
    // ================================================================================================================

    /// <summary>The linked copy next to the test assembly, else Apple/Tests/Fixtures/command-vectors.json found by walking up; null when absent.</summary>
    private static string? FindVectorFile()
    {
        var linked = Path.Combine(AppContext.BaseDirectory, "Fixtures", "command-vectors.json");
        if (File.Exists(linked)) return linked;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Apple", "Tests", "Fixtures", "command-vectors.json");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    [Fact]
    public void EveryInputOfTheSharedVectorFileIsPlannedSoundlyInEveryContext()
    {
        var path = FindVectorFile();
        if (path is null) return; // The file belongs to the parser work and may not be on this branch yet.

        using var json = JsonDocument.Parse(File.ReadAllBytes(path));
        Assert.True(json.RootElement.TryGetProperty("cases", out var cases) && cases.ValueKind == JsonValueKind.Array,
            $"{path} has no \"cases\" array");
        var inputs = new List<string>();
        foreach (var c in cases.EnumerateArray())
        {
            if (!c.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.String) continue;
            try
            {
                inputs.Add(input.GetString() ?? string.Empty);
            }
            catch (InvalidOperationException)
            {
                // An input with an unpaired surrogate escape cannot be read as a .NET string; the parser tests cover it.
            }
        }
        CheckPhrases(inputs, "vector file inputs");
    }
}
