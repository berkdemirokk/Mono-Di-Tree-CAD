import XCTest

/// The finger drawing sheet ("Parmakla şekil çiz"): adding points, clearing them, the
/// area size and the protection against losing points by accident.
final class ComposerUITests: XCTestCase {
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

    /// A slow drag over half the workshop tools, so that momentum never skips a control.
    private func dragWorkshopTools(in app: XCUIApplication, towardsEnd: Bool) {
        let tools = app.scrollViews["workshop-tools"]
        let scroll = tools.exists ? tools : app.scrollViews.firstMatch
        let from = scroll.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: towardsEnd ? 0.75 : 0.25))
        let to = scroll.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: towardsEnd ? 0.25 : 0.75))
        from.press(forDuration: 0.05, thenDragTo: to)
    }

    /// Opens a new, empty project and the finger drawing sheet from its "1 · Parça ekle" section.
    /// Returns the drawing area of the sheet.
    private func openComposer(_ app: XCUIApplication,
                              file: StaticString = #filePath, line: UInt = #line) -> XCUIElement {
        let start = app.buttons["Yeni proje oluştur"]
        XCTAssertTrue(start.waitForExistence(timeout: 15), file: file, line: line)
        start.tap()
        XCTAssertTrue(app.staticTexts["Kalıbını oluştur"].waitForExistence(timeout: 10), file: file, line: line)

        let open = app.buttons["Parmakla şekil çiz"]
        XCTAssertTrue(open.waitForExistence(timeout: 10), file: file, line: line)
        for _ in 0..<10 {
            if open.isHittable { break }
            dragWorkshopTools(in: app, towardsEnd: true)
        }
        for _ in 0..<10 {
            if open.isHittable { break }
            dragWorkshopTools(in: app, towardsEnd: false)
        }
        XCTAssertTrue(open.isHittable, "The finger drawing button should be reachable", file: file, line: line)
        open.tap()

        let canvas = app.descendants(matching: .any)["composer-canvas"]
        XCTAssertTrue(canvas.waitForExistence(timeout: 10), "The drawing sheet should open", file: file, line: line)
        return canvas
    }

    /// Scrolls the sheet until `element` is fully on screen, below the navigation bar.
    private func reveal(_ element: XCUIElement, in app: XCUIApplication,
                        file: StaticString = #filePath, line: UInt = #line) {
        let composerScroll = app.scrollViews["composer-scroll"]
        let scroll = composerScroll.exists ? composerScroll : app.scrollViews.firstMatch
        let screen = app.windows.firstMatch.frame
        let top = screen.minY + 120, bottom = screen.maxY - 40
        for _ in 0..<10 {
            let frame = element.frame
            if element.isHittable && frame.minY >= top && frame.maxY <= bottom { return }
            let up = frame.maxY > bottom
            // Short steps: the slack around the drawing area is small on a 4.7-inch screen.
            let from = scroll.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: up ? 0.6 : 0.45))
            let to = scroll.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: up ? 0.45 : 0.6))
            from.press(forDuration: 0.05, thenDragTo: to)
        }
        XCTAssertTrue(element.isHittable, "Control should be reachable by scrolling the sheet",
                      file: file, line: line)
    }

    /// Taps inside the drawing square. The square is centred in the area with a margin of at
    /// least 34 pt on every side, so offsets between 0.35 and 0.65 always land inside it.
    private func tapCanvas(_ canvas: XCUIElement, _ dx: CGFloat, _ dy: CGFloat) {
        canvas.coordinate(withNormalizedOffset: CGVector(dx: dx, dy: dy)).tap()
    }

    private func expectCount(_ count: Int, in app: XCUIApplication,
                             file: StaticString = #filePath, line: UInt = #line) {
        let text = app.staticTexts["composer-point-count"]
        let expectation = XCTNSPredicateExpectation(
            predicate: NSPredicate(format: "label == %@", "\(count) nokta"), object: text)
        XCTAssertEqual(XCTWaiter().wait(for: [expectation], timeout: 5), .completed,
                       "Point count should read \"\(count) nokta\", reads \"\(text.label)\"",
                       file: file, line: line)
    }

    func testTapsAddPointsClearAllEmptiesAndCancelKeepsPoints() {
        let app = launchClean()
        let canvas = openComposer(app)
        reveal(canvas, in: app)

        tapCanvas(canvas, 0.4, 0.62)
        tapCanvas(canvas, 0.6, 0.62)
        tapCanvas(canvas, 0.5, 0.38)
        expectCount(3, in: app)

        let clear = app.buttons["Tümünü temizle"]
        reveal(clear, in: app)
        clear.tap()
        let confirmClear = app.buttons["Tüm noktaları sil"]
        XCTAssertTrue(confirmClear.waitForExistence(timeout: 5), "Clearing should ask first")
        confirmClear.tap()
        expectCount(0, in: app)
        XCTAssertFalse(clear.isEnabled, "Nothing left to clear")

        // With points on the sheet, Vazgeç asks; continuing keeps every point.
        reveal(canvas, in: app)
        tapCanvas(canvas, 0.4, 0.62)
        tapCanvas(canvas, 0.6, 0.62)
        expectCount(2, in: app)
        let cancel = app.navigationBars["Parmakla çiz"].buttons["Vazgeç"]
        XCTAssertTrue(cancel.exists)
        cancel.tap()
        let keepDrawing = app.buttons["Çizmeye devam et"]
        XCTAssertTrue(keepDrawing.waitForExistence(timeout: 5), "Vazgeç should ask before dropping points")
        keepDrawing.tap()
        XCTAssertTrue(canvas.waitForExistence(timeout: 5), "The sheet should stay open")
        expectCount(2, in: app)
    }

    func testSmallerAreaAsksBeforeDroppingPointsAndLargerAreaKeepsThem() {
        let app = launchClean()
        let canvas = openComposer(app)
        let area100 = app.buttons["composer-area-100"]
        let area300 = app.buttons["composer-area-300"]
        let area1000 = app.buttons["composer-area-1000"]
        XCTAssertTrue(area300.waitForExistence(timeout: 5))
        XCTAssertTrue(area300.isSelected, "300 mm is the default area")

        reveal(canvas, in: app)
        // The middle of the 300 mm square is 150, 150: outside a 100 mm area.
        tapCanvas(canvas, 0.5, 0.5)
        tapCanvas(canvas, 0.6, 0.62)
        expectCount(2, in: app)

        reveal(area100, in: app)
        area100.tap()
        let keepArea = app.buttons["Alanı değiştirme"]
        XCTAssertTrue(keepArea.waitForExistence(timeout: 5), "Points that do not fit must not vanish silently")
        keepArea.tap()
        expectCount(2, in: app)
        XCTAssertTrue(area300.isSelected)
        XCTAssertFalse(area100.isSelected)

        // Every point fits in a larger area, so it changes without a question.
        area1000.tap()
        XCTAssertFalse(app.buttons["Alanı değiştirme"].waitForExistence(timeout: 2))
        XCTAssertTrue(area1000.isSelected)
        expectCount(2, in: app)
    }

    func testFinishedShapeIsAddedToTheDrawing() {
        let app = launchClean()
        let canvas = openComposer(app)
        reveal(canvas, in: app)
        tapCanvas(canvas, 0.4, 0.62)
        tapCanvas(canvas, 0.6, 0.62)
        tapCanvas(canvas, 0.5, 0.38)
        expectCount(3, in: app)

        let add = app.buttons["Şekli çizime ekle"]
        reveal(add, in: app)
        XCTAssertTrue(add.isEnabled)
        add.tap()
        XCTAssertTrue(app.staticTexts["1 parça • Önizleme ölçüye göre ekrana sığdırılır"].waitForExistence(timeout: 10),
                      "The new piece should appear in the workshop")
    }
}
