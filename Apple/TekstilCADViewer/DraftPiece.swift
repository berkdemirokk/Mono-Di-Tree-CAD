import Foundation

struct DraftPoint: Equatable, Codable {
    var x: Double
    var y: Double
}

struct DraftPiece: Identifiable, Equatable, Codable {
    var id = UUID()
    var name: String
    var closed: Bool
    var points: [DraftPoint]

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

    static func display(_ value: Double) -> String {
        value.formatted(.number.grouping(.never).precision(.fractionLength(0...3)))
    }

    // A label rounded to three decimals must never change the underlying pattern.
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
        // Scaling at the coordinate limit can round distinct vertices onto each other.
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
}
