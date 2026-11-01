#if DEBUG
import Foundation

/// Owns the document revision used by this experiment. Every relevant document
/// change must go through replaceDocument; external state is outside its scope.
public final class DebugPointCopyCoordinator {
    private let lock = NSLock()
    private var documentID: UUID
    private var revision: UUID
    private var data: Data
    private var pendingEdit: UUID?

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
        pendingEdit = nil
        return self.revision
    }

    public struct EditRequest {
        fileprivate let id: UUID
        public let source: DebugPointCopyStore.Snapshot
    }

    /// Only one worker may be pending; a new request invalidates the previous one.
    public func beginEdit(name: String) -> EditRequest {
        lock.lock(); defer { lock.unlock() }
        let id = UUID()
        pendingEdit = id
        return .init(id: id, source: .init(documentID: documentID, revision: revision, name: name, data: data))
    }

    /// Returns true only when cancellation wins before application.
    @discardableResult
    public func cancelEdit(_ request: EditRequest) -> Bool {
        lock.lock(); defer { lock.unlock() }
        guard pendingEdit == request.id else { return false }
        pendingEdit = nil
        return true
    }

    /// Cancellation and application are ordered by the same lock. Once apply
    /// wins, a later cancel cannot undo the already installed result.
    @discardableResult
    public func applyEdit(_ request: EditRequest, data: Data) throws -> UUID {
        lock.lock(); defer { lock.unlock() }
        guard pendingEdit == request.id else { throw DebugPointCopyStore.Error.staleSnapshot }
        pendingEdit = nil
        guard data.count <= DebugPointCopyStore.maximumBytes else { throw DebugPointCopyStore.Error.tooLarge }
        let source = request.source
        guard source.documentID == documentID, source.revision == revision, source.data == self.data else {
            throw DebugPointCopyStore.Error.staleSnapshot
        }
        self.data = data
        revision = UUID()
        return revision
    }
    /// Returns the bytes and revision owned by this coordinator under one lock.
    public func snapshot(name: String) -> DebugPointCopyStore.Snapshot {
        lock.lock(); defer { lock.unlock() }
        return .init(documentID: documentID, revision: revision, name: name, data: data)
    }

    /// Synchronous, potentially blocking I/O. Call off the UI thread and do not
    /// reenter this coordinator from the test hook or the storage implementation.
    public func save(at database: URL, id: UUID = UUID(), snapshot: DebugPointCopyStore.Snapshot,
                     cancellation: DebugSaveCancellation? = nil,
                     beforeAuthorization: (() throws -> Void)? = nil) throws -> UUID {
        lock.lock(); defer { lock.unlock() }
        let documentID = self.documentID, revision = self.revision
        guard snapshot.documentID == documentID, snapshot.revision == revision, snapshot.data == data else {
            throw DebugPointCopyStore.Error.staleSnapshot
        }
        return try DebugPointCopyStore.saveAuthorized(at: database, id: id, snapshot: snapshot, cancellation: cancellation, isCurrent: { document, version in
            try beforeAuthorization?()
            return document == documentID && version == revision
        })
    }
}
#endif
