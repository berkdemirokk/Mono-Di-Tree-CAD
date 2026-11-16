namespace TekstilCAD.Core.Commands;

/// <summary>One catalogue entry: the exact Turkish template of a refusal; {…} marks the variable parts.</summary>
public sealed record RefusalTemplate(string Key, string Code, string Text, bool NeedsExample);

/// <summary>
/// Every refusal and question text of the command box (FINAL_COMMANDS.md section 3, R01-R28b), verbatim.
/// Each message says what happened, what to do and gives an example. The words "yapay zeka", "asistan" and
/// "model" never appear in any message. Messages are built here, never inline in the parser.
/// </summary>
public static class Refusals
{
    public const string NotUnderstood = "R01";
    public const string NumberMissing = "R02";
    public const string NoSelection = "R03";
    public const string OutOfRange = "R04";
    public const string AmbiguousNumber = "R05";
    public const string UnitQuestion = "R06";
    public const string DrawingInProgress = "R07";
    public const string LockedLayer = "R08";
    public const string BusyWithFile = "R09";
    public const string NotRectangle = "R10";
    public const string ExtraData = "R11";
    public const string CurvedPiece = "R12";
    public const string Angle = "R13";
    public const string MultipleSelected = "R14";
    public const string NameNotFound = "R15a";
    public const string NameAmbiguous = "R15b";
    public const string TwoJobs = "R16";
    public const string TwoMeanings = "R16b";
    public const string TooManyPoints = "R17";
    public const string NothingToUndo = "R18";
    public const string TwoMeasuresNeeded = "R19";
    public const string AreaDrafts = "R20";
    public const string RecoveryRunning = "R21";
    public const string PreviewStale = "R22";
    public const string TooLong = "R23";
    public const string EmptyName = "R24";
    public const string NoChange = "R25";
    public const string NegativeNumber = "R26";
    public const string NamedPieceMove = "R27";
    public const string ExecutionFailed = "R28a";
    public const string RefreshFailed = "R28b";

    /// <summary>The first example of each refusal that offers examples.</summary>
    public const string RectangleExample = "120 x 80 dikdörtgen";
    public const string MoveExample = "25 sağa taşı";
    public const string RotateExample = "90 sağa döndür";
    public const string UndoExample = "geri al";

    public const string TemplateNotUnderstood = "Bu yazı anlaşılmadı: “{yazı}”. Şöyle yazabilirsiniz: 120 x 80 dikdörtgen · 25 sağa taşı · 90 sağa döndür · geri al";
    public const string TemplateNumberMissing = "Kaç milimetre? Örnek: {örnek}";
    public const string TemplateNoSelection = "Önce bir parça seçin. Parçaya tıklayın ya da ön bedeni seç yazın.";
    public const string TemplateNoPieces = "Henüz parça yok. Önce çizin. Örnek: 120 x 80 dikdörtgen";
    public const string TemplateOutOfRange = "Ölçü 1 ile 20000 mm arasında olmalı. Örnek: 120 x 80 dikdörtgen";
    public const string TemplateAmbiguousNumber = "{sayı} iki türlü okunabilir. {a} mm için {a} yazın; {b} mm için {b} yazın.";
    public const string TemplateUnitQuestion = "{yazı}: milimetre mi, santimetre mi?";
    public const string TemplateDrawingInProgress = "Önce çizimi bitirin (Enter) ya da Esc ile vazgeçin.";
    public const string TemplateLockedLayer = "Parça kilitli bir katmanda. Ayrıntılı görünümde katmanın kilidini açın.";
    public const string TemplateBusyWithFile = "Şu anda bir dosya işlemi sürüyor. Bitince yeniden deneyin.";
    public const string TemplateNotRectangle = "Bu parça dikdörtgen değil; ölçüsü yazıyla değiştirilemez. Yeni ölçüde dikdörtgen çizmek için yazın: 95 x 80 dikdörtgen";
    public const string TemplateExtraData = "Bu parçada iplik yönü, köşe işareti, serileme kuralı veya eğri bilgisi var; ölçüsü yazıyla değiştirilemez. Tüm araçlar bölümünden düzenleyin.";
    public const string TemplateCurvedPiece = "Bu parça eğri ayarlarını koruyor; taşımadan önce Tüm araçlar bölümünde “Eğriyi sabit çizgiye çevir…” yapın.";
    public const string TemplateAngle = "Şimdilik yalnız 90, 180 ve 270 derece döndürülebilir. Örnek: 90 sağa döndür";
    public const string TemplateMultipleSelected = "Birden fazla parça seçili. Tek parça seçin ya da “Birden fazla parçayı taşı…” kullanın.";
    public const string TemplateNameNotFound = "“{ad}” adında parça bulunamadı. Örnek: {gerçek ad} seç";
    public const string TemplateNameAmbiguous = "{n} parça eşleşti. Hangisi?";
    public const string TemplateTwoJobs = "Bu yazı iki ayrı iş gibi okunuyor. Tek iş yazın. Örnek: 120 x 80 dikdörtgen";
    public const string TemplateTwoMeaningsFlip = "“ters çevir” aynalamak da döndürmek de olabilir. Örnek: aynala · 90 sağa döndür";
    public const string TemplateTwoMeaningsGrow = "“büyüt” görünümü mü, parça ölçüsünü mü? Örnek: yakınlaştır · genişliği 95 yap";
    public const string TemplateTooManyPoints = "Bu parça 4096 noktadan fazla. Bu işlem için Ayrıntılı görünümdeki düğmeyi kullanın.";
    public const string TemplateNothingToUndo = "Geri alınacak işlem yok. Önce bir şey yazıp uygulayın. Örnek: 120 x 80 dikdörtgen";
    public const string TemplateNothingToRedo = "Yinelenecek işlem yok.";
    public const string TemplateTwoMeasuresNeeded = "Dikdörtgen için iki ölçü gerekir: en ve boy. Örnek: 120 x 80 dikdörtgen";
    public const string TemplateAreaDrafts = "Önce alan taslaklarını uygulayın ya da temizleyin. Ayrıntılı görünümde “Alan taslaklarını temizle” düğmesi var.";
    public const string TemplateRecoveryRunning = "Kurtarma işlemi bitmeden değişiklik yapılamaz. Üstteki kurtarma uyarısındaki düğmeyi kullanın.";
    public const string TemplatePreviewStale = "Parça değişti; önizleme yenilendi. Enter ile uygulayın.";
    public const string TemplateTooLong = "Yazı çok uzun (en fazla 200 karakter). Kısaltın. Örnek: 120 x 80 dikdörtgen";
    public const string TemplateEmptyName = "Yeni ad boş olamaz. Örnek: adı ön beden olsun";
    public const string TemplateNoChange = "Ölçü zaten {ölçü}. Değişiklik yok.";
    public const string TemplateNegativeNumber = "Negatif ölçü yazılamaz. Yön için sağa, sola, yukarı veya aşağı yazın. Örnek: 25 sola taşı";
    public const string TemplateNamedPieceMove = "Önce parçayı seçin: ön bedeni seç. Sonra: 25 sağa taşı";
    public const string TemplateExecutionFailed = "İşlem tamamlanamadı: {neden}. Hiçbir şey değişmedi.";
    public const string TemplateRefreshFailed = "Ekran yenileniyor; işlem yapıldı. Komutu yeniden yazmayın.";

    /// <summary>All templates in catalogue order. <c>NeedsExample</c> is false only where the specification lists no example or button.</summary>
    public static IReadOnlyList<RefusalTemplate> Catalogue { get; } = new[]
    {
        new RefusalTemplate("R01", "R01", TemplateNotUnderstood, true),
        new RefusalTemplate("R02", "R02", TemplateNumberMissing, true),
        new RefusalTemplate("R03a", "R03", TemplateNoSelection, true),
        new RefusalTemplate("R03b", "R03", TemplateNoPieces, true),
        new RefusalTemplate("R04", "R04", TemplateOutOfRange, true),
        new RefusalTemplate("R05", "R05", TemplateAmbiguousNumber, true),
        new RefusalTemplate("R06", "R06", TemplateUnitQuestion, true),
        new RefusalTemplate("R07", "R07", TemplateDrawingInProgress, true),
        new RefusalTemplate("R08", "R08", TemplateLockedLayer, true),
        new RefusalTemplate("R09", "R09", TemplateBusyWithFile, false),
        new RefusalTemplate("R10", "R10", TemplateNotRectangle, true),
        new RefusalTemplate("R11", "R11", TemplateExtraData, true),
        new RefusalTemplate("R12", "R12", TemplateCurvedPiece, true),
        new RefusalTemplate("R13", "R13", TemplateAngle, true),
        new RefusalTemplate("R14", "R14", TemplateMultipleSelected, true),
        new RefusalTemplate("R15a", "R15a", TemplateNameNotFound, true),
        new RefusalTemplate("R15b", "R15b", TemplateNameAmbiguous, true),
        new RefusalTemplate("R16", "R16", TemplateTwoJobs, true),
        new RefusalTemplate("R16b-1", "R16b", TemplateTwoMeaningsFlip, true),
        new RefusalTemplate("R16b-2", "R16b", TemplateTwoMeaningsGrow, true),
        new RefusalTemplate("R17", "R17", TemplateTooManyPoints, true),
        new RefusalTemplate("R18a", "R18", TemplateNothingToUndo, true),
        new RefusalTemplate("R18b", "R18", TemplateNothingToRedo, false),
        new RefusalTemplate("R19", "R19", TemplateTwoMeasuresNeeded, true),
        new RefusalTemplate("R20", "R20", TemplateAreaDrafts, true),
        new RefusalTemplate("R21", "R21", TemplateRecoveryRunning, false),
        new RefusalTemplate("R22", "R22", TemplatePreviewStale, false),
        new RefusalTemplate("R23", "R23", TemplateTooLong, true),
        new RefusalTemplate("R24", "R24", TemplateEmptyName, true),
        new RefusalTemplate("R25", "R25", TemplateNoChange, false),
        new RefusalTemplate("R26", "R26", TemplateNegativeNumber, true),
        new RefusalTemplate("R27", "R27", TemplateNamedPieceMove, true),
        new RefusalTemplate("R28a", "R28a", TemplateExecutionFailed, false),
        new RefusalTemplate("R28b", "R28b", TemplateRefreshFailed, true),
    };

    private static readonly IReadOnlyList<RefusalButton> NoButtons = Array.Empty<RefusalButton>();

    private static IReadOnlyList<RefusalButton> Buttons(params string[] labels) =>
        labels.Select(l => new RefusalButton(l)).ToArray();

    // ---- Refusals the parser produces -------------------------------------------------------------------------

    /// <summary>R01. The typed text is echoed unless it contains a word the product never says (then it is shown as “…”).</summary>
    public static Refusal Unrecognized(string typed, RefusalButton? didYouMean = null)
    {
        var shown = TurkishText.ContainsForbiddenWord(typed) ? "…" : typed;
        var buttons = new List<RefusalButton>();
        if (didYouMean is not null) buttons.Add(didYouMean);
        buttons.Add(new RefusalButton(RectangleExample));
        buttons.Add(new RefusalButton(MoveExample));
        buttons.Add(new RefusalButton(RotateExample));
        return new Refusal(NotUnderstood, TemplateNotUnderstood.Replace("{yazı}", shown, StringComparison.Ordinal), buttons);
    }

    /// <summary>R02. <paramref name="example"/> is a complete working command such as "25 sağa taşı" or "150 çizgi".</summary>
    public static Refusal NumberMissingFor(string example) =>
        new(NumberMissing, TemplateNumberMissing.Replace("{örnek}", example, StringComparison.Ordinal), Buttons(example));

    /// <summary>R03 when pieces exist but none is selected; the buttons select a piece by name.</summary>
    public static Refusal NoPieceSelected(IEnumerable<string>? pieceNames = null)
    {
        var buttons = (pieceNames ?? Array.Empty<string>()).Take(3)
            .Select(n => new RefusalButton(n, n + " seç")).ToArray();
        return new Refusal(NoSelection, TemplateNoSelection, buttons);
    }

    /// <summary>R03 when the project has no piece at all.</summary>
    public static Refusal NoPiecesYet() =>
        new(NoSelection, TemplateNoPieces, Buttons(RectangleExample));

    public static Refusal OutOfRangeMeasure() =>
        new(Refusals.OutOfRange, TemplateOutOfRange, Buttons(RectangleExample));

    /// <summary>R05. <paramref name="numberText"/> is the number as typed ("1.500"); the buttons replace it inside <paramref name="typed"/>.</summary>
    public static Refusal AmbiguousNumberText(string numberText, string readAsDecimal, string readAsWhole, string typed, int numberStart, int numberLength)
    {
        string Replace(string with) => numberStart >= 0 && numberStart + numberLength <= typed.Length
            ? typed.Substring(0, numberStart) + with + typed.Substring(numberStart + numberLength)
            : with;
        var message = TemplateAmbiguousNumber
            .Replace("{sayı}", numberText, StringComparison.Ordinal)
            .Replace("{a}", readAsDecimal, StringComparison.Ordinal)
            .Replace("{b}", readAsWhole, StringComparison.Ordinal);
        return new Refusal(AmbiguousNumber, message,
            new[] { new RefusalButton(Replace(readAsDecimal)), new RefusalButton(Replace(readAsWhole)) });
    }

    /// <summary>R06, the unit question. <paramref name="typed"/> is the text as typed; the two buttons put mm or cm after the last number.</summary>
    public static Refusal UnitQuestionFor(string typed, string withMillimeters, string withCentimeters) =>
        new(UnitQuestion, TemplateUnitQuestion.Replace("{yazı}", typed, StringComparison.Ordinal),
            Buttons(withMillimeters, withCentimeters));

    public static Refusal AngleNotSupported() => new(Angle, TemplateAngle, Buttons(RotateExample));

    /// <summary>R15a. With no real names the buttons are empty (the parser reports R03 for a project without pieces instead).</summary>
    public static Refusal NameNotFoundFor(string typedName, IReadOnlyList<string> realNames, string verb)
    {
        var first = realNames.Count > 0 ? realNames[0] : "ön beden";
        var shown = TurkishText.ContainsForbiddenWord(typedName) ? "…" : typedName;
        var message = TemplateNameNotFound
            .Replace("{ad}", shown, StringComparison.Ordinal)
            .Replace("{gerçek ad}", first, StringComparison.Ordinal);
        var buttons = realNames.Take(3).Select(n => new RefusalButton(n, n + " " + verb)).ToArray();
        return new Refusal(NameNotFound, message, buttons);
    }

    /// <summary>R15b. Buttons are the matching names (at most three); each fills "name verb", which resolves because an exact name match wins.</summary>
    public static Refusal NameAmbiguousFor(IReadOnlyList<string> matchingNames, string verb)
    {
        var message = TemplateNameAmbiguous.Replace("{n}", matchingNames.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var buttons = matchingNames.Take(3).Select(n => new RefusalButton(n, n + " " + verb)).ToArray();
        return new Refusal(NameAmbiguous, message, buttons);
    }

    public static Refusal TwoJobsRefusal() => new(TwoJobs, TemplateTwoJobs, Buttons(RectangleExample));

    /// <summary>R16b for "ters çevir" (flip or rotate).</summary>
    public static Refusal FlipOrRotate() => new(TwoMeanings, TemplateTwoMeaningsFlip, Buttons("aynala", RotateExample));

    /// <summary>R16b for "büyüt" (zoom or resize).</summary>
    public static Refusal ZoomOrResize() => new(TwoMeanings, TemplateTwoMeaningsGrow, Buttons("yakınlaştır", "genişliği 95 yap"));

    public static Refusal TwoMeasuresRequired() => new(TwoMeasuresNeeded, TemplateTwoMeasuresNeeded, Buttons(RectangleExample));

    public static Refusal TextTooLong() => new(TooLong, TemplateTooLong, Buttons(RectangleExample));

    public static Refusal EmptyNameRefusal() => new(EmptyName, TemplateEmptyName, Buttons("adı ön beden olsun"));

    public static Refusal NegativeNumberRefusal() => new(NegativeNumber, TemplateNegativeNumber, Buttons("25 sola taşı"));

    public static Refusal NamedPieceCannotMove() => new(NamedPieceMove, TemplateNamedPieceMove, Buttons("ön bedeni seç"));

    // ---- Refusals the planner and executor produce (kept here so every text lives in one place) --------------------

    public static Refusal DrawingPending() => new(DrawingInProgress, TemplateDrawingInProgress, Buttons("çizimi kapat", "çizimi iptal et"));

    public static Refusal Locked() => new(LockedLayer, TemplateLockedLayer, Buttons("Ayrıntılı görünüm"));

    public static Refusal Busy() => new(BusyWithFile, TemplateBusyWithFile, NoButtons);

    public static Refusal NotARectangle() => new(NotRectangle, TemplateNotRectangle, Buttons("95 x 80 dikdörtgen"));

    public static Refusal HasExtraData() => new(ExtraData, TemplateExtraData, Buttons("Tüm araçlar"));

    public static Refusal HasCurves() => new(CurvedPiece, TemplateCurvedPiece, Buttons("Tüm araçlar"));

    public static Refusal SeveralSelected() => new(MultipleSelected, TemplateMultipleSelected, Buttons("seçimi bırak"));

    public static Refusal TooManyPointsRefusal() => new(TooManyPoints, TemplateTooManyPoints, Buttons("Ayrıntılı görünüm"));

    public static Refusal NothingToUndoRefusal() => new(NothingToUndo, TemplateNothingToUndo, Buttons(RectangleExample));

    public static Refusal NothingToRedoRefusal() => new(NothingToUndo, TemplateNothingToRedo, NoButtons);

    public static Refusal AreaDraftsPending() => new(AreaDrafts, TemplateAreaDrafts, Buttons("Ayrıntılı görünüm"));

    public static Refusal RecoveryInProgress() => new(RecoveryRunning, TemplateRecoveryRunning, NoButtons);

    public static Refusal PreviewRefreshed() => new(PreviewStale, TemplatePreviewStale, NoButtons);

    public static Refusal NothingChanges(string measureText) =>
        new(NoChange, TemplateNoChange.Replace("{ölçü}", measureText, StringComparison.Ordinal), NoButtons);

    public static Refusal Failed(string reason) =>
        new(ExecutionFailed, TemplateExecutionFailed.Replace("{neden}", reason, StringComparison.Ordinal), NoButtons);

    public static Refusal RefreshFailedAfterApply() => new(RefreshFailed, TemplateRefreshFailed, Buttons("Geri al"));
}
