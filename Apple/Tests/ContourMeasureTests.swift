import XCTest
@testable import TekstilCADViewer_iOS

final class ContourMeasureTests: XCTestCase {
    private func p(_ x: Double, _ y: Double) -> ContourMeasure.Point { .init(x: x, y: y) }

    func testCollapsedRequiresMultipleIdenticalPoints() {
        XCTAssertFalse(ContourMeasure.isCollapsed([]))
        XCTAssertFalse(ContourMeasure.isCollapsed([p(1, 2)]))
        XCTAssertTrue(ContourMeasure.isCollapsed([p(1, 2), p(1, 2), p(1, 2)]))
        XCTAssertFalse(ContourMeasure.isCollapsed([p(1, 2), p(1, 2), p(1, 3)]))
    }

    func testClosedContourRequiresThreeDistinctVerticesBeyondClosingAlias() {
        XCTAssertFalse(ContourMeasure.hasMinimumContourPoints([p(0, 0), p(0, 0), p(1, 0)], closed: true))
        XCTAssertFalse(ContourMeasure.hasMinimumContourPoints([p(0, 0), p(1, 0), p(1, 0), p(0, 0)], closed: true))
        XCTAssertTrue(ContourMeasure.hasMinimumContourPoints([p(0, 0), p(1, 0), p(0, 1), p(0, 0)], closed: true))
        XCTAssertTrue(ContourMeasure.hasMinimumContourPoints([p(0, 0), p(0, 0)], closed: false))
    }

    func testOpenClosedAndExplicitClosingPoint() throws {
        let triangle = [p(0, 0), p(3, 0), p(3, 4)]
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.length(triangle, closed: false)), 7, accuracy: 1e-12)
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.length(triangle, closed: true)), 12, accuracy: 1e-12)
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.length(triangle + [p(0, 0)], closed: true)), 12, accuracy: 1e-12)
    }

    func testZeroTwoPointsAndFiniteLimits() throws {
        XCTAssertEqual(ContourMeasure.length([], closed: true), 0)
        XCTAssertEqual(ContourMeasure.length([p(1, 1), p(1, 1)], closed: false), 0)
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.length([p(0, 0), p(3, 4)], closed: true)), 10, accuracy: 1e-12)
        XCTAssertNotNil(ContourMeasure.length([p(1e99, 0), p(-1e99, 0)], closed: false))
        XCTAssertNil(ContourMeasure.length([p(.infinity, 0)], closed: false))
        XCTAssertNil(ContourMeasure.length([p(-Double.greatestFiniteMagnitude, 0), p(Double.greatestFiniteMagnitude, 0)], closed: false))
    }

    func testTenThousandPoints() throws {
        let points = (0..<10_000).map { p(Double($0), 0) }
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.length(points, closed: false)), 9_999, accuracy: 1e-9)
    }
}
