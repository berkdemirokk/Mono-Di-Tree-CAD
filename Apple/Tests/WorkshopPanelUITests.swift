import XCTest

/// The workshop's tool cards: ready-made shapes, one-step sharing, closing with a value that
/// cannot be applied, and the side-by-side layout on a wide screen.
final class WorkshopPanelUITests: XCTestCase {
    override func tearDown() {
        XCUIDevice.shared.orientation = .portrait
        super.tearDown()
    }

    private func launchClean() -> XCUIApplication {
        let app = XCUIApplication()
        app.launchArguments += ["-AppleLanguages", "(en)", "-AppleLocale", "en_US", "--ui-test-reset-draft"]
        app.launch()
        return app
    }

    private func openNewProject(in app: XCUIApplication, file: StaticString = #filePath, line: UInt = #line) {
        let start = app.buttons["Yeni proje oluştur"]
        XCTAssertTrue(start.waitForExistence(timeout: 15), file: file, line: line)
        start.tap()
        XCTAssertTrue(app.staticTexts["Kalıbını oluştur"].waitForExistence(timeout: 10), file: file, line: line)
    }

    private func countLabel(_ count: Int, in app: XCUIApplication) -> XCUIElement {
        app.staticTexts["\(count) parça • Önizleme ölçüye göre ekrana sığdırılır"]
    }

    /// A slow drag over half the tools area; a flick would carry momentum and skip a control.
    private func dragTools(in app: XCUIApplication, towardsEnd: Bool) {
        let tools = app.scrollViews["workshop-tools"]
        let scroll = tools.exists ? tools : app.scrollViews.firstMatch
        let from = scroll.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: towardsEnd ? 0.75 : 0.25))
        let to = scroll.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: towardsEnd ? 0.25 : 0.75))
        from.press(forDuration: 0.05, thenDragTo: to)
    }

    /// Scrolls the tools until the element can be touched. Returns whether it can.
    @discardableResult
    private func reach(_ element: XCUIElement, in app: XCUIApplication) -> Bool {
        for _ in 0..<14 {
            if element.exists && element.isHittable { return true }
            dragTools(in: app, towardsEnd: true)
        }
        for _ in 0..<14 {
            if element.exists && element.isHittable { return true }
            dragTools(in: app, towardsEnd: false)
        }
        return element.exists && element.isHittable
    }

    private func tapBelow(_ element: XCUIElement, in app: XCUIApplication,
                          file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertTrue(reach(element, in: app), "Control should be reachable by scrolling", file: file, line: line)
        element.tap()
    }

    private func expectValue(_ value: String, of element: XCUIElement,
                             file: StaticString = #filePath, line: UInt = #line) {
        let expectation = XCTNSPredicateExpectation(
            predicate: NSPredicate(format: "value == %@", value), object: element)
        XCTAssertEqual(XCTWaiter().wait(for: [expectation], timeout: 5), .completed,
                       "Expected \(value)", file: file, line: line)
    }

    func testCircleAndTriangleAreAddedAsEditablePieces() {
        let app = launchClean()
        openNewProject(in: app)

        tapBelow(app.buttons["Daire ekle"], in: app)
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 5))
        // The new circle is selected, so its name is in the name box.
        let name = app.textFields["Seçili parçanın adı"]
        XCTAssertTrue(reach(name, in: app))
        expectValue("Daire 1", of: name)
        // The diameter reads in the width and height boxes, like any closed piece.
        let width = app.textFields["En"]
        XCTAssertTrue(reach(width, in: app))
        expectValue("100", of: width)
        expectValue("100", of: app.textFields["Boy"])

        tapBelow(app.buttons["Üçgen ekle"], in: app)
        XCTAssertTrue(countLabel(2, in: app).waitForExistence(timeout: 5))
        XCTAssertTrue(reach(name, in: app))
        expectValue("Üçgen 2", of: name)
        XCTAssertFalse(app.alerts["İşlem tamamlanamadı"].exists)
    }

    func testShareIsOneStepAndStaysAvailableAfterAnEdit() {
        let app = launchClean()
        openNewProject(in: app)

        // With nothing to share the button is still there, with the reason under it.
        let note = app.staticTexts["Paylaşmak için önce en az bir parça ekle."]
        XCTAssertTrue(reach(note, in: app), "The reason should be shown instead of hiding the button")
        let share = app.buttons["Paylaş veya Dosyalara kaydet"]
        XCTAssertTrue(share.exists)
        XCTAssertFalse(share.isEnabled)

        tapBelow(app.buttons["Dikdörtgen ekle"], in: app)
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 5))
        tapBelow(app.buttons["Daire ekle"], in: app)
        XCTAssertTrue(countLabel(2, in: app).waitForExistence(timeout: 5))

        // After the edits the same button is there and usable; no preparing step comes first.
        XCTAssertTrue(reach(share, in: app))
        XCTAssertTrue(share.isEnabled)
        XCTAssertFalse(app.buttons["Dosyayı hazırla (.tcad)"].exists)
        share.tap()

        XCTAssertFalse(app.alerts["İşlem tamamlanamadı"].waitForExistence(timeout: 2))
        let sheet = app.otherElements["ActivityListView"]
        let opened = sheet.waitForExistence(timeout: 10) || !share.isHittable
        XCTAssertTrue(opened, "The system share sheet should open from the single tap")
    }

    func testCloseWithAnInvalidValueOffersToDiscardIt() {
        let app = launchClean()
        openNewProject(in: app)
        tapBelow(app.buttons["Dikdörtgen ekle"], in: app)
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 5))

        let width = app.textFields["En"]
        tapBelow(width, in: app)
        width.typeText(",,")
        let done = app.keyboards.buttons["Done"]
        if done.waitForExistence(timeout: 3) { done.tap() }

        let close = app.buttons["Kapat"]
        close.tap()
        let discard = app.buttons["Değişikliği at ve kapat"]
        XCTAssertTrue(discard.waitForExistence(timeout: 5), "Closing should offer a way out")
        let back = app.buttons["Düzeltmeye dön"]
        XCTAssertTrue(back.exists)
        back.tap()
        let gone = XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: discard)
        XCTAssertEqual(XCTWaiter().wait(for: [gone], timeout: 5), .completed)
        XCTAssertTrue(countLabel(1, in: app).exists, "Going back keeps the workshop open")

        close.tap()
        XCTAssertTrue(discard.waitForExistence(timeout: 5))
        discard.tap()
        let resume = app.buttons["Son çizime devam et"]
        XCTAssertTrue(resume.waitForExistence(timeout: 10), "The workshop should close")

        // Only the typed text was dropped: the drawing itself is intact.
        resume.tap()
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 10))
        let reopened = app.textFields["En"]
        XCTAssertTrue(reach(reopened, in: app))
        expectValue("100", of: reopened)
    }

    func testWideScreenPutsTheDrawingBesideTheTools() {
        let app = launchClean()
        openNewProject(in: app)
        tapBelow(app.buttons["Dikdörtgen ekle"], in: app)
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 5))

        XCUIDevice.shared.orientation = .landscapeLeft
        let drawing = app.descendants(matching: .any)["Dokunarak parça seçebileceğiniz kalıp önizlemesi"]
        let tools = app.scrollViews["workshop-tools"]
        XCTAssertTrue(drawing.waitForExistence(timeout: 5))
        XCTAssertTrue(tools.waitForExistence(timeout: 5))
        let beside = XCTNSPredicateExpectation(predicate: NSPredicate { _, _ in
            drawing.frame.maxX <= tools.frame.minX + 1 && drawing.frame.width > tools.frame.width
        }, object: nil)
        XCTAssertEqual(XCTWaiter().wait(for: [beside], timeout: 5), .completed,
                       "Drawing \(drawing.frame) should sit left of the tools \(tools.frame)")
        // The tools still work in this layout.
        tapBelow(app.buttons["Üçgen ekle"], in: app)
        XCTAssertTrue(countLabel(2, in: app).waitForExistence(timeout: 5))

        // Back in portrait the drawing returns above the tools.
        XCUIDevice.shared.orientation = .portrait
        let above = XCTNSPredicateExpectation(predicate: NSPredicate { _, _ in
            drawing.frame.maxY <= tools.frame.minY + 1
        }, object: nil)
        XCTAssertEqual(XCTWaiter().wait(for: [above], timeout: 5), .completed)
    }
}
