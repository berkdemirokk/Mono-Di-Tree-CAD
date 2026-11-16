import XCTest

/// The corner point editor: adding and deleting points, and typed values that are applied instead
/// of being dropped when the user moves to another point.
final class PointEditorUITests: XCTestCase {
    override func setUp() {
        super.setUp()
        continueAfterFailure = false
    }

    private func launchClean() -> XCUIApplication {
        let app = XCUIApplication()
        app.launchArguments += ["-AppleLanguages", "(en)", "-AppleLocale", "en_US", "--ui-test-reset-draft"]
        app.launch()
        return app
    }

    private func element(_ identifier: String, in app: XCUIApplication) -> XCUIElement {
        app.descendants(matching: .any)[identifier]
    }

    private func waitFor(_ format: String, _ argument: String, on object: XCUIElement,
                         file: StaticString = #filePath, line: UInt = #line) {
        let expectation = XCTNSPredicateExpectation(predicate: NSPredicate(format: format, argument),
                                                    object: object)
        XCTAssertEqual(XCTWaiter().wait(for: [expectation], timeout: 5), .completed,
                       "\(object) should match \(format) \(argument)", file: file, line: line)
    }

    /// Slow drags, so momentum never skips a control. `scroll` is the area to drag in.
    private func reach(_ target: XCUIElement, in scroll: XCUIElement, app: XCUIApplication,
                       file: StaticString = #filePath, line: UInt = #line) {
        let area = scroll.exists ? scroll : app.scrollViews.firstMatch
        for towardsEnd in [true, false] {
            for _ in 0..<12 {
                if target.exists && target.isHittable { return }
                let from = area.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: towardsEnd ? 0.75 : 0.3))
                let to = area.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: towardsEnd ? 0.3 : 0.75))
                from.press(forDuration: 0.05, thenDragTo: to)
            }
        }
        XCTAssertTrue(target.isHittable, "Control should be reachable by scrolling", file: file, line: line)
    }

    private func tap(_ target: XCUIElement, inEditor app: XCUIApplication,
                     file: StaticString = #filePath, line: UInt = #line) {
        reach(target, in: app.scrollViews["point-editor"], app: app, file: file, line: line)
        target.tap()
    }

    /// Opens the sample drawing; its first piece, a closed four-point piece, is selected.
    private func openEditorOnSample() -> XCUIApplication {
        let app = launchClean()
        let demo = app.buttons["Örnekle dene"]
        XCTAssertTrue(demo.waitForExistence(timeout: 15))
        demo.tap()
        XCTAssertTrue(app.staticTexts["2 parça • Önizleme ölçüye göre ekrana sığdırılır"].waitForExistence(timeout: 10))
        let open = app.buttons["Köşe noktalarını düzenle"]
        let tools = app.scrollViews["workshop-tools"]
        reach(open, in: tools, app: app)
        open.tap()
        XCTAssertTrue(element("point-count", in: app).waitForExistence(timeout: 10))
        return app
    }

    private func replaceText(of field: XCUIElement, with text: String, app: XCUIApplication) {
        tap(field, inEditor: app)
        // A tap near the right edge leaves the cursor after the current text.
        field.coordinate(withNormalizedOffset: CGVector(dx: 0.95, dy: 0.5)).tap()
        let current = field.value as? String ?? ""
        field.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: current.count + 2) + text)
        let done = app.keyboards.buttons["Done"]
        if done.waitForExistence(timeout: 3) { done.tap() }
    }

    func testAddAndDeletePointChangeTheCountAndATypedValueIsAppliedWhenSwitchingPoints() {
        let app = openEditorOnSample()
        let count = element("point-count", in: app)
        waitFor("label == %@", "Bu parçada 4 nokta var.", on: count)

        // Nokta 1 is selected; the new point goes half way to Nokta 2 and becomes Nokta 2.
        tap(app.buttons["Nokta ekle"], inEditor: app)
        waitFor("label == %@", "Bu parçada 5 nokta var.", on: count)
        let status = element("point-status", in: app)
        waitFor("label CONTAINS %@", "Nokta 2 eklendi", on: status)
        XCTAssertTrue(app.textFields["Nokta 2 X koordinatı, milimetre"].exists)

        tap(app.buttons["Noktayı sil"], inEditor: app)
        waitFor("label == %@", "Bu parçada 4 nokta var.", on: count)
        waitFor("label CONTAINS %@", "Nokta 2 silindi", on: status)

        // The old Nokta 3 (100, 0) took the deleted point's place and is selected.
        let x2 = app.textFields["Nokta 2 X koordinatı, milimetre"]
        XCTAssertTrue(x2.waitForExistence(timeout: 5))
        XCTAssertEqual(x2.value as? String, "100")
        replaceText(of: x2, with: "120", app: app)
        XCTAssertTrue(element("point-pending", in: app).waitForExistence(timeout: 5),
                      "The sheet should say the typed value is not applied yet")

        // Moving on must apply the typed value, not drop it.
        tap(app.buttons["Sonraki nokta"], inEditor: app)
        waitFor("label CONTAINS %@", "Nokta 2 için yazdığınız değer uygulandı", on: status)
        XCTAssertTrue(app.textFields["Nokta 3 X koordinatı, milimetre"].waitForExistence(timeout: 5))
        XCTAssertFalse(app.alerts["Bu değişiklik yapılamadı"].exists)

        tap(app.buttons["Önceki nokta"], inEditor: app)
        let back = app.textFields["Nokta 2 X koordinatı, milimetre"]
        XCTAssertTrue(back.waitForExistence(timeout: 5))
        waitFor("value == %@", "120", on: back)
        waitFor("label == %@", "Bu parçada 4 nokta var.", on: count)

        app.buttons["Bitti"].tap()
        let gone = XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: count)
        XCTAssertEqual(XCTWaiter().wait(for: [gone], timeout: 5), .completed)
    }

    func testInvalidTypedValueKeepsThePointSelectedAndSaysWhy() {
        let app = openEditorOnSample()
        let x1 = app.textFields["Nokta 1 X koordinatı, milimetre"]
        XCTAssertTrue(x1.waitForExistence(timeout: 5))
        // Two separators can be typed without leaving the numbers keyboard page.
        replaceText(of: x1, with: ",,", app: app)

        tap(app.buttons["Sonraki nokta"], inEditor: app)
        let alert = app.alerts["Bu değişiklik yapılamadı"]
        XCTAssertTrue(alert.waitForExistence(timeout: 5))
        let why = alert.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "Nokta 1 seçili kaldı")).firstMatch
        XCTAssertTrue(why.exists, "The alert should say the point stayed selected")
        alert.buttons["Tamam"].tap()

        XCTAssertTrue(x1.waitForExistence(timeout: 5))
        XCTAssertEqual(x1.value as? String, ",,", "The typed text must stay for the user to fix")
        XCTAssertFalse(app.textFields["Nokta 2 X koordinatı, milimetre"].exists)

        // The escape hatch puts the saved value back and allows moving on.
        tap(app.buttons["Yazdığımı geri al"], inEditor: app)
        waitFor("value == %@", "0", on: x1)
        tap(app.buttons["Sonraki nokta"], inEditor: app)
        XCTAssertTrue(app.textFields["Nokta 2 X koordinatı, milimetre"].waitForExistence(timeout: 5))
    }
}
