import Foundation
import XCTest
import CSQLite
@testable import ModelStorePrototype

final class ModelStoreTests: XCTestCase {
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
