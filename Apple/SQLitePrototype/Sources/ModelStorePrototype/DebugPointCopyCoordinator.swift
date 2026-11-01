#if DEBUG
import Foundation

/// Owns the document revision used by this experiment. Every relevant document
/// change must go through replaceDocument; external state is outside its scope.
public final class DebugPointCopyCoordinator {
    private let lock = NSLock()
    private var documentID: UUID
    private var revision: UUID

    public init(documentID: UUID) {
        self.documentID = documentID
        self.revision = UUID()
    }

    /// Each change gets a new token; callers cannot restore an old revision.
    @discardableResult
    public func replaceDocument(documentID: UUID) -> UUID {
        lock.lock(); defer { lock.unlock() }
        self.documentID = documentID
        self.revision = UUID()
        return self.revision
    }

    /// The bytes must have been prepared for the state the caller serialized
    /// with this coordinator; external document state is not inspected.
    public func snapshot(name: String, data: Data) -> DebugPointCopyStore.Snapshot {
        lock.lock(); defer { lock.unlock() }
        return .init(documentID: documentID, revision: revision, name: name, data: data)
    }

    /// Synchronous, potentially blocking I/O. Call off the UI thread and do not
    /// reenter this coordinator from the test hook or the storage implementation.
    public func save(at database: URL, id: UUID = UUID(), snapshot: DebugPointCopyStore.Snapshot,
                     beforeAuthorization: (() throws -> Void)? = nil) throws -> UUID {
        lock.lock(); defer { lock.unlock() }
        let documentID = self.documentID, revision = self.revision
        return try DebugPointCopyStore.saveAuthorized(at: database, id: id, snapshot: snapshot) { document, version in
            try beforeAuthorization?()
            return document == documentID && version == revision
        }
    }
}
#endif
