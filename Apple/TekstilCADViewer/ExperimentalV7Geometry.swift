import Foundation

/// Conservative exact-vector check for a quarter-turn followed by translation.
/// This does not validate topology, physical suitability, historical hashes or
/// marker signed-zero bits. Rounding in legitimate layouts may cause rejection.
enum ExperimentalV7Geometry {
    private static func exactDifference(_ a: Double, _ b: Double) -> (Double, Double)? {
        guard a.isFinite, b.isFinite, abs(a) <= 100_000_000, abs(b) <= 100_000_000 else { return nil }
        let high = a - b
        let bVirtual = a - high
        let aVirtual = high + bVirtual
        let low = (a - aVirtual) + (bVirtual - b)
        guard high.isFinite, low.isFinite else { return nil }
        return (high, low)
    }
    private static func sameDifference(_ a: Double, _ b: Double, _ c: Double, _ d: Double) -> Bool {
        guard let first = exactDifference(a, b), let second = exactDifference(c, d) else { return false }
        return first.0 == second.0 && first.1 == second.1
    }
    private static func turn(_ point: Drawing.Point, degrees: Int) -> Drawing.Point {
        switch degrees {
        case 0: return point
        case 90: return Drawing.Point(x: -point.y, y: point.x)
        case 180: return Drawing.Point(x: -point.x, y: -point.y)
        default: return Drawing.Point(x: point.y, y: -point.x)
        }
    }
    private static func sameText(_ a: String?, _ b: String?) -> Bool {
        switch (a, b) {
        case (nil, nil): return true
        case let (left?, right?): return left.unicodeScalars.map(\.value) == right.unicodeScalars.map(\.value)
        default: return false
        }
    }
    static func exactlyMatches(source: Drawing.Piece, placed: Drawing.Piece, degrees: Int,
                               direction: String, tolerance: Double,
                               cancelled: () -> Bool = { false }) throws -> Bool {
        if cancelled() { throw CancellationError() }
        guard [0, 90, 180, 270].contains(degrees), source.closed, placed.closed,
              ["OneWay", "TwoWay"].contains(direction), tolerance.isFinite, tolerance >= 0, tolerance <= 90,
              sameText(source.name, placed.name), sameText(source.role, placed.role), sameText(source.size, placed.size),
              sameText(source.material, placed.material), placed.quantity == 1,
              source.points.count == placed.points.count, let first = source.points.first,
              let placedFirst = placed.points.first, source.notches == placed.notches else { return false }
        let anchor = turn(first, degrees: degrees)
        func sameTranslation(_ raw: Drawing.Point, _ transformed: Drawing.Point) -> Bool {
            let p = turn(raw, degrees: degrees)
            return sameDifference(p.x, anchor.x, transformed.x, placedFirst.x) &&
                   sameDifference(p.y, anchor.y, transformed.y, placedFirst.y)
        }
        for (i, point) in source.points.enumerated() {
            if i % 256 == 0 && cancelled() { throw CancellationError() }
            if !sameTranslation(point, placed.points[i]) { return false }
        }
        switch (source.grain, placed.grain) {
        case (nil, nil): break
        case let (a?, b?):
            guard !a.needsReview, !b.needsReview, sameTranslation(a.start, b.start), sameTranslation(a.end, b.end),
                  grainAllowed(start: turn(a.start, degrees: degrees), end: turn(a.end, degrees: degrees), direction: direction, tolerance: tolerance),
                  grainAllowed(start: b.start, end: b.end, direction: direction, tolerance: tolerance) else { return false }
        default: return false
        }
        return true
    }

    private static func grainAllowed(start: Drawing.Point, end: Drawing.Point, direction: String, tolerance: Double) -> Bool {
        let dx = end.x - start.x
        var dy = end.y - start.y
        if direction == "TwoWay" { dy = abs(dy) }
        if dx == 0 && dy > 0 { return true }
        if dy == 0 && tolerance == 90 { return true }
        return dx != 0 && tolerance > 0 && atan2(abs(dx), dy) * (180 / Double.pi) < tolerance - 1e-12
    }
}
