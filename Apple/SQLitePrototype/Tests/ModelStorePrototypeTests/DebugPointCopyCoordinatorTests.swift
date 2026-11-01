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
        let coordinator = try DebugPointCopyCoordinator(documentID: doc, data: Data([1, 0, 255]))
        let snapshot = coordinator.snapshot(name: "point")
        let id = UUID()
        try ModelStorePrototype.save(at: db, id: UUID().uuidString, name: "existing", data: Data([9]))
        try coordinator.replaceDocument(documentID: doc, data: Data([2, 3, 4]))
        XCTAssertThrowsError(try coordinator.save(at: db, id: id, snapshot: snapshot))
        XCTAssertNil(try ModelStorePrototype.read(at: db, id: id.uuidString))
        try coordinator.replaceDocument(documentID: doc, data: Data([2, 3, 4]))
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
        let coordinator = try DebugPointCopyCoordinator(documentID: doc, data: oldData)
        let old = coordinator.snapshot(name: "old")
        try coordinator.replaceDocument(documentID: doc, data: newData)
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
    func testOversizedStateIsRejectedWithoutChangingCurrentDocument() throws {
        let oversized = Data(count: DebugPointCopyStore.maximumBytes + 1)
        let doc = UUID(), other = UUID(), original = Data([1, 0, 255])
        XCTAssertThrowsError(try DebugPointCopyCoordinator(documentID: doc, data: oversized))
        let coordinator = try DebugPointCopyCoordinator(documentID: doc, data: original)
        let before = coordinator.snapshot(name: "before")
        XCTAssertThrowsError(try coordinator.replaceDocument(documentID: other, data: oversized))
        let after = coordinator.snapshot(name: "after")
        XCTAssertEqual(after.documentID, before.documentID)
        XCTAssertEqual(after.revision, before.revision)
        XCTAssertEqual(after.data, original)
    }
    func testLateEditCannotReplaceNewDocumentAndCanSaveTheNewState() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let db = root.appendingPathComponent("copy.sqlite"), oldID = UUID(), newID = UUID()
        let coordinator = try DebugPointCopyCoordinator(documentID: oldID, data: Data([1]))
        let editSource = try coordinator.beginEdit(name: "old")
        try coordinator.replaceDocument(documentID: newID, data: Data([2]))
        let newSource = try coordinator.beginEdit(name: "new")
        XCTAssertThrowsError(try coordinator.applyEdit(editSource, data: Data([3])))
        let after = coordinator.snapshot(name: "new")
        XCTAssertEqual(after.documentID, newID)
        XCTAssertEqual(after.revision, newSource.source.revision)
        XCTAssertEqual(after.data, Data([2]))
        try coordinator.applyEdit(newSource, data: Data([4]))
        let edited = coordinator.snapshot(name: "edited"), savedID = UUID()
        XCTAssertNotEqual(edited.revision, newSource.source.revision)
        try coordinator.save(at: db, id: savedID, snapshot: edited)
        XCTAssertEqual(try ModelStorePrototype.read(at: db, id: savedID.uuidString)?.data, Data([4]))
        XCTAssertThrowsError(try coordinator.applyEdit(newSource, data: Data([5])))
    }

    func testCancellationWinsBeforeApplyAndDoesNotCancelNewRequest() throws {
        let coordinator = try DebugPointCopyCoordinator(documentID: UUID(), data: Data([1]))
        let cancelled = try coordinator.beginEdit(name: "point")
        XCTAssertTrue(coordinator.cancelEdit(cancelled))
        XCTAssertThrowsError(try coordinator.applyEdit(cancelled, data: Data([2])))
        XCTAssertEqual(coordinator.snapshot(name: "point").data, Data([1]))
        let current = try coordinator.beginEdit(name: "point")
        XCTAssertFalse(coordinator.cancelEdit(cancelled))
        try coordinator.applyEdit(current, data: Data([3]))
        XCTAssertFalse(coordinator.cancelEdit(current))
        XCTAssertThrowsError(try coordinator.applyEdit(current, data: Data([4])))
        XCTAssertEqual(coordinator.snapshot(name: "point").data, Data([3]))
    }
    func testAppliedEditSnapshotCannotSaveAfterAnotherDocumentOpens() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let db = root.appendingPathComponent("copy.sqlite"), sourceID = UUID(), otherID = UUID()
        let coordinator = try DebugPointCopyCoordinator(documentID: sourceID, data: Data([1]))
        let request = try coordinator.beginEdit(name: "edited copy")
        let applied = try coordinator.applyEdit(request, data: Data([2]))
        XCTAssertEqual(applied.documentID, sourceID)
        XCTAssertEqual(applied.data, Data([2]))
        XCTAssertEqual(applied.name, "edited copy")
        try coordinator.replaceDocument(documentID: otherID, data: Data([3]))
        XCTAssertThrowsError(try coordinator.save(at: db, snapshot: applied))
        XCTAssertFalse(FileManager.default.fileExists(atPath: db.path))
        let current = coordinator.snapshot(name: "other")
        XCTAssertEqual(current.documentID, otherID)
        XCTAssertEqual(current.data, Data([3]))
    }
    func testOversizedEditLeavesStateUnchanged() throws {
        let coordinator = try DebugPointCopyCoordinator(documentID: UUID(), data: Data([1]))
        let request = try coordinator.beginEdit(name: "point")
        let source = request.source
        XCTAssertThrowsError(try coordinator.applyEdit(request, data: Data(count: DebugPointCopyStore.maximumBytes + 1)))
        XCTAssertThrowsError(try coordinator.applyEdit(request, data: Data([2])))
        XCTAssertFalse(coordinator.cancelEdit(request))
        let after = coordinator.snapshot(name: "point")
        XCTAssertEqual(after.documentID, source.documentID)
        XCTAssertEqual(after.revision, source.revision)
        XCTAssertEqual(after.data, source.data)
    }
    func testCancelledSaveRollsBackAndCommittedSaveCannotBeCancelled() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let db = root.appendingPathComponent("copy.sqlite")
        let coordinator = try DebugPointCopyCoordinator(documentID: UUID(), data: Data([1, 255]))
        let snapshot = coordinator.snapshot(name: "point"), id = UUID()
        try ModelStorePrototype.save(at: db, id: "existing", name: "old", data: Data([9]))
        let cancelled = DebugSaveCancellation()
        XCTAssertThrowsError(try coordinator.save(at: db, id: id, snapshot: snapshot, cancellation: cancelled) {
            XCTAssertTrue(cancelled.cancel())
        })
        XCTAssertNil(try ModelStorePrototype.read(at: db, id: id.uuidString))
        XCTAssertEqual(try ModelStorePrototype.read(at: db, id: "existing")?.data, Data([9]))
        let committed = DebugSaveCancellation()
        try coordinator.save(at: db, id: id, snapshot: snapshot, cancellation: committed)
        XCTAssertFalse(committed.cancel())
        XCTAssertEqual(try ModelStorePrototype.read(at: db, id: id.uuidString)?.data, snapshot.data)
    }
    func testPendingOpenInvalidatesEditAndBlocksSaveUntilCancellation() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let db = root.appendingPathComponent("copy.sqlite"), originalID = UUID()
        let coordinator = try DebugPointCopyCoordinator(documentID: originalID, data: Data([1]))
        let snapshot = coordinator.snapshot(name: "original")
        let edit = try coordinator.beginEdit(name: "original")
        let oldOpen = coordinator.beginOpen(), activeOpen = coordinator.beginOpen()
        XCTAssertFalse(coordinator.cancelOpen(oldOpen))
        XCTAssertThrowsError(try coordinator.finishOpen(oldOpen, documentID: UUID(), data: Data([2])))
        XCTAssertThrowsError(try coordinator.applyEdit(edit, data: Data([3])))
        XCTAssertThrowsError(try coordinator.beginEdit(name: "blocked"))
        XCTAssertThrowsError(try coordinator.save(at: db, snapshot: snapshot))
        XCTAssertFalse(FileManager.default.fileExists(atPath: db.path))
        XCTAssertTrue(coordinator.cancelOpen(activeOpen))
        XCTAssertThrowsError(try coordinator.finishOpen(activeOpen, documentID: UUID(), data: Data([4])))
        XCTAssertEqual(coordinator.snapshot(name: "original").documentID, originalID)
        let id = UUID()
        try coordinator.save(at: db, id: id, snapshot: snapshot)
        XCTAssertEqual(try ModelStorePrototype.read(at: db, id: id.uuidString)?.data, Data([1]))
    }

    func testSuccessfulOpenRejectsPreviousDocumentSnapshot() throws {
        let coordinator = try DebugPointCopyCoordinator(documentID: UUID(), data: Data([1]))
        let before = coordinator.snapshot(name: "old"), open = coordinator.beginOpen(), newID = UUID()
        try coordinator.finishOpen(open, documentID: newID, data: Data([2]))
        let current = coordinator.snapshot(name: "new")
        XCTAssertEqual(current.documentID, newID)
        XCTAssertEqual(current.data, Data([2]))
        XCTAssertNotEqual(current.revision, before.revision)
    }
    func testConcurrentSaveAndChangeLeaveOldSnapshotStale() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let db = root.appendingPathComponent("copy.sqlite"), doc = UUID()
        let coordinator = try DebugPointCopyCoordinator(documentID: doc, data: Data([1, 0, 255]))
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
            do { try coordinator.replaceDocument(documentID: doc, data: Data([2, 3, 4])) }
            catch { XCTFail("Unexpected document update failure: \(error)") }
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
