import XCTest

final class ViewerUITests: XCTestCase {
    private func launchClean() -> XCUIApplication {
        let app = XCUIApplication()
        app.launchArguments += ["-AppleLanguages", "(en)", "-AppleLocale", "en_US", "--ui-test-reset-draft"]
        app.launch()
        return app
    }

    private func countLabel(_ count: Int, in app: XCUIApplication) -> XCUIElement {
        app.staticTexts["\(count) parça • Tümü ekranda"]
    }

    /// The workshop's tools scroll under a pinned drawing. Scrolling must start inside that tools
    /// area: the first scroll view in the hierarchy can be the home screen under the sheet.
    private func toolsScroll(in app: XCUIApplication) -> XCUIElement {
        let tools = app.scrollViews["workshop-tools"]
        return tools.exists ? tools : app.scrollViews.firstMatch
    }

    /// A slow drag over half the viewport. A flick would carry momentum and could skip a control.
    private func dragTools(in app: XCUIApplication, towardsEnd: Bool) {
        let scroll = toolsScroll(in: app)
        let from = scroll.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: towardsEnd ? 0.75 : 0.25))
        let to = scroll.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: towardsEnd ? 0.25 : 0.75))
        from.press(forDuration: 0.05, thenDragTo: to)
    }

    private func tapBelow(_ button: XCUIElement, in app: XCUIApplication,
                          file: StaticString = #filePath, line: UInt = #line) {
        for _ in 0..<14 {
            if button.exists && button.isHittable { break }
            dragTools(in: app, towardsEnd: true)
        }
        // The control may sit above the current position, for example after an earlier scroll.
        for _ in 0..<14 {
            if button.exists && button.isHittable { break }
            dragTools(in: app, towardsEnd: false)
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
        XCTAssertTrue(app.staticTexts["2 parça • Tümü ekranda"].waitForExistence(timeout: 10))
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

    func testCopyAppliesTypedPieceNameBeforeCreatingDuplicate() {
        let app = launchClean()
        app.buttons["Örnekle dene"].tap()
        let name = app.textFields["Seçili parçanın adı"]
        tapBelow(name, in: app)
        name.typeText(" 27")
        // The on-screen keyboard covers lower editing controls on an iPhone.
        // Dismiss it through the real Done action before reaching Copy.
        let done = app.keyboards.buttons["Done"]
        XCTAssertTrue(done.waitForExistence(timeout: 5))
        done.tap()
        guard let source = name.value as? String, source.contains("27") else {
            return XCTFail("Parça adı alanda görünmeli.")
        }
        tapBelow(app.buttons["Kopyala"], in: app)
        XCTAssertEqual(name.value as? String, source + " kopya")
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
    func testDrawingAndUndoStayInReachWhileTypingADimension() {
        let app = launchClean()
        app.buttons["Yeni proje oluştur"].tap()
        XCTAssertTrue(app.staticTexts["Kalıbını oluştur"].waitForExistence(timeout: 10))
        app.buttons["Dikdörtgen ekle"].tap()
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 5))

        let drawing = app.descendants(matching: .any)["Dokunarak parça seçebileceğin kalıp çizimi"]
        XCTAssertTrue(drawing.waitForExistence(timeout: 5))
        let width = app.textFields["En"]
        tapBelow(width, in: app)
        let keyboard = app.keyboards.firstMatch
        XCTAssertTrue(keyboard.waitForExistence(timeout: 5))
        sleep(1)

        // The piece being measured is still visible above the keyboard, with the field in between.
        XCTAssertGreaterThan(drawing.frame.height, 80)
        XCTAssertLessThanOrEqual(drawing.frame.maxY, width.frame.minY)
        XCTAssertLessThanOrEqual(width.frame.maxY, keyboard.frame.minY)
        // Undo sits in the navigation bar, not at the end of the scrolling form.
        XCTAssertTrue(app.buttons["Geri al"].isHittable)
    }

    func testZoomButtonsChangeTheViewAndFitBringsItBack() {
        let app = launchClean()
        app.buttons["Yeni proje oluştur"].tap()
        XCTAssertTrue(app.staticTexts["Kalıbını oluştur"].waitForExistence(timeout: 10))
        // Nothing to zoom while the drawing is empty.
        XCTAssertFalse(app.buttons["Yakınlaştır"].exists)
        app.buttons["Dikdörtgen ekle"].tap()
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 5))

        let zoomIn = app.buttons["Yakınlaştır"]
        XCTAssertTrue(zoomIn.waitForExistence(timeout: 5))
        XCTAssertFalse(app.buttons["Ekrana sığdır"].exists, "Fit is only offered once zoomed")
        zoomIn.tap()
        zoomIn.tap()
        XCTAssertTrue(app.buttons["Ekrana sığdır"].waitForExistence(timeout: 5))
        XCTAssertTrue(app.staticTexts["1 parça • 2.6× yakınlaştırıldı"].waitForExistence(timeout: 5))

        app.buttons["Uzaklaştır"].tap()
        XCTAssertTrue(app.staticTexts["1 parça • 1.6× yakınlaştırıldı"].waitForExistence(timeout: 5))
        // A gesture-free way to bring the selected piece back to the middle.
        XCTAssertTrue(app.buttons["Seçili parçayı göster"].exists)
        app.buttons["Seçili parçayı göster"].tap()
        XCTAssertTrue(app.staticTexts["1 parça • 1.6× yakınlaştırıldı"].exists)
        app.buttons["Ekrana sığdır"].tap()
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 5))
        XCTAssertFalse(app.buttons["Ekrana sığdır"].exists)
        XCTAssertFalse(app.buttons["Seçili parçayı göster"].exists)
    }

    func testWorkshopTellsABeginnerWhatToDoNext() {
        let app = launchClean()
        app.buttons["Yeni proje oluştur"].tap()
        XCTAssertTrue(app.staticTexts["Kalıbını oluştur"].waitForExistence(timeout: 10))
        let tip = app.descendants(matching: .any)["coach-tip"]
        XCTAssertTrue(tip.waitForExistence(timeout: 5))
        XCTAssertTrue(tip.label.contains("Başlamak için bir şekil ekle"), tip.label)
        app.buttons["Dikdörtgen ekle"].tap()
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 5))
        let expectation = XCTNSPredicateExpectation(
            predicate: NSPredicate(format: "label CONTAINS %@", "Ölçüleri milimetre olarak yaz"), object: tip)
        XCTAssertEqual(XCTWaiter().wait(for: [expectation], timeout: 5), .completed)
    }

    func testResumedDraftNewerThanSavedProjectCanBeSavedAgain() {
        let app = launchClean()
        app.buttons["Örnekle dene"].tap()
        XCTAssertTrue(countLabel(2, in: app).waitForExistence(timeout: 10))
        // An edit creates the local draft; then the project is saved once.
        app.buttons["Dikdörtgen ekle"].tap()
        XCTAssertTrue(countLabel(3, in: app).waitForExistence(timeout: 5))
        tapBelow(app.buttons["Projeyi kaydet"], in: app)
        app.buttons["Kapat"].tap()

        // Continue the same drawing so the draft becomes newer than the saved project.
        let resume = app.buttons["Son çizime devam et"]
        XCTAssertTrue(resume.waitForExistence(timeout: 10))
        resume.tap()
        XCTAssertTrue(countLabel(3, in: app).waitForExistence(timeout: 10))
        app.buttons["Çizgi ekle"].tap()
        XCTAssertTrue(countLabel(4, in: app).waitForExistence(timeout: 5))
        app.buttons["Kapat"].tap()

        // Resuming that newer draft must still allow saving the project itself.
        XCTAssertTrue(resume.waitForExistence(timeout: 10))
        resume.tap()
        XCTAssertTrue(countLabel(4, in: app).waitForExistence(timeout: 10))
        tapBelow(app.buttons["Projeyi kaydet"], in: app)
        XCTAssertFalse(app.alerts["İşlem tamamlanamadı"].waitForExistence(timeout: 3),
                       "Saving a resumed draft over its own saved project must not report a conflict")
        XCTAssertTrue(app.staticTexts["Proje kaydedildi. Ana ekrandaki Son projeler bölümünden açabilirsiniz."].exists)
    }

    func testUnreadableDraftCanBeSetAsideInsteadOfBlockingNewWork() {
        let app = XCUIApplication()
        app.launchArguments += ["-AppleLanguages", "(en)", "-AppleLocale", "en_US",
                                "--ui-test-reset-draft", "--ui-test-unreadable-draft"]
        app.launch()

        let notice = app.alerts["Son çizim açılamadı"]
        XCTAssertTrue(notice.waitForExistence(timeout: 15), "The unreadable draft should be reported at launch")
        notice.buttons["Tamam"].tap()
        XCTAssertFalse(app.buttons["Son çizime devam et"].exists)

        app.buttons["Yeni proje oluştur"].tap()
        let blocked = app.alerts["Önceki taslak okunamıyor"]
        XCTAssertTrue(blocked.waitForExistence(timeout: 10))
        blocked.buttons["Vazgeç"].tap()
        XCTAssertTrue(app.staticTexts["Kalıbını kolayca oluştur"].waitForExistence(timeout: 5))

        app.buttons["Yeni proje oluştur"].tap()
        XCTAssertTrue(blocked.waitForExistence(timeout: 10))
        blocked.buttons["Taslağı ayır ve devam et"].tap()
        XCTAssertTrue(app.staticTexts["Kalıbını oluştur"].waitForExistence(timeout: 10))
        app.buttons["Dikdörtgen ekle"].tap()
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 5))
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
        XCTAssertTrue(app.descendants(matching: .any)["Arka Beden önizlemesi"].exists)
        let details = app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS %@", "Kumaş yönü: Kontrol edilmeli")).firstMatch
        XCTAssertTrue(details.waitForExistence(timeout: 10))
        XCTAssertTrue(details.label.contains("Pamuk"))
        XCTAssertTrue(details.label.contains("Adet: 2"))
        let hideGrain = app.buttons["Kumaş yönünü gizle"]
        XCTAssertTrue(hideGrain.waitForExistence(timeout: 10))
        hideGrain.tap()
        XCTAssertTrue(app.buttons["Kumaş yönünü göster"].exists)
        selected.tap()
        XCTAssertTrue(app.buttons["Tüm parçalar"].waitForExistence(timeout: 10))
        app.buttons["Tüm parçalar"].tap()
        XCTAssertTrue(picker.waitForExistence(timeout: 10))
        XCTAssertTrue(app.descendants(matching: .any)["2 parçanın önizlemesi"].exists)
    }

    func testVerticalMoveButtonsShiftThePieceAndUndoRestoresIt() {
        let app = launchClean()
        app.buttons["Yeni proje oluştur"].tap()
        XCTAssertTrue(app.staticTexts["Kalıbını oluştur"].waitForExistence(timeout: 10))
        app.buttons["Dikdörtgen ekle"].tap()
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 5))

        let yField = app.textFields["Y"]
        func expectY(_ value: String, file: StaticString = #filePath, line: UInt = #line) {
            let expectation = XCTNSPredicateExpectation(
                predicate: NSPredicate(format: "value == %@", value), object: yField)
            XCTAssertEqual(XCTWaiter().wait(for: [expectation], timeout: 5), .completed,
                           "Y should read \(value)", file: file, line: line)
        }
        expectY("0")

        // CAD Y grows upwards: the up button adds 10 mm, the down button takes it back.
        let up = app.buttons["↑ 10 mm"]
        let down = app.buttons["↓ 10 mm"]
        tapBelow(up, in: app)
        expectY("10")
        tapBelow(down, in: app)
        expectY("0")

        let undo = app.buttons["Geri al"]
        XCTAssertTrue(undo.isEnabled)
        undo.tap()
        expectY("10")
        XCTAssertTrue(app.buttons["İleri al"].isEnabled)
    }

    func testInvalidDimensionNamesTheFieldInTheAlert() {
        let app = launchClean()
        app.buttons["Yeni proje oluştur"].tap()
        XCTAssertTrue(app.staticTexts["Kalıbını oluştur"].waitForExistence(timeout: 10))
        app.buttons["Dikdörtgen ekle"].tap()
        XCTAssertTrue(countLabel(1, in: app).waitForExistence(timeout: 5))

        let width = app.textFields["En"]
        tapBelow(width, in: app)
        // Two separators can be typed without leaving the numbers keyboard page.
        width.typeText(",,")
        let done = app.keyboards.buttons["Done"]
        if done.exists { done.tap() }
        tapBelow(app.buttons["Ölçüleri, konumu ve adı uygula"], in: app)

        let alert = app.alerts["İşlem tamamlanamadı"]
        XCTAssertTrue(alert.waitForExistence(timeout: 5))
        let reason = alert.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "birden çok")).firstMatch
        XCTAssertTrue(reason.exists, "The alert should say which box is wrong and why")
        alert.buttons["Tamam"].tap()
        // The alert fades out; wait for it instead of checking at once.
        let gone = XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: alert)
        XCTAssertEqual(XCTWaiter().wait(for: [gone], timeout: 5), .completed)
        // Nothing was applied: the drawing still has its one piece.
        XCTAssertTrue(countLabel(1, in: app).exists)
    }
}
