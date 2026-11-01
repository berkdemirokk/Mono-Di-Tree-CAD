import XCTest
@testable import TekstilCADViewer_iOS

final class ContourViewportTests: XCTestCase {
    private func p(_ x: Double, _ y: Double) -> ContourMeasure.Point { .init(x: x, y: y) }

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
}
