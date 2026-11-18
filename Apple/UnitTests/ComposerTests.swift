import XCTest
@testable import TekstilCADViewer

final class ComposerTests: XCTestCase {
    private func p(_ x: Double, _ y: Double) -> DraftPoint { DraftPoint(x: x, y: y) }

    func testDefaultAreaIsOfferedAndSorted() {
        XCTAssertTrue(ComposerArea.sizes.contains(ComposerArea.defaultSize))
        XCTAssertEqual(ComposerArea.defaultSize, 300)
        XCTAssertEqual(ComposerArea.sizes, ComposerArea.sizes.sorted())
    }

    func testPointsOnTheBorderFitAndOutsidePointsAreCounted() {
        let points = [p(0, 0), p(100, 100), p(150, 20), p(20, 300)]
        XCTAssertEqual(ComposerArea.outsideCount(points, extent: 300), 0)
        XCTAssertEqual(ComposerArea.outsideCount(points, extent: 100), 2)
        XCTAssertEqual(ComposerArea.outsideCount(points, extent: 1000), 0)
        XCTAssertFalse(ComposerArea.contains(p(-0.1, 5), extent: 100))
    }

    func testRemovingOutsideKeepsOrderAndMergesNewNeighbours() {
        let points = [p(10, 10), p(200, 10), p(10, 10), p(50, 50)]
        XCTAssertEqual(ComposerArea.removingOutside(points, extent: 100), [p(10, 10), p(50, 50)])
        XCTAssertEqual(ComposerArea.removingOutside([p(150, 150)], extent: 100), [])
    }

    func testClampingMovesPointsToTheNearestEdgeAndMergesRepeats() {
        let points = [p(10, 10), p(250, 40), p(280, 60), p(40, 260)]
        XCTAssertEqual(ComposerArea.clamped(points, extent: 100),
                       [p(10, 10), p(100, 40), p(100, 60), p(40, 100)])
        let repeats = [p(10, 10), p(150, 150), p(200, 300), p(50, 50)]
        XCTAssertEqual(ComposerArea.clamped(repeats, extent: 100), [p(10, 10), p(100, 100), p(50, 50)])
    }

    func testClosedShapeNeverEndsOnItsFirstPoint() {
        // A shape with two corners outside a 100 mm area: clamping moves the last corner onto the
        // first one, which a closed shape must not keep as a duplicate.
        let square = [p(100, 100), p(150, 50), p(50, 50), p(150, 150)]
        let openResult = ComposerArea.clamped(square, extent: 100)
        XCTAssertEqual(openResult.last, openResult.first, "An open line keeps its end point")
        let closedResult = ComposerArea.clamped(square, extent: 100, closed: true)
        XCTAssertEqual(closedResult, [p(100, 100), p(100, 50), p(50, 50)])
        XCTAssertNotEqual(closedResult.last, closedResult.first)

        let tail = [p(10, 10), p(50, 50), p(10, 10)]
        XCTAssertEqual(ComposerArea.mergingRepeats(tail, closed: true), [p(10, 10), p(50, 50)])
        XCTAssertEqual(ComposerArea.mergingRepeats(tail), tail)
        XCTAssertEqual(ComposerArea.removingOutside(tail + [p(300, 5)], extent: 100, closed: true),
                       [p(10, 10), p(50, 50)])
    }

    func testGridAndLabelStepsDivideEachArea() {
        for extent in ComposerArea.sizes {
            let grid = ComposerArea.gridStep(extent: extent)
            let label = ComposerArea.labelStep(extent: extent)
            XCTAssertEqual((extent / grid).truncatingRemainder(dividingBy: 1), 0, "\(extent)")
            XCTAssertEqual((extent / label).truncatingRemainder(dividingBy: 1), 0, "\(extent)")
            XCTAssertLessThanOrEqual(extent / grid, 30, "Grid stays light at \(extent) mm")
            XCTAssertLessThanOrEqual(extent / label, 4, "Few numbers on the edge at \(extent) mm")
        }
        XCTAssertEqual(ComposerArea.name(1000), "1000 × 1000 mm")
    }
}
