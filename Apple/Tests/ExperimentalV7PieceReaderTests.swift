import Foundation
import XCTest
@testable import TekstilCADViewer_iOS

final class ExperimentalV7PieceReaderTests: XCTestCase {
    private func extract(_ data: Data, chunk: Int) throws -> [Data] {
        var reader = ExperimentalV7PieceReader()
        var offset = 0
        while offset < data.count {
            let end = min(data.count, offset + chunk)
            try reader.feed(data.subdata(in: offset..<end)); offset = end
        }
        return try reader.finish()
    }
    func testDesktopFixtureExtractsOnlyVisiblePiecesAcrossChunkBoundaries() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "desktop-synthetic-rotated-v7-stale", withExtension: "tcad"))
        let original = try Data(contentsOf: url)
        let root = try XCTUnwrap(try JSONSerialization.jsonObject(with: original) as? [Any])
        let body = try XCTUnwrap(root[1] as? [String: Any])
        let expected = try XCTUnwrap(body["Pieces"] as? [[String: Any]])
        for chunk in [1, 7, 4096] {
            let parts = try extract(original, chunk: chunk)
            XCTAssertEqual(parts.count, expected.count)
            for (part, source) in zip(parts, expected) {
                let value = try XCTUnwrap(try JSONSerialization.jsonObject(with: part) as? [String: Any])
                XCTAssertEqual(value["Id"] as? String, source["Id"] as? String)
                XCTAssertEqual(value["Name"] as? String, source["Name"] as? String)
            }
            let actual = try ExperimentalV7Contours.decode(parts)
            let existing = try Drawing.load(original)
            XCTAssertEqual(actual.map(\.id), existing.pieces.map(\.id))
            XCTAssertEqual(actual.map(\.name), existing.pieces.map(\.name))
            XCTAssertEqual(actual.map(\.notches), existing.pieces.map(\.notches))
            XCTAssertEqual(actual.map { $0.points.count }, existing.pieces.map { $0.points.count })
        }
    }
    func testContourDecoderRejectsDuplicateKeysAndInvalidMarker() {
        let duplicate = Data(#"{"Id":"11111111-1111-1111-1111-111111111111","Name":"A","Name":"B","Closed":false,"Points":[]}"#.utf8)
        XCTAssertThrowsError(try ExperimentalV7Contours.decode([duplicate]))
        let invalid = Data(#"{"Id":"11111111-1111-1111-1111-111111111111","Name":"A","Closed":false,"Points":[{"X":0,"Y":0}],"Notches":[{"VertexIndex":1,"Position":{"X":0,"Y":0}}]}"#.utf8)
        XCTAssertThrowsError(try ExperimentalV7Contours.decode([invalid]))
        XCTAssertThrowsError(try ExperimentalV7Contours.decode([invalid], cancelled: { true }))
    }
    func testContourDecoderRejectsTooFewPointsAndNonIntegerQuantityTokens() throws {
        let prefix = #"{"Id":"11111111-1111-1111-1111-111111111111","Name":"A","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}],"Metadata":{"Role":"Kesim","Quantity":NUMBER}}"#
        for token in ["1.0", "1e0", "1E+0"] {
            let bytes = Data(prefix.replacingOccurrences(of: "NUMBER", with: token).utf8)
            XCTAssertThrowsError(try ExperimentalV7Contours.decode([bytes]), token)
        }
        let valid = Data(prefix.replacingOccurrences(of: "NUMBER", with: "1").utf8)
        XCTAssertEqual(try ExperimentalV7Contours.decode([valid]).first?.quantity, 1)
        let point = #"{"X":0,"Y":0}"#
        for (closed, points) in [(false, ""), (false, point), (true, point), (true, point + "," + point)] {
            let json = "{\"Id\":\"11111111-1111-1111-1111-111111111111\",\"Name\":\"A\",\"Closed\":\(closed),\"Points\":[\(points)]}"
            XCTAssertThrowsError(try ExperimentalV7Contours.decode([Data(json.utf8)]))
        }
    }
    func testSignedIntegerTokensUseFieldRangeAfterSyntax() throws {
        let template = #"{"Id":"11111111-1111-1111-1111-111111111111","Name":"A","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}],"Notches":[{"VertexIndex":INDEX,"Position":{"X":0,"Y":0}}],"Metadata":{"Role":"Kesim","Quantity":QUANTITY}}"#
        func input(_ index: String, _ quantity: String) -> Data {
            Data(template.replacingOccurrences(of: "INDEX", with: index).replacingOccurrences(of: "QUANTITY", with: quantity).utf8)
        }
        XCTAssertEqual(try ExperimentalV7Contours.decode([input("-0", "1")]).first?.notches, [0])
        for index in ["-1", "-01", "-0.0", "-0e0"] {
            XCTAssertThrowsError(try ExperimentalV7Contours.decode([input(index, "1")]), index)
        }
        for quantity in ["-0", "-1", "1.0"] {
            XCTAssertThrowsError(try ExperimentalV7Contours.decode([input("0", quantity)]), quantity)
        }
    }
    func testOptionalExternalLargeDesktopV7Fixture() throws {
        guard let path = ProcessInfo.processInfo.environment["TCAD_LARGE_V7_FIXTURE"], !path.isEmpty else {
            throw XCTSkip("Set TCAD_LARGE_V7_FIXTURE to the external synthetic desktop v7 file")
        }
        let file = try FileHandle(forReadingFrom: URL(fileURLWithPath: path))
        defer { try? file.close() }
        var reader = ExperimentalV7PieceReader()
        while true {
            let chunk = try file.read(upToCount: 64 * 1024) ?? Data()
            if chunk.isEmpty { break }
            try reader.feed(chunk)
        }
        let parts = try reader.finish()
        XCTAssertEqual(parts.count, 8_000)
        XCTAssertEqual(try ExperimentalV7Contours.decode(parts).count, 8_000)
    }
    func testEscapedKeyNestedNamesAndSyntaxFailures() throws {
        let json = #"[7,{"Name":"Pieces","\u0050ieces":[{"Name":"{[x]}","Points":[]}],"GradingRules":[],"NestingSnapshot":{"Pieces":[{"Name":"hidden"}]}}]"#
        let parts = try extract(Data(json.utf8), chunk: 2)
        XCTAssertEqual(parts.count, 1)
        XCTAssertTrue(String(decoding: parts[0], as: UTF8.self).contains("{[x]}"))
        for bad in [#"[6,{"Pieces":[]}]"#, #"[7,{"Pieces":[{}],"Pieces":[]}]"#, #"[7,{"Pieces":[{},]}]"#,
                    #"[7,{"Name":"P","Pieces":[],"NestingSnapshot":{}}]"#,
                    #"[7,{"Name":"P","Pieces":[],"GradingRules":[],"NestingSnapshot":{},"Unknown":1}]"#] {
            XCTAssertThrowsError(try extract(Data(bad.utf8), chunk: 1))
        }
    }
    func testLongEscapedProjectNameIsNotTreatedAsAKey() throws {
        let name = String(repeating: #"\u0061"#, count: 1_000)
        let json = "[7,{\"Name\":\"\(name)\",\"Pieces\":[],\"GradingRules\":[],\"NestingSnapshot\":{}}]"
        XCTAssertEqual(try extract(Data(json.utf8), chunk: 23).count, 0)
    }
    func testLimitsAndCancelledReadsNeverReturnPartialResults() {
        let json = Data(#"[7,{"Name":"P","Pieces":[{"Name":"one"},{"Name":"two"}],"GradingRules":[],"NestingSnapshot":{}}]"#.utf8)
        var reader = ExperimentalV7PieceReader(maximumPieces: 1)
        XCTAssertThrowsError(try reader.feed(json))
        XCTAssertThrowsError(try reader.finish())
        var bytes = ExperimentalV7PieceReader(maximumPieceBytes: 5)
        XCTAssertThrowsError(try bytes.feed(json))
        var output = ExperimentalV7PieceReader(maximumExtractedBytes: 5)
        XCTAssertThrowsError(try output.feed(json))
        var cancelled = ExperimentalV7PieceReader(); var checks = 0
        XCTAssertThrowsError(try cancelled.feed(json, cancelled: { checks += 1; return checks == 10 }))
        XCTAssertThrowsError(try cancelled.finish())
    }
}
