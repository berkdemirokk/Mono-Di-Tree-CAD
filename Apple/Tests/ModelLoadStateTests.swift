import XCTest
@testable import TekstilCADViewer_iOS

final class ModelLoadStateTests: XCTestCase {
    func testDesktopV7PreviewKeepsSourceBytesAndRejectsInvalidVisibleMarks() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "desktop-synthetic-rotated-v7", withExtension: "tcad"))
        let data = try Data(contentsOf: url)
        let before = data
        let drawing = try Drawing.load(data)
        XCTAssertEqual(drawing.formatVersion, 7)
        XCTAssertEqual(drawing.pieces.count, 2)
        XCTAssertTrue(drawing.pieces.allSatisfy { !$0.notches.isEmpty && $0.grain != nil })
        XCTAssertEqual(data, before)
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
