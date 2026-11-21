import XCTest
@testable import TekstilCADViewer

/// Zooming about a finger, keeping the zoom buttons' corner free at fit, and the grid step.
final class ViewportAnchorTests: XCTestCase {
    private func p(_ x: Double, _ y: Double) -> DraftPoint { DraftPoint(x: x, y: y) }

    private let size = CGSize(width: 390, height: 230)

    private func fitMap() throws -> DraftCanvasMap {
        // scale 0.85, so the drawing is 255 x 170 points at fit.
        try XCTUnwrap(DraftCanvasMap(size: size, minX: 0, maxX: 300, minY: 0, maxY: 200, inset: 30))
    }

    func testThePointUnderTheFingerStaysUnderItWhileZooming() throws {
        let fit = try fitMap()
        let start = DraftViewport().zoomed(by: 1.6)
            .panned(by: CGSize(width: 10, height: -5), in: size, content: fit.contentSize)
        for anchor in [CGPoint(x: 50, y: 40), CGPoint(x: 300, y: 200), CGPoint(x: 195, y: 115)] {
            for factor in [1.5, 2.0, 0.8] {
                let under = fit.viewed(start, in: size).model(at: anchor)
                let next = start.zoomed(by: factor, keeping: anchor, in: size, content: fit.contentSize)
                XCTAssertEqual(next.zoom, 1.6 * factor, accuracy: 1e-9)
                let after = fit.viewed(next, in: size).screen(under)
                XCTAssertEqual(after.x, anchor.x, accuracy: 1e-6, "anchor \(anchor), factor \(factor)")
                XCTAssertEqual(after.y, anchor.y, accuracy: 1e-6, "anchor \(anchor), factor \(factor)")
            }
        }
    }

    func testZoomingAboutTheCentreMatchesTheButtons() throws {
        let fit = try fitMap()
        let start = DraftViewport().zoomed(by: 2)
            .panned(by: CGSize(width: -30, height: 12), in: size, content: fit.contentSize)
        let centre = CGPoint(x: size.width / 2, y: size.height / 2)
        for factor in [DraftViewport.step, 1 / DraftViewport.step, 3.0] {
            let anchored = start.zoomed(by: factor, keeping: centre, in: size, content: fit.contentSize)
            let button = start.zoomed(by: factor)
            XCTAssertEqual(anchored.zoom, button.zoom, accuracy: 1e-9)
            XCTAssertEqual(anchored.pan.width, button.pan.width, accuracy: 1e-9)
            XCTAssertEqual(anchored.pan.height, button.pan.height, accuracy: 1e-9)
        }
    }

    func testAnchoredZoomKeepsTheZoomAndPanLimits() throws {
        let fit = try fitMap()
        let corner = CGPoint(x: 0, y: 0)
        let most = DraftViewport().zoomed(by: 1e9, keeping: corner, in: size, content: fit.contentSize)
        XCTAssertEqual(most.zoom, DraftViewport.maxZoom)
        // (content * zoom + size) / 2 - keepVisible: the pan never goes past the usual bounds.
        let limitX = (255 * DraftViewport.maxZoom + 390) / 2 - DraftViewport.keepVisible
        let limitY = (170 * DraftViewport.maxZoom + 230) / 2 - DraftViewport.keepVisible
        XCTAssertEqual(most.pan.width, limitX, accuracy: 1e-6)
        XCTAssertEqual(most.pan.height, limitY, accuracy: 1e-6)
        // Already at the top: a further pinch changes nothing.
        let same = most.zoomed(by: 2, keeping: CGPoint(x: 120, y: 30), in: size, content: fit.contentSize)
        XCTAssertEqual(same.zoom, most.zoom)
        XCTAssertEqual(same.pan.width, most.pan.width, accuracy: 1e-6)
        XCTAssertEqual(same.pan.height, most.pan.height, accuracy: 1e-6)
        // Back to fit clears the pan, wherever the fingers are.
        XCTAssertEqual(most.zoomed(by: 1e-9, keeping: corner, in: size, content: fit.contentSize), DraftViewport())
        XCTAssertEqual(DraftViewport().zoomed(by: 0.5, keeping: corner, in: size, content: fit.contentSize),
                       DraftViewport())
        XCTAssertTrue(DraftViewport().zoomed(by: 1, keeping: corner, in: size, content: fit.contentSize).isFit)
    }

    func testAnchoredZoomIgnoresBrokenInput() throws {
        let fit = try fitMap()
        let start = DraftViewport().zoomed(by: 3)
        let anchor = CGPoint(x: 40, y: 40)
        for factor in [Double.nan, Double.infinity, 0, -2] {
            XCTAssertEqual(start.zoomed(by: factor, keeping: anchor, in: size, content: fit.contentSize), start)
        }
        for bad in [CGPoint(x: CGFloat.nan, y: 0), CGPoint(x: 0, y: CGFloat.infinity)] {
            XCTAssertEqual(start.zoomed(by: 2, keeping: bad, in: size, content: fit.contentSize), start)
        }
    }

    // MARK: Keeping the zoom buttons' corner free

    private let phone = CGSize(width: 361, height: 230)
    private let buttons = CGSize(width: 106, height: 56)

    func testASmallDrawingThatMissesTheButtonsIsNotMoved() throws {
        let plain = try XCTUnwrap(DraftCanvasMap(size: phone, minX: 0, maxX: 100, minY: 0, maxY: 150, inset: 30))
        let kept = try XCTUnwrap(DraftCanvasMap(size: phone, minX: 0, maxX: 100, minY: 0, maxY: 150, inset: 30,
                                                keepingClear: buttons))
        for point in [p(0, 0), p(100, 150), p(100, 0)] {
            XCTAssertEqual(kept.screen(point).x, plain.screen(point).x, accuracy: 1e-9)
            XCTAssertEqual(kept.screen(point).y, plain.screen(point).y, accuracy: 1e-9)
        }
        let none = try XCTUnwrap(DraftCanvasMap(size: phone, minX: 0, maxX: 300, minY: 0, maxY: 200, inset: 30,
                                                keepingClear: .zero))
        let wide = try XCTUnwrap(DraftCanvasMap(size: phone, minX: 0, maxX: 300, minY: 0, maxY: 200, inset: 30))
        XCTAssertEqual(none.pointsPerMillimetre, wide.pointsPerMillimetre, accuracy: 1e-12)
    }

    func testALargeDrawingGivesUpTheCheaperStrip() throws {
        // 300 x 200 mm reaches under the buttons. A free strip on the right costs less scale
        // (225 / 300 = 0.75) than one at the bottom (144 / 200 = 0.72).
        let tall = try XCTUnwrap(DraftCanvasMap(size: phone, minX: 0, maxX: 300, minY: 0, maxY: 200, inset: 30,
                                                keepingClear: buttons))
        XCTAssertEqual(tall.pointsPerMillimetre, 0.75, accuracy: 1e-9)
        XCTAssertFalse(tall.overlapsCorner(width: 106, height: 56, in: phone))
        // 300 x 120 mm only just reaches the buttons; a bottom strip keeps the full width scale.
        let plainLow = try XCTUnwrap(DraftCanvasMap(size: phone, minX: 0, maxX: 300, minY: 0, maxY: 120, inset: 30))
        XCTAssertTrue(plainLow.overlapsCorner(width: 106, height: 56, in: phone))
        let low = try XCTUnwrap(DraftCanvasMap(size: phone, minX: 0, maxX: 300, minY: 0, maxY: 120, inset: 30,
                                               keepingClear: buttons))
        XCTAssertEqual(low.pointsPerMillimetre, plainLow.pointsPerMillimetre, accuracy: 1e-9)
        XCTAssertFalse(low.overlapsCorner(width: 106, height: 56, in: phone))
    }

    func testNoDrawingShapeEndsUpUnderTheButtons() throws {
        for (spanX, spanY) in [(1.0, 1.0), (10, 1000), (1000, 10), (400, 260), (260, 400), (5000, 3300)] {
            let map = try XCTUnwrap(DraftCanvasMap(size: phone, minX: -50, maxX: -50 + spanX,
                                                   minY: 20, maxY: 20 + spanY, inset: 30, keepingClear: buttons))
            XCTAssertFalse(map.overlapsCorner(width: 106, height: 56, in: phone), "\(spanX) x \(spanY)")
            // The drawing still stays inside the area.
            let topLeft = map.screen(p(-50, 20)), bottomRight = map.screen(p(-50 + spanX, 20 + spanY))
            XCTAssertGreaterThanOrEqual(topLeft.x, 30 - 1e-9)
            XCTAssertLessThanOrEqual(bottomRight.x, phone.width - 30 + 1e-9)
            XCTAssertGreaterThanOrEqual(topLeft.y, 30 - 1e-9)
            XCTAssertLessThanOrEqual(bottomRight.y, phone.height - 30 + 1e-9)
        }
    }

    // MARK: Grid

    func testGridStepFollowsTheZoom() throws {
        let fitted = try XCTUnwrap(DraftGrid(pointsPerMillimetre: 1.1333))
        XCTAssertEqual(fitted.step, 50)
        XCTAssertEqual(fitted.majorEvery, 2, "major lines every 100 mm")
        let closer = try XCTUnwrap(DraftGrid(pointsPerMillimetre: 2.9))
        XCTAssertEqual(closer.step, 5)
        XCTAssertEqual(closer.majorEvery, 2, "major lines every 10 mm")
        let closest = try XCTUnwrap(DraftGrid(pointsPerMillimetre: 12))
        XCTAssertEqual(closest.step, 1)
        XCTAssertEqual(closest.majorEvery, 5)
        let far = try XCTUnwrap(DraftGrid(pointsPerMillimetre: 0.17))
        XCTAssertEqual(far.step, 100)
        XCTAssertEqual(far.majorEvery, 5)
    }

    func testGridSquaresAreNeverTooSmallAndNeverNeedlesslyLarge() throws {
        var previousStep = Double.infinity
        for ppm in stride(from: 0.01, through: 40, by: 0.037) {
            let grid = try XCTUnwrap(DraftGrid(pointsPerMillimetre: ppm))
            XCTAssertGreaterThanOrEqual(grid.step * ppm, DraftGrid.minimumSpacing - 1e-9)
            if grid.step > 1 {
                // The step before it in 1, 5, 10, 50 … would have been too small.
                let smaller = grid.step / (grid.majorEvery == 5 ? 2 : 5)
                XCTAssertLessThan(smaller * ppm, DraftGrid.minimumSpacing, "ppm \(ppm)")
            }
            XCTAssertLessThanOrEqual(grid.step, previousStep, "zooming in never makes squares larger")
            previousStep = grid.step
        }
        for bad in [0, -1, Double.nan, Double.infinity] {
            XCTAssertNil(DraftGrid(pointsPerMillimetre: bad))
        }
        let grid = try XCTUnwrap(DraftGrid(pointsPerMillimetre: 2.9))
        XCTAssertTrue(grid.isMajor(0))
        XCTAssertTrue(grid.isMajor(-4))
        XCTAssertFalse(grid.isMajor(3))
    }
}
