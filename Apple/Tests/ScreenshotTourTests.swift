import XCTest

/// Walks the main screens and saves a screenshot of each for visual review.
///
/// These tours check only that each screen can be reached; they make no claim about layout
/// quality. Every screenshot is attached to the test result and, when `SCREENSHOT_DIR` is set
/// (CI passes it as `TEST_RUNNER_SCREENSHOT_DIR`), is also written there as a PNG so the
/// workflow can upload the folder as an artifact.
final class ScreenshotTourTests: XCTestCase {
    override func setUp() {
        super.setUp()
        continueAfterFailure = true
    }

    override func tearDown() {
        XCUIDevice.shared.orientation = .portrait
        XCUIDevice.shared.appearance = .light
        super.tearDown()
    }

    private func launch(_ extra: [String] = [], environment: [String: String] = [:]) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchArguments += ["-AppleLanguages", "(en)", "-AppleLocale", "en_US"] + extra
        for (key, value) in environment { app.launchEnvironment[key] = value }
        app.launch()
        return app
    }

    private func snap(_ name: String) {
        let screenshot = XCUIScreen.main.screenshot()
        let attachment = XCTAttachment(screenshot: screenshot)
        attachment.name = name
        attachment.lifetime = .keepAlways
        add(attachment)
        guard let directory = ProcessInfo.processInfo.environment["SCREENSHOT_DIR"], !directory.isEmpty else { return }
        let folder = URL(fileURLWithPath: directory, isDirectory: true)
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        try? screenshot.pngRepresentation.write(to: folder.appendingPathComponent(name + ".png"))
    }

    /// Scrolls until the control can be tapped, then taps it. Returns false when it never appears.
    private func reachAndTap(_ element: XCUIElement, in app: XCUIApplication) -> Bool {
        guard element.waitForExistence(timeout: 10) else { return false }
        for _ in 0..<14 {
            if element.isHittable { break }
            dragTools(in: app)
        }
        guard element.isHittable else { return false }
        element.tap()
        return true
    }

    /// Scrolls the workshop's tools (or the first scroll view elsewhere) by half a viewport, slowly,
    /// so that nothing is skipped by momentum.
    private func dragTools(in app: XCUIApplication) {
        let tools = app.scrollViews["workshop-tools"]
        let scroll = tools.exists ? tools : app.scrollViews.firstMatch
        let from = scroll.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.75))
        let to = scroll.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.25))
        from.press(forDuration: 0.05, thenDragTo: to)
    }

    private func openEditorWithRectangle(_ app: XCUIApplication) -> Bool {
        guard reachAndTap(app.buttons["Yeni proje oluştur"], in: app) else { return false }
        guard app.staticTexts["Kalıbını oluştur"].waitForExistence(timeout: 10) else { return false }
        return true
    }

    func testTour01HomeAndEditor() {
        let app = launch(["--ui-test-reset-draft"])
        XCTAssertTrue(app.buttons["Yeni proje oluştur"].waitForExistence(timeout: 15))
        snap("01-home")

        XCTAssertTrue(openEditorWithRectangle(app), "The editor should open from the home screen")
        snap("02-editor-empty")

        XCTAssertTrue(reachAndTap(app.buttons["Dikdörtgen ekle"], in: app))
        snap("03-editor-one-piece")

        // The drawing should stay on screen while a dimension is being typed.
        if reachAndTap(app.textFields["En"], in: app) {
            // Wait for the keyboard and the shrinking drawing to settle: one second caught the
            // layout half way through its animation on the CI simulator.
            sleep(4)
            snap("03b-editor-typing-dimension")
            let done = app.keyboards.buttons["Done"]
            if done.exists { done.tap() }
        }

        for step in 1...6 {
            dragTools(in: app)
            snap("04-editor-scrolled-\(step)")
        }

        XCUIDevice.shared.orientation = .landscapeLeft
        sleep(1)
        snap("08-editor-landscape")
    }

    func testTour02ExampleSaveAndRecents() {
        let app = launch(["--ui-test-reset-draft"])
        XCTAssertTrue(reachAndTap(app.buttons["Örnekle dene"], in: app))
        XCTAssertTrue(app.staticTexts["2 parça • Önizleme ölçüye göre ekrana sığdırılır"].waitForExistence(timeout: 10))
        snap("09-example-two-pieces")

        XCTAssertTrue(reachAndTap(app.buttons["Projeyi kaydet"], in: app))
        snap("10-after-save")

        XCTAssertTrue(reachAndTap(app.buttons["Kapat"], in: app))
        let recent = app.buttons.matching(NSPredicate(format: "identifier BEGINSWITH %@", "recent-")).firstMatch
        XCTAssertTrue(recent.waitForExistence(timeout: 10))
        snap("11-home-with-recent-project")
    }

    func testTour03FactoryFileViewer() throws {
        let fixture = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "selection", withExtension: "tcad"))
        let data = try Data(contentsOf: fixture)
        let app = launch(["--ui-test-fixture"], environment: ["TCAD_UI_TEST_FIXTURE": data.base64EncodedString()])
        XCTAssertTrue(app.staticTexts["Seçim Testi"].waitForExistence(timeout: 15))
        snap("12-viewer-all-pieces")

        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        XCTAssertTrue(reachAndTap(picker, in: app))
        XCTAssertTrue(app.buttons["Arka Beden"].waitForExistence(timeout: 10))
        snap("13-viewer-piece-picker")

        app.buttons["Arka Beden"].tap()
        let details = app.descendants(matching: .any)
            .matching(NSPredicate(format: "label CONTAINS %@", "İplik yönü")).firstMatch
        _ = details.waitForExistence(timeout: 10)
        snap("14-viewer-piece-selected")

        XCUIDevice.shared.orientation = .landscapeLeft
        sleep(1)
        snap("15-viewer-landscape")
    }

    func testTour04RejectedFile() {
        let malformed = """
        {"Name":"Bozuk koordinat","Pieces":[{"Points":[{"X":true,"Y":0},{"X":1,"Y":1}],"Closed":false}]}
        """
        let app = launch(["--ui-test-fixture"],
                         environment: ["TCAD_UI_TEST_FIXTURE": Data(malformed.utf8).base64EncodedString()])
        XCTAssertTrue(app.alerts["Dosya açılamadı"].waitForExistence(timeout: 15))
        snap("16-rejected-file-alert")
    }

    func testTour05LargeText() {
        let app = launch(["--ui-test-reset-draft",
                          "-UIPreferredContentSizeCategoryName", "UICTContentSizeCategoryAccessibilityXL"])
        XCTAssertTrue(app.buttons["Yeni proje oluştur"].waitForExistence(timeout: 15))
        snap("17-home-large-text")

        XCTAssertTrue(openEditorWithRectangle(app))
        XCTAssertTrue(reachAndTap(app.buttons["Dikdörtgen ekle"], in: app))
        snap("18-editor-large-text")
        dragTools(in: app)
        snap("19-editor-large-text-scrolled")
    }

    /// The app is deliberately light-only: with the phone in dark mode the screens must stay light.
    func testTour06SystemDarkStaysLight() {
        XCUIDevice.shared.appearance = .dark
        let app = launch(["--ui-test-reset-draft"])
        XCTAssertTrue(app.buttons["Yeni proje oluştur"].waitForExistence(timeout: 15))
        snap("20-home-system-dark-app-light")

        XCTAssertTrue(openEditorWithRectangle(app))
        XCTAssertTrue(reachAndTap(app.buttons["Dikdörtgen ekle"], in: app))
        snap("21-editor-system-dark-app-light")
    }
}
