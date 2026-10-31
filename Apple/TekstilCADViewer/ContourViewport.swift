import Foundation

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
        guard let first = points.first, width.isFinite, height.isFinite, width > 0, height > 0 else { return nil }
        var minX = first.x, maxX = first.x, minY = first.y, maxY = first.y
        for p in points {
            guard p.x.isFinite, p.y.isFinite else { return nil }
            minX = min(minX, p.x); maxX = max(maxX, p.x)
            minY = min(minY, p.y); maxY = max(maxY, p.y)
        }
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
                y: height - offsetY - ((point.y * coordinateScale - minY) / extent) * fitted)
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
