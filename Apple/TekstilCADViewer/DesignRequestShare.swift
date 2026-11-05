import Foundation
#if os(iOS)
import SwiftUI
import UIKit

/// A private, bounded hand-off file. It is never the user's saved original.
enum DesignRequestShareFile {
    enum Failure: Error { case cleanupFailed(URL) }
    static func create(bytes: Data, id: UUID, base: URL = FileManager.default.temporaryDirectory) throws -> URL {
        guard !bytes.isEmpty, bytes.count <= 12 * 1024 * 1024 else { throw CocoaError(.fileWriteOutOfSpace) }
        try Task.checkCancellation()
        let folder = base.appendingPathComponent("TekstilCAD-share-" + id.uuidString, isDirectory: true)
        guard !FileManager.default.fileExists(atPath: folder.path) else { throw CocoaError(.fileWriteFileExists) }
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: false, attributes: [.protectionKey: FileProtectionType.complete])
        do {
            let file = folder.appendingPathComponent("elbise-istegi.tcad-request.json")
            try bytes.write(to: file, options: [.atomic, .completeFileProtection])
            try Task.checkCancellation()
            return file
        } catch {
            let original = error
            do { try FileManager.default.removeItem(at: folder) }
            catch { throw Failure.cleanupFailed(folder.appendingPathComponent("elbise-istegi.tcad-request.json")) }
            throw original
        }
    }
    static func remove(_ file: URL, id: UUID, base: URL = FileManager.default.temporaryDirectory) throws {
        let folder = base.appendingPathComponent("TekstilCAD-share-" + id.uuidString, isDirectory: true)
        guard file.standardizedFileURL == folder.appendingPathComponent("elbise-istegi.tcad-request.json").standardizedFileURL else { throw CocoaError(.fileNoSuchFile) }
        if FileManager.default.fileExists(atPath: folder.path) { try FileManager.default.removeItem(at: folder) }
    }
}
struct DesignRequestShareItem: Identifiable { let id: UUID; let url: URL }
struct DesignRequestShareSheet: UIViewControllerRepresentable {
    let item: DesignRequestShareItem
    let finished: (UUID, Bool, Error?) -> Void
    func makeUIViewController(context: Context) -> UIActivityViewController {
        let controller = UIActivityViewController(activityItems: [item.url], applicationActivities: nil)
        controller.completionWithItemsHandler = { _, completed, _, error in finished(item.id, completed, error) }
        return controller
    }
    func updateUIViewController(_ controller: UIActivityViewController, context: Context) { }
}
#endif
