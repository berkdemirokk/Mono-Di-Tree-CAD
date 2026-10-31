import Foundation

enum ContourMeasure {
    struct Point { let x: Double; let y: Double }

    // Measures only the polyline in the file. Invalid or overflowing values
    // are unavailable rather than presented as an authoritative measurement.
    static func length(_ points: [Point], closed: Bool) -> Double? {
        guard points.allSatisfy({ $0.x.isFinite && $0.y.isFinite }) else { return nil }
        guard points.count > 1 else { return 0 }
        var total = 0.0
        func distance(_ a: Point, _ b: Point) -> Double { hypot(b.x - a.x, b.y - a.y) }
        for index in 1..<points.count {
            total += distance(points[index - 1], points[index])
            guard total.isFinite else { return nil }
        }
        if closed {
            total += distance(points[points.count - 1], points[0])
            guard total.isFinite else { return nil }
        }
        return total
    }
}
