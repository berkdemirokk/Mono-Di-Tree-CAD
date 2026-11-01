import Foundation
import Darwin

/// Rejects a staging path that already exists before this save starts.
enum ModelFilePublisher {
    static func publish(_ data: Data, staging: URL, destination: URL,
                        beforePublish: (() throws -> Void)? = nil) throws {
        guard staging.path.withCString({ mkdir($0, 0o700) }) == 0 else {
            throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO)
        }
        // Pin the directory before writing: its pathname can later be replaced.
        let directory = staging.path.withCString { open($0, O_RDONLY | O_DIRECTORY | O_NOFOLLOW) }
        guard directory >= 0 else { throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO) }
        defer { close(directory) }
        let payload = openat(directory, "model.tcad", O_WRONLY | O_CREAT | O_EXCL | O_NOFOLLOW, 0o600)
        guard payload >= 0 else { throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO) }
        defer { close(payload) }
            try data.withUnsafeBytes { bytes in
                var offset = 0
                while offset < bytes.count {
                    let written = write(payload, bytes.baseAddress!.advanced(by: offset), bytes.count - offset)
                    if written < 0 {
                        if errno == EINTR { continue }
                        throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO)
                    }
                    guard written > 0 else { throw POSIXError(.EIO) }
                    offset += written
                }
            }
        try beforePublish?()
        // Reject a replacement made after the payload was opened. This check
        // does not make the following linkat atomic with the identity check.
        var openedPayload = stat(), namedPayload = stat()
        guard fstat(payload, &openedPayload) == 0,
              fstatat(directory, "model.tcad", &namedPayload, AT_SYMLINK_NOFOLLOW) == 0,
              openedPayload.st_dev == namedPayload.st_dev,
              openedPayload.st_ino == namedPayload.st_ino else {
            throw POSIXError(.ESTALE)
        }
        guard destination.path.withCString({ linkat(directory, "model.tcad", AT_FDCWD, $0, 0) }) == 0 else {
            throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO)
        }
        _ = unlinkat(directory, "model.tcad", 0)
        // Only remove an empty staging directory; no recursive cleanup.
        // Avoid deleting a replacement by retaining the directory if its path
        // no longer identifies the one opened above.
        var opened = stat(), current = stat()
        if fstat(directory, &opened) == 0,
           staging.path.withCString({ lstat($0, &current) }) == 0,
           opened.st_dev == current.st_dev, opened.st_ino == current.st_ino {
            _ = staging.path.withCString { rmdir($0) }
        }
    }
}
