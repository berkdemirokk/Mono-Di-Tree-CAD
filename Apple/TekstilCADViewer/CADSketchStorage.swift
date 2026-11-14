import Foundation
import Darwin

struct DraftProject: Equatable, Codable {
    var id = UUID()
    var name: String
    var pieces: [DraftPiece]

    static var blank: DraftProject { DraftProject(name: "Yeni kalıp", pieces: []) }
    static var example: DraftProject { DraftProject(name: "Örnek kalıp", pieces: [
        DraftPiece(name: "Ön parça", closed: true, points: [
            DraftPoint(x: 0, y: 0), DraftPoint(x: 100, y: 0),
            DraftPoint(x: 100, y: 160), DraftPoint(x: 0, y: 160)
        ]),
        DraftPiece(name: "Çizgi", closed: false, points: [
            DraftPoint(x: 125, y: 0), DraftPoint(x: 225, y: 100)
        ])
    ]) }
}

enum SketchStoreError: LocalizedError {
    case invalid, oversized, identity, changed

    var errorDescription: String? {
        switch self {
        case .invalid: return "Dosyanın içeriği veya sürümü desteklenmiyor."
        case .oversized: return "Dosya izin verilen boyutu aşıyor."
        case .identity: return "Dosyanın proje kimliği kayıtla eşleşmiyor."
        case .changed: return "Kayıt başka bir çalışma tarafından değiştirildi. Projeyi yeniden açın."
        }
    }
}

enum CADFileIO {
    /// Lock a permanent sidecar inode rather than the atomically replaced data
    /// file. Cooperating editors/processes serialize check-and-write this way.
    /// Do not unlink the sidecar, even when the document is removed.
    static func withExclusiveLock<T>(for target: URL, _ operation: () throws -> T) throws -> T {
        try FileManager.default.createDirectory(
            at: target.deletingLastPathComponent(), withIntermediateDirectories: true)
        let lock = target.appendingPathExtension("lock")
        let fd = Darwin.open(lock.path, O_CREAT | O_RDWR | O_CLOEXEC | O_NOFOLLOW, mode_t(0o600))
        guard fd >= 0 else { throw NSError(domain: NSPOSIXErrorDomain, code: Int(errno)) }
        defer { _ = Darwin.close(fd) }

        while Darwin.flock(fd, LOCK_EX) != 0 {
            if errno == EINTR { continue }
            throw NSError(domain: NSPOSIXErrorDomain, code: Int(errno))
        }
        defer { _ = Darwin.flock(fd, LOCK_UN) }
        return try operation()
    }

    static func read(_ url: URL, limit: Int) throws -> Data {
        let attributes = try url.resourceValues(forKeys: [.isRegularFileKey, .fileSizeKey])
        guard attributes.isRegularFile == true else { throw SketchStoreError.invalid }
        if let size = attributes.fileSize, size > limit { throw SketchStoreError.oversized }
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }
        var data = Data()
        while let chunk = try handle.read(upToCount: min(64 * 1024, limit + 1 - data.count)), !chunk.isEmpty {
            data.append(chunk)
            if data.count > limit { throw SketchStoreError.oversized }
        }
        return data
    }

    // JSONSerialization collapses duplicate keys. Count key separators outside strings
    // and compare with the exact schema already checked by the caller.
    static func hasKeyCount(_ expected: Int, in data: Data) -> Bool {
        var quoted = false, escaped = false, keys = 0
        for byte in data {
            if escaped { escaped = false }
            else if quoted && byte == 92 { escaped = true }
            else if byte == 34 { quoted.toggle() }
            else if !quoted && byte == 58 { keys += 1 }
        }
        return keys == expected
    }
}

enum CADLocalDraftStore {
    static let limit = 2 * 1024 * 1024
    static var directory: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("MonoDiTreeCAD", isDirectory: true)
    }
    static var current: URL { directory.appendingPathComponent("current-draft.json") }

    static func isValid(_ document: DraftProject) -> Bool {
        guard !document.name.isEmpty, document.name.count <= 128,
              document.pieces.count <= 250 else { return false }
        var ids = Set<UUID>()
        var totalPoints = 0
        for piece in document.pieces {
            guard piece.id.uuidString != "00000000-0000-0000-0000-000000000000", ids.insert(piece.id).inserted,
                  !piece.name.isEmpty, piece.name.count <= 128,
                  piece.points.count >= (piece.closed ? 3 : 2),
                  piece.points.count <= 10_000 else { return false }
            totalPoints += piece.points.count
            guard totalPoints <= 20_000,
                  piece.points.allSatisfy({ $0.x.isFinite && $0.y.isFinite &&
                      abs($0.x) <= 100_000_000 && abs($0.y) <= 100_000_000 }) else { return false }
        }
        return true
    }


    static func read(_ url: URL) throws -> DraftProject {
        let data = try CADFileIO.read(url, limit: limit)
        guard var object = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw SketchStoreError.invalid
        }
        let legacy = Set(object.keys) == ["name", "pieces"]
        guard legacy || Set(object.keys) == ["id", "name", "pieces"] else { throw SketchStoreError.invalid }
        guard let pieces = object["pieces"] as? [[String: Any]] else { throw SketchStoreError.invalid }
        var keys = object.count
        for piece in pieces {
            guard Set(piece.keys) == ["id", "name", "closed", "points"],
                  let points = piece["points"] as? [[String: Any]],
                  points.allSatisfy({ Set($0.keys) == ["x", "y"] }) else { throw SketchStoreError.invalid }
            keys += piece.count + 2 * points.count
        }
        guard CADFileIO.hasKeyCount(keys, in: data) else { throw SketchStoreError.invalid }
        // The original singleton draft had no project UUID. Keep its identity stable
        // until its first successful atomic rewrite, without modifying it on read.
        if legacy { object["id"] = "D435B928-1DA6-40AC-9C5B-2AC11EF014D0" }
        let bytes = legacy ? try JSONSerialization.data(withJSONObject: object) : data
        let project = try JSONDecoder().decode(DraftProject.self, from: bytes)
        guard isValid(project) else { throw SketchStoreError.invalid }
        return project
    }

    static func restore() throws -> DraftProject? {
        guard FileManager.default.fileExists(atPath: current.path) else { return nil }
        return try read(current)
    }

    static func write(_ project: DraftProject, to url: URL) throws {
        guard isValid(project) else { throw SketchStoreError.invalid }
        let data = try JSONEncoder().encode(project)
        guard data.count <= limit else { throw SketchStoreError.oversized }
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try data.write(to: url, options: .atomic)
    }

    /// Rejects edits based on a stale in-memory draft even when its UUID is the
    /// same. A successful write must then become the next expected snapshot.
    /// This is optimistic stale-session protection, not a cross-process lock.
    static func save(_ project: DraftProject, replacing expected: DraftProject?, to url: URL) throws {
        try CADFileIO.withExclusiveLock(for: url) {
            let previous: DraftProject?
            if FileManager.default.fileExists(atPath: url.path) {
                previous = try read(url)
            } else {
                previous = nil
            }
            guard previous == expected else { throw SketchStoreError.changed }
            try write(project, to: url)
        }
    }

    static func save(_ project: DraftProject, replacing expected: DraftProject?) throws {
        try save(project, replacing: expected, to: current)
    }

    static func archiveBeforeSwitch() throws {
        try CADFileIO.withExclusiveLock(for: current) {
            guard var previous = try restore() else { return }
            let savedURL = CADProjectLibrary.url(previous.id)
            if FileManager.default.fileExists(atPath: savedURL.path) {
                let saved = try CADProjectLibrary.open(previous.id)
                if saved != previous {
                    previous.id = UUID()
                    previous.name = String(previous.name.prefix(120)) + " (taslak)"
                    try CADProjectLibrary.save(previous)
                }
            } else {
                try CADProjectLibrary.save(previous)
            }
            // Archive and remove under the same lock: no newer autosave can
            // appear between copying the current file and deleting it.
            try FileManager.default.removeItem(at: current)
        }
    }

    #if DEBUG && targetEnvironment(simulator)
    static func discard() { try? FileManager.default.removeItem(at: current) }
    #endif
}

enum CADProjectLibrary {
    struct Entry: Identifiable {
        let id: UUID
        let name: String
        let modified: Date
    }

    private static var folder: URL { CADLocalDraftStore.directory.appendingPathComponent("Projects", isDirectory: true) }
    static func url(_ id: UUID) -> URL { folder.appendingPathComponent(id.uuidString + ".json") }

    /// Never overwrite a different version of a saved project without the
    /// caller's verified snapshot. Repeated saves of identical content are safe.
    /// Like autosave, this is optimistic protection, not an OS file lock.
    static func save(_ project: DraftProject, replacing expected: DraftProject? = nil) throws {
        try save(project, replacing: expected, to: url(project.id))
    }

    // Path-injectable for deterministic tests; production always uses url(id).
    static func save(_ project: DraftProject, replacing expected: DraftProject?, to destination: URL) throws {
        try CADFileIO.withExclusiveLock(for: destination) {
            let previous: DraftProject?
            if FileManager.default.fileExists(atPath: destination.path) {
                let stored = try CADLocalDraftStore.read(destination)
                guard stored.id == project.id else { throw SketchStoreError.identity }
                previous = stored
            } else {
                previous = nil
            }
            guard previous == expected || previous == project else { throw SketchStoreError.changed }
            try CADLocalDraftStore.write(project, to: destination)
        }
    }

    static func open(_ id: UUID) throws -> DraftProject {
        let project = try CADLocalDraftStore.read(url(id))
        guard project.id == id else { throw SketchStoreError.identity }
        return project
    }

    static func recent() throws -> [Entry] {
        guard FileManager.default.fileExists(atPath: folder.path) else { return [] }
        let keys: Set<URLResourceKey> = [.contentModificationDateKey]
        let files = try FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: Array(keys))
        return try files.compactMap { file -> Entry? in
            guard file.pathExtension == "json", let id = UUID(uuidString: file.deletingPathExtension().lastPathComponent) else { return nil }
            let modified = try file.resourceValues(forKeys: keys).contentModificationDate ?? .distantPast
            let name = (try? open(id).name) ?? "Okunamayan proje"
            return Entry(id: id, name: name, modified: modified)
        }.sorted { $0.modified > $1.modified }
    }

    #if DEBUG && targetEnvironment(simulator)
    static func resetTestLibrary() { try? FileManager.default.removeItem(at: folder) }
    #endif
}
