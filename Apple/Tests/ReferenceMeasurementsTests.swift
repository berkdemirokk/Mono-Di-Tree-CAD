import XCTest
@testable import TekstilCADViewer

final class ReferenceMeasurementsTests: XCTestCase {
    func testChartMatchesFirstSourcePhotoTranscription() {
        XCTAssertEqual(ReferenceMeasurements.sizes, [36,38,40,42,44,46,48,50,52])
        let expected: [(String, [Double])] = [
            ("bust", [86,90,94,98,102,106,110,114,118]),
            ("waist", [66,70,74,78,82,86,90,94,98]),
            ("hips", [92,96,100,104,108,112,116,120,124]),
            ("total_dress_length", [110,110,110,110,110,110,110,110,110]),
            ("bust_height", [25,26,27,27.5,28,28,28,28,28]),
            ("front_bodice_length", [48,49,50,50,50,50,50,50,50]),
            ("back_bodice_length", [48,49,50,50,50,50,50,50,50]),
            ("shoulder", [37,38,40,41,41.5,42,42.5,43,43.5]),
            ("arm_length", [60,61,62,62,62,62,62,62,62]),
            ("upper_arm", [31,32,33,34,34.5,35,35.5,36,36.5]),
            ("neck", [39,40,41,42,42.5,43,43.5,44,44.5]),
            ("wrist", [19,20,21,22,22.5,23,23.5,24,24.5])
        ]
        XCTAssertEqual(ReferenceMeasurements.rows.count, expected.count)
        for (row, source) in zip(ReferenceMeasurements.rows, expected) {
            XCTAssertEqual(row.id, source.0)
            XCTAssertEqual(row.values, source.1, row.id)
            XCTAssertEqual(row.values.count, ReferenceMeasurements.sizes.count)
        }
    }

    func testTotalLengthMeaningAndLanguageFormatting() throws {
        let length = try XCTUnwrap(ReferenceMeasurements.rows.first { $0.id == "total_dress_length" })
        XCTAssertEqual(length.label(english: false), "Toplam elbise boyu")
        XCTAssertEqual(length.label(english: true), "Total dress length")
        XCTAssertEqual(ReferenceMeasurements.formatted(27.5, english: false), "27,5")
        XCTAssertEqual(ReferenceMeasurements.formatted(27.5, english: true), "27.5")
        XCTAssertEqual(ReferenceMeasurements.formatted(110, english: true), "110")
        XCTAssertEqual(Set(ReferenceMeasurements.rows.map(\.id)).count, 12)
    }
}
