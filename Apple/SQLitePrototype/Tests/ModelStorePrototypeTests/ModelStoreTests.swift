import Foundation
import XCTest
import CSQLite
@testable import ModelStorePrototype

final class ModelStoreTests: XCTestCase {
    func testLegacyFileCannotBeAccidentallyUsedAsDestination() throws {
        try withDatabase { url in
            let legacy = Data("[1,{\"Name\":\"original\",\"Pieces\":[]}]".utf8)
            try legacy.write(to: url)
            XCTAssertThrowsError(try ModelStorePrototype.importSnapshots(at: url, records: [
                .init(id: "copy", name: "Copy", data: legacy)
            ]))
            XCTAssertEqual(try Data(contentsOf: url), legacy)
        }
    }

    func testUnrecognizedDatabaseAndFutureVersionAreNotModified() throws {
        try withDatabase { url in
            var db: OpaquePointer?
            XCTAssertEqual(sqlite3_open(url.path, &db), SQLITE_OK)
            let handle = try XCTUnwrap(db)
            XCTAssertEqual(sqlite3_exec(handle, "CREATE TABLE customer_data(value TEXT); INSERT INTO customer_data VALUES('keep');", nil, nil, nil), SQLITE_OK)
            XCTAssertEqual(sqlite3_close(handle), SQLITE_OK)
            let original = try Data(contentsOf: url)
            XCTAssertThrowsError(try ModelStorePrototype.save(at: url, id: "x", name: "X", data: Data([1])))
            XCTAssertEqual(try Data(contentsOf: url), original)
        }
        try withDatabase { url in
            try ModelStorePrototype.save(at: url, id: "old", name: "Old", data: Data([7]))
            var db: OpaquePointer?
            XCTAssertEqual(sqlite3_open(url.path, &db), SQLITE_OK)
            let handle = try XCTUnwrap(db)
            XCTAssertEqual(sqlite3_exec(handle, "PRAGMA user_version=2", nil, nil, nil), SQLITE_OK)
            XCTAssertEqual(sqlite3_close(handle), SQLITE_OK)
            let original = try Data(contentsOf: url)
            XCTAssertThrowsError(try ModelStorePrototype.importSnapshots(at: url, records: [.init(id: "new", name: "New", data: Data([8]))]))
            XCTAssertEqual(try Data(contentsOf: url), original)
        }
    }

    func testCorruptPayloadTypeIsRejectedInsteadOfConverted() throws {
        try withDatabase { url in
            try ModelStorePrototype.save(at: url, id: "x", name: "X", data: Data([1]))
            var db: OpaquePointer?
            XCTAssertEqual(sqlite3_open(url.path, &db), SQLITE_OK)
            let handle = try XCTUnwrap(db)
            XCTAssertEqual(sqlite3_exec(handle, "UPDATE models SET payload='not a blob' WHERE id='x'", nil, nil, nil), SQLITE_OK)
            XCTAssertEqual(sqlite3_close(handle), SQLITE_OK)
            XCTAssertThrowsError(try ModelStorePrototype.read(at: url, id: "x"))
        }
    }

    func testExplicitImportIsAtomicAndLeavesCallerSourceUntouched() throws {
        struct Interrupted: Error { }
        try withDatabase { url in
            let source = url.deletingLastPathComponent().appendingPathComponent("legacy.tcad")
            let original = Data([0, 1, 255])
            try original.write(to: source)
            try ModelStorePrototype.save(at: url, id: "existing", name: "Existing", data: Data([9]))
            let snapshots = [ModelStorePrototype.Record(id: "a", name: "A", data: try Data(contentsOf: source)),
                             ModelStorePrototype.Record(id: "b", name: "B", data: Data([2]))]
            XCTAssertThrowsError(try ModelStorePrototype.importSnapshots(at: url, records: snapshots) { index in
                if index == 0 { throw Interrupted() }
            })
            XCTAssertNil(try ModelStorePrototype.read(at: url, id: "a"))
            XCTAssertNil(try ModelStorePrototype.read(at: url, id: "b"))
            XCTAssertEqual(try Data(contentsOf: source), original)
            try ModelStorePrototype.importSnapshots(at: url, records: snapshots)
            XCTAssertEqual(try ModelStorePrototype.read(at: url, id: "a")?.data, original)
            XCTAssertThrowsError(try ModelStorePrototype.importSnapshots(at: url, records: [
                .init(id: "new", name: "New", data: Data([3])), snapshots[0]
            ]))
            XCTAssertNil(try ModelStorePrototype.read(at: url, id: "new"))
            XCTAssertEqual(try ModelStorePrototype.read(at: url, id: "existing")?.data, Data([9]))
            XCTAssertEqual(try Data(contentsOf: source), original)
        }
    }

    func testListAndDeleteUseIdentityAndKeepOtherSameNamedModels() throws {
        try withDatabase { url in
            try ModelStorePrototype.save(at: url, id: "b", name: "Aynı", data: Data([2]))
            try ModelStorePrototype.save(at: url, id: "a", name: "Aynı", data: Data([1]))
            XCTAssertEqual(try ModelStorePrototype.list(at: url).map(\.id), ["a", "b"])
            XCTAssertTrue(try ModelStorePrototype.delete(at: url, id: "a"))
            XCTAssertFalse(try ModelStorePrototype.delete(at: url, id: "a"))
            XCTAssertNil(try ModelStorePrototype.read(at: url, id: "a"))
            XCTAssertEqual(try ModelStorePrototype.read(at: url, id: "b")?.data, Data([2]))
            XCTAssertEqual(try ModelStorePrototype.list(at: url).map(\.id), ["b"])
        }
    }

    func testReadListAndDeleteDoNotCreateMissingDatabase() throws {
        try withDatabase { url in
            XCTAssertThrowsError(try ModelStorePrototype.read(at: url, id: "x"))
            XCTAssertThrowsError(try ModelStorePrototype.list(at: url))
            XCTAssertThrowsError(try ModelStorePrototype.delete(at: url, id: "x"))
            XCTAssertFalse(FileManager.default.fileExists(atPath: url.path))
        }
    }

    private func withDatabase(_ body: (URL) throws -> Void) throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        try body(root.appendingPathComponent("models.sqlite"))
    }

    func testExactBytesAndUnicodeNameSurviveReopen() throws {
        try withDatabase { url in
            let bytes = Data([0, 255, 10, 0, 7])
            try ModelStorePrototype.save(at: url, id: "model", name: "İş\0Kol", data: bytes)
            let record = try XCTUnwrap(ModelStorePrototype.read(at: url, id: "model"))
            XCTAssertEqual(record.name, "İş\0Kol")
            XCTAssertEqual(record.data, bytes)
            try ModelStorePrototype.save(at: url, id: "empty", name: "Boş", data: Data())
            XCTAssertEqual(try ModelStorePrototype.read(at: url, id: "empty")?.data, Data())
        }
    }

    func testFailureBeforeCommitRollsBackWholeRecordAndPreservesExisting() throws {
        struct Interrupted: Error { }
        try withDatabase { url in
            let original = Data("original".utf8)
            try ModelStorePrototype.save(at: url, id: "existing", name: "Old", data: original)
            XCTAssertThrowsError(try ModelStorePrototype.save(at: url, id: "new", name: "New", data: Data("new".utf8)) { throw Interrupted() })
            XCTAssertNil(try ModelStorePrototype.read(at: url, id: "new"))
            XCTAssertEqual(try ModelStorePrototype.read(at: url, id: "existing")?.data, original)
            XCTAssertThrowsError(try ModelStorePrototype.save(at: url, id: "existing", name: "Overwrite", data: Data()))
            XCTAssertEqual(try ModelStorePrototype.read(at: url, id: "existing")?.name, "Old")
            XCTAssertEqual(try ModelStorePrototype.read(at: url, id: "existing")?.data, original)
        }
    }

    func testClosingConnectionWithUncommittedInsertDoesNotPublish() throws {
        try withDatabase { url in
            try ModelStorePrototype.save(at: url, id: "existing", name: "Old", data: Data([1]))
            var db: OpaquePointer?
            XCTAssertEqual(sqlite3_open_v2(url.path, &db, SQLITE_OPEN_READWRITE, nil), SQLITE_OK)
            let handle = try XCTUnwrap(db)
            XCTAssertEqual(sqlite3_exec(handle, "BEGIN IMMEDIATE; INSERT INTO models VALUES('pending','Pending',X'02');", nil, nil, nil), SQLITE_OK)
            XCTAssertEqual(sqlite3_close(handle), SQLITE_OK)
            XCTAssertNil(try ModelStorePrototype.read(at: url, id: "pending"))
            XCTAssertEqual(try ModelStorePrototype.read(at: url, id: "existing")?.data, Data([1]))
        }
    }

    func testConcurrentConnectionsKeepEachModelAndItsNameTogether() throws {
        try withDatabase { url in
            try ModelStorePrototype.save(at: url, id: "initial", name: "Initial", data: Data())
            let lock = NSLock()
            var failures: [String] = []
            DispatchQueue.concurrentPerform(iterations: 16) { index in
                do {
                    try ModelStorePrototype.save(at: url, id: "id-\(index)", name: "name-\(index)", data: Data("payload-\(index)".utf8))
                } catch {
                    lock.lock(); failures.append("\(index): \(error)"); lock.unlock()
                }
            }
            XCTAssertTrue(failures.isEmpty, failures.joined(separator: ", "))
            for index in 0..<16 {
                let row = try XCTUnwrap(ModelStorePrototype.read(at: url, id: "id-\(index)"))
                XCTAssertEqual(row.name, "name-\(index)")
                XCTAssertEqual(row.data, Data("payload-\(index)".utf8))
            }
        }
    }
}
