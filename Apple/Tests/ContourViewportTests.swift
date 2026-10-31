import XCTest
@testable import TekstilCADViewer_iOS

final class ContourViewportTests: XCTestCase {
    private func p(_ x: Double, _ y: Double) -> ContourMeasure.Point { .init(x: x, y: y) }

    func testTinyContourStillFillsViewport() throws {
        let points = [p(0, 0), p(1e-200, 0), p(1e-200, 1e-200)]
        let viewport = try XCTUnwrap(ContourViewport(points: points, width: 232, height: 132))
        let a = viewport.position(points[0]), b = viewport.position(points[2])
        XCTAssertEqual(b.x - a.x, 100, accuracy: 1e-9)
        XCTAssertEqual(a.y - b.y, 100, accuracy: 1e-9)
    }

    func testOrdinaryTranslatedContourRetainsAspectAndOrientation() throws {
        let points = [p(100, -50), p(300, 50)]
        let viewport = try XCTUnwrap(ContourViewport(points: points, width: 232, height: 132))
        let a = viewport.position(points[0]), b = viewport.position(points[1])
        XCTAssertEqual(a.x, 16, accuracy: 1e-9)
        XCTAssertEqual(b.x, 216, accuracy: 1e-9)
        XCTAssertEqual(a.y, 116, accuracy: 1e-9)
        XCTAssertEqual(b.y, 16, accuracy: 1e-9)
    }

    func testHorizontalVerticalAndDegenerateContours() throws {
        let horizontal = try XCTUnwrap(ContourViewport(points: [p(0, 0), p(1e-300, 0)], width: 232, height: 132))
        XCTAssertEqual(horizontal.position(p(1e-300, 0)).x - horizontal.position(p(0, 0)).x, 200, accuracy: 1e-9)
        let vertical = try XCTUnwrap(ContourViewport(points: [p(0, 0), p(0, 1e-300)], width: 232, height: 132))
        XCTAssertEqual(vertical.position(p(0, 0)).y - vertical.position(p(0, 1e-300)).y, 100, accuracy: 1e-9)
        XCTAssertNil(ContourViewport(points: [p(1, 1), p(1, 1)], width: 232, height: 132))
    }
}
