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
}
