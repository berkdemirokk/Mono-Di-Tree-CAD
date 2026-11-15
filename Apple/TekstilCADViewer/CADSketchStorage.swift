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
    case invalid, oversized, identity, changed, name, missing

    var errorDescription: String? {
        switch self {
        case .invalid: return "Dosyanın içeriği veya sürümü desteklenmiyor."
        case .oversized: return "Dosya izin verilen boyutu aşıyor."
        case .identity: return "Dosyanın proje kimliği kayıtla eşleşmiyor."
        case .changed: return "Kayıt başka bir çalışma tarafından değiştirildi. Çalışmanı kaybetmemek için «Farklı kopya kaydet»i kullan ya da projeyi yeniden aç."
        case .name: return "Proje adı 1 ile 120 karakter arasında olmalı."
        case .missing: return "Proje artık bulunamıyor."
        }
    }
}

enum CADFileIO {
    // POSIX fcntl record locks synchronize *processes*, but not independent
    // threads in one process. Hold a recursive in-process lock too: archive
    // may safely call library save while owning the current-draft lock.
    private static let processLock = NSRecursiveLock()

    /// Lock a permanent sidecar inode rather than the atomically replaced data
    /// file. Cooperating editors/processes serialize check-and-write this way.
    /// Do not unlink the sidecar, even when the document is removed.
    static func withExclusiveLock<T>(for target: URL, _ operation: () throws -> T) throws -> T {
        processLock.lock()
        defer { processLock.unlock() }

        try FileManager.default.createDirectory(
            at: target.deletingLastPathComponent(), withIntermediateDirectories: true)
        let lockURL = target.appendingPathExtension("lock")
        let fd = Darwin.open(lockURL.path, O_CREAT | O_RDWR | O_CLOEXEC | O_NOFOLLOW, mode_t(0o600))
        guard fd >= 0 else { throw NSError(domain: NSPOSIXErrorDomain, code: Int(errno)) }
        defer { _ = Darwin.close(fd) }

        var request = Darwin.flock()
        request.l_type = Int16(F_WRLCK)
        request.l_whence = Int16(SEEK_SET)
        request.l_start = 0
        request.l_len = 0  // To EOF, including future writes.
        while Darwin.fcntl(fd, F_SETLKW, &request) == -1 {
            if errno == EINTR { continue }
            throw NSError(domain: NSPOSIXErrorDomain, code: Int(errno))
        }
        defer {
            request.l_type = Int16(F_UNLCK)
            _ = Darwin.fcntl(fd, F_SETLK, &request)
        }
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
    /// Cooperating writers are serialized by the adjacent permanent lock file.
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

    /// True when the file is there but its content is not a draft this version can read.
    /// A failure to read the file at all (an I/O error on a possibly good draft) does not count:
    /// such a draft must not be offered for moving aside.
    static func hasUnreadableContent(_ url: URL) -> Bool {
        do {
            _ = try read(url)
            return false
        } catch is SketchStoreError {
            return true
        } catch is DecodingError {
            return true
        } catch {
            // 3840 is what JSONSerialization reports for bytes that are not JSON.
            let failure = error as NSError
            return failure.domain == NSCocoaErrorDomain && failure.code == 3840
        }
    }

    /// True when a current draft file exists but cannot be read as a draft.
    static func hasUnreadableDraft() -> Bool {
        guard FileManager.default.fileExists(atPath: current.path) else { return false }
        return hasUnreadableContent(current)
    }

    /// Moves an unreadable current draft aside so that a new drawing can start.
    /// The bytes are kept, never deleted, and a draft that can still be read stays
    /// where it is. Returns the new location, or nil when nothing was moved.
    @discardableResult
    static func setAsideUnreadableDraft() throws -> URL? {
        try setAsideUnreadableDraft(
            at: current, into: directory.appendingPathComponent("Unreadable", isDirectory: true))
    }

    // Path-injectable for deterministic tests; production always uses current.
    static func setAsideUnreadableDraft(at url: URL, into folder: URL) throws -> URL? {
        return try CADFileIO.withExclusiveLock(for: url) { () throws -> URL? in
            guard FileManager.default.fileExists(atPath: url.path) else { return nil }
            guard hasUnreadableContent(url) else { return nil }
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            let destination = folder.appendingPathComponent("draft-\(UUID().uuidString).json")
            try FileManager.default.moveItem(at: url, to: destination)
            return destination
        }
    }

    #if DEBUG && targetEnvironment(simulator)
    static func discard() { try? FileManager.default.removeItem(at: current) }
    /// UI-test hook: leaves a current draft that no reader accepts.
    static func writeUnreadableDraftForTesting() {
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try? Data("{\"version\":99}".utf8).write(to: current, options: .atomic)
    }
    #endif
}

enum CADProjectLibrary {
    struct Entry: Identifiable {
        let id: UUID
        let name: String
        let modified: Date
        /// False when the file exists but cannot be read as a project. Such an
        /// entry can still be removed, but not renamed or opened.
        var readable = true
    }

    static let nameLimit = 120

    private static var folder: URL { CADLocalDraftStore.directory.appendingPathComponent("Projects", isDirectory: true) }
    static func url(_ id: UUID) -> URL { folder.appendingPathComponent(id.uuidString + ".json") }

    /// Never overwrite a different version of a saved project without the
    /// caller's verified snapshot. Repeated saves of identical content are safe.
    /// Cooperative processes serialize the compare-and-write through a lock sidecar.
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
            // Repeating an identical library save must not rewrite bytes:
            // JSONEncoder object key order is not a stable file identity.
            if previous == project { return }
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
        return files.compactMap { file -> Entry? in
            guard file.pathExtension == "json", let id = UUID(uuidString: file.deletingPathExtension().lastPathComponent) else { return nil }
            // A project deleted while the list is being read is skipped, not an error for the whole list.
            guard let values = try? file.resourceValues(forKeys: keys) else { return nil }
            let modified = values.contentModificationDate ?? .distantPast
            let stored = try? open(id)
            return Entry(id: id, name: stored?.name ?? "Okunamayan proje", modified: modified,
                         readable: stored != nil)
        }.sorted { $0.modified > $1.modified }
    }

    /// Trimmed project name, or nil when it is empty or longer than `nameLimit`.
    static func validatedName(_ proposed: String) -> String? {
        let name = proposed.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty, name.count <= nameLimit else { return nil }
        return name
    }

    static func rename(_ id: UUID, to proposed: String) throws {
        try CADFileIO.withExclusiveLock(for: CADLocalDraftStore.current) { () throws -> Void in
            let file = url(id)
            let before = try? CADLocalDraftStore.read(file)
            try rename(id, to: proposed, at: file)
            // An untouched autosave of this project would otherwise differ from the renamed saved
            // copy and be archived as a second "(taslak)" project. A draft with edits is left alone.
            guard let before = before, let name = validatedName(proposed),
                  var draft = try? CADLocalDraftStore.read(CADLocalDraftStore.current),
                  draft == before else { return }
            draft.name = name
            try CADLocalDraftStore.write(draft, to: CADLocalDraftStore.current)
        }
    }

    /// Changes only the name of a readable project. The compare-and-write runs under the
    /// project's lock sidecar, on the bytes read inside the lock, so a concurrent editor
    /// save cannot be overwritten with an older copy. Path-injectable for tests.
    static func rename(_ id: UUID, to proposed: String, at file: URL) throws {
        guard let name = validatedName(proposed) else { throw SketchStoreError.name }
        try CADFileIO.withExclusiveLock(for: file) {
            guard FileManager.default.fileExists(atPath: file.path) else { throw SketchStoreError.missing }
            var stored = try CADLocalDraftStore.read(file)
            guard stored.id == id else { throw SketchStoreError.identity }
            if stored.name == name { return }
            stored.name = name
            try CADLocalDraftStore.write(stored, to: file)
        }
    }

    private static func itemType(_ file: URL) -> FileAttributeType? {
        guard let attributes = try? FileManager.default.attributesOfItem(atPath: file.path) else { return nil }
        return attributes[.type] as? FileAttributeType
    }

    /// Removes one saved project file, readable or not. A project that is already gone
    /// is not an error. Other projects and the current draft are never touched, and the
    /// lock sidecar stays in place (see `CADFileIO.withExclusiveLock`).
    static func delete(_ id: UUID) throws {
        // The autosaved draft of this project would otherwise be archived back into the library by
        // the next `archiveBeforeSwitch`, silently undoing the delete. Same lock order as there.
        try CADFileIO.withExclusiveLock(for: CADLocalDraftStore.current) { () throws -> Void in
            try delete(at: url(id))
            if let draft = try? CADLocalDraftStore.read(CADLocalDraftStore.current), draft.id == id {
                try FileManager.default.removeItem(at: CADLocalDraftStore.current)
            }
        }
    }

    // Path-injectable for deterministic tests; production always uses url(id).
    static func delete(at file: URL) throws {
        guard file.pathExtension == "json" else { throw SketchStoreError.invalid }
        // Do not create the folder or a lock sidecar for a project that does not exist.
        guard itemType(file) != nil else { return }
        try CADFileIO.withExclusiveLock(for: file) {
            guard let type = itemType(file) else { return }
            guard type != .typeDirectory else { throw SketchStoreError.invalid }
            try FileManager.default.removeItem(at: file)
        }
    }

    #if DEBUG && targetEnvironment(simulator)
    static func resetTestLibrary() { try? FileManager.default.removeItem(at: folder) }
    #endif
}
