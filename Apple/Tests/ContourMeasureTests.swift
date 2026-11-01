import XCTest
@testable import TekstilCADViewer_iOS

final class ContourMeasureTests: XCTestCase {
    private func p(_ x: Double, _ y: Double) -> ContourMeasure.Point { .init(x: x, y: y) }

    func testComparisonDifferenceIsSignedAndRejectsUnavailableOrOverflow() {
        XCTAssertEqual(ContourMeasure.difference(first: 100, second: 50), -50)
        XCTAssertEqual(ContourMeasure.difference(first: 50, second: 100), 50)
        XCTAssertNil(ContourMeasure.difference(first: nil, second: 100))
        XCTAssertNil(ContourMeasure.difference(first: .infinity, second: 100))
        XCTAssertNil(ContourMeasure.difference(first: -Double.greatestFiniteMagnitude, second: Double.greatestFiniteMagnitude))
    }
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

    func testStraightDistanceUsesSelectedFileIndicesAndRejectsInvalidValues() throws {
        let points = [p(0, 0), p(3, 4), p(0, 0)]
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.distance(points, from: 0, to: 1)), 5, accuracy: 1e-12)
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.distance(points, from: 0, to: 2)), 0, accuracy: 1e-12)
        XCTAssertNil(ContourMeasure.distance(points, from: -1, to: 0))
        XCTAssertNil(ContourMeasure.distance(points, from: 0, to: 3))
        XCTAssertNil(ContourMeasure.distance([p(.infinity, 0), p(0, 0)], from: 0, to: 1))
        XCTAssertNil(ContourMeasure.distance([p(-Double.greatestFiniteMagnitude, 0), p(Double.greatestFiniteMagnitude, 0)], from: 0, to: 1))
    }
    func testHighlightedPathUsesSameOpenAndClosedVertexOrder() throws {
        let triangle = [p(0, 0), p(3, 0), p(3, 4)]
        XCTAssertEqual(ContourMeasure.pathIndices(triangle, from: 2, to: 0, closed: false), [0, 1, 2])
        XCTAssertEqual(ContourMeasure.pathIndices(triangle, from: 0, to: 1, closed: true), [0, 1])
        XCTAssertEqual(ContourMeasure.pathIndices(triangle, from: 0, to: 1, closed: true, forward: false), [1, 2, 0])
        XCTAssertEqual(ContourMeasure.pathIndices(triangle + [p(0, 0)], from: 3, to: 1, closed: true), [0, 1])
        XCTAssertEqual(ContourMeasure.pathIndices(triangle + [p(0, 0)], from: 3, to: 0, closed: true), [0])
        XCTAssertNil(ContourMeasure.pathIndices(triangle, from: 3, to: 0, closed: true))
    }
    func testPathBetweenVerticesHandlesOpenClosedAndClosingAlias() throws {
        let triangle = [p(0, 0), p(3, 0), p(3, 4)]
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.pathLength(triangle, from: 2, to: 0, closed: false)), 7, accuracy: 1e-12)
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.pathLength(triangle, from: 0, to: 1, closed: true)), 3, accuracy: 1e-12)
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.pathLength(triangle, from: 0, to: 1, closed: true, forward: false)), 9, accuracy: 1e-12)
        let repeated = triangle + [p(0, 0)]
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.pathLength(repeated, from: 3, to: 1, closed: true)), 3, accuracy: 1e-12)
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.pathLength(repeated, from: 3, to: 0, closed: true, forward: false)), 0, accuracy: 1e-12)
        XCTAssertNil(ContourMeasure.pathLength(triangle, from: -1, to: 0, closed: true))
        XCTAssertNil(ContourMeasure.pathLength([p(.infinity, 0), p(0, 0)], from: 0, to: 0, closed: false))
        XCTAssertNil(ContourMeasure.pathLength([p(0, 0), p(0, 0)], from: 0, to: 1, closed: true))
    }
    func testTenThousandPoints() throws {
        let points = (0..<10_000).map { p(Double($0), 0) }
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.length(points, closed: false)), 9_999, accuracy: 1e-9)
    }
}
