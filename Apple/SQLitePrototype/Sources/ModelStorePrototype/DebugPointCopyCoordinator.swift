#if DEBUG
import Foundation

/// Owns the document revision used by this experiment. Every relevant document
/// change must go through replaceDocument; external state is outside its scope.
public final class DebugPointCopyCoordinator {
    private let lock = NSLock()
    private var documentID: UUID
    private var revision: UUID
    private var data: Data

    public init(documentID: UUID, data: Data) throws {
        guard data.count <= DebugPointCopyStore.maximumBytes else { throw DebugPointCopyStore.Error.tooLarge }
        self.documentID = documentID
        self.revision = UUID()
        self.data = data
    }

    /// Each change gets a new token; callers cannot restore an old revision.
    @discardableResult
    public func replaceDocument(documentID: UUID, data: Data) throws -> UUID {
        guard data.count <= DebugPointCopyStore.maximumBytes else { throw DebugPointCopyStore.Error.tooLarge }
        lock.lock(); defer { lock.unlock() }
        self.documentID = documentID
        self.revision = UUID()
        self.data = data
        return self.revision
    }

    /// Returns the bytes and revision owned by this coordinator under one lock.
    public func snapshot(name: String) -> DebugPointCopyStore.Snapshot {
        lock.lock(); defer { lock.unlock() }
        return .init(documentID: documentID, revision: revision, name: name, data: data)
    }

    /// Synchronous, potentially blocking I/O. Call off the UI thread and do not
    /// reenter this coordinator from the test hook or the storage implementation.
    public func save(at database: URL, id: UUID = UUID(), snapshot: DebugPointCopyStore.Snapshot,
                     beforeAuthorization: (() throws -> Void)? = nil) throws -> UUID {
        lock.lock(); defer { lock.unlock() }
        let documentID = self.documentID, revision = self.revision
        guard snapshot.documentID == documentID, snapshot.revision == revision, snapshot.data == data else {
            throw DebugPointCopyStore.Error.staleSnapshot
        }
        return try DebugPointCopyStore.saveAuthorized(at: database, id: id, snapshot: snapshot) { document, version in
            try beforeAuthorization?()
            return document == documentID && version == revision
        }
    }
}
#endif
