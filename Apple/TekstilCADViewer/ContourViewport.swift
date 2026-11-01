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

    enum VertexHit: Equatable { case none, ambiguous, vertex(Int) }

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
