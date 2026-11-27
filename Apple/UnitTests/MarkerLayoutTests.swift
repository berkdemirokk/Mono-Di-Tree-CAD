import XCTest
@testable import TekstilCADViewer

final class MarkerLayoutTests: XCTestCase {
    private func rectangle(_ name: String, x: Double = 0, y: Double = 0,
                          width: Double, height: Double) -> DraftPiece {
        DraftPiece(name: name, closed: true, points: [
            DraftPoint(x: x, y: y), DraftPoint(x: x + width, y: y),
            DraftPoint(x: x + width, y: y + height), DraftPoint(x: x, y: y + height)
        ])
    }

    func testCopiesAreTranslatedWithoutRotationAndOriginalIsUntouched() throws {
        let piece = rectangle("Ön", x: 10, y: 20, width: 40, height: 30)
        let source = DraftProject(name: "Bluz", pieces: [piece])
        let result = try MarkerLayout.arrange(source, widthMM: 110, gapMM: 5,
                                             marginMM: 10, quantities: [piece.id: 3])

        XCTAssertEqual(source.pieces, [piece])
        XCTAssertEqual(result.pieces.count, 3)
        XCTAssertEqual(Set(result.pieces.map(\.id)).count, 3)
        XCTAssertTrue(result.pieces.allSatisfy { $0.points.count == piece.points.count })
        XCTAssertEqual(result.pieces.map(\.minX), [10, 55, 10])
        XCTAssertEqual(result.pieces.map(\.minY), [10, 10, 45])
        XCTAssertEqual(result.pieces.map { $0.maxX - $0.minX }, [40, 40, 40])
        XCTAssertEqual(result.pieces.map { $0.maxY - $0.minY }, [30, 30, 30])
    }

    func testRejectsTooWidePieceAndInvalidNumbers() {
        let piece = rectangle("Ön", width: 80, height: 40)
        let source = DraftProject(name: "Bluz", pieces: [piece])
        XCTAssertThrowsError(try MarkerLayout.arrange(source, widthMM: 90, gapMM: 0,
                                                       marginMM: 6, quantities: [piece.id: 1]))
        XCTAssertThrowsError(try MarkerLayout.arrange(source, widthMM: .infinity, gapMM: 0,
                                                       marginMM: 0, quantities: [piece.id: 1]))
        XCTAssertThrowsError(try MarkerLayout.arrange(source, widthMM: 100, gapMM: -1,
                                                       marginMM: 0, quantities: [piece.id: 1]))
    }

    func testBoundsExpandedCopiesAndTotalPoints() {
        let piece = rectangle("Ön", width: 10, height: 10)
        let source = DraftProject(name: "Bluz", pieces: [piece])
        XCTAssertThrowsError(try MarkerLayout.arrange(source, widthMM: 100, gapMM: 0,
                                                       marginMM: 0, quantities: [piece.id: 251]))
        XCTAssertThrowsError(try MarkerLayout.arrange(source, widthMM: 100, gapMM: 0,
                                                       marginMM: 0, quantities: [piece.id: 5_001]))
    }
}
