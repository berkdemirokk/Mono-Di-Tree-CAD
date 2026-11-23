import Foundation

/// Operations used by the bounded, interactive contour tools. Coordinates and distances are in mm.
enum DraftContourGeometry {
    static let pointLimit = 250
    static let coordinateLimit = 100_000_000.0
    private static let epsilon = 1e-8

    static func coordinate(_ text: String) -> Double? {
        let input = text.trimmingCharacters(in: .whitespacesAndNewlines)
            .replacingOccurrences(of: ",", with: ".")
        guard let value = Double(input), value.isFinite, abs(value) <= coordinateLimit else { return nil }
        return value
    }

    static func validationError(points: [DraftPoint], closed: Bool) -> String? {
        guard points.count >= (closed ? 3 : 2) else {
            return closed ? "Kapalı bir parça için en az 3 nokta gerekli." : "Açık çizgi için en az 2 nokta gerekli."
        }
        guard points.count <= pointLimit else { return "Bu araç en fazla 250 noktalı parçaları düzenler." }
        guard points.allSatisfy({ abs($0.x) <= coordinateLimit && abs($0.y) <= coordinateLimit }) else {
            return "Nokta çalışma alanı sınırını aşıyor."
        }

        let edgeCount = closed ? points.count : points.count - 1
        for i in 0..<edgeCount {
            let a = points[i], b = points[(i + 1) % points.count]
            if hypot(b.x - a.x, b.y - a.y) <= epsilon {
                return "Aynı konumda iki ardışık nokta var."
            }
            for j in (i + 1)..<edgeCount {
                let c = points[j], d = points[(j + 1) % points.count]
                if j == i + 1 {
                    if overlapsAtVertex(a, b, d) { return "Komşu kenarlar üst üste biniyor." }
                } else if closed && i == 0 && j == edgeCount - 1 {
                    if overlapsAtVertex(b, a, c) { return "Komşu kenarlar üst üste biniyor." }
                } else if intersects(a, b, c, d) {
                    return "Parçanın çizgileri birbirini kesiyor. Noktaları gözden geçirin."
                }
            }
        }
        if closed {
            // Translation before multiplication avoids cancellation for small shapes far from the origin.
            let origin = points[0]
            var twiceArea = 0.0
            for i in 1..<(points.count - 1) {
                twiceArea += cross(origin, points[i], points[i + 1])
            }
            if abs(twiceArea) <= 1e-6 { return "Parça kapalı bir alan oluşturmuyor." }
        }
        return nil
    }

    private static func cross(_ a: DraftPoint, _ b: DraftPoint, _ c: DraftPoint) -> Double {
        (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)
    }

    private static func orientation(_ a: DraftPoint, _ b: DraftPoint, _ c: DraftPoint) -> Int {
        let determinant = cross(a, b, c)
        let tolerance = epsilon * max(1, hypot(b.x - a.x, b.y - a.y))
        if determinant > tolerance { return 1 }
        if determinant < -tolerance { return -1 }
        return 0
    }

    private static func overlapsAtVertex(_ a: DraftPoint, _ joint: DraftPoint, _ b: DraftPoint) -> Bool {
        let dot = (a.x - joint.x) * (b.x - joint.x) + (a.y - joint.y) * (b.y - joint.y)
        guard dot > 0 else { return false }
        return cross(joint, a, b) == 0 || (dot > epsilon && orientation(a, joint, b) == 0)
    }

    private static func withinBounds(_ p: DraftPoint, _ a: DraftPoint, _ b: DraftPoint) -> Bool {
        p.x >= min(a.x, b.x) - epsilon && p.x <= max(a.x, b.x) + epsilon &&
        p.y >= min(a.y, b.y) - epsilon && p.y <= max(a.y, b.y) + epsilon
    }

    private static func intersects(_ a: DraftPoint, _ b: DraftPoint, _ c: DraftPoint, _ d: DraftPoint) -> Bool {
        let abc = orientation(a, b, c), abd = orientation(a, b, d)
        let cda = orientation(c, d, a), cdb = orientation(c, d, b)
        if abc * abd < 0 && cda * cdb < 0 { return true }
        return (abc == 0 && withinBounds(c, a, b)) || (abd == 0 && withinBounds(d, a, b))
            || (cda == 0 && withinBounds(a, c, d)) || (cdb == 0 && withinBounds(b, c, d))
    }
}

/// Zoom and pan of the drawing, relative to the fit-to-screen view. Zoom is about the centre of the
/// drawing area; pan is a screen-space offset in points. Kept as plain values so it can be tested
/// without a view.
struct DraftViewport: Equatable {
    static let minZoom = 1.0
    static let maxZoom = 24.0
    static let step = 1.6
    /// How much of the drawing must stay inside the drawing area, in points.
    static let keepVisible = 48.0

    var zoom = 1.0
    var pan = CGSize.zero

    var isFit: Bool { zoom <= Self.minZoom + 1e-9 && pan == .zero }
    var canZoomIn: Bool { zoom < Self.maxZoom - 1e-9 }
    var canZoomOut: Bool { zoom > Self.minZoom + 1e-9 }

    /// Multiplies the zoom by `factor`, clamped. The point under the centre stays there, so the
    /// pan scales with the zoom. Returning to fit also clears the pan.
    func zoomed(by factor: Double) -> DraftViewport {
        guard factor.isFinite, factor > 0 else { return self }
        let next = min(Self.maxZoom, max(Self.minZoom, zoom * factor))
        if next <= Self.minZoom + 1e-9 { return DraftViewport() }
        let ratio = next / zoom
        return DraftViewport(zoom: next,
                             pan: CGSize(width: Double(pan.width) * ratio, height: Double(pan.height) * ratio))
    }

    /// Multiplies the zoom by `factor` like `zoomed(by:)`, but keeps the drawing under `anchor` (a
    /// point in the drawing area, for example where a pinch started) in place instead of the
    /// centre. The same clamps apply: zoom stays within 1–24, the pan within the bounds of
    /// `panned(by:in:content:)`, and returning to fit clears the pan. When a bound is hit the
    /// anchor may drift by the clamped amount.
    func zoomed(by factor: Double, keeping anchor: CGPoint, in size: CGSize, content: CGSize) -> DraftViewport {
        guard factor.isFinite, factor > 0, anchor.x.isFinite, anchor.y.isFinite,
              size.width.isFinite, size.height.isFinite else { return self }
        let next = min(Self.maxZoom, max(Self.minZoom, zoom * factor))
        if next <= Self.minZoom + 1e-9 { return DraftViewport() }
        let ratio = next / zoom
        // Screen = centre + (fit - centre) * zoom + pan. Holding the screen point d (relative to
        // the centre) fixed while the zoom changes gives pan' = d - (d - pan) * ratio.
        let dx = Double(anchor.x) - Double(size.width) / 2
        let dy = Double(anchor.y) - Double(size.height) / 2
        let target = CGSize(width: dx - (dx - Double(pan.width)) * ratio,
                            height: dy - (dy - Double(pan.height)) * ratio)
        return DraftViewport(zoom: next).panned(by: target, in: size, content: content)
    }

    /// Adds `delta` to the pan. `content` is the size of the drawing at fit (zoom 1). The drawing
    /// can be moved until only `keepVisible` points of it are left in view, never further.
    func panned(by delta: CGSize, in size: CGSize, content: CGSize) -> DraftViewport {
        guard delta.width.isFinite, delta.height.isFinite, zoom > Self.minZoom + 1e-9 else { return self }
        let limitX = max(0, (Double(content.width) * zoom + Double(size.width)) / 2 - Self.keepVisible)
        let limitY = max(0, (Double(content.height) * zoom + Double(size.height)) / 2 - Self.keepVisible)
        return DraftViewport(zoom: zoom, pan: CGSize(
            width: min(limitX, max(-limitX, Double(pan.width + delta.width))),
            height: min(limitY, max(-limitY, Double(pan.height + delta.height)))))
    }
}

/// Spacing of the faint millimetre grid behind the drawing. The step follows the zoom through
/// 1, 5, 10, 50, 100, 500 … mm: the smallest one whose squares are at least `minimumSpacing`
/// points wide. Every `majorEvery`-th line is drawn a little stronger; it marks the step the grid
/// would switch to when zoomed out further.
struct DraftGrid: Equatable {
    static let minimumSpacing = 12.0

    let step: Double
    let majorEvery: Int

    init?(pointsPerMillimetre: Double, minimumSpacing: Double = DraftGrid.minimumSpacing) {
        guard pointsPerMillimetre.isFinite, pointsPerMillimetre > 0,
              minimumSpacing.isFinite, minimumSpacing > 0 else { return nil }
        var step = 1.0
        var nextIsFive = true
        while step * pointsPerMillimetre < minimumSpacing {
            guard step < 1e9 else { return nil }
            step *= nextIsFive ? 5 : 2
            nextIsFive.toggle()
        }
        self.step = step
        majorEvery = nextIsFive ? 5 : 2
    }

    /// Whether the line at `index × step` millimetres is a major line.
    func isMajor(_ index: Double) -> Bool {
        index.truncatingRemainder(dividingBy: Double(majorEvery)) == 0
    }
}

enum DrawingProjection {
    static func screen(_ p: CGPoint, minimum: CGPoint, offset: CGPoint, scale: Double) -> CGPoint {
        CGPoint(x: offset.x + (p.x - minimum.x) * scale, y: offset.y + (p.y - minimum.y) * scale)
    }
}

/// A centered aspect-fit map shared by drawing and picking. CAD Y increases downwards.
struct DraftCanvasMap {
    private let scale: Double
    private let left: Double
    private let top: Double
    private let minX: Double
    private let minY: Double
    private let contentWidth: Double
    private let contentHeight: Double

    private init(scale: Double, left: Double, top: Double, minX: Double, minY: Double,
                 contentWidth: Double, contentHeight: Double) {
        self.scale = scale
        self.left = left
        self.top = top
        self.minX = minX
        self.minY = minY
        self.contentWidth = contentWidth
        self.contentHeight = contentHeight
    }

    /// Size of the drawing on screen at fit, before any zoom.
    var contentSize: CGSize { CGSize(width: contentWidth, height: contentHeight) }

    /// The pan that brings `point` to the middle of the drawing area at `zoom`. Called on the
    /// fit map, not on a zoomed one.
    func panCentering(_ point: DraftPoint, zoom: Double, in size: CGSize) -> CGSize {
        let at = screen(point)
        return CGSize(width: -(Double(at.x) - Double(size.width) / 2) * zoom,
                      height: -(Double(at.y) - Double(size.height) / 2) * zoom)
    }

    /// The same map seen through a zoom and pan. Distances measured on screen are divided by the
    /// zoomed scale, so dragging and picking keep working while zoomed in.
    func viewed(_ viewport: DraftViewport, in size: CGSize) -> DraftCanvasMap {
        let cx = Double(size.width) / 2, cy = Double(size.height) / 2
        let z = viewport.zoom
        return DraftCanvasMap(scale: scale * z,
                              left: cx + (left - cx) * z + Double(viewport.pan.width),
                              top: cy + (top - cy) * z + Double(viewport.pan.height),
                              minX: minX, minY: minY,
                              contentWidth: contentWidth, contentHeight: contentHeight)
    }

    init?(size: CGSize, minX: Double, maxX: Double, minY: Double, maxY: Double, inset: Double) {
        self.init(size: size, minX: minX, maxX: maxX, minY: minY, maxY: maxY,
                  insetLeft: inset, insetRight: inset, insetTop: inset, insetBottom: inset)
    }

    /// Fits like `init(size:minX:maxX:minY:maxY:inset:)`, but keeps a bottom-right `corner` free
    /// for controls drawn over the drawing. When the plain fit already misses the corner it is
    /// used unchanged; otherwise either a strip on the right or one at the bottom is kept free,
    /// whichever leaves the drawing larger.
    init?(size: CGSize, minX: Double, maxX: Double, minY: Double, maxY: Double, inset: Double,
          keepingClear corner: CGSize) {
        guard let plain = DraftCanvasMap(size: size, minX: minX, maxX: maxX, minY: minY, maxY: maxY,
                                         inset: inset) else { return nil }
        let cornerWidth = Double(corner.width), cornerHeight = Double(corner.height)
        guard cornerWidth.isFinite, cornerHeight.isFinite, cornerWidth > 0, cornerHeight > 0,
              plain.overlapsCorner(width: cornerWidth, height: cornerHeight, in: size) else {
            self = plain
            return
        }
        let right = DraftCanvasMap(size: size, minX: minX, maxX: maxX, minY: minY, maxY: maxY,
                                   insetLeft: inset, insetRight: max(inset, cornerWidth),
                                   insetTop: inset, insetBottom: inset)
        let below = DraftCanvasMap(size: size, minX: minX, maxX: maxX, minY: minY, maxY: maxY,
                                   insetLeft: inset, insetRight: inset,
                                   insetTop: inset, insetBottom: max(inset, cornerHeight))
        switch (right, below) {
        case let (r?, b?): self = r.scale >= b.scale ? r : b
        case let (r?, nil): self = r
        case let (nil, b?): self = b
        // Too small to keep the corner free: drawing under the controls beats drawing nothing.
        case (nil, nil): self = plain
        }
    }

    private init?(size: CGSize, minX: Double, maxX: Double, minY: Double, maxY: Double,
                  insetLeft: Double, insetRight: Double, insetTop: Double, insetBottom: Double) {
        let width = Double(size.width), height = Double(size.height)
        let areaWidth = width - insetLeft - insetRight, areaHeight = height - insetTop - insetBottom
        guard areaWidth > 0, areaHeight > 0, maxX >= minX, maxY >= minY else { return nil }
        let spanX = maxX - minX, spanY = maxY - minY
        scale = min(areaWidth / max(spanX, 1), areaHeight / max(spanY, 1))
        left = insetLeft + (areaWidth - spanX * scale) / 2
        top = insetTop + (areaHeight - spanY * scale) / 2
        self.minX = minX
        self.minY = minY
        contentWidth = spanX * scale
        contentHeight = spanY * scale
    }

    /// Screen points per millimetre, including any zoom.
    var pointsPerMillimetre: Double { scale }

    /// Whether the drawing's bounding box reaches into the bottom-right corner of `size`. Meant for
    /// a fit map; a zoomed one keeps the fit-size box.
    func overlapsCorner(width cornerWidth: Double, height cornerHeight: Double, in size: CGSize) -> Bool {
        // A drawing that only touches the corner's edge (to round-off) does not count.
        left + contentWidth > Double(size.width) - cornerWidth + 1e-6 &&
            top + contentHeight > Double(size.height) - cornerHeight + 1e-6
    }

    func screen(_ p: DraftPoint) -> CGPoint {
        DrawingProjection.screen(CGPoint(x: p.x, y: p.y), minimum: CGPoint(x: minX, y: minY),
                                 offset: CGPoint(x: left, y: top), scale: scale)
    }

    func model(at p: CGPoint) -> DraftPoint {
        DraftPoint(x: minX + (Double(p.x) - left) / scale, y: minY + (Double(p.y) - top) / scale)
    }

    func translation(_ drag: CGSize, for piece: DraftPiece, snap: Bool) -> DraftPoint {
        func delta(_ pixels: Double, origin: Double) -> Double {
            guard pixels != 0 else { return 0 }
            let distance = pixels / scale
            return snap ? ((origin + distance) / 5).rounded() * 5 - origin : distance
        }
        return DraftPoint(x: delta(Double(drag.width), origin: piece.minX),
                          y: delta(Double(drag.height), origin: piece.minY))
    }

    func sketchPoint(at location: CGPoint, extent: Double, snapToFive: Bool) -> DraftPoint? {
        let p = model(at: location)
        // Allow sub-nanometre round-off when tapping the border.
        let epsilon = 1e-8
        guard p.x >= -epsilon, p.x <= extent + epsilon,
              p.y >= -epsilon, p.y <= extent + epsilon else { return nil }
        let step = snapToFive ? 5.0 : 0.1
        func snap(_ value: Double) -> Double {
            min(extent, max(0, (value / step).rounded() * step))
        }
        return DraftPoint(x: snap(p.x), y: snap(p.y))
    }
    func nearestVertex(to location: CGPoint, points: [DraftPoint]) -> Int? {
        var best: Int?
        var distance = 22.0
        for (i, p) in points.enumerated() {
            let at = screen(p)
            let candidate = hypot(Double(at.x - location.x), Double(at.y - location.y))
            if candidate <= distance {
                distance = candidate
                best = i
            }
        }
        return best
    }
}
