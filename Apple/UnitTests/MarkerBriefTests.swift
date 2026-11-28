import XCTest
@testable import TekstilCADViewer

final class MarkerBriefTests: XCTestCase {
    func testParsesNamedPiecesAndRetainsTrace() throws {
        let source = StudioPattern.skirt.makeProject()
        let sentence = "150 cm kumaşa iki ön, bir arka yerleştir"
        let request = try XCTUnwrap(MarkerBrief.parse(sentence, source: source))
        XCTAssertEqual(request.sourceText, sentence)
        XCTAssertEqual(request.widthMM, 1500)
        XCTAssertEqual(request.gapMM, 10)
        XCTAssertEqual(request.marginMM, 10)
        XCTAssertEqual(request.quantities[source.pieces[0].id], 2)
        XCTAssertEqual(request.quantities[source.pieces[1].id], 1)
        XCTAssertNil(request.output)
    }

    func testMatchesExactMultiwordSourceNamesAndSpacingValues() throws {
        let source = DraftProject(name: "Çalışma", pieces: [
            DraftPiece(name: "Dikdörtgen 1", closed: true, points: [
                DraftPoint(x: 0, y: 0), DraftPoint(x: 100, y: 0), DraftPoint(x: 100, y: 40)
            ])
        ])
        let request = try XCTUnwrap(MarkerBrief.parse(
            "150 cm kumaşa 2 adet Dikdörtgen 1 5 mm aralık 10 mm kenar payı yerleştir", source: source))
        XCTAssertEqual(request.quantities[source.pieces[0].id], 2)
        XCTAssertEqual(request.gapMM, 5)
        XCTAssertEqual(request.marginMM, 10)
    }

    func testParsesStandaloneApronQuantitiesAndPlotterIntent() throws {
        let source = StudioPattern.apron.makeProject()
        let sentence = "önlük için 150 cm kumaşa 2 gövde ve 1 cep yerleştir ve plotter dosyası hazırla"
        let request = try XCTUnwrap(MarkerBrief.parse(sentence, source: source))
        XCTAssertEqual(request.quantities[source.pieces[0].id], 2)
        XCTAssertEqual(request.quantities[source.pieces[1].id], 1)
        XCTAssertEqual(request.output, .plotter)
    }

    func testPatternBriefCreatesMarkerDraftAndPreservesPlotterIntent() throws {
        let sentence = "önlük için 150 cm kumaşa 2 gövde ve 1 cep yerleştir ve plotter dosyası hazırla"
        let result = try PatternBrief.read(sentence)
        XCTAssertEqual(result.output, .plotter)
        XCTAssertEqual(result.project.name, "Önlük · Pastal")
        XCTAssertEqual(result.project.pieces.count, 3)
        XCTAssertEqual(result.project.pieces.filter { $0.name.hasPrefix("Gövde") }.count, 2)
        XCTAssertEqual(result.project.pieces.filter { $0.name.hasPrefix("Cep") }.count, 1)
    }

    func testRejectsUnknownAmbiguousAndInvalidPieceCounts() {
        let apron = StudioPattern.apron.makeProject()
        for sentence in ["150 cm kumaşa 2 yaka yerleştir",
                         "150 cm kumaşa 0 gövde yerleştir",
                         "150 cm kumaşa 101 gövde yerleştir",
                         "150 kumaşa 2 gövde yerleştir",
                         "150 cm kumaşa 2 gövde yerleştir ekstra",
                         "150 cm kumaşa 2 gövde 3 cep yerleştir"] {
            XCTAssertThrowsError(try MarkerBrief.parse(sentence, source: apron), sentence)
        }

        var ambiguous = StudioPattern.skirt.makeProject()
        ambiguous.pieces.append(DraftPiece(name: "Ön cep", closed: true, points: [
            DraftPoint(x: 0, y: 0), DraftPoint(x: 10, y: 0), DraftPoint(x: 10, y: 10)
        ]))
        XCTAssertThrowsError(try MarkerBrief.parse("150 cm kumaşa iki ön yerleştir", source: ambiguous))
    }
}
