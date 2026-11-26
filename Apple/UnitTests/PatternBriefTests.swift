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
