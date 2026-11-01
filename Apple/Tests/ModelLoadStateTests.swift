import XCTest
@testable import TekstilCADViewer_iOS

final class ModelLoadStateTests: XCTestCase {
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
