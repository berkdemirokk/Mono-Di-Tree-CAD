#if DEBUG
import Foundation
import XCTest
@testable import ModelStorePrototype

final class DebugPointCopyStoreTests: XCTestCase {
    private func withDatabase(_ body: (URL) throws -> Void) throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        try body(root.appendingPathComponent("points.sqlite"))
    }

    func testRollbackPreservesExistingAndDoesNotPublishEditedCopy() throws {
        struct Interrupted: Error { }
        try withDatabase { db in
            let existingID = UUID(), newID = UUID()
            let original = Data([0, 255, 1]), edited = Data([0, 254, 2])
            try DebugPointCopyStore.save(at: db, id: existingID, name: "existing", data: original)
            XCTAssertThrowsError(try DebugPointCopyStore.save(at: db, id: newID, name: "edited", data: edited) {
                throw Interrupted()
            })
            XCTAssertNil(try ModelStorePrototype.read(at: db, id: newID.uuidString))
            let existing = try XCTUnwrap(ModelStorePrototype.read(at: db, id: existingID.uuidString))
            XCTAssertEqual(existing.name, "existing")
            XCTAssertEqual(existing.data, original)
            try DebugPointCopyStore.save(at: db, id: newID, name: "edited", data: edited)
            XCTAssertEqual(try ModelStorePrototype.read(at: db, id: newID.uuidString)?.data, edited)
        }
    }

    func testDuplicateCannotOverwriteSavedBytesOrName() throws {
        try withDatabase { db in
            let id = UUID(), original = Data([1, 0, 255])
            try DebugPointCopyStore.save(at: db, id: id, name: "first", data: original)
            XCTAssertThrowsError(try DebugPointCopyStore.save(at: db, id: id, name: "second", data: Data([2])))
            let saved = try XCTUnwrap(ModelStorePrototype.read(at: db, id: id.uuidString))
            XCTAssertEqual(saved.name, "first")
            XCTAssertEqual(saved.data, original)
            XCTAssertEqual(try ModelStorePrototype.list(at: db).count, 1)
        }
    }

    func testOversizeDoesNotCreateDatabase() throws {
        try withDatabase { db in
            XCTAssertThrowsError(try DebugPointCopyStore.save(at: db, name: "large", data: Data(count: DebugPointCopyStore.maximumBytes + 1)))
            XCTAssertFalse(FileManager.default.fileExists(atPath: db.path))
        }
    }
}
#endif
