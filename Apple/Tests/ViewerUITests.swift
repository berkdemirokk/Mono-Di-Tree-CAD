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
}
