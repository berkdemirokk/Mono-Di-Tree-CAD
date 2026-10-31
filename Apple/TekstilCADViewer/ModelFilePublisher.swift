import Foundation
import Darwin

/// Rejects a staging path that already exists before this save starts.
enum ModelFilePublisher {
    static func publish(_ data: Data, staging: URL, destination: URL) throws {
        guard staging.path.withCString({ mkdir($0, 0o700) }) == 0 else {
            throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO)
        }
        // Cleanup is path-based. If another actor replaces this directory after
        // mkdir, it could remove that actor's data; concurrent replacement has
        // not been tested or protected against here.
        defer { try? FileManager.default.removeItem(at: staging) }
        let payload = staging.appendingPathComponent("model.tcad")
        try data.write(to: payload, options: .withoutOverwriting)
        try FileManager.default.moveItem(at: payload, to: destination)
    }
}
