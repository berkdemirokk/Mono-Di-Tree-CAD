import Foundation
import Darwin

/// Rejects a staging path that already exists before this save starts.
enum ModelFilePublisher {
    static func publish(_ data: Data, staging: URL, destination: URL,
                        beforePublish: (() throws -> Void)? = nil) throws {
        guard staging.path.withCString({ mkdir($0, 0o700) }) == 0 else {
            throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO)
        }
        // Never recursively remove a path another process could have replaced.
        // rmdir removes only an empty directory and does not follow symlinks.
        // On failure the payload is retained for inspection.
        defer { _ = staging.path.withCString { rmdir($0) } }
        let payload = staging.appendingPathComponent("model.tcad")
        try data.write(to: payload, options: .withoutOverwriting)
        try beforePublish?()
        try FileManager.default.moveItem(at: payload, to: destination)
    }
}
