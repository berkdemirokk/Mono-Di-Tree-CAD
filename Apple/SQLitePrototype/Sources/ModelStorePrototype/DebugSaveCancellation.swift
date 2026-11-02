#if DEBUG
import Foundation

/// One save attempt only. Cancellation is ordered against SQLite COMMIT.
public final class DebugSaveCancellation {
    private let lock = NSLock()
    private var state = 0 // pending, cancelled, or commit attempted
    public init() {}
    @discardableResult public func cancel() -> Bool {
        lock.lock(); defer { lock.unlock() }
        guard state == 0 else { return false }
        state = 1
        return true
    }
    func commit(_ action: () throws -> Void) throws {
        lock.lock(); defer { lock.unlock() }
        guard state == 0 else { throw DebugPointCopyStore.Error.cancelled }
        state = 2
        try action()
    }
}
#endif
