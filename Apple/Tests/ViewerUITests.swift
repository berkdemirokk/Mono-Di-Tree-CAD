import XCTest

final class ViewerUITests: XCTestCase {
    func testLaunchAndCancelFilePicker() {
        let app = XCUIApplication()
        app.launchArguments += ["-AppleLanguages", "(en)", "-AppleLocale", "en_US"]
        app.launch()

        let openButton = app.buttons[".tcad aç"]
        let emptyMessage = app.staticTexts["Önizlemek için bir .tcad dosyası açın."]
        XCTAssertTrue(openButton.waitForExistence(timeout: 15))
        XCTAssertTrue(emptyMessage.exists)

        for _ in 0..<2 {
            openButton.tap()
            let cancel = app.buttons["Cancel"]
            XCTAssertTrue(cancel.waitForExistence(timeout: 10), "The system file picker should open")
            cancel.tap()
            XCTAssertTrue(openButton.waitForExistence(timeout: 10))
            XCTAssertTrue(emptyMessage.exists, "Cancel must leave the empty viewer unchanged")
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

            let alert = app.alerts["İşlem tamamlanamadı"]
            XCTAssertTrue(alert.waitForExistence(timeout: 15))
            XCTAssertTrue(alert.staticTexts[message].exists)
            alert.buttons["Tamam"].tap()
            XCTAssertTrue(app.staticTexts["Önizlemek için bir .tcad dosyası açın."].exists)
            app.terminate()
        }
    }
    func testSavedModelCanBeFoundAndReopenedAfterRelaunch() throws {
        let fixture = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "selection", withExtension: "tcad"))
        let data = try Data(contentsOf: fixture)
        let app = XCUIApplication()
        app.launchArguments += ["-AppleLanguages", "(en)", "-AppleLocale", "en_US",
                                "--ui-test-fixture", "--ui-test-library-fixture", "--ui-test-library-reset"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = data.base64EncodedString()
        app.launch()

        XCTAssertTrue(app.staticTexts["Seçim Testi"].waitForExistence(timeout: 15))
        let save = app.buttons["Bu modeli telefona kaydet"]
        XCTAssertTrue(save.waitForExistence(timeout: 10))
        save.tap()
        XCTAssertTrue(app.staticTexts["Model telefona kaydedildi."].waitForExistence(timeout: 10))

        // Start without the import fixture so reopening must read the saved copy.
        app.terminate()
        app.launchArguments.removeAll { $0 == "--ui-test-fixture" || $0 == "--ui-test-library-reset" }
        app.launchEnvironment.removeValue(forKey: "TCAD_UI_TEST_FIXTURE")
        app.launch()
        XCTAssertTrue(app.staticTexts["Önizlemek için bir .tcad dosyası açın."].waitForExistence(timeout: 15))
        app.buttons["Modellerim"].tap()

        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 10))
        search.tap()
        search.typeText("Deneme")
        let savedModel = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Deneme Modeli")).firstMatch
        XCTAssertTrue(savedModel.waitForExistence(timeout: 10))
        search.typeText("-no-match")
        XCTAssertTrue(app.staticTexts["Eşleşen model bulunamadı."].waitForExistence(timeout: 5))
        search.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: "-no-match".count))
        XCTAssertTrue(savedModel.waitForExistence(timeout: 5))
        savedModel.tap()
        XCTAssertTrue(app.staticTexts["Seçim Testi"].waitForExistence(timeout: 10))
        XCTAssertTrue(app.descendants(matching: .any)["2 parçalı kontur önizlemesi"].exists)
        XCTAssertFalse(app.buttons["Bu modeli telefona kaydet"].exists)
    }

    func testRenamedCopyWithLongSpecialNameCanBeFoundReopenedAndDeleted() throws {
        let fixture = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "selection", withExtension: "tcad"))
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture", "--ui-test-library-fixture", "--ui-test-library-reset"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = try Data(contentsOf: fixture).base64EncodedString()
        app.launch()
        let rename = app.buttons["Proje adını değiştir ve kopya kaydet"]
        XCTAssertTrue(rename.waitForExistence(timeout: 15))
        rename.tap()
        let alert = app.alerts["Proje adını değiştir"]
        let field = alert.textFields.firstMatch
        XCTAssertTrue(field.waitForExistence(timeout: 5))
        field.tap()
        let oldName = field.value as? String ?? ""
        let name = String(repeating: "A", count: 105) + "/%Son"
        field.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: oldName.count) + name)
        alert.buttons["Yeni kopyayı kaydet"].tap()
        XCTAssertTrue(app.staticTexts["Yeni kopya Modellerim bölümüne kaydedildi."].waitForExistence(timeout: 10))
        app.buttons["Proje adını değiştir ve kopya kaydet"].tap()
        XCTAssertTrue(field.waitForExistence(timeout: 5))
        XCTAssertEqual(field.value as? String, "Seçim Testi")
        alert.buttons["İptal"].tap()
        app.terminate()
        app.launchArguments.removeAll { $0 == "--ui-test-fixture" || $0 == "--ui-test-library-reset" }
        app.launchEnvironment.removeValue(forKey: "TCAD_UI_TEST_FIXTURE")
        app.launch()
        XCTAssertTrue(app.buttons["Modellerim"].waitForExistence(timeout: 15))
        app.buttons["Modellerim"].tap()
        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 10))
        search.tap()
        search.typeText("/%Son")
        let row = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", name)).firstMatch
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        row.tap()
        XCTAssertTrue(app.staticTexts[name].waitForExistence(timeout: 10))
        app.buttons["Modellerim"].tap()
        let saved = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", name)).firstMatch
        XCTAssertTrue(saved.waitForExistence(timeout: 10))
        saved.swipeLeft()
        app.buttons["Sil"].tap()
        let confirmation = app.alerts["Modeli sil?"]
        XCTAssertTrue(confirmation.waitForExistence(timeout: 5))
        confirmation.buttons["Sil"].tap()
        XCTAssertTrue(app.staticTexts["Henüz kayıtlı model yok. Bir .tcad açıp telefona kaydedebilirsiniz."].waitForExistence(timeout: 5))
    }

    func testFailedRenameKeepsEnteredNameForRetry() {
        // Preview accepts this JSON, but lossless editing rejects duplicate name keys.
        let fixture = #"{"Name":"Orijinal","Name":"Orijinal","Pieces":[]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()

        let rename = app.buttons["Proje adını değiştir ve kopya kaydet"]
        XCTAssertTrue(rename.waitForExistence(timeout: 15))
        rename.tap()
        let editor = app.alerts["Proje adını değiştir"]
        let field = editor.textFields.firstMatch
        XCTAssertTrue(field.waitForExistence(timeout: 5))
        field.tap()
        let original = field.value as? String ?? ""
        field.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: original.count) + "Yeni ad")
        editor.buttons["Yeni kopyayı kaydet"].tap()

        let failure = app.alerts["İşlem tamamlanamadı"]
        XCTAssertTrue(failure.waitForExistence(timeout: 10))
        failure.buttons["Tamam"].tap()
        rename.tap()
        XCTAssertTrue(field.waitForExistence(timeout: 5))
        XCTAssertEqual(field.value as? String, "Yeni ad")
        editor.buttons["İptal"].tap()
        rename.tap()
        XCTAssertTrue(field.waitForExistence(timeout: 5))
        XCTAssertEqual(field.value as? String, "Orijinal")
    }

    func testHiddenFilenameIsVisibleAndCanBeRemoved() throws {
        let fixture = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "selection", withExtension: "tcad"))
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-library-fixture", "--ui-test-library-reset"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = try Data(contentsOf: fixture).base64EncodedString()
        app.launchEnvironment["TCAD_UI_TEST_FILENAME"] = ".hidden.tcad"
        app.launch()
        let save = app.buttons["Bu modeli telefona kaydet"]
        XCTAssertTrue(save.waitForExistence(timeout: 15))
        save.tap()
        XCTAssertTrue(app.staticTexts["Model telefona kaydedildi."].waitForExistence(timeout: 10))
        app.terminate()
        app.launchArguments.removeAll { $0 == "--ui-test-library-reset" }
        app.launchEnvironment.removeValue(forKey: "TCAD_UI_TEST_FIXTURE")
        app.launch()
        XCTAssertTrue(app.staticTexts["Önizlemek için bir .tcad dosyası açın."].waitForExistence(timeout: 15))
        app.buttons["Modellerim"].tap()
        let row = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "hidden")).firstMatch
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        row.swipeLeft()
        app.buttons["Sil"].tap()
        let confirmation = app.alerts["Modeli sil?"]
        XCTAssertTrue(confirmation.waitForExistence(timeout: 5))
        confirmation.buttons["İptal"].tap()
        XCTAssertTrue(row.exists)
        row.swipeLeft()
        app.buttons["Sil"].tap()
        XCTAssertTrue(confirmation.waitForExistence(timeout: 5))
        confirmation.buttons["Sil"].tap()
        XCTAssertTrue(app.staticTexts["Henüz kayıtlı model yok. Bir .tcad açıp telefona kaydedebilirsiniz."].waitForExistence(timeout: 5))
    }
    func testLegacyNameThatLooksLikeAnEncodingMarkerIsUnchanged() throws {
        let fixture = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "selection", withExtension: "tcad"))
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-library-fixture", "--ui-test-library-reset"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = try Data(contentsOf: fixture).base64EncodedString()
        app.launchEnvironment["TCAD_UI_TEST_FILENAME"] = "__tcad_name_v1__%2F.tcad"
        app.launch()
        let save = app.buttons["Bu modeli telefona kaydet"]
        XCTAssertTrue(save.waitForExistence(timeout: 15))
        save.tap()
        XCTAssertTrue(app.staticTexts["Model telefona kaydedildi."].waitForExistence(timeout: 10))
        app.terminate()
        app.launchArguments.removeAll { $0 == "--ui-test-library-reset" }
        app.launchEnvironment.removeValue(forKey: "TCAD_UI_TEST_FIXTURE")
        app.launch()
        XCTAssertTrue(app.staticTexts["Önizlemek için bir .tcad dosyası açın."].waitForExistence(timeout: 15))
        app.buttons["Modellerim"].tap()
        let row = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "__tcad_name_v1__%2F")).firstMatch
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        row.tap()
        XCTAssertTrue(app.staticTexts["Seçim Testi"].waitForExistence(timeout: 10))
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
