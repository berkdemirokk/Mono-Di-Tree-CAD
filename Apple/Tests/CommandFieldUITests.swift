import XCTest

/// The command field of the workshop: type a sentence, read what would happen, confirm, undo.
/// Typed with ASCII only (the parser folds Turkish letters), so the tests do not depend on the
/// simulator's keyboard layout. Controls are found by accessibility identifier because their
/// visible Turkish labels also occur elsewhere on the screen ("Vazgeç", "Uygula").
final class CommandFieldUITests: XCTestCase {
    private func launchClean() -> XCUIApplication {
        let app = XCUIApplication()
        app.launchArguments += ["-AppleLanguages", "(en)", "-AppleLocale", "en_US", "--ui-test-reset-draft"]
        app.launch()
        return app
    }

    private func openWorkshop(_ app: XCUIApplication) {
        let start = app.buttons["Yeni proje oluştur"]
        XCTAssertTrue(start.waitForExistence(timeout: 15))
        start.tap()
        XCTAssertTrue(app.staticTexts["Kalıbını oluştur"].waitForExistence(timeout: 10))
    }

    private func countLabel(_ count: Int, in app: XCUIApplication) -> XCUIElement {
        app.staticTexts["\(count) parça • Tümü ekranda"]
    }

    private func text(startingWith prefix: String, in app: XCUIApplication) -> XCUIElement {
        app.staticTexts.matching(NSPredicate(format: "label BEGINSWITH %@", prefix)).firstMatch
    }

    private func text(containing part: String, in app: XCUIApplication) -> XCUIElement {
        app.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", part)).firstMatch
    }

    /// Types into the command field. `submit` presses Return, which only prepares.
    @discardableResult
    private func enter(_ command: String, in app: XCUIApplication, submit: Bool = false) -> XCUIElement {
        let field = app.textFields["command-field"]
        XCTAssertTrue(field.waitForExistence(timeout: 10), "The command field should be on the workshop screen")
        field.tap()
        field.typeText(command + (submit ? "\n" : ""))
        return field
    }

    private func prepare(in app: XCUIApplication) {
        let button = app.buttons["command-prepare"]
        XCTAssertTrue(button.waitForExistence(timeout: 5))
        button.tap()
    }

    private func toolsScroll(in app: XCUIApplication) -> XCUIElement {
        let tools = app.scrollViews["workshop-tools"]
        return tools.exists ? tools : app.scrollViews.firstMatch
    }

    private func dragTools(in app: XCUIApplication, towardsEnd: Bool) {
        let scroll = toolsScroll(in: app)
        let from = scroll.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: towardsEnd ? 0.75 : 0.25))
        let to = scroll.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: towardsEnd ? 0.25 : 0.75))
        from.press(forDuration: 0.05, thenDragTo: to)
    }

    private func tapBelow(_ element: XCUIElement, in app: XCUIApplication,
                          file: StaticString = #filePath, line: UInt = #line) {
        for _ in 0..<14 {
            if element.exists && element.isHittable { break }
            dragTools(in: app, towardsEnd: true)
        }
        for _ in 0..<14 {
            if element.exists && element.isHittable { break }
            dragTools(in: app, towardsEnd: false)
        }
        XCTAssertTrue(element.isHittable, "Control should be reachable by scrolling", file: file, line: line)
        element.tap()
    }

    // MARK: Preview, confirm, undo

    func testSkirtModelCanBePreviewedInsertedAndUndoneFromTheCommandField() {
        let app = launchClean()
        openWorkshop(app)
        enter("etek modeli ekle", in: app, submit: true)
        XCTAssertTrue(text(startingWith: "Etek modelinden 3 parça eklenecek", in: app).waitForExistence(timeout: 5))
        XCTAssertFalse(countLabel(3, in: app).exists)
        tapBelow(app.buttons["command-apply"], in: app)
        XCTAssertTrue(countLabel(3, in: app).waitForExistence(timeout: 5))
        tapBelow(app.buttons["command-undo"], in: app)
        XCTAssertTrue(app.staticTexts["Kalıbını oluştur"].waitForExistence(timeout: 5))
    }

    func testACommandIsShownBeforeItChangesAnythingAndCanBeUndone() {
        let app = launchClean()
        openWorkshop(app)
        enter("120 x 80 dikdortgen", in: app)
        prepare(in: app)

        let sentence = "Dikdörtgen çizilecek: 120 × 80 mm (12 × 8 cm). Sol üst köşe: 0 mm, 0 mm."
        XCTAssertTrue(app.staticTexts[sentence].waitForExistence(timeout: 5), "One line says what will happen")
        XCTAssertFalse(countLabel(1, in: app).exists, "Nothing is drawn before the user confirms")
        XCTAssertTrue(app.staticTexts["Kalıbını oluştur"].exists)

        app.buttons["command-apply"].tap()
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 5))
        XCTAssertTrue(app.staticTexts["command-result"].waitForExistence(timeout: 5))
        XCTAssertTrue(text(startingWith: "Yapıldı: Dikdörtgen eklendi: 120 × 80 mm", in: app).exists)

        app.buttons["command-undo"].tap()
        XCTAssertTrue(app.staticTexts["Kalıbını oluştur"].waitForExistence(timeout: 5))
        XCTAssertFalse(countLabel(1, in: app).exists)
    }

    func testTypedConfirmationAppliesOnceAndCanUndoByCommand() {
        let app = launchClean()
        openWorkshop(app)
        enter("etek modeli ekle", in: app, submit: true)
        XCTAssertTrue(app.buttons["command-apply"].waitForExistence(timeout: 5))
        enter("uygula", in: app, submit: true)
        XCTAssertTrue(countLabel(3, in: app).waitForExistence(timeout: 5))
        XCTAssertFalse(app.buttons["command-apply"].exists)
        enter("geri al", in: app, submit: true)
        XCTAssertTrue(app.buttons["command-apply"].waitForExistence(timeout: 5))
        enter("uygula", in: app, submit: true)
        XCTAssertTrue(app.staticTexts["Kalıbını oluştur"].waitForExistence(timeout: 5))
    }

    func testReturnOnlyPreparesAndNeverApplies() {
        let app = launchClean()
        openWorkshop(app)
        enter("kare 100", in: app, submit: true)
        XCTAssertTrue(text(startingWith: "Kare çizilecek: 100 × 100 mm (10 × 10 cm).", in: app).waitForExistence(timeout: 5))
        XCTAssertFalse(countLabel(1, in: app).exists, "The first Return must not apply the command")
        XCTAssertTrue(app.buttons["command-apply"].exists)
        app.buttons["command-cancel"].tap()
        XCTAssertFalse(countLabel(1, in: app).exists)
        XCTAssertTrue(app.staticTexts["Kalıbını oluştur"].exists)
    }

    func testMovingTheSelectedPieceThroughTheCommandField() {
        let app = launchClean()
        openWorkshop(app)
        app.buttons["Dikdörtgen ekle"].tap()
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 5))
        enter("25 saga tasi", in: app)
        prepare(in: app)
        let sentence = text(startingWith: "‘Dikdörtgen 1’ 25 mm sağa taşınacak (2,5 cm). Sol üst köşe 0, 0 mm iken 25, 0 mm olacak.", in: app)
        XCTAssertTrue(sentence.waitForExistence(timeout: 5))
        app.buttons["command-apply"].tap()
        XCTAssertTrue(text(startingWith: "Yapıldı: ‘Dikdörtgen 1’ taşındı: 25 mm sağa.", in: app).waitForExistence(timeout: 5))
        // The X field shows the new position.
        let x = app.textFields["X"]
        for _ in 0..<14 where !(x.exists && x.isHittable) { dragTools(in: app, towardsEnd: true) }
        XCTAssertEqual(x.value as? String, "25")
    }

    func testUndoByCommandGoesThroughTheSameHistoryAsTheButton() {
        let app = launchClean()
        openWorkshop(app)
        app.buttons["Dikdörtgen ekle"].tap()
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 5))
        enter("geri al", in: app)
        prepare(in: app)
        XCTAssertTrue(app.staticTexts["Son işlem geri alınacak."].waitForExistence(timeout: 5))
        XCTAssertTrue(countLabel(1, in: app).exists)
        app.buttons["command-apply"].tap()
        XCTAssertTrue(app.staticTexts["Kalıbını oluştur"].waitForExistence(timeout: 5))
        XCTAssertTrue(app.buttons["İleri al"].isEnabled, "The undone step can be redone with the toolbar button")
    }

    // MARK: Destructive, refused, ambiguous

    func testDeleteAlwaysAsksAndCancelKeepsThePiece() {
        let app = launchClean()
        openWorkshop(app)
        app.buttons["Dikdörtgen ekle"].tap()
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 5))
        enter("sil", in: app, submit: true)
        XCTAssertTrue(text(startingWith: "‘Dikdörtgen 1’ silinecek (100 × 150 mm). Geri alabilirsiniz.", in: app)
            .waitForExistence(timeout: 5))
        XCTAssertTrue(app.buttons["Evet, parçayı sil"].exists, "Deleting needs its own explicit button")
        app.buttons["command-cancel"].tap()
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 5))

        // Prepare again and confirm.
        prepare(in: app)
        app.buttons["Evet, parçayı sil"].tap()
        XCTAssertTrue(app.staticTexts["Kalıbını oluştur"].waitForExistence(timeout: 5))
    }

    func testAnUnknownSentenceIsRefusedWithExamplesAndChangesNothing() {
        let app = launchClean()
        openWorkshop(app)
        enter("zzz", in: app)
        prepare(in: app)
        XCTAssertTrue(text(startingWith: "“zzz” anlaşılamadı.", in: app).waitForExistence(timeout: 5))
        XCTAssertFalse(app.alerts.firstMatch.exists, "A refusal is shown in the card, not in an alert")
        // An example fills the field and shows its preview; it is not applied.
        let example = app.buttons["120 x 80 dikdörtgen"]
        XCTAssertTrue(example.waitForExistence(timeout: 5))
        example.tap()
        XCTAssertTrue(text(startingWith: "Dikdörtgen çizilecek: 120 × 80 mm", in: app).waitForExistence(timeout: 5))
        XCTAssertFalse(countLabel(1, in: app).exists)
    }

    func testAskedUnitIsAnsweredByTappingAButton() {
        let app = launchClean()
        openWorkshop(app)
        enter("12 x 8 dikdortgen", in: app)
        prepare(in: app)
        XCTAssertTrue(text(containing: "milimetre mi, santimetre mi?", in: app).waitForExistence(timeout: 5))
        app.buttons["12 x 8 cm dikdortgen"].tap()
        XCTAssertTrue(text(startingWith: "Dikdörtgen çizilecek: 120 × 80 mm (12 × 8 cm).", in: app).waitForExistence(timeout: 5))
        XCTAssertFalse(countLabel(1, in: app).exists)
    }

    func testAThingOnlyWindowsDoesIsSaidPlainly() {
        let app = launchClean()
        openWorkshop(app)
        enter("dxf al", in: app, submit: true)
        XCTAssertTrue(text(containing: "yalnız Windows programında var", in: app).waitForExistence(timeout: 5))
    }

    // MARK: Fields typed but not applied

    func testAnInvalidFieldRefusesTheCommandWithItsReason() {
        let app = launchClean()
        openWorkshop(app)
        app.buttons["Dikdörtgen ekle"].tap()
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 5))
        let width = app.textFields["En"]
        tapBelow(width, in: app)
        width.typeText("x")
        let done = app.keyboards.buttons["Done"]
        XCTAssertTrue(done.waitForExistence(timeout: 5))
        done.tap()

        enter("25 saga tasi", in: app, submit: true)
        XCTAssertTrue(text(containing: "Komut, alanlardaki değerlere dayanır", in: app).waitForExistence(timeout: 5),
                      "The command names the field problem instead of silently depending on it")
        XCTAssertFalse(app.alerts["İşlem tamamlanamadı"].exists)
        XCTAssertFalse(app.buttons["command-apply"].exists)
    }

    // MARK: Layout

    func testTheFieldSitsUnderTheDrawingAndAboveTheKeyboard() {
        let app = launchClean()
        openWorkshop(app)
        app.buttons["Dikdörtgen ekle"].tap()
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 5))
        let drawing = app.descendants(matching: .any)["Dokunarak parça seçebileceğin kalıp çizimi"]
        XCTAssertTrue(drawing.waitForExistence(timeout: 5))
        let field = app.textFields["command-field"]
        XCTAssertTrue(field.waitForExistence(timeout: 5))
        XCTAssertGreaterThanOrEqual(field.frame.height, 44, "The field is a full-size touch target")
        field.tap()
        let keyboard = app.keyboards.firstMatch
        XCTAssertTrue(keyboard.waitForExistence(timeout: 5))
        sleep(2)
        XCTAssertGreaterThan(drawing.frame.height, 80, "The drawing stays visible while a command is typed")
        XCTAssertLessThanOrEqual(drawing.frame.maxY, field.frame.minY)
        XCTAssertLessThanOrEqual(field.frame.maxY, keyboard.frame.minY)
        XCTAssertTrue(app.buttons["Geri al"].isHittable)
        field.typeText("25 saga")
        let prepare = app.buttons["command-prepare"]
        XCTAssertTrue(prepare.waitForExistence(timeout: 5))
        XCTAssertGreaterThanOrEqual(prepare.frame.height, 44)
        XCTAssertGreaterThanOrEqual(prepare.frame.width, 44)
    }

    func testTheBarStepsAsideWhenAnotherFieldHasTheKeyboard() {
        let app = launchClean()
        openWorkshop(app)
        app.buttons["Dikdörtgen ekle"].tap()
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 5))
        XCTAssertTrue(app.textFields["command-field"].waitForExistence(timeout: 5))
        let width = app.textFields["En"]
        tapBelow(width, in: app)
        XCTAssertTrue(app.keyboards.firstMatch.waitForExistence(timeout: 5))
        sleep(1)
        XCTAssertFalse(app.textFields["command-field"].exists,
                       "While a millimetre field is being typed into, the command bar leaves its room to it")
    }
}
