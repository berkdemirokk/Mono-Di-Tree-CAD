import Foundation

struct DraftPoint: Equatable, Codable {
    var x: Double
    var y: Double
}

struct DraftPiece: Identifiable, Equatable, Codable {
    var id = UUID()
    var name: String
    var closed: Bool
    /// Örneklenmiş ekran polilinesi. Geri dönüş-değişikliklerde kullanılan gerçek nokta kümesi.
    var points: [DraftPoint]
    /// Parametrik eğri kaynağı (çizgi, kubik Bézier, yay). Nil ise salı çizgi (polyline) vardır.
    var curves: [CurveSegment]?

    var minX: Double { points.lazy.map(\.x).min() ?? 0 }
    var minY: Double { points.lazy.map(\.y).min() ?? 0 }
    var maxX: Double { points.lazy.map(\.x).max() ?? 0 }
    var maxY: Double { points.lazy.map(\.y).max() ?? 0 }
    var lineLength: Double {
        guard !closed, points.count >= 2 else { return maxX - minX }
        return zip(points.dropFirst(), points).reduce(0.0) { length, pair in
            length + hypot(pair.0.x - pair.1.x, pair.0.y - pair.1.y)
        }
    }

    /// Parametrik eğrinin toplam uzunluğu mm cinsinden; yoksa polyline uzunluğu.
    var contourLength: Double {
        if let curves, !curves.isEmpty { return curves.reduce(0) { $0 + $1.length } }
        return lineLength
    }

    static func display(_ value: Double) -> String {
        value.formatted(.number.grouping(.never).precision(.fractionLength(0...3)))
    }

    /// Kullanıcı giriş metnini koordinat olarak çözer. Aynı değeri gösteriyorsa orijinali döndürür (yamuk kaydırmayı önler).
    private func measurement(_ text: String, original: Double) -> Double? {
        text == Self.display(original) ? original : DraftContourGeometry.coordinate(text)
    }

    func applyingDimensions(x: String, y: String, width: String, height: String) -> [DraftPoint]? {
        let left = minX, bottom = minY
        let oldWidth = lineLength, oldHeight = maxY - bottom
        guard let leftInput = measurement(x, original: left),
              let bottomInput = measurement(y, original: bottom),
              let widthInput = measurement(width, original: oldWidth),
              let heightInput = measurement(height, original: oldHeight) else { return nil }
        if leftInput == left && bottomInput == bottom && widthInput == oldWidth && heightInput == oldHeight {
            return points
        }
        guard oldWidth > 0, widthInput > 0,
              !closed || (oldHeight > 0 && heightInput > 0) else { return nil }
        let sx = widthInput / oldWidth
        let sy = closed ? heightInput / oldHeight : sx
        let updated = points.map { p in
            DraftPoint(x: sx == 1 ? p.x + (leftInput - left) : leftInput + (p.x - left) * sx,
                       y: sy == 1 ? p.y + (bottomInput - bottom) : bottomInput + (p.y - bottom) * sy)
        }
        guard updated.allSatisfy({ abs($0.x) <= DraftContourGeometry.coordinateLimit &&
                                   abs($0.y) <= DraftContourGeometry.coordinateLimit }) else { return nil }
        if updated.count <= DraftContourGeometry.pointLimit,
           DraftContourGeometry.validationError(points: updated, closed: closed) != nil { return nil }
        return updated
    }

    func rotatedQuarterTurn() -> [DraftPoint] {
        let cx = (minX + maxX) / 2, cy = (minY + maxY) / 2
        return points.map { DraftPoint(x: cx - ($0.y - cy), y: cy + ($0.x - cx)) }
    }

    /// Mevcut polyline'i parametrik bir tek çizgi segmentine dönüştürür.
    func asSingleLineCurve() -> CurveSegment? {
        guard points.count >= 2 else { return nil }
        return .line(start: points[0], end: points[1])
    }
}
