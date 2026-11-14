import XCTest

final class FixtureLoadingTests: XCTestCase {

    private func launch(with fixture: String) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchArguments += ["-AppleLanguages", "(en)", "-AppleLocale", "en_US", "--ui-test-fixture"]
        if let url = Bundle(for: Self.self).url(forResource: fixture, withExtension: "tcad"),
           let data = try? Data(contentsOf: url) {
            app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = data.base64EncodedString()
        }
        app.launch()
        return app
    }


    // These are parser/screen smoke checks, not Windows round-trip validation:
    // production v7 rotation traces and v8 layers require independent C# validation.
    private func launch(json: String) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchArguments += ["-AppleLanguages", "(en)", "-AppleLocale", "en_US", "--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(json.utf8).base64EncodedString()
        app.launch()
        return app
    }

    func testNewerWindowsEnvelopesShowReadOnlyLimitations() {
        let body = #""Name":"Windows Önizleme","Pieces":[{"Name":"Kontur","Closed":true,"Points":[{"X":0,"Y":0},{"X":100,"Y":0},{"X":100,"Y":100}]}],"GradingRules":[]""#
        let cases: [(Int, String)] = [
            (6, "[6,{\(body),\"Pieces\":[{\"Name\":\"Kontur\",\"Closed\":true,\"Points\":[{\"X\":0,\"Y\":0},{\"X\":100,\"Y\":0},{\"X\":100,\"Y\":100}],\"Notches\":[{\"VertexIndex\":0,\"Position\":{\"X\":0,\"Y\":0}}]}]}]"),
            (7, "[7,{\(body),\"NestingSnapshot\":{\"RotationTrace\":{}}}]"),
            (8, "[8,{\(body),\"Layers\":[{\"Id\":\"00000000-0000-0000-0000-000000000000\",\"Name\":\"Katman 1\",\"Visible\":true,\"Locked\":false}]}]")
        ]
        for (version, fixture) in cases {
            let app = launch(json: fixture)
            XCTAssertTrue(app.staticTexts["Windows Önizleme"].waitForExistence(timeout: 15))
            XCTAssertTrue(app.staticTexts["Kontur"].exists)
            let notice = app.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "Sürüm \(version) yalnız kontur önizlemesi")).firstMatch
            XCTAssertTrue(notice.exists)
            app.terminate()
        }
    }

    func testUnsupportedAndMalformedNewerVersionsAreRejected() {
        let body = #""Name":"Hatalı","Pieces":[{"Name":"P","Closed":false,"Points":[{"X":0,"Y":0},{"X":10,"Y":0}]}],"GradingRules":[]""#
        let cases: [(String, String)] = [
            ("[9,{\(body)}]", "Desteklenmeyen proje biçimi."),
            ("[8,{\(body)}]", "Sürüm 8 katman listesi eksik."),
            ("[7,{\(body)}]", "Sürüm 7 döndürme izi eksik."),
            ("[6,{\(body)}]", "Sürüm 6 çentik işareti içermiyor."),
            ("[true,{\(body)}]", "Desteklenmeyen proje biçimi.")
        ]
        for (fixture, message) in cases {
            let app = launch(json: fixture)
            let alert = app.alerts["Dosya açılamadı"]
            XCTAssertTrue(alert.waitForExistence(timeout: 15))
            XCTAssertTrue(alert.staticTexts[message].exists)
            app.terminate()
        }
    }

    func testEmptyProjectLoadsWithoutError() {
        let app = launch(with: "empty-project")
        XCTAssertTrue(app.staticTexts["Boş Proje"].waitForExistence(timeout: 15))
        XCTAssertTrue(app.staticTexts["Bu projede görüntülenebilir kontur yok."].waitForExistence(timeout: 5))
        XCTAssertTrue(app.staticTexts["0 parça • Ölçekli üretim çıktısı değildir"].exists)
    }

    func testMinimalPieceLoads() {
        let app = launch(with: "minimal-piece")
        XCTAssertTrue(app.staticTexts["Minimal Parça"].waitForExistence(timeout: 15))
        XCTAssertTrue(app.staticTexts["1 parça • Ölçekli üretim çıktısı değildir"].exists)
        XCTAssertTrue(app.staticTexts["İki Nokta"].exists)
    }

    func testTurkishUTF8CharactersArePreserved() {
        let app = launch(with: "turkish-utf8")
        XCTAssertTrue(app.staticTexts["Türkçe Örnek"].waitForExistence(timeout: 15))
        XCTAssertTrue(app.staticTexts["Göğüs Ölçümü"].exists)
        XCTAssertTrue(app.staticTexts["Rol: Kesim"].exists)
        XCTAssertTrue(app.staticTexts["Beden: M"].exists)
        XCTAssertTrue(app.staticTexts["Malzeme: Pamuk"].exists)
        XCTAssertTrue(app.staticTexts["Adet: 5"].exists)
        XCTAssertTrue(app.staticTexts["İplik yönü: Kayıtlı"].exists)
    }

    func testPieceWithoutGrainlineShowsUnspecified() {
        let app = launch(with: "no-grainline")
        XCTAssertTrue(app.staticTexts["İplik Yoksuz"].waitForExistence(timeout: 15))
        XCTAssertTrue(app.staticTexts["Yoksuz Parça"].exists)
        XCTAssertTrue(app.staticTexts["İplik yönü: Belirtilmemiş"].exists)
    }

    func testOpenContourLoads() {
        let app = launch(with: "open-contour")
        XCTAssertTrue(app.staticTexts["Açık Kontur"].waitForExistence(timeout: 15))
        XCTAssertTrue(app.staticTexts["Açık Çizgi"].exists)
        XCTAssertTrue(app.staticTexts["Açık kontur"].exists)
    }

    func testLargeCoordinatesLoadWithoutCrash() {
        let app = launch(with: "large-coordinates")
        XCTAssertTrue(app.staticTexts["Büyük Koordinatlar"].waitForExistence(timeout: 15))
        XCTAssertTrue(app.staticTexts["Büyük"].exists)
        XCTAssertFalse(app.staticTexts["Dosya açılamadı"].exists)
    }

    func testExistingSelectionFixtureStillWorks() {
        let app = launch(with: "selection")
        XCTAssertTrue(app.staticTexts["Seçim Testi"].waitForExistence(timeout: 15))
        XCTAssertTrue(app.staticTexts["Ön Beden"].exists)
        XCTAssertTrue(app.staticTexts["Arka Beden"].exists)
        XCTAssertTrue(app.staticTexts["2 parça • Ölçekli üretim çıktısı değildir"].exists)
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
