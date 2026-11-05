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
enum DesignRequestShareClose {
    enum Decision: Equatable { case cancelPreparation, reviewCleanup(UUID), proceed }
    static func decide(work: UUID?, cleanup: UUID?, acknowledged: UUID?) -> Decision {
        if work != nil { return .cancelPreparation }
        if let cleanup, cleanup != acknowledged { return .reviewCleanup(cleanup) }
        return .proceed
    }
    static func cancelClose(acknowledged: inout UUID?) { acknowledged = nil }
}
enum DesignRequestShareOutcome: Equatable {
    case completed, cancelled, failed
    static func classify(completed: Bool, error: Error?) -> Self {
        error != nil ? .failed : (completed ? .completed : .cancelled)
    }
    var message: String {
        switch self {
        case .completed: return "Paylaşım seçimi tamamlandı; Windows’a ulaştığını ayrıca kontrol edin."
        case .cancelled: return "Paylaşım iptal edildi; kayıtlı dosya ve taslak korundu."
        case .failed: return "Paylaşım tamamlanamadı; kayıtlı dosya ve taslak korundu."
        }
    }
}
/// Host is already in the sheet hierarchy before presenting the iPad popover.
final class DesignRequestShareHost: UIViewController {
    let item: DesignRequestShareItem
    let finished: (UUID, Bool, Error?) -> Void
    private var presented = false
    init(item: DesignRequestShareItem, finished: @escaping (UUID, Bool, Error?) -> Void) {
        self.item = item; self.finished = finished; super.init(nibName: nil, bundle: nil)
    }
    required init?(coder: NSCoder) { return nil }
    static func anchor(in bounds: CGRect) -> CGRect { CGRect(x: bounds.midX, y: bounds.midY, width: 1, height: 1) }
    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = .systemBackground
        let button = UIButton(type: .system)
        button.setTitle("Paylaşımı kapat", for: .normal)
        button.addTarget(self, action: #selector(cancelShare), for: .touchUpInside)
        button.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(button)
        NSLayoutConstraint.activate([button.centerXAnchor.constraint(equalTo: view.centerXAnchor),
                                     button.centerYAnchor.constraint(equalTo: view.centerYAnchor),
                                     button.heightAnchor.constraint(greaterThanOrEqualToConstant: 44)])
    }
    @objc private func cancelShare() { finished(item.id, false, nil) }
    override func viewDidAppear(_ animated: Bool) {
        super.viewDidAppear(animated)
        guard !presented else { return }; presented = true
        let controller = UIActivityViewController(activityItems: [item.url], applicationActivities: nil)
        if let popover = controller.popoverPresentationController {
            popover.sourceView = view
            popover.sourceRect = Self.anchor(in: view.bounds)
            popover.permittedArrowDirections = []
        }
        controller.completionWithItemsHandler = { [finished = self.finished, id = self.item.id] _, completed, _, error in finished(id, completed, error) }
        present(controller, animated: true)
    }
}
struct DesignRequestShareSheet: UIViewControllerRepresentable {
    let item: DesignRequestShareItem
    let finished: (UUID, Bool, Error?) -> Void
    func makeUIViewController(context: Context) -> DesignRequestShareHost { DesignRequestShareHost(item: item, finished: finished) }
    func updateUIViewController(_ controller: DesignRequestShareHost, context: Context) { }
}
#endif
