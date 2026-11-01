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
        }
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
