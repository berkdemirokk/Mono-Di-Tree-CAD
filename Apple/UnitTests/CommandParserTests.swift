import XCTest
@testable import TekstilCADViewer

/// Table-driven tests of the Turkish command parser. The sentences follow the 60 test phrases of
/// tasarim/FINAL_COMMANDS.md section 5, so the iPhone parser stays in step with the .NET one.
/// Distances are millimetres; "down" is the screen direction (up is negative), as in the shared
/// vectors.
final class CommandParserTests: XCTestCase {
    private let names = ["Ön beden", "Arka beden", "Kol"]

    private func outcome(_ text: String, _ pieces: [String]? = nil) -> CommandOutcome {
        CommandParser.parse(text, pieceNames: pieces ?? names)
    }

    private func command(_ text: String, _ pieces: [String]? = nil,
                         file: StaticString = #filePath, line: UInt = #line) -> ParsedCommand? {
        let result = outcome(text, pieces)
        if case .command(let parsed) = result { return parsed }
        XCTFail("“\(text)” should be a command but was \(result)", file: file, line: line)
        return nil
    }

    private func refusal(_ text: String, _ pieces: [String]? = nil) -> CommandRefusal? {
        switch outcome(text, pieces) {
        case .ask(let r), .refuse(let r): return r
        case .command: return nil
        }
    }

    private func assertCommands(_ table: [(String, ParsedCommand)], _ pieces: [String]? = nil,
                                file: StaticString = #filePath, line: UInt = #line) {
        for (text, expected) in table {
            XCTAssertEqual(command(text, pieces, file: file, line: line), expected, "“\(text)”", file: file, line: line)
        }
    }

    private func assertRefused(_ table: [(String, String)], _ pieces: [String]? = nil,
                               file: StaticString = #filePath, line: UInt = #line) {
        for (text, code) in table {
            let r = refusal(text, pieces)
            XCTAssertEqual(r?.code, code, "“\(text)” → \(String(describing: r))", file: file, line: line)
        }
    }

    // MARK: Drawing

    func testRectangleForms() {
        let rect = ParsedCommand.addRectangle(width: 120, height: 80)
        assertCommands([
            ("120 x 80 dikdörtgen", rect),
            ("120x80 dikdörtgen", rect),
            ("dikdörtgen 120 80", rect),
            ("12 x 8 cm dikdörtgen", rect),
            ("yüz yirmi çarpı seksen dikdörtgen", rect),
            ("DİKDÖRTGEN 120 X 80", rect),
            ("dikdortgen 120x80", rect),
            ("120 x 80 dikdörtgen çiz", rect),
            ("  lütfen 120 x 80 dikdörtgen çiz.  ", rect),
            ("120 × 80 dikdörtgen", rect),
            ("120 * 80 dikdörtgen", rect),
            ("12 cm x 80 mm dikdörtgen", rect),
            ("100'e 150 dikdörtgen", .addRectangle(width: 100, height: 150)),
            ("100’e 150 dikdörtgen", .addRectangle(width: 100, height: 150)),
            ("100 çarpı 150 kutu", .addRectangle(width: 100, height: 150)),
            ("eni 100 boyu 150 dikdörtgen", .addRectangle(width: 100, height: 150)),
            ("yüze 150 dikdörtgen", .addRectangle(width: 100, height: 150)),
            ("12,5 x 80 dikdörtgen", .addRectangle(width: 12.5, height: 80)),
            ("120,5 x 80 dikdörtgen", .addRectangle(width: 120.5, height: 80))
        ])
    }

    func testSquareAndLineForms() {
        assertCommands([
            ("kare 100", .addRectangle(width: 100, height: 100)),
            ("100'lük kare", .addRectangle(width: 100, height: 100)),
            ("100 x 100 kare", .addRectangle(width: 100, height: 100)),
            ("10 cm kare", .addRectangle(width: 100, height: 100)),
            ("150 çizgi", .addLine(length: 150, vertical: false)),
            ("150 mm çizgi", .addLine(length: 150, vertical: false)),
            ("15 cm yatay çizgi", .addLine(length: 150, vertical: false)),
            ("15 santimlik çizgi", .addLine(length: 150, vertical: false)),
            ("80 dikey çizgi", .addLine(length: 80, vertical: true)),
            ("dikey 80", .addLine(length: 80, vertical: true))
        ])
    }

    // MARK: Moving, turning, resizing

    func testMoveForms() {
        assertCommands([
            ("25 sağa taşı", .move(right: 25, down: 0)),
            ("25 sağa", .move(right: 25, down: 0)),
            ("sağa 25", .move(right: 25, down: 0)),
            ("25 SAĞA TAŞI", .move(right: 25, down: 0)),
            ("25 saga tasi", .move(right: 25, down: 0)),
            ("1 cm sola kaydır", .move(right: -10, down: 0)),
            ("yarım santim yukarı", .move(right: 0, down: -5)),
            ("bir buçuk santim aşağı", .move(right: 0, down: 15)),
            ("1 santim 2 milim sağa", .move(right: 12, down: 0)),
            ("on milim sola", .move(right: -10, down: 0)),
            ("10 mm aşağı al", .move(right: 0, down: 10)),
            ("20 sağa 10 yukarı", .move(right: 20, down: -10)),
            ("20 sağa ve 10 aşağı", .move(right: 20, down: 10)),
            ("2 cm sola 5 mm yukarı", .move(right: -20, down: -5)),
            ("x 100 y 50 konumuna taşı", .moveTo(x: 100, y: 50)),
            ("100, 50 noktasına taşı", .moveTo(x: 100, y: 50))
        ])
    }

    func testRotateForms() {
        assertCommands([
            ("döndür", .rotate(degrees: 90, clockwise: true)),
            ("90 sağa döndür", .rotate(degrees: 90, clockwise: true)),
            ("270 sağa döndür", .rotate(degrees: 270, clockwise: true)),
            ("saat yönünde 90", .rotate(degrees: 90, clockwise: true)),
            ("çeyrek tur", .rotate(degrees: 90, clockwise: true)),
            ("sola döndür", .rotate(degrees: 90, clockwise: false)),
            ("saat yönünün tersine döndür", .rotate(degrees: 90, clockwise: false)),
            ("yarım tur", .rotate(degrees: 180, clockwise: true)),
            ("180 derece", .rotate(degrees: 180, clockwise: true)),
            ("180 döndür", .rotate(degrees: 180, clockwise: true))
        ])
    }

    func testResizeRenameAndPieceJobs() {
        assertCommands([
            ("genişliği 95 yap", .resize(width: 95, height: nil)),
            ("eni 9,5 cm yap", .resize(width: 95, height: nil)),
            ("boyu 60 yap", .resize(width: nil, height: 60)),
            ("95 x 60 yap", .resize(width: 95, height: 60)),
            ("95'e 60 yap", .resize(width: 95, height: 60)),
            ("ölçüsünü 95 x 60 yap", .resize(width: 95, height: 60)),
            ("adı Ön Beden olsun", .rename("Ön Beden")),
            ("adını ön beden yap", .rename("ön beden")),
            ("ismi kol olsun", .rename("kol")),
            ("adı Kol-2 olsun", .rename("Kol-2")),
            ("çoğalt", .duplicate),
            ("kopyala", .duplicate),
            ("bir kopya daha", .duplicate),
            ("sil", .delete(name: nil)),
            ("parçayı sil", .delete(name: nil)),
            ("bunu sil", .delete(name: nil)),
            ("kolu sil", .delete(name: "kol")),
            ("ön bedeni seç", .select(name: "ön beden")),
            ("arka bedeni seç", .select(name: "arka beden")),
            ("seçimi bırak", .clearSelection),
            ("hiçbirini seçme", .clearSelection),
            ("seçimi kaldır", .clearSelection),
            ("sonraki parçayı seç", .selectAdjacent(next: true)),
            ("önceki parçayı seç", .selectAdjacent(next: false))
        ])
    }

    func testUndoRedoSaveHelpCancel() {
        assertCommands([
            ("geri al", .undo(steps: 1)),
            ("geri", .undo(steps: 1)),
            ("son işlemi geri al", .undo(steps: 1)),
            ("üç adım geri", .undo(steps: 3)),
            ("3 adım geri", .undo(steps: 3)),
            ("iki geri", .undo(steps: 2)),
            ("yinele", .redo),
            ("ileri al", .redo),
            ("tekrar yap", .redo),
            ("kaydet", .save),
            ("projeyi kaydet", .save),
            ("farklı kaydet", .saveCopy),
            ("yedeğini al", .saveCopy),
            ("yardım", .help),
            ("?", .help),
            ("neler yazabilirim", .help),
            ("vazgeç", .cancel),
            ("iptal", .cancel)
        ])
    }

    // MARK: Numbers and units

    func testNumberWords() {
        let table: [(String, Double)] = [
            ("sıfır virgül beş", 0.5), ("on", 10), ("on iki", 12), ("yirmi", 20), ("elli beş", 55),
            ("yüz", 100), ("yüz yirmi", 120), ("iki yüz", 200), ("iki yüz yirmi beş", 225),
            ("bin", 1000), ("bin beş yüz", 1500), ("iki bin", 2000), ("on iki bin beş yüz", 12_500),
            ("bir buçuk", 1.5), ("iki buçuk", 2.5), ("yarım", 0.5), ("on virgül beş", 10.5),
            ("on beş", 15), ("doksan dokuz", 99)
        ]
        for (words, value) in table {
            // Lines accept 1...20000 mm: spell the number as a line length in millimetres.
            if value < 1 {
                XCTAssertEqual(refusal("\(words) mm çizgi")?.code, "R04", words)
            } else {
                XCTAssertEqual(command("\(words) mm çizgi"), .addLine(length: value, vertical: false), words)
            }
        }
        XCTAssertEqual(command("iki yüz yirmi mm çizgi"), .addLine(length: 220, vertical: false))
    }

    func testPieceNamedLikeNumberWordIsNotANumber() {
        // "ön" folds to "on" (ten); as a piece name it must stay a name.
        XCTAssertEqual(command("ön bedeni seç"), .select(name: "ön beden"))
        XCTAssertEqual(command("ön bedeni sil"), .delete(name: "ön beden"))
    }

    func testUnitsAndTheLastUnitRule() {
        assertCommands([
            ("10'a 15 santim dikdörtgen", .addRectangle(width: 100, height: 150)),
            ("12 x 8 milimetre dikdörtgen", .addRectangle(width: 12, height: 8)),
            ("1,5 m çizgi", .addLine(length: 1500, vertical: false)),
            ("2 metre çizgi", .addLine(length: 2000, vertical: false)),
            ("15 santimetre çizgi", .addLine(length: 150, vertical: false)),
            ("10 sağa 5 cm yukarı", .move(right: 10, down: -50))
        ])
    }

    func testAmbiguousThousandsAreAskedNotGuessed() {
        let r = refusal("1.500 çizgi")
        XCTAssertEqual(r?.code, "R05")
        XCTAssertEqual(r?.suggestions, ["1,5 çizgi", "1500 çizgi"])
        XCTAssertEqual(refusal("1,500 çizgi")?.code, "R05")
        XCTAssertEqual(refusal("12.500 x 80 dikdörtgen")?.code, "R05")
        // A leading zero is a plain decimal.
        // 0,125 is a number (not two readings); it is simply below the 1 mm minimum.
        XCTAssertEqual(refusal("0,125 mm çizgi")?.code, "R04")
        XCTAssertEqual(command("1,5 mm çizgi"), .addLine(length: 1.5, vertical: false))
        XCTAssertEqual(command("1.5 mm çizgi"), .addLine(length: 1.5, vertical: false))
        XCTAssertEqual(refusal("1.500.000 çizgi")?.code, "R01")
    }

    func testBareSmallNumbersAskForTheUnit() {
        let r = refusal("12 x 8 dikdörtgen")
        XCTAssertEqual(r?.code, "R06")
        XCTAssertEqual(r?.suggestions, ["12 x 8 mm dikdörtgen", "12 x 8 cm dikdörtgen"])
        XCTAssertEqual(refusal("kare 5")?.code, "R06")
        XCTAssertEqual(refusal("genişliği 9 yap")?.code, "R06")
        // Moving an existing piece never asks.
        XCTAssertEqual(command("5 sağa taşı"), .move(right: 5, down: 0))
        // An explicit unit or a size of 30 and more is plain.
        XCTAssertEqual(command("12 x 8 mm dikdörtgen"), .addRectangle(width: 12, height: 8))
        XCTAssertEqual(command("30 x 8 mm dikdörtgen"), .addRectangle(width: 30, height: 8))
    }

    // MARK: Refusals

    func testRefusalCodes() {
        assertRefused([
            ("zzz", "R01"),
            ("yapay zeka ile çiz", "R01"),
            ("dikdörtgen", "R19"),
            ("dikdörtgen 120", "R19"),
            ("taşı", "R02"),
            ("150 mm taşı", "R01b"),
            ("sağa taşı", "R02"),
            ("çizgi", "R02"),
            ("genişliği yap", "R02"),
            ("45 derece döndür", "R13"),
            ("30000 x 80 dikdörtgen", "R04"),
            ("0 x 80 dikdörtgen", "R04"),
            ("sil dikdörtgen 120 80", "R16"),
            ("kopyala sil", "R16"),
            ("eksi 20 sağa", "R26"),
            ("-20 sağa", "R26"),
            ("100 sağa 20 sağa", "R16"),
            ("0 sağa", "R04"),
            ("11 adım geri", "R04"),
            ("adı olsun", "R24"),
            ("ön bedeni taşı", "R27"),
            ("kol bilmem ne", "R01")
        ])
        XCTAssertEqual(refusal("ters çevir")?.code, "R16b")
        XCTAssertEqual(refusal("büyüt")?.code, "R16b")
        XCTAssertEqual(refusal("90 sağa")?.code, "R16b")
        XCTAssertEqual(refusal("90 mm sağa taşı"), nil)
    }

    func testVeryLongInputIsRefused() {
        let long = String(repeating: "a", count: 201)
        XCTAssertEqual(refusal(long)?.code, "R23")
        let borderline = String(repeating: "a", count: 200)
        XCTAssertEqual(refusal(borderline)?.code, "R01")
        XCTAssertEqual(refusal("")?.code, "R01")
        XCTAssertEqual(refusal("   ")?.code, "R01")
    }

    func testRefusalsNeverPromiseAnythingAboutTheDocument() {
        // The R01 text carries examples that work on the iPhone, not Windows-only ones.
        let r = refusal("zzz")
        XCTAssertEqual(r?.suggestions, ["120 x 80 dikdörtgen", "25 sağa taşı", "90 sağa döndür"])
        XCTAssertTrue(r?.message.contains("geri al") == true)
    }

    func testTypoSuggestionIsOfferedButNeverRun() {
        let r = refusal("dikdortgem 120 80")
        XCTAssertEqual(r?.code, "R01")
        XCTAssertEqual(r?.suggestions, ["dikdörtgen 120 80"])
        XCTAssertTrue(r?.message.hasPrefix("Şunu mu demek istediniz: dikdörtgen 120 80?") == true)
        // A short word is not suggested.
        XCTAssertEqual(refusal("dikdor 120 80")?.suggestions.first?.hasPrefix("dikd"), false)
    }

    // MARK: Pieces by name

    func testSeveralMatchesAreAsked() {
        let both = ["Ön beden", "Ön beden 2"]
        let r = refusal("ön bedeni seç", both)
        XCTAssertEqual(r?.code, "R15b")
        XCTAssertEqual(r?.suggestions, ["Ön beden", "Ön beden 2"])
        XCTAssertEqual(r?.exactChoices.map(\.command),
                       [.selectAt(index: 0, name: "Ön beden"), .selectAt(index: 1, name: "Ön beden 2")])
        XCTAssertEqual(refusal("ön bedeni sil", both)?.exactChoices.first?.command,
                       .deleteAt(index: 0, name: "Ön beden"))
        // A number in the name tells them apart.
        XCTAssertEqual(command("ön beden 2 seç", both), .select(name: "ön beden 2"))
    }

    func testMissingPieceNames() {
        XCTAssertEqual(refusal("bacak seç")?.code, "R15a")
        XCTAssertEqual(refusal("bacak seç", [])?.code, "R03")
        XCTAssertEqual(refusal("seç")?.code, "R15a")
        XCTAssertEqual(refusal("sonraki parçayı seç", [])?.code, "R03")
        XCTAssertEqual(command("sil", []), .delete(name: nil))
    }

    func testPieceNamesIgnoreCaseAndTurkishLetters() {
        XCTAssertEqual(command("ARKA BEDENİ SEÇ"), .select(name: "arka beden"))
        XCTAssertEqual(command("on bedeni sec", ["Ön beden"]), .select(name: "on beden"))
        XCTAssertEqual(command("ÖN BEDENİ SİL"), .delete(name: "ön beden"))
    }

    // MARK: Windows-only and elsewhere

    func testThingsTheIPhoneDoesNotDoSaySoAndPointSomewhere() {
        assertRefused([
            ("aynala", "W01"), ("alt üst aynala", "W01"), ("sağ sol aynala", "W01"),
            ("1 cm dikiş payı", "W01"), ("dikiş payı 10", "W01"),
            ("bir beden büyüt", "W01"), ("serileme yap", "W01"),
            ("pastal yap", "W01"), ("dxf al", "W01"), ("dışa aktar", "P01"),
            ("plotter dosyası hazırla", "W01"), ("a4 çıktı", "W01"), ("makine dosyası hazırla", "W01"),
            ("iplik yönü", "W01"), ("parça bilgileri", "W01"), ("yeni katman", "W01"),
            ("karşı parçasını çıkar", "W01"), ("koyu tema", "W01"), ("hakkında", "W01"),
            ("dikdörtgen aracı", "P01"), ("çizgi aracı", "P01"), ("çizimi kapat", "P01"),
            ("tümünü göster", "P01"), ("yakınlaştır", "P01"),
            ("aç", "P01"), ("yeni proje", "P01"), ("kapat", "P01")
        ])
        XCTAssertTrue(refusal("dxf al")?.message.contains("Windows") == true)
        XCTAssertTrue(refusal("aynala")?.message.contains("iPhone'da") == true)
    }

    func testMovingToAPositionMeasuredFromTheTopIsRefusedOnThePhone() {
        // The phone measures Y from the bottom; "yukarıdan" would silently mean something else.
        XCTAssertEqual(refusal("soldan 100 yukarıdan 50 konuma taşı")?.code, "P01")
    }

    // MARK: Behaviour that must hold for any input

    func testNoSentenceCrashesAndRefusalsAlwaysSayWhatToDo() {
        let hostile = [
            "", " ", ".", "...", "?", "!!!", "'", "''''", "-", "--5", "1.", ".5", "1,", ",5", "x", "×", "*", "12x", "x12",
            "1e5 çizgi", "99999999999999999999 çizgi", "1.5.5 çizgi", "١٢٣ çizgi", "💥", "dikdörtgen 💥 120 80",
            "yüz yüz", "bin bin", "yarım yarım", "virgül", "buçuk", "on virgül", "sıfır", "sıfır sıfır",
            "100'", "100'e", "'e 100", "100'e'e 100", "çizgi çizgi", "adı", "adı  ", "adını", "ismi.",
            String(repeating: "yüz ", count: 40), String(repeating: "1 ", count: 90), "sağa sağa sağa",
            "25 sağa 25 sağa 25 sağa", "yukarı", "sola 5 sağa", "döndür döndür", "90 90 döndür",
            "seç", "sil sil", "ön ön ön seç", "geri geri", "iki iki geri", "-3 adım geri", "0 adım geri",
            "adı \(String(repeating: "k", count: 150)) olsun", "\n120 x 80 dikdörtgen\n", "120\tx\t80 dikdörtgen"
        ]
        for text in hostile {
            switch outcome(text) {
            case .command(let parsed):
                // A command must at least be internally sane.
                switch parsed {
                case .addRectangle(let w, let h):
                    XCTAssertTrue(w >= 1 && w <= 20_000 && h >= 1 && h <= 20_000, text)
                case .addLine(let l, _):
                    XCTAssertTrue(l >= 1 && l <= 20_000, text)
                case .move(let right, let down):
                    XCTAssertTrue(right.isFinite && down.isFinite, text)
                case .rename(let name):
                    XCTAssertTrue(!name.isEmpty && name.count <= 128, text)
                default:
                    break
                }
            case .ask(let r), .refuse(let r):
                XCTAssertFalse(r.message.isEmpty, text)
                XCTAssertFalse(r.code.isEmpty, text)
            }
        }
    }

    func testWhitespaceAndPunctuationAreIgnored() {
        let rect = ParsedCommand.addRectangle(width: 120, height: 80)
        for text in ["120 x 80 dikdörtgen", "  120   x   80   dikdörtgen  ", "120 x 80 dikdörtgen.", "120 x 80 dikdörtgen!",
                     "120 x 80 dikdörtgen?", "120 x 80 dikdörtgen\n", "120 x 80 dikdörtgen,", "hemen şunu 120 x 80 dikdörtgen yap"] {
            XCTAssertEqual(command(text), rect, text)
        }
    }

    func testTheSentenceIsNotChangedByLettersCase() {
        for text in ["25 sağa taşı", "25 SAĞA TAŞI", "25 Sağa Taşı", "25 sAğA tAşI"] {
            XCTAssertEqual(command(text), .move(right: 25, down: 0), text)
        }
        // Dotted and dotless i.
        XCTAssertEqual(command("DİKDÖRTGEN 120 X 80"), .addRectangle(width: 120, height: 80))
        XCTAssertEqual(command("GERİ AL"), .undo(steps: 1))
    }

    func testRefusalMessagesNeverTalkAboutAI() {
        let samples = ["zzz", "yapay zeka ile çiz", "dikdörtgen", "taşı", "aynala", "1.500 çizgi", "12 x 8 dikdörtgen", "45 derece döndür"]
        for text in samples {
            // The message may quote what was typed, but the fixed text must not.
            guard let r = refusal(text) else { continue }
            let fixed = r.message.replacingOccurrences(of: text, with: "")
            for word in ["asistan", "model", "yapay zeka"] {
                XCTAssertFalse(fixed.lowercased().contains(word), "\(text): \(r.message)")
            }
        }
    }
}
