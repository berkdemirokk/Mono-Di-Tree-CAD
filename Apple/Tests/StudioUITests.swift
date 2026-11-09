import XCTest

final class StudioUITests: XCTestCase {
    func testCatalogueOpensWithoutChangingTheCurrentDrawing() {
        let app = XCUIApplication()
        app.launchArguments += ["-interfaceEnglish", "YES"]
        app.launch()
        let catalogue = app.buttons["factory-catalog"]
        XCTAssertTrue(catalogue.waitForExistence(timeout: 15))
        catalogue.tap()
        XCTAssertTrue(app.navigationBars["Model catalogue"].waitForExistence(timeout: 5))
        XCTAssertTrue(app.buttons["factory-catalog-import"].exists)
        app.buttons["Close"].tap()
        XCTAssertTrue(app.buttons["marker-workspace"].waitForExistence(timeout: 5))
    }

    func testWorkshopLanguageAndMissingSource() {
        let app = XCUIApplication()
        app.launchArguments += ["-interfaceEnglish", "YES"]
        app.launch()
        let workshop = app.buttons["marker-workspace"]
        XCTAssertTrue(workshop.waitForExistence(timeout: 15))
        workshop.tap()
        XCTAssertTrue(app.navigationBars["Marker work order"].waitForExistence(timeout: 5))
        XCTAssertTrue(app.staticTexts.containing(NSPredicate(format: "label CONTAINS %@", "No loaded model")).firstMatch.exists)
        app.buttons["Close"].tap()
        let measurements = app.buttons["Reference measurements · 36–52"]
        XCTAssertTrue(measurements.waitForExistence(timeout: 5))
        measurements.tap()
        XCTAssertTrue(app.staticTexts.containing(NSPredicate(format: "label CONTAINS %@", "110")).firstMatch.waitForExistence(timeout: 5))
    }
}
