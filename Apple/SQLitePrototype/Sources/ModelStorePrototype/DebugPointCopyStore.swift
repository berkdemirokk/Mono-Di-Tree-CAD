#if DEBUG
import Foundation

/// Independent experiment; the app library and its file publisher do not use this.
public enum DebugPointCopyStore {
    public enum Error: Swift.Error { case tooLarge }
    public static let maximumBytes = 8 * 1024 * 1024

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
