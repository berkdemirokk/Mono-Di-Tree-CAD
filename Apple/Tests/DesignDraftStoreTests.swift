import XCTest
@testable import TekstilCADViewer_iOS

#if DEBUG
@MainActor
final class DesignDraftStoreTests: XCTestCase {
    func testRoundTripAndFailedWritePreservesPrevious() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("draft.json")
        let png = Data(base64Encoded: "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/aX8AAAAASUVORK5CYII=")!
        let original = ExperimentalDesignBrief.Draft(style: .aline, description: "Tarif", bust: "92 cm", waist: "", hips: "", length: "", otherMeasurements: "", image: png, imageName: "örnek.png")
        let revision = try ExperimentalDesignBrief.DraftStore.save(original, to: url, expected: nil)
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.load(url)?.draft, original)
        let changed = ExperimentalDesignBrief.Draft(style: .wrap, description: "Yeni", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        XCTAssertThrowsError(try ExperimentalDesignBrief.DraftStore.save(changed, to: url, expected: revision, write: { _, _ in throw CocoaError(.fileWriteOutOfSpace) }))
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.load(url)?.draft, original)
        _ = try ExperimentalDesignBrief.DraftStore.save(changed, to: url, expected: revision)
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.load(url)?.draft, changed)
    }
    func testCorruptExistingFileIsNotOverwritten() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("draft.json"), bad = Data("corrupt".utf8)
        try bad.write(to: url)
        let draft = ExperimentalDesignBrief.Draft(style: .straight, description: "", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        XCTAssertThrowsError(try ExperimentalDesignBrief.DraftStore.save(draft, to: url, expected: nil))
        XCTAssertEqual(try Data(contentsOf: url), bad)
    }
    func testEmptyDraftAndCompetingWindows() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("draft.json")
        let first = ExperimentalDesignBrief.Draft(style: .describe, description: "Önce", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let empty = ExperimentalDesignBrief.Draft(style: .describe, description: "", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let rev1 = try ExperimentalDesignBrief.DraftStore.save(first, to: url, expected: nil)
        let rev2 = try ExperimentalDesignBrief.DraftStore.save(empty, to: url, expected: rev1)
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.load(url)?.draft, empty)
        XCTAssertThrowsError(try ExperimentalDesignBrief.DraftStore.save(first, to: url, expected: rev1))
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.load(url)?.revision, rev2)
    }
    func testRecoveryCopyPreservesEmptyAndMeasurementOnlyDrafts() throws {
        let empty = ExperimentalDesignBrief.Draft(style: .describe, description: "", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let measurement = ExperimentalDesignBrief.Draft(style: .describe, description: "", bust: "92 cm", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        for value in [empty, measurement] {
            let encoded = try ExperimentalDesignBrief.DraftStore.recoveryData(value)
            XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.recoveryDraft(encoded), value)
        }
        let longDraft = ExperimentalDesignBrief.Draft(style: .describe, description: String(repeating: "ü", count: 16_385), bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        XCTAssertThrowsError(try ExperimentalDesignBrief.DraftStore.validate(longDraft))
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.recoveryDraft(ExperimentalDesignBrief.DraftStore.recoveryData(longDraft)), longDraft)
        XCTAssertThrowsError(try ExperimentalDesignBrief.DraftStore.recoveryDraft(Data("invalid".utf8)))
        XCTAssertThrowsError(try ExperimentalDesignBrief.DraftStore.recoveryDraft(Data(repeating: 0, count: ExperimentalDesignBrief.DraftStore.recoveryMaxBytes + 1)))
    }
    func testSerialWriterCoalescesEditsAndFlushUsesLatest() {
        let finished = expectation(description: "latest flush")
        let first = ExperimentalDesignBrief.Draft(style: .describe, description: "ilk", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let last = ExperimentalDesignBrief.Draft(style: .describe, description: "son", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let writer = ExperimentalDesignBrief.DraftWriter { value, _, revision in
            XCTAssertEqual(value, last); XCTAssertNil(revision); return Data([42])
        }
        let path = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        writer.configure(revision: nil)
        writer.submit(first, to: path) { _ in XCTFail("Eski bekleyen yazı çalışmamalı") }
        writer.submit(last, to: path, immediately: true) { result in
            if case .failure = result { XCTFail("Son taslak yazılmalı") }
            finished.fulfill()
        }
        wait(for: [finished], timeout: 3)
    }
    func testWriterReportsConflictWithoutReplacingRevision() {
        let finished = expectation(description: "conflict")
        let draft = ExperimentalDesignBrief.Draft(style: .describe, description: "yeni", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let writer = ExperimentalDesignBrief.DraftWriter { _, _, revision in
            XCTAssertEqual(revision, Data([1])); throw ExperimentalDesignBrief.DraftStore.Conflict.changed
        }
        writer.configure(revision: Data([1]))
        writer.submit(draft, to: FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString), immediately: true) { result in
            if case .failure(let error) = result { XCTAssertTrue(error is ExperimentalDesignBrief.DraftStore.Conflict) }
            else { XCTFail("Çakışma bildirilmeli") }
            finished.fulfill()
        }
        wait(for: [finished], timeout: 3)
    }
    func testPauseWaitsForStartedWriteBeforeAcknowledging() {
        let started = expectation(description: "write started"), paused = expectation(description: "barrier finished")
        let release = DispatchSemaphore(value: 0)
        let writer = ExperimentalDesignBrief.DraftWriter { _, _, _ in
            started.fulfill(); _ = release.wait(timeout: .now() + 3); return Data([1])
        }
        let draft = ExperimentalDesignBrief.Draft(style: .describe, description: "içerik", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        writer.configure(revision: nil)
        writer.submit(draft, to: FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString), immediately: true) { _ in }
        wait(for: [started], timeout: 2)
        writer.pause { paused.fulfill() }
        DispatchQueue.global().asyncAfter(deadline: .now() + 0.1) { release.signal() }
        wait(for: [paused], timeout: 3)
    }
    func testImmediateCloseFlushReportsWriteFailure() {
        let finished = expectation(description: "failure delivered")
        let writer = ExperimentalDesignBrief.DraftWriter { _, _, _ in throw CocoaError(.fileWriteOutOfSpace) }
        let draft = ExperimentalDesignBrief.Draft(style: .describe, description: "korunacak", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        writer.configure(revision: nil)
        writer.submit(draft, to: FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString), immediately: true) { result in
            if case .success = result { XCTFail("Yazma hatası kapatma akışına iletilmeli") }
            finished.fulfill()
        }
        wait(for: [finished], timeout: 3)
    }
    func testAcknowledgingStaleWarningsDoesNotRemoveNewOrActiveScene() {
        let displayed: Set<String> = ["old", "active"]
        let now: Set<String> = ["old", "active", "new"]
        XCTAssertEqual(DraftPendingLedger.remaining(current: now, displayed: displayed, active: ["active"]), Set(["active", "new"]))
        XCTAssertEqual(DraftPendingLedger.remaining(current: ["new"], displayed: ["old"], active: []), Set(["new"]))
    }
    func testExpiredButRunningMarkerSurvivesAcknowledgement() {
        let visible: Set<String> = ["running"]
        // Expiration alone does not remove an active writer's identity.
        XCTAssertEqual(DraftPendingLedger.remaining(current: visible, displayed: visible, active: visible), visible)
        // Only after the writer completes or a queue barrier confirms it stopped can it be acknowledged.
        XCTAssertEqual(DraftPendingLedger.remaining(current: visible, displayed: visible, active: []), Set<String>())
    }
    func testOldBarrierDoesNotDeactivateNewerWriteIdentity() {
        let old: Set<String> = ["old"]
        let active: Set<String> = ["old", "new"]
        let afterOldBarrier = active.subtracting(old)
        XCTAssertEqual(DraftPendingLedger.remaining(current: ["old", "new"], displayed: ["old", "new"], active: afterOldBarrier), Set(["new"]))
    }
    func testCorruptLedgerAndResetRace() {
        XCTAssertNil(DraftPendingLedger.decode("{broken"))
        XCTAssertNil(DraftPendingLedger.decode("{\"old\":true}"))
        XCTAssertEqual(DraftPendingLedger.decode("[\"a\",\"b\"]"), Set(["a", "b"]))
        let shown = Data([1]), changed = Data([2])
        XCTAssertFalse(DraftPendingLedger.mayReset(expected: shown, current: changed, hasActiveWrites: false))
        XCTAssertFalse(DraftPendingLedger.mayReset(expected: shown, current: shown, hasActiveWrites: true))
        XCTAssertTrue(DraftPendingLedger.mayReset(expected: shown, current: shown, hasActiveWrites: false))
    }
    func testPortableRecoveryFixture() throws {
        let path = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "dress-draft-v1", withExtension: "json"))
        let draft = try ExperimentalDesignBrief.DraftStore.recoveryDraft(Data(contentsOf: path))
        XCTAssertEqual(draft.style, .aline)
        XCTAssertEqual(draft.description, "Diz altı, uzun kollu")
        XCTAssertEqual(draft.bust, "92 cm")
        XCTAssertNil(draft.image)
    }
    func testPortableRequestFixturePreservesUnicodeAndImage() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "portable-design-request-v1", withExtension: "json"))
        let original = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: Data(contentsOf: url))
        XCTAssertEqual(original.format, "tekstilcad.design-request")
        XCTAssertEqual(original.version, 1)
        let reference = try XCTUnwrap(original.reference)
        let image = try XCTUnwrap(Data(base64Encoded: reference.dataBase64))
        let output = try ExperimentalDesignBrief.make(prompt: original.prompt, measurements: original.measurements, image: image, imageName: reference.name)
        let roundTrip = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: output)
        XCTAssertEqual(roundTrip.prompt, original.prompt)
        XCTAssertEqual(roundTrip.measurements, original.measurements)
        XCTAssertEqual(roundTrip.reference?.dataBase64, reference.dataBase64)
        XCTAssertEqual(roundTrip.reference?.mediaType, reference.mediaType)
        let imported = try ExperimentalDesignBrief.portableDraft(Data(contentsOf: url))
        XCTAssertEqual(imported.prompt, original.prompt)
        XCTAssertEqual(imported.measurements, original.measurements)
        XCTAssertEqual(imported.image, image)
        let badVersion = Data(#"{"format":"tekstilcad.design-request","version":2,"prompt":"x","measurements":"","reference":null}"#.utf8)
        XCTAssertThrowsError(try ExperimentalDesignBrief.portableDraft(badVersion))
        let whitespace = try ExperimentalDesignBrief.make(prompt: "x", measurements: "  ", image: nil, imageName: nil)
        XCTAssertEqual(try ExperimentalDesignBrief.portableDraft(whitespace).measurements, "  ")
    }
    func testPortableImportRejectsUnknownAndDuplicateFields() {
        let cases = [
            #"{"format":"tekstilcad.design-request","version":1,"prompt":"x","measurements":"","reference":null,"future":true}"#,
            #"{"format":"tekstilcad.design-request","version":1,"prompt":"x","prompt":"y","measurements":"","reference":null}"#,
            #"{"format":"tekstilcad.design-request","version":1,"prompt":"x","measurements":"","reference":{"name":"a.png","mediaType":"image/png","dataBase64":"AA==","future":1}}"#,
            #"{"format":"tekstilcad.design-request","version":1,"prompt":"x","measurements":"","reference":{"name":"a.png","name":"b.png","mediaType":"image/png","dataBase64":"AA=="}}"#
        ]
        for json in cases { XCTAssertThrowsError(try ExperimentalDesignBrief.portableDraft(Data(json.utf8))) }
    }
    func testPortableBase64AcceptsDesktopWhitespaceWithoutChangingBytes() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "portable-design-request-v1", withExtension: "json"))
        let source = try String(contentsOf: url, encoding: .utf8)
        let spaced = source.replacingOccurrences(of: "iVBORw0K", with: "iVBO \\nRw0K")
        let a = try ExperimentalDesignBrief.portableDraft(Data(source.utf8))
        let b = try ExperimentalDesignBrief.portableDraft(Data(spaced.utf8))
        XCTAssertEqual(a.image, b.image)
    }
    func testPortableRequestEditAndExportPreservesFieldsAndImage() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "portable-design-request-v1", withExtension: "json"))
        let original = try ExperimentalDesignBrief.portableDraft(Data(contentsOf: url))
        XCTAssertEqual(original.style, .describe) // v1 has no model ID; do not infer it.
        let editedText = original.description + "\nKol: uzun 👗"
        let whitespaceMeasurements = "  \t\n "
        let output = try ExperimentalDesignBrief.make(prompt: editedText, measurements: whitespaceMeasurements,
                                                       image: original.image, imageName: original.imageName)
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let originalPath = dir.appendingPathComponent("original.tcad-request.json")
        let newPath = dir.appendingPathComponent("edited.tcad-request.json")
        let originalBytes = try Data(contentsOf: url)
        try originalBytes.write(to: originalPath, options: .withoutOverwriting)
        try output.write(to: newPath, options: .withoutOverwriting)
        XCTAssertEqual(try Data(contentsOf: originalPath), originalBytes)
        let edited = try ExperimentalDesignBrief.portableDraft(Data(contentsOf: newPath))
        XCTAssertEqual(edited.description, editedText)
        XCTAssertEqual(edited.measurements, whitespaceMeasurements)
        XCTAssertEqual(edited.image, original.image)
        XCTAssertEqual(edited.imageName, original.imageName)
        XCTAssertEqual(original.measurements, "Göğüs çevresi: 92 cm\nBel çevresi: 72 cm")
    }
    func testMalformedImportDoesNotChangeExistingDraftOrImage() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "portable-design-request-v1", withExtension: "json"))
        let old = try ExperimentalDesignBrief.portableDraft(Data(contentsOf: url))
        var current = old
        do { current = try ExperimentalDesignBrief.portableDraft(Data("{broken".utf8)); XCTFail("Bozuk belge reddedilmeli") }
        catch { XCTAssertEqual(current, old); XCTAssertEqual(current.image, old.image) }
    }
    func testReferenceSizeBoundariesPreserveEarlierDraft() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "portable-design-request-v1", withExtension: "json"))
        let prior = try ExperimentalDesignBrief.portableDraft(Data(contentsOf: url))
        var maximum = try XCTUnwrap(prior.image)
        maximum.append(Data(count: ExperimentalDesignBrief.maxImageBytes - maximum.count))
        let output = try ExperimentalDesignBrief.make(prompt: "Düz", measurements: "", image: maximum, imageName: "örnek.png")
        let restored = try ExperimentalDesignBrief.portableDraft(output)
        XCTAssertEqual(restored.image, maximum)
        var oversized = maximum; oversized.append(0)
        XCTAssertThrowsError(try ExperimentalDesignBrief.make(prompt: "Düz", measurements: "", image: oversized, imageName: "örnek.png"))
        XCTAssertEqual(prior.image, try ExperimentalDesignBrief.portableDraft(Data(contentsOf: url)).image)
    }
    func testFifteenMeasurementFieldsAppendOnlyOnExplicitApply() throws {
        XCTAssertEqual(BodyMeasurementNotes.fields.count, 15)
        var values = Array(repeating: "", count: 15)
        values[0] = "310"; values[12] = "-12,5"
        let old = "Mevcut not 👗"
        let result = try BodyMeasurementNotes.append(values: values, unit: "mm", current: old, existingNotes: old)
        XCTAssertEqual(result, "Mevcut not 👗\nüst kol çevresi 310 mm\nomuz eğimi -12,5 derece")
        XCTAssertEqual(old, "Mevcut not 👗")
        XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "mm", current: old, existingNotes: "üst kol çevresi 32 cm"))
        values[12] = "90"
        XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "cm", current: old, existingNotes: old))
        values[12] = ""; values[0] = "10001"
        XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "mm", current: old, existingNotes: old))
        XCTAssertThrowsError(try BodyMeasurementNotes.append(values: Array(repeating: "", count: 15), unit: "cm", current: old, existingNotes: old))
    }
    func testMeasurementUnknownNoteDoesNotImplyExistingValue() throws {
        var values = Array(repeating: "", count: 15); values[8] = "72"
        let current = "bel çevresi ölçülmedi"
        let result = try BodyMeasurementNotes.append(values: values, unit: "cm", current: current, existingNotes: current)
        XCTAssertEqual(result, "bel çevresi ölçülmedi\nbel çevresi 72 cm")
        XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "cm", current: current, existingNotes: "bel çevresi: 70 cm"))
    }
    func testMeasurementNotesAllowExactTextLimit() throws {
        var values = Array(repeating: "", count: 15); values[8] = "72"
        let line = "bel çevresi 72 cm"
        let base = String(repeating: "x", count: ExperimentalDesignBrief.maxTextUnits - line.utf16.count - 1)
        let result = try BodyMeasurementNotes.append(values: values, unit: "cm", current: base, existingNotes: base)
        XCTAssertEqual(result.utf16.count, ExperimentalDesignBrief.maxTextUnits)
        XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "cm", current: base + "x", existingNotes: base))
    }
    func testMeasurementConflictAndAmbiguousNoteRequireReview() throws {
        var values = Array(repeating: "", count: 15); values[8] = "72"
        for old in ["bel çevresi yaklaşık 70 cm", "bel çevresi = 70 cm", "Not: bel çevresi 70 cm"] {
            XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "cm", current: old, existingNotes: old, allowAmbiguous: true))
        }
        let ambiguous = "bel çevresi daha önce kontrol edilecek"
        XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "cm", current: ambiguous, existingNotes: ambiguous))
        XCTAssertEqual(try BodyMeasurementNotes.append(values: values, unit: "cm", current: ambiguous, existingNotes: ambiguous, allowAmbiguous: true), ambiguous + "\nbel çevresi 72 cm")
        let unknown = "bel çevresi ölçülmedi"
        XCTAssertEqual(try BodyMeasurementNotes.append(values: values, unit: "cm", current: unknown, existingNotes: unknown), unknown + "\nbel çevresi 72 cm")
    }
    func testImportedModelLabelIsNeverSilentlyReplaced() throws {
        let old = "Seçilen elbise modeli: A kesim elbise\nİpek 👗"
        let windows = try ExperimentalDesignBrief.make(prompt: old, measurements: "Bel çevresi: 72 cm", image: nil, imageName: nil)
        let imported = try ExperimentalDesignBrief.portableDraft(windows)
        XCTAssertEqual(imported.style, .describe)
        XCTAssertEqual(imported.prompt, old)
        XCTAssertTrue(ExperimentalDesignBrief.hasConflictingModelLabels(style: .wrap, description: imported.description))
        XCTAssertFalse(ExperimentalDesignBrief.hasConflictingModelLabels(style: .describe, description: imported.description))
        let preserved = try ExperimentalDesignBrief.make(prompt: imported.prompt, measurements: imported.measurements, image: nil, imageName: nil)
        let reopened = try ExperimentalDesignBrief.portableDraft(preserved)
        XCTAssertEqual(reopened.prompt, old)
        XCTAssertEqual(reopened.measurements, "Bel çevresi: 72 cm")
        XCTAssertTrue(ExperimentalDesignBrief.hasEmbeddedModelLine("Not\n SEÇİLEN ELBİSE MODELİ: Düz elbise"))
        XCTAssertFalse(ExperimentalDesignBrief.hasEmbeddedModelLine("İpek elbise"))
        XCTAssertTrue(ExperimentalDesignBrief.hasEmbeddedModelLine("Model: A kesim elbise"))
        XCTAssertTrue(ExperimentalDesignBrief.hasEmbeddedModelLine("Seçilen elbise modeli : A kesim elbise"))
        XCTAssertTrue(ExperimentalDesignBrief.hasConflictingModelLabels(style: .wrap, description: "Model: A kesim elbise"))
        XCTAssertFalse(ExperimentalDesignBrief.hasEmbeddedModelLine("Modelci: öneri"))
    }
    func testEmptyMeasurementFormDoesNotBlockRegardlessOfUnit() {
        let empty = Array(repeating: "", count: 15)
        XCTAssertNil(ExperimentalDesignBrief.MeasurementForm.pending(values: empty, unit: "cm"))
        XCTAssertNil(ExperimentalDesignBrief.MeasurementForm.pending(values: empty, unit: "mm"))
        let legacyEmpty = ExperimentalDesignBrief.MeasurementForm(values: empty, unit: "mm")
        XCTAssertFalse(legacyEmpty.hasValues)
        let draft = ExperimentalDesignBrief.Draft(style: .describe, description: "", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil, measurementForm: legacyEmpty)
        XCTAssertFalse(draft.hasInput)
        var entered = empty; entered[8] = "72"
        let form = ExperimentalDesignBrief.MeasurementForm.pending(values: entered, unit: "mm")
        XCTAssertEqual(form?.values[8], "72"); XCTAssertEqual(form?.unit, "mm")
        var spaces = empty; spaces[0] = " "
        XCTAssertNotNil(ExperimentalDesignBrief.MeasurementForm.pending(values: spaces, unit: "mm"))
    }
    func testWhitespaceFormRequiresExplicitCleanupWithoutChangingRealValues() throws {
        var values = Array(repeating: "", count: 15)
        values[0] = " \t"; values[8] = " 72 "
        let before = ExperimentalDesignBrief.MeasurementForm.pending(values: values, unit: "mm")
        XCTAssertNotNil(before)
        XCTAssertEqual(BodyMeasurementNotes.whitespaceOnlyIndices(values), [0])
        XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "mm", current: "önceki", existingNotes: "önceki"))
        XCTAssertEqual(before?.values, values)
        let after = BodyMeasurementNotes.clearingWhitespaceOnly(values)
        XCTAssertEqual(after[0], ""); XCTAssertEqual(after[8], " 72 ")
        XCTAssertEqual(try BodyMeasurementNotes.append(values: after, unit: "mm", current: "önceki", existingNotes: "önceki"), "önceki\nbel çevresi 72 mm")
        XCTAssertNil(ExperimentalDesignBrief.MeasurementForm.pending(values: BodyMeasurementNotes.clearingWhitespaceOnly([" ", "\t"]), unit: "cm"))
    }
    func testUnapprovedMeasurementFormRoundTripDoesNotModifyNotes() throws {
        var fields = Array(repeating: "", count: 15); fields[0] = "310"; fields[12] = "-12,5"
        let form = ExperimentalDesignBrief.MeasurementForm(values: fields, unit: "mm")
        let original = ExperimentalDesignBrief.Draft(style: .aline, description: "Tarif 👗", bust: "92 cm", waist: "", hips: "", length: "", otherMeasurements: "eski not", image: nil, imageName: nil, measurementForm: form)
        XCTAssertEqual(original.measurements, "Göğüs çevresi: 92 cm\neski not")
        let bytes = try ExperimentalDesignBrief.DraftStore.recoveryData(original)
        let reopened = try ExperimentalDesignBrief.DraftStore.recoveryDraft(bytes)
        XCTAssertEqual(reopened.measurementForm, form)
        XCTAssertEqual(reopened.measurements, original.measurements)
        let parsed = try XCTUnwrap(JSONSerialization.jsonObject(with: bytes) as? [String: Any])
        XCTAssertEqual(parsed["version"] as? Int, 2)
        var downgraded = parsed; downgraded["version"] = 1
        XCTAssertThrowsError(try ExperimentalDesignBrief.DraftStore.recoveryDraft(JSONSerialization.data(withJSONObject: downgraded)))
        let oldFixture = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "dress-draft-v1", withExtension: "json"))
        XCTAssertNil(try ExperimentalDesignBrief.DraftStore.recoveryDraft(Data(contentsOf: oldFixture)).measurementForm)
    }
    func testUnapprovedFormRevisionConflictRetainsPriorSnapshot() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("draft.json")
        var fields = Array(repeating: "", count: 15); fields[8] = "72"
        let draft = ExperimentalDesignBrief.Draft(style: .describe, description: "", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil, measurementForm: .init(values: fields, unit: "cm"))
        let first = try ExperimentalDesignBrief.DraftStore.save(draft, to: url, expected: nil)
        let second = try ExperimentalDesignBrief.DraftStore.save(draft, to: url, expected: first)
        XCTAssertThrowsError(try ExperimentalDesignBrief.DraftStore.save(draft, to: url, expected: first))
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.load(url)?.revision, second)
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.load(url)?.draft.measurementForm, draft.measurementForm)
    }
    func testReferenceChangesRequireMatchingOriginalAndPreserveBytes() throws {
        let original = ExperimentalDesignBrief.ReferenceSelection(data: Data([1, 2]), name: "ilk.png")
        let replacement = ExperimentalDesignBrief.ReferenceSelection(data: Data([3, 4]), name: "yeni.png")
        let changedElsewhere = ExperimentalDesignBrief.ReferenceSelection(data: Data([5]), name: "başka.png")
        let replace = ExperimentalDesignBrief.ReferenceChange(original: original, replacement: replacement)
        XCTAssertEqual(try replace.apply(to: original), replacement)
        XCTAssertThrowsError(try replace.apply(to: changedElsewhere))
        XCTAssertEqual(original.data, Data([1, 2]))
        let remove = ExperimentalDesignBrief.ReferenceChange(original: original, replacement: nil)
        XCTAssertNil(try remove.apply(to: original))
        XCTAssertThrowsError(try remove.apply(to: changedElsewhere))
    }
    func testCoordinatedImageReadAndLimit() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let file = dir.appendingPathComponent("foto.png")
        let bytes = Data([137, 80, 78, 71])
        try bytes.write(to: file)
        XCTAssertEqual(try ExperimentalDesignBrief.readImage(file), bytes)
        try Data(count: ExperimentalDesignBrief.maxImageBytes + 1).write(to: file)
        XCTAssertThrowsError(try ExperimentalDesignBrief.readImage(file))
    }
    func testReferenceLoadTicketRejectsCancellationAndChangedSelection() {
        let old = ExperimentalDesignBrief.ReferenceSelection(data: Data([1]), name: "önce.png")
        let other = ExperimentalDesignBrief.ReferenceSelection(data: Data([2]), name: "sonra.png")
        let first = ExperimentalDesignBrief.ReferenceLoadTicket(original: old)
        let second = ExperimentalDesignBrief.ReferenceLoadTicket(original: old)
        XCTAssertTrue(first.matches(id: first.id, current: old))
        XCTAssertFalse(first.matches(id: nil, current: old))
        XCTAssertFalse(first.matches(id: second.id, current: old))
        XCTAssertFalse(first.matches(id: first.id, current: other))
        XCTAssertFalse(first.matches(id: first.id, current: nil))
    }
    func testReferenceFlowBlocksNavigationAndExportUntilResolved() throws {
        let pending = ExperimentalDesignBrief.referenceDecisionPending
        XCTAssertTrue(pending(true, false, false)) // Loading or a slow provider.
        XCTAssertTrue(pending(false, true, true))  // Awaiting replacement approval.
        XCTAssertTrue(pending(false, false, true)) // Candidate still unresolved.
        XCTAssertTrue(pending(false, true, false)) // Dialog dismissal in flight.
        XCTAssertFalse(pending(false, false, false)) // Applied, cancelled, or failed.
        let original = ExperimentalDesignBrief.ReferenceSelection(data: Data([1, 2]), name: "eski.png")
        let candidate = ExperimentalDesignBrief.ReferenceSelection(data: Data([3, 4]), name: "yeni.png")
        let change = ExperimentalDesignBrief.ReferenceChange(original: original, replacement: candidate)
        // Cancel/error leave the original unchanged; an approved change is explicit.
        var current = original
        XCTAssertEqual(current, original)
        current = try XCTUnwrap(change.apply(to: current))
        XCTAssertEqual(current, candidate)
        XCTAssertThrowsError(try change.apply(to: current))
    }
    func testRequestSummaryUsesExactSnapshotAndDoesNotInferModel() {
        let form = ExperimentalDesignBrief.MeasurementForm(values: ["72"] + Array(repeating: "", count: 14), unit: "cm")
        let draft = ExperimentalDesignBrief.Draft(style: .describe, description: "Model: A kesim 👗", bust: "92 cm", waist: "", hips: "", length: "", otherMeasurements: "  ", image: Data([1]), imageName: "örnek.png", measurementForm: form)
        let review = ExperimentalDesignBrief.RequestSummary(draft)
        XCTAssertNil(review.model)
        XCTAssertEqual(review.description, "Model: A kesim 👗")
        XCTAssertEqual(review.exportedPrompt, draft.prompt)
        XCTAssertEqual(review.measurements, "Göğüs çevresi: 92 cm\n  ")
        XCTAssertEqual(review.referenceName, "örnek.png")
        XCTAssertTrue(review.hasPendingMeasurements)
    }
    func testSummaryFlagsWhitespaceWithoutAlteringDraft() {
        let draft = ExperimentalDesignBrief.Draft(style: .describe, description: " \n", bust: "\t", waist: "72 cm", hips: "", length: "", otherMeasurements: "  ", image: nil, imageName: nil)
        let summary = ExperimentalDesignBrief.RequestSummary(draft)
        XCTAssertTrue(summary.descriptionIsWhitespace)
        XCTAssertEqual(summary.whitespaceMeasurementLabels, ["Göğüs çevresi", "Ek ölçüler veya notlar"])
        XCTAssertEqual(summary.description, " \n")
        XCTAssertEqual(summary.measurements, draft.measurements)
        XCTAssertEqual(draft.bust, "\t"); XCTAssertEqual(draft.otherMeasurements, "  ")
        XCTAssertFalse(ExperimentalDesignBrief.RequestSummary.isOnlyWhitespace(""))
        XCTAssertFalse(ExperimentalDesignBrief.RequestSummary.isOnlyWhitespace(" 72 cm "))
    }
    func testRequestSummarySeparatesSelectedModelAndExactOutput() {
        let draft = ExperimentalDesignBrief.Draft(style: .wrap, description: "İpek", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let review = ExperimentalDesignBrief.RequestSummary(draft)
        XCTAssertEqual(review.model, "Anvelop elbise")
        XCTAssertEqual(review.description, "İpek")
        XCTAssertEqual(review.exportedPrompt, "Seçilen elbise modeli: Anvelop elbise\nİpek")
        XCTAssertNil(review.referenceName); XCTAssertFalse(review.hasPendingMeasurements)
    }
    func testAtelierOverviewReflectsOnlyEnteredMeasurementsAndReference() {
        let empty = ExperimentalDesignBrief.Draft(style: .describe, description: "", bust: "", waist: " ", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let emptyView = ExperimentalDesignBrief.AtelierOverview(empty)
        XCTAssertEqual(emptyView.bust, "Girilmedi"); XCTAssertEqual(emptyView.waist, "Girilmedi")
        XCTAssertFalse(emptyView.hasOtherMeasurements); XCTAssertNil(emptyView.referenceName)
        let form = ExperimentalDesignBrief.MeasurementForm(values: ["72"] + Array(repeating: "", count: 14), unit: "cm")
        let entered = ExperimentalDesignBrief.Draft(style: .aline, description: "Tarif", bust: "92 cm", waist: "72 cm", hips: "", length: "110 cm", otherMeasurements: "", image: Data([1]), imageName: "örnek.png", measurementForm: form)
        let overview = ExperimentalDesignBrief.AtelierOverview(entered)
        XCTAssertEqual(overview.bust, "92 cm"); XCTAssertEqual(overview.waist, "72 cm")
        XCTAssertEqual(overview.hips, "Girilmedi"); XCTAssertTrue(overview.hasOtherMeasurements)
        XCTAssertTrue(overview.hasPendingMeasurements); XCTAssertEqual(overview.referenceName, "örnek.png")
        XCTAssertEqual(entered.bust, "92 cm")
    }
    func testCompletePortableRequestPreservesAmbiguityWhitespaceAndImage() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "ios-complete-request.tcad-request", withExtension: "json"))
        let bytes = try Data(contentsOf: url)
        let original = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: bytes)
        let draft = try ExperimentalDesignBrief.portableDraft(bytes)
        XCTAssertEqual(draft.prompt, original.prompt)
        XCTAssertEqual(draft.measurements, original.measurements)
        XCTAssertEqual(draft.image, Data(base64Encoded: try XCTUnwrap(original.reference).dataBase64))
        XCTAssertEqual(draft.imageName, original.reference?.name)
        let output = try ExperimentalDesignBrief.make(prompt: draft.prompt, measurements: draft.measurements, image: draft.image, imageName: draft.imageName)
        let reopened = try ExperimentalDesignBrief.portableDraft(output)
        XCTAssertEqual(reopened.prompt, draft.prompt)
        XCTAssertEqual(reopened.measurements, draft.measurements)
        XCTAssertEqual(reopened.image, draft.image)
        let entered = ExperimentalDesignBrief.measurements(bust: "92 cm", waist: "  ", hips: "", length: "", other: "bel çevresi yaklaşık 72 cm olsun; kalça çevresi 98 cm olmasın")
        XCTAssertEqual(entered, original.measurements)
    }
    func testPortableV2ModelRoundTripAndRejectsAmbiguity() throws {
        let image = try XCTUnwrap(Data(base64Encoded: "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/aO0AAAAASUVORK5CYII="))
        let original = ExperimentalDesignBrief.Draft(style: .aline, description: "İpek 👗", bust: "92 cm", waist: "", hips: "", length: "", otherMeasurements: "bel çevresi 72 cm olmasın", image: image, imageName: "örnek.png")
        let bytes = try ExperimentalDesignBrief.makeV2(original)
        let reopened = try ExperimentalDesignBrief.portableDraft(bytes)
        XCTAssertEqual(reopened.style, .aline)
        XCTAssertEqual(reopened.description, original.description)
        XCTAssertEqual(reopened.measurements, original.measurements)
        XCTAssertEqual(reopened.image, image)
        XCTAssertEqual(reopened.imageName, original.imageName)
        let text = try XCTUnwrap(String(data: bytes, encoding: .utf8))
        XCTAssertThrowsError(try ExperimentalDesignBrief.portableDraft(Data(text.replacingOccurrences(of: "\"aline\"", with: "\"unknown\"").utf8)))
        XCTAssertThrowsError(try ExperimentalDesignBrief.portableDraft(Data(text.replacingOccurrences(of: "\"id\":\"aline\"", with: "\"id\":\"aline\",\"id\":\"wrap\"").utf8)))
        let conflict = ExperimentalDesignBrief.Draft(style: .aline, description: "Model: düz", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        XCTAssertThrowsError(try ExperimentalDesignBrief.makeV2(conflict))
        let conflictingImport = #"{"format":"tekstilcad.design-request","version":2,"prompt":"Model: düz","measurements":"","reference":null,"model":{"id":"aline"}}"#
        XCTAssertThrowsError(try ExperimentalDesignBrief.portableDraft(Data(conflictingImport.utf8)))
        let unknownField = #"{"format":"tekstilcad.design-request","version":2,"prompt":"İpek","measurements":"","reference":null,"model":{"id":"aline","extra":true}}"#
        XCTAssertThrowsError(try ExperimentalDesignBrief.portableDraft(Data(unknownField.utf8)))
        let v1 = try ExperimentalDesignBrief.make(prompt: original.prompt, measurements: original.measurements, image: image, imageName: original.imageName)
        let v1Reopened = try ExperimentalDesignBrief.portableDraft(v1)
        XCTAssertEqual(v1Reopened.style, .describe)
        XCTAssertEqual(v1Reopened.prompt, original.prompt)
        XCTAssertEqual(v1Reopened.image, image)
        let damaged = Data([137,80,78,71,13,10,26,10])
        let damagedDraft = ExperimentalDesignBrief.Draft(style: .aline, description: "örnek", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: damaged, imageName: "broken.png")
        XCTAssertThrowsError(try ExperimentalDesignBrief.makeV2(damagedDraft))
    }
    func testV2RetainsFreeTextPotentialModelConflictWithoutClaimingDetection() throws {
        let draft = ExperimentalDesignBrief.Draft(style: .aline, description: "Düz elbise istiyorum", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        XCTAssertFalse(ExperimentalDesignBrief.hasConflictingModelLabels(style: draft.style, description: draft.description))
        let recovered = try ExperimentalDesignBrief.portableDraft(ExperimentalDesignBrief.makeV2(draft))
        XCTAssertEqual(recovered.style, .aline)
        XCTAssertEqual(recovered.description, "Düz elbise istiyorum")
    }
    func testV2ToV1RejectsOverLimitWithoutTruncatingEmoji() throws {
        let text = String(repeating: "👗", count: 8192)
        let original = ExperimentalDesignBrief.Draft(style: .aline, description: text, bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let v2 = try ExperimentalDesignBrief.makeV2(original)
        let reopened = try ExperimentalDesignBrief.portableDraft(v2)
        XCTAssertEqual(reopened.description, text)
        XCTAssertThrowsError(try ExperimentalDesignBrief.make(prompt: reopened.prompt, measurements: reopened.measurements, image: nil, imageName: nil))
        XCTAssertEqual(try ExperimentalDesignBrief.portableDraft(v2).description, text)
    }
    func testPortableV1AndV2AttemptsKeepSnapshotWhenCancelledOrSuperseded() throws {
        let draft = ExperimentalDesignBrief.Draft(style: .aline, description: "İpek 👗", bust: "92 cm", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let v1 = try ExperimentalDesignBrief.make(prompt: draft.prompt, measurements: draft.measurements, image: nil, imageName: nil)
        let v2 = try ExperimentalDesignBrief.makeV2(draft)
        let first = ExperimentalDesignBrief.ExportAttempt(draft: draft, bytes: v1)
        let second = ExperimentalDesignBrief.ExportAttempt(draft: draft, bytes: v2)
        var presentation = ExperimentalDesignBrief.ExportPresentation()
        presentation.begin(first.id)
        presentation.setPresented(false, for: first.id) // cancellation changes presentation only
        XCTAssertFalse(presentation.isPresented(for: first.id))
        XCTAssertEqual(first.bytes, v1)
        XCTAssertEqual(first.draft, draft)
        presentation.begin(second.id)
        presentation.setPresented(false, for: first.id) // late callback cannot dismiss v2
        XCTAssertTrue(presentation.isPresented(for: second.id))
        XCTAssertEqual(try ExperimentalDesignBrief.portableDraft(second.bytes).style, .aline)
        XCTAssertEqual(try ExperimentalDesignBrief.portableDraft(first.bytes).style, .describe)
        XCTAssertEqual(first.draft, second.draft)
    }
    func testMeasurementMessagePreservedAsUserAuthoredText() throws {
        let message = "Göğüs çevresi 92 cm, bel çevresi 72 cm.\nKol ölçüsünü bilmiyorum 👗"
        let draft = ExperimentalDesignBrief.Draft(style: .aline, description: "Elbise", bust: "", waist: "", hips: "", length: "", otherMeasurements: message, image: nil, imageName: nil)
        XCTAssertEqual(draft.measurements, message)
        let recovered = try ExperimentalDesignBrief.DraftStore.recoveryDraft(ExperimentalDesignBrief.DraftStore.recoveryData(draft))
        XCTAssertEqual(recovered.otherMeasurements, message)
        let portable = try ExperimentalDesignBrief.make(prompt: draft.prompt, measurements: draft.measurements, image: nil, imageName: nil)
        XCTAssertEqual(try ExperimentalDesignBrief.portableDraft(portable).measurements, message)
    }
    func testMeasurementMessageParserAcceptsOnlyExplicitLabelsAndUnits() throws {
        let source = "Göğüs çevresi 92,5 cm, bel çevresi: 720 mm; kalça çevresi 98 cm\nelbise boyu 110 cm"
        let proposal = try MeasurementMessageParser.parse(source, existing: [:])
        XCTAssertEqual(proposal.source, source)
        XCTAssertEqual(proposal.values["göğüs çevresi"], "92,5 cm")
        XCTAssertEqual(proposal.values["bel çevresi"], "720 mm")
        XCTAssertEqual(proposal.values["kalça çevresi"], "98 cm")
        XCTAssertEqual(proposal.values["elbise boyu"], "110 cm")
        XCTAssertTrue(proposal.preview.contains("bel çevresi: 720 mm"))
        let partial = try MeasurementMessageParser.parse("göğüs çevresi 92 cm", existing: [:])
        XCTAssertTrue(partial.preview.contains("Mesajda belirtilmeyenler: bel çevresi"))
    }
    func testMeasurementMessageOlsunPreservesTextAndRejectsAmbiguity() throws {
        let source = "Göğüs çevresi 92,5 cm olsun; bel çevresi 720 mm olsun"
        let result = try MeasurementMessageParser.parse(source, existing: [:])
        XCTAssertEqual(result.source, source)
        XCTAssertEqual(result.values["göğüs çevresi"], "92,5 cm")
        XCTAssertEqual(result.values["bel çevresi"], "720 mm")
        XCTAssertTrue(MeasurementMessageParser.mayNeedDesktopReview("göğüs çevresi 92 cm olsun ve bel çevresi 72 cm"))
        XCTAssertThrowsError(try MeasurementMessageParser.parse("bel çevresi 72 cm olsun; bel çevresi 73 cm olsun", existing: [:]))
        XCTAssertThrowsError(try MeasurementMessageParser.parse("bel çevresi 72 cm olsun; kol uzun olsun", existing: [:]))
        XCTAssertThrowsError(try MeasurementMessageParser.parse("bel çevresi 72 cm olsun", existing: ["bel çevresi": "70 cm"]))
        for ambiguous in ["bel çevresi yaklaşık 72 cm olsun", "bel çevresi 72 cm olmasın", "bel çevresi 72 cm olsun değil"] {
            XCTAssertThrowsError(try MeasurementMessageParser.parse(ambiguous, existing: [:]))
            let draft = ExperimentalDesignBrief.Draft(style: .describe, description: "", bust: "", waist: "", hips: "", length: "", otherMeasurements: ambiguous, image: nil, imageName: nil)
            XCTAssertEqual(draft.measurements, ambiguous)
        }
        let raw = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "ios-message-olsun.tcad-request", withExtension: "json"))
        let data = try Data(contentsOf: raw)
        let draft = try ExperimentalDesignBrief.portableDraft(data)
        XCTAssertEqual(draft.otherMeasurements, source)
        let serialized = try ExperimentalDesignBrief.make(prompt: draft.prompt, measurements: draft.measurements, image: nil, imageName: nil)
        XCTAssertEqual(try ExperimentalDesignBrief.portableDraft(serialized).otherMeasurements, source)
    }
    func testMeasurementMessageParserRejectsConflictsWithoutMutatingInputs() {
        let existing = ["bel çevresi": "70 cm"]
        let cases = ["bel çevresi 72 cm, bel çevresi 73 cm", "bel çevresi 72", "bel 72 cm", "bel çevresi 0 cm", "bel çevresi 1001 cm", "bel çevresi 72 cm ve kol uzun", "bel çevresi 72 cm, kalça çevresi 100 cm"]
        for message in cases { XCTAssertThrowsError(try MeasurementMessageParser.parse(message, existing: existing)) }
        XCTAssertEqual(existing["bel çevresi"], "70 cm")
        XCTAssertThrowsError(try MeasurementMessageParser.parse("göğüs çevresi 92 cm, göğüs çevresi 92 cm", existing: [:]))
        XCTAssertThrowsError(try MeasurementMessageParser.parse("göğüs çevresi 92 cm, bel 72 cm", existing: [:]))
        XCTAssertThrowsError(try MeasurementMessageParser.parse("", existing: [:]))
    }
    func testMessageLabelsRejectUnknownTurkishICaseWithoutPartialProposal() throws {
        XCTAssertEqual(MeasurementMessageParser.canonicalLabel("BEL ÇEVRESİ"), "bel çevresi")
        XCTAssertNil(MeasurementMessageParser.canonicalLabel("BEL ÇEVRESI"))
        XCTAssertNil(MeasurementMessageParser.canonicalLabel("bilinmeyen"))
        XCTAssertThrowsError(try MeasurementMessageParser.parse("göğüs çevresi 92 cm, BEL ÇEVRESI 72 cm", existing: [:]))
        let bad = MeasurementMessageParser.Proposal(source: "x", values: ["bel çevresı": "72 cm"])
        XCTAssertFalse(bad.isApplicable)
        XCTAssertFalse(MeasurementMessageParser.Proposal(source: "x", values: [:]).isApplicable)
        XCTAssertTrue(try MeasurementMessageParser.parse("bel çevresi 72 cm", existing: [:]).isApplicable)
    }
    func testMessageLineEndingsPreserveOriginalAndParseAll() throws {
        for lineBreak in ["\n", "\r\n", "\r"] {
            let original = "göğüs çevresi 92 cm" + lineBreak + "bel çevresi 72 cm"
            let proposal = try MeasurementMessageParser.parse(original, existing: [:])
            XCTAssertEqual(proposal.source, original)
            XCTAssertEqual(proposal.values["göğüs çevresi"], "92 cm")
            XCTAssertEqual(proposal.values["bel çevresi"], "72 cm")
        }
        XCTAssertThrowsError(try MeasurementMessageParser.parse("göğüs çevresi 92 cm\r\nbilinmeyen 72 cm", existing: [:]))
    }
    func testMeasurementMessageSupportsAndAndSentencePunctuation() throws {
        let examples = [
            "göğüs çevresi 92 cm ve bel çevresi 72 cm",
            "göğüs çevresi 92 cm. bel çevresi 72 cm.",
            "göğüs çevresi 92 cm, bel çevresi 72 cm.",
            "göğüs çevresi 92 cm.\r\nbel çevresi 72 cm."
        ]
        for source in examples {
            let result = try MeasurementMessageParser.parse(source, existing: [:])
            XCTAssertEqual(result.source, source)
            XCTAssertEqual(result.values["göğüs çevresi"], "92 cm")
            XCTAssertEqual(result.values["bel çevresi"], "72 cm")
        }
        let decimal = try MeasurementMessageParser.parse("göğüs çevresi 92.5 cm ve bel çevresi 72,5 cm.", existing: [:])
        XCTAssertEqual(decimal.values["göğüs çevresi"], "92.5 cm")
        XCTAssertEqual(decimal.values["bel çevresi"], "72,5 cm")
    }
    func testMeasurementMessageNeverReturnsPartialForNaturalLanguageFailures() {
        for source in ["göğüs çevresi 92 cm ve bel çevresi 72", "göğüs çevresi 92 cm ve kol 40 cm", "göğüs çevresi 92 cm ve göğüs çevresi 93 cm", "göğüs çevresi 92 cm ve ", "göğüs çevresi 92 cm. not ekle", "göğüs çevresi 92 cm, bel çevresi 0 cm"] {
            XCTAssertThrowsError(try MeasurementMessageParser.parse(source, existing: [:]))
        }
        XCTAssertThrowsError(try MeasurementMessageParser.parse("göğüs çevresi 92 cm ve bel çevresi 72 cm", existing: ["bel çevresi": "71 cm"]))
    }
    func testDesktopMeasurementRiskWarnsWithoutChangingPortableData() throws {
        XCTAssertTrue(MeasurementMessageParser.mayNeedDesktopReview("göğüs çevresi 92 cm ve bel çevresi 72 cm"))
        XCTAssertTrue(MeasurementMessageParser.mayNeedDesktopReview("göğüs çevresi 92 cm. bel çevresi 72 cm."))
        XCTAssertFalse(MeasurementMessageParser.mayNeedDesktopReview("göğüs çevresi 92.5 cm, bel çevresi 72 cm"))
        XCTAssertFalse(MeasurementMessageParser.mayNeedDesktopReview("göğüs çevresi 92 cm.\n"))
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "portable-desktop-and-v1", withExtension: "json"))
        let raw = try Data(contentsOf: url)
        let original = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: raw)
        let imported = try ExperimentalDesignBrief.portableDraft(raw)
        XCTAssertTrue(MeasurementMessageParser.mayNeedDesktopReview(imported.otherMeasurements))
        XCTAssertEqual(imported.measurements, original.measurements)
        let bytes = try ExperimentalDesignBrief.make(prompt: imported.prompt, measurements: imported.measurements, image: nil, imageName: nil)
        let reopened = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: bytes)
        XCTAssertEqual(reopened.prompt, original.prompt); XCTAssertEqual(reopened.measurements, original.measurements)
    }
    func testSharedPortableFixturesRoundTripAndRejectInvalid() throws {
        func fixture(_ name: String) throws -> Data {
            let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: name + ".tcad-request", withExtension: "json"))
            return try Data(contentsOf: url)
        }
        for name in ["ios-unicode-photo", "ios-message-ve", "ios-whitespace-notes"] {
            let raw = try fixture(name)
            let expected = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: raw)
            let imported = try ExperimentalDesignBrief.portableDraft(raw)
            XCTAssertEqual(imported.prompt, expected.prompt)
            XCTAssertEqual(imported.measurements, expected.measurements)
            if let reference = expected.reference {
                XCTAssertEqual(imported.image, Data(base64Encoded: reference.dataBase64))
                XCTAssertEqual(imported.imageName, reference.name)
            } else { XCTAssertNil(imported.image) }
            let bytes = try ExperimentalDesignBrief.make(prompt: imported.prompt, measurements: imported.measurements, image: imported.image, imageName: imported.imageName)
            let roundTrip = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: bytes)
            XCTAssertEqual(roundTrip.prompt, expected.prompt); XCTAssertEqual(roundTrip.measurements, expected.measurements)
            XCTAssertEqual(roundTrip.reference?.dataBase64, expected.reference?.dataBase64)
        }
        for name in ["ios-invalid-duplicate", "ios-unsupported-version"] {
            XCTAssertThrowsError(try ExperimentalDesignBrief.portableDraft(fixture(name)))
        }
    }
    func testOversizedInputDoesNotReplacePreviousRecord() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("draft.json")
        let valid = ExperimentalDesignBrief.Draft(style: .describe, description: "Önceki", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let revision = try ExperimentalDesignBrief.DraftStore.save(valid, to: url, expected: nil)
        let oversized = ExperimentalDesignBrief.Draft(style: .describe, description: String(repeating: "x", count: 16_385), bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        XCTAssertThrowsError(try ExperimentalDesignBrief.DraftStore.save(oversized, to: url, expected: revision))
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.load(url)?.draft, valid)
        _ = try ExperimentalDesignBrief.DraftStore.save(valid, to: url, expected: revision)
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.load(url)?.draft, valid)
    }
}
#endif
