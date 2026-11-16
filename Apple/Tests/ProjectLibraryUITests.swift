import XCTest

/// The "Son projeler" list on the home screen: deleting (with a confirmation), renaming,
/// "Tümünü göster", unreadable files, and the line that says where a replaced drawing went.
/// `--ui-test-seed-projects` saves twelve projects "Deneme 01" ... "Deneme 12", newest last.
final class ProjectLibraryUITests: XCTestCase {
    override func setUp() {
        super.setUp()
        continueAfterFailure = false
    }

    private func launch(_ extra: [String] = []) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchArguments += ["-AppleLanguages", "(en)", "-AppleLocale", "en_US", "--ui-test-reset-draft"]
        app.launchArguments += extra
        app.launch()
        return app
    }

    private func row(_ name: String, in app: XCUIApplication) -> XCUIElement {
        app.buttons.matching(NSPredicate(format: "identifier BEGINSWITH %@ AND label == %@", "recent-", name)).firstMatch
    }

    private func actionsButton(_ name: String, in app: XCUIApplication) -> XCUIElement {
        app.buttons["\(name) için işlemler"]
    }

    /// The home screen scrolls; move it until the element can be tapped.
    private func reveal(_ element: XCUIElement, in app: XCUIApplication,
                        file: StaticString = #filePath, line: UInt = #line) {
        for _ in 0..<8 {
            if element.exists && element.isHittable { break }
            app.swipeUp()
        }
        XCTAssertTrue(element.isHittable, "Element should be reachable by scrolling", file: file, line: line)
    }

    private func waitUntilGone(_ element: XCUIElement, timeout: TimeInterval = 5) -> Bool {
        let gone = XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: element)
        return XCTWaiter().wait(for: [gone], timeout: timeout) == .completed
    }

    private func chooseAction(_ action: String, for name: String, in app: XCUIApplication) {
        let actions = actionsButton(name, in: app)
        reveal(actions, in: app)
        actions.tap()
        let item = app.buttons[action]
        XCTAssertTrue(item.waitForExistence(timeout: 5), "\(action) should be offered for \(name)")
        item.tap()
    }

    func testDeletingAProjectAsksFirstAndThenRemovesIt() {
        let app = launch(["--ui-test-seed-projects"])
        let target = row("Deneme 12", in: app)
        XCTAssertTrue(target.waitForExistence(timeout: 15))

        // Cancelling keeps the project.
        chooseAction("Sil", for: "Deneme 12", in: app)
        let confirm = app.alerts["Deneme 12 silinsin mi?"]
        XCTAssertTrue(confirm.waitForExistence(timeout: 5))
        confirm.buttons["Vazgeç"].tap()
        XCTAssertTrue(waitUntilGone(confirm))
        XCTAssertTrue(target.exists)

        // Confirming removes it, without any error.
        chooseAction("Sil", for: "Deneme 12", in: app)
        XCTAssertTrue(confirm.waitForExistence(timeout: 5))
        confirm.buttons["Sil"].tap()
        XCTAssertTrue(waitUntilGone(target, timeout: 10))
        XCTAssertFalse(app.alerts["Proje silinemedi"].waitForExistence(timeout: 2))
        XCTAssertTrue(row("Deneme 11", in: app).exists)
        XCTAssertTrue(app.buttons["Tümünü göster (11)"].waitForExistence(timeout: 5))
    }

    func testRenamingAProjectShowsTheNewNameInTheList() {
        let app = launch(["--ui-test-seed-projects"])
        XCTAssertTrue(row("Deneme 12", in: app).waitForExistence(timeout: 15))

        chooseAction("Adı değiştir", for: "Deneme 12", in: app)
        let prompt = app.alerts["Adı değiştir"]
        XCTAssertTrue(prompt.waitForExistence(timeout: 5))
        let field = prompt.textFields.firstMatch
        XCTAssertTrue(field.waitForExistence(timeout: 5))
        XCTAssertEqual(field.value as? String, "Deneme 12")
        field.tap()
        field.typeText(" yeni")
        prompt.buttons["Kaydet"].tap()

        XCTAssertTrue(row("Deneme 12 yeni", in: app).waitForExistence(timeout: 10))
        XCTAssertFalse(row("Deneme 12", in: app).exists)
        XCTAssertFalse(app.alerts["Ad değiştirilemedi"].exists)
    }

    func testAnEmptyNameIsRefusedUnderItsOwnTitle() {
        let app = launch(["--ui-test-seed-projects"])
        XCTAssertTrue(row("Deneme 12", in: app).waitForExistence(timeout: 15))

        chooseAction("Adı değiştir", for: "Deneme 12", in: app)
        let prompt = app.alerts["Adı değiştir"]
        XCTAssertTrue(prompt.waitForExistence(timeout: 5))
        let field = prompt.textFields.firstMatch
        XCTAssertTrue(field.waitForExistence(timeout: 5))
        field.tap()
        let current = (field.value as? String) ?? ""
        field.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: current.count + 2))
        prompt.buttons["Kaydet"].tap()

        // The failure follows the closed prompt; it is not filed under "Dosya açılamadı".
        let failure = app.alerts["Ad değiştirilemedi"]
        XCTAssertTrue(failure.waitForExistence(timeout: 5))
        XCTAssertFalse(app.alerts["Dosya açılamadı"].exists)
        failure.buttons["Tamam"].tap()
        XCTAssertTrue(waitUntilGone(failure))
        XCTAssertTrue(row("Deneme 12", in: app).exists)
    }

    func testShowAllRevealsOlderProjectsAndShowLessHidesThemAgain() {
        let app = launch(["--ui-test-seed-projects"])
        XCTAssertTrue(row("Deneme 12", in: app).waitForExistence(timeout: 15))
        // Ten newest projects are listed at first.
        XCTAssertTrue(row("Deneme 03", in: app).exists)
        XCTAssertFalse(row("Deneme 02", in: app).exists)
        XCTAssertFalse(row("Deneme 01", in: app).exists)

        let toggle = app.buttons["show-all-projects"]
        reveal(toggle, in: app)
        XCTAssertEqual(toggle.label, "Tümünü göster (12)")
        toggle.tap()
        XCTAssertTrue(row("Deneme 01", in: app).waitForExistence(timeout: 5))
        XCTAssertTrue(row("Deneme 02", in: app).exists)

        let less = app.buttons["Daha az göster"]
        reveal(less, in: app)
        less.tap()
        XCTAssertTrue(waitUntilGone(row("Deneme 01", in: app)))
        XCTAssertTrue(app.buttons["Tümünü göster (12)"].waitForExistence(timeout: 5))
    }

    func testUnreadableProjectIsMarkedAndCanOnlyBeDeleted() {
        let app = launch(["--ui-test-unreadable-project"])
        let marked = app.descendants(matching: .any)
            .matching(NSPredicate(format: "identifier BEGINSWITH %@", "unreadable-")).firstMatch
        XCTAssertTrue(marked.waitForExistence(timeout: 15))
        XCTAssertTrue(marked.label.contains("Okunamıyor"), marked.label)
        // It is not an "open" button.
        XCTAssertFalse(app.buttons.matching(NSPredicate(format: "identifier BEGINSWITH %@", "recent-")).firstMatch.exists)

        let actions = actionsButton("Okunamayan proje", in: app)
        reveal(actions, in: app)
        actions.tap()
        let delete = app.buttons["Sil"]
        XCTAssertTrue(delete.waitForExistence(timeout: 5))
        XCTAssertFalse(app.buttons["Adı değiştir"].exists)
        delete.tap()

        let confirm = app.alerts["Okunamayan proje silinsin mi?"]
        XCTAssertTrue(confirm.waitForExistence(timeout: 5))
        confirm.buttons["Sil"].tap()
        XCTAssertTrue(waitUntilGone(marked, timeout: 10))
        XCTAssertTrue(app.staticTexts["Kaydettiğin projeler burada görünür."].waitForExistence(timeout: 5))
    }

    func testResumeButtonNamesTheDrawingAndSwitchingSaysWhereItWent() {
        let app = launch()
        app.buttons["Yeni proje oluştur"].tap()
        XCTAssertTrue(app.staticTexts["Kalıbını oluştur"].waitForExistence(timeout: 10))
        app.buttons["Dikdörtgen ekle"].tap()
        XCTAssertTrue(app.staticTexts["1 parça • Önizleme ölçüye göre ekrana sığdırılır"].waitForExistence(timeout: 5))
        app.buttons["Kapat"].tap()

        let resume = app.buttons["Son çizime devam et"]
        XCTAssertTrue(resume.waitForExistence(timeout: 10))
        let detail = (resume.value as? String) ?? ""
        XCTAssertTrue(detail.contains("Yeni kalıp"), detail)
        XCTAssertTrue(detail.contains("1 parça"), detail)
        XCTAssertTrue(detail.contains("değiştirildi"), detail)

        // Starting the example puts the previous drawing into the library and says so.
        app.buttons["Örnekle dene"].tap()
        XCTAssertTrue(app.staticTexts["2 parça • Önizleme ölçüye göre ekrana sığdırılır"].waitForExistence(timeout: 10))
        app.buttons["Kapat"].tap()
        let notice = app.staticTexts["Önceki çizimin «Yeni kalıp» Son projeler'e kaydedildi."]
        XCTAssertTrue(notice.waitForExistence(timeout: 10))
        XCTAssertTrue(row("Yeni kalıp", in: app).exists)

        app.buttons["Bildirimi kapat"].tap()
        XCTAssertTrue(waitUntilGone(notice))
    }
}
