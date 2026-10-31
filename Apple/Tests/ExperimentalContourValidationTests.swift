import XCTest
@testable import TekstilCADViewer_iOS

final class ExperimentalContourValidationTests: XCTestCase {
    private func p(_ x: Double, _ y: Double) -> ExperimentalContourValidation.Point { .init(x: x, y: y) }
    func testOpenAndClosedContoursAndExplicitClosure() {
        let triangle = [p(0, 0), p(4, 0), p(0, 4)]
        XCTAssertNoThrow(try ExperimentalContourValidation.validate(triangle, closed: false))
        XCTAssertNoThrow(try ExperimentalContourValidation.validate(triangle, closed: true))
        XCTAssertNoThrow(try ExperimentalContourValidation.validate(triangle + [triangle[0]], closed: true))
        XCTAssertThrowsError(try ExperimentalContourValidation.validate([p(0, 0)], closed: false))
        XCTAssertThrowsError(try ExperimentalContourValidation.validate([p(0, 0), p(1, 0)], closed: true))
    }
    func testCoordinateAndLengthBoundaries() {
        let max = ExperimentalContourValidation.maximumCoordinate
        XCTAssertNoThrow(try ExperimentalContourValidation.validate([p(-max, 0), p(max, 0)], closed: false))
        XCTAssertThrowsError(try ExperimentalContourValidation.validate([p(0, 0), p(max + 1, 0)], closed: false))
        XCTAssertThrowsError(try ExperimentalContourValidation.validate([p(0, 0), p(.infinity, 0)], closed: false))
        let e = ExperimentalContourValidation.epsilon
        XCTAssertThrowsError(try ExperimentalContourValidation.validate([p(0, 0), p(e, 0)], closed: false))
        XCTAssertNoThrow(try ExperimentalContourValidation.validate([p(0, 0), p(e * 2, 0)], closed: false))
        XCTAssertThrowsError(try ExperimentalContourValidation.validate(Array(repeating: p(0, 0), count: 4097), closed: false))
    }
    func testCrossingsOverlapAndArea() {
        XCTAssertThrowsError(try ExperimentalContourValidation.validate([p(0, 0), p(2, 2), p(0, 2), p(2, 0)], closed: false))
        XCTAssertThrowsError(try ExperimentalContourValidation.validate([p(0, 0), p(2, 0), p(1, 0)], closed: false))
        XCTAssertThrowsError(try ExperimentalContourValidation.validate([p(0, 0), p(1, 0), p(2, 0)], closed: true))
        XCTAssertThrowsError(try ExperimentalContourValidation.validate([p(0, 0), p(1, 0), p(1, 1), p(1, 0)], closed: true))
        let e = ExperimentalContourValidation.epsilon
        XCTAssertThrowsError(try ExperimentalContourValidation.validate([p(0, 0), p(1, 0), p(0, e)], closed: true))
        XCTAssertNoThrow(try ExperimentalContourValidation.validate([p(0, 0), p(1, 0), p(0, e * 4)], closed: true))
    }

    // Expected outcomes follow Geometry.HasSelfIntersections/ValidateContour;
    // these fixtures are not evidence of a completed cross-platform test run.
    func testDesktopPredicateFixtures() {
        let cases: [(String, [ExperimentalContourValidation.Point], Bool, Bool)] = [
            ("adjacent straight continuation", [p(0, 0), p(1, 0), p(2, 0)], false, true),
            ("adjacent reversal", [p(0, 0), p(2, 0), p(1, 0)], false, false),
            ("nonadjacent endpoint touch", [p(0, 0), p(2, 0), p(2, 2), p(1, 0)], false, false),
            ("nonadjacent collinear overlap", [p(0, 0), p(3, 0), p(3, 1), p(1, 0), p(2, 0)], false, false),
            ("disjoint open path", [p(0, 0), p(2, 0), p(2, 2), p(0, 2)], false, true),
            ("clockwise polygon", [p(0, 0), p(0, 2), p(2, 2), p(2, 0)], true, true),
            ("translated explicit closure", [p(99_999_990, 99_999_990), p(99_999_999, 99_999_990), p(99_999_990, 99_999_999), p(99_999_990, 99_999_990)], true, true),
            ("repeated interior vertex", [p(0, 0), p(2, 0), p(2, 2), p(2, 0), p(0, 2)], true, false),
            ("zero edge", [p(0, 0), p(0, 0), p(1, 1)], false, false)
        ]
        for (name, points, closed, accepted) in cases {
            if accepted { XCTAssertNoThrow(try ExperimentalContourValidation.validate(points, closed: closed), name) }
            else { XCTAssertThrowsError(try ExperimentalContourValidation.validate(points, closed: closed), name) }
        }
    }

    func testConservativeStoredPointLimit() {
        // Even an explicit closure at 4097 stored points is intentionally rejected:
        // desktop point editing delegates that size to its unported large validator.
        let points = (0..<4096).map { p(Double($0), 0) }
        XCTAssertNoThrow(try ExperimentalContourValidation.validate(points, closed: false))
        XCTAssertThrowsError(try ExperimentalContourValidation.validate(points + [p(0, 0)], closed: true))
    }

    func testDetachedPointMovePreservesClosureAndRejectsInvalidResult() throws {
        let source = [p(0, 0), p(4, 0), p(0, 4), p(0, 0)]
        let moved = try ExperimentalContourValidation.movingPoint(source, closed: true, index: 3, to: p(1, 1))
        XCTAssertEqual(moved, [p(1, 1), p(4, 0), p(0, 4), p(1, 1)])
        XCTAssertEqual(source[0], p(0, 0))
        XCTAssertThrowsError(try ExperimentalContourValidation.movingPoint(source, closed: true, index: 4, to: p(0, 0)))
        XCTAssertThrowsError(try ExperimentalContourValidation.movingPoint(source, closed: true, index: 1, to: p(0, 4)))
        XCTAssertThrowsError(try ExperimentalContourValidation.movingPoint(source, closed: true, index: 1, to: p(.infinity, 0)))
    }
}
