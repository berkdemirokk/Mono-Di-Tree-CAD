import XCTest
@testable import TekstilCADViewer

final class DraftContourGeometryTests: XCTestCase {
    private func p(_ x: Double, _ y: Double) -> DraftPoint { DraftPoint(x: x, y: y) }

    func testConcaveContourAndBothWindings() {
        let shape = [p(0, 0), p(6, 0), p(6, 6), p(3, 3), p(0, 6)]
        XCTAssertNil(DraftContourGeometry.validationError(points: shape, closed: true))
        XCTAssertNil(DraftContourGeometry.validationError(points: Array(shape.reversed()), closed: true))
    }

    func testOpenBowTieRejectedAndValidOpenPolylineRetained() {
        let bow = [p(0, 0), p(5, 5), p(0, 5), p(5, 0)]
        XCTAssertNotNil(DraftContourGeometry.validationError(points: bow, closed: false))
        XCTAssertNotNil(DraftContourGeometry.validationError(points: bow, closed: true))
        let path = [p(0, 0), p(0, 10), p(5, 10), p(5, 5)]
        XCTAssertNil(DraftContourGeometry.validationError(points: path, closed: false))
    }

    func testNonadjacentTouchAndAdjacentRetracing() {
        XCTAssertNotNil(DraftContourGeometry.validationError(points: [p(0,0), p(5,0), p(5,3), p(2,0)], closed: false))
        XCTAssertNotNil(DraftContourGeometry.validationError(points: [p(0,0), p(0.0001,0), p(0.00005,0)], closed: false))
        XCTAssertNil(DraftContourGeometry.validationError(points: [p(0,0), p(0.0001,0), p(0.00005,0.000001)], closed: false))
        XCTAssertNil(DraftContourGeometry.validationError(points: [p(0,0), p(2,0), p(5,0)], closed: false))
    }

    func testDegenerateAndOutOfRangeCoordinatesRejected() {
        XCTAssertNotNil(DraftContourGeometry.validationError(points: [p(1,1), p(1,1)], closed: false))
        XCTAssertNotNil(DraftContourGeometry.validationError(points: [p(1,1), p(2,2), p(3,3)], closed: true))
        for invalid in [Double.nan, Double.infinity, -100_000_001, 100_000_001] {
            XCTAssertNotNil(DraftContourGeometry.validationError(points: [p(1,1), p(invalid,4)], closed: false))
        }
        let overLimit = (0...DraftContourGeometry.pointLimit).map { p(Double($0), 0) }
        XCTAssertNotNil(DraftContourGeometry.validationError(points: overLimit, closed: false))
    }

    func testTinyShapeAtLargeCoordinatesKeepsItsArea() {
        let v = 99_999_999.0
        let square = [p(v,v), p(v + 0.01,v), p(v + 0.01,v + 0.01), p(v,v + 0.01)]
        XCTAssertNil(DraftContourGeometry.validationError(points: square, closed: true))
    }

    func testMapRoundTripAndTouchTarget() throws {
        let map = try XCTUnwrap(DraftCanvasMap(size: CGSize(width: 500, height: 300),
                                               minX: -180, maxX: 120, minY: 10, maxY: 260, inset: 15))
        let vertices = [p(-180,10), p(120,260), p(20,100)]
        for (index, point) in vertices.enumerated() {
            let screen = map.screen(point)
            let mapped = map.model(at: screen)
            XCTAssertEqual(mapped.x, point.x, accuracy: 1e-9)
            XCTAssertEqual(mapped.y, point.y, accuracy: 1e-9)
            XCTAssertEqual(map.nearestVertex(to: CGPoint(x: screen.x + 18, y: screen.y), points: vertices), index)
            XCTAssertNil(map.nearestVertex(to: CGPoint(x: screen.x + 23, y: screen.y), points: vertices))
        }
        XCTAssertLessThan(map.screen(vertices[1]).y, map.screen(vertices[0]).y)
    }

    func testComposerSnapsInModelSpaceAndRejectsOutsideBorder() throws {
        let map = try XCTUnwrap(DraftCanvasMap(size: CGSize(width: 340, height: 500),
                                               minX: 0, maxX: 300, minY: 0, maxY: 300, inset: 12))
        let screen = map.screen(p(12.48, 18.22))
        XCTAssertEqual(map.sketchPoint(at: screen, extent: 300, snapToFive: true), p(10,20))
        let fine = try XCTUnwrap(map.sketchPoint(at: screen, extent: 300, snapToFive: false))
        XCTAssertEqual(fine.x, 12.5, accuracy: 1e-9)
        XCTAssertEqual(fine.y, 18.2, accuracy: 1e-9)
        XCTAssertNil(map.sketchPoint(at: map.screen(p(302,10)), extent: 300, snapToFive: true))
    }

    func testPrecisionEntryAcceptsTurkishDecimalSeparator() {
        XCTAssertEqual(DraftContourGeometry.coordinate(" -12,34567 "), -12.34567)
        for bad in ["NaN", "inf", "100000001", "3,4,5"] {
            XCTAssertNil(DraftContourGeometry.coordinate(bad))
        }
    }
}
