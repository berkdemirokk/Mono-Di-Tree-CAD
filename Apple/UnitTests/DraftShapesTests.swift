import XCTest
@testable import TekstilCADViewer

final class DraftShapesTests: XCTestCase {
    private func piece(_ name: String, _ points: [DraftPoint]) -> DraftPiece {
        DraftPiece(name: name, closed: true, points: points)
    }

    func testCircleIsAValidRoundClosedShapeInsideItsBox() {
        let points = DraftShapes.circle(left: 20, bottom: 40, diameter: 100)
        XCTAssertEqual(points.count, DraftShapes.circleCorners)
        XCTAssertLessThanOrEqual(points.count, DraftContourGeometry.pointLimit)
        XCTAssertNil(DraftContourGeometry.validationError(points: points, closed: true))
        let circle = piece("Daire", points)
        XCTAssertEqual(circle.minX, 20)
        XCTAssertEqual(circle.maxX, 120)
        XCTAssertEqual(circle.minY, 40)
        XCTAssertEqual(circle.maxY, 140)
        // The width box of a closed piece reads the diameter.
        XCTAssertEqual(DraftPiece.display(circle.lineLength), "100")
        for p in points {
            XCTAssertEqual(hypot(p.x - 70, p.y - 90), 50, accuracy: 1e-5)
        }
    }

    func testTriangleIsValidAndFillsItsBox() {
        let points = DraftShapes.triangle(left: 0, bottom: 0, width: 100, height: 80)
        XCTAssertEqual(points, [DraftPoint(x: 0, y: 80), DraftPoint(x: 100, y: 80), DraftPoint(x: 50, y: 0)])
        XCTAssertNil(DraftContourGeometry.validationError(points: points, closed: true))
        let triangle = piece("Üçgen", points)
        XCTAssertEqual(triangle.maxX - triangle.minX, 100)
        XCTAssertEqual(triangle.maxY - triangle.minY, 80)
    }

    func testDegenerateInputGivesNoShape() {
        XCTAssertTrue(DraftShapes.circle(left: 0, bottom: 0, diameter: 0).isEmpty)
        XCTAssertTrue(DraftShapes.circle(left: 0, bottom: 0, diameter: 10, corners: 2).isEmpty)
        XCTAssertTrue(DraftShapes.circle(left: .nan, bottom: 0, diameter: 10).isEmpty)
        XCTAssertTrue(DraftShapes.triangle(left: 0, bottom: 0, width: 10, height: 0).isEmpty)
        XCTAssertTrue(DraftShapes.triangle(left: 0, bottom: 0, width: -1, height: 10).isEmpty)
    }

    func testNewShapesWorkWithWidthHeightEditingAndRotation() throws {
        for shape in [piece("Daire", DraftShapes.circle(left: 0, bottom: 0, diameter: 100)),
                      piece("Üçgen", DraftShapes.triangle(left: 0, bottom: 0, width: 100, height: 100))] {
            let resized = try XCTUnwrap(shape.applyingDimensions(x: "10", y: "5", width: "250", height: "60"),
                                        shape.name)
            let edited = piece(shape.name, resized)
            XCTAssertEqual(edited.minX, 10, accuracy: 1e-9)
            XCTAssertEqual(edited.minY, 5, accuracy: 1e-9)
            XCTAssertEqual(edited.maxX - edited.minX, 250, accuracy: 1e-6)
            XCTAssertEqual(edited.maxY - edited.minY, 60, accuracy: 1e-6)
            XCTAssertNil(DraftContourGeometry.validationError(points: resized, closed: true))

            let turned = shape.rotatedQuarterTurn()
            XCTAssertNil(DraftContourGeometry.validationError(points: turned, closed: true))
        }
    }

    func testDrawingWithNewShapesStaysStorableAndExportable() throws {
        let project = DraftProject(name: "Şekiller", pieces: [
            piece("Daire 1", DraftShapes.circle(left: 0, bottom: 0, diameter: 100)),
            piece("Üçgen 2", DraftShapes.triangle(left: 20, bottom: 20, width: 100, height: 100))
        ])
        XCTAssertTrue(CADLocalDraftStore.isValid(project))
        let data = try CADSketchImport.encode(project)
        let root = try JSONSerialization.jsonObject(with: data)
        let reopened = try XCTUnwrap(CADSketchImport.loadEditableCopy(from: data, jsonRoot: root))
        XCTAssertEqual(reopened.pieces.map(\.name), ["Daire 1", "Üçgen 2"])
        XCTAssertEqual(reopened.pieces.map(\.points.count), [DraftShapes.circleCorners, 3])
    }

    func testExportFileNameFollowsTheProjectName() {
        XCTAssertEqual(CADSketchImport.exportFileName(forProjectNamed: "Şekiller"), "Şekiller.tcad")
    }
}
