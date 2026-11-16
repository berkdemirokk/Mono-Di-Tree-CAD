import XCTest
@testable import TekstilCADViewer

final class PointEditingTests: XCTestCase {
    private func p(_ x: Double, _ y: Double) -> DraftPoint { DraftPoint(x: x, y: y) }
    private var square: [DraftPoint] { [p(0, 0), p(10, 0), p(10, 10), p(0, 10)] }

    private func edit(_ outcome: PointEditing.Outcome,
                      file: StaticString = #filePath, line: UInt = #line) -> PointEditing.Edit? {
        guard case .changed(let edit) = outcome else {
            XCTFail("Expected a change, got \(outcome)", file: file, line: line)
            return nil
        }
        return edit
    }

    private func reason(_ outcome: PointEditing.Outcome,
                        file: StaticString = #filePath, line: UInt = #line) -> String? {
        guard case .refused(let reason) = outcome else {
            XCTFail("Expected a refusal, got \(outcome)", file: file, line: line)
            return nil
        }
        return reason
    }

    // MARK: - Insert

    func testInsertSplitsTheEdgeToTheNextPointAndSelectsTheNewPoint() {
        guard let result = edit(PointEditing.inserting(after: 1, in: square, closed: true)) else { return }
        XCTAssertEqual(result.points, [p(0, 0), p(10, 0), p(10, 5), p(10, 10), p(0, 10)])
        XCTAssertEqual(result.selected, 2)
        XCTAssertNil(DraftContourGeometry.validationError(points: result.points, closed: true))
    }

    func testInsertAfterTheLastPointOfAClosedPieceUsesTheEdgeBackToTheFirst() {
        guard let result = edit(PointEditing.inserting(after: 3, in: square, closed: true)) else { return }
        XCTAssertEqual(result.points, square + [p(0, 5)])
        XCTAssertEqual(result.selected, 4)
    }

    func testInsertOnAnOpenLine() {
        let line = [p(0, 0), p(10, 0)]
        guard let middle = edit(PointEditing.inserting(after: 0, in: line, closed: false)) else { return }
        XCTAssertEqual(middle.points, [p(0, 0), p(5, 0), p(10, 0)])
        XCTAssertEqual(middle.selected, 1)
        // The last point has no next point: the line continues by half its last edge.
        XCTAssertTrue(PointEditing.extendsLine(after: 1, count: 2, closed: false))
        XCTAssertFalse(PointEditing.extendsLine(after: 1, count: 2, closed: true))
        guard let end = edit(PointEditing.inserting(after: 1, in: line, closed: false)) else { return }
        XCTAssertEqual(end.points, [p(0, 0), p(10, 0), p(15, 0)])
        XCTAssertEqual(end.selected, 2)
    }

    func testInsertIsRefusedWhenTheExtensionWouldCrossTheLine() {
        // The last edge points down at the first edge; continuing it would cross it at (5, 0).
        let hook = [p(0, 0), p(10, 0), p(10, 10), p(5, 10), p(5, 2)]
        XCTAssertNil(DraftContourGeometry.validationError(points: hook, closed: false))
        guard let text = reason(PointEditing.inserting(after: 4, in: hook, closed: false)) else { return }
        XCTAssertTrue(text.hasPrefix("Buraya nokta eklenemedi."), text)
    }

    func testInsertRespectsThePointLimit() {
        let limit = DraftContourGeometry.pointLimit
        XCTAssertTrue(PointEditing.canInsert(count: limit - 1))
        XCTAssertFalse(PointEditing.canInsert(count: limit))
        let full = (0..<limit).map { p(Double($0), Double($0 % 2)) }
        XCTAssertEqual(PointEditing.inserting(after: 0, in: full, closed: false), .refused(PointEditing.limitReason))
    }

    func testInsertWithoutAValidSelectionIsRefused() {
        XCTAssertNotNil(reason(PointEditing.inserting(after: 4, in: square, closed: true)))
        XCTAssertNotNil(reason(PointEditing.inserting(after: -1, in: square, closed: true)))
        XCTAssertNil(PointEditing.newPoint(after: 0, in: [p(1, 1)], closed: false))
    }

    // MARK: - Delete

    func testMinimumsMatchTheGeometryRule() {
        XCTAssertEqual(PointEditing.minimumCount(closed: true), 3)
        XCTAssertEqual(PointEditing.minimumCount(closed: false), 2)
        XCTAssertNotNil(DraftContourGeometry.validationError(points: [p(0, 0), p(1, 0)], closed: true))
        XCTAssertNil(DraftContourGeometry.validationError(points: [p(0, 0), p(1, 0), p(0, 1)], closed: true))
        XCTAssertNotNil(DraftContourGeometry.validationError(points: [p(0, 0)], closed: false))
        XCTAssertNil(DraftContourGeometry.validationError(points: [p(0, 0), p(1, 0)], closed: false))
    }

    func testDeleteIsRefusedAtTheMinimum() {
        let triangle = [p(0, 0), p(10, 0), p(0, 10)]
        XCTAssertFalse(PointEditing.canDelete(count: 3, closed: true))
        XCTAssertTrue(PointEditing.canDelete(count: 4, closed: true))
        XCTAssertEqual(PointEditing.deleting(at: 1, in: triangle, closed: true),
                       .refused(PointEditing.minimumReason(closed: true)))
        let line = [p(0, 0), p(10, 0)]
        XCTAssertFalse(PointEditing.canDelete(count: 2, closed: false))
        XCTAssertTrue(PointEditing.canDelete(count: 3, closed: false))
        XCTAssertEqual(PointEditing.deleting(at: 0, in: line, closed: false),
                       .refused(PointEditing.minimumReason(closed: false)))
    }

    func testDeleteSelectsThePointThatTookItsPlace() {
        guard let middle = edit(PointEditing.deleting(at: 1, in: square, closed: true)) else { return }
        XCTAssertEqual(middle.points, [p(0, 0), p(10, 10), p(0, 10)])
        XCTAssertEqual(middle.selected, 1)
        // On a closed piece the point after the last one is the first.
        guard let last = edit(PointEditing.deleting(at: 3, in: square, closed: true)) else { return }
        XCTAssertEqual(last.points, [p(0, 0), p(10, 0), p(10, 10)])
        XCTAssertEqual(last.selected, 0)
        guard let first = edit(PointEditing.deleting(at: 0, in: square, closed: true)) else { return }
        XCTAssertEqual(first.points, [p(10, 0), p(10, 10), p(0, 10)])
        XCTAssertEqual(first.selected, 0)
    }

    func testDeletingTheEndOfAnOpenLineSelectsTheNewEnd() {
        let line = [p(0, 0), p(10, 0), p(10, 10)]
        guard let result = edit(PointEditing.deleting(at: 2, in: line, closed: false)) else { return }
        XCTAssertEqual(result.points, [p(0, 0), p(10, 0)])
        XCTAssertEqual(result.selected, 1)
    }

    func testDeleteIsRefusedWhenTheRestHasNoArea() {
        let shape = [p(0, 0), p(5, 0), p(10, 0), p(5, 5)]
        XCTAssertNil(DraftContourGeometry.validationError(points: shape, closed: true))
        guard let text = reason(PointEditing.deleting(at: 3, in: shape, closed: true)) else { return }
        XCTAssertTrue(text.hasPrefix("Bu nokta silinemedi."), text)
        XCTAssertNotNil(reason(PointEditing.deleting(at: 4, in: shape, closed: true)))
    }

    func testInsertThenDeleteRestoresTheShape() {
        guard let inserted = edit(PointEditing.inserting(after: 2, in: square, closed: true)) else { return }
        guard let restored = edit(PointEditing.deleting(at: inserted.selected, in: inserted.points, closed: true))
        else { return }
        XCTAssertEqual(restored.points, square)
        XCTAssertEqual(restored.selected, 3)
    }
}
