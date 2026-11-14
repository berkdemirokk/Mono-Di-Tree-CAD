import XCTest

final class ViewerUITests: XCTestCase {
    private func launchClean() -> XCUIApplication {
        let app = XCUIApplication()
        app.launchArguments += ["-AppleLanguages", "(en)", "-AppleLocale", "en_US", "--ui-test-reset-draft"]
        app.launch()
        return app
    }

    private func countLabel(_ count: Int, in app: XCUIApplication) -> XCUIElement {
        app.staticTexts["\(count) parça • Önizleme ölçüye göre ekrana sığdırılır"]
    }

    private func tapBelow(_ button: XCUIElement, in app: XCUIApplication,
                          file: StaticString = #filePath, line: UInt = #line) {
        for _ in 0..<8 {
            if button.exists && button.isHittable { break }
            app.scrollViews.firstMatch.swipeUp()
        }
        XCTAssertTrue(button.isHittable, "Control should be reachable by scrolling", file: file, line: line)
        button.tap()
    }

    func testBeginnerCanCreateDuplicateUndoAndRedo() {
        let app = launchClean()
        let start = app.buttons["Yeni proje oluştur"]
        XCTAssertTrue(start.waitForExistence(timeout: 15))
        start.tap()
        XCTAssertTrue(app.staticTexts["Kalıbını oluştur"].waitForExistence(timeout: 10))
        app.buttons["Dikdörtgen ekle"].tap()
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 5))
        XCTAssertTrue(app.buttons["Ölçüleri, konumu ve adı uygula"].exists)
        tapBelow(app.buttons["Kopyala"], in: app)
        XCTAssertTrue(countLabel(2, in: app).exists)
        tapBelow(app.buttons["Geri al"], in: app)
        XCTAssertTrue(countLabel(1, in: app).exists)
        tapBelow(app.buttons["İleri al"], in: app)
        XCTAssertTrue(countLabel(2, in: app).exists)
    }

    func testExampleLetsNewUsersTryRealTools() {
        let app = XCUIApplication()
        app.launchArguments += ["-AppleLanguages", "(en)", "-AppleLocale", "en_US", "--ui-test-reset-draft"]
        app.launch()

        let demo = app.buttons["Örnekle dene"]
        XCTAssertTrue(demo.waitForExistence(timeout: 15))
        demo.tap()
        XCTAssertTrue(app.staticTexts["2 parça • Önizleme ölçüye göre ekrana sığdırılır"].waitForExistence(timeout: 10))
        XCTAssertTrue(app.buttons["Dikdörtgen ekle"].exists)
    }

    func testDraftRecoversAfterAppTerminationWithoutClosingEditor() {
        let app = launchClean()
        app.buttons["Yeni proje oluştur"].tap()
        app.buttons["Dikdörtgen ekle"].tap()
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 10))
        app.terminate()

        app.launchArguments.removeAll { $0 == "--ui-test-reset-draft" }
        app.launch()
        let resume = app.buttons["Son çizime devam et"]
        XCTAssertTrue(resume.waitForExistence(timeout: 10))
        resume.tap()
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 10))
    }

    func testSavedProjectReopensFromRecentsAfterRelaunch() {
        let app = launchClean()
        app.buttons["Örnekle dene"].tap()
        XCTAssertTrue(countLabel(2, in: app).waitForExistence(timeout: 10))
        tapBelow(app.buttons["Projeyi kaydet"], in: app)
        app.buttons["Kapat"].tap()
        let row = app.buttons.matching(NSPredicate(format: "identifier BEGINSWITH %@", "recent-")).firstMatch
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        XCTAssertTrue(row.label.contains("Örnek kalıp"))
        app.terminate()

        app.launchArguments.removeAll { $0 == "--ui-test-reset-draft" }
        app.launch()
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        row.tap()
        XCTAssertTrue(countLabel(2, in: app).waitForExistence(timeout: 10))
        app.buttons["Çizgi ekle"].tap()
        XCTAssertTrue(countLabel(3, in: app).exists)
    }

    func testClosingEditorPersistsTypedProjectNameAndArchivesItOnSwitch() {
        let app = launchClean()
        app.buttons["Yeni proje oluştur"].tap()
        app.buttons["Dikdörtgen ekle"].tap()
        let name = app.textFields["Proje adı"]
        tapBelow(name, in: app)
        name.typeText(" 42")
        let editedName = name.value as? String
        XCTAssertTrue(editedName?.contains("42") == true)
        app.buttons["Kapat"].tap()

        app.buttons["Örnekle dene"].tap()
        XCTAssertTrue(countLabel(2, in: app).waitForExistence(timeout: 10))
        app.buttons["Kapat"].tap()
        let archived = app.buttons.matching(NSPredicate(format: "identifier BEGINSWITH %@", "recent-")).firstMatch
        XCTAssertTrue(archived.waitForExistence(timeout: 10))
        XCTAssertTrue(archived.label.contains(editedName ?? "42"))
        archived.tap()
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 10))
    }
    func testLaunchAndCancelFilePicker() {
        let app = XCUIApplication()
        app.launchArguments += ["-AppleLanguages", "(en)", "-AppleLocale", "en_US"]
        app.launch()

        let openButton = app.buttons["Dosya aç (.tcad)"]
        let homeTitle = app.staticTexts["Kalıbını kolayca oluştur"]
        XCTAssertTrue(openButton.waitForExistence(timeout: 15))
        XCTAssertTrue(homeTitle.exists)

        for _ in 0..<2 {
            openButton.tap()
            let cancel = app.buttons["Cancel"]
            XCTAssertTrue(cancel.waitForExistence(timeout: 10), "The system file picker should open")
            cancel.tap()
            XCTAssertTrue(openButton.waitForExistence(timeout: 10))
            XCTAssertTrue(homeTitle.exists, "Vazgeçince proje ana ekranına dönülmeli")
        }
    }

    func testMalformedBooleanAndNumericFieldsShowErrors() {
        let cases = [
            ("""
            {"Name":"Bozuk koordinat","Pieces":[{"Points":[{"X":true,"Y":0},{"X":1,"Y":1}],"Closed":false}]}
            """, "Geçersiz veya gösterilemeyen koordinat."),
            ("""
            {"Name":"Bozuk kontur","Pieces":[{"Points":[{"X":0,"Y":0},{"X":1,"Y":1}],"Closed":1}]}
            """, "Parça konturu eksik veya sınır dışında.")
        ]

        for (fixture, message) in cases {
            let app = XCUIApplication()
            app.launchArguments += ["--ui-test-fixture"]
            app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
            app.launch()

            let alert = app.alerts["Dosya açılamadı"]
            XCTAssertTrue(alert.waitForExistence(timeout: 15))
            XCTAssertTrue(alert.staticTexts[message].exists)
            alert.buttons["Tamam"].tap()
            XCTAssertTrue(app.staticTexts["Kalıbını kolayca oluştur"].exists)
            app.terminate()
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
        let details = app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS %@", "İplik yönü: Kontrol bekliyor")).firstMatch
        XCTAssertTrue(details.waitForExistence(timeout: 10))
        XCTAssertTrue(details.label.contains("Pamuk"))
        XCTAssertTrue(details.label.contains("Adet: 2"))
        let hideGrain = app.buttons["İplik yönlerini gizle"]
        XCTAssertTrue(hideGrain.waitForExistence(timeout: 10))
        hideGrain.tap()
        XCTAssertTrue(app.buttons["İplik yönlerini göster"].exists)
        selected.tap()
        XCTAssertTrue(app.buttons["Tüm parçalar"].waitForExistence(timeout: 10))
        app.buttons["Tüm parçalar"].tap()
        XCTAssertTrue(picker.waitForExistence(timeout: 10))
        XCTAssertTrue(app.descendants(matching: .any)["2 parçalı kontur önizlemesi"].exists)
    }
}
