import Foundation

/// Operations used by the bounded, interactive contour tools. Coordinates and distances are in mm.
enum DraftContourGeometry {
    static let pointLimit = 250
    static let coordinateLimit = 100_000_000.0
    private static let epsilon = 1e-8

    static func coordinate(_ text: String) -> Double? {
        let input = text.trimmingCharacters(in: .whitespacesAndNewlines)
            .replacingOccurrences(of: ",", with: ".")
        guard let value = Double(input), value.isFinite, abs(value) <= coordinateLimit else { return nil }
        return value
    }

    static func validationError(points: [DraftPoint], closed: Bool) -> String? {
        guard points.count >= (closed ? 3 : 2) else {
            return closed ? "Kapalı bir kalıp için en az 3 nokta gerekli." : "Açık çizgi için en az 2 nokta gerekli."
        }
        guard points.count <= pointLimit else { return "Bu araç en fazla 250 noktalı konturları düzenler." }
        guard points.allSatisfy({ abs($0.x) <= coordinateLimit && abs($0.y) <= coordinateLimit }) else {
            return "Nokta çalışma alanı sınırını aşıyor."
        }

        let edgeCount = closed ? points.count : points.count - 1
        for i in 0..<edgeCount {
            let a = points[i], b = points[(i + 1) % points.count]
            if hypot(b.x - a.x, b.y - a.y) <= epsilon {
                return "Aynı konumda iki ardışık nokta var."
            }
            for j in (i + 1)..<edgeCount {
                let c = points[j], d = points[(j + 1) % points.count]
                if j == i + 1 {
                    if overlapsAtVertex(a, b, d) { return "Komşu kenarlar üst üste biniyor." }
                } else if closed && i == 0 && j == edgeCount - 1 {
                    if overlapsAtVertex(b, a, c) { return "Komşu kenarlar üst üste biniyor." }
                } else if intersects(a, b, c, d) {
                    return "Kontur çizgileri birbirini kesiyor. Noktaları gözden geçirin."
                }
            }
        }
        if closed {
            // Translation before multiplication avoids cancellation for small shapes far from the origin.
            let origin = points[0]
            var twiceArea = 0.0
            for i in 1..<(points.count - 1) {
                twiceArea += cross(origin, points[i], points[i + 1])
            }
            if abs(twiceArea) <= 1e-6 { return "Kalıp kapalı bir alan oluşturmuyor." }
        }
        return nil
    }

    private static func cross(_ a: DraftPoint, _ b: DraftPoint, _ c: DraftPoint) -> Double {
        (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)
    }

    private static func orientation(_ a: DraftPoint, _ b: DraftPoint, _ c: DraftPoint) -> Int {
        let determinant = cross(a, b, c)
        let tolerance = epsilon * max(1, hypot(b.x - a.x, b.y - a.y))
        if determinant > tolerance { return 1 }
        if determinant < -tolerance { return -1 }
        return 0
    }

    private static func overlapsAtVertex(_ a: DraftPoint, _ joint: DraftPoint, _ b: DraftPoint) -> Bool {
        let dot = (a.x - joint.x) * (b.x - joint.x) + (a.y - joint.y) * (b.y - joint.y)
        guard dot > 0 else { return false }
        return cross(joint, a, b) == 0 || (dot > epsilon && orientation(a, joint, b) == 0)
    }

    private static func withinBounds(_ p: DraftPoint, _ a: DraftPoint, _ b: DraftPoint) -> Bool {
        p.x >= min(a.x, b.x) - epsilon && p.x <= max(a.x, b.x) + epsilon &&
        p.y >= min(a.y, b.y) - epsilon && p.y <= max(a.y, b.y) + epsilon
    }

    private static func intersects(_ a: DraftPoint, _ b: DraftPoint, _ c: DraftPoint, _ d: DraftPoint) -> Bool {
        let abc = orientation(a, b, c), abd = orientation(a, b, d)
        let cda = orientation(c, d, a), cdb = orientation(c, d, b)
        if abc * abd < 0 && cda * cdb < 0 { return true }
        return (abc == 0 && withinBounds(c, a, b)) || (abd == 0 && withinBounds(d, a, b))
            || (cda == 0 && withinBounds(a, c, d)) || (cdb == 0 && withinBounds(b, c, d))
    }
}

/// A centered aspect-fit map shared by drawing and picking. CAD Y increases upwards.
struct DraftCanvasMap {
    private let scale: Double
    private let left: Double
    private let bottom: Double
    private let minX: Double
    private let minY: Double

    init?(size: CGSize, minX: Double, maxX: Double, minY: Double, maxY: Double, inset: Double) {
        let width = Double(size.width), height = Double(size.height)
        guard width > 2 * inset, height > 2 * inset, maxX >= minX, maxY >= minY else { return nil }
        let spanX = maxX - minX, spanY = maxY - minY
        scale = min((width - 2 * inset) / max(spanX, 1), (height - 2 * inset) / max(spanY, 1))
        left = (width - spanX * scale) / 2
        bottom = (height + spanY * scale) / 2
        self.minX = minX
        self.minY = minY
    }

    func screen(_ p: DraftPoint) -> CGPoint {
        CGPoint(x: left + (p.x - minX) * scale, y: bottom - (p.y - minY) * scale)
    }

    func model(at p: CGPoint) -> DraftPoint {
        DraftPoint(x: minX + (Double(p.x) - left) / scale, y: minY + (bottom - Double(p.y)) / scale)
    }

    func sketchPoint(at location: CGPoint, extent: Double, snapToFive: Bool) -> DraftPoint? {
        let p = model(at: location)
        // Allow sub-nanometre round-off when tapping the border.
        let epsilon = 1e-8
        guard p.x >= -epsilon, p.x <= extent + epsilon,
              p.y >= -epsilon, p.y <= extent + epsilon else { return nil }
        let step = snapToFive ? 5.0 : 0.1
        func snap(_ value: Double) -> Double {
            min(extent, max(0, (value / step).rounded() * step))
        }
        return DraftPoint(x: snap(p.x), y: snap(p.y))
    }
    func nearestVertex(to location: CGPoint, points: [DraftPoint]) -> Int? {
        var best: Int?
        var distance = 22.0
        for (i, p) in points.enumerated() {
            let at = screen(p)
            let candidate = hypot(Double(at.x - location.x), Double(at.y - location.y))
            if candidate <= distance {
                distance = candidate
                best = i
            }
        }
        return best
    }
}
