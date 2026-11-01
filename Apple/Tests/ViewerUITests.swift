import XCTest

final class ViewerUITests: XCTestCase {
    func testBOMEncodedLegacyModelsExplainUnavailableNameEditing() throws {
        let input = #"[1,{"Name":"Ön","Pieces":[],"GradingRules":[]}]"#
        let encodings: [(String.Encoding, [UInt8])] = [
            (.utf16LittleEndian, [0xff, 0xfe]), (.utf16BigEndian, [0xfe, 0xff]),
            (.utf32LittleEndian, [0xff, 0xfe, 0, 0]), (.utf32BigEndian, [0, 0, 0xfe, 0xff])
        ]
        let app = XCUIApplication()
        app.launchArguments.append("--ui-test-library-fixture")
        for (encoding, bom) in encodings {
            var bytes = Data(bom)
            bytes.append(try XCTUnwrap(input.data(using: encoding)))
            app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = bytes.base64EncodedString()
            app.launch()
            let rename = app.buttons["Proje adını değiştir ve kopya kaydet"]
            XCTAssertTrue(rename.waitForExistence(timeout: 15))
            XCTAssertFalse(rename.isEnabled)
            XCTAssertTrue(app.staticTexts["UTF-16 veya UTF-32 dosyalarında proje adını değiştirme henüz desteklenmiyor. Açık modeli özgün baytlarıyla dışa aktarabilirsiniz."].exists)
            XCTAssertTrue(app.buttons["Bu modeli telefona kaydet"].isEnabled)
            XCTAssertTrue(app.buttons["Açık modeli dışa aktar…"].isEnabled)
            app.terminate()
        }
    }

    func testBOMEncodedModelsExplainUnavailablePieceNameEditing() throws {
        let input = #"[1,{"Name":"Ön","Pieces":[{"Id":"C1F46B38-5857-4CA4-9F6C-D3AE73A4C9C9","Name":"Parça","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]}],"GradingRules":[]}]"#
        let encodings: [(String.Encoding, [UInt8])] = [
            (.utf16LittleEndian, [0xff, 0xfe]), (.utf16BigEndian, [0xfe, 0xff]),
            (.utf32LittleEndian, [0xff, 0xfe, 0, 0]), (.utf32BigEndian, [0, 0, 0xfe, 0xff])
        ]
        let app = XCUIApplication()
        app.launchArguments.append("--ui-test-library-fixture")
        for (encoding, bom) in encodings {
            var bytes = Data(bom)
            bytes.append(try XCTUnwrap(input.data(using: encoding)))
            app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = bytes.base64EncodedString()
            app.launch()
            let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
            XCTAssertTrue(picker.waitForExistence(timeout: 15))
            picker.tap()
            app.buttons["Parça"].tap()
            let rename = app.buttons["Parça adını değiştir ve kopya kaydet"]
            XCTAssertTrue(rename.waitForExistence(timeout: 5))
            XCTAssertFalse(rename.isEnabled)
            XCTAssertTrue(app.staticTexts["UTF-16 veya UTF-32 dosyalarında parça adını değiştirme henüz desteklenmiyor. Açık modeli özgün baytlarıyla dışa aktarabilirsiniz."].exists)
            XCTAssertFalse(app.buttons["Deneysel nokta düzenle…"].isEnabled)
            XCTAssertTrue(app.staticTexts["UTF-16 veya UTF-32 dosyalarında nokta düzenleme henüz desteklenmiyor. Açık modeli özgün baytlarıyla dışa aktarabilirsiniz."].exists)
            app.terminate()
        }
    }

    func testFailedRawSaveCanBeRetriedWithoutSelectingSourceAgain() {
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-library-fixture", "--ui-test-library-reset", "--ui-test-invalid-v6-import", "--ui-test-raw-save-fail-once", "--ui-test-raw-picker-lifecycle"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(#"{"Name":"Önceki","Pieces":[]}"#.utf8).base64EncodedString()
        app.launch()
        let warning = app.alerts["Dosya önizlenemedi"]
        XCTAssertTrue(warning.waitForExistence(timeout: 15))
        warning.buttons["Ham kopyayı telefona kaydet"].tap()
        let failure = app.alerts["İşlem tamamlanamadı"]
        XCTAssertTrue(failure.waitForExistence(timeout: 15)); failure.buttons["Tamam"].tap()
        let retry = app.buttons["Ham kopyayı yeniden kaydet…"]
        XCTAssertTrue(retry.waitForExistence(timeout: 5))
        XCTAssertTrue(app.staticTexts["Önceki"].exists)
        app.buttons["Test: seçim iptal"].tap()
        XCTAssertTrue(retry.exists)
        app.buttons["Test: geçersiz seçim"].tap()
        XCTAssertTrue(failure.waitForExistence(timeout: 5)); failure.buttons["Tamam"].tap()
        XCTAssertTrue(retry.exists)
        retry.tap()
        XCTAssertTrue(warning.waitForExistence(timeout: 5))
        warning.buttons["Ham kopyayı telefona kaydet"].tap()
        XCTAssertTrue(app.staticTexts["Ham kopya Modellerim bölümüne kaydedildi; önizlenemiyor."].waitForExistence(timeout: 15))
        XCTAssertFalse(retry.exists)
        app.buttons["Modellerim"].tap()
        let raw = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Ham kayıt:")).firstMatch
        XCTAssertTrue(raw.waitForExistence(timeout: 10))
        XCTAssertTrue((raw.value as? String ?? "").contains("Önizlenemiyor · Ham kopya kurtarma denenebilir"))
    }

    func testUnderflowingVersionSixNotchCoordinatesAreNotPreviewed() {
        let app = XCUIApplication()
        app.launchArguments.append("--ui-test-fixture")
        let invalid = #"[6,{"Name":"İşaret","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":1e-999,"Y":0},{"X":10,"Y":10}],"Notches":[{"VertexIndex":0,"Position":{"X":1e-999,"Y":0}}]}],"GradingRules":[]}]"#
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(invalid.utf8).base64EncodedString()
        app.launch()
        XCTAssertTrue(app.alerts["İşlem tamamlanamadı"].waitForExistence(timeout: 15))
        XCTAssertFalse(app.buttons["Köşe işaretlerini ara ve seç…"].exists)
        app.terminate()
        let valid = invalid.replacingOccurrences(of: "1e-999", with: "0e-999")
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(valid.utf8).base64EncodedString()
        app.launch()
        XCTAssertTrue(app.buttons["Köşe işaretlerini ara ve seç…"].waitForExistence(timeout: 15))
    }

    func testNotchRowsExposePieceAndSizeForIdenticalCoordinates() {
        let fixture = #"[6,{"Name":"Bedenler","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":10,"Y":10}],"Metadata":{"Size":"S"},"Notches":[{"VertexIndex":0,"Position":{"X":0,"Y":0}}]},{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":20,"Y":20}],"Metadata":{"Size":"XL"},"Notches":[{"VertexIndex":0,"Position":{"X":0,"Y":0}}]}],"GradingRules":[]}]"#
        let app = XCUIApplication()
        app.launchArguments.append("--ui-test-fixture")
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let picker = app.buttons["Köşe işaretlerini ara ve seç…"]
        XCTAssertTrue(picker.waitForExistence(timeout: 15)); picker.tap()
        let marks = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Nokta 0"))
        XCTAssertTrue(marks.element(boundBy: 1).waitForExistence(timeout: 10))
        XCTAssertEqual(marks.count, 2)
        let first = marks.element(boundBy: 0), second = marks.element(boundBy: 1)
        XCTAssertEqual(first.label, second.label)
        XCTAssertTrue((first.value as? String ?? "").contains("Beden: S"))
        XCTAssertTrue((second.value as? String ?? "").contains("Beden: XL"))
        XCTAssertTrue((second.value as? String ?? "").contains("Parça 2"))
        second.tap()
        let selected = app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS %@", "Beden: XL")).firstMatch
        XCTAssertTrue(selected.waitForExistence(timeout: 5))
    }

    func testUnknownVersionSixNotchCanBeKeptAsUnvalidatedRawData() {
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-library-fixture", "--ui-test-library-reset", "--ui-test-unknown-v6-notch"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(#"{"Name":"Önceki","Pieces":[]}"#.utf8).base64EncodedString()
        app.launch()
        let warning = app.alerts["Dosya önizlenemedi"]
        XCTAssertTrue(warning.waitForExistence(timeout: 15))
        warning.buttons["Ham kopyayı telefona kaydet"].tap()
        XCTAssertTrue(app.staticTexts["Ham kopya Modellerim bölümüne kaydedildi; önizlenemiyor."].waitForExistence(timeout: 15))
        XCTAssertTrue(app.staticTexts["Önceki"].exists)
        app.buttons["Modellerim"].tap()
        let raw = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Ham kayıt:")).firstMatch
        XCTAssertTrue(raw.waitForExistence(timeout: 10)); raw.tap()
        XCTAssertTrue(app.alerts["Ham kopyayı kurtar?"].waitForExistence(timeout: 5))
    }

    func testVersionSixRejectsUnknownGeometryAndNotchSemantics() {
        let base = #"[6,{"Name":"İşaretli","Pieces":[{"Name":"Ön","Closed":true,"Points":[{"X":0,"Y":0},{"X":4,"Y":0},{"X":0,"Y":4}],"Notches":[{"VertexIndex":1,"Position":{"X":4,"Y":0}}]}],"GradingRules":[]}]"#
        let invalid = [
            base.replacingOccurrences(of: #""Name":"İşaretli""#, with: #""Name":"İşaretli","GeometryMode":"other""#),
            base.replacingOccurrences(of: #""Closed":true"#, with: #""Closed":true,"Curves":[]"#),
            base.replacingOccurrences(of: #"{"X":0,"Y":4}"#, with: #"{"X":0,"Y":4,"Curve":1}"#),
            base.replacingOccurrences(of: #""VertexIndex":1"#, with: #""VertexIndex":1,"Depth":2"#),
            base.replacingOccurrences(of: #""Position":{"X":4,"Y":0}"#, with: #""Position":{"X":4,"Y":0,"Z":1}"#),
            base.replacingOccurrences(of: #"{"X":0,"Y":4}"#, with: #"{"X":0,"Y":100000001}"#),
            base.replacingOccurrences(of: #""Closed":true"#, with: #""Closed":true,"Metadata":{"Size":"M","Mode":"other"}"#)
        ]
        let app = XCUIApplication()
        app.launchArguments.append("--ui-test-fixture")
        for input in invalid {
            app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(input.utf8).base64EncodedString()
            app.launch()
            XCTAssertTrue(app.alerts["İşlem tamamlanamadı"].waitForExistence(timeout: 15))
            XCTAssertFalse(app.otherElements["1 parçalı kontur önizlemesi"].exists)
            app.terminate()
        }
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(base.utf8).base64EncodedString()
        app.launch()
        XCTAssertTrue(app.otherElements["1 parçalı kontur önizlemesi"].waitForExistence(timeout: 15))
    }

    func testInvalidV6ImportCanBeCancelledOrKeptAsRawWithoutReplacingCurrentModel() {
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-library-fixture", "--ui-test-library-reset", "--ui-test-invalid-v6-import"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(#"{"Name":"Önceki","Pieces":[]}"#.utf8).base64EncodedString()
        app.launch()
        var warning = app.alerts["Dosya önizlenemedi"]
        XCTAssertTrue(warning.waitForExistence(timeout: 15))
        warning.buttons["Vazgeç"].tap()
        XCTAssertTrue(app.staticTexts["Önceki"].exists)
        app.terminate()
        app.launch()
        warning = app.alerts["Dosya önizlenemedi"]
        XCTAssertTrue(warning.waitForExistence(timeout: 15))
        warning.buttons["Ham kopyayı telefona kaydet"].tap()
        XCTAssertTrue(app.staticTexts["Ham kopya Modellerim bölümüne kaydedildi; önizlenemiyor."].waitForExistence(timeout: 15))
        XCTAssertTrue(app.staticTexts["Önceki"].exists)
        app.buttons["Modellerim"].tap()
        let raw = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Ham kayıt:")).firstMatch
        XCTAssertTrue(raw.waitForExistence(timeout: 10)); raw.tap()
        XCTAssertTrue(app.alerts["Ham kopyayı kurtar?"].waitForExistence(timeout: 5))
    }

    func testDesktopVersionSevenShowsUnverifiedTraceWarning() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "desktop-synthetic-rotated-v7", withExtension: "tcad"))
        let data = try Data(contentsOf: url)
        let app = XCUIApplication()
        app.launchArguments.append("--ui-test-fixture")
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = data.base64EncodedString()
        app.launch()
        let warning = app.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "Döndürme geçmişi, eşleşmeler ve üretime uygunluk bu telefonda doğrulanmaz")).firstMatch
        XCTAssertTrue(warning.waitForExistence(timeout: 15))
        XCTAssertTrue(app.buttons["Köşe işaretlerini ara ve seç…"].exists)
        XCTAssertFalse(app.buttons["Proje adını değiştir ve kopya kaydet"].isEnabled)
        XCTAssertFalse(app.alerts["İşlem tamamlanamadı"].exists)
    }

    func testVersionSixBOMEncodingsShowNotchPreview() throws {
        let text = #"[6,{"Name":"Kodlanmış","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":10,"Y":20}],"Notches":[{"VertexIndex":0,"Position":{"X":0,"Y":0}}]}],"GradingRules":[]}]"#
        let variants: [(String.Encoding, [UInt8])] = [
            (.utf16LittleEndian, [0xff, 0xfe]), (.utf16BigEndian, [0xfe, 0xff]),
            (.utf32LittleEndian, [0xff, 0xfe, 0, 0]), (.utf32BigEndian, [0, 0, 0xfe, 0xff])
        ]
        let app = XCUIApplication()
        app.launchArguments.append("--ui-test-fixture")
        for (encoding, bom) in variants {
            var data = Data(bom)
            data.append(try XCTUnwrap(text.data(using: encoding)))
            app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = data.base64EncodedString()
            app.launch()
            XCTAssertTrue(app.staticTexts["Kodlanmış"].waitForExistence(timeout: 15))
            XCTAssertTrue(app.buttons["Köşe işaretlerini ara ve seç…"].exists)
            XCTAssertFalse(app.alerts["İşlem tamamlanamadı"].exists)
            app.terminate()
        }
    }

    func testOversizedV6ImportOffersUnvalidatedRawRecovery() {
        assertOversizedV6RawRecovery(utf16: false)
    }

    func testOversizedUTF16V6ImportKeepsPreviousModelAndOffersRawRecovery() {
        assertOversizedV6RawRecovery(utf16: true)
    }

    func testUTF16V6ExpandedValidationTextOffersRawRecovery() {
        assertOversizedV6RawRecovery(utf16: true, expansion: true)
    }

    func testLateUTF16V6HeaderOffersRawRecovery() {
        assertOversizedV6RawRecovery(utf16: true, lateHeader: true)
    }

    private func assertOversizedV6RawRecovery(utf16: Bool, expansion: Bool = false, lateHeader: Bool = false) {
        let fixture = #"{"Name":"Önceki","Pieces":[]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-library-fixture", "--ui-test-library-reset", "--ui-test-large-v6-import"]
        if utf16 { app.launchArguments.append("--ui-test-large-v6-utf16be") }
        if expansion { app.launchArguments.append("--ui-test-v6-utf16-expansion") }
        if lateHeader { app.launchArguments.append("--ui-test-v6-long-header") }
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let warning = app.alerts["Dosya önizlenemedi"]
        XCTAssertTrue(warning.waitForExistence(timeout: 20))
        XCTAssertTrue(warning.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "Dosya doğrulanmadı")).firstMatch.exists)
        warning.buttons["Ham kopyayı telefona kaydet"].tap()
        XCTAssertTrue(app.staticTexts["Ham kopya Modellerim bölümüne kaydedildi; önizlenemiyor."].waitForExistence(timeout: 20))
        XCTAssertTrue(app.staticTexts["Önceki"].exists)
        app.buttons["Modellerim"].tap()
        let raw = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Ham kayıt:")).firstMatch
        XCTAssertTrue(raw.waitForExistence(timeout: 10)); raw.tap()
        XCTAssertTrue(app.alerts["Ham kopyayı kurtar?"].waitForExistence(timeout: 5))
    }

    func testProjectRenameIsDisabledForVersionSixButAvailableForEarlierFormats() {
        let app = XCUIApplication()
        app.launchArguments.append("--ui-test-fixture")
        let body = #"{"Name":"Kontrol","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]}],"GradingRules":[]}"#
        for input in [body, "[1,\(body)]", "[5,\(body)]"] {
            app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(input.utf8).base64EncodedString()
            app.launch()
            let rename = app.buttons["Proje adını değiştir ve kopya kaydet"]
            XCTAssertTrue(rename.waitForExistence(timeout: 15))
            XCTAssertTrue(rename.isEnabled)
            app.terminate()
        }
        let versionSix = #"[6,{"Name":"Kontrol","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}],"Notches":[{"VertexIndex":0,"Position":{"X":0,"Y":0}}]}],"GradingRules":[]}]"#
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(versionSix.utf8).base64EncodedString()
        app.launch()
        let rename = app.buttons["Proje adını değiştir ve kopya kaydet"]
        XCTAssertTrue(rename.waitForExistence(timeout: 15))
        XCTAssertFalse(rename.isEnabled)
        XCTAssertTrue(app.staticTexts["Sürüm 6 projelerinde proje adını değiştirip kopya kaydetme henüz desteklenmiyor. Açık modeli dışa aktarabilirsiniz."].exists)
        XCTAssertTrue(app.buttons["Açık modeli dışa aktar…"].isEnabled)
    }

    func testCorruptSavedModelOffersExplicitRawRecovery() {
        let fixture = #"{"Name":"Kaynak","Pieces":[]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-library-fixture", "--ui-test-library-reset", "--ui-test-corrupt-library-model"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let library = app.buttons["Modellerim"]
        XCTAssertTrue(library.waitForExistence(timeout: 15)); library.tap()
        let actions = app.buttons["Bozuk model işlemleri"]
        XCTAssertTrue(actions.waitForExistence(timeout: 10)); actions.tap()
        let raw = app.buttons["Ham kopyayı dışa aktar…"]
        XCTAssertTrue(raw.waitForExistence(timeout: 5)); raw.tap()
        let warning = app.alerts["Ham kopyayı kurtar?"]
        XCTAssertTrue(warning.waitForExistence(timeout: 5))
        XCTAssertTrue(warning.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "geçerli bir proje olduğu doğrulanmaz")).firstMatch.exists)
        warning.buttons["İptal"].tap()
        XCTAssertTrue(actions.exists)
    }

    func testSlowOldPieceSearchCannotReplaceNewResults() {
        let fixture = #"{"Name":"Arama","Pieces":[{"Name":"old","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]},{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":5,"Y":5}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture", "--ui-test-delay-old-piece-search"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Gösterilen parça")).firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 15)); picker.tap()
        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 5)); search.tap(); search.typeText("old")
        let delay = expectation(description: "Allow the old search to start")
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { delay.fulfill() }
        wait(for: [delay], timeout: 2)
        search.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: 3) + "kol")
        let result = app.buttons["Kol"]
        XCTAssertTrue(result.waitForExistence(timeout: 5))
        let stale = expectation(for: NSPredicate(format: "exists == true"), evaluatedWith: app.buttons["old"])
        stale.isInverted = true
        wait(for: [stale], timeout: 2)
        XCTAssertTrue(result.exists)
    }

    func testLargePreviewNavigatesNotchesAndKeepsSelection() {
        let fixture = #"[6,{"Name":"İşaretler","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":10,"Y":20}],"Notches":[{"VertexIndex":0,"Position":{"X":0,"Y":0}},{"VertexIndex":1,"Position":{"X":10,"Y":20}}]}],"GradingRules":[]}]"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let nextPiece = app.buttons["Sonraki parça"]
        XCTAssertTrue(nextPiece.waitForExistence(timeout: 15)); nextPiece.tap()
        let large = app.buttons["Konturu büyük görünümde incele"]
        for _ in 0..<5 where !large.isHittable { app.swipeUp() }
        XCTAssertTrue(large.isHittable); large.tap()
        let sheet = app.sheets.firstMatch
        let next = sheet.buttons["Sonraki işaret"]
        XCTAssertTrue(next.waitForExistence(timeout: 5)); next.tap()
        let label = sheet.staticTexts["largePreviewNotchLabel"]
        XCTAssertTrue(label.waitForExistence(timeout: 5))
        XCTAssertTrue(label.label.contains("Nokta 0"))
        next.tap()
        XCTAssertTrue(label.label.contains("Nokta 1"))
        XCTAssertFalse(next.isEnabled)
        app.navigationBars["Kontur inceleme"].buttons["Kapat"].tap()
        let selected = app.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "Seçili köşe işareti: Nokta 1")).firstMatch
        XCTAssertTrue(selected.waitForExistence(timeout: 5))
    }

    func testLargePreviewCanSearchAndSelectPiecesWithoutClosing() {
        let fixture = #"[5,{"Name":"Bedenler","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":10,"Y":20}],"Metadata":{"Size":"S"}},{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":20,"Y":30}],"Metadata":{"Size":"XL"}}],"GradingRules":[]}]"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let large = app.buttons["Konturu büyük görünümde incele"]
        XCTAssertTrue(large.waitForExistence(timeout: 15))
        for _ in 0..<5 where !large.isHittable { app.swipeUp() }
        XCTAssertTrue(large.isHittable); large.tap()
        let pick = app.buttons["Büyük görünümde parça seç"]
        XCTAssertTrue(pick.waitForExistence(timeout: 5)); pick.tap()
        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 5)); search.tap(); search.typeText("kol xl")
        let piece = app.buttons["Kol"]
        XCTAssertTrue(piece.waitForExistence(timeout: 5)); piece.tap()
        let details = app.staticTexts["largePreviewPieceDetails"]
        XCTAssertTrue(details.waitForExistence(timeout: 5))
        XCTAssertTrue(details.label.contains("Beden: XL"))
        XCTAssertTrue(app.navigationBars["Kontur inceleme"].exists)
        pick.tap()
        let all = app.buttons["Tüm parçalar"]
        XCTAssertTrue(all.waitForExistence(timeout: 5)); all.tap()
        XCTAssertTrue(app.otherElements["2 parçalı kontur önizlemesi"].waitForExistence(timeout: 5))
    }

    func testSlowOldNotchSearchCannotReplaceNewResults() {
        let fixture = #"[6,{"Name":"Arama","Pieces":[{"Name":"old","Closed":false,"Points":[{"X":0,"Y":0},{"X":10,"Y":10}],"Notches":[{"VertexIndex":0,"Position":{"X":0,"Y":0}},{"VertexIndex":1,"Position":{"X":10,"Y":10}}]}],"GradingRules":[]}]"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture", "--ui-test-delay-old-notch-search"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let picker = app.buttons["Köşe işaretlerini ara ve seç…"]
        XCTAssertTrue(picker.waitForExistence(timeout: 15)); picker.tap()
        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 5)); search.tap(); search.typeText("old")
        let delay = expectation(description: "Allow the old search to start")
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { delay.fulfill() }
        wait(for: [delay], timeout: 2)
        search.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: 3) + "nokta 1")
        let point1 = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Nokta 1")).firstMatch
        XCTAssertTrue(point1.waitForExistence(timeout: 5))
        let point0 = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Nokta 0")).firstMatch
        let stale = expectation(for: NSPredicate(format: "exists == true"), evaluatedWith: point0)
        stale.isInverted = true
        wait(for: [stale], timeout: 2)
        XCTAssertTrue(point1.exists)
    }

    func testOversizedMetadataRequiresExplicitRecovery() {
        let fixture = #"{"Name":"Kaynak","Pieces":[]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-library-fixture", "--ui-test-library-reset", "--ui-test-oversized-metadata"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let library = app.buttons["Modellerim"]
        XCTAssertTrue(library.waitForExistence(timeout: 15))
        library.tap()
        let entry = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Kurtarma gerekli")).firstMatch
        XCTAssertTrue(entry.waitForExistence(timeout: 10))
        entry.tap()
        let warning = app.alerts["Ham kopyayı kurtar?"]
        XCTAssertTrue(warning.waitForExistence(timeout: 5))
        XCTAssertTrue(warning.buttons["Ham kopyayı dışa aktar"].exists)
        warning.buttons["İptal"].tap()
        XCTAssertTrue(entry.exists)
    }

    func testLegacyDuplicateKeysAreRejectedInUTF8AndUTF16() {
        let app = XCUIApplication()
        app.launchArguments.append("--ui-test-fixture")
        let duplicate = #"[5,{"Name":"Kontur","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"\u0058":999,"Y":0},{"X":10,"Y":20}]}],"GradingRules":[]}]"#
        var utf16 = Data([0xff, 0xfe])
        utf16.append(duplicate.data(using: .utf16LittleEndian)!)
        for data in [Data(duplicate.utf8), utf16] {
            app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = data.base64EncodedString()
            app.launch()
            XCTAssertTrue(app.alerts["İşlem tamamlanamadı"].waitForExistence(timeout: 15))
            XCTAssertFalse(app.otherElements["1 parçalı kontur önizlemesi"].exists)
            app.terminate()
        }
    }

    func testInvalidFiniteFlagDoesNotProduceAContourPreview() {
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        for value in ["false", "null", "1", "\"true\""] {
            let fixture = #"[5,{"Name":"Kontur","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0,"IsFinite":\#(value)},{"X":10,"Y":20}]}],"GradingRules":[]}]"#
            app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
            app.launch()
            XCTAssertTrue(app.alerts["İşlem tamamlanamadı"].waitForExistence(timeout: 15), "Invalid IsFinite value: \(value)")
            XCTAssertFalse(app.otherElements["1 parçalı kontur önizlemesi"].exists)
            app.terminate()
        }
        let valid = #"[5,{"Name":"Kontur","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0,"IsFinite":true},{"X":10,"Y":20}]}],"GradingRules":[]}]"#
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(valid.utf8).base64EncodedString()
        app.launch()
        XCTAssertTrue(app.otherElements["1 parçalı kontur önizlemesi"].waitForExistence(timeout: 15))
    }

    func testInvalidGrainEndpointFlagIsNotShownAsValidGrainline() {
        let fixture = #"[5,{"Name":"İplik","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":10,"Y":20}],"Grainline":{"Start":{"X":0,"Y":0,"IsFinite":false},"End":{"X":1,"Y":1},"NeedsReview":false}}],"GradingRules":[]}]"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let next = app.buttons["Sonraki parça"]
        XCTAssertTrue(next.waitForExistence(timeout: 15))
        next.tap()
        XCTAssertTrue(app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS %@", "İplik yönü: Okunamadı")).firstMatch.exists)
        XCTAssertFalse(app.buttons["İplik yönlerini gizle"].exists)
    }

    func testLargeViewDistinguishesSameNamedSizesWhileNavigating() {
        let fixture = #"{"Name":"Bedenler","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":100,"Y":100}],"Metadata":{"Size":"S"}},{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":120,"Y":120}],"Metadata":{"Size":"XL"}}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let next = app.buttons["Sonraki parça"]
        XCTAssertTrue(next.waitForExistence(timeout: 15))
        next.tap()
        let largeView = app.buttons["Konturu büyük görünümde incele"]
        for _ in 0..<5 where !largeView.isHittable { app.swipeUp() }
        XCTAssertTrue(largeView.isHittable)
        largeView.tap()
        let sheet = app.sheets.firstMatch
        let details = sheet.staticTexts["largePreviewPieceDetails"]
        XCTAssertTrue(details.waitForExistence(timeout: 5))
        XCTAssertTrue(details.label.contains("Parça 1"))
        XCTAssertTrue(details.label.contains("Beden: S"))
        sheet.buttons["Sonraki parça"].tap()
        XCTAssertTrue(details.label.contains("Parça 2"))
        XCTAssertTrue(details.label.contains("Beden: XL"))
    }

    func testSelectedPieceCanBeInspectedInLargeView() {
        let fixture = #"{"Name":"İnceleme","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":100,"Y":100}]},{"Name":"Ön","Closed":false,"Points":[{"X":0,"Y":0},{"X":30,"Y":60}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let next = app.buttons["Sonraki parça"]
        XCTAssertTrue(next.waitForExistence(timeout: 15))
        next.tap()
        let largeView = app.buttons["Konturu büyük görünümde incele"]
        for _ in 0..<5 where !largeView.isHittable { app.swipeUp() }
        XCTAssertTrue(largeView.isHittable)
        largeView.tap()
        XCTAssertTrue(app.navigationBars["Kontur inceleme"].waitForExistence(timeout: 5))
        XCTAssertTrue(app.otherElements["Kol kontur önizlemesi"].exists)
        let zoom = app.buttons["Yakınlaştır"]
        for _ in 0..<5 where !zoom.isHittable { app.swipeUp() }
        XCTAssertTrue(zoom.isHittable)
        zoom.tap()
        let sheet = app.sheets.firstMatch
        let nextInSheet = sheet.buttons["Sonraki parça"]
        for _ in 0..<5 where !nextInSheet.isHittable { sheet.swipeDown() }
        XCTAssertTrue(nextInSheet.isHittable)
        nextInSheet.tap()
        XCTAssertTrue(sheet.otherElements["Ön kontur önizlemesi"].waitForExistence(timeout: 5))
        XCTAssertFalse(nextInSheet.isEnabled)
        app.navigationBars["Kontur inceleme"].buttons["Kapat"].tap()
        XCTAssertTrue(largeView.waitForExistence(timeout: 5))
        XCTAssertTrue(app.otherElements["Ön kontur önizlemesi"].exists)
    }

    func testImportedModelCanBeSavedUnderLibraryNameAndReopened() {
        let fixture = #"{"Name":"Orijinal Proje","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":100,"Y":100}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-library-fixture", "--ui-test-library-reset"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let saveAs = app.buttons["Telefona farklı adla kaydet…"]
        XCTAssertTrue(saveAs.waitForExistence(timeout: 15))
        saveAs.tap()
        let name = app.alerts.textFields.firstMatch
        XCTAssertTrue(name.waitForExistence(timeout: 5))
        name.tap()
        name.typeText(" 2026")
        app.alerts.buttons["Telefona kaydet"].tap()
        XCTAssertTrue(app.staticTexts["Model telefona kaydedildi."].waitForExistence(timeout: 10))
        app.buttons["Modellerim"].tap()
        let saved = app.buttons["Orijinal Proje 2026"]
        XCTAssertTrue(saved.waitForExistence(timeout: 10))
        saved.tap()
        XCTAssertTrue(app.staticTexts["Orijinal Proje"].waitForExistence(timeout: 10))
        XCTAssertTrue(app.otherElements["1 parçalı kontur önizlemesi"].exists)
    }

    func testLibraryFindsModelWhenNameWordsAreReversed() {
        let fixture = #"{"Name":"Ön Beden Kol","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-library-fixture", "--ui-test-library-reset"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launchEnvironment["TCAD_UI_TEST_FILENAME"] = "Ön Beden Kol.tcad"
        app.launch()
        let save = app.buttons["Bu modeli telefona kaydet"]
        XCTAssertTrue(save.waitForExistence(timeout: 15))
        save.tap()
        XCTAssertTrue(app.staticTexts["Model telefona kaydedildi."].waitForExistence(timeout: 10))
        app.buttons["Modellerim"].tap()
        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 10))
        search.tap()
        search.typeText("kol on")
        let model = app.buttons["Ön Beden Kol"]
        XCTAssertTrue(model.waitForExistence(timeout: 5))
        model.tap()
        XCTAssertTrue(app.staticTexts["Ön Beden Kol"].waitForExistence(timeout: 10))
    }

    func testNotchSearchCombinesPieceAndPoint() {
        let fixture = #"[6,{"Name":"Arama","Pieces":[{"Name":"Ön Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":25,"Y":40}],"Notches":[{"VertexIndex":0,"Position":{"X":0,"Y":0}},{"VertexIndex":1,"Position":{"X":25,"Y":40}}]},{"Name":"Arka Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":25,"Y":40}],"Notches":[{"VertexIndex":1,"Position":{"X":25,"Y":40}}]}],"GradingRules":[]}]"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let picker = app.buttons["Köşe işaretlerini ara ve seç…"]
        XCTAssertTrue(picker.waitForExistence(timeout: 15))
        picker.tap()
        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 5))
        search.tap()
        search.typeText("bulunmayan")
        let clear = app.buttons["Aramayı temizle"]
        XCTAssertTrue(clear.waitForExistence(timeout: 5)); clear.tap()
        XCTAssertTrue(app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Nokta 0")).firstMatch.waitForExistence(timeout: 5))
        search.tap()
        search.typeText("on kol nokta 1")
        let point = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Nokta 1")).firstMatch
        XCTAssertTrue(point.waitForExistence(timeout: 5))
        XCTAssertFalse(app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Nokta 0")).firstMatch.exists)
        point.tap()
        XCTAssertTrue(app.otherElements["Ön Kol kontur önizlemesi"].waitForExistence(timeout: 5))
    }

    func testDrawingControlsDoNotCoverContourPreview() {
        let fixture = #"[6,{"Name":"Görünüm","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":100,"Y":100}]}],"GradingRules":[]}]"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let drawing = app.otherElements["1 parçalı kontur önizlemesi"]
        XCTAssertTrue(drawing.waitForExistence(timeout: 15))
        let pan = app.buttons["Çizimi taşı"]
        for _ in 0..<5 where !pan.isHittable { app.swipeUp() }
        XCTAssertTrue(pan.isHittable)
        XCTAssertFalse(drawing.frame.intersects(pan.frame))
        pan.tap()
        let direction = app.buttons["Sol"]
        XCTAssertTrue(direction.waitForExistence(timeout: 5))
        XCTAssertFalse(drawing.frame.intersects(direction.frame))
    }

    func testChangingPieceClearsPreviousNotchSelection() {
        let fixture = #"[6,{"Name":"Seçim","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":100,"Y":100}],"Notches":[{"VertexIndex":0,"Position":{"X":0,"Y":0}}]},{"Name":"Ön","Closed":false,"Points":[{"X":0,"Y":0},{"X":50,"Y":50}]}],"GradingRules":[]}]"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let notchPicker = app.buttons["Köşe işaretlerini ara ve seç…"]
        XCTAssertTrue(notchPicker.waitForExistence(timeout: 15))
        notchPicker.tap()
        app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Nokta 0")).firstMatch.tap()
        let selected = app.staticTexts.matching(NSPredicate(format: "label BEGINSWITH %@", "Seçili köşe işareti: Nokta 0")).firstMatch
        XCTAssertTrue(selected.waitForExistence(timeout: 5))
        let piecePicker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Gösterilen parça")).firstMatch
        piecePicker.tap()
        app.buttons["Ön"].tap()
        piecePicker.tap()
        app.buttons["Kol"].tap()
        XCTAssertFalse(selected.exists)
    }

    func testPieceNavigationMovesBetweenContoursAndClearsNotch() {
        let fixture = #"[6,{"Name":"Gezinme","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":100,"Y":100}],"Notches":[{"VertexIndex":0,"Position":{"X":0,"Y":0}}]},{"Name":"Ön","Closed":false,"Points":[{"X":0,"Y":0},{"X":50,"Y":50}]}],"GradingRules":[]}]"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let next = app.buttons["Sonraki parça"]
        let previous = app.buttons["Önceki parça"]
        XCTAssertTrue(next.waitForExistence(timeout: 15))
        XCTAssertFalse(previous.isEnabled)
        next.tap()
        XCTAssertTrue(app.otherElements["Kol kontur önizlemesi"].exists)
        app.buttons["Köşe işaretlerini ara ve seç…"].tap()
        app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Nokta 0")).firstMatch.tap()
        next.tap()
        XCTAssertTrue(app.otherElements["Ön kontur önizlemesi"].exists)
        XCTAssertFalse(app.staticTexts.matching(NSPredicate(format: "label BEGINSWITH %@", "Seçili köşe işareti:")).firstMatch.exists)
        XCTAssertFalse(next.isEnabled)
        previous.tap()
        XCTAssertTrue(app.otherElements["Kol kontur önizlemesi"].exists)
        XCTAssertFalse(previous.isEnabled)
    }
    func testSelectedNotchCoordinatesFollowNavigation() {
        let fixture = #"[6,{"Name":"İşaret inceleme","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":25,"Y":40}],"Notches":[{"VertexIndex":0,"Position":{"X":0,"Y":0}},{"VertexIndex":1,"Position":{"X":25,"Y":40}}]}],"GradingRules":[]}]"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let nextPiece = app.buttons["Sonraki parça"]
        XCTAssertTrue(nextPiece.waitForExistence(timeout: 15))
        nextPiece.tap()
        let nextNotch = app.buttons["Sonraki işaret"]
        XCTAssertTrue(nextNotch.waitForExistence(timeout: 5))
        nextNotch.tap()
        let first = app.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "Nokta 0 · X 0 · Y 0 mm")).firstMatch
        XCTAssertTrue(first.waitForExistence(timeout: 5))
        nextNotch.tap()
        let second = app.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "Nokta 1 · X 25 · Y 40 mm")).firstMatch
        XCTAssertTrue(second.waitForExistence(timeout: 5))
        XCTAssertFalse(first.exists)
    }
    func testSelectionIsUnavailableWhileAnotherModelLoads() {
        let fixture = #"[6,{"Name":"Yükleme Testi","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}],"Notches":[{"VertexIndex":0,"Position":{"X":0,"Y":0}}]}],"GradingRules":[]}]"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture", "--ui-test-library-fixture", "--ui-test-library-reset", "--ui-test-open-delay"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let save = app.buttons["Bu modeli telefona kaydet"]
        XCTAssertTrue(save.waitForExistence(timeout: 15))
        save.tap()
        XCTAssertTrue(app.staticTexts["Model telefona kaydedildi."].waitForExistence(timeout: 10))
        app.buttons["Modellerim"].tap()
        let model = app.buttons["Deneme Modeli"]
        XCTAssertTrue(model.waitForExistence(timeout: 10))
        model.tap()
        XCTAssertTrue(app.staticTexts["Proje açılıyor…"].waitForExistence(timeout: 5))
        let notches = app.buttons["Köşe işaretlerini ara ve seç…"]
        XCTAssertFalse(notches.isEnabled)
        let pieces = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Gösterilen parça")).firstMatch
        XCTAssertTrue(pieces.exists)
        XCTAssertFalse(pieces.isEnabled)
        XCTAssertTrue(notches.waitForExistence(timeout: 10))
        let enabled = expectation(for: NSPredicate(format: "enabled == true"), evaluatedWith: notches)
        wait(for: [enabled], timeout: 10)
    }
    func testCancellingModelOpenPreservesPreviousModelAfterDelayedRead() {
        let fixture = #"{"Name":"Yükleme Testi","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":100,"Y":100}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture", "--ui-test-library-fixture", "--ui-test-library-reset", "--ui-test-open-delay"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let save = app.buttons["Bu modeli telefona kaydet"]
        XCTAssertTrue(save.waitForExistence(timeout: 15)); save.tap()
        XCTAssertTrue(app.staticTexts["Model telefona kaydedildi."].waitForExistence(timeout: 10))
        app.buttons["Modellerim"].tap()
        let model = app.buttons["Deneme Modeli"]
        XCTAssertTrue(model.waitForExistence(timeout: 10)); model.tap()
        let cancel = app.buttons["Açmayı iptal et"]
        XCTAssertTrue(cancel.waitForExistence(timeout: 5)); cancel.tap()
        XCTAssertTrue(app.staticTexts["Model açma iptal edildi."].waitForExistence(timeout: 5))
        XCTAssertTrue(save.exists)
        let staleCompletion = expectation(for: NSPredicate(format: "exists == false"), evaluatedWith: save)
        staleCompletion.isInverted = true
        wait(for: [staleCompletion], timeout: 4)
        XCTAssertTrue(save.isEnabled)
    }

    func testCancellingImporterDoesNotCancelModelAlreadyLoading() {
        let fixture = #"{"Name":"Yükleme Testi","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":100,"Y":100}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-library-fixture", "--ui-test-library-reset", "--ui-test-open-delay"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let save = app.buttons["Bu modeli telefona kaydet"]
        XCTAssertTrue(save.waitForExistence(timeout: 15))
        save.tap()
        XCTAssertTrue(app.staticTexts["Model telefona kaydedildi."].waitForExistence(timeout: 10))
        app.buttons["Modellerim"].tap()
        let model = app.buttons["Deneme Modeli"]
        XCTAssertTrue(model.waitForExistence(timeout: 10))
        model.tap()
        XCTAssertTrue(app.staticTexts["Proje açılıyor…"].waitForExistence(timeout: 5))
        app.buttons[".tcad aç"].tap()
        let cancel = app.buttons.matching(NSPredicate(format: "label == %@ OR label == %@", "Cancel", "İptal")).firstMatch
        XCTAssertTrue(cancel.waitForExistence(timeout: 5))
        cancel.tap()
        let loading = app.staticTexts["Proje açılıyor…"]
        let finished = expectation(for: NSPredicate(format: "exists == false"), evaluatedWith: loading)
        wait(for: [finished], timeout: 10)
        XCTAssertFalse(app.buttons["Bu modeli telefona kaydet"].exists, "The saved library model should finish opening after the picker is cancelled")
    }

    func testSelectingNotchPreservesZoomEvenWhenSelectingItAgain() {
        let fixture = #"[6,{"Name":"İşaret Testi","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":100,"Y":100}],"Notches":[{"VertexIndex":0,"Position":{"X":0,"Y":0}}]}],"GradingRules":[]}]"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let picker = app.buttons["Köşe işaretlerini ara ve seç…"]
        XCTAssertTrue(picker.waitForExistence(timeout: 15))
        var expectedZoom: String?
        for _ in 0..<2 {
            picker.tap()
            let notch = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Nokta 0")).firstMatch
            XCTAssertTrue(notch.waitForExistence(timeout: 5))
            notch.tap()
            let preview = app.otherElements["Kol kontur önizlemesi"]
            XCTAssertTrue(preview.waitForExistence(timeout: 5))
            let scroll = app.scrollViews.firstMatch
            for _ in 0..<6 where !preview.isHittable { scroll.swipeUp() }
            if let expectedZoom { XCTAssertEqual(preview.value as? String, expectedZoom) }
            preview.pinch(withScale: 1.5, velocity: 1)
            XCTAssertNotEqual(preview.value as? String, "Yüzde 100")
            expectedZoom = preview.value as? String
            for _ in 0..<6 where !picker.isHittable { scroll.swipeDown() }
            XCTAssertTrue(picker.isHittable)
        }
    }

    func testNotchNavigationPreservesZoomInBothDirections() {
        let fixture = #"[6,{"Name":"İşaret Testi","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":100,"Y":100}],"Notches":[{"VertexIndex":0,"Position":{"X":0,"Y":0}},{"VertexIndex":1,"Position":{"X":100,"Y":100}}]}],"GradingRules":[]}]"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let nextPiece = app.buttons["Sonraki parça"]
        XCTAssertTrue(nextPiece.waitForExistence(timeout: 15))
        nextPiece.tap()
        app.buttons["Sonraki işaret"].tap()
        let scroll = app.scrollViews.firstMatch
        let preview = app.otherElements["Kol kontur önizlemesi"]
        for button in ["Sonraki işaret", "Önceki işaret"] {
            for _ in 0..<6 where !preview.isHittable { scroll.swipeUp() }
            XCTAssertTrue(preview.isHittable)
            preview.pinch(withScale: 1.5, velocity: 1)
            XCTAssertNotEqual(preview.value as? String, "Yüzde 100")
            let zoomBeforeNavigation = preview.value as? String
            let navigation = app.buttons[button]
            for _ in 0..<6 where !navigation.isHittable { scroll.swipeDown() }
            XCTAssertTrue(navigation.isHittable)
            navigation.tap()
            XCTAssertEqual(preview.value as? String, zoomBeforeNavigation)
        }
    }

    func testContourZoomButtonsAndReset() {
        let fixture = #"{"Name":"Deneme","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":100,"Y":100}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let zoomIn = app.buttons["Yakınlaştır"]
        XCTAssertTrue(zoomIn.waitForExistence(timeout: 15))
        let scroll = app.scrollViews.firstMatch
        for _ in 0..<6 where !zoomIn.isHittable { scroll.swipeUp() }
        XCTAssertTrue(zoomIn.isHittable)
        let preview = app.otherElements["1 parçalı kontur önizlemesi"]
        XCTAssertEqual(preview.value as? String, "Yüzde 100")
        zoomIn.tap()
        XCTAssertEqual(preview.value as? String, "Yüzde 125")
        app.buttons["Uzaklaştır"].tap()
        XCTAssertEqual(preview.value as? String, "Yüzde 100")
        zoomIn.tap()
        app.buttons["Görünümü sıfırla"].tap()
        XCTAssertEqual(preview.value as? String, "Yüzde 100")
    }

    func testViewerCanScrollToContourOnPhone() {
        let fixture = #"{"Name":"Deneme","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()

        let preview = app.otherElements["1 parçalı kontur önizlemesi"]
        XCTAssertTrue(preview.waitForExistence(timeout: 15))
        let scroll = app.scrollViews.firstMatch
        XCTAssertTrue(scroll.exists)
        for _ in 0..<4 where !preview.isHittable { scroll.swipeUp() }
        XCTAssertTrue(preview.isHittable, "The contour should remain reachable when the content exceeds the screen")
        XCTAssertGreaterThanOrEqual(preview.frame.height, 300)
    }

    func testContourScrollPanAndPinchModes() {
        let fixture = #"{"Name":"Deneme","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let preview = app.otherElements["1 parçalı kontur önizlemesi"]
        XCTAssertTrue(preview.waitForExistence(timeout: 15))
        let scroll = app.scrollViews.firstMatch
        for _ in 0..<4 where !app.buttons["Çizimi taşı"].isHittable { scroll.swipeUp() }
        let pan = app.buttons["Çizimi taşı"]
        XCTAssertTrue(pan.isHittable)
        pan.tap()
        XCTAssertTrue(app.buttons["Taşımayı bitir"].exists)
        XCTAssertTrue(app.buttons["Sağ"].exists)
        app.buttons["Sağ"].tap()
        let center = preview.coordinate(withNormalizedOffset: CGVector(dx: 0.3, dy: 0.6))
        center.press(forDuration: 0.1, thenDragTo: center.withOffset(CGVector(dx: 35, dy: 35)))
        preview.pinch(withScale: 1.5, velocity: 1)
        XCTAssertTrue((preview.value as? String)?.contains("Yüzde") == true)
        XCTAssertNotEqual(preview.value as? String, "Yüzde 100")
        app.buttons["Taşımayı bitir"].tap()
        XCTAssertTrue(pan.exists)
        let before = preview.frame.minY
        preview.swipeDown()
        XCTAssertGreaterThan(preview.frame.minY, before, "A swipe over the drawing should scroll the page after leaving pan mode")
    }
    func testLibrarySearchAndSortCanBeUsedToSelectAModel() {
        let fixture = #"{"Name":"Deneme","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-library-fixture", "--ui-test-library-reset"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()

        let rename = app.buttons["Proje adını değiştir ve kopya kaydet"]
        XCTAssertTrue(rename.waitForExistence(timeout: 15))
        for name in ["Akasya", "Zebra"] {
            rename.tap()
            let editor = app.alerts["Proje adını değiştir"]
            let field = editor.textFields.firstMatch
            XCTAssertTrue(field.waitForExistence(timeout: 5))
            field.tap()
            field.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: 6) + name)
            editor.buttons["Yeni kopyayı kaydet"].tap()
            let ready = expectation(for: NSPredicate(format: "enabled == true"), evaluatedWith: rename)
            wait(for: [ready], timeout: 10)
        }

        app.buttons["Modellerim"].tap()
        let zebra = app.buttons["Zebra"]
        let akasya = app.buttons["Akasya"]
        XCTAssertTrue(zebra.waitForExistence(timeout: 10))
        XCTAssertTrue(akasya.waitForExistence(timeout: 10))
        let sort = app.buttons["Model listesi seçenekleri"]
        sort.tap()
        app.buttons["Ada göre"].tap()
        XCTAssertLessThan(akasya.frame.minY, zebra.frame.minY)
        sort.tap()
        app.buttons["En yeni önce"].tap()
        XCTAssertLessThan(zebra.frame.minY, akasya.frame.minY)
        sort.tap()
        app.buttons["Ada göre"].tap()
        XCTAssertLessThan(akasya.frame.minY, zebra.frame.minY)
        sort.tap()
        app.buttons["Kontrol gerekenler"].tap()
        let showAll = app.buttons["Tüm kayıtları göster"]
        XCTAssertTrue(showAll.waitForExistence(timeout: 5))
        showAll.tap()
        XCTAssertTrue(zebra.waitForExistence(timeout: 5))

        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 5))
        search.tap()
        search.typeText("ZEBRA")
        XCTAssertTrue(zebra.waitForExistence(timeout: 5))
        XCTAssertFalse(akasya.exists)
        sort.tap()
        app.buttons["Listeyi yenile"].tap()
        XCTAssertTrue(zebra.waitForExistence(timeout: 10))
        XCTAssertFalse(akasya.exists, "Refreshing must retain the active search")
        XCTAssertEqual(search.value as? String, "ZEBRA")
        zebra.tap()
        XCTAssertTrue(app.staticTexts["Zebra"].waitForExistence(timeout: 10))
    }
    func testExperimentalPointSaveRejectsSourceChangeDuringPreparation() {
        let fixture = #"{"Name":"Eski","Pieces":[{"Id":"11111111-1111-1111-1111-111111111111","Name":"Ön","Closed":true,"Points":[{"X":0,"Y":0},{"X":10,"Y":0},{"X":0,"Y":10}]}],"GradingRules":[]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture", "--ui-test-library-fixture", "--ui-test-library-reset", "--ui-test-point-save-delay", "--ui-test-point-source-change"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        XCTAssertTrue(app.staticTexts["Eski"].waitForExistence(timeout: 15))
        app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch.tap()
        app.buttons["Ön"].tap()
        app.buttons["Deneysel nokta düzenle…"].tap()
        let x = app.textFields["X · mm"]
        XCTAssertTrue(x.waitForExistence(timeout: 5)); x.tap()
        XCTAssertTrue(app.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "Serileme kuralı varsa eski kaynak geometrisiyle aynen korunur")).firstMatch.exists)
        x.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: 3) + "1")
        app.buttons["Önizlemeyi hesapla"].tap()
        XCTAssertTrue(app.staticTexts["Önizleme hazır; kopya henüz kaydedilmedi."].waitForExistence(timeout: 15))
        app.buttons["Yeni kopyayı kaydet"].tap()
        XCTAssertTrue(app.buttons["Kaydı iptal et"].waitForExistence(timeout: 5))
        app.buttons["Test: kaynak değiştir"].tap()
        XCTAssertTrue(app.staticTexts["Açık model veya seçim değişti; kopya model listesine kaydedilmedi. Geçici veri cihazda kalabilir."].waitForExistence(timeout: 10))
        app.buttons["Kapat"].tap()
        app.buttons["Modellerim"].tap()
        XCTAssertTrue(app.staticTexts["Henüz kayıtlı model yok. Bir .tcad açıp telefona kaydedebilirsiniz."].waitForExistence(timeout: 10))
    }
    func testExperimentalPointPreviewSavesSeparateCopy() {
        let fixture = #"{"Name":"Deney","Pieces":[{"Id":"11111111-1111-1111-1111-111111111111","Name":"Ön","Closed":true,"Points":[{"X":0,"Y":0},{"X":10,"Y":0},{"X":0,"Y":10}]}],"GradingRules":[]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture", "--ui-test-library-fixture", "--ui-test-library-reset", "--ui-test-point-save-delay"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        XCTAssertTrue(app.staticTexts["Deney"].waitForExistence(timeout: 15))
        app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch.tap()
        app.buttons["Ön"].tap()
        let edit = app.buttons["Deneysel nokta düzenle…"]
        XCTAssertTrue(edit.waitForExistence(timeout: 5)); edit.tap()
        let x = app.textFields["X · mm"]
        XCTAssertTrue(x.waitForExistence(timeout: 5)); x.tap()
        x.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: 3) + "1e-999")
        app.buttons["Önizlemeyi hesapla"].tap()
        XCTAssertTrue(app.staticTexts["Geçerli X/Y girin ve açık parçanın değişmediğini doğrulayın."].waitForExistence(timeout: 5))
        x.tap(); x.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: 6) + "1")
        app.buttons["Önizlemeyi hesapla"].tap()
        XCTAssertTrue(app.staticTexts["Önizleme hazır; kopya henüz kaydedilmedi."].waitForExistence(timeout: 15))
        app.buttons["Yeni kopyayı kaydet"].tap()
        XCTAssertTrue(app.buttons["Kaydı iptal et"].waitForExistence(timeout: 5))
        XCTAssertFalse(app.buttons["Yeni kopyayı kaydet"].isEnabled)
        app.buttons["Kaydı iptal et"].tap()
        XCTAssertFalse(app.buttons["Yeni kopyayı kaydet"].isEnabled)
        XCTAssertTrue(app.staticTexts["Kayıt iptal edildi; kopya model listesine kaydedilmedi. Geçici veri cihazda kalabilir."].waitForExistence(timeout: 5))
        app.buttons["Yeni kopyayı kaydet"].tap()
        XCTAssertTrue(app.staticTexts["Deneysel kopya Modellerim bölümüne kaydedildi. Orijinal korunuyor."].waitForExistence(timeout: 10))
        app.buttons["Kapat"].tap()
        XCTAssertTrue(app.staticTexts["Deney"].exists)
        app.buttons["Modellerim"].tap()
        let copies = app.buttons.matching(identifier: "Deney · nokta denemesi")
        XCTAssertTrue(copies.firstMatch.waitForExistence(timeout: 10))
        XCTAssertEqual(copies.count, 1)
    }
    func testDesktopSavedSyntheticVersionSixFixture() throws {
        // DesktopVerification.VerifyVertexNotches creates and saves this project.
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "desktop-synthetic-notch", withExtension: "tcad"))
        let data = try Data(contentsOf: url)
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = data.base64EncodedString()
        app.launch()
        XCTAssertTrue(app.staticTexts["Yeni proje"].waitForExistence(timeout: 15))
        XCTAssertTrue(app.staticTexts["Köşe işaretleri halkayla gösterilir; kesim derinliği veya yönü belirtilmez. Önizleme üretim çıktısı değildir."].exists)
        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 5)); picker.tap()
        let row = app.buttons["İşaret testi"]
        XCTAssertTrue(row.waitForExistence(timeout: 5))
        XCTAssertTrue((row.value as? String ?? "").contains("1 köşe işareti"))
    }
    func testVersionSixNotchPreviewAndInvalidNotches() {
        let project = #"[6,{"Name":"İşaretli","Pieces":[{"Name":"Ön","Closed":true,"Points":[{"X":0,"Y":0},{"X":4,"Y":0},{"X":0,"Y":4}],"Notches":[{"VertexIndex":1,"Position":{"X":4,"Y":0,"IsFinite":true}}]}],"GradingRules":[]}]"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(project.utf8).base64EncodedString()
        app.launch()
        XCTAssertTrue(app.staticTexts["İşaretli"].waitForExistence(timeout: 15))
        XCTAssertTrue(app.staticTexts["Köşe işaretleri halkayla gösterilir; kesim derinliği veya yönü belirtilmez. Önizleme üretim çıktısı değildir."].exists)
        app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch.tap()
        let row = app.buttons["Ön"]
        XCTAssertTrue(row.waitForExistence(timeout: 5))
        XCTAssertTrue((row.value as? String ?? "").contains("1 köşe işareti"))
        app.terminate()
        for invalid in [project.replacingOccurrences(of: #""X":4,"Y":0,"IsFinite"#, with: #""X":3,"Y":0,"IsFinite"#),
                        project.replacingOccurrences(of: #""VertexIndex":1"#, with: #""VertexIndex":true"#),
                        project.replacingOccurrences(of: #""VertexIndex":1"#, with: #""VertexIndex":1.0"#),
                        project.replacingOccurrences(of: #""VertexIndex":1"#, with: #""VertexIndex":1,"\u0056ertexIndex":1"#),
                        project.replacingOccurrences(of: "[6,", with: "[6.0,"),
                        project.replacingOccurrences(of: #""IsFinite":true"#, with: #""IsFinite":false"#),
                        project.replacingOccurrences(of: "[6,", with: "[5,")] {
            app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(invalid.utf8).base64EncodedString()
            app.launch()
            XCTAssertTrue(app.alerts["İşlem tamamlanamadı"].waitForExistence(timeout: 15))
            app.terminate()
        }
    }
    func testVersionMustBeAnIntegerJSONNumber() {
        let app = XCUIApplication()
        app.launchArguments.append("--ui-test-fixture")
        for version in ["true", "false", "1.0", "1e0", "1E+0", "18446744073709551617", "9223372036854775809", "7", "0"] {
            let project = "[\(version),{\"Name\":\"Sürüm testi\",\"Pieces\":[]}]"
            app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(project.utf8).base64EncodedString()
            app.launch()
            XCTAssertTrue(app.alerts["İşlem tamamlanamadı"].waitForExistence(timeout: 15), "Invalid version \(version) must be rejected")
            app.terminate()
        }
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(" \n[ \t1 \r, {\"Name\":\"Sürüm testi\",\"Pieces\":[]}]".utf8).base64EncodedString()
        app.launch()
        XCTAssertTrue(app.staticTexts["Sürüm testi"].waitForExistence(timeout: 15))
        XCTAssertFalse(app.alerts["İşlem tamamlanamadı"].exists)
    }

    func testDesktopBOMEncodedVersionedProjectsOpen() {
        let project = #"[1,{"Name":"Kodlama testi","Pieces":[],"GradingRules":[]}]"#
        let cases: [(String, String.Encoding, [UInt8])] = [
            ("UTF-8", .utf8, [0xef, 0xbb, 0xbf]),
            ("UTF-16 LE", .utf16LittleEndian, [0xff, 0xfe]),
            ("UTF-16 BE", .utf16BigEndian, [0xfe, 0xff]),
            ("UTF-32 LE", .utf32LittleEndian, [0xff, 0xfe, 0x00, 0x00]),
            ("UTF-32 BE", .utf32BigEndian, [0x00, 0x00, 0xfe, 0xff])
        ]
        let app = XCUIApplication()
        app.launchArguments.append("--ui-test-fixture")
        for (name, encoding, bom) in cases {
            var data = Data(bom)
            data.append(project.data(using: encoding)!)
            app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = data.base64EncodedString()
            app.launch()
            XCTAssertTrue(app.staticTexts["Kodlama testi"].waitForExistence(timeout: 15), "Failed to open \(name)")
            app.terminate()
        }
    }
    func testLargeNotchListDisclosesPreviewLimit() {
        let points = (0..<501).map { #"{"X":\#($0),"Y":0}"# }.joined(separator: ",")
        let marks = (0..<501).map { #"{"VertexIndex":\#($0),"Position":{"X":\#($0),"Y":0}}"# }.joined(separator: ",")
        let project = #"[6,{"Name":"Çok işaret","Pieces":[{"Name":"Açık","Closed":false,"Points":[\#(points)],"Notches":[\#(marks)]}],"GradingRules":[]}]"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(project.utf8).base64EncodedString()
        app.launch()
        XCTAssertTrue(app.staticTexts["Çok işaret"].waitForExistence(timeout: 15))
        XCTAssertTrue(app.staticTexts["Görünümde yalnız ilk 500 köşe işareti çiziliyor. Tüm işaretler kaynak dosyada korunur; parçaları ayrı seçerek inceleyin."].exists)
        app.buttons["Köşe işaretlerini ara ve seç…"].tap()
        XCTAssertTrue(app.staticTexts["İlk 500 eşleşme gösteriliyor. Diğer işaretleri bulmak için aramayı daraltın."].waitForExistence(timeout: 5))
        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 5)); search.tap(); search.typeText("500")
        let mark = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Nokta 500")).firstMatch
        XCTAssertTrue(mark.waitForExistence(timeout: 5)); mark.tap()
        XCTAssertTrue(app.staticTexts["Seçili köşe işareti: nokta 500. Önizlemede sarı halka ile gösterilir."].waitForExistence(timeout: 5))
    }
    func testSameNameCopiesCanBeSearchedAndDeletedByFullIdentity() {
        let fixture = #"{"Name":"Aynı Model","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-library-fixture", "--ui-test-library-reset"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let rename = app.buttons["Proje adını değiştir ve kopya kaydet"]
        XCTAssertTrue(rename.waitForExistence(timeout: 15))
        for _ in 0..<2 {
            rename.tap()
            let editor = app.alerts["Proje adını değiştir"]
            XCTAssertTrue(editor.waitForExistence(timeout: 5))
            editor.buttons["Yeni kopyayı kaydet"].tap()
            XCTAssertTrue(app.staticTexts["Yeni kopya Modellerim bölümüne kaydedildi."].waitForExistence(timeout: 10))
        }
        app.buttons["Modellerim"].tap()
        let rows = app.buttons.matching(identifier: "Aynı Model")
        XCTAssertTrue(rows.firstMatch.waitForExistence(timeout: 10))
        XCTAssertEqual(rows.count, 2)
        let firstValue = rows.element(boundBy: 0).value as? String ?? ""
        let secondValue = rows.element(boundBy: 1).value as? String ?? ""
        let prefix = "Kayıt kimliği: "
        XCTAssertTrue(firstValue.hasPrefix(prefix));XCTAssertTrue(secondValue.hasPrefix(prefix))
        let firstID = String(firstValue.dropFirst(prefix.count)).components(separatedBy: ", Kaydedildi:")[0]
        let secondID = String(secondValue.dropFirst(prefix.count)).components(separatedBy: ", Kaydedildi:")[0]
        XCTAssertFalse(firstID.isEmpty);XCTAssertNotEqual(firstID, secondID)
        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 5));search.tap();search.typeText(firstID)
        XCTAssertEqual(rows.count, 1)
        XCTAssertTrue((rows.firstMatch.value as? String ?? "").contains(firstID))
        let actions = app.buttons["Aynı Model model işlemleri"]
        XCTAssertTrue(actions.waitForExistence(timeout: 5))
        XCTAssertTrue((actions.value as? String ?? "").contains(firstID))
        actions.tap()
        app.buttons["Kayıtlı modeli sil"].tap()
        let confirmation = app.alerts["Modeli sil?"]
        XCTAssertTrue(confirmation.waitForExistence(timeout: 5))
        XCTAssertTrue(confirmation.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", firstID)).firstMatch.exists)
        confirmation.buttons["İptal"].tap()
        XCTAssertEqual(rows.count, 1)
        actions.tap()
        app.buttons["Kayıtlı modeli sil"].tap()
        XCTAssertTrue(confirmation.waitForExistence(timeout: 5))
        confirmation.buttons["Sil"].tap()
        search.tap();search.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: firstID.count))
        XCTAssertTrue(rows.firstMatch.waitForExistence(timeout: 5));XCTAssertEqual(rows.count, 1)
        XCTAssertTrue((rows.firstMatch.value as? String ?? "").contains(secondID))
        rows.firstMatch.tap()
        XCTAssertTrue(app.staticTexts["Aynı Model"].waitForExistence(timeout: 10))
    }

    func testCollapsedContourWarningCountsAffectedPieces() {
        let fixture = #"{"Name":"Çökmüş","Pieces":[{"Name":"Bir","Closed":false,"Points":[{"X":1,"Y":2},{"X":1,"Y":2}]},{"Name":"İki","Closed":true,"Points":[{"X":3,"Y":4},{"X":3,"Y":4},{"X":3,"Y":4}]},{"Name":"Normal","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":0}]},{"Name":"Tek","Closed":false,"Points":[{"X":0,"Y":0}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let warning = app.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "2 parçanın tüm noktaları üst üste geliyor")).firstMatch
        XCTAssertTrue(warning.waitForExistence(timeout: 15))
        app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch.tap()
        app.buttons["Normal"].tap()
        XCTAssertTrue(warning.exists)
        app.buttons["Yakınlaştır"].tap()
        XCTAssertTrue(warning.exists)
        XCTAssertTrue(app.descendants(matching: .any)["Normal kontur önizlemesi"].exists)
    }

    func testInsufficientContoursRemainSearchableButAreNotDrawnOrMeasured() {
        let fixture = #"{"Name":"Eksik","Pieces":[{"Name":"Boş","Closed":false,"Points":[]},{"Name":"Tek","Closed":false,"Points":[{"X":0,"Y":0}]},{"Name":"Kapalı iki","Closed":true,"Points":[{"X":0,"Y":0},{"X":3,"Y":4}]},{"Name":"Geçerli","Closed":false,"Points":[{"X":0,"Y":0},{"X":3,"Y":4}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let warning = app.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "3 parça asgari nokta sayısını karşılamıyor")).firstMatch
        XCTAssertTrue(warning.waitForExistence(timeout: 15))
        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        picker.tap()
        for name in ["Boş", "Tek", "Kapalı iki"] {
            let row = app.buttons[name]
            XCTAssertTrue(row.waitForExistence(timeout: 5))
            XCTAssertTrue((row.value as? String ?? "").contains("nokta sayısı yetersiz"))
        }
        app.buttons["Kapalı iki"].tap()
        XCTAssertTrue(app.staticTexts["Bu parça asgari nokta sayısını karşılamıyor; kontur çizimi ve ölçüsü gösterilemiyor."].waitForExistence(timeout: 5))
        XCTAssertFalse(app.descendants(matching: .any)["Kapalı iki kontur önizlemesi"].exists)
        XCTAssertFalse(app.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "Yaklaşık kontur uzunluğu")).firstMatch.exists)
        let selected = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Kapalı iki")).firstMatch
        selected.tap();app.buttons["Geçerli"].tap()
        XCTAssertTrue(app.descendants(matching: .any)["Geçerli kontur önizlemesi"].waitForExistence(timeout: 5))
    }

    func testAllInsufficientContoursStillHavePicker() {
        let fixture = #"{"Name":"Eksik","Pieces":[{"Name":"Tek","Closed":false,"Points":[{"X":0,"Y":0}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 15))
        XCTAssertTrue(app.staticTexts["Bu projede görüntülenebilir kontur yok."].exists)
        picker.tap();XCTAssertTrue(app.buttons["Tek"].waitForExistence(timeout: 5))
    }

    func testRepeatedClosureDoesNotCountAsThirdVertex() {
        let fixture = #"{"Name":"Kapanış","Pieces":[{"Name":"Eksik","Closed":true,"Points":[{"X":0,"Y":0},{"X":3,"Y":0},{"X":0,"Y":0}]},{"Name":"Üçgen","Closed":true,"Points":[{"X":0,"Y":0},{"X":3,"Y":0},{"X":0,"Y":4},{"X":0,"Y":0}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 15)); picker.tap()
        let incomplete = app.buttons["Eksik"]
        XCTAssertTrue(incomplete.waitForExistence(timeout: 5))
        XCTAssertTrue((incomplete.value as? String ?? "").contains("nokta sayısı yetersiz"))
        incomplete.tap()
        XCTAssertTrue(app.staticTexts["Bu parça asgari nokta sayısını karşılamıyor; kontur çizimi ve ölçüsü gösterilemiyor."].waitForExistence(timeout: 5))
        XCTAssertFalse(app.descendants(matching: .any)["Eksik kontur önizlemesi"].exists)
        let selected = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Eksik")).firstMatch
        selected.tap(); app.buttons["Üçgen"].tap()
        XCTAssertTrue(app.descendants(matching: .any)["Üçgen kontur önizlemesi"].waitForExistence(timeout: 5))
    }

    func testContourLengthTracksSelectedOpenAndClosedPiece() {
        let fixture = #"{"Name":"Ölçü","Pieces":[{"Name":"Açık","Closed":false,"Points":[{"X":0,"Y":0},{"X":3,"Y":0},{"X":3,"Y":4}]},{"Name":"Kapalı","Closed":true,"Points":[{"X":0,"Y":0},{"X":3,"Y":0},{"X":3,"Y":4}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture", "-AppleLanguages", "(en)", "-AppleLocale", "en_US"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 15))
        picker.tap(); app.buttons["Açık"].tap()
        let openLength = app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS %@", "Yaklaşık kontur uzunluğu: 7 mm")).firstMatch
        XCTAssertTrue(openLength.waitForExistence(timeout: 5))
        XCTAssertTrue(app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS %@", "Yaklaşık en: 3 mm • boy: 4 mm")).firstMatch.exists)
        let selected = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Açık")).firstMatch
        selected.tap(); app.buttons["Kapalı"].tap()
        let closedLength = app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS %@", "Yaklaşık kontur uzunluğu: 12 mm")).firstMatch
        XCTAssertTrue(closedLength.waitForExistence(timeout: 5))
        XCTAssertTrue(app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS %@", "Yaklaşık en: 3 mm • boy: 4 mm")).firstMatch.exists)
    }

    func testInvalidProductionQuantityIsNotShownAsValid() {
        let fixture = #"{"Name":"Adet","Pieces":[{"Name":"Geçersiz","Closed":false,"Points":[{"X":0,"Y":0},{"X":3,"Y":4}],"Metadata":{"Quantity":0}},{"Name":"Geçerli","Closed":false,"Points":[{"X":0,"Y":0},{"X":3,"Y":4}],"Metadata":{"Quantity":2}}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 15)); picker.tap()
        let row = app.buttons["Geçersiz"]
        XCTAssertTrue(row.waitForExistence(timeout: 5))
        XCTAssertTrue((row.value as? String ?? "").contains("Adet okunamadı veya geçersiz"))
        row.tap()
        XCTAssertTrue(app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS %@", "Adet okunamadı veya geçersiz")).firstMatch.waitForExistence(timeout: 5))
        XCTAssertFalse(app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS %@", "Adet: 0")).firstMatch.exists)
    }

    func testMalformedPieceMetadataIsReportedWhenSelectingAndViewing() {
        let fixture = #"{"Name":"Üretim","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":3,"Y":4}],"Metadata":"okunamayan"}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 15)); picker.tap()
        let row = app.buttons["Kol"]
        XCTAssertTrue(row.waitForExistence(timeout: 5))
        XCTAssertTrue((row.value as? String ?? "").contains("Üretim bilgileri okunamadı"))
        row.tap()
        XCTAssertTrue(app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS %@", "Üretim bilgileri okunamadı; kaynak dosyayı kontrol edin.")).firstMatch.waitForExistence(timeout: 5))
    }

    func testEmptyProductionMetadataIsDistinguishedFromAbsentMetadata() {
        let fixture = #"{"Name":"Üretim","Pieces":[{"Name":"Boş","Closed":false,"Points":[{"X":0,"Y":0},{"X":3,"Y":4}],"Metadata":{"Role":null}},{"Name":"Yok","Closed":false,"Points":[{"X":0,"Y":0},{"X":3,"Y":4}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 15)); picker.tap()
        let empty = app.buttons["Boş"], absent = app.buttons["Yok"]
        XCTAssertTrue(empty.waitForExistence(timeout: 5)); XCTAssertTrue(absent.exists)
        XCTAssertTrue((empty.value as? String ?? "").contains("Üretim bilgileri boş"))
        XCTAssertFalse((absent.value as? String ?? "").contains("Üretim bilgileri boş"))
        empty.tap()
        XCTAssertTrue(app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS %@", "Üretim bilgileri boş; dosya masaüstünde açılamayabilir")).firstMatch.waitForExistence(timeout: 5))
    }

    func testUnsupportedPieceRoleIsNotPresentedAsAValidRole() {
        let fixture = #"{"Name":"Üretim","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":3,"Y":4}],"Metadata":{"Role":"Bilinmeyen"}}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 15)); picker.tap()
        let row = app.buttons["Kol"]
        XCTAssertTrue(row.waitForExistence(timeout: 5))
        XCTAssertTrue((row.value as? String ?? "").contains("Parça türü okunamadı veya geçersiz"))
        row.tap()
        XCTAssertTrue(app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS %@", "Parça türü okunamadı veya geçersiz")).firstMatch.waitForExistence(timeout: 5))
        XCTAssertFalse(app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS %@", "Rol: Bilinmeyen")).firstMatch.exists)
    }

    func testInvalidSizeIsReportedWhileValidMaterialRemainsVisible() {
        let fixture = #"{"Name":"Üretim","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":3,"Y":4}],"Metadata":{"Size":42,"Material":"Pamuk"}}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 15)); picker.tap()
        let row = app.buttons["Kol"]
        XCTAssertTrue(row.waitForExistence(timeout: 5))
        XCTAssertTrue((row.value as? String ?? "").contains("Beden veya malzeme okunamadı veya geçersiz"))
        XCTAssertTrue((row.value as? String ?? "").contains("Malzeme: Pamuk"))
        row.tap()
        let details = app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS %@", "Beden veya malzeme okunamadı veya geçersiz")).firstMatch
        XCTAssertTrue(details.waitForExistence(timeout: 5))
        XCTAssertTrue(details.label.contains("Malzeme: Pamuk"))
    }

    func testCancellingExportKeepsOpenModelAndDoesNotReportSuccess() {
        let fixture = #"{"Name":"Deneme","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture", "-AppleLanguages", "(en)", "-AppleLocale", "en_US"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let export = app.buttons["Açık modeli dışa aktar…"]
        XCTAssertTrue(export.waitForExistence(timeout: 15))
        export.tap()
        XCTAssertTrue(app.staticTexts["Dışa aktarılan veri, dosyanın açıldığı andaki kopyasıdır. Kaynak başka yerde değişmiş olabilir. Kaynağı korumak için farklı bir dosya adı seçin; aynı hedef seçilirse sonraki değişiklikler üzerine yazılabilir."].waitForExistence(timeout: 5))
        app.buttons["Vazgeç"].tap()
        XCTAssertTrue(export.exists)
        XCTAssertFalse(app.staticTexts["Model seçilen hedefe dışa aktarıldı."].exists)
        export.tap()
        app.buttons["Hedef seç"].tap()
        let cancel = app.buttons["Cancel"]
        XCTAssertTrue(cancel.waitForExistence(timeout: 10))
        cancel.tap()
        XCTAssertTrue(export.waitForExistence(timeout: 10))
        XCTAssertTrue(app.staticTexts["Deneme"].exists)
        XCTAssertFalse(app.staticTexts["Model seçilen hedefe dışa aktarıldı."].exists)
        XCTAssertFalse(app.alerts["İşlem tamamlanamadı"].exists)
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
        search.typeText("42 kol")
        XCTAssertEqual(rows.count, 1)
        XCTAssertTrue((rows.firstMatch.value as? String ?? "").contains("Beden: 42"))
        rows.firstMatch.tap()
        XCTAssertTrue(app.descendants(matching: .any)["Kol kontur önizlemesi"].waitForExistence(timeout: 5))
    }

    func testPiecePickerDistinguishesSameNamedPiecesByDimensions() {
        let fixture = #"{"Name":"Deneme","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":17,"Y":8}]},{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":43,"Y":9}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 15)); picker.tap()
        let rows = app.buttons.matching(identifier: "Kol")
        let first = rows.element(boundBy: 0)
        XCTAssertTrue(first.waitForExistence(timeout: 5))
        XCTAssertTrue((first.value as? String ?? "").contains("Yaklaşık en: 17 mm"))
        XCTAssertTrue((rows.element(boundBy: 1).value as? String ?? "").contains("Yaklaşık en: 43 mm"))
        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 5)); search.tap(); search.typeText("43")
        let count = app.staticTexts["2 parçadan 1 tanesi gösteriliyor"]
        XCTAssertTrue(count.waitForExistence(timeout: 5))
        XCTAssertTrue((rows.firstMatch.value as? String ?? "").contains("Yaklaşık en: 43 mm"))
        rows.firstMatch.tap()
        XCTAssertTrue(app.descendants(matching: .any)["Kol kontur önizlemesi"].waitForExistence(timeout: 5))
    }

    func testPieceSearchCanDistinguishOpenAndClosedContours() {
        let fixture = #"{"Name":"Deneme","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]},{"Name":"Kol","Closed":true,"Points":[{"X":0,"Y":0},{"X":2,"Y":0},{"X":0,"Y":2}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 15)); picker.tap()
        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 5)); search.tap(); search.typeText("kapalı kol")
        let count = app.staticTexts["2 parçadan 1 tanesi gösteriliyor"]
        XCTAssertTrue(count.waitForExistence(timeout: 5))
        let row = app.buttons["Kol"]
        XCTAssertTrue((row.value as? String ?? "").contains("Kapalı kontur"))
        XCTAssertTrue((row.value as? String ?? "").contains("3 nokta"))
        row.tap()
        XCTAssertTrue(app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS %@", "3 nokta • Kapalı kontur")).firstMatch.waitForExistence(timeout: 5))
    }

    func testPiecePickerShowsAndFindsEscapedDirectionalMetadata() {
        let fixture = #"{"Name":"Proje","Pieces":[{"Name":"Kol","Metadata":{"Size":"A\u202EB","Material":"Pamuk"},"Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 15)); picker.tap()
        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 5)); search.tap(); search.typeText("202E")
        let row = app.buttons["Kol"]
        XCTAssertTrue(row.waitForExistence(timeout: 5))
        XCTAssertTrue((row.value as? String ?? "").contains(#"Beden: A\u{202E}B"#))
        row.tap()
        XCTAssertTrue(app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS %@", #"Beden: A\u{202E}B"#)).firstMatch.waitForExistence(timeout: 5))
    }

    func testPiecePickerShowsAndFindsEscapedDirectionalName() {
        let fixture = #"{"Name":"Proje","Pieces":[{"Name":"A\u202EB","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 15)); picker.tap()
        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 5)); search.tap(); search.typeText("202E")
        let row = app.buttons[#"A\u{202E}B"#]
        XCTAssertTrue(row.waitForExistence(timeout: 5)); row.tap()
        XCTAssertTrue(app.descendants(matching: .any)[#"A\u{202E}B kontur önizlemesi"#].waitForExistence(timeout: 5))
    }

    func testPieceSearchShowsResultCountAndCanClearNoMatch() {
        let fixture = #"{"Name":"Deneme","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]},{"Name":"Beden","Closed":false,"Points":[{"X":0,"Y":0},{"X":2,"Y":2}]}]}"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 15)); picker.tap()
        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 5)); search.tap(); search.typeText("Kol")
        XCTAssertTrue(app.staticTexts["2 parçadan 1 tanesi gösteriliyor"].waitForExistence(timeout: 5))
        search.typeText("olmayan")
        XCTAssertTrue(app.staticTexts["2 parçadan 0 tanesi gösteriliyor"].waitForExistence(timeout: 5))
        app.buttons["Aramayı temizle"].tap()
        XCTAssertTrue(app.staticTexts["2 parçadan 2 tanesi gösteriliyor"].waitForExistence(timeout: 5))
        XCTAssertTrue(app.buttons["Beden"].exists)
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
    func testLibraryLoadFailureCanBeRetried() throws {
        let fixture = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "selection", withExtension: "tcad"))
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture", "--ui-test-library-fixture", "--ui-test-library-reset", "--ui-test-library-fail-once"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = try Data(contentsOf: fixture).base64EncodedString()
        app.launch()

        let save = app.buttons["Bu modeli telefona kaydet"]
        XCTAssertTrue(save.waitForExistence(timeout: 15))
        save.tap()
        XCTAssertTrue(app.staticTexts["Model telefona kaydedildi."].waitForExistence(timeout: 10))
        app.buttons["Modellerim"].tap()

        let alert = app.alerts["İşlem tamamlanamadı"]
        XCTAssertTrue(alert.waitForExistence(timeout: 10))
        alert.buttons["Tamam"].tap()
        XCTAssertTrue(app.staticTexts["Kayıtlı modeller yüklenemedi."].waitForExistence(timeout: 5))
        XCTAssertFalse(app.staticTexts["Henüz kayıtlı model yok. Bir .tcad açıp telefona kaydedebilirsiniz."].exists)
        app.buttons["Tekrar dene"].tap()
        XCTAssertTrue(app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Deneme Modeli")).firstMatch.waitForExistence(timeout: 10))
        XCTAssertFalse(app.staticTexts["Kayıtlı modeller yüklenemedi."].exists)
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
        XCTAssertTrue(app.staticTexts["1 kayıttan 1 tanesi gösteriliyor"].waitForExistence(timeout: 5))
        search.typeText("-no-match")
        XCTAssertTrue(app.staticTexts["Eşleşen model bulunamadı."].waitForExistence(timeout: 5))
        XCTAssertTrue(app.staticTexts["1 kayıttan 0 tanesi gösteriliyor"].exists)
        app.buttons["Aramayı temizle"].tap()
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
        XCTAssertTrue(app.buttons["İplik yönlerini göster"].waitForExistence(timeout: 5))
        app.buttons["Konturu büyük görünümde incele"].tap()
        XCTAssertTrue(app.buttons["İplik yönlerini göster"].waitForExistence(timeout: 5))
    }

    func testNotchSearchDistinguishesSameNamedPiecesBySize() {
        let fixture = #"[6,{"Name":"Bedenler","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":10,"Y":10}],"Metadata":{"Size":"S"},"Notches":[{"VertexIndex":0,"Position":{"X":0,"Y":0}}]},{"Name":"Kol","Closed":false,"Points":[{"X":20,"Y":20},{"X":30,"Y":30}],"Metadata":{"Size":"XL"},"Notches":[{"VertexIndex":1,"Position":{"X":30,"Y":30}}]}]}]"#
        let app = XCUIApplication()
        app.launchArguments += ["--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let picker = app.buttons["Köşe işaretlerini ara ve seç…"]
        XCTAssertTrue(picker.waitForExistence(timeout: 15))
        picker.tap()
        XCTAssertTrue(app.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "Beden: S")).firstMatch.exists)
        XCTAssertTrue(app.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "Beden: XL")).firstMatch.exists)
        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 10))
        search.tap()
        search.typeText("XL")
        XCTAssertFalse(app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Nokta 0")).firstMatch.exists)
        let notch = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Nokta 1")).firstMatch
        XCTAssertTrue(notch.waitForExistence(timeout: 5))
        notch.tap()
        XCTAssertTrue(app.otherElements["Kol kontur önizlemesi"].waitForExistence(timeout: 5))
        XCTAssertTrue(app.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "Nokta 1")).firstMatch.exists)
    }

    func testPieceAndNotchSearchAcceptUnaccentedNames() {
        let fixture = #"[6,{"Name":"Arama","Pieces":[{"Name":"Ön Beden","Closed":false,"Points":[{"X":0,"Y":0},{"X":10,"Y":10}],"Notches":[{"VertexIndex":0,"Position":{"X":0,"Y":0}}]},{"Name":"Arka Beden","Closed":false,"Points":[{"X":0,"Y":0},{"X":5,"Y":5}]}]}]"#
        let app = XCUIApplication()
        app.launchArguments += ["-AppleLanguages", "(en)", "-AppleLocale", "en_US", "--ui-test-fixture"]
        app.launchEnvironment["TCAD_UI_TEST_FIXTURE"] = Data(fixture.utf8).base64EncodedString()
        app.launch()
        let picker = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Tüm parçalar")).firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 15))
        picker.tap()
        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 10))
        search.tap()
        search.typeText("on beden")
        XCTAssertTrue(app.buttons["Ön Beden"].waitForExistence(timeout: 5))
        XCTAssertFalse(app.buttons["Arka Beden"].exists)
        app.buttons["Ön Beden"].tap()
        let notchPicker = app.buttons["Köşe işaretlerini ara ve seç…"]
        XCTAssertTrue(notchPicker.waitForExistence(timeout: 10))
        notchPicker.tap()
        let notchSearch = app.searchFields.firstMatch
        XCTAssertTrue(notchSearch.waitForExistence(timeout: 10))
        notchSearch.tap()
        notchSearch.typeText("on beden")
        XCTAssertTrue(app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Nokta 0")).firstMatch.waitForExistence(timeout: 5))
    }
}
