import Foundation

/// Tek bir parametrik kontur segmenti. Eğriler kesikli polilineye dönüştürülmeden
/// kesin matematiksel gösterimini saklar; `points` alanını ekran çizimi için örnekler.
enum CurveSegment: Equatable, Codable {
    /// Düz çizgi.
    case line(start: DraftPoint, end: DraftPoint)

    /// İki uç nokta ve iki kontrol noktasına sahip kubik Bézier.
    case cubicBezier(p0: DraftPoint, p1: DraftPoint, p2: DraftPoint, p3: DraftPoint)

    /// Merkez, yarıçap, başlangıç açısı ve sweep açısıyla tanımlanan dairesel yay (radyan, pozitif saat yönünün tersi).
    case arc(center: DraftPoint, radius: Double, startAngle: Double, sweepAngle: Double)

    /// Segmentin parametrik uzunluğu milimetrede.
    var length: Double {
        switch self {
        case .line(let start, let end):
            return hypot(end.x - start.x, end.y - start.y)
        case .cubicBezier(let p0, let p1, let p2, let p3):
            return Self.flattenedBezierLength(p0, p1, p2, p3, tolerance: 0.25)
        case .arc(_, let radius, _, let sweepAngle):
            return abs(radius * sweepAngle)
        }
    }

    /// Parametre t ∈ [0, 1] üzerindeki nokta. Aşırı değerler sınırlanır.
    func pointAt(_ t: Double) -> DraftPoint {
        let clamped = min(max(t, 0), 1)
        switch self {
        case .line(let start, let end):
            return DraftPoint(x: start.x + (end.x - start.x) * clamped,
                              y: start.y + (end.y - start.y) * clamped)
        case .cubicBezier(let p0, let p1, let p2, let p3):
            let u = 1 - clamped
            return DraftPoint(
                x: p0.x * (u * u * u) + p1.x * (3 * u * u * clamped) + p2.x * (3 * u * clamped * clamped) + p3.x * (clamped * clamped * clamped),
                y: p0.y * (u * u * u) + p1.y * (3 * u * u * clamped) + p2.y * (3 * u * clamped * clamped) + p3.y * (clamped * clamped * clamped))
        case .arc(let center, let radius, let startAngle, let sweepAngle):
            let angle = startAngle + sweepAngle * clamped
            return DraftPoint(x: center.x + radius * cos(angle), y: center.y + radius * sin(angle))
        }
    }

    /// Segmenti verilen tolerans içinde chord sapması kalmayacak şekilde örnekler.
    func sampled(toleranceMm: Double = 0.25) -> [DraftPoint] {
        guard toleranceMm.isFinite, toleranceMm > 0 else { return [startPoint, endPoint].compactMap { $0 } }
        switch self {
        case .line(let start, let end):
            return [start, end]
        case .cubicBezier(let p0, let p1, let p2, let p3):
            return Self.sampleBezier(p0, p1, p2, p3, tolerance: toleranceMm)
        case .arc(let center, let radius, let startAngle, let sweepAngle):
            return Self.sampleArc(center: center, radius: radius, startAngle: startAngle, sweepAngle: sweepAngle, tolerance: toleranceMm)
        }
    }

    var startPoint: DraftPoint? {
        switch self {
        case .line(let start, _): return start
        case .cubicBezier(let p0, _, _, _): return p0
        case .arc(let center, let radius, let startAngle, _):
            return DraftPoint(x: center.x + radius * cos(startAngle), y: center.y + radius * sin(startAngle))
        }
    }

    var endPoint: DraftPoint? {
        switch self {
        case .line(_, let end): return end
        case .cubicBezier(_, _, _, let p3): return p3
        case .arc(let center, let radius, let startAngle, let sweepAngle):
            let angle = startAngle + sweepAngle
            return DraftPoint(x: center.x + radius * cos(angle), y: center.y + radius * sin(angle))
        }
    }

    // MARK: - Private sampling helpers

    private static let bezierFlatnessTolerance: Double = 0.25

    private static func flattenedBezierLength(_ p0: DraftPoint, _ p1: DraftPoint, _ p2: DraftPoint, _ p3: DraftPoint, tolerance: Double) -> Double {
        var length = 0.0
        flattenLength(a: p0, b: p1, c: p2, d: p3, tolerance: tolerance, length: &length)
        return length
    }

    private static func flattenLength(a: DraftPoint, b: DraftPoint, c: DraftPoint, d: DraftPoint, tolerance: Double, length: inout Double) {
        let flatness = max(distancePointToSegment(p: b, a: a, b: d), distancePointToSegment(p: c, a: a, b: d))
        let span = max(hypot(b.x - a.x, b.y - a.y), max(hypot(c.x - b.x, c.y - b.y), hypot(d.x - c.x, d.y - c.y)))
        if flatness <= tolerance || span <= 1e-12 {
            length += hypot(d.x - a.x, d.y - a.y)
            return
        }
        let ab = DraftPoint(x: (a.x + b.x) * 0.5, y: (a.y + b.y) * 0.5)
        let bc = DraftPoint(x: (b.x + c.x) * 0.5, y: (b.y + c.y) * 0.5)
        let cd = DraftPoint(x: (c.x + d.x) * 0.5, y: (c.y + d.y) * 0.5)
        let abbc = DraftPoint(x: (ab.x + bc.x) * 0.5, y: (ab.y + bc.y) * 0.5)
        let bccd = DraftPoint(x: (bc.x + cd.x) * 0.5, y: (bc.y + cd.y) * 0.5)
        let abbccd = DraftPoint(x: (abbc.x + bccd.x) * 0.5, y: (abbc.y + bccd.y) * 0.5)
        flattenLength(a: a, b: ab, c: abbc, d: abbccd, tolerance: tolerance, length: &length)
        flattenLength(a: abbccd, b: bccd, c: cd, d: d, tolerance: tolerance, length: &length)
    }

    private static func sampleBezier(_ p0: DraftPoint, _ p1: DraftPoint, _ p2: DraftPoint, _ p3: DraftPoint, tolerance: Double) -> [DraftPoint] {
        var result = [p0]
        flatten(result: &result, a: p0, b: p1, c: p2, d: p3, tolerance: tolerance)
        return result
    }

    private static func flatten(result: inout [DraftPoint], a: DraftPoint, b: DraftPoint, c: DraftPoint, d: DraftPoint, tolerance: Double) {
        let flatness = max(distancePointToSegment(p: b, a: a, b: d), distancePointToSegment(p: c, a: a, b: d))
        let span = max(hypot(b.x - a.x, b.y - a.y), max(hypot(c.x - b.x, c.y - b.y), hypot(d.x - c.x, d.y - c.y)))
        if flatness <= tolerance || span <= 1e-12 {
            result.append(d)
            return
        }
        let ab = DraftPoint(x: (a.x + b.x) * 0.5, y: (a.y + b.y) * 0.5)
        let bc = DraftPoint(x: (b.x + c.x) * 0.5, y: (b.y + c.y) * 0.5)
        let cd = DraftPoint(x: (c.x + d.x) * 0.5, y: (c.y + d.y) * 0.5)
        let abbc = DraftPoint(x: (ab.x + bc.x) * 0.5, y: (ab.y + bc.y) * 0.5)
        let bccd = DraftPoint(x: (bc.x + cd.x) * 0.5, y: (bc.y + cd.y) * 0.5)
        let abbccd = DraftPoint(x: (abbc.x + bccd.x) * 0.5, y: (abbc.y + bccd.y) * 0.5)
        flatten(result: &result, a: a, b: ab, c: abbc, d: abbccd, tolerance: tolerance)
        flatten(result: &result, a: abbccd, b: bccd, c: cd, d: d, tolerance: tolerance)
    }

    private static func sampleArc(center: DraftPoint, radius: Double, startAngle: Double, sweepAngle: Double, tolerance: Double) -> [DraftPoint] {
        guard radius.isFinite, radius > 0 else { return [startPoint].compactMap { $0 } }
        let sweepAbs = abs(sweepAngle)
        guard sweepAbs > 1e-12 else { return [startPoint].compactMap { $0 } }
        var ratio = tolerance / abs(radius)
        if ratio >= 1.0 { ratio = 0.999 }
        let delta = 2.0 * acos(1.0 - ratio)
        let count = max(1, Int(ceil(sweepAbs / delta)))
        var result = [DraftPoint](repeating: DraftPoint(x: 0, y: 0), count: count + 1)
        for k in 0...count {
            let t = Double(k) / Double(count)
            result[k] = DraftPoint(x: center.x + radius * cos(startAngle + sweepAngle * t),
                                   y: center.y + radius * sin(startAngle + sweepAngle * t))
        }
        return result
    }

    private static func distancePointToSegment(p: DraftPoint, a: DraftPoint, b: DraftPoint) -> Double {
        let ab = DraftPoint(x: b.x - a.x, y: b.y - a.y)
        let ab2 = ab.x * ab.x + ab.y * ab.y
        guard ab2 > 1e-12 else { return hypot(p.x - a.x, p.y - a.y) }
        var t = ((p.x - a.x) * ab.x + (p.y - a.y) * ab.y) / ab2
        t = min(max(t, 0), 1)
        let closest = DraftPoint(x: a.x + ab.x * t, y: a.y + ab.y * t)
        return hypot(p.x - closest.x, p.y - closest.y)
    }
}

/// Tüm bir kapalı/açık konturun parametrik kaynağı ve ekran için örneklenmiş poliline.
struct ContourGeometry: Equatable, Codable {
    var segments: [CurveSegment]
    var closed: Bool

    /// Ekran çizimi ve çarpışma testi için örneklenmiş noktalar.
    var sampledPoints: [DraftPoint] {
        guard !segments.isEmpty else { return [] }
        var result: [DraftPoint] = []
        for (index, segment) in segments.enumerated() {
            let points = segment.sampled()
            if index > 0, let last = result.last, let first = points.first, last == first {
                result.append(contentsOf: points.dropFirst())
            } else {
                result.append(contentsOf: points)
            }
        }
        if closed, let first = result.first, let last = result.last, last != first {
            result.append(first)
        }
        return result
    }

    var bounds: (minX: Double, minY: Double, maxX: Double, maxY: Double)? {
        let pts = sampledPoints
        guard let first = pts.first else { return nil }
        var minX = first.x, maxX = first.x, minY = first.y, maxY = first.y
        for p in pts.dropFirst() {
            minX = min(minX, p.x); maxX = max(maxX, p.x)
            minY = min(minY, p.y); maxY = max(maxY, p.y)
        }
        return (minX, minY, maxX, maxY)
    }

    var totalLength: Double {
        segments.reduce(0) { $0 + $1.length }
    }
}
