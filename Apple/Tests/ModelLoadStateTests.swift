import XCTest
@testable import TekstilCADViewer

private func makeTestRequest(prompt: String, measurements: String, image: Data?, imageName: String?) throws -> Data {
    try ExperimentalDesignBrief.make(.init(style: .describe, description: prompt, bust: "", waist: "", hips: "", length: "", otherMeasurements: measurements, image: image, imageName: imageName))
}


final class ModelLoadStateTests: XCTestCase {
    func testV7AndV10PieceValidationErrorsNameTheActualVersion() throws {
        for (version, fixture) in [(7, "desktop-synthetic-rotated-v7"), (10, "v10-minimal")] {
            let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: fixture, withExtension: "tcad"))
            let data = try Data(contentsOf: url)
            let envelope = try XCTUnwrap(try JSONSerialization.jsonObject(with: data) as? [Any])
            let body = try XCTUnwrap(envelope[1] as? [String: Any])
            let initialPieces = try XCTUnwrap(body["Pieces"] as? [[String: Any]])
            for (kind, expected) in [
                ("missing", "parça kimliği veya adı eksik"),
                ("metadata", "parça bilgisi veya iplik yönü geçersiz"),
                ("duplicate", "parça kimliği yinelenmiş veya geçersiz")
            ] {
                var changed = body
                var pieces = initialPieces
                switch kind {
                case "missing": pieces[0].removeValue(forKey: "Id")
                case "metadata": pieces[0]["Metadata"] = ["Role": "geçersiz"]
                default: pieces.append(pieces[0])
                }
                changed["Pieces"] = pieces
                let invalid = try JSONSerialization.data(withJSONObject: [version, changed])
                XCTAssertThrowsError(try Drawing.load(invalid), "\(fixture): \(kind)") { error in
                    XCTAssertEqual(error.localizedDescription, "Sürüm \(version) \(expected); dosya önizlenmedi.")
                }
            }
        }
    }
    func testWindowsV10FixturesAreReadOnlyAndKeepSource() throws {
        func fixture(_ name: String) throws -> Data {
            let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: name, withExtension: "tcad"))
            return try Data(contentsOf: url)
        }
        for name in ["v10-minimal", "v10-mixed"] {
            let data = try fixture(name), original = data
            let drawing = try Drawing.load(data)
            XCTAssertEqual(drawing.formatVersion, 10)
            var state = LoadedModelState<Drawing>()
            state.install(drawing, data: data, selectedPiece: 0)
            XCTAssertEqual(state.sourceData, original)
            XCTAssertTrue(drawing.pieces.contains { $0.experimentalALine })
            if name == "v10-mixed" {
                XCTAssertFalse(drawing.pieces[0].experimentalALine)
                XCTAssertTrue(drawing.pieces[1].experimentalALine)
            }
            let id = try XCTUnwrap(drawing.pieces.first?.id)
            XCTAssertThrowsError(try ExperimentalPieceRename.replacing(in: data, snapshot: data, pieceID: id, with: "Edited"))
            XCTAssertThrowsError(try ExperimentalPointMoveJSON.moving(data, snapshot: data, pieceID: id, index: 0, to: .init(x: 1, y: 1)))
            XCTAssertThrowsError(try LosslessProjectName.replacing(in: data, with: "Edited"))
            XCTAssertEqual(data, original)
        }
        XCTAssertThrowsError(try Drawing.load(fixture("v10-history-only")))
    }

    func testV10AndV7FixturesRejectUnderflowCoordinates() throws {
        for name in ["v10-minimal", "desktop-synthetic-rotated-v7"] {
            let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: name, withExtension: "tcad"))
            let source = try String(contentsOf: url, encoding: .utf8)
            XCTAssertNoThrow(try Drawing.load(Data(source.utf8)))
            let pattern = #"\"X\"\s*:\s*-?[0-9]+(?:\.[0-9]+)?"#
            let range = try XCTUnwrap(source.range(of: pattern, options: .regularExpression))
            for token in ["1e-400", "-1e-400"] {
                var changed = source
                changed.replaceSubrange(range, with: "\"X\": \(token)")
                XCTAssertThrowsError(try Drawing.load(Data(changed.utf8)), "\(name): \(token)")
            }
        }
    }

    func testV10NameLimitsUseUTF16AndQuantityUsesIntegerToken() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "v10-minimal", withExtension: "tcad"))
        let data = try Data(contentsOf: url)
        let envelope = try XCTUnwrap(try JSONSerialization.jsonObject(with: data) as? [Any])
        let original = try XCTUnwrap(envelope[1] as? [String: Any])
        let goodName = String(repeating: "😀", count: 512)
        for target in ["project", "piece"] {
            for (name, allowed) in [(goodName, true), (goodName + "a", false)] {
                var body = original
                var pieces = try XCTUnwrap(body["Pieces"] as? [[String: Any]])
                if target == "project" { body["Name"] = name } else { pieces[0]["Name"] = name }
                body["Pieces"] = pieces
                let edited = try JSONSerialization.data(withJSONObject: [10, body])
                if allowed { XCTAssertNoThrow(try Drawing.load(edited)) }
                else { XCTAssertThrowsError(try Drawing.load(edited)) }
            }
        }
        let text = try XCTUnwrap(String(data: data, encoding: .utf8))
        XCTAssertTrue(text.contains("\"ExperimentalALine\":"))
        for (token, allowed) in [("1", true), ("1.0", false), ("1e0", false)] {
            let changed = text.replacingOccurrences(of: "\"ExperimentalALine\":", with: "\"Metadata\": {\"Quantity\": \(token)}, \"ExperimentalALine\":")
            if allowed { XCTAssertNoThrow(try Drawing.load(Data(changed.utf8))) }
            else { XCTAssertThrowsError(try Drawing.load(Data(changed.utf8))) }
        }
    }
    func testV10ReadOnlyEnvelopeRejectsUnknownDuplicatesAndHistory() throws {
        let marker = "\"ExperimentalALine\":{\"Algorithm\":\"A-04.lower-section.experimental.v1\",\"SourceSha256\":\"" + String(repeating: "A", count: 64) + "\",\"Side\":\"front\"}"
        func bytes(_ markerText: String, extra: String = "") -> Data {
            Data("[10,{\"Name\":\"Test\",\"Pieces\":[{\"Id\":\"11111111-1111-1111-1111-111111111111\",\"Name\":\"Ön\",\"Closed\":true,\"Points\":[{\"X\":0,\"Y\":0},{\"X\":10,\"Y\":0},{\"X\":0,\"Y\":10}],\(markerText)}],\"GradingRules\":[]\(extra)}]".utf8)
        }
        let source = bytes(marker), original = source
        let drawing = try Drawing.load(source)
        XCTAssertEqual(drawing.formatVersion, 10)
        XCTAssertEqual(drawing.pieces.count, 1)
        XCTAssertEqual(source, original)
        XCTAssertThrowsError(try Drawing.load(bytes(marker + "," + marker)))
        XCTAssertThrowsError(try Drawing.load(bytes(marker.replacingOccurrences(of: "\"Side\":\"front\"", with: "\"Side\":\"front\",\"Side\":\"back\""))))
        XCTAssertThrowsError(try Drawing.load(bytes(marker.replacingOccurrences(of: "\"Side\":\"front\"", with: "\"Side\":\"front\",\"Extra\":1"))))
        XCTAssertThrowsError(try Drawing.load(bytes(marker.replacingOccurrences(of: "\"front\"", with: "\"unknown\""))))
        XCTAssertThrowsError(try Drawing.load(bytes(marker.replacingOccurrences(of: String(repeating: "A", count: 64), with: String(repeating: "a", count: 64)))))
        XCTAssertThrowsError(try Drawing.load(bytes(marker, extra: ",\"NestingSnapshot\":{\"RotationTrace\":{}}")))
        XCTAssertThrowsError(try Drawing.load(bytes(marker, extra: ",\"PhotoTraceHistory\":[]")))
        let v11 = String(decoding: source, as: UTF8.self).replacingOccurrences(of: "[10,", with: "[11,")
        XCTAssertThrowsError(try Drawing.load(Data(v11.utf8)))
    }
    #if DEBUG
    func testOldExporterCannotDismissNewAttempt() {
        var presentation = ExperimentalDesignBrief.ExportPresentation()
        let old = UUID(), current = UUID()
        presentation.begin(old)
        XCTAssertTrue(presentation.isPresented(for: old))
        presentation.setPresented(false, for: old)
        presentation.begin(current)
        XCTAssertTrue(presentation.isPresented(for: current))
        presentation.setPresented(false, for: old)
        XCTAssertTrue(presentation.isPresented(for: current))
        XCTAssertFalse(presentation.isPresented(for: old))
        presentation.setPresented(false, for: current)
        XCTAssertFalse(presentation.isPresented(for: current))
    }
    func testExportAttemptRejectsLateCallbackFromEarlierAttempt() {
        let draft = ExperimentalDesignBrief.Draft(style: .describe, description: "Elbise", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let old = ExperimentalDesignBrief.ExportAttempt(draft: draft, bytes: Data([1]))
        let current = ExperimentalDesignBrief.ExportAttempt(draft: draft, bytes: Data([2]))
        XCTAssertFalse(current.matches(old.id))
        XCTAssertTrue(current.matches(current.id))
        XCTAssertNotEqual(old.bytes, current.bytes)
    }
    func testV1ExportAttemptRequiresUnchangedDraftBeforeConfirmation() throws {
        func makeDraft(_ text: String, image: Data? = nil) -> ExperimentalDesignBrief.Draft {
            .init(style: .aline, description: text, bust: "92 cm", waist: "", hips: "", length: "", otherMeasurements: "", image: image, imageName: image == nil ? nil : "photo.png")
        }
        let original = makeDraft("İpek")
        let bytes = try makeTestRequest(prompt: original.prompt, measurements: original.measurements, image: nil, imageName: nil)
        let attempt = ExperimentalDesignBrief.ExportAttempt(draft: original, bytes: bytes)
        XCTAssertTrue(attempt.matchesCurrentDraft(original))
        XCTAssertFalse(attempt.matchesCurrentDraft(makeDraft("Keten")))
        XCTAssertFalse(attempt.matchesCurrentDraft(makeDraft("İpek", image: Data([1]))))
        var presentation = ExperimentalDesignBrief.ExportPresentation()
        presentation.begin(attempt.id)
        presentation.setPresented(false, for: attempt.id)
        XCTAssertFalse(presentation.isPresented(for: attempt.id))
        XCTAssertEqual(attempt.draft, original)
        XCTAssertEqual(attempt.bytes, bytes)
    }
    func testBriefSavedSnapshotTracksLaterChangesIncludingImage() {
        func draft(_ description: String, image: Data? = nil) -> ExperimentalDesignBrief.Draft {
            .init(style: .describe, description: description, bust: "", waist: "", hips: "", length: "",
                  otherMeasurements: "", image: image, imageName: image == nil ? nil : "photo.png")
        }
        let empty = draft(""), entered = draft("Elbise"), changed = draft("Farklı"), withImage = draft("Elbise", image: Data([1]))
        XCTAssertFalse(empty.requiresDiscardConfirmation(saved: nil))
        XCTAssertTrue(entered.requiresDiscardConfirmation(saved: nil)) // no successful save
        XCTAssertFalse(entered.requiresDiscardConfirmation(saved: entered))
        XCTAssertTrue(changed.requiresDiscardConfirmation(saved: entered))
        XCTAssertTrue(empty.requiresDiscardConfirmation(saved: entered))
        XCTAssertTrue(withImage.requiresDiscardConfirmation(saved: entered))
        XCTAssertFalse(withImage.requiresDiscardConfirmation(saved: withImage))
    }
    func testDressStyleAndOnlyEnteredMeasurementsAreCarriedIntoRequest() throws {
        let prompt = ExperimentalDesignBrief.prompt(style: .aline, description: "Uzun kollu")
        let measures = ExperimentalDesignBrief.measurements(bust: "92 cm", waist: "", hips: "  ", length: "110 cm", other: "Omuz: 38 cm")
        let data = try makeTestRequest(prompt: prompt, measurements: measures, image: nil, imageName: nil)
        let brief = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: data)
        XCTAssertEqual(brief.prompt, "Seçilen elbise modeli: A kesim elbise\nUzun kollu")
        XCTAssertEqual(brief.measurements, "Göğüs çevresi: 92 cm\nKalça çevresi:   \nElbise boyu: 110 cm\nOmuz: 38 cm")
        XCTAssertEqual(ExperimentalDesignBrief.prompt(style: .describe, description: "Kendi tarifim"), "Kendi tarifim")
        XCTAssertEqual(ExperimentalDesignBrief.measurements(bust: "", waist: "", hips: "", length: "", other: ""), "")
    }
    func testDressMeasurementsUseDesktopCircumferenceNames() throws {
        let measures = ExperimentalDesignBrief.measurements(bust: "92 cm", waist: "72 cm", hips: "98 cm", length: "110 cm", other: "")
        let data = try makeTestRequest(prompt: "Düz elbise", measurements: measures, image: nil, imageName: nil)
        let brief = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: data)
        XCTAssertEqual(brief.measurements, "Göğüs çevresi: 92 cm\nBel çevresi: 72 cm\nKalça çevresi: 98 cm\nElbise boyu: 110 cm")
    }
    func testDesignBriefPreservesUserTextAndReferenceBytes() throws {
        let image = try XCTUnwrap(Data(base64Encoded: "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/aO0AAAAASUVORK5CYII="))
        let bytes = try makeTestRequest(prompt: "Mavi ceket\nCepli", measurements: "Göğüs 92 cm", image: image, imageName: "ornek.png")
        let brief = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: bytes)
        XCTAssertEqual(brief.format, "tekstilcad.design-request")
        XCTAssertEqual(brief.version, 1)
        XCTAssertEqual(brief.prompt, "Mavi ceket\nCepli")
        XCTAssertEqual(brief.measurements, "Göğüs 92 cm")
        let reference = try XCTUnwrap(brief.reference)
        XCTAssertEqual(reference.name, "ornek.png")
        XCTAssertEqual(reference.mediaType, "image/png")
        XCTAssertEqual(reference.dataBase64, image.base64EncodedString())
        XCTAssertThrowsError(try makeTestRequest(prompt: "Ceket", measurements: "", image: image, imageName: "..\\ornek.png"))
        XCTAssertThrowsError(try makeTestRequest(prompt: "Ceket", measurements: "", image: image, imageName: String(repeating: "x", count: 256)))
        let imageOnly = try makeTestRequest(prompt: "", measurements: "", image: image, imageName: "ornek.png")
        XCTAssertEqual(try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: imageOnly).prompt, "")
    }

    func testDesignBriefRejectsMissingOrOversizedInputAndAllowsNoImage() throws {
        XCTAssertThrowsError(try makeTestRequest(prompt: "  ", measurements: "", image: nil, imageName: nil))
        XCTAssertThrowsError(try makeTestRequest(prompt: String(repeating: "x", count: 16_385), measurements: "", image: nil, imageName: nil))
        XCTAssertThrowsError(try makeTestRequest(prompt: "Ceket", measurements: "", image: Data([0, 1]), imageName: "x.png"))
        XCTAssertThrowsError(try makeTestRequest(prompt: "Ceket", measurements: "", image: Data(count: 8 * 1024 * 1024 + 1), imageName: "x.png"))
        let brief = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self,
            from: makeTestRequest(prompt: "Ceket", measurements: "", image: nil, imageName: nil))
        XCTAssertNil(brief.reference)
        let raw = try XCTUnwrap(JSONSerialization.jsonObject(with: makeTestRequest(prompt: "Ceket", measurements: "", image: nil, imageName: nil)) as? [String: Any])
        XCTAssertTrue(raw["reference"] is NSNull)
    }
    #endif
    func testRejectedFutureProjectLoadsKeepPreviouslyOpenModelAndSourceFile() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let original = Data(#"[1,{"Name":"Önceki","Pieces":[]}]"#.utf8)
        var state = LoadedModelState<String>()
        state.install("Önceki", data: original, selectedPiece: 0)
        let revision = state.revision
        for version in [8, 9, 10] {
            let candidate = Data("[\(version),{\"Name\":\"Gelecek\",\"Pieces\":[]}]".utf8)
            let url = directory.appendingPathComponent("future-\(version).tcad")
            try candidate.write(to: url)
            let request = state.load.begin()
            // The real reader rejects these versions in ViewerUITests; the error
            // completion must leave the previously installed snapshot untouched.
            XCTAssertTrue(state.load.finish(request))
            XCTAssertEqual(state.model, "Önceki")
            XCTAssertEqual(state.sourceData, original)
            XCTAssertEqual(state.selectedPiece, 0)
            XCTAssertEqual(state.revision, revision)
            XCTAssertEqual(try Data(contentsOf: url), candidate)
        }
    }
    func testInstalledModelKeepsLoadRequestAndRejectsStaleCompletion() {
        var state = LoadedModelState<Int>()
        state.install(1, data: Data([1]))
        let stale = state.load.begin()
        let current = state.load.begin()
        XCTAssertFalse(state.load.finish(stale))
        XCTAssertEqual(state.model, 1)
        XCTAssertEqual(state.sourceData, Data([1]))
        XCTAssertTrue(state.load.finish(current))
        state.install(2, data: Data([2]))
        XCTAssertEqual(state.load.request, current)
        XCTAssertFalse(state.load.isLoading)
        XCTAssertEqual(state.model, 2)
        XCTAssertEqual(state.sourceData, Data([2]))
    }
    func testPreviewCompletionInstallsOnlyCurrentLoadAndPreservesCancelledModel() {
        var state = LoadedModelState<Int>()
        state.install(1, data: Data([1]), selectedPiece: 2)
        let originalRevision = state.revision
        let stale = state.load.begin()
        let latest = state.load.begin()
        XCTAssertFalse(state.completePreview(stale, model: 2, data: Data([2]), selectedPiece: 0))
        XCTAssertEqual(state.model, 1)
        XCTAssertEqual(state.sourceData, Data([1]))
        XCTAssertEqual(state.selectedPiece, 2)
        XCTAssertEqual(state.revision, originalRevision)
        state.load.invalidate()
        XCTAssertFalse(state.completePreview(latest, model: 3, data: Data([3]), selectedPiece: nil))
        XCTAssertEqual(state.model, 1)
        let accepted = state.load.begin()
        XCTAssertTrue(state.completePreview(accepted, model: 4, data: Data([4]), selectedPiece: 0))
        XCTAssertEqual(state.model, 4)
        XCTAssertEqual(state.sourceData, Data([4]))
        XCTAssertEqual(state.selectedPiece, 0)
        XCTAssertNotEqual(state.revision, originalRevision)
        XCTAssertFalse(state.load.isLoading)
    }
    #if DEBUG
    func testEditIdentityRejectsSelectionLoadAndModelChanges() throws {
        var state = LoadedModelState<Int>()
        state.install(1, data: Data([1]), selectedPiece: 0)
        let identity = try XCTUnwrap(state.editIdentity())
        XCTAssertTrue(state.isCurrent(identity, data: Data([1])))
        state.selectedPiece = 1
        XCTAssertFalse(state.isCurrent(identity, data: Data([1])))
        state.selectedPiece = 0
        XCTAssertFalse(state.isCurrent(identity, data: Data([1])))
        let refreshed = try XCTUnwrap(state.editIdentity())
        state.selectedPiece = 0
        XCTAssertTrue(state.isCurrent(refreshed, data: Data([1])))
        let load = state.load.begin()
        XCTAssertNil(state.editIdentity())
        XCTAssertFalse(state.isCurrent(identity, data: Data([1])))
        state.load.invalidate()
        XCTAssertFalse(state.isCurrent(identity, data: Data([1])))
        XCTAssertFalse(state.completePreview(load, model: 2, data: Data([2]), selectedPiece: 0))
        let replacement = state.load.begin()
        XCTAssertTrue(state.completePreview(replacement, model: 2, data: Data([2]), selectedPiece: 0))
        XCTAssertFalse(state.isCurrent(identity, data: Data([1])))
    }
    #endif
    func testNotchSelectionIsClearedWhenPieceOrModelChanges() {
        var state = LoadedModelState<Int>()
        state.install(1, data: Data([1]), selectedPiece: 0)
        state.selectedNotch = (piece: 0, point: 3)
        state.selectedPiece = 0
        XCTAssertEqual(state.selectedNotch?.point, 3)
        state.selectedPiece = 1
        XCTAssertNil(state.selectedNotch)
        state.selectedNotch = (piece: 1, point: 2)
        state.install(2, data: Data([2]), selectedPiece: 1)
        XCTAssertNil(state.selectedNotch)
        XCTAssertEqual(state.selectedPiece, 1)
    }
    #if DEBUG
    func testPointEditorSnapshotInvalidatesWithSelectionOrLoadChange() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "desktop-synthetic-point", withExtension: "tcad"))
        let data = try Data(contentsOf: url), drawing = try Drawing.load(data)
        let piece = try XCTUnwrap(drawing.pieces.first), id = try XCTUnwrap(piece.id)
        var state = LoadedModelState<Drawing>()
        state.install(drawing, data: data, selectedPiece: 0)
        func makeSnapshot(_ state: LoadedModelState<Drawing>) throws -> PointEditSnapshot {
            PointEditSnapshot(data: data, piece: piece, pieceID: id, index: 0,
                              projectName: drawing.name, identity: try XCTUnwrap(state.editIdentity()))
        }
        let first = try makeSnapshot(state), newer = try makeSnapshot(state)
        state.pointEditSnapshot = first
        state.pointEditSnapshot = newer
        state.closePointEditSession(first.id)
        XCTAssertEqual(state.pointEditSnapshot?.id, newer.id)
        state.closePointEditSession(newer.id)
        XCTAssertNil(state.pointEditSnapshot)
        state.pointEditSnapshot = try makeSnapshot(state)
        state.selectedPiece = 1
        XCTAssertNil(state.pointEditSnapshot)
        state.selectedPiece = 0
        state.pointEditSnapshot = try makeSnapshot(state)
        _ = state.load.begin()
        XCTAssertNil(state.pointEditSnapshot)
        state.load.invalidate()
        state.pointEditSnapshot = try makeSnapshot(state)
        state.install(drawing, data: data, selectedPiece: 0)
        XCTAssertNil(state.pointEditSnapshot)
    }
    #endif
    func testInstallingModelReplacesSelectionWithMatchedOrDefaultSelection() {
        var state = LoadedModelState<Int>()
        state.install(1, data: Data([1]), selectedPiece: 2)
        XCTAssertEqual(state.selectedPiece, 2)
        state.install(2, data: Data([2]), selectedPiece: 0)
        XCTAssertEqual(state.model, 2)
        XCTAssertEqual(state.sourceData, Data([2]))
        XCTAssertEqual(state.selectedPiece, 0)
        state.install(3, data: Data([3]))
        XCTAssertNil(state.selectedPiece)
    }
    func testInstallingModelKeepsDrawingBytesAndRevisionTogether() {
        var state = LoadedModelState<Int>()
        let initial = state.revision
        XCTAssertNil(state.model)
        XCTAssertNil(state.sourceData)
        state.install(1, data: Data([0, 255]))
        let first = state.revision
        XCTAssertNotEqual(initial, first)
        XCTAssertEqual(state.model, 1)
        XCTAssertEqual(state.sourceData, Data([0, 255]))
        state.install(2, data: Data([2]))
        XCTAssertNotEqual(first, state.revision)
        XCTAssertEqual(state.model, 2)
        XCTAssertEqual(state.sourceData, Data([2]))
    }
    func testDesktopSavedStaleV7WithNewPieceShowsVisibleContours() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "desktop-synthetic-rotated-v7-stale", withExtension: "tcad"))
        let data = try Data(contentsOf: url), before = data
        let drawing = try Drawing.load(data)
        XCTAssertEqual(drawing.formatVersion, 7)
        XCTAssertTrue(drawing.historyPieceIDsDiffer)
        XCTAssertEqual(drawing.pieces.count, 3)
        XCTAssertNil(drawing.pieces[0].grain); XCTAssertNil(drawing.pieces[0].material)
        XCTAssertEqual(drawing.pieces[2].name, "Yeni parça")
        XCTAssertNil(drawing.pieces[2].grain); XCTAssertNil(drawing.pieces[2].material)
        XCTAssertEqual(data, before)
    }

    func testV7StaleVisiblePiecesMayHaveNullMetadataAndGrainline() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "desktop-synthetic-rotated-v7", withExtension: "tcad"))
        let original = try Data(contentsOf: url)
        let envelope = try XCTUnwrap(try JSONSerialization.jsonObject(with: original) as? [Any])
        var body = try XCTUnwrap(envelope[1] as? [String: Any])
        var pieces = try XCTUnwrap(body["Pieces"] as? [[String: Any]])
        pieces[0]["Metadata"] = NSNull(); pieces[0]["Grainline"] = NSNull()
        pieces.append(["Id": "11111111-1111-1111-1111-111111111111", "Name": "Ek parça", "Closed": false,
                       "Points": [["X": 0, "Y": 0], ["X": 10, "Y": 20]],
                       "Metadata": NSNull(), "Grainline": NSNull()])
        body["Pieces"] = pieces
        let data = try JSONSerialization.data(withJSONObject: [7, body]), before = data
        let drawing = try Drawing.load(data)
        XCTAssertEqual(drawing.pieces.count, 3)
        XCTAssertNil(drawing.pieces[0].grain); XCTAssertNil(drawing.pieces[0].material)
        XCTAssertEqual(drawing.pieces[2].name, "Ek parça")
        XCTAssertNil(drawing.pieces[2].grain); XCTAssertNil(drawing.pieces[2].material)
        XCTAssertEqual(data, before)
        for key in ["Metadata", "Grainline"] {
            var bad = pieces; bad[0][key] = "invalid"; body["Pieces"] = bad
            XCTAssertThrowsError(try Drawing.load(JSONSerialization.data(withJSONObject: [7, body])))
        }
        body["Pieces"] = pieces
        var snapshot = try XCTUnwrap(body["NestingSnapshot"] as? [String: Any])
        var trace = try XCTUnwrap(snapshot["RotationTrace"] as? [String: Any])
        var history = try XCTUnwrap(trace["SourcePieces"] as? [[String: Any]])
        history[0]["Grainline"] = NSNull(); trace["SourcePieces"] = history
        snapshot["RotationTrace"] = trace; body["NestingSnapshot"] = snapshot
        XCTAssertThrowsError(try Drawing.load(JSONSerialization.data(withJSONObject: [7, body])))
    }

    func testDesktopV7PreviewKeepsSourceBytesAndRejectsInvalidVisibleMarks() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "desktop-synthetic-rotated-v7", withExtension: "tcad"))
        let data = try Data(contentsOf: url)
        let before = data
        let drawing = try Drawing.load(data)
        XCTAssertEqual(drawing.formatVersion, 7)
        XCTAssertFalse(drawing.historyPieceIDsDiffer)
        XCTAssertEqual(drawing.pieces.count, 2)
        XCTAssertTrue(drawing.pieces.allSatisfy { !$0.notches.isEmpty && $0.grain != nil })
        XCTAssertEqual(data, before)
        let envelope = try XCTUnwrap(try JSONSerialization.jsonObject(with: data) as? [Any])
        var body = try XCTUnwrap(envelope[1] as? [String: Any])
        var changedPieces = try XCTUnwrap(body["Pieces"] as? [[String: Any]])
        changedPieces[0]["Name"] = "Değiştirilmiş"
        body["Pieces"] = changedPieces
        XCTAssertFalse(try Drawing.load(JSONSerialization.data(withJSONObject: [7, body])).historyPieceIDsDiffer,
                       "ID equality does not establish that history is current")
        let invalid = #"[7,{"Name":"Proje","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}],"Notches":[{"VertexIndex":9,"Position":{"X":0,"Y":0}}]}],"NestingSnapshot":{"RotationTrace":{}}}]"#
        XCTAssertThrowsError(try Drawing.load(Data(invalid.utf8)))
        XCTAssertTrue(LosslessProjectName.requiresVersionSixRawRecovery(Data(("[7," + String(repeating: " ", count: 8 * 1024 * 1024)).utf8), version: 7))
    }
    func testRotationTraceCannotUseOlderEnvelopeAndInvalidV7TraceIsRejected() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "desktop-synthetic-rotated-v7", withExtension: "tcad"))
        let original = try Data(contentsOf: url)
        let array = try XCTUnwrap(try JSONSerialization.jsonObject(with: original) as? [Any])
        let body = try XCTUnwrap(array[1] as? [String: Any])
        func bytes(_ version: Int, _ body: [String: Any], encoding: String.Encoding, bom: [UInt8]) throws -> Data {
            let json = try JSONSerialization.data(withJSONObject: [version, body])
            let text = try XCTUnwrap(String(data: json, encoding: .utf8))
            var result = Data(bom); result.append(try XCTUnwrap(text.data(using: encoding))); return result
        }
        let encodings: [(String.Encoding, [UInt8])] = [(.utf8, []), (.utf16LittleEndian, [0xff, 0xfe]), (.utf32BigEndian, [0, 0, 0xfe, 0xff])]
        for (encoding, bom) in encodings {
            let valid = try bytes(7, body, encoding: encoding, bom: bom)
            XCTAssertEqual(try Drawing.load(valid).formatVersion, 7)
            for version in [1, 5, 6] {
                let old = try bytes(version, body, encoding: encoding, bom: bom), before = old
                XCTAssertThrowsError(try Drawing.load(old)); XCTAssertEqual(old, before)
            }
            var badBody = body
            var snapshot = try XCTUnwrap(body["NestingSnapshot"] as? [String: Any])
            snapshot["RotationTrace"] = [:]; badBody["NestingSnapshot"] = snapshot
            let bad = try bytes(7, badBody, encoding: encoding, bom: bom), before = bad
            XCTAssertThrowsError(try Drawing.load(bad)); XCTAssertEqual(bad, before)
            var trace = try XCTUnwrap((body["NestingSnapshot"] as? [String: Any])?["RotationTrace"] as? [String: Any])
            trace["Schema"] = "unknown"; snapshot["RotationTrace"] = trace; badBody["NestingSnapshot"] = snapshot
            XCTAssertThrowsError(try Drawing.load(bytes(7, badBody, encoding: encoding, bom: bom)))
            trace = try XCTUnwrap((body["NestingSnapshot"] as? [String: Any])?["RotationTrace"] as? [String: Any])
            trace.removeValue(forKey: "Copies"); snapshot["RotationTrace"] = trace; badBody["NestingSnapshot"] = snapshot
            XCTAssertThrowsError(try Drawing.load(bytes(7, badBody, encoding: encoding, bom: bom)))
            trace["Copies"] = "invalid"; snapshot["RotationTrace"] = trace; badBody["NestingSnapshot"] = snapshot
            XCTAssertThrowsError(try Drawing.load(bytes(7, badBody, encoding: encoding, bom: bom)))
            trace = try XCTUnwrap((body["NestingSnapshot"] as? [String: Any])?["RotationTrace"] as? [String: Any])
            trace["Extra"] = true; snapshot["RotationTrace"] = trace; badBody["NestingSnapshot"] = snapshot
            XCTAssertThrowsError(try Drawing.load(bytes(7, badBody, encoding: encoding, bom: bom)))
            badBody = body; badBody.removeValue(forKey: "GradingRules")
            XCTAssertThrowsError(try Drawing.load(bytes(7, badBody, encoding: encoding, bom: bom)))
            badBody = body; badBody["Extra"] = 1
            XCTAssertThrowsError(try Drawing.load(bytes(7, badBody, encoding: encoding, bom: bom)))
            snapshot = try XCTUnwrap(body["NestingSnapshot"] as? [String: Any])
            snapshot.removeValue(forKey: "Gap"); badBody = body; badBody["NestingSnapshot"] = snapshot
            XCTAssertThrowsError(try Drawing.load(bytes(7, badBody, encoding: encoding, bom: bom)))
            snapshot = try XCTUnwrap(body["NestingSnapshot"] as? [String: Any])
            snapshot["Unexpected"] = 1; badBody["NestingSnapshot"] = snapshot
            XCTAssertThrowsError(try Drawing.load(bytes(7, badBody, encoding: encoding, bom: bom)))
            snapshot = try XCTUnwrap(body["NestingSnapshot"] as? [String: Any])
            snapshot["Placements"] = "invalid"; badBody["NestingSnapshot"] = snapshot
            XCTAssertThrowsError(try Drawing.load(bytes(7, badBody, encoding: encoding, bom: bom)))
            var pieces = try XCTUnwrap(body["Pieces"] as? [[String: Any]])
            pieces[0].removeValue(forKey: "Id"); badBody = body; badBody["Pieces"] = pieces
            XCTAssertThrowsError(try Drawing.load(bytes(7, badBody, encoding: encoding, bom: bom)))
        }
        let text = try XCTUnwrap(String(data: original, encoding: .utf8))
        let duplicate = text.replacingOccurrences(of: "\"RotationTrace\": {", with: "\"RotationTrace\": {}, \"RotationTrace\": {")
        XCTAssertNotEqual(duplicate, text)
        for (encoding, bom) in encodings {
            var data = Data(bom); data.append(try XCTUnwrap(duplicate.data(using: encoding)))
            let before = data; XCTAssertThrowsError(try Drawing.load(data)); XCTAssertEqual(data, before)
        }
    }
    func testPieceDisplayEscapesBidiWithoutChangingSourceName() throws {
        let bytes = Data(#"{"Name":"Proje","Pieces":[{"Name":"A\u202EB","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]}]}"#.utf8)
        let drawing = try Drawing.load(bytes)
        XCTAssertEqual(drawing.pieces[0].name, "A\u{202E}B")
        XCTAssertEqual(drawing.pieces[0].displayName(index: 0), #"A\u{202E}B"#)
    }
    func testDrawingParsingStopsDuringSupersededLargePiece() throws {
        let point = #"{"X":1,"Y":2}"#
        let points = Array(repeating: point, count: 4096).joined(separator: ",")
        let bytes = Data("{\"Name\":\"Büyük\",\"Pieces\":[{\"Closed\":false,\"Points\":[\(points)]}]}".utf8)
        var checks = 0
        XCTAssertThrowsError(try Drawing.load(bytes, cancelled: {
            checks += 1
            return checks >= 25
        })) { error in XCTAssertTrue(error is CancellationError) }
        XCTAssertEqual(checks, 25)
        let normal = Data(#"{"Name":"Normal","Pieces":[{"Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]}]}"#.utf8)
        XCTAssertEqual(try Drawing.load(normal).pieces.count, 1)
    }
    func testPickerCancellationIsDistinctFromRealFileErrors() {
        XCTAssertTrue(isFilePickerCancellation(CocoaError(.userCancelled)))
        XCTAssertFalse(isFilePickerCancellation(CocoaError(.fileWriteNoPermission)))
    }

    func testRejectedSelectionStopsOlderLoadFromReplacingCurrentModel() {
        var state = ModelLoadState()
        let older = state.begin()
        state.invalidate()
        XCTAssertFalse(state.isLoading)
        XCTAssertFalse(state.finish(older))
    }

    func testOlderCompletionCannotStopNewerLoad() {
        var state = ModelLoadState()
        let older = state.begin()
        let newer = state.begin()
        XCTAssertFalse(state.finish(older))
        XCTAssertTrue(state.isLoading)
        XCTAssertTrue(state.finish(newer))
        XCTAssertFalse(state.isLoading)
    }
    func testPickerFailureStopsOlderLoad() {
        var state = ModelLoadState()
        let older = state.begin()
        XCTAssertTrue(state.rejectPickerFailure(CocoaError(.fileReadNoPermission)))
        XCTAssertFalse(state.isLoading)
        XCTAssertFalse(state.finish(older))
    }

    func testPickerCancellationKeepsOlderLoad() {
        var state = ModelLoadState()
        let older = state.begin()
        XCTAssertFalse(state.rejectPickerFailure(CocoaError(.userCancelled)))
        XCTAssertTrue(state.isLoading)
        XCTAssertTrue(state.finish(older))
    }
}
