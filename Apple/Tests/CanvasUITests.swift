import XCTest

/// The workshop drawing: its reference grid, panning while zoomed and the fit control.
final class CanvasUITests: XCTestCase {
    private let drawingLabel = "Dokunarak parça seçebileceğiniz kalıp önizlemesi"

    /// A new project with one 100 x 150 mm rectangle whose corner is at (0, 0).
    private func openWithRectangle() -> XCUIApplication {
        let app = XCUIApplication()
        app.launchArguments += ["-AppleLanguages", "(en)", "-AppleLocale", "en_US", "--ui-test-reset-draft"]
        app.launch()
        let start = app.buttons["Yeni proje oluştur"]
        XCTAssertTrue(start.waitForExistence(timeout: 15))
        start.tap()
        XCTAssertTrue(app.staticTexts["Kalıbını oluştur"].waitForExistence(timeout: 10))
        app.buttons["Dikdörtgen ekle"].tap()
        XCTAssertTrue(fitCaption(in: app).waitForExistence(timeout: 5))
        return app
    }

    private func fitCaption(in app: XCUIApplication) -> XCUIElement {
        app.staticTexts["1 parça • Önizleme ölçüye göre ekrana sığdırılır"]
    }

    private func drawing(in app: XCUIApplication) -> XCUIElement {
        app.descendants(matching: .any)[drawingLabel]
    }

    private func waitFor(_ element: XCUIElement, _ format: String, _ argument: String,
                         timeout: TimeInterval = 5) -> Bool {
        let expectation = XCTNSPredicateExpectation(predicate: NSPredicate(format: format, argument),
                                                    object: element)
        return XCTWaiter().wait(for: [expectation], timeout: timeout) == .completed
    }

    private func zoomInTwice(_ app: XCUIApplication) {
        let zoomIn = app.buttons["Yakınlaştır"]
        XCTAssertTrue(zoomIn.waitForExistence(timeout: 5))
        zoomIn.tap()
        zoomIn.tap()
        XCTAssertTrue(app.staticTexts["1 parça • 2.6× yakınlaştırıldı"].waitForExistence(timeout: 5))
    }

    /// The drag rule is explained once per installation; a later run may not show it.
    private func dismissPanHintIfShown(_ app: XCUIApplication) {
        let ok = app.buttons["canvas-pan-hint-ok"]
        guard ok.waitForExistence(timeout: 2) else { return }
        ok.tap()
        let gone = XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: ok)
        XCTAssertEqual(XCTWaiter().wait(for: [gone], timeout: 5), .completed, "Anladım should close the hint")
    }

    /// A slow one-finger drag across the middle of the drawing.
    private func drag(_ element: XCUIElement, from startX: CGFloat, to endX: CGFloat) {
        let from = element.coordinate(withNormalizedOffset: CGVector(dx: startX, dy: 0.5))
        let to = element.coordinate(withNormalizedOffset: CGVector(dx: endX, dy: 0.5))
        from.press(forDuration: 0.1, thenDragTo: to)
    }

    func testDrawingDescribesItsGridAndWhereXAndYStart() {
        let app = openWithRectangle()
        let canvas = drawing(in: app)
        XCTAssertTrue(canvas.waitForExistence(timeout: 5))
        // The rectangle starts at (0, 0), so at fit the origin marker is on screen.
        XCTAssertTrue(waitFor(canvas, "value CONTAINS %@", "Başlangıç noktası (0, 0) ekranda"),
                      String(describing: canvas.value))
        XCTAssertTrue(waitFor(canvas, "value CONTAINS %@", "X sağa, Y yukarı doğru artar"))
        XCTAssertTrue(waitFor(canvas, "value CONTAINS %@", "her kare"))

        // Zoomed in about the middle, the corner at (0, 0) leaves the small drawing area.
        zoomInTwice(app)
        dismissPanHintIfShown(app)
        XCTAssertTrue(waitFor(canvas, "value CONTAINS %@", "şu an ekranda değil"),
                      String(describing: canvas.value))
    }

    func testDraggingTheSelectedPieceWhileZoomedMovesTheViewNotThePiece() {
        let app = openWithRectangle()
        let canvas = drawing(in: app)
        XCTAssertTrue(canvas.waitForExistence(timeout: 5))
        zoomInTwice(app)
        dismissPanHintIfShown(app)
        // The selected rectangle now fills the middle of the drawing; there is no empty paper left
        // under the finger. Dragging its inside pans the view.
        drag(canvas, from: 0.35, to: 0.65)
        XCTAssertTrue(waitFor(canvas, "value CONTAINS %@", "Görünüm kaydırılmış"),
                      String(describing: canvas.value))
        XCTAssertTrue(app.staticTexts["1 parça • 2.6× yakınlaştırıldı"].exists)
        // The piece itself did not move.
        XCTAssertEqual(app.textFields["X"].value as? String, "0")
        XCTAssertEqual(app.textFields["Y"].value as? String, "0")
    }

    func testDraggingThePieceAtFitStillMovesIt() {
        let app = openWithRectangle()
        let canvas = drawing(in: app)
        XCTAssertTrue(canvas.waitForExistence(timeout: 5))
        let x = app.textFields["X"]
        XCTAssertTrue(waitFor(x, "value == %@", "0"))
        // At fit the middle of the drawing is inside the rectangle; dragging it to the right moves it.
        drag(canvas, from: 0.5, to: 0.7)
        XCTAssertTrue(waitFor(x, "value != %@", "0"), "Dragging the piece at fit should change X")
        XCTAssertTrue(fitCaption(in: app).exists, "Moving a piece must not zoom or pan")
    }

    func testFitControlBringsBackTheWholeDrawingAndKeepsZoomInReachable() {
        let app = openWithRectangle()
        let canvas = drawing(in: app)
        XCTAssertTrue(canvas.waitForExistence(timeout: 5))
        XCTAssertFalse(app.buttons["Ekrana sığdır"].exists, "Fit is only offered once zoomed")
        zoomInTwice(app)
        dismissPanHintIfShown(app)
        drag(canvas, from: 0.35, to: 0.6)
        XCTAssertTrue(waitFor(canvas, "value CONTAINS %@", "Görünüm kaydırılmış"))

        let fit = app.buttons["zoom-fit"]
        XCTAssertTrue(fit.isHittable)
        fit.tap()
        XCTAssertTrue(fitCaption(in: app).waitForExistence(timeout: 5))
        XCTAssertTrue(waitFor(canvas, "value CONTAINS %@", "Ekrana sığdırılmış"))
        XCTAssertFalse((canvas.value as? String ?? "").contains("kaydırılmış"), "Fit clears the pan")
        XCTAssertFalse(app.buttons["Ekrana sığdır"].exists)
        // VoiceOver focus moves to Yakınlaştır when the fit button goes away; it must be there.
        let zoomIn = app.buttons["zoom-in"]
        XCTAssertTrue(zoomIn.isHittable)
        XCTAssertTrue(zoomIn.isEnabled)
        XCTAssertFalse(app.buttons["zoom-out"].isEnabled)
    }
}
