import SwiftUI

#if os(iOS)
import UIKit

@MainActor
final class PatternPaperPrinter: NSObject, ObservableObject {
    weak var anchor: UIView?

    func present(_ url: URL, title: String, completion: @escaping (String) -> Void) {
        guard UIPrintInteractionController.isPrintingAvailable,
              UIPrintInteractionController.canPrint(url), let anchor, anchor.window != nil else {
            completion("Yazdırma ekranı açılamadı. A4 PDF dosyasını paylaşabilirsin.")
            return
        }
        let controller = UIPrintInteractionController.shared
        let info = UIPrintInfo(dictionary: nil)
        info.jobName = title
        info.outputType = .general
        info.orientation = .portrait
        info.duplex = .none
        controller.printInfo = info
        controller.printingItem = url
        controller.showsPaperSelectionForLoadedPapers = true
        let handler: UIPrintInteractionController.CompletionHandler = { _, completed, error in
            if let error {
                completion("Yazdırma işlemi tamamlanamadı. \(error.localizedDescription)")
            } else {
                completion(completed ? "Yazdırma işi sisteme iletildi." : "Yazdırma iptal edildi.")
            }
        }
        let presented: Bool
        if anchor.traitCollection.userInterfaceIdiom == .pad {
            let source = CGRect(x: anchor.bounds.midX, y: anchor.bounds.midY, width: 1, height: 1)
            presented = controller.present(from: source, in: anchor, animated: true, completionHandler: handler)
        } else {
            presented = controller.present(animated: true, completionHandler: handler)
        }
        if !presented { completion("Yazdırma ekranı açılamadı. A4 PDF dosyasını paylaşabilirsin.") }
    }
}

struct PatternPrintAnchor: UIViewRepresentable {
    let presenter: PatternPaperPrinter

    func makeUIView(context: Context) -> UIView {
        let view = UIView()
        view.isUserInteractionEnabled = false
        presenter.anchor = view
        return view
    }

    func updateUIView(_ view: UIView, context: Context) {
        presenter.anchor = view
    }
}
#elseif os(macOS)
import AppKit
import PDFKit

@MainActor
final class PatternPaperPrinter: ObservableObject {
    func present(_ url: URL, title: String, completion: @escaping (String) -> Void) {
        guard let document = PDFDocument(url: url),
              let info = NSPrintInfo.shared.copy() as? NSPrintInfo else {
            completion("A4 PDF yazdırma için açılamadı.")
            return
        }
        info.paperSize = NSSize(width: 210 * 72 / 25.4, height: 297 * 72 / 25.4)
        guard let operation = document.printOperation(for: info, scalingMode: .pageScaleNone, autoRotate: false) else {
            completion("Yazdırma ekranı açılamadı. A4 PDF dosyasını paylaşabilirsin.")
            return
        }
        operation.jobTitle = title
        completion(operation.run() ? "Yazdırma işi sisteme iletildi." : "Yazdırma tamamlanmadı.")
    }
}
#endif
