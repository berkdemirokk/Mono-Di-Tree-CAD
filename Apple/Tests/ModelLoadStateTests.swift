import XCTest
@testable import TekstilCADViewer_iOS

final class ModelLoadStateTests: XCTestCase {
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
}
