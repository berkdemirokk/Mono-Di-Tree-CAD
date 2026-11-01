#if DEBUG
import Foundation
import XCTest
@testable import ModelStorePrototype

final class DebugPointCopyCoordinatorTests: XCTestCase {
    func testStaleAndThrownAuthorizationRollBack() throws {
        struct Cancelled: Error { }
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let db = root.appendingPathComponent("copy.sqlite"), doc = UUID(), old = UUID(), new = UUID()
        let snapshot = DebugPointCopyStore.Snapshot(documentID: doc, revision: old, name: "point", data: Data([1, 0, 255]))
        let coordinator = DebugPointCopyCoordinator(documentID: doc, revision: old)
        let id = UUID()
        coordinator.replaceDocument(documentID: doc, revision: new)
        XCTAssertThrowsError(try coordinator.save(at: db, id: id, snapshot: snapshot))
        XCTAssertNil(try ModelStorePrototype.read(at: db, id: id.uuidString))
        coordinator.replaceDocument(documentID: doc, revision: old)
        XCTAssertThrowsError(try coordinator.save(at: db, id: id, snapshot: snapshot) { throw Cancelled() })
        XCTAssertNil(try ModelStorePrototype.read(at: db, id: id.uuidString))
    }

    func testDocumentChangeWaitsForCommitAndOldSnapshotThenFails() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let db = root.appendingPathComponent("copy.sqlite"), doc = UUID(), old = UUID(), new = UUID()
        let snapshot = DebugPointCopyStore.Snapshot(documentID: doc, revision: old, name: "point", data: Data([1, 2, 3]))
        let coordinator = DebugPointCopyCoordinator(documentID: doc, revision: old)
        let entered = DispatchSemaphore(value: 0), release = DispatchSemaphore(value: 0)
        let attempted = DispatchSemaphore(value: 0), changed = DispatchSemaphore(value: 0)
        let saveDone = expectation(description: "save completed")
        let changeDone = expectation(description: "change completed")
        let id = UUID()
        DispatchQueue.global().async {
            defer { saveDone.fulfill() }
            do {
                try coordinator.save(at: db, id: id, snapshot: snapshot) {
                    entered.signal()
                    guard release.wait(timeout: .now() + 5) == .success else { throw NSError(domain: "test", code: 1) }
                }
            } catch { XCTFail("Unexpected save failure: \(error)") }
        }
        guard entered.wait(timeout: .now() + 5) == .success else {
            release.signal(); XCTFail("Save did not reach transaction"); return
        }
        DispatchQueue.global().async {
            attempted.signal()
            coordinator.replaceDocument(documentID: doc, revision: new)
            changed.signal()
            changeDone.fulfill()
        }
        XCTAssertEqual(attempted.wait(timeout: .now() + 5), .success)
        XCTAssertEqual(changed.wait(timeout: .now() + 0.1), .timedOut)
        release.signal()
        wait(for: [saveDone, changeDone], timeout: 10)
        XCTAssertEqual(try ModelStorePrototype.read(at: db, id: id.uuidString)?.data, snapshot.data)
        let staleID = UUID()
        XCTAssertThrowsError(try coordinator.save(at: db, id: staleID, snapshot: snapshot))
        XCTAssertNil(try ModelStorePrototype.read(at: db, id: staleID.uuidString))
    }
}
#endif
