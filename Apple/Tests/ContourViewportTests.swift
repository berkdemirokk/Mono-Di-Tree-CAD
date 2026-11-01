import XCTest
@testable import TekstilCADViewer_iOS

final class ContourViewportTests: XCTestCase {
    private func p(_ x: Double, _ y: Double) -> ContourMeasure.Point { .init(x: x, y: y) }

    func testShiftInputSnapshotRejectsChangesDuringValidation() {
        let numeric = ContourOverlay.ShiftInputSnapshot(first: "1", second: "-2.5")
        XCTAssertTrue(numeric.matches("1", "-2.5"))
        XCTAssertFalse(numeric.matches("2", "-2.5"))
        XCTAssertFalse(numeric.matches("1", "-2,5"))
        let anchors = ContourOverlay.ShiftInputSnapshot(first: "0", second: "1")
        XCTAssertFalse(anchors.matches("0", "2"))
    }
    func testAnchorAlignmentRequiresExactBoundedHalfMillimeterOffset() throws {
        let a = try XCTUnwrap(ContourBounds(points: [p(0, 0), p(10, 10)]))
        let b = try XCTUnwrap(ContourBounds(points: [p(-10, 0), p(0, 10)]))
        let target = try XCTUnwrap(ContourOverlay.anchorOffset(first: p(5.5, 2), firstBounds: a,
                                                               second: p(-9, 0), secondBounds: b))
        XCTAssertEqual(target.x, 4.5); XCTAssertEqual(target.y, 2)
        XCTAssertNil(ContourOverlay.anchorOffset(first: p(5.25, 2), firstBounds: a,
                                                  second: p(-9, 0), secondBounds: b))
        let decimals = try XCTUnwrap(ContourBounds(points: [p(-0.1, 0), p(0.3, 1)]))
        XCTAssertNil(ContourOverlay.anchorOffset(first: p(0.3, 0), firstBounds: decimals,
                                                  second: p(-10, 0), secondBounds: b))
        XCTAssertNil(ContourOverlay.anchorOffset(first: p(.infinity, 0), firstBounds: a,
                                                  second: p(-9, 0), secondBounds: b))
    }
    func testNumericOverlayShiftAcceptsExactlyRepresentableHalfMillimeters() {
        XCTAssertEqual(ContourOverlay.halfMillimeterShift("  -10000  "), -10_000)
        XCTAssertEqual(ContourOverlay.halfMillimeterShift("+250"), 250)
        XCTAssertEqual(ContourOverlay.halfMillimeterShift("-0,5"), -0.5)
        XCTAssertEqual(ContourOverlay.halfMillimeterShift("9999.5"), 9999.5)
        XCTAssertEqual(ContourOverlay.halfMillimeterShift("0.0"), 0)
        XCTAssertNil(ContourOverlay.halfMillimeterShift("10000.5"))
        XCTAssertNil(ContourOverlay.halfMillimeterShift("-10001"))
        XCTAssertNil(ContourOverlay.halfMillimeterShift("1.25"))
        XCTAssertNil(ContourOverlay.halfMillimeterShift("1,5.0"))
        XCTAssertNil(ContourOverlay.halfMillimeterShift("1e3"))
        XCTAssertNil(ContourOverlay.halfMillimeterShift("NaN"))
        XCTAssertNil(ContourOverlay.halfMillimeterShift(String(repeating: "9", count: 1000)))
    }
    func testOverlayErrorBoundStopsWhenCancelled() throws {
        let points = (0..<10_000).map { p(Double($0), 0) }
        let bounds = try XCTUnwrap(ContourBounds(points: points))
        var checks = 0
        let result = ContourOverlay.errorBound(first: points, firstBounds: bounds, second: points, secondBounds: bounds) {
            checks += 1
            return checks >= 50
        }
        XCTAssertNil(result)
        XCTAssertEqual(checks, 50)
    }
    func testOverlayBoundAcceptsOrdinaryDecimalsButRejectsLargeAndAccumulatedErrors() throws {
        let ordinaryPoints = [p(-0.1, 0), p(0.3, 1)]
        let ordinary = try XCTUnwrap(ContourBounds(points: ordinaryPoints))
        let error = try XCTUnwrap(ContourOverlay.errorBound(first: ordinaryPoints, firstBounds: ordinary,
                                                            second: ordinaryPoints, secondBounds: ordinary))
        XCTAssertGreaterThan(error, 0)
        XCTAssertLessThanOrEqual(error, ContourOverlay.maximumError)
        let roundedPoints = [p(0, 0), p(9_007_199_254_740_994, 1)]
        let rounded = try XCTUnwrap(ContourBounds(points: roundedPoints))
        XCTAssertNotEqual(rounded.maxX + 1, rounded.maxX) // rounds by 2 mm
        XCTAssertNil(ContourOverlay.errorBound(first: ordinaryPoints, firstBounds: ordinary,
                                               second: roundedPoints, secondBounds: rounded, dx: 1))
        let interiorPoints = [p(0, 0), p(9_007_199_254_740_991, 0), p(9_007_199_254_740_992, 1)]
        let interior = try XCTUnwrap(ContourBounds(points: interiorPoints))
        XCTAssertNil(ContourOverlay.errorBound(first: ordinaryPoints, firstBounds: ordinary,
                                               second: interiorPoints, secondBounds: interior, dx: 2))
        let few = [p(0, 0), p(1e12, 0)]
        let many = [p(0, 0)] + Array(repeating: p(1e12, 0), count: 100)
        let large = try XCTUnwrap(ContourBounds(points: few))
        XCTAssertNotNil(ContourOverlay.errorBound(first: ordinaryPoints, firstBounds: ordinary,
                                                  second: few, secondBounds: large, dx: 0.0001))
        XCTAssertNil(ContourOverlay.errorBound(first: ordinaryPoints, firstBounds: ordinary,
                                               second: many, secondBounds: large, dx: 0.0001))
    }
    func testOverlayTranslationChangesOnlySecondRelativePositionAndFits() throws {
        let a = try XCTUnwrap(ContourBounds(points: [p(100, 20), p(200, 70)]))
        let b = try XCTUnwrap(ContourBounds(points: [p(-70, 10), p(-20, 60)]))
        let bounds = try XCTUnwrap(ContourOverlay.bounds(a, b, dx: -3, dy: 5))
        XCTAssertEqual(bounds.minX, -3)
        XCTAssertEqual(bounds.maxX, 100)
        XCTAssertEqual(bounds.maxY, 55)
        let originA = try XCTUnwrap(ContourOverlay.aligned(p(100, 20), bounds: a))
        let originB = try XCTUnwrap(ContourOverlay.aligned(p(-70, 10), bounds: b, dx: -3, dy: 5))
        XCTAssertEqual(originA.x, 0); XCTAssertEqual(originA.y, 0)
        XCTAssertEqual(originB.x, -3); XCTAssertEqual(originB.y, 5)
        XCTAssertNil(ContourOverlay.bounds(a, b, dx: .infinity))
        let huge = try XCTUnwrap(ContourBounds(points: [p(0, 0), p(Double.greatestFiniteMagnitude, 1)]))
        XCTAssertNil(ContourOverlay.errorBound(first: [p(100, 20), p(200, 70)], firstBounds: a,
                                               second: [p(0, 0), p(Double.greatestFiniteMagnitude, 1)], secondBounds: huge, dx: 1))
    }
    func testOverlayUsesSharedScaleAndRejectsOverflow() throws {
        let first = try XCTUnwrap(ContourBounds(points: [p(100, 20), p(200, 70)]))
        let second = try XCTUnwrap(ContourBounds(points: [p(-70, 10), p(-20, 60)]))
        let bounds = try XCTUnwrap(ContourOverlay.bounds(first, second))
        let viewport = try XCTUnwrap(ContourViewport(bounds: bounds, width: 232, height: 132))
        let a0 = viewport.position(try XCTUnwrap(ContourOverlay.aligned(p(100, 20), bounds: first)))
        let a1 = viewport.position(try XCTUnwrap(ContourOverlay.aligned(p(200, 70), bounds: first)))
        let b0 = viewport.position(try XCTUnwrap(ContourOverlay.aligned(p(-70, 10), bounds: second)))
        let b1 = viewport.position(try XCTUnwrap(ContourOverlay.aligned(p(-20, 60), bounds: second)))
        XCTAssertEqual(a0, b0)
        XCTAssertEqual(a1.x - a0.x, 2 * (b1.x - b0.x), accuracy: 1e-9)
        let extreme = try XCTUnwrap(ContourBounds(points: [p(-Double.greatestFiniteMagnitude, 0), p(Double.greatestFiniteMagnitude, 0)]))
        XCTAssertNil(ContourOverlay.bounds(extreme, second))
        XCTAssertNil(ContourOverlay.aligned(p(Double.greatestFiniteMagnitude, 0), bounds: extreme))
    }
    func testCombinedPieceAndGrainBoundsMatchAllPoints() throws {
        let groups = [[p(0, 1), p(2, 3)], [p(-20, 9), p(8, -4)], [p(-40, 0), p(60, 0)]]
        let cached = try XCTUnwrap(ContourBounds(combining: groups.compactMap { ContourBounds(points: $0) }))
        let direct = try XCTUnwrap(ContourBounds(points: groups.flatMap { $0 }))
        XCTAssertEqual(cached.minX, direct.minX); XCTAssertEqual(cached.maxX, direct.maxX)
        XCTAssertEqual(cached.minY, direct.minY); XCTAssertEqual(cached.maxY, direct.maxY)
        XCTAssertNil(ContourBounds(combining: []))
    }

    func testCachedBoundsMatchDirectViewportAcrossSizesAndExtremeCoordinates() throws {
        let cases: [[ContourMeasure.Point]] = [
            [p(0, 0), p(20, 30), p(-10, 5)],
            [p(4, 4), p(4, 4)],
            [p(-Double.greatestFiniteMagnitude, 1), p(Double.greatestFiniteMagnitude, -1)],
            [p(0, 0), p(1e-200, 1e-200)]
        ]
        for points in cases {
            let bounds = try XCTUnwrap(ContourBounds(points: points))
            for size in [CGSize(width: 232, height: 132), CGSize(width: 120, height: 450)] {
                let cached = try XCTUnwrap(ContourViewport(bounds: bounds, width: Double(size.width), height: Double(size.height)))
                let direct = try XCTUnwrap(ContourViewport(points: points, width: Double(size.width), height: Double(size.height)))
                for point in points {
                    XCTAssertEqual(cached.position(point).x, direct.position(point).x, accuracy: 1e-9)
                    XCTAssertEqual(cached.position(point).y, direct.position(point).y, accuracy: 1e-9)
                }
            }
        }
        XCTAssertNil(ContourBounds(points: []))
        XCTAssertNil(ContourBounds(points: [p(.nan, 0)]))
    }

    func testRevealPreservesVisiblePositionAndMovesOnlyOffscreenAxes() throws {
        let size = CGSize(width: 200, height: 100)
        let unchanged = try XCTUnwrap(ContourViewport.visibleOffset(point: CGPoint(x: 100, y: 50), size: size, zoom: 2, offset: .zero))
        XCTAssertEqual(unchanged, .zero)
        let shifted = try XCTUnwrap(ContourViewport.visibleOffset(point: CGPoint(x: 190, y: 50), size: size, zoom: 2, offset: .zero))
        XCTAssertEqual(shifted.width, -102, accuracy: 1e-9)
        XCTAssertEqual(shifted.height, 0, accuracy: 1e-9)
        XCTAssertNil(ContourViewport.visibleOffset(point: .zero, size: .zero, zoom: 2, offset: .zero))
    }

    func testRevealAfterGrainVisibilityChangesViewport() throws {
        let size = CGSize(width: 200, height: 100)
        let contour = [p(0, 0), p(100, 0)]
        let withGrain = try XCTUnwrap(ContourViewport(points: contour + [p(-1000, 0), p(1000, 0)], width: 200, height: 100))
        let withoutGrain = try XCTUnwrap(ContourViewport(points: contour, width: 200, height: 100))
        let zoom: CGFloat = 4
        let selected = contour[1]
        let oldOffset = try XCTUnwrap(ContourViewport.visibleOffset(point: withGrain.position(selected), size: size, zoom: zoom, offset: .zero))
        let newPosition = withoutGrain.position(selected)
        let corrected = try XCTUnwrap(ContourViewport.visibleOffset(point: newPosition, size: size, zoom: zoom, offset: oldOffset))
        XCTAssertNotEqual(corrected, oldOffset)
        let visibleX = (newPosition.x - size.width / 2) * zoom + size.width / 2 + corrected.width
        XCTAssertGreaterThanOrEqual(visibleX, 0)
        XCTAssertLessThanOrEqual(visibleX, size.width)
    }
    func testFiniteOppositeExtremeCoordinatesFitWithoutOverflow() throws {
        let points = [p(-Double.greatestFiniteMagnitude, -1),
                      p(Double.greatestFiniteMagnitude, 1)]
        let viewport = try XCTUnwrap(ContourViewport(points: points, width: 232, height: 132))
        let left = viewport.position(points[0]), right = viewport.position(points[1])
        XCTAssertEqual(left.x, 16, accuracy: 1e-9)
        XCTAssertEqual(right.x, 216, accuracy: 1e-9)
        XCTAssertTrue(left.y.isFinite && right.y.isFinite)
    }

    func testTinyContourStillFillsViewport() throws {
        let points = [p(0, 0), p(1e-200, 0), p(1e-200, 1e-200)]
        let viewport = try XCTUnwrap(ContourViewport(points: points, width: 232, height: 132))
        let a = viewport.position(points[0]), b = viewport.position(points[2])
        XCTAssertEqual(b.x - a.x, 100, accuracy: 1e-9)
        XCTAssertEqual(b.y - a.y, 100, accuracy: 1e-9)
    }

    func testOrdinaryTranslatedContourRetainsAspectAndOrientation() throws {
        let points = [p(100, -50), p(300, 50)]
        let viewport = try XCTUnwrap(ContourViewport(points: points, width: 232, height: 132))
        let a = viewport.position(points[0]), b = viewport.position(points[1])
        XCTAssertEqual(a.x, 16, accuracy: 1e-9)
        XCTAssertEqual(b.x, 216, accuracy: 1e-9)
        XCTAssertEqual(a.y, 16, accuracy: 1e-9)
        XCTAssertEqual(b.y, 116, accuracy: 1e-9)
    }

    func testHorizontalVerticalAndDegenerateContours() throws {
        let horizontal = try XCTUnwrap(ContourViewport(points: [p(0, 0), p(1e-300, 0)], width: 232, height: 132))
        XCTAssertEqual(horizontal.position(p(1e-300, 0)).x - horizontal.position(p(0, 0)).x, 200, accuracy: 1e-9)
        let vertical = try XCTUnwrap(ContourViewport(points: [p(0, 0), p(0, 1e-300)], width: 232, height: 132))
        XCTAssertEqual(vertical.position(p(0, 1e-300)).y - vertical.position(p(0, 0)).y, 100, accuracy: 1e-9)
        let collapsed = try XCTUnwrap(ContourViewport(points: [p(1, 1), p(1, 1)], width: 232, height: 132))
        XCTAssertEqual(collapsed.position(p(1, 1)), CGPoint(x: 116, y: 66))
    }

    func testAsymmetricContourUsesDesktopDownwardYAxis() throws {
        let points = [p(0, 0), p(40, 0), p(10, 30)]
        let viewport = try XCTUnwrap(ContourViewport(points: points, width: 232, height: 132))
        XCTAssertEqual(viewport.position(points[0]).y, viewport.position(points[1]).y, accuracy: 1e-9)
        XCTAssertGreaterThan(viewport.position(points[2]).y, viewport.position(points[0]).y)
        XCTAssertLessThan(viewport.position(points[2]).x, viewport.position(points[1]).x)
    }
    func testUnsuccessfulVertexHitExplainsThatOldMeasurementRemains() {
        for hit in [ContourViewport.VertexHit.none, .ambiguous] {
            let notice = hit.selectionNotice ?? ""
            XCTAssertTrue(notice.contains("önceki seçime aittir"))
            XCTAssertTrue(notice.contains("listeden seçin"))
        }
        XCTAssertNil(ContourViewport.VertexHit.vertex(1).selectionNotice)
    }
    func testVertexTapRespectsZoomOffsetAndRejectsAmbiguity() throws {
        let points = [p(0, 0), p(100, 0), p(100, 100)]
        let size = CGSize(width: 232, height: 132)
        let viewport = try XCTUnwrap(ContourViewport(points: points, width: 232, height: 132))
        let base = viewport.position(points[1]), zoom: CGFloat = 2
        let offset = CGSize(width: 20, height: -10)
        let tap = CGPoint(x: (base.x - size.width / 2) * zoom + size.width / 2 + offset.width,
                          y: (base.y - size.height / 2) * zoom + size.height / 2 + offset.height)
        XCTAssertEqual(viewport.vertexHit(points: points, closed: false, tap: tap, size: size, zoom: zoom, offset: offset), .vertex(1))
        XCTAssertEqual(viewport.vertexHit(points: points, closed: false, tap: CGPoint(x: -1000, y: -1000), size: size, zoom: zoom, offset: offset), .none)
        let duplicate = points + [points[0]]
        let first = viewport.position(points[0])
        XCTAssertEqual(viewport.vertexHit(points: duplicate, closed: true, tap: first, size: size, zoom: 1, offset: .zero), .vertex(0))
        XCTAssertEqual(viewport.vertexHit(points: duplicate, closed: false, tap: first, size: size, zoom: 1, offset: .zero), .ambiguous)
        let close = [p(0, 0), p(1, 0), p(100, 0)]
        let closeViewport = try XCTUnwrap(ContourViewport(points: close, width: 232, height: 132))
        XCTAssertEqual(closeViewport.vertexHit(points: close, closed: false, tap: closeViewport.position(close[0]), size: size, zoom: 1, offset: .zero), .ambiguous)
    }
}
