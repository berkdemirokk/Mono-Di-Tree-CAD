import Foundation

// Geometry bounds are independent of canvas size and gesture state.
struct ContourBounds {
    let minX: Double, maxX: Double, minY: Double, maxY: Double

    init?(combining bounds: [ContourBounds]) {
        guard let first = bounds.first else { return nil }
        var minX = first.minX, maxX = first.maxX, minY = first.minY, maxY = first.maxY
        for bound in bounds.dropFirst() {
            minX = min(minX, bound.minX); maxX = max(maxX, bound.maxX)
            minY = min(minY, bound.minY); maxY = max(maxY, bound.maxY)
        }
        self.minX = minX; self.maxX = maxX; self.minY = minY; self.maxY = maxY
    }

    init?(points: [ContourMeasure.Point]) {
        guard let first = points.first else { return nil }
        var minX = first.x, maxX = first.x, minY = first.y, maxY = first.y
        for p in points {
            guard p.x.isFinite, p.y.isFinite else { return nil }
            minX = min(minX, p.x); maxX = max(maxX, p.x)
            minY = min(minY, p.y); maxY = max(maxY, p.y)
        }
        self.minX = minX; self.maxX = maxX; self.minY = minY; self.maxY = maxY
    }
}

// Align each bounding-box minimum at the same origin without scaling either
// piece independently. Reject overflow instead of drawing misleading geometry.
enum ContourOverlay {
    // Strictly below 0.001 mm even though that decimal is not exactly binary64.
    static let maximumError = 0.001.nextDown
    // Half-millimetre increments are exactly representable in binary64.
    // Accept dot or comma, but reject exponents and ambiguous grouping.
    static func halfMillimeterShift(_ text: String) -> Double? {
        let input = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !input.isEmpty, input.utf8.count <= 8 else { return nil }
        let negative = input.first == "-"
        let unsigned = (input.first == "-" || input.first == "+") ? String(input.dropFirst()) : input
        let parts = unsigned.replacingOccurrences(of: ",", with: ".").split(separator: ".", omittingEmptySubsequences: false)
        guard (1...2).contains(parts.count), !parts[0].isEmpty,
              parts[0].utf8.allSatisfy({ (48...57).contains($0) }), let whole = Int(parts[0]),
              whole <= 10_000 else { return nil }
        let fraction = parts.count == 2 ? String(parts[1]) : "0"
        guard fraction == "0" || fraction == "5" else { return nil }
        let value = Double(whole) + (fraction == "5" ? 0.5 : 0)
        guard value <= 10_000 else { return nil }
        return negative ? -value : value
    }
    // TwoSum gives the exact residual of each finite binary64 addition under
    // IEEE round-to-nearest with gradual underflow. The positive accumulator is
    // rounded upward after every nonzero contribution, including all vertices.
    private struct ErrorBudget {
        var total = 0.0
        mutating func add(_ a: Double, _ b: Double) -> Double? {
            guard a.isFinite, b.isFinite else { return nil }
            let sum = a + b
            guard sum.isFinite else { return nil }
            let bVirtual = sum - a
            guard bVirtual.isFinite else { return nil }
            let residual = (a - (sum - bVirtual)) + (b - bVirtual)
            guard residual.isFinite else { return nil }
            if residual != 0 {
                let upper = (total + abs(residual)).nextUp
                guard upper.isFinite, upper <= ContourOverlay.maximumError else { return nil }
                total = upper
            }
            return sum
        }
    }

    // This assessment covers normalized bounding coordinates and every point,
    // both axes and both translation stages. It sums absolute residuals across
    // all operations (conservative, including duplicate boundary points).
    static func errorBound(first: [ContourMeasure.Point], firstBounds: ContourBounds,
                           second: [ContourMeasure.Point], secondBounds: ContourBounds,
                           dx: Double = 0, dy: Double = 0) -> Double? {
        var budget = ErrorBudget()
        guard let aw = budget.add(firstBounds.maxX, -firstBounds.minX),
              let ah = budget.add(firstBounds.maxY, -firstBounds.minY),
              let bw = budget.add(secondBounds.maxX, -secondBounds.minX),
              let bh = budget.add(secondBounds.maxY, -secondBounds.minY),
              aw >= 0, ah >= 0, bw >= 0, bh >= 0,
              budget.add(bw, dx) != nil, budget.add(bh, dy) != nil else { return nil }
        func include(_ point: ContourMeasure.Point, _ bounds: ContourBounds, _ x: Double, _ y: Double, _ budget: inout ErrorBudget) -> Bool {
            guard let nx = budget.add(point.x, -bounds.minX), let ny = budget.add(point.y, -bounds.minY),
                  budget.add(nx, x) != nil, budget.add(ny, y) != nil else { return false }
            return true
        }
        for point in first { if !include(point, firstBounds, 0, 0, &budget) { return nil } }
        for point in second { if !include(point, secondBounds, dx, dy, &budget) { return nil } }
        return budget.total
    }

    // Caller must assess the complete set before rendering or changing shift.
    static func bounds(_ first: ContourBounds, _ second: ContourBounds, dx: Double = 0, dy: Double = 0) -> ContourBounds? {
        let aw = first.maxX - first.minX, ah = first.maxY - first.minY
        let bw = second.maxX - second.minX, bh = second.maxY - second.minY
        let maxX = bw + dx, maxY = bh + dy
        guard [aw, ah, bw, bh, dx, dy, maxX, maxY].allSatisfy(\.isFinite),
              aw >= 0, ah >= 0, bw >= 0, bh >= 0 else { return nil }
        return ContourBounds(points: [.init(x: 0, y: 0), .init(x: aw, y: ah),
                                      .init(x: dx, y: dy), .init(x: maxX, y: maxY)])
    }
    static func aligned(_ point: ContourMeasure.Point, bounds: ContourBounds, dx: Double = 0, dy: Double = 0) -> ContourMeasure.Point? {
        let x = (point.x - bounds.minX) + dx, y = (point.y - bounds.minY) + dy
        guard x.isFinite, y.isFinite else { return nil }
        return .init(x: x, y: y)
    }
}
// Fits finite model coordinates without multiplying by an enormous scale.
struct ContourViewport {
    let coordinateScale: Double
    let minX: Double
    let minY: Double
    let extent: Double
    let fitted: Double
    let offsetX: Double
    let offsetY: Double
    let height: Double

    init?(points: [ContourMeasure.Point], width: Double, height: Double) {
        guard let bounds = ContourBounds(points: points) else { return nil }
        self.init(bounds: bounds, width: width, height: height)
    }

    init?(bounds: ContourBounds, width: Double, height: Double) {
        guard width.isFinite, height.isFinite, width > 0, height > 0 else { return nil }
        var minX = bounds.minX, maxX = bounds.maxX, minY = bounds.minY, maxY = bounds.maxY
        // Finite opposite-sign endpoints can overflow when subtracted.
        let coordinateScale = (maxX - minX).isFinite && (maxY - minY).isFinite ? 1.0 : 0.5
        minX *= coordinateScale; maxX *= coordinateScale
        minY *= coordinateScale; maxY *= coordinateScale
        let modelWidth = maxX - minX, modelHeight = maxY - minY
        let extent = max(modelWidth, modelHeight)
        guard extent.isFinite else { return nil }
        self.coordinateScale = coordinateScale
        // Keep a collapsed contour available for inspection at the center.
        if extent == 0 {
            self.minX = minX; self.minY = minY; self.extent = 1; self.fitted = 0
            self.offsetX = width / 2; self.offsetY = height / 2; self.height = height
            return
        }
        let normalizedWidth = modelWidth / extent, normalizedHeight = modelHeight / extent
        let availableWidth = max(width - 32, 1), availableHeight = max(height - 32, 1)
        let fitted = min(normalizedWidth > 0 ? availableWidth / normalizedWidth : .infinity,
                         normalizedHeight > 0 ? availableHeight / normalizedHeight : .infinity)
        guard fitted.isFinite else { return nil }
        self.minX = minX; self.minY = minY; self.extent = extent; self.fitted = fitted
        self.offsetX = (width - normalizedWidth * fitted) / 2
        self.offsetY = (height - normalizedHeight * fitted) / 2
        self.height = height
    }

    func position(_ point: ContourMeasure.Point) -> CGPoint {
        CGPoint(x: offsetX + ((point.x * coordinateScale - minX) / extent) * fitted,
                y: offsetY + ((point.y * coordinateScale - minY) / extent) * fitted)
    }

    enum VertexHit: Equatable {
        case none, ambiguous, vertex(Int)
        var selectionNotice: String? {
            switch self {
            case .none:
                return "Nokta seçilemedi. Aşağıdaki işaretler ve ölçüler önceki seçime aittir. Bir köşeye dokunun veya listeden seçin."
            case .ambiguous:
                return "Birden fazla yakın nokta var; yeni nokta seçilmedi. Aşağıdaki işaretler ve ölçüler önceki seçime aittir. Yakınlaştırın veya listeden seçin."
            case .vertex: return nil
            }
        }
    }

    // Screen-space hit test. Distinct nearby vertices must be selected from
    // the list instead of guessing; a repeated closure aliases vertex zero.
    func vertexHit(points: [ContourMeasure.Point], closed: Bool, tap: CGPoint,
                   size: CGSize, zoom: CGFloat, offset: CGSize, tolerance: CGFloat = 18) -> VertexHit {
        guard tap.x.isFinite, tap.y.isFinite, size.width.isFinite, size.height.isFinite,
              size.width > 0, size.height > 0, zoom.isFinite, zoom > 0,
              offset.width.isFinite, offset.height.isFinite, tolerance.isFinite, tolerance > 0 else { return .none }
        let repeated = closed && points.count > 1 && points[0].x == points[points.count - 1].x && points[0].y == points[points.count - 1].y
        let count = points.count - (repeated ? 1 : 0)
        var hit: Int?
        for index in 0..<count {
            let point = position(points[index])
            let x = (point.x - size.width / 2) * zoom + size.width / 2 + offset.width
            let y = (point.y - size.height / 2) * zoom + size.height / 2 + offset.height
            guard x.isFinite, y.isFinite else { continue }
            if hypot(x - tap.x, y - tap.y) <= tolerance {
                if hit != nil { return .ambiguous }
                hit = index
            }
        }
        return hit.map(VertexHit.vertex) ?? .none
    }
    // Keep the highlighted ring inside the clipped canvas without changing zoom.
    static func visibleOffset(point: CGPoint, size: CGSize, zoom: CGFloat, offset: CGSize) -> CGSize? {
        guard size.width.isFinite, size.height.isFinite, size.width > 0, size.height > 0,
              zoom.isFinite, zoom > 0, point.x.isFinite, point.y.isFinite,
              offset.width.isFinite, offset.height.isFinite else { return nil }
        func adjust(_ p: CGFloat, _ length: CGFloat, _ current: CGFloat) -> CGFloat {
            let margin = min(10 * zoom + 2, length / 2)
            let displayed = (p - length / 2) * zoom + length / 2 + current
            return current + max(margin - displayed, 0) - max(displayed - (length - margin), 0)
        }
        let result = CGSize(width: adjust(point.x, size.width, offset.width),
                            height: adjust(point.y, size.height, offset.height))
        guard result.width.isFinite, result.height.isFinite else { return nil }
        return result
    }
}
