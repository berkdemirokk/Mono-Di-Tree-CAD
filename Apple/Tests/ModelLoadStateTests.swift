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
