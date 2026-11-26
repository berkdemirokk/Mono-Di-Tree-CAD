import CoreGraphics
import CoreText
import Foundation

enum PatternBriefPDF {
    enum ExportError: LocalizedError {
        case invalidDrawing, tooLarge, writeFailed

        var errorDescription: String? {
            switch self {
            case .invalidDrawing: return "PDF için geçerli çizim parçaları gerekiyor."
            case .tooLarge: return "Kalıp bu A4 çıktı sınırını aşıyor. Daha küçük bir çalışma seçin."
            case .writeFailed: return "PDF dosyası tamamlanamadı."
            }
        }
    }

    private static let ptPerMM = 72.0 / 25.4
    private static let tileWidth = 190.0
    private static let tileHeight = 267.0
    private static let marginX = 10.0
    private static let marginY = 15.0
    private static let padding = 2.0

    static func export(_ project: DraftProject) throws -> URL {
        var bounds = CGRect.null
        var pointCount = 0
        for piece in project.pieces {
            guard piece.points.count >= (piece.closed ? 3 : 2) else { throw ExportError.invalidDrawing }
            guard piece.points.count <= 100_000 - pointCount else { throw ExportError.tooLarge }
            pointCount += piece.points.count
            for point in piece.points {
                guard point.x.isFinite, point.y.isFinite else { throw ExportError.invalidDrawing }
                bounds = bounds.union(CGRect(x: point.x, y: point.y, width: 0, height: 0))
            }
        }
        guard !bounds.isNull else { throw ExportError.invalidDrawing }
        let across = ceil((bounds.width + 2 * padding) / tileWidth)
        let down = ceil((bounds.height + 2 * padding) / tileHeight)
        guard across >= 1, down >= 1, across * down <= 120 else { throw ExportError.tooLarge }
        let columns = Int(across), rows = Int(down)
        guard pointCount * rows * columns <= 2_000_000 else { throw ExportError.tooLarge }
        let target = FileManager.default.temporaryDirectory.appendingPathComponent("Kalip-A4-\(UUID().uuidString).pdf")
        var box = CGRect(x: 0, y: 0, width: 210 * ptPerMM, height: 297 * ptPerMM)
        guard let pdf = CGContext(target as CFURL, mediaBox: &box, nil) else { throw ExportError.writeFailed }

        for row in 0..<rows {
            for column in 0..<columns {
                pdf.beginPDFPage(nil)
                let tile = CGRect(x: marginX * ptPerMM, y: marginY * ptPerMM,
                                  width: tileWidth * ptPerMM, height: tileHeight * ptPerMM)
                pdf.saveGState()
                pdf.clip(to: tile)
                pdf.translateBy(x: tile.minX, y: tile.maxY)
                pdf.scaleBy(x: ptPerMM, y: -ptPerMM)
                pdf.setLineWidth(0.3)
                let originX = bounds.minX - padding + Double(column) * tileWidth
                let originY = bounds.minY - padding + Double(row) * tileHeight
                for piece in project.pieces {
                    for (index, point) in piece.points.enumerated() {
                        let local = CGPoint(x: point.x - originX, y: point.y - originY)
                        if index == 0 { pdf.move(to: local) } else { pdf.addLine(to: local) }
                    }
                    if piece.closed { pdf.closePath() }
                    pdf.strokePath()
                }
                pdf.restoreGState()
                pdf.saveGState()
                pdf.setLineWidth(0.4)
                pdf.setLineDash(phase: 0, lengths: [3, 3])
                pdf.stroke(tile)
                pdf.restoreGState()
                label("Satır \(row + 1)/\(rows) · Sütun \(column + 1)/\(columns) · %100 · Kesikli sınırları birleştirin",
                      at: CGPoint(x: tile.minX, y: tile.maxY + 8), in: pdf)
                let base = CGPoint(x: tile.minX, y: tile.minY - 14)
                let end = CGPoint(x: base.x + 50 * ptPerMM, y: base.y)
                pdf.setLineWidth(0.7)
                pdf.move(to: base)
                pdf.addLine(to: end)
                for x in [base.x, end.x] {
                    pdf.move(to: CGPoint(x: x, y: base.y - 3))
                    pdf.addLine(to: CGPoint(x: x, y: base.y + 3))
                }
                pdf.strokePath()
                label("50 mm kontrol", at: CGPoint(x: end.x + 6, y: base.y - 3), in: pdf)
                pdf.endPDFPage()
            }
        }
        pdf.closePDF()
        guard let reopened = CGPDFDocument(target as CFURL), reopened.numberOfPages == rows * columns else {
            try? FileManager.default.removeItem(at: target)
            throw ExportError.writeFailed
        }
        return target
    }

    private static func label(_ text: String, at point: CGPoint, in pdf: CGContext) {
        let font = CTFontCreateWithName("Helvetica" as CFString, 9, nil)
        let attributed = NSAttributedString(string: text, attributes: [
            NSAttributedString.Key(kCTFontAttributeName as String): font
        ])
        pdf.textPosition = point
        CTLineDraw(CTLineCreateWithAttributedString(attributed as CFAttributedString), pdf)
    }
}
