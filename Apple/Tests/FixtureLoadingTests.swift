import XCTest

final class FixtureLoadingTests: XCTestCase {

    private func launch(with fixture: String) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchArguments += ["-AppleLanguages", "(en)", "-AppleLocale", "en_US", "--ui-test-reset-draft", "--ui-test-fixture"]
        if let url = Bundle(for: Self.self).url(forResource: fixture, withExtension: "tcad"),
           let data = try? Data(contentsOf: url) {
            app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = data.base64EncodedString()
        }
        app.launch()
        return app
    }



    // Preview parser smoke checks. Production traces still need Windows-side validation.
    private func launch(json: String) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchArguments += ["-AppleLanguages", "(en)", "-AppleLocale", "en_US", "--ui-test-reset-draft", "--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(json.utf8).base64EncodedString()
        app.launch()
        return app
    }

    private func versionedProject(_ version: Any, includeRequiredMetadata: Bool = false) -> String {
        var piece: [String: Any] = [
            "Name": "Kontur", "Closed": true,
            "Points": [["X": 0, "Y": 0], ["X": 100, "Y": 0], ["X": 100, "Y": 100]]
        ]
        var body: [String: Any] = [
            "Name": "Windows Önizleme", "Pieces": [piece], "GradingRules": []
        ]
        if includeRequiredMetadata, let number = version as? Int {
            switch number {
            case 6:
                piece["Notches"] = [["VertexIndex": 0, "Position": ["X": 0, "Y": 0]]]
                body["Pieces"] = [piece]
            case 7:
                body["NestingSnapshot"] = ["RotationTrace": [:] as [String: Any]]
            case 8:
                body["Layers"] = [[
                    "Id": "00000000-0000-0000-0000-000000000000",
                    "Name": "Katman 1", "Visible": true, "Locked": false
                ]]
            case 9:
                // This is a real deterministic two-point line in the v9 shape.
                piece["Closed"] = false
                piece["Points"] = [["X": 0, "Y": 0], ["X": 100, "Y": 0]]
                piece["Curves"] = [[
                    "$type": "line",
                    "Start": ["X": 0, "Y": 0],
                    "End": ["X": 100, "Y": 0]
                ]]
                body["Pieces"] = [piece]
            default:
                break
            }
        }
        let data = try! JSONSerialization.data(withJSONObject: [version, body], options: [.sortedKeys])
        return String(decoding: data, as: UTF8.self)
    }

    func testNewerWindowsEnvelopesShowReadOnlyLimitations() {
        for version in [6, 7, 8, 9] {
            let app = launch(json: versionedProject(version, includeRequiredMetadata: true))
            XCTAssertTrue(app.staticTexts["Windows Önizleme"].waitForExistence(timeout: 15))
            XCTAssertTrue(app.staticTexts["1 parça • Ekrana sığdırıldı; kesim için kullanmayın"].exists)
            let notice = app.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "(sürüm \(version))")).firstMatch
            XCTAssertTrue(notice.exists)
            XCTAssertFalse(app.buttons["Kopyasını düzenle"].exists,
                           "A versioned production project must remain read-only.")
            app.terminate()
        }
    }

    func testUnsupportedAndMalformedNewerVersionsAreRejected() {
        let cases: [(String, String)] = [
            (versionedProject(10), "Desteklenmeyen proje biçimi."),
            (versionedProject(9), "Sürüm 9 parametrik kontur içermiyor."),
            (versionedProject(8), "Sürüm 8 katman listesi eksik."),
            (versionedProject(7), "Sürüm 7 döndürme izi eksik."),
            (versionedProject(6), "Sürüm 6 çentik işareti içermiyor."),
            (versionedProject(true), "Desteklenmeyen proje biçimi.")
        ]
        for (fixture, message) in cases {
            let app = launch(json: fixture)
            let alert = app.alerts["Dosya açılamadı"]
            XCTAssertTrue(alert.waitForExistence(timeout: 15))
            XCTAssertTrue(alert.staticTexts[message].exists)
            app.terminate()
        }
    }

    private func selectPiece(_ name: String, in app: XCUIApplication) {
        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 10))
        picker.tap()
        let row = app.buttons[name]
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        row.tap()
        let selected = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", name)).firstMatch
        XCTAssertTrue(selected.waitForExistence(timeout: 10))
    }

    private func hasAccessibleDetail(_ text: String, in app: XCUIApplication) -> Bool {
        app.descendants(matching: .any)
            .matching(NSPredicate(format: "label CONTAINS %@", text))
            .firstMatch.exists
    }

    func testOwnSimpleMobileFileCanBeEditedAsACopy() {
        let app = launch(with: "mobile-simple")
        XCTAssertTrue(app.staticTexts["Telefon çizimi"].waitForExistence(timeout: 15))
        let edit = app.buttons["Kopyasını düzenle"]
        XCTAssertTrue(edit.waitForExistence(timeout: 10))
        edit.tap()
        XCTAssertTrue(app.staticTexts["2 parça • Tümü ekranda"]
            .waitForExistence(timeout: 15))
        app.buttons["Dikdörtgen ekle"].tap()
        XCTAssertTrue(app.staticTexts["3 parça • Tümü ekranda"].exists)
        app.buttons["Kapat"].tap()
        let resume = app.buttons["Son çizime devam et"]
        XCTAssertTrue(resume.waitForExistence(timeout: 10))
        resume.tap()
        XCTAssertTrue(app.staticTexts["3 parça • Tümü ekranda"].waitForExistence(timeout: 10))
        app.buttons["Kapat"].tap()
        XCTAssertTrue(app.staticTexts["2 parça • Ekrana sığdırıldı; kesim için kullanmayın"].exists)
    }

    func testSketchWithProductionMetadataIsReadOnly() {
        let sketch = #"""
        {"Name":"Özel üretim","GradingRules":[],
         "Pieces":[{"Id":"11111111-1111-4111-8111-111111111111",
           "Name":"İşaretli","Closed":true,
           "Points":[{"X":0,"Y":0},{"X":100,"Y":0},{"X":100,"Y":100}],
           "Metadata":{"Role":"Kesim","Size":"M"}}]}
        """#
        let app = launch(json: sketch)
        XCTAssertTrue(app.staticTexts["Özel üretim"].waitForExistence(timeout: 15))
        XCTAssertFalse(app.buttons["Kopyasını düzenle"].exists,
                       "Unsupported metadata must not be discarded silently.")
    }

    func testEmptyProjectLoadsWithoutError() {
        let app = launch(with: "empty-project")
        XCTAssertTrue(app.staticTexts["Boş Proje"].waitForExistence(timeout: 15))
        XCTAssertTrue(app.staticTexts["Bu dosyada gösterilecek parça yok."].waitForExistence(timeout: 5))
        XCTAssertTrue(app.staticTexts["0 parça • Ekrana sığdırıldı; kesim için kullanmayın"].exists)
    }

    func testMinimalPieceLoads() {
        let app = launch(with: "minimal-piece")
        XCTAssertTrue(app.staticTexts["Minimal Parça"].waitForExistence(timeout: 15))
        XCTAssertTrue(app.staticTexts["1 parça • Ekrana sığdırıldı; kesim için kullanmayın"].exists)
        selectPiece("İki Nokta", in: app)
    }

    func testTurkishUTF8CharactersArePreserved() {
        let app = launch(with: "turkish-utf8")
        XCTAssertTrue(app.staticTexts["Türkçe Örnek"].waitForExistence(timeout: 15))
        selectPiece("Göğüs Ölçümü", in: app)
        for detail in ["Rol: Kesim", "Beden: M", "Malzeme: Pamuk", "Adet: 5", "Kumaş yönü: Dosyada var"] {
            XCTAssertTrue(hasAccessibleDetail(detail, in: app), "Missing detail: \(detail)")
        }
    }

    func testPieceWithoutGrainlineShowsUnspecified() {
        let app = launch(with: "no-grainline")
        XCTAssertTrue(app.staticTexts["İplik Yoksuz"].waitForExistence(timeout: 15))
        selectPiece("Yoksuz Parça", in: app)
        XCTAssertTrue(hasAccessibleDetail("Kumaş yönü: Dosyada yok", in: app))
    }

    func testOpenContourLoads() {
        let app = launch(with: "open-contour")
        XCTAssertTrue(app.staticTexts["Açık Kontur"].waitForExistence(timeout: 15))
        selectPiece("Açık Çizgi", in: app)
        XCTAssertTrue(hasAccessibleDetail("Açık çizgi", in: app))
    }

    func testLargeCoordinatesLoadWithoutCrash() {
        let app = launch(with: "large-coordinates")
        XCTAssertTrue(app.staticTexts["Büyük Koordinatlar"].waitForExistence(timeout: 15))
        selectPiece("Büyük", in: app)
        XCTAssertFalse(app.staticTexts["Dosya açılamadı"].exists)
    }

    func testExistingSelectionFixtureStillWorks() {
        let app = launch(with: "selection")
        XCTAssertTrue(app.staticTexts["Seçim Testi"].waitForExistence(timeout: 15))
        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 10))
        picker.tap()
        XCTAssertTrue(app.buttons["Ön Beden"].waitForExistence(timeout: 10))
        XCTAssertTrue(app.buttons["Arka Beden"].exists)
        app.buttons["Arka Beden"].tap()
        XCTAssertTrue(app.staticTexts["2 parça • Ekrana sığdırıldı; kesim için kullanmayın"].exists)
    }

    func testInvalidJSONShowsError() {
        let app = launch(with: "invalid-json")
        XCTAssertTrue(app.alerts["Dosya açılamadı"].waitForExistence(timeout: 15))
        XCTAssertTrue(app.alerts["Dosya açılamadı"].staticTexts["Desteklenmeyen proje biçimi."].exists)
    }

    func testMissingNameShowsError() {
        let app = launch(with: "missing-name")
        XCTAssertTrue(app.alerts["Dosya açılamadı"].waitForExistence(timeout: 15))
        XCTAssertTrue(app.alerts["Dosya açılamadı"].staticTexts["Proje adı veya parça listesi eksik ya da sınır dışında."].exists)
    }

    func testMissingPiecesShowsError() {
        let app = launch(with: "missing-pieces")
        XCTAssertTrue(app.alerts["Dosya açılamadı"].waitForExistence(timeout: 15))
        XCTAssertTrue(app.alerts["Dosya açılamadı"].staticTexts["Proje adı veya parça listesi eksik ya da sınır dışında."].exists)
    }

    func testMissingClosedShowsError() {
        let app = launch(with: "missing-closed")
        XCTAssertTrue(app.alerts["Dosya açılamadı"].waitForExistence(timeout: 15))
        XCTAssertTrue(app.alerts["Dosya açılamadı"].staticTexts["Parça konturu eksik veya sınır dışında."].exists)
    }

    func testNaNCoordinateShowsError() {
        let app = launch(with: "nan-coordinate")
        XCTAssertTrue(app.alerts["Dosya açılamadı"].waitForExistence(timeout: 15))
        XCTAssertTrue(app.alerts["Dosya açılamadı"].staticTexts["Geçersiz veya gösterilemeyen koordinat."].exists)
    }

    func testManyPiecesFixtureLoads() {
        let app = launch(with: "many-pieces")
        XCTAssertTrue(app.staticTexts["Çok Fazla Parça"].waitForExistence(timeout: 15))
    }
}
