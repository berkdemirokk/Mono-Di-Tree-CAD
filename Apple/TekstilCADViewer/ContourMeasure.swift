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

    // Straight-line distance only; does not follow the contour between points.
    static func distance(_ points: [Point], from first: Int, to second: Int) -> Double? {
        guard points.indices.contains(first), points.indices.contains(second) else { return nil }
        let a = points[first], b = points[second]
        guard a.x.isFinite, a.y.isFinite, b.x.isFinite, b.y.isFinite else { return nil }
        let value = hypot(b.x - a.x, b.y - a.y)
        return value.isFinite ? value : nil
    }
    // Polyline length between file vertices. On a closed contour forward follows
    // file order and backward follows the other direction; selecting the same
    // logical vertex yields zero in both directions, not a complete lap.
    static func pathLength(_ points: [Point], from first: Int, to second: Int,
                           closed: Bool, forward: Bool = true) -> Double? {
        guard points.indices.contains(first), points.indices.contains(second),
              hasMinimumContourPoints(points, closed: closed),
              points[first].x.isFinite, points[first].y.isFinite,
              points[second].x.isFinite, points[second].y.isFinite else { return nil }
        let repeated = closed && points.count > 1 && points[0].x == points[points.count - 1].x && points[0].y == points[points.count - 1].y
        let count = points.count - (repeated ? 1 : 0)
        var start = repeated && first == points.count - 1 ? 0 : first
        var end = repeated && second == points.count - 1 ? 0 : second
        if !closed && start > end { swap(&start, &end) }
        if closed && !forward { swap(&start, &end) }
        var total = 0.0
        var index = start
        while index != end {
            let next = closed ? (index + 1) % count : index + 1
            guard let segment = distance(points, from: index, to: next) else { return nil }
            total += segment
            guard total.isFinite else { return nil }
            index = next
        }
        return total
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
