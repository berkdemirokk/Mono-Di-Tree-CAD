#if DEBUG
import Foundation

/// Independent experiment; the app library and its file publisher do not use this.
public enum DebugPointCopyStore {
    public enum Error: Swift.Error { case tooLarge, staleSnapshot }
    public static let maximumBytes = 8 * 1024 * 1024

    public struct Snapshot {
        public let documentID: UUID
        public let revision: UUID
        public let name: String
        public let data: Data
        public init(documentID: UUID, revision: UUID, name: String, data: Data) {
            self.documentID = documentID; self.revision = revision
            self.name = name; self.data = data
        }
    }

    /// The caller must hold its document-state serialization gate throughout
    /// this synchronous call, including COMMIT. No UI or actor is accessed here.
    /// The authorization callback runs after insertion but before commit, so a
    /// stale or cancelled snapshot rolls back rather than becoming visible.
    public static func saveAuthorized(at database: URL, id: UUID = UUID(), snapshot: Snapshot,
                                      isCurrent: @escaping (UUID, UUID) throws -> Bool) throws -> UUID {
        guard snapshot.data.count <= maximumBytes else { throw Error.tooLarge }
        try ModelStorePrototype.save(at: database, id: id.uuidString, name: snapshot.name, data: snapshot.data) {
            guard try isCurrent(snapshot.documentID, snapshot.revision) else { throw Error.staleSnapshot }
        }
        return id
    }
    /// `data` is an already validated snapshot. The caller must serialize document
    /// state changes with this operation; this API does not validate freshness.
    public static func save(at database: URL, id: UUID = UUID(), name: String, data: Data,
                            afterInsert: (() throws -> Void)? = nil) throws -> UUID {
        guard data.count <= maximumBytes else { throw Error.tooLarge }
        try ModelStorePrototype.save(at: database, id: id.uuidString, name: name, data: data,
                                     afterInsert: afterInsert)
        return id
    }
}
#endif
