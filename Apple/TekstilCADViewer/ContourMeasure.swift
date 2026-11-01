import Foundation

enum ContourMeasure {
    struct Point { let x: Double; let y: Double }

    // Closing a stored path or repeating another vertex does not create a
    // third distinct corner. Inputs from the viewer have finite coordinates.
    static func hasMinimumContourPoints(_ points: [Point], closed: Bool) -> Bool {
        if !closed { return points.count >= 2 }
        guard let first = points.first else { return false }
        var second: Point?
        for point in points.dropFirst() {
            if point.x == first.x && point.y == first.y { continue }
            guard let other = second else { second = point; continue }
            if point.x != other.x || point.y != other.y { return true }
        }
        return false
    }

    // A single point is handled by the separate minimum-point warning.
    static func isCollapsed(_ points: [Point]) -> Bool {
        guard points.count > 1, let first = points.first else { return false }
        return points.allSatisfy { $0.x == first.x && $0.y == first.y }
    }

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
