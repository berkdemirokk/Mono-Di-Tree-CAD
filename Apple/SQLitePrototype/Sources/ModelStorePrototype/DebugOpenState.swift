#if DEBUG
import Foundation

/// Independent first step towards a single document owner. The app does not use it.
public final class DebugOpenState {
    public struct Document {
        public let id: UUID
        public let revision: UUID
        public let data: Data
    }
    public struct LoadRequest: Equatable {
        fileprivate let id: UUID
    }
    public enum Completion { case opened(Document), stale }
    public enum Error: Swift.Error { case tooLarge }

    private let lock = NSLock()
    private var active: LoadRequest?
    private var current: Document?

    public init() {}

    /// Starting another load invalidates the prior request, but keeps the open document.
    public func begin() -> LoadRequest {
        lock.lock(); defer { lock.unlock() }
        let request = LoadRequest(id: UUID())
        active = request
        return request
    }

    /// A stale cancellation cannot cancel a later request.
    @discardableResult public func cancel(_ request: LoadRequest) -> Bool {
        lock.lock(); defer { lock.unlock() }
        guard active == request else { return false }
        active = nil
        return true
    }

    /// A rejected or stale load never replaces the current document. The caller
    /// must validate model content before invoking this method.
    public func finish(_ request: LoadRequest, documentID: UUID, data: Data) throws -> Completion {
        lock.lock(); defer { lock.unlock() }
        guard active == request else { return .stale }
        active = nil
        guard data.count <= DebugPointCopyStore.maximumBytes else { throw Error.tooLarge }
        let document = Document(id: documentID, revision: UUID(), data: data)
        current = document
        return .opened(document)
    }

    public func snapshot() -> Document? {
        lock.lock(); defer { lock.unlock() }
        return current
    }
}
#endif
