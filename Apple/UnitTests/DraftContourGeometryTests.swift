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
        XCTAssertGreaterThan(map.screen(vertices[1]).y, map.screen(vertices[0]).y)
    }

    func testWindowsContourPlacementAndScreenEditKeepFileCoordinates() throws {
        let data = Data(#"{"Name":"Windows","GradingRules":[],"Pieces":[{"Id":"88E8B892-60A4-4479-B39D-0274F21AFAD4","Name":"Front","Closed":true,"Points":[{"X":0,"Y":0},{"X":100,"Y":0},{"X":30,"Y":50}]}]}"#.utf8)
        let root = try JSONSerialization.jsonObject(with: data)
        var project = try XCTUnwrap(CADSketchImport.loadEditableCopy(from: data, jsonRoot: root))
        let map = try XCTUnwrap(DraftCanvasMap(size: CGSize(width: 220, height: 120),
                                               minX: 0, maxX: 100, minY: 0, maxY: 50, inset: 10))
        // CadCanvas.Screen at two points per millimetre and an offset of (10, 10).
        let expected = [CGPoint(x: 10, y: 10), CGPoint(x: 210, y: 10), CGPoint(x: 70, y: 110)]
        let vertices = project.pieces[0].points
        XCTAssertEqual(vertices.map { map.screen($0) }, expected)
        let preview = vertices.map {
            DrawingProjection.screen(CGPoint(x: $0.x, y: $0.y), minimum: .zero,
                                     offset: CGPoint(x: 10, y: 10), scale: 2)
        }
        XCTAssertEqual(preview, expected)

        let tap = CGPoint(x: 90, y: 80)
        project.pieces[0].points[2] = try XCTUnwrap(map.sketchPoint(at: tap, extent: 100, snapToFive: true))
        let exported = try CADSketchImport.encode(project)
        let document = try XCTUnwrap(JSONSerialization.jsonObject(with: exported) as? [String: Any])
        let pieces = try XCTUnwrap(document["Pieces"] as? [[String: Any]])
        let coordinates = try XCTUnwrap(pieces.first?["Points"] as? [[String: Double]])
        XCTAssertEqual(coordinates, [["X": 0, "Y": 0], ["X": 100, "Y": 0], ["X": 40, "Y": 35]])
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

    func testViewportZoomIsClampedAndFitClearsPan() {
        var view = DraftViewport()
        XCTAssertTrue(view.isFit)
        view = view.zoomed(by: 1e9)
        XCTAssertEqual(view.zoom, DraftViewport.maxZoom)
        XCTAssertFalse(view.canZoomIn)
        view = view.panned(by: CGSize(width: 40, height: -20), in: CGSize(width: 300, height: 200),
                           content: CGSize(width: 200, height: 100))
        XCTAssertEqual(view.pan, CGSize(width: 40, height: -20))
        view = view.zoomed(by: 1e-9)
        XCTAssertTrue(view.isFit, "zooming back out to fit must also clear the pan")
        XCTAssertEqual(view.pan, .zero)
        XCTAssertEqual(DraftViewport().zoomed(by: .nan), DraftViewport())
        XCTAssertEqual(DraftViewport().zoomed(by: 0), DraftViewport())
        XCTAssertEqual(DraftViewport().zoomed(by: -2), DraftViewport())
    }

    func testViewportPanIsBoundedAndIgnoredAtFit() {
        let size = CGSize(width: 300, height: 200)
        let content = CGSize(width: 200, height: 100)
        XCTAssertEqual(DraftViewport().panned(by: CGSize(width: 50, height: 50), in: size, content: content),
                       DraftViewport())
        let zoomed = DraftViewport().zoomed(by: 2)
        let far = zoomed.panned(by: CGSize(width: 1e9, height: -1e9), in: size, content: content)
        // (content * zoom + size) / 2 - keepVisible
        XCTAssertEqual(far.pan.width, (200 * 2 + 300) / 2 - DraftViewport.keepVisible, accuracy: 1e-9)
        XCTAssertEqual(far.pan.height, -((100 * 2 + 200) / 2 - DraftViewport.keepVisible), accuracy: 1e-9)
        XCTAssertEqual(zoomed.panned(by: CGSize(width: CGFloat.infinity, height: 0), in: size, content: content),
                       zoomed)
    }

    func testPannedAwayDrawingStillKeepsPartOfItInView() throws {
        let size = CGSize(width: 390, height: 230)
        let fit = try XCTUnwrap(DraftCanvasMap(size: size, minX: 0, maxX: 100, minY: 0, maxY: 150, inset: 30))
        for zoom in [2.0, 6.0, 24.0] {
            let view = DraftViewport().zoomed(by: zoom)
            let right = view.panned(by: CGSize(width: 1e9, height: 0), in: size, content: fit.contentSize)
            let left = view.panned(by: CGSize(width: -1e9, height: 0), in: size, content: fit.contentSize)
            // Pushed right, the drawing's left edge must still be inside the area; pushed left, its right edge.
            let leftEdge = fit.viewed(right, in: size).screen(DraftPoint(x: 0, y: 75)).x
            let rightEdge = fit.viewed(left, in: size).screen(DraftPoint(x: 100, y: 75)).x
            XCTAssertLessThan(leftEdge, size.width - DraftViewport.keepVisible + 1e-6)
            XCTAssertGreaterThan(rightEdge, DraftViewport.keepVisible - 1e-6)
        }
    }

    func testCenteringBringsAPointToTheMiddleAtAnyZoom() throws {
        let size = CGSize(width: 390, height: 230)
        let fit = try XCTUnwrap(DraftCanvasMap(size: size, minX: 0, maxX: 300, minY: 0, maxY: 200, inset: 30))
        for zoom in [1.6, 4.0, 10.0] {
            let target = p(40, 150)
            let pan = fit.panCentering(target, zoom: zoom, in: size)
            let view = DraftViewport(zoom: zoom, pan: pan)
            let at = fit.viewed(view, in: size).screen(target)
            XCTAssertEqual(at.x, size.width / 2, accuracy: 1e-6)
            XCTAssertEqual(at.y, size.height / 2, accuracy: 1e-6)
        }
    }

    func testExportFileNameFollowsTheProjectNameAndStaysSafe() {
        XCTAssertEqual(CADSketchImport.exportFileName(forProjectNamed: "Örnek kalıp"), "Örnek kalıp.tcad")
        XCTAssertEqual(CADSketchImport.exportFileName(forProjectNamed: "a/b\\c:d*e?f\"g<h>i|j"), "a-b-c-d-e-f-g-h-i-j.tcad")
        XCTAssertEqual(CADSketchImport.exportFileName(forProjectNamed: "  ..gizli.. "), "gizli.tcad")
        XCTAssertEqual(CADSketchImport.exportFileName(forProjectNamed: "   "), "Mono Di Tree cizimi.tcad")
        XCTAssertEqual(CADSketchImport.exportFileName(forProjectNamed: "///"), "---.tcad")
        let long = String(repeating: "x", count: 200)
        XCTAssertEqual(CADSketchImport.exportFileName(forProjectNamed: long).count, 60 + ".tcad".count)
        XCTAssertFalse(CADSketchImport.exportFileName(forProjectNamed: "satır\nsonu").contains("\n"))
    }

    func testZoomedMapKeepsTheCentreAndStaysInvertible() throws {
        let size = CGSize(width: 400, height: 240)
        let fit = try XCTUnwrap(DraftCanvasMap(size: size, minX: 0, maxX: 200, minY: 0, maxY: 100, inset: 16))
        let centre = fit.model(at: CGPoint(x: 200, y: 120))
        let zoomed = fit.viewed(DraftViewport().zoomed(by: 4), in: size)
        let after = zoomed.model(at: CGPoint(x: 200, y: 120))
        XCTAssertEqual(after.x, centre.x, accuracy: 1e-9)
        XCTAssertEqual(after.y, centre.y, accuracy: 1e-9)
        // A fit viewport changes nothing.
        let same = fit.viewed(DraftViewport(), in: size)
        XCTAssertEqual(same.screen(p(37, 11)).x, fit.screen(p(37, 11)).x, accuracy: 1e-9)
        // Panning moves the drawing by exactly the pan, and the round trip still holds.
        let panned = fit.viewed(DraftViewport().zoomed(by: 3)
            .panned(by: CGSize(width: 25, height: -10), in: size, content: fit.contentSize), in: size)
        let base = fit.viewed(DraftViewport().zoomed(by: 3), in: size)
        XCTAssertEqual(panned.screen(p(80, 40)).x - base.screen(p(80, 40)).x, 25, accuracy: 1e-9)
        XCTAssertEqual(panned.screen(p(80, 40)).y - base.screen(p(80, 40)).y, -10, accuracy: 1e-9)
        let back = panned.model(at: panned.screen(p(80, 40)))
        XCTAssertEqual(back.x, 80, accuracy: 1e-9)
        XCTAssertEqual(back.y, 40, accuracy: 1e-9)
    }

    func testDragDistanceShrinksWithZoomSoPiecesMoveByTheSameAmountOnScreen() throws {
        let size = CGSize(width: 400, height: 240)
        let piece = DraftPiece(name: "t", closed: true, points: [p(0, 0), p(10, 0), p(10, 10), p(0, 10)])
        let fit = try XCTUnwrap(DraftCanvasMap(size: size, minX: 0, maxX: 200, minY: 0, maxY: 100, inset: 16))
        let zoomed = fit.viewed(DraftViewport().zoomed(by: 4), in: size)
        let a = fit.translation(CGSize(width: 40, height: 0), for: piece, snap: false)
        let b = zoomed.translation(CGSize(width: 40, height: 0), for: piece, snap: false)
        XCTAssertEqual(a.x / b.x, 4, accuracy: 1e-9)
    }
}
