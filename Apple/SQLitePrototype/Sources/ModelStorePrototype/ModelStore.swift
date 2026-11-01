import Foundation
import CSQLite

// Research-only store. Each operation has its own connection; callers must put
// the database in an app-private directory and preserve its journal on backup.
public enum ModelStorePrototype {
    public struct Record: Equatable {
        public let id: String
        public let name: String
        public let data: Data
    }

    public struct StoreError: Error {
        public let code: Int32
        public let message: String
    }

    private static let transient = unsafeBitCast(-1, to: sqlite3_destructor_type.self)

    private static func bindText(_ statement: OpaquePointer, _ index: Int32, _ value: String) -> Int32 {
        guard value.utf8.count <= Int(Int32.max) else { return SQLITE_TOOBIG }
        return value.withCString { sqlite3_bind_text(statement, index, $0, Int32(value.utf8.count), transient) }
    }

    private static func fail(_ db: OpaquePointer?, _ code: Int32) -> StoreError {
        StoreError(code: code, message: db.map { String(cString: sqlite3_errmsg($0)) } ?? "SQLite open failed")
    }

    private static func execute(_ db: OpaquePointer, _ sql: String) throws {
        let code = sqlite3_exec(db, sql, nil, nil, nil)
        guard code == SQLITE_OK else { throw fail(db, code) }
    }

    private static func connection<T>(_ url: URL, _ body: (OpaquePointer) throws -> T) throws -> T {
        var db: OpaquePointer?
        let code = sqlite3_open_v2(url.path, &db, SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX, nil)
        guard code == SQLITE_OK, let handle = db else {
            let error = fail(db, code)
            if let db { sqlite3_close(db) }
            throw error
        }
        defer { sqlite3_close(handle) }
        sqlite3_busy_timeout(handle, 10_000)
        try execute(handle, "PRAGMA journal_mode=DELETE")
        try execute(handle, "PRAGMA synchronous=FULL")
        try execute(handle, "CREATE TABLE IF NOT EXISTS models (id TEXT PRIMARY KEY NOT NULL, name TEXT NOT NULL, payload BLOB NOT NULL)")
        return try body(handle)
    }

    // afterInsert is a test seam for an error before COMMIT; never pass one in production.
    public static func save(at url: URL, id: String = UUID().uuidString, name: String, data: Data,
                            afterInsert: (() throws -> Void)? = nil) throws {
        try connection(url) { db in
            try execute(db, "BEGIN IMMEDIATE")
            do {
                var statement: OpaquePointer?
                let prepared = sqlite3_prepare_v2(db, "INSERT INTO models(id,name,payload) VALUES(?,?,?)", -1, &statement, nil)
                guard prepared == SQLITE_OK, let stmt = statement else { throw fail(db, prepared) }
                defer { sqlite3_finalize(stmt) }
                let idResult = bindText(stmt, 1, id)
                guard idResult == SQLITE_OK else { throw fail(db, idResult) }
                let nameResult = bindText(stmt, 2, name)
                guard nameResult == SQLITE_OK else { throw fail(db, nameResult) }
                let bound: Int32 = data.withUnsafeBytes { bytes in
                    if bytes.isEmpty { return sqlite3_bind_zeroblob(stmt, 3, 0) }
                    guard bytes.count <= Int(Int32.max) else { return SQLITE_TOOBIG }
                    return sqlite3_bind_blob(stmt, 3, bytes.baseAddress, Int32(bytes.count), transient)
                }
                guard bound == SQLITE_OK else { throw fail(db, bound) }
                let inserted = sqlite3_step(stmt)
                guard inserted == SQLITE_DONE else { throw fail(db, inserted) }
                try afterInsert?()
                try execute(db, "COMMIT")
            } catch {
                try? execute(db, "ROLLBACK")
                throw error
            }
        }
    }

    public static func read(at url: URL, id: String) throws -> Record? {
        try connection(url) { db in
            var statement: OpaquePointer?
            let prepared = sqlite3_prepare_v2(db, "SELECT name,payload FROM models WHERE id=?", -1, &statement, nil)
            guard prepared == SQLITE_OK, let stmt = statement else { throw fail(db, prepared) }
            defer { sqlite3_finalize(stmt) }
            let bound = bindText(stmt, 1, id)
            guard bound == SQLITE_OK else { throw fail(db, bound) }
            let result = sqlite3_step(stmt)
            if result == SQLITE_DONE { return nil }
            guard result == SQLITE_ROW, let name = sqlite3_column_text(stmt, 0) else { throw fail(db, result) }
            let count = Int(sqlite3_column_bytes(stmt, 1))
            let data = count == 0 ? Data() : Data(bytes: sqlite3_column_blob(stmt, 1)!, count: count)
            let nameCount = Int(sqlite3_column_bytes(stmt, 0))
            let nameBytes = UnsafeBufferPointer(start: name, count: nameCount)
            return Record(id: id, name: String(decoding: nameBytes, as: UTF8.self), data: data)
        }
    }
}
