import XCTest
@testable import TekstilCADViewer

final class PatternBriefTests: XCTestCase {
    func testNaturalLanguageProducesMeasuredEditableApronAndOutput() throws {
        let brief = "65 cm eninde 85 cm boyunda cepli bir önlük çiz ve PDF hazırla"
        let project = try PatternBrief.parse(brief)
        XCTAssertEqual(project.pieces.map(\.name), ["Gövde", "Cep"])
        let body = try XCTUnwrap(project.pieces.first)
        XCTAssertEqual(body.maxX - body.minX, 650, accuracy: 0.001)
        XCTAssertEqual(body.maxY - body.minY, 850, accuracy: 0.001)
        XCTAssertEqual(try PatternBrief.output(in: brief), .pdf)
        XCTAssertTrue(CADLocalDraftStore.isValid(project))
    }

    func testRevisedBriefChangesGeometryAndPocket() throws {
        for text in ["Eni 62,5cm, boyu 900mm olan cepsiz önlük", "62,5 × 90 cm cepsiz önlük"] {
            let project = try PatternBrief.parse(text)
            let body = try XCTUnwrap(project.pieces.first)
            XCTAssertEqual(project.pieces.count, 1)
            XCTAssertEqual(body.maxX - body.minX, 625, accuracy: 0.001)
            XCTAssertEqual(body.maxY - body.minY, 900, accuracy: 0.001)
        }
    }

    func testUnsupportedDetailsAndAmbiguousNumbersAreRefused() {
        for text in ["65 x 85 önlük", "cepli cepsiz önlük", "fermuarlı önlük",
                     "etek ve önlük", "eni 2 cm önlük", "etek 50 cm boyunda",
                     "önlük PDF ve TCAD", "65 cm eninde 70 cm eninde önlük"] {
            XCTAssertThrowsError(try PatternBrief.parse(text), text)
        }
    }

    func testFixedSizeTemplatesAndNoOutputRequest() throws {
        XCTAssertEqual(try PatternBrief.parse("bez çanta kalıbı oluştur").pieces.count, 4)
        XCTAssertEqual(try PatternBrief.parse("etek çiz").pieces.count, 3)
        XCTAssertNil(try PatternBrief.output(in: "etek çiz"))
    }
}

extension PatternBriefTests {
    func testApronRevisionsPreservePreviousMeasurementsPiecesAndIdentity() throws {
        let initial = try PatternBrief.parse("67,5 x 92,5 cm cepli önlük")
        let original = initial
        let withoutPocket = try PatternBrief.read("cebi kaldır", previous: initial).project
        XCTAssertEqual(withoutPocket.pieces, [original.pieces[0]])
        let wider = try PatternBrief.read("eni 70 cm olsun", previous: withoutPocket).project
        XCTAssertEqual(wider.id, initial.id)
        XCTAssertEqual(wider.pieces.map(\.id), withoutPocket.pieces.map(\.id))
        let body = try XCTUnwrap(wider.pieces.first)
        XCTAssertEqual(body.maxX - body.minX, 700, accuracy: 0.001)
        XCTAssertEqual(body.maxY - body.minY, 925, accuracy: 0.001)
        for (request, expected) in [("PDF hazırla", PatternBrief.Output.pdf), ("TCAD olarak kaydet", .tcad)] {
            let result = try PatternBrief.read(request, previous: wider)
            XCTAssertEqual(result.project, wider)
            XCTAssertEqual(result.output, expected)
        }
        XCTAssertThrowsError(try PatternBrief.read("cebi büyüt", previous: wider))
        XCTAssertThrowsError(try PatternBrief.read("eni 2 cm olsun", previous: wider))
        XCTAssertThrowsError(try PatternBrief.read("cebi kaldır"))
        XCTAssertEqual(initial, original)
    }
}

extension PatternBriefTests {
    func testPrinterRequestsUseTheCurrentDrawingAndRejectUnknownMachineActions() throws {
        let draft = try PatternBrief.parse("67 x 82 cm cepsiz önlük")
        for text in ["makinada bastır", "makinede bastır", "plotterda bastır", "plotter dosyası hazırla", "yazdır"] {
            let result = try PatternBrief.read(text, previous: draft)
            XCTAssertEqual(result.project, draft)
            XCTAssertEqual(result.output, .plotter)
        }
        XCTAssertEqual(try PatternBrief.read("kâğıda yazdır", previous: draft).output, .paper)
        XCTAssertEqual(try PatternBrief.output(in: "bez çanta oluştur ve makinede bastır"), .plotter)
        XCTAssertThrowsError(try PatternBrief.read("makinada bastır"))
        XCTAssertThrowsError(try PatternBrief.read("TCAD olarak kaydet ve bastır", previous: draft))
        XCTAssertThrowsError(try PatternBrief.read("kesim makinesine gönder", previous: draft))
    }
}
