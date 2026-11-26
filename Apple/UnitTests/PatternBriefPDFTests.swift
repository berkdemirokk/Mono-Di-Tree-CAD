import CoreGraphics
import XCTest
@testable import TekstilCADViewer

final class PatternBriefPDFTests: XCTestCase {
    private func rectangle(width: Double, height: Double) -> DraftProject {
        let piece = DraftPiece(name: "Gövde", closed: true, points: [
            DraftPoint(x: -50, y: -20), DraftPoint(x: width - 50, y: -20),
            DraftPoint(x: width - 50, y: height - 20), DraftPoint(x: -50, y: height - 20)
        ])
        return DraftProject(name: "Ölçek", pieces: [piece])
    }

    func testActualPDFUsesA4PaperAndTilesInsteadOfShrinking() throws {
        let url = try PatternBriefPDF.export(rectangle(width: 380, height: 300))
        defer { try? FileManager.default.removeItem(at: url) }
        let pdf = try XCTUnwrap(CGPDFDocument(url as CFURL))
        XCTAssertEqual(pdf.numberOfPages, 6)
        let page = try XCTUnwrap(pdf.page(at: 1))
        let box = page.getBoxRect(.mediaBox)
        XCTAssertEqual(box.width * 25.4 / 72, 210, accuracy: 0.01)
        XCTAssertEqual(box.height * 25.4 / 72, 297, accuracy: 0.01)
    }

    func testEmptyAndOversizedPatternsAreRefused() {
        XCTAssertThrowsError(try PatternBriefPDF.export(.blank))
        XCTAssertThrowsError(try PatternBriefPDF.export(rectangle(width: 1e12, height: 800)))
    }
}
