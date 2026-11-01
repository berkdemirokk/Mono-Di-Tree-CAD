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
        let db = root.appendingPathComponent("copy.sqlite"), doc = UUID()
        let coordinator = DebugPointCopyCoordinator(documentID: doc, data: Data([1, 0, 255]))
        let snapshot = coordinator.snapshot(name: "point")
        let id = UUID()
        try ModelStorePrototype.save(at: db, id: UUID().uuidString, name: "existing", data: Data([9]))
        coordinator.replaceDocument(documentID: doc, data: Data([2, 3, 4]))
        XCTAssertThrowsError(try coordinator.save(at: db, id: id, snapshot: snapshot))
        XCTAssertNil(try ModelStorePrototype.read(at: db, id: id.uuidString))
        coordinator.replaceDocument(documentID: doc, data: Data([2, 3, 4]))
        XCTAssertThrowsError(try coordinator.save(at: db, id: id, snapshot: snapshot))
        let current = coordinator.snapshot(name: "point")
        XCTAssertThrowsError(try coordinator.save(at: db, id: id, snapshot: current) { throw Cancelled() })
        XCTAssertNil(try ModelStorePrototype.read(at: db, id: id.uuidString))
    }

    func testSnapshotUsesBytesFromSameSerializedRevision() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let db = root.appendingPathComponent("copy.sqlite"), doc = UUID()
        let oldData = Data([1, 0, 255]), newData = Data([2, 0, 254])
        let coordinator = DebugPointCopyCoordinator(documentID: doc, data: oldData)
        let old = coordinator.snapshot(name: "old")
        coordinator.replaceDocument(documentID: doc, data: newData)
        let current = coordinator.snapshot(name: "new")
        XCTAssertNotEqual(old.revision, current.revision)
        XCTAssertEqual(old.data, oldData)
        XCTAssertEqual(current.data, newData)
        let forged = DebugPointCopyStore.Snapshot(documentID: current.documentID, revision: current.revision, name: "forged", data: oldData)
        XCTAssertThrowsError(try coordinator.save(at: db, id: UUID(), snapshot: forged))
        let staleID = UUID(), newID = UUID()
        try ModelStorePrototype.save(at: db, id: UUID().uuidString, name: "existing", data: Data([9]))
        XCTAssertThrowsError(try coordinator.save(at: db, id: staleID, snapshot: old))
        XCTAssertNil(try ModelStorePrototype.read(at: db, id: staleID.uuidString))
        try coordinator.save(at: db, id: newID, snapshot: current)
        XCTAssertEqual(try ModelStorePrototype.read(at: db, id: newID.uuidString)?.data, newData)
    }
    func testConcurrentSaveAndChangeLeaveOldSnapshotStale() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let db = root.appendingPathComponent("copy.sqlite"), doc = UUID()
        let coordinator = DebugPointCopyCoordinator(documentID: doc, data: Data([1, 0, 255]))
        let snapshot = coordinator.snapshot(name: "point")
        let entered = DispatchSemaphore(value: 0), release = DispatchSemaphore(value: 0)
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
            coordinator.replaceDocument(documentID: doc, data: Data([2, 3, 4]))
            changeDone.fulfill()
        }
        release.signal()
        wait(for: [saveDone, changeDone], timeout: 10)
        XCTAssertEqual(try ModelStorePrototype.read(at: db, id: id.uuidString)?.data, snapshot.data)
        let staleID = UUID()
        XCTAssertThrowsError(try coordinator.save(at: db, id: staleID, snapshot: snapshot))
        XCTAssertNil(try ModelStorePrototype.read(at: db, id: staleID.uuidString))
    }
}
#endif
