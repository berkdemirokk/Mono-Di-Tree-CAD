import XCTest
@testable import TekstilCADViewer_iOS

final class ModelLoadStateTests: XCTestCase {
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
