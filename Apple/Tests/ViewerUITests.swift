import XCTest

final class ViewerUITests: XCTestCase {
    func testCancellingExportKeepsOpenModelAndDoesNotReportSuccess() {
        let fixture = #"{"Name":"Deneme","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture", "-AppleLanguages", "(en)", "-AppleLocale", "en_US"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let export = app.buttons["Açık modeli dışa aktar…"]
        XCTAssertTrue(export.waitForExistence(timeout: 15))
        export.tap()
        let cancel = app.buttons["Cancel"]
        XCTAssertTrue(cancel.waitForExistence(timeout: 10))
        cancel.tap()
        XCTAssertTrue(export.waitForExistence(timeout: 10))
        XCTAssertTrue(app.staticTexts["Deneme"].exists)
        XCTAssertFalse(app.staticTexts["Model seçilen hedefe dışa aktarıldı."].exists)
    }

    func testDuplicatePieceNamesCanBeDistinguishedAndFoundBySize() {
        let fixture = #"{"Name":"Deneme","Pieces":[{"Name":"Kol","Metadata":{"Size":"38","Material":"Pamuk"},"Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]},{"Name":"Kol","Metadata":{"Size":"42","Material":"Yün"},"Closed":false,"Points":[{"X":0,"Y":0},{"X":2,"Y":2}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 15))
        picker.tap()
        let rows = app.buttons.matching(identifier: "Kol")
        XCTAssertEqual(rows.count, 2)
        XCTAssertTrue((rows.element(boundBy: 0).value as? String ?? "").contains("Beden: 38"))
        XCTAssertTrue((rows.element(boundBy: 1).value as? String ?? "").contains("Parça 2"))
        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 5))
        search.tap()
        search.typeText("42")
        XCTAssertEqual(rows.count, 1)
        XCTAssertTrue((rows.firstMatch.value as? String ?? "").contains("Beden: 42"))
        rows.firstMatch.tap()
        XCTAssertTrue(app.descendants(matching: .any)["Kol kontur önizlemesi"].waitForExistence(timeout: 5))
    }

    func testFailedPieceRenameRetainsEditOnlyForSameSelectionUntilCancel() {
        // The viewer accepts duplicate keys, while the lossless editor rejects them.
        let fixture = #"{"Name":"Deneme","Name":"Deneme","Pieces":[{"Name":"Ilk","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]},{"Name":"Diger","Closed":false,"Points":[{"X":0,"Y":0},{"X":2,"Y":2}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()

        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 15))
        picker.tap()
        app.buttons["Ilk"].tap()
        let rename = app.buttons["Parça adını değiştir ve kopya kaydet"]
        XCTAssertTrue(rename.waitForExistence(timeout: 5))
        let editor = app.alerts["Parça adını değiştir"]
        let field = editor.textFields.firstMatch
        let failure = app.alerts["İşlem tamamlanamadı"]

        rename.tap()
        XCTAssertTrue(field.waitForExistence(timeout: 5))
        field.tap()
        field.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: 3) + "Yeni")
        editor.buttons["Yeni kopyayı kaydet"].tap()
        XCTAssertTrue(failure.waitForExistence(timeout: 10))
        failure.buttons["Tamam"].tap()
        rename.tap()
        XCTAssertTrue(field.waitForExistence(timeout: 5))
        XCTAssertEqual(field.value as? String, "Yeni")
        editor.buttons["İptal"].tap()
        rename.tap()
        XCTAssertTrue(field.waitForExistence(timeout: 5))
        XCTAssertEqual(field.value as? String, "Ilk")

        field.tap()
        field.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: 3) + "Tekrar")
        editor.buttons["Yeni kopyayı kaydet"].tap()
        XCTAssertTrue(failure.waitForExistence(timeout: 10))
        failure.buttons["Tamam"].tap()
        let selectedPicker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Ilk")).firstMatch
        XCTAssertTrue(selectedPicker.waitForExistence(timeout: 5))
        selectedPicker.tap()
        app.buttons["Diger"].tap()
        let otherPicker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Diger")).firstMatch
        XCTAssertTrue(otherPicker.waitForExistence(timeout: 5))
        otherPicker.tap()
        app.buttons["Ilk"].tap()
        rename.tap()
        XCTAssertTrue(field.waitForExistence(timeout: 5))
        XCTAssertEqual(field.value as? String, "Ilk")
        editor.buttons["İptal"].tap()
    }

    func testFailedPieceRenameDraftIsClearedWhenIdenticalModelReopens() {
        let fixture = #"{"Name":"Deneme","Name":"Deneme","Pieces":[{"Name":"Ilk","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-library-fixture", "--ui-test-library-reset"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launchEnvironment["TCAD_UI_TEST_FILENAME"] = "Deneme.tcad"
        app.launch()
        let save = app.buttons["Bu modeli telefona kaydet"]
        XCTAssertTrue(save.waitForExistence(timeout: 15))
        save.tap()
        XCTAssertTrue(app.staticTexts["Model telefona kaydedildi."].waitForExistence(timeout: 10))
        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 5))
        picker.tap()
        app.buttons["Ilk"].tap()
        let rename = app.buttons["Parça adını değiştir ve kopya kaydet"]
        XCTAssertTrue(rename.waitForExistence(timeout: 5))
        rename.tap()
        let editor = app.alerts["Parça adını değiştir"]
        let field = editor.textFields.firstMatch
        XCTAssertTrue(field.waitForExistence(timeout: 5))
        field.tap()
        field.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: 3) + "Yeni")
        editor.buttons["Yeni kopyayı kaydet"].tap()
        let failure = app.alerts["İşlem tamamlanamadı"]
        XCTAssertTrue(failure.waitForExistence(timeout: 10))
        failure.buttons["Tamam"].tap()

        app.buttons["Modellerim"].tap()
        let saved = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Deneme")).firstMatch
        XCTAssertTrue(saved.waitForExistence(timeout: 10))
        saved.tap()
        XCTAssertTrue(picker.waitForExistence(timeout: 10))
        picker.tap()
        app.buttons["Ilk"].tap()
        XCTAssertTrue(rename.waitForExistence(timeout: 5))
        rename.tap()
        XCTAssertTrue(field.waitForExistence(timeout: 5))
        XCTAssertEqual(field.value as? String, "Ilk")
        editor.buttons["İptal"].tap()
    }

    func testPieceRenameExplainsInvalidProjectIdentities() {
        let valid = "11111111-1111-1111-1111-111111111111"
        let cases = [
            "\"Id\":\"00000000-0000-0000-0000-000000000000\",\"Name\":\"Diğer\"",
            "\"Id\":\"bad\",\"Name\":\"Diğer\"",
            "\"Id\":\"\(valid)\",\"Name\":\"Diğer\"",
            "\"Id\":null,\"Name\":\"Diğer\"",
            "\"Id\":\"22222222-2222-2222-2222-222222222222\""
        ]
        for other in cases {
            let fixture = """
            {"Name":"Deneme","Pieces":[{"Id":"\(valid)","Name":"Seçili","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]},{\(other),"Closed":false,"Points":[{"X":0,"Y":0},{"X":2,"Y":2}]}]}
            """
            let app = XCUIApplication()
            app.launchArguments += ["--ui-test-fixture"]
            app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
            app.launch()
            let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
            XCTAssertTrue(picker.waitForExistence(timeout: 15))
            picker.tap()
            app.buttons["Seçili"].tap()
            let rename = app.buttons["Parça adını değiştir ve kopya kaydet"]
            XCTAssertTrue(rename.waitForExistence(timeout: 5))
            XCTAssertFalse(rename.isEnabled)
            XCTAssertTrue(app.staticTexts["Parça adını değiştirmek için tüm parçaların adı ve varsa geçerli, benzersiz kimliği olmalı."].exists)
            app.terminate()
        }
    }
    func testPieceRenameSavesIndependentCopyForIdentifiedAndLegacyPieces() {
        for idField in ["\"Id\":\"22222222-2222-2222-2222-222222222222\",", ""] {
            let fixture = """
            {"Name":"Deneme","Pieces":[{"Name":"İlk","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]},{\(idField)"Name":"Eski","Closed":false,"Points":[{"X":0,"Y":0},{"X":2,"Y":2}]}]}
            """
            let app = XCUIApplication()
            app.launchArguments += ["--ui-test-library-fixture", "--ui-test-library-reset"]
            app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
            app.launch()
            let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
            XCTAssertTrue(picker.waitForExistence(timeout: 15))
            picker.tap()
            app.buttons["Eski"].tap()
            let rename = app.buttons["Parça adını değiştir ve kopya kaydet"]
            XCTAssertTrue(rename.waitForExistence(timeout: 5))
            rename.tap()
            let editor = app.alerts["Parça adını değiştir"]
            XCTAssertTrue(editor.waitForExistence(timeout: 5))
            XCTAssertTrue(editor.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "pastal")).firstMatch.exists)
            let field = editor.textFields.firstMatch
            field.tap()
            field.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: 4) + "Yeni")
            editor.buttons["Yeni kopyayı kaydet"].tap()
            XCTAssertTrue(app.staticTexts["Parça adı değiştirilmiş yeni kopya Modellerim bölümüne kaydedildi."].waitForExistence(timeout: 10))
            XCTAssertTrue(app.descendants(matching: .any)["Eski kontur önizlemesi"].exists)
            app.buttons["Modellerim"].tap()
            let saved = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Deneme – Yeni")).firstMatch
            XCTAssertTrue(saved.waitForExistence(timeout: 10))
            saved.tap()
            let reopenedPicker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
            XCTAssertTrue(reopenedPicker.waitForExistence(timeout: 10))
            reopenedPicker.tap()
            XCTAssertTrue(app.buttons["Yeni"].waitForExistence(timeout: 5))
            XCTAssertTrue(app.buttons["İlk"].exists)
            XCTAssertFalse(app.buttons["Eski"].exists)
            app.terminate()
        }
    }
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
