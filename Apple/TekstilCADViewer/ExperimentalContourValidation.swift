import Foundation

// Experimental bounded port of the desktop point-edit geometry predicates.
// Paths above 4096 stored points are deliberately rejected; the desktop's
// large-path validators have not been ported. This is not production approval.
enum ExperimentalContourValidation {
    struct Point: Hashable { let x: Double; let y: Double }
    enum Failure: Error { case invalid }
    static let epsilon = 1e-8
    static let maximumPoints = 4096
    static let maximumCoordinate = 100_000_000.0

    static func validate(_ points: [Point], closed: Bool) throws {
        guard points.count <= maximumPoints, points.count >= (closed ? 3 : 2),
              points.allSatisfy({ $0.x.isFinite && $0.y.isFinite && abs($0.x) <= maximumCoordinate && abs($0.y) <= maximumCoordinate }) else { throw Failure.invalid }
        var count = points.count
        if closed && count > 1 && points[0] == points[count - 1] { count -= 1 }
        if closed {
            guard count >= 3, Set(points.prefix(count)).count == count else { throw Failure.invalid }
            let origin = points[0]
            var sum = 0.0
            for i in 1..<(points.count - 1) { sum += cross(sub(points[i], origin), sub(points[i + 1], origin)) }
            guard sum.isFinite && abs(sum / 2) > epsilon else { throw Failure.invalid }
        }
        let edges = closed ? count : count - 1
        for i in 0..<edges {
            let a = points[i], b = points[(i + 1) % count]
            guard distance(a, b) > epsilon else { throw Failure.invalid }
            for j in (i + 1)..<edges {
                let c = points[j], d = points[(j + 1) % count]
                if j == i + 1 || (closed && i == 0 && j == edges - 1) {
                    let common = j == i + 1 ? b : a
                    let first = j == i + 1 ? a : b
                    let second = j == i + 1 ? d : c
                    if orientation(first, common, second) == 0 && dot(sub(first, common), sub(second, common)) > epsilon { throw Failure.invalid }
                } else if intersects(a, b, c, d) { throw Failure.invalid }
            }
        }
    }
    private static func sub(_ a: Point, _ b: Point) -> Point { Point(x: a.x - b.x, y: a.y - b.y) }
    private static func cross(_ a: Point, _ b: Point) -> Double { a.x * b.y - a.y * b.x }
    private static func dot(_ a: Point, _ b: Point) -> Double { a.x * b.x + a.y * b.y }
    // Match Geometry.Distance's operation order at epsilon boundaries.
    private static func distance(_ a: Point, _ b: Point) -> Double {
        let dx = abs(a.x - b.x), dy = abs(a.y - b.y)
        let scale = max(dx, dy)
        if scale == 0 { return 0 }
        return scale * sqrt(pow(dx / scale, 2) + pow(dy / scale, 2))
    }
    private static func orientation(_ a: Point, _ b: Point, _ c: Point) -> Int {
        let value = cross(sub(b, a), sub(c, a)), tolerance = epsilon * max(1, distance(a, b))
        return value > tolerance ? 1 : value < -tolerance ? -1 : 0
    }
    private static func onSegment(_ p: Point, _ a: Point, _ b: Point) -> Bool {
        orientation(a, b, p) == 0 && p.x >= min(a.x, b.x) - epsilon && p.x <= max(a.x, b.x) + epsilon && p.y >= min(a.y, b.y) - epsilon && p.y <= max(a.y, b.y) + epsilon
    }
    private static func intersects(_ a: Point, _ b: Point, _ c: Point, _ d: Point) -> Bool {
        let o1 = orientation(a, b, c), o2 = orientation(a, b, d), o3 = orientation(c, d, a), o4 = orientation(c, d, b)
        return (o1 * o2 < 0 && o3 * o4 < 0) || (o1 == 0 && onSegment(c, a, b)) || (o2 == 0 && onSegment(d, a, b)) || (o3 == 0 && onSegment(a, c, d)) || (o4 == 0 && onSegment(b, c, d))
    }
}
