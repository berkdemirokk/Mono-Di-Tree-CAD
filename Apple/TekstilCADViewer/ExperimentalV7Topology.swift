import Foundation

/// Conservative preliminary simple-contour screening for experimental history.
/// Ambiguous floating-point predicates are rejected, not certified as valid.
enum ExperimentalV7Topology {
    enum Failure: Error { case invalid, cancelled }
    private static func same(_ a: Drawing.Point, _ b: Drawing.Point) -> Bool { a.x == b.x && a.y == b.y }
    private static func orient(_ a: Drawing.Point, _ b: Drawing.Point, _ c: Drawing.Point) -> Int? {
        let left = (a.x - c.x) * (b.y - c.y)
        let right = (a.y - c.y) * (b.x - c.x)
        let determinant = left - right, magnitude = abs(left) + abs(right)
        guard determinant.isFinite, magnitude.isFinite, magnitude >= Double.leastNormalMagnitude,
              abs(determinant) > magnitude * 3.3306690738754716e-16 else { return nil }
        return determinant > 0 ? 1 : -1
    }
    private static func boxesDisjoint(_ a: Drawing.Point, _ b: Drawing.Point, _ c: Drawing.Point, _ d: Drawing.Point) -> Bool {
        max(a.x, b.x) < min(c.x, d.x) || max(c.x, d.x) < min(a.x, b.x) ||
        max(a.y, b.y) < min(c.y, d.y) || max(c.y, d.y) < min(a.y, b.y)
    }
    static func verify(_ piece: Drawing.Piece, cancelled: () -> Bool = { false }) throws {
        if cancelled() { throw Failure.cancelled }
        guard piece.closed else { throw Failure.invalid }
        let points = piece.points
        let count = points.count > 1 && same(points[0], points[points.count - 1]) ? points.count - 1 : points.count
        guard count >= 3, count <= 2_000 else { throw Failure.invalid }
        var area = 0.0, magnitude = 0.0, hasCertainTurn = false
        let origin = points[0]
        for i in 0..<count {
            if cancelled() { throw Failure.cancelled }
            let a = points[i], b = points[(i + 1) % count], c = points[(i + 2) % count]
            guard a.x.isFinite, a.y.isFinite, abs(a.x) <= 100_000_000, abs(a.y) <= 100_000_000,
                  hypot(a.x - b.x, a.y - b.y) > 1.000000000000001e-8 else { throw Failure.invalid }
            if orient(a, b, c) != nil { hasCertainTurn = true }
            else {
                // Ambiguous adjacent edges are accepted only if one coordinate
                // proves their interiors lie on opposite sides of the joint.
                let monotoneX = (a.x < b.x && b.x < c.x) || (c.x < b.x && b.x < a.x)
                let monotoneY = (a.y < b.y && b.y < c.y) || (c.y < b.y && b.y < a.y)
                guard monotoneX || monotoneY else { throw Failure.invalid }
            }
            if i > 0 && i + 1 < count {
                let left = (a.x - origin.x) * (b.y - origin.y)
                let right = (a.y - origin.y) * (b.x - origin.x)
                area += left - right; magnitude += abs(left) + abs(right)
            }
            for j in (i + 1)..<count {
                if j % 256 == 0 && cancelled() { throw Failure.cancelled }
                if same(a, points[j]) { throw Failure.invalid }
                if j == i + 1 || (i == 0 && j == count - 1) { continue }
                let d = points[j], e = points[(j + 1) % count]
                if boxesDisjoint(a, b, d, e) { continue }
                guard let abD = orient(a, b, d), let abE = orient(a, b, e),
                      let deA = orient(d, e, a), let deB = orient(d, e, b) else { throw Failure.invalid }
                if abD != abE && deA != deB { throw Failure.invalid }
            }
        }
        // A generous error allowance deliberately rejects numerically uncertain
        // areas; this screening does not prove desktop or production equivalence.
        guard hasCertainTurn, area.isFinite, magnitude.isFinite,
              abs(area) > 2e-8 + magnitude * Double(count) * 1e-12 else { throw Failure.invalid }
    }
}
