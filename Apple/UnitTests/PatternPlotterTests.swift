import XCTest
@testable import TekstilCADViewer

final class PatternPlotterTests: XCTestCase {
    func testOpenAndClosedPathsUseAbsolutePlotterUnitsAndLiftBetweenPieces() throws {
        let triangle = DraftPiece(name: "Ön", closed: true, points: [
            DraftPoint(x: 0, y: 0), DraftPoint(x: 50, y: 0), DraftPoint(x: 0, y: 100)
        ])
        let line = DraftPiece(name: "İşaret", closed: false, points: [
            DraftPoint(x: 62.5, y: 0), DraftPoint(x: 65, y: 10)
        ])
        let project = DraftProject(name: "Ölçü", pieces: [triangle, line])
        let url = try PatternPlotterFile.export(project)
        defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }
        XCTAssertEqual(url.pathExtension, "plt")
        let output = try String(contentsOf: url, encoding: .ascii)
        XCTAssertEqual(output, "IN;SP1;PA;\nPU0,0;PD2000,0,0,4000,0,0;PU;\nPU2500,0;PD2600,400;PU;\nSP0;\n")
    }

    func testRefusesEdgeThatVanishesAtPlotterResolution() {
        let line = DraftPiece(name: "Çizgi", closed: false, points: [DraftPoint(x: 0, y: 0), DraftPoint(x: 0.01, y: 0)])
        XCTAssertThrowsError(try PatternPlotterFile.encode(DraftProject(name: "Deneme", pieces: [line])))
    }

    func testCalibrationReferenceEncodesKnownMillimetreLengths() throws {
        let output = String(decoding: try PatternPlotterFile.encode(PatternPlotterFile.calibrationProject()), as: UTF8.self)
        let paths = output.components(separatedBy: "\n")

        XCTAssertTrue(paths.contains("PU0,0;PD4000,0,4000,4000,0,4000,0,0;PU;"),
                      "The closed 100 mm square must be 4000 plotter units on each side")
        XCTAssertTrue(paths.contains("PU1000,2000;PD3000,2000;PU;"),
                      "The gauge line must span 2000 plotter units (50 mm)")
        let tickXs = [1000, 1400, 1800, 2200, 2600, 3000]
        let ticks = paths.filter { path in
            path != "PU1000,2000;PD3000,2000;PU;" && tickXs.contains { path.hasPrefix("PU\($0),") }
        }
        XCTAssertEqual(ticks.count, 6, "A 50 mm gauge with 10 mm divisions has six tick marks including both ends")
        XCTAssertTrue(ticks.contains("PU1000,1880;PD1000,2120;PU;"))
        XCTAssertTrue(ticks.contains("PU3000,1880;PD3000,2120;PU;"))
    }

    func testTurkishCalibrationCommandPlansPreviewWithoutChangingDocument() throws {
        let parsed = CommandParser.parse("plotter kalibrasyon dosyası hazırla")
        XCTAssertEqual(parsed.command, .plotterCalibration)

        let document = DraftProject.blank
        let context = CommandContext(document: document, base: document, baseProblem: nil,
                                    selectedID: nil, undoCount: 0, redoCount: 0)
        guard case .plan(let plan) = CommandPlanner.plan(.plotterCalibration, context: context) else {
            return XCTFail("Calibration should produce a confirmation preview")
        }

        XCTAssertEqual(plan.sentence, "100 × 100 mm kalibrasyon karesi ve 50 mm ölçü çizgisi için PLT dosyası hazırlanacak.")
        XCTAssertEqual(plan.base, document)
        XCTAssertEqual(plan.patternPreview?.name, "Plotter Kalibrasyon 100 mm")
        XCTAssertEqual(plan.patternPreview?.pieces.map(\.name).first, "100 mm kare")
        guard case .markerPlotter(let reference) = plan.effect else {
            return XCTFail("Confirmation should create a shareable PLT file")
        }
        XCTAssertEqual(reference.pieces.map(\.name).first, "100 mm kare")
        XCTAssertEqual(document.pieces.count, 0, "Preparing the preview must not alter the source drawing")
    }
}
