import Foundation
import CSQLite

// Research-only store. Each operation has its own connection; callers must put
// the database in an app-private directory and preserve its journal on backup.
public enum ModelStorePrototype {
    public struct Record: Equatable {
        public let id: String
        public let name: String
        public let data: Data
        public init(id: String, name: String, data: Data) { self.id = id; self.name = name; self.data = data }
    }

    public struct Summary: Equatable {
        public let id: String
        public let name: String
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

    private static func integer(_ db: OpaquePointer, _ sql: String) throws -> Int64 {
        var statement: OpaquePointer?
        let prepared = sqlite3_prepare_v2(db, sql, -1, &statement, nil)
        guard prepared == SQLITE_OK, let stmt = statement else { throw fail(db, prepared) }
        defer { sqlite3_finalize(stmt) }
        let step = sqlite3_step(stmt)
        guard step == SQLITE_ROW, sqlite3_column_type(stmt, 0) == SQLITE_INTEGER else {
            throw StoreError(code: SQLITE_CORRUPT, message: "Invalid database metadata")
        }
        return sqlite3_column_int64(stmt, 0)
    }

    private static func validateSchema(_ db: OpaquePointer, allowInitialize: Bool) throws {
        let appID = try integer(db, "PRAGMA application_id")
        let version = try integer(db, "PRAGMA user_version")
        if appID == 0 && version == 0 && allowInitialize {
            let objects = try integer(db, "SELECT count(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%'")
            guard objects == 0 else { throw StoreError(code: SQLITE_NOTADB, message: "Unrecognized database") }
            try execute(db, "CREATE TABLE models (id TEXT PRIMARY KEY NOT NULL, name TEXT NOT NULL, payload BLOB NOT NULL)")
            try execute(db, "PRAGMA application_id=1413693764")
            try execute(db, "PRAGMA user_version=1")
        } else if appID != 1413693764 || version != 1 {
            throw StoreError(code: SQLITE_NOTADB, message: "Unrecognized or unsupported database")
        }
    }

    private static func connection<T>(_ url: URL, create: Bool = false, _ body: (OpaquePointer) throws -> T) throws -> T {
        guard url.isFileURL else { throw StoreError(code: SQLITE_CANTOPEN, message: "Database URL must be a file URL") }
        var db: OpaquePointer?
        let flags = SQLITE_OPEN_READWRITE | SQLITE_OPEN_FULLMUTEX | (create ? SQLITE_OPEN_CREATE : 0)
        let code = sqlite3_open_v2(url.path, &db, flags, nil)
        guard code == SQLITE_OK, let handle = db else {
            let error = fail(db, code)
            if let db { sqlite3_close(db) }
            throw error
        }
        defer { sqlite3_close(handle) }
        sqlite3_busy_timeout(handle, 10_000)
        // Never alter settings or add tables to an unrecognized database.
        if create {
            try transaction(handle) { try validateSchema(handle, allowInitialize: true) }
        } else {
            try validateSchema(handle, allowInitialize: false)
        }
        try execute(handle, "PRAGMA journal_mode=DELETE")
        try execute(handle, "PRAGMA synchronous=FULL")
        return try body(handle)
    }

    private static func transaction<T>(_ db: OpaquePointer, _ action: () throws -> T) throws -> T {
        try execute(db, "BEGIN IMMEDIATE")
        do {
            let result = try action()
            try execute(db, "COMMIT")
            return result
        } catch {
            try? execute(db, "ROLLBACK")
            throw error
        }
    }

    private static func insert(_ record: Record, into db: OpaquePointer) throws {
                var statement: OpaquePointer?
                let prepared = sqlite3_prepare_v2(db, "INSERT INTO models(id,name,payload) VALUES(?,?,?)", -1, &statement, nil)
                guard prepared == SQLITE_OK, let stmt = statement else { throw fail(db, prepared) }
                defer { sqlite3_finalize(stmt) }
                let idResult = bindText(stmt, 1, record.id)
                guard idResult == SQLITE_OK else { throw fail(db, idResult) }
                let nameResult = bindText(stmt, 2, record.name)
                guard nameResult == SQLITE_OK else { throw fail(db, nameResult) }
                let bound: Int32 = record.data.withUnsafeBytes { bytes in
                    if bytes.isEmpty { return sqlite3_bind_zeroblob(stmt, 3, 0) }
                    guard bytes.count <= Int(Int32.max) else { return SQLITE_TOOBIG }
                    return sqlite3_bind_blob(stmt, 3, bytes.baseAddress, Int32(bytes.count), transient)
                }
                guard bound == SQLITE_OK else { throw fail(db, bound) }
                let inserted = sqlite3_step(stmt)
                guard inserted == SQLITE_DONE else { throw fail(db, inserted) }
    }

    // afterInsert is a test seam for an error before COMMIT.
    public static func save(at url: URL, id: String = UUID().uuidString, name: String, data: Data,
                            afterInsert: (() throws -> Void)? = nil) throws {
        try connection(url, create: true) { db in
            try transaction(db) {
                try insert(Record(id: id, name: name, data: data), into: db)
                try afterInsert?()
            }
        }
    }

    // Explicit caller-provided snapshots only; source files are never touched.
    // Duplicate IDs or any insertion error roll back the entire batch.
    public static func importSnapshots(at url: URL, records: [Record], afterInsert: ((Int) throws -> Void)? = nil) throws {
        try connection(url, create: true) { db in
            try transaction(db) {
                for (index, record) in records.enumerated() {
                    try insert(record, into: db)
                    try afterInsert?(index)
                }
            }
        }
    }

    public static func list(at url: URL) throws -> [Summary] {
        try connection(url) { db in
            var statement: OpaquePointer?
            let result = sqlite3_prepare_v2(db, "SELECT id,name FROM models ORDER BY id", -1, &statement, nil)
            guard result == SQLITE_OK, let stmt = statement else { throw fail(db, result) }
            defer { sqlite3_finalize(stmt) }
            var rows: [Summary] = []
            while true {
                let step = sqlite3_step(stmt)
                if step == SQLITE_DONE { return rows }
                guard step == SQLITE_ROW else { throw fail(db, step) }
                guard let id = textColumn(stmt, 0), let name = textColumn(stmt, 1) else {
                    throw StoreError(code: SQLITE_CORRUPT, message: "Invalid model metadata")
                }
                rows.append(Summary(id: id, name: name))
            }
        }
    }

    public static func delete(at url: URL, id: String) throws -> Bool {
        try connection(url) { db in
            try transaction(db) {
                var statement: OpaquePointer?
                let prepared = sqlite3_prepare_v2(db, "DELETE FROM models WHERE id=?", -1, &statement, nil)
                guard prepared == SQLITE_OK, let stmt = statement else { throw fail(db, prepared) }
                defer { sqlite3_finalize(stmt) }
                let bound = bindText(stmt, 1, id)
                guard bound == SQLITE_OK else { throw fail(db, bound) }
                let step = sqlite3_step(stmt)
                guard step == SQLITE_DONE else { throw fail(db, step) }
                return sqlite3_changes(db) == 1
            }
        }
    }

    private static func textColumn(_ statement: OpaquePointer, _ index: Int32) -> String? {
        guard sqlite3_column_type(statement, index) == SQLITE_TEXT,
              let text = sqlite3_column_text(statement, index) else { return nil }
        return String(bytes: UnsafeBufferPointer(start: text, count: Int(sqlite3_column_bytes(statement, index))), encoding: .utf8)
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
            guard result == SQLITE_ROW else { throw fail(db, result) }
            guard let name = textColumn(stmt, 0), sqlite3_column_type(stmt, 1) == SQLITE_BLOB else {
                throw StoreError(code: SQLITE_CORRUPT, message: "Invalid model record")
            }
            let count = Int(sqlite3_column_bytes(stmt, 1))
            let data: Data
            if count == 0 { data = Data() }
            else if let blob = sqlite3_column_blob(stmt, 1) { data = Data(bytes: blob, count: count) }
            else { throw fail(db, sqlite3_errcode(db)) }
            return Record(id: id, name: name, data: data)
        }
    }
}
