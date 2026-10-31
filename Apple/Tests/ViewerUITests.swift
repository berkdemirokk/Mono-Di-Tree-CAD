import XCTest

final class ViewerUITests: XCTestCase {
    func testLaunchAndCancelFilePicker() {
        let app = XCUIApplication()
        app.launchArguments += ["-AppleLanguages", "(en)", "-AppleLocale", "en_US"]
        app.launch()

        let openButton = app.buttons[".tcad aç"]
        let emptyMessage = app.staticTexts["Önizlemek için bir .tcad dosyası açın."]
        XCTAssertTrue(openButton.waitForExistence(timeout: 15))
        XCTAssertTrue(emptyMessage.exists)

        for _ in 0..<2 {
            openButton.tap()
            let cancel = app.buttons["Cancel"]
            XCTAssertTrue(cancel.waitForExistence(timeout: 10), "The system file picker should open")
            cancel.tap()
            XCTAssertTrue(openButton.waitForExistence(timeout: 10))
            XCTAssertTrue(emptyMessage.exists, "Cancel must leave the empty viewer unchanged")
        }
    }

    func testSearchAndSelectPiece() throws {
        let fixture = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "selection", withExtension: "tcad"))
        let data = try Data(contentsOf: fixture)
        let app = XCUIApplication()
        app.launchArguments += ["-AppleLanguages", "(en)", "-AppleLocale", "en_US", "--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = data.base64EncodedString()
        app.launch()

        XCTAssertTrue(app.staticTexts["Seçim Testi"].waitForExistence(timeout: 15))
        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 10))
        picker.tap()
        XCTAssertTrue(app.buttons["Ön Beden"].waitForExistence(timeout: 10))
        XCTAssertTrue(app.buttons["Arka Beden"].exists)

        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 10))
        search.tap()
        search.typeText("Arka")
        XCTAssertTrue(app.buttons["Arka Beden"].waitForExistence(timeout: 5))
        XCTAssertFalse(app.buttons["Ön Beden"].exists)
        app.buttons["Arka Beden"].tap()

        let selected = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Arka Beden")).firstMatch
        XCTAssertTrue(selected.waitForExistence(timeout: 10))
        XCTAssertTrue(app.descendants(matching: .any)["Arka Beden kontur önizlemesi"].exists)
        selected.tap()
        XCTAssertTrue(app.buttons["Tüm parçalar"].waitForExistence(timeout: 10))
        app.buttons["Tüm parçalar"].tap()
        XCTAssertTrue(picker.waitForExistence(timeout: 10))
        XCTAssertTrue(app.descendants(matching: .any)["2 parçalı kontur önizlemesi"].exists)
    }
}
