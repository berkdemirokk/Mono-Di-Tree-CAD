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
}
