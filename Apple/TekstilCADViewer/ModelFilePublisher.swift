import Foundation
import Darwin

/// Rejects a staging path that already exists before this save starts.
enum ModelFilePublisher {
    private static func identity(_ url: URL) throws -> stat {
        var info = stat()
        guard url.path.withCString({ lstat($0, &info) }) == 0 else {
            throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO)
        }
        return info
    }

    private static func sameFile(_ a: stat, _ b: stat) -> Bool {
        a.st_dev == b.st_dev && a.st_ino == b.st_ino && a.st_mode == b.st_mode
    }

    static func publish(_ data: Data, staging: URL, destination: URL,
                        beforePayloadCreation: (() throws -> Void)? = nil,
                        beforePublish: (() throws -> Void)? = nil) throws {
        guard staging.path.withCString({ mkdir($0, 0o700) }) == 0 else {
            throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO)
        }
        // Even rmdir could remove someone else's empty replacement directory.
        // Keep the directory on both success and failure.
        let directoryIdentity = try identity(staging)
        let directoryFD = staging.path.withCString { open($0, O_RDONLY | O_DIRECTORY | O_NOFOLLOW) }
        guard directoryFD >= 0 else {
            throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO)
        }
        defer { close(directoryFD) }
        var openedDirectory = stat()
        guard fstat(directoryFD, &openedDirectory) == 0,
              sameFile(directoryIdentity, openedDirectory) else {
            throw CocoaError(.fileWriteUnknown)
        }
        try beforePayloadCreation?()
        // Create only inside the already opened directory, never through its
        // potentially replaced pathname. O_EXCL also rejects existing names.
        let payloadFD = "model.tcad".withCString {
            openat(directoryFD, $0, O_WRONLY | O_CREAT | O_EXCL | O_NOFOLLOW, 0o600)
        }
        guard payloadFD >= 0 else {
            throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO)
        }
        let output = FileHandle(fileDescriptor: payloadFD, closeOnDealloc: true)
        try output.write(contentsOf: data)
        try output.close()
        let payload = staging.appendingPathComponent("model.tcad")
        let payloadIdentity = try identity(payload)
        try beforePublish?()
        guard sameFile(directoryIdentity, try identity(staging)),
              sameFile(payloadIdentity, try identity(payload)) else {
            throw CocoaError(.fileWriteUnknown)
        }
        // Check in bounded chunks so in-place edits before publication fail closed.
        let handle = try FileHandle(forReadingFrom: payload)
        defer { try? handle.close() }
        var opened = stat()
        guard fstat(handle.fileDescriptor, &opened) == 0,
              sameFile(payloadIdentity, opened) else {
            throw CocoaError(.fileWriteUnknown)
        }
        var offset = 0
        while offset < data.count {
            let length = min(64 * 1024, data.count - offset)
            guard let chunk = try handle.read(upToCount: length),
                  chunk == data.subdata(in: offset..<(offset + length)) else {
                throw CocoaError(.fileWriteUnknown)
            }
            offset += length
        }
        guard (try handle.read(upToCount: 1) ?? Data()).isEmpty,
              sameFile(directoryIdentity, try identity(staging)),
              sameFile(payloadIdentity, try identity(payload)) else {
            throw CocoaError(.fileWriteUnknown)
        }
        // These checks are not atomic with moveItem; a concurrent replacement
        // after the final check remains possible.
        try FileManager.default.moveItem(at: payload, to: destination)
    }
}
