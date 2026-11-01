#if DEBUG
import Foundation
import XCTest
@testable import ModelStorePrototype

final class DebugOpenStateTests: XCTestCase {
    func testStaleCompletionAndCancellationPreserveOpenDocument() throws {
        let state = DebugOpenState(), firstID = UUID(), first = state.begin()
        guard case .opened(let original) = try state.finish(first, documentID: firstID, data: Data([1, 0, 255])) else {
            return XCTFail("Expected initial document")
        }
        let old = state.begin(), latest = state.begin()
        XCTAssertFalse(state.cancel(old))
        guard case .stale = try state.finish(old, documentID: UUID(), data: Data([2])) else {
            return XCTFail("Old load was accepted")
        }
        XCTAssertTrue(state.cancel(latest))
        guard case .stale = try state.finish(latest, documentID: UUID(), data: Data([3])) else {
            return XCTFail("Cancelled load was accepted")
        }
        let retained = try XCTUnwrap(state.snapshot())
        XCTAssertEqual(retained.id, original.id)
        XCTAssertEqual(retained.revision, original.revision)
        XCTAssertEqual(retained.data, original.data)
    }

    func testOversizeFailureConsumesRequestButPreservesDocument() throws {
        let state = DebugOpenState(), initial = state.begin(), id = UUID()
        guard case .opened(let original) = try state.finish(initial, documentID: id, data: Data([1])) else {
            return XCTFail("Expected initial document")
        }
        let request = state.begin()
        XCTAssertThrowsError(try state.finish(request, documentID: UUID(), data: Data(count: DebugPointCopyStore.maximumBytes + 1)))
        guard case .stale = try state.finish(request, documentID: UUID(), data: Data([2])) else {
            return XCTFail("Rejected request was reusable")
        }
        let retained = try XCTUnwrap(state.snapshot())
        XCTAssertEqual(retained.id, original.id)
        XCTAssertEqual(retained.revision, original.revision)
        XCTAssertEqual(retained.data, original.data)
    }

    func testSuccessReplacesDocumentAndChangesRevision() throws {
        let state = DebugOpenState(), id = UUID()
        guard case .opened(let first) = try state.finish(state.begin(), documentID: id, data: Data([1])) else {
            return XCTFail("Expected initial document")
        }
        guard case .opened(let second) = try state.finish(state.begin(), documentID: id, data: Data([2])) else {
            return XCTFail("Expected replacement")
        }
        XCTAssertNotEqual(first.revision, second.revision)
        XCTAssertEqual(state.snapshot()?.data, Data([2]))
    }
}
#endif
