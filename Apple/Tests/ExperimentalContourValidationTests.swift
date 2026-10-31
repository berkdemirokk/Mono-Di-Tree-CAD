import XCTest
@testable import TekstilCADViewer

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
}
