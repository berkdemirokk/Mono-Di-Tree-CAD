#if canImport(UIKit)
import UIKit

enum MarkerPDFExporter {
    static let pointsPerMM = 72.0 / 25.4
    static let tileWidth = 190.0
    static let tileHeight = 257.0
    static let calibrationMM = 50.0
    struct PagePlan {
        let columns: Int
        let rows: Int
        var count: Int { columns * rows }
        var widthCovered: Double { Double(columns) * MarkerPDFExporter.tileWidth }
        var heightCovered: Double { Double(rows) * MarkerPDFExporter.tileHeight }
    }
    static func pagePlan(_ layout: MarkerLayout) throws -> PagePlan {
        guard layout.width.isFinite, layout.length.isFinite, layout.width > 0, layout.length > 0, layout.width <= 10_000, layout.length <= 10_000, !layout.placements.isEmpty else {
            throw MarkerFailure(tr: "PDF yerleşim sınırları geçersiz.", en: "Invalid PDF layout bounds.")
        }
        let plan = PagePlan(columns: Int(ceil(layout.width / tileWidth)), rows: Int(ceil(layout.length / tileHeight)))
        let work = layout.placements.reduce(0) { $0 + $1.piece.points.count + 4 * $1.piece.notches.count + ($1.piece.grain == nil ? 0 : 5) + 4 }
        guard plan.count <= 100, plan.count * work <= 2_000_000 else {
            throw MarkerFailure(tr: "PDF 100 sayfa veya çizim sınırını aşıyor; işi küçültün.", en: "PDF exceeds 100 pages or its drawing budget; reduce the job.")
        }
        return plan
    }
    /// Produces complete in-memory bytes only. No printers, plotters, or network destinations.
    static func data(layout: MarkerLayout, english: Bool, cancelled: () -> Bool = { false }) throws -> Data {
        let plan = try pagePlan(layout)
        let deadline = ProcessInfo.processInfo.systemUptime + 30
        func check() throws {
            if cancelled() { throw MarkerFailure(tr: "PDF iptal edildi.", en: "PDF cancelled.") }
            if ProcessInfo.processInfo.systemUptime >= deadline { throw MarkerFailure(tr: "PDF süre sınırı doldu.", en: "PDF time limit reached.") }
        }
        try check()
        let scale = pointsPerMM
        let format = UIGraphicsPDFRendererFormat()
        let identities = layout.placements.map { p in
            "\(p.piece.id!.uuidString) | \(p.piece.name ?? "") | \(p.piece.size ?? "") | \(p.piece.material ?? "") | cut \(p.piece.quantity ?? 0) | copy \(p.copy)"
        }.joined(separator: "\n")
        format.documentInfo = [kCGPDFContextTitle as String: layout.sourceName,
                               kCGPDFContextCreator as String: "TekstilCAD iPhone — conservative shelf layout",
                               kCGPDFContextSubject as String: "Grainline unchecked; no rotation; inspection only.\n" + identities]
        let renderer = UIGraphicsPDFRenderer(bounds: CGRect(x: 0, y: 0, width: 210*scale, height: 297*scale), format: format)
        var failure: Error?
        let bytes = renderer.pdfData { rendererContext in
            do {
                for row in 0..<plan.rows {
                    for column in 0..<plan.columns {
                        try check()
                        rendererContext.beginPage()
                        let c = rendererContext.cgContext
                        func label(_ text: String, _ x: Double, _ y: Double, _ width: Double, _ font: CGFloat = 7) {
                            (text as NSString).draw(in: CGRect(x: x*scale, y: y*scale, width: width*scale, height: 5*scale), withAttributes: [.font: UIFont.systemFont(ofSize: font), .foregroundColor: UIColor.black])
                        }
                        label(english ? "ROW \(row+1)/\(plan.rows)  COLUMN \(column+1)/\(plan.columns)  PAGE \(row*plan.columns+column+1)/\(plan.count) — PRINT 100%" : "SATIR \(row+1)/\(plan.rows)  SÜTUN \(column+1)/\(plan.columns)  SAYFA \(row*plan.columns+column+1)/\(plan.count) — %100 YAZDIR", 10, 7, 190, 8)
                        label(english ? "Fabric \(layout.width) mm × \(layout.length) mm. Dotted edge is not a cut line." : "Kumaş \(layout.width) mm × \(layout.length) mm. Noktalı sınır kesim çizgisi değildir.", 10, 12, 190)
                        label(english ? "Shelf layout / no rotation / grainline unchecked / inspection only" : "Raf yerleşimi / döndürme yok / iplik yönü kontrol edilmedi / inceleme amaçlı", 10, 16, 190, 6)
                        let frame = CGRect(x: 10*scale, y: 20*scale, width: tileWidth*scale, height: tileHeight*scale)
                        c.setStrokeColor(UIColor.gray.cgColor); c.setLineWidth(0.3); c.stroke(frame)
                        c.setStrokeColor(UIColor.black.cgColor); c.setLineWidth(0.5)
                        c.move(to: CGPoint(x: 10*scale, y: 284*scale)); c.addLine(to: CGPoint(x: (10+calibrationMM)*scale, y: 284*scale)); c.strokePath()
                        label(english ? "50 mm CHECK" : "50 mm KONTROL", 63, 282, 45, 8)
                        label(english ? "Dashed arrow: grain. Cross: visual mark, not cut." : "Kesik ok: iplik. Artı: görsel işaret, kesim değil.", 10, 288, 190)
                        let review = layout.placements.filter { $0.piece.grain?.needsReview == true }.count
                        label(english ? "Grain review pending: \(review). Join grey frames; disable fit-to-page." : "İplik kontrolü bekleyen: \(review). Gri çerçeveleri birleştirin; sayfaya sığdırmayı kapatın.", 10, 292, 190, 6)
                        c.saveGState(); c.clip(to: frame)
                        defer { c.restoreGState() }
                        func map(_ x: Double, _ y: Double) -> CGPoint {
                            CGPoint(x: (10+x-Double(column)*tileWidth)*scale, y: (20+y-Double(row)*tileHeight)*scale)
                        }
                        c.setStrokeColor(UIColor.gray.cgColor); c.setLineDash(phase: 0, lengths: [1, 2]); c.setLineWidth(0.3)
                        c.stroke(CGRect(origin: map(0, 0), size: CGSize(width: layout.width*scale, height: layout.length*scale)))
                        c.setLineDash(phase: 0, lengths: []); c.setStrokeColor(UIColor.black.cgColor); c.setLineWidth(0.5)
                        for p in layout.placements {
                            try check()
                            let piece = p.piece
                            c.beginPath()
                            for (index, point) in piece.points.enumerated() {
                                let mapped = map(point.x+p.dx, point.y+p.dy)
                                if index == 0 { c.move(to: mapped) } else { c.addLine(to: mapped) }
                            }
                            c.closePath(); c.strokePath()
                            for index in piece.notches {
                                let point = piece.points[index]
                                let px = point.x+p.dx, py = point.y+p.dy
                                c.move(to: map(px-0.75, py)); c.addLine(to: map(px+0.75, py))
                                c.move(to: map(px, py-0.75)); c.addLine(to: map(px, py+0.75)); c.strokePath()
                            }
                            if let grain = piece.grain {
                                let dx = grain.end.x-grain.start.x, dy = grain.end.y-grain.start.y, length = hypot(dx, dy)
                                let arrow = min(2.0, length/3), ux = dx/length, uy = dy/length
                                c.setLineDash(phase: 0, lengths: [4, 2])
                                c.move(to: map(grain.start.x+p.dx, grain.start.y+p.dy)); c.addLine(to: map(grain.end.x+p.dx, grain.end.y+p.dy)); c.strokePath()
                                c.setLineDash(phase: 0, lengths: [])
                                c.move(to: map(grain.end.x+p.dx-arrow*ux-0.35*arrow*uy, grain.end.y+p.dy-arrow*uy+0.35*arrow*ux))
                                c.addLine(to: map(grain.end.x+p.dx, grain.end.y+p.dy))
                                c.addLine(to: map(grain.end.x+p.dx-arrow*ux+0.35*arrow*uy, grain.end.y+p.dy-arrow*uy-0.35*arrow*ux)); c.strokePath()
                            }
                            let origin = map(p.bounds.minX+2, p.bounds.minY+2)
                            let name = "\(piece.name ?? "") · \(piece.size ?? "") · \(piece.material ?? "") · #\(p.copy)"
                            (name as NSString).draw(at: origin, withAttributes: [.font: UIFont.systemFont(ofSize: 5), .foregroundColor: UIColor.darkGray])
                            (piece.id!.uuidString as NSString).draw(at: CGPoint(x: origin.x, y: origin.y+6), withAttributes: [.font: UIFont.systemFont(ofSize: 4), .foregroundColor: UIColor.darkGray])
                        }
                    }
                }
            } catch { failure = error }
        }
        if let failure { throw failure }
        try check()
        guard bytes.count <= 50_000_000 else { throw MarkerFailure(tr: "PDF dosyası çok büyük.", en: "PDF file is too large.") }
        return bytes
    }
}
#endif
