import Foundation

// Port of Refusals.cs: every refusal and question text of the command box (R01-R28b), verbatim, and the builders the
// parser (and the planner) use. The words "yapay zeka", "asistan" and "model" never appear in any message.
// Messages are built here, never inline in the parser.

/// One catalogue entry: the exact Turkish template of a refusal; {...} marks the variable parts.
struct RefusalTemplate: Equatable {
    let key: String
    let code: String
    let text: String
    let needsExample: Bool
}

enum Refusals {
    static let notUnderstood = "R01"
    static let numberMissing = "R02"
    static let noSelection = "R03"
    static let outOfRange = "R04"
    static let ambiguousNumber = "R05"
    static let unitQuestion = "R06"
    static let drawingInProgress = "R07"
    static let lockedLayer = "R08"
    static let busyWithFile = "R09"
    static let notRectangle = "R10"
    static let extraData = "R11"
    static let curvedPiece = "R12"
    static let angle = "R13"
    static let multipleSelected = "R14"
    static let nameNotFound = "R15a"
    static let nameAmbiguous = "R15b"
    static let twoJobs = "R16"
    static let twoMeanings = "R16b"
    static let tooManyPoints = "R17"
    static let nothingToUndo = "R18"
    static let twoMeasuresNeeded = "R19"
    static let areaDrafts = "R20"
    static let recoveryRunning = "R21"
    static let previewStale = "R22"
    static let tooLong = "R23"
    static let emptyName = "R24"
    static let noChange = "R25"
    static let negativeNumber = "R26"
    static let namedPieceMove = "R27"
    static let executionFailed = "R28a"
    static let refreshFailed = "R28b"
    static let rectangleExample = "120 x 80 dikdörtgen"
    static let moveExample = "25 sağa taşı"
    static let rotateExample = "90 sağa döndür"
    static let undoExample = "geri al"
    static let templateNotUnderstood = "Bu yazı anlaşılmadı: “{yazı}”. Şöyle yazabilirsiniz: 120 x 80 dikdörtgen · 25 sağa taşı · 90 sağa döndür · geri al"
    static let templateNumberMissing = "Kaç milimetre? Örnek: {örnek}"
    static let templateNoSelection = "Önce bir parça seçin. Parçaya tıklayın ya da ön bedeni seç yazın."
    static let templateNoPieces = "Henüz parça yok. Önce çizin. Örnek: 120 x 80 dikdörtgen"
    static let templateOutOfRange = "Ölçü 1 ile 20000 mm arasında olmalı. Örnek: 120 x 80 dikdörtgen"
    static let templateAmbiguousNumber = "{sayı} iki türlü okunabilir. {a} mm için {a} yazın; {b} mm için {b} yazın."
    static let templateUnitQuestion = "{yazı}: milimetre mi, santimetre mi?"
    static let templateDrawingInProgress = "Önce çizimi bitirin (Enter) ya da Esc ile vazgeçin."
    static let templateLockedLayer = "Parça kilitli bir katmanda. Ayrıntılı görünümde katmanın kilidini açın."
    static let templateBusyWithFile = "Şu anda bir dosya işlemi sürüyor. Bitince yeniden deneyin."
    static let templateNotRectangle = "Bu parça dikdörtgen değil; ölçüsü yazıyla değiştirilemez. Yeni ölçüde dikdörtgen çizmek için yazın: 95 x 80 dikdörtgen"
    static let templateExtraData = "Bu parçada iplik yönü, köşe işareti, serileme kuralı veya eğri bilgisi var; ölçüsü yazıyla değiştirilemez. Tüm araçlar bölümünden düzenleyin."
    static let templateCurvedPiece = "Bu parça eğri ayarlarını koruyor; taşımadan önce Tüm araçlar bölümünde “Eğriyi sabit çizgiye çevir…” yapın."
    static let templateAngle = "Şimdilik yalnız 90, 180 ve 270 derece döndürülebilir. Örnek: 90 sağa döndür"
    static let templateMultipleSelected = "Birden fazla parça seçili. Tek parça seçin ya da “Birden fazla parçayı taşı…” kullanın."
    static let templateNameNotFound = "“{ad}” adında parça bulunamadı. Örnek: {gerçek ad} seç"
    static let templateNameAmbiguous = "{n} parça eşleşti. Hangisi?"
    static let templateTwoJobs = "Bu yazı iki ayrı iş gibi okunuyor. Tek iş yazın. Örnek: 120 x 80 dikdörtgen"
    static let templateTwoMeaningsFlip = "“ters çevir” aynalamak da döndürmek de olabilir. Örnek: aynala · 90 sağa döndür"
    static let templateTwoMeaningsGrow = "“büyüt” görünümü mü, parça ölçüsünü mü? Örnek: yakınlaştır · genişliği 95 yap"
    static let templateTooManyPoints = "Bu parça 4096 noktadan fazla. Bu işlem için Ayrıntılı görünümdeki düğmeyi kullanın."
    static let templateNothingToUndo = "Geri alınacak işlem yok. Önce bir şey yazıp uygulayın. Örnek: 120 x 80 dikdörtgen"
    static let templateNothingToRedo = "Yinelenecek işlem yok."
    static let templateTwoMeasuresNeeded = "Dikdörtgen için iki ölçü gerekir: en ve boy. Örnek: 120 x 80 dikdörtgen"
    static let templateAreaDrafts = "Önce alan taslaklarını uygulayın ya da temizleyin. Ayrıntılı görünümde “Alan taslaklarını temizle” düğmesi var."
    static let templateRecoveryRunning = "Kurtarma işlemi bitmeden değişiklik yapılamaz. Üstteki kurtarma uyarısındaki düğmeyi kullanın."
    static let templatePreviewStale = "Parça değişti; önizleme yenilendi. Enter ile uygulayın."
    static let templateTooLong = "Yazı çok uzun (en fazla 200 karakter). Kısaltın. Örnek: 120 x 80 dikdörtgen"
    static let templateEmptyName = "Yeni ad boş olamaz. Örnek: adı ön beden olsun"
    static let templateNoChange = "Ölçü zaten {ölçü}. Değişiklik yok."
    static let templateNegativeNumber = "Negatif ölçü yazılamaz. Yön için sağa, sola, yukarı veya aşağı yazın. Örnek: 25 sola taşı"
    static let templateNamedPieceMove = "Önce parçayı seçin: ön bedeni seç. Sonra: 25 sağa taşı"
    static let templateExecutionFailed = "İşlem tamamlanamadı: {neden}. Hiçbir şey değişmedi."
    static let templateRefreshFailed = "Ekran yenileniyor; işlem yapıldı. Komutu yeniden yazmayın."

    /// All templates in catalogue order. `needsExample` is false only where the specification lists no example or button.
    static let catalogue: [RefusalTemplate] = [
        RefusalTemplate(key: "R01", code: "R01", text: templateNotUnderstood, needsExample: true),
        RefusalTemplate(key: "R02", code: "R02", text: templateNumberMissing, needsExample: true),
        RefusalTemplate(key: "R03a", code: "R03", text: templateNoSelection, needsExample: true),
        RefusalTemplate(key: "R03b", code: "R03", text: templateNoPieces, needsExample: true),
        RefusalTemplate(key: "R04", code: "R04", text: templateOutOfRange, needsExample: true),
        RefusalTemplate(key: "R05", code: "R05", text: templateAmbiguousNumber, needsExample: true),
        RefusalTemplate(key: "R06", code: "R06", text: templateUnitQuestion, needsExample: true),
        RefusalTemplate(key: "R07", code: "R07", text: templateDrawingInProgress, needsExample: true),
        RefusalTemplate(key: "R08", code: "R08", text: templateLockedLayer, needsExample: true),
        RefusalTemplate(key: "R09", code: "R09", text: templateBusyWithFile, needsExample: false),
        RefusalTemplate(key: "R10", code: "R10", text: templateNotRectangle, needsExample: true),
        RefusalTemplate(key: "R11", code: "R11", text: templateExtraData, needsExample: true),
        RefusalTemplate(key: "R12", code: "R12", text: templateCurvedPiece, needsExample: true),
        RefusalTemplate(key: "R13", code: "R13", text: templateAngle, needsExample: true),
        RefusalTemplate(key: "R14", code: "R14", text: templateMultipleSelected, needsExample: true),
        RefusalTemplate(key: "R15a", code: "R15a", text: templateNameNotFound, needsExample: true),
        RefusalTemplate(key: "R15b", code: "R15b", text: templateNameAmbiguous, needsExample: true),
        RefusalTemplate(key: "R16", code: "R16", text: templateTwoJobs, needsExample: true),
        RefusalTemplate(key: "R16b-1", code: "R16b", text: templateTwoMeaningsFlip, needsExample: true),
        RefusalTemplate(key: "R16b-2", code: "R16b", text: templateTwoMeaningsGrow, needsExample: true),
        RefusalTemplate(key: "R17", code: "R17", text: templateTooManyPoints, needsExample: true),
        RefusalTemplate(key: "R18a", code: "R18", text: templateNothingToUndo, needsExample: true),
        RefusalTemplate(key: "R18b", code: "R18", text: templateNothingToRedo, needsExample: false),
        RefusalTemplate(key: "R19", code: "R19", text: templateTwoMeasuresNeeded, needsExample: true),
        RefusalTemplate(key: "R20", code: "R20", text: templateAreaDrafts, needsExample: true),
        RefusalTemplate(key: "R21", code: "R21", text: templateRecoveryRunning, needsExample: false),
        RefusalTemplate(key: "R22", code: "R22", text: templatePreviewStale, needsExample: false),
        RefusalTemplate(key: "R23", code: "R23", text: templateTooLong, needsExample: true),
        RefusalTemplate(key: "R24", code: "R24", text: templateEmptyName, needsExample: true),
        RefusalTemplate(key: "R25", code: "R25", text: templateNoChange, needsExample: false),
        RefusalTemplate(key: "R26", code: "R26", text: templateNegativeNumber, needsExample: true),
        RefusalTemplate(key: "R27", code: "R27", text: templateNamedPieceMove, needsExample: true),
        RefusalTemplate(key: "R28a", code: "R28a", text: templateExecutionFailed, needsExample: false),
        RefusalTemplate(key: "R28b", code: "R28b", text: templateRefreshFailed, needsExample: true),
    ]

    private static func buttons(_ labels: String...) -> [RefusalButton] {
        labels.map { RefusalButton($0) }
    }

    // ---- Refusals the parser produces -------------------------------------------------------------------------

    /// R01. The typed text is echoed unless it contains a word the product never says (then it is shown as “…”).
    static func unrecognized(_ typed: String, didYouMean: RefusalButton? = nil) -> CommandRefusal {
        let shown = TurkishText.containsForbiddenWord(typed) ? "…" : typed
        var list: [RefusalButton] = []
        if let didYouMean { list.append(didYouMean) }
        list.append(RefusalButton(rectangleExample))
        list.append(RefusalButton(moveExample))
        list.append(RefusalButton(rotateExample))
        return CommandRefusal(code: notUnderstood,
                              message: TurkishText.replaceOrdinal(templateNotUnderstood, "{yazı}", shown),
                              buttons: list)
    }

    /// R02. `example` is a complete working command such as "25 sağa taşı" or "150 çizgi".
    static func numberMissingFor(_ example: String) -> CommandRefusal {
        CommandRefusal(code: numberMissing,
                       message: TurkishText.replaceOrdinal(templateNumberMissing, "{örnek}", example),
                       buttons: buttons(example))
    }

    /// R03 when pieces exist but none is selected; the buttons select a piece by name.
    static func noPieceSelected(_ pieceNames: [String]? = nil) -> CommandRefusal {
        let list = (pieceNames ?? []).prefix(3).map { RefusalButton($0, fill: $0 + " seç") }
        return CommandRefusal(code: noSelection, message: templateNoSelection, buttons: list)
    }

    /// R03 when the project has no piece at all.
    static func noPiecesYet() -> CommandRefusal {
        CommandRefusal(code: noSelection, message: templateNoPieces, buttons: buttons(rectangleExample))
    }

    static func outOfRangeMeasure() -> CommandRefusal {
        CommandRefusal(code: outOfRange, message: templateOutOfRange, buttons: buttons(rectangleExample))
    }

    /// R05. `numberText` is the number as typed ("1.500"); the buttons replace it inside `typed`.
    /// `numberStart` and `numberLength` are UTF-16 offsets into `typed`.
    static func ambiguousNumberText(_ numberText: String, _ readAsDecimal: String, _ readAsWhole: String,
                                    typed: String, numberStart: Int, numberLength: Int) -> CommandRefusal {
        let typedUnits = Array(typed.utf16)
        func replaced(_ with: String) -> String {
            if numberStart >= 0 && numberLength >= 0 && numberStart + numberLength <= typedUnits.count {
                return TurkishText.string(Array(typedUnits[0..<numberStart]))
                    + with
                    + TurkishText.string(Array(typedUnits[(numberStart + numberLength)...]))
            }
            return with
        }
        var message = TurkishText.replaceOrdinal(templateAmbiguousNumber, "{sayı}", numberText)
        message = TurkishText.replaceOrdinal(message, "{a}", readAsDecimal)
        message = TurkishText.replaceOrdinal(message, "{b}", readAsWhole)
        return CommandRefusal(code: ambiguousNumber, message: message,
                              buttons: [RefusalButton(replaced(readAsDecimal)), RefusalButton(replaced(readAsWhole))])
    }

    /// R06, the unit question. `typed` is the text as typed; the two buttons put mm or cm after the last number.
    static func unitQuestionFor(_ typed: String, _ withMillimeters: String, _ withCentimeters: String) -> CommandRefusal {
        CommandRefusal(code: unitQuestion,
                       message: TurkishText.replaceOrdinal(templateUnitQuestion, "{yazı}", typed),
                       buttons: buttons(withMillimeters, withCentimeters))
    }

    static func angleNotSupported() -> CommandRefusal {
        CommandRefusal(code: angle, message: templateAngle, buttons: buttons(rotateExample))
    }

    /// R15a. With no real names the buttons are empty (the parser reports R03 for a project without pieces instead).
    static func nameNotFoundFor(_ typedName: String, _ realNames: [String], _ verb: String) -> CommandRefusal {
        let first = realNames.first ?? "ön beden"
        let shown = TurkishText.containsForbiddenWord(typedName) ? "…" : typedName
        var message = TurkishText.replaceOrdinal(templateNameNotFound, "{ad}", shown)
        message = TurkishText.replaceOrdinal(message, "{gerçek ad}", first)
        let list = realNames.prefix(3).map { RefusalButton($0, fill: $0 + " " + verb) }
        return CommandRefusal(code: nameNotFound, message: message, buttons: list)
    }

    /// R15b. Buttons are the matching names (at most three); each fills "name verb", which resolves because an exact
    /// name match wins.
    static func nameAmbiguousFor(_ matchingNames: [String], _ verb: String) -> CommandRefusal {
        let message = TurkishText.replaceOrdinal(templateNameAmbiguous, "{n}", String(matchingNames.count))
        let list = matchingNames.prefix(3).map { RefusalButton($0, fill: $0 + " " + verb) }
        return CommandRefusal(code: nameAmbiguous, message: message, buttons: list)
    }

    static func twoJobsRefusal() -> CommandRefusal {
        CommandRefusal(code: twoJobs, message: templateTwoJobs, buttons: buttons(rectangleExample))
    }

    /// R16b for "ters çevir" (flip or rotate).
    static func flipOrRotate() -> CommandRefusal {
        CommandRefusal(code: twoMeanings, message: templateTwoMeaningsFlip, buttons: buttons("aynala", rotateExample))
    }

    /// R16b for "büyüt" (zoom or resize).
    static func zoomOrResize() -> CommandRefusal {
        CommandRefusal(code: twoMeanings, message: templateTwoMeaningsGrow, buttons: buttons("yakınlaştır", "genişliği 95 yap"))
    }

    static func twoMeasuresRequired() -> CommandRefusal {
        CommandRefusal(code: twoMeasuresNeeded, message: templateTwoMeasuresNeeded, buttons: buttons(rectangleExample))
    }

    static func textTooLong() -> CommandRefusal {
        CommandRefusal(code: tooLong, message: templateTooLong, buttons: buttons(rectangleExample))
    }

    static func emptyNameRefusal() -> CommandRefusal {
        CommandRefusal(code: emptyName, message: templateEmptyName, buttons: buttons("adı ön beden olsun"))
    }

    static func negativeNumberRefusal() -> CommandRefusal {
        CommandRefusal(code: negativeNumber, message: templateNegativeNumber, buttons: buttons("25 sola taşı"))
    }

    static func namedPieceCannotMove() -> CommandRefusal {
        CommandRefusal(code: namedPieceMove, message: templateNamedPieceMove, buttons: buttons("ön bedeni seç"))
    }

    // ---- Refusals the planner and executor produce (kept here so every text lives in one place) ----------------------

    static func drawingPending() -> CommandRefusal {
        CommandRefusal(code: drawingInProgress, message: templateDrawingInProgress, buttons: buttons("çizimi kapat", "çizimi iptal et"))
    }

    static func locked() -> CommandRefusal {
        CommandRefusal(code: lockedLayer, message: templateLockedLayer, buttons: buttons("Ayrıntılı görünüm"))
    }

    static func busy() -> CommandRefusal {
        CommandRefusal(code: busyWithFile, message: templateBusyWithFile, buttons: [])
    }

    static func notARectangle() -> CommandRefusal {
        CommandRefusal(code: notRectangle, message: templateNotRectangle, buttons: buttons("95 x 80 dikdörtgen"))
    }

    static func hasExtraData() -> CommandRefusal {
        CommandRefusal(code: extraData, message: templateExtraData, buttons: buttons("Tüm araçlar"))
    }

    static func hasCurves() -> CommandRefusal {
        CommandRefusal(code: curvedPiece, message: templateCurvedPiece, buttons: buttons("Tüm araçlar"))
    }

    static func severalSelected() -> CommandRefusal {
        CommandRefusal(code: multipleSelected, message: templateMultipleSelected, buttons: buttons("seçimi bırak"))
    }

    static func tooManyPointsRefusal() -> CommandRefusal {
        CommandRefusal(code: tooManyPoints, message: templateTooManyPoints, buttons: buttons("Ayrıntılı görünüm"))
    }

    static func nothingToUndoRefusal() -> CommandRefusal {
        CommandRefusal(code: nothingToUndo, message: templateNothingToUndo, buttons: buttons(rectangleExample))
    }

    static func nothingToRedoRefusal() -> CommandRefusal {
        CommandRefusal(code: nothingToUndo, message: templateNothingToRedo, buttons: [])
    }

    static func areaDraftsPending() -> CommandRefusal {
        CommandRefusal(code: areaDrafts, message: templateAreaDrafts, buttons: buttons("Ayrıntılı görünüm"))
    }

    static func recoveryInProgress() -> CommandRefusal {
        CommandRefusal(code: recoveryRunning, message: templateRecoveryRunning, buttons: [])
    }

    static func previewRefreshed() -> CommandRefusal {
        CommandRefusal(code: previewStale, message: templatePreviewStale, buttons: [])
    }

    static func nothingChanges(_ measureText: String) -> CommandRefusal {
        CommandRefusal(code: noChange,
                       message: TurkishText.replaceOrdinal(templateNoChange, "{ölçü}", measureText),
                       buttons: [])
    }

    static func failed(_ reason: String) -> CommandRefusal {
        CommandRefusal(code: executionFailed,
                       message: TurkishText.replaceOrdinal(templateExecutionFailed, "{neden}", reason),
                       buttons: [])
    }

    static func refreshFailedAfterApply() -> CommandRefusal {
        CommandRefusal(code: refreshFailed, message: templateRefreshFailed, buttons: buttons("Geri al"))
    }
}
