import Foundation
import XCTest
@testable import TekstilCADViewer_iOS

final class ExperimentalV7PieceReaderTests: XCTestCase {
    private var minimalSnapshot: String {
        let hash = String(repeating: "A", count: 64)
        // Guid constructed by the desktop from SHA-256 bytes for this source/copy.
        let source = "11111111-1111-1111-1111-111111111111", placed = "05465454-9f9e-9b46-87d3-3e3ecb12fb73"
        func piece(_ id: String) -> String { "{\"Id\":\"\(id)\",\"Name\":\"A\",\"Closed\":true,\"Points\":[{\"X\":0,\"Y\":0},{\"X\":1,\"Y\":0},{\"X\":0,\"Y\":1}],\"Metadata\":{\"Role\":\"Kesim\",\"Material\":\"Pamuk\",\"Quantity\":1},\"Grainline\":{\"Start\":{\"X\":0,\"Y\":0},\"End\":{\"X\":0,\"Y\":1},\"NeedsReview\":false}}" }
        return "{\"FabricWidth\":1,\"UsedLength\":1,\"Gap\":0,\"EdgeMargin\":0,\"Algorithm\":\"deterministic-shelf-v1\",\"LayoutFingerprint\":\"\(hash)\",\"Placements\":[{\"SourcePieceId\":\"\(source)\",\"CopyNumber\":1,\"PlacedPieceId\":\"\(placed)\"}],\"RotationTrace\":{\"Schema\":\"per-copy-orthogonal-trace-v1\",\"Algorithm\":\"explicit-per-copy-shelf-v1\",\"TransformConvention\":\"origin-quarter-turn-then-translate-v1\",\"SourceFingerprint\":\"\(hash)\",\"LayoutFingerprint\":\"\(hash)\",\"SourcePieces\":[\(piece(source))],\"LayoutPieces\":[\(piece(placed))],\"Selections\":[\"\(source)\"],\"Copies\":[{\"SourcePieceId\":\"\(source)\",\"CopyNumber\":1,\"PlacedPieceId\":\"\(placed)\",\"Degrees\":0}],\"FabricWidth\":1,\"Gap\":0,\"EdgeMargin\":0,\"UsedLength\":1,\"MaxLength\":null,\"GrainDirection\":\"TwoWay\",\"ToleranceDegrees\":0}}"
    }
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
        let json = #"[7,{"Name":"Pieces","\u0050ieces":[{"Name":"{[x]}","Points":[]}],"GradingRules":[],"NestingSnapshot":\#(minimalSnapshot)}]"#
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
        let json = "[7,{\"Name\":\"\(name)\",\"Pieces\":[],\"GradingRules\":[],\"NestingSnapshot\":\(minimalSnapshot)}]"
        XCTAssertEqual(try extract(Data(json.utf8), chunk: 23).count, 0)
        var reader = ExperimentalV7PieceReader()
        for byte in json.utf8 { try reader.feed(Data([byte])) }
        let candidate = try reader.finishPreview()
        XCTAssertEqual(candidate.name, String(repeating: "a", count: 1_000))
        XCTAssertTrue(candidate.pieces.isEmpty)
    }
    func testPreviewCandidateMatchesDesktopFixtureVisiblePieces() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "desktop-synthetic-rotated-v7-stale", withExtension: "tcad"))
        let data = try Data(contentsOf: url)
        var reader = ExperimentalV7PieceReader()
        for offset in stride(from: 0, to: data.count, by: 31) {
            try reader.feed(data.subdata(in: offset..<min(offset + 31, data.count)))
        }
        let candidate = try reader.finishPreview()
        let expected = try Drawing.load(data)
        XCTAssertEqual(candidate.name, expected.name)
        XCTAssertEqual(candidate.pieces.map(\.id), expected.pieces.map(\.id))
        XCTAssertEqual(candidate.pieces.map(\.notches), expected.pieces.map(\.notches))
    }
    func testFourDesktopRotationsMatchCurrentViewerContours() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "desktop-synthetic-rotated-v7-four-angle", withExtension: "tcad"))
        let bytes = try Data(contentsOf: url), before = bytes
        var reader = ExperimentalV7PieceReader()
        for offset in stride(from: 0, to: bytes.count, by: 127) {
            try reader.feed(bytes.subdata(in: offset..<min(offset + 127, bytes.count)))
        }
        let candidate = try reader.finishPreview(), expected = try Drawing.load(bytes)
        XCTAssertEqual(candidate.name, expected.name)
        XCTAssertEqual(candidate.pieces.count, 4)
        for (a, b) in zip(candidate.pieces, expected.pieces) {
            XCTAssertEqual(a.id, b.id); XCTAssertEqual(a.notches, b.notches)
            XCTAssertEqual(a.points.count, b.points.count)
            for (x, y) in zip(a.points, b.points) { XCTAssertEqual(x.x, y.x); XCTAssertEqual(x.y, y.y) }
        }
        XCTAssertEqual(bytes, before)
    }
    func testExperimentalDocumentKeepsExactlyTheFedBytesAndFlagsDifferentIDs() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "desktop-synthetic-rotated-v7-stale", withExtension: "tcad"))
        let bytes = try Data(contentsOf: url)
        var reader = ExperimentalV7PieceReader()
        for offset in stride(from: 0, to: bytes.count, by: 257) {
            try reader.feed(bytes.subdata(in: offset..<min(offset + 257, bytes.count)))
        }
        let document = try reader.finishDocument(original: bytes)
        let expected = try Drawing.load(bytes)
        XCTAssertEqual(document.originalBytes, bytes)
        XCTAssertEqual(document.drawing.name, expected.name)
        XCTAssertEqual(document.drawing.pieces.map(\.id), expected.pieces.map(\.id))
        XCTAssertTrue(document.drawing.historyPieceIDsDiffer)

        var changed = bytes; changed[changed.count - 1] ^= 1
        var other = ExperimentalV7PieceReader(); try other.feed(bytes)
        XCTAssertThrowsError(try other.finishDocument(original: changed))
        XCTAssertThrowsError(try other.finishDocument(original: bytes))
        var cancelled = ExperimentalV7PieceReader(); try cancelled.feed(bytes)
        XCTAssertThrowsError(try cancelled.finishDocument(original: bytes, cancelled: { true }))
    }
    func testSnapshotAndTraceHeaderRejectMissingUnknownOrWrongFields() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "desktop-synthetic-rotated-v7-four-angle", withExtension: "tcad"))
        let bytes = try Data(contentsOf: url)
        let array = try XCTUnwrap(try JSONSerialization.jsonObject(with: bytes) as? [Any])
        let original = try XCTUnwrap(array[1] as? [String: Any])
        let originalSnapshot = try XCTUnwrap(original["NestingSnapshot"] as? [String: Any])
        let originalTrace = try XCTUnwrap(originalSnapshot["RotationTrace"] as? [String: Any])
        func rejected(_ snapshot: [String: Any]) throws {
            var body = original; body["NestingSnapshot"] = snapshot
            let data = try JSONSerialization.data(withJSONObject: [7, body])
            XCTAssertThrowsError(try extract(data, chunk: 101))
        }
        var snapshot = originalSnapshot; snapshot.removeValue(forKey: "Gap"); try rejected(snapshot)
        snapshot = originalSnapshot; snapshot["Unknown"] = true; try rejected(snapshot)
        var trace = originalTrace; trace.removeValue(forKey: "Copies")
        snapshot = originalSnapshot; snapshot["RotationTrace"] = trace; try rejected(snapshot)
        trace = originalTrace; trace["Schema"] = "unknown"
        snapshot["RotationTrace"] = trace; try rejected(snapshot)
        trace = originalTrace; trace["SourcePieces"] = "not an array"
        snapshot["RotationTrace"] = trace; try rejected(snapshot)
        trace = originalTrace
        var copies = try XCTUnwrap(trace["Copies"] as? [[String: Any]])
        copies[0]["CopyNumber"] = 999; trace["Copies"] = copies
        snapshot["RotationTrace"] = trace; try rejected(snapshot)
        trace = originalTrace; copies = try XCTUnwrap(trace["Copies"] as? [[String: Any]])
        copies[0]["Degrees"] = 45; trace["Copies"] = copies
        snapshot["RotationTrace"] = trace; try rejected(snapshot)
        snapshot = originalSnapshot; snapshot["Gap"] = 123; try rejected(snapshot)
        snapshot = originalSnapshot; snapshot["LayoutFingerprint"] = String(repeating: "A", count: 64); try rejected(snapshot)
        trace = originalTrace; trace["Selections"] = []
        snapshot = originalSnapshot; snapshot["RotationTrace"] = trace; try rejected(snapshot)
        trace = originalTrace
        var sources = try XCTUnwrap(trace["SourcePieces"] as? [[String: Any]])
        sources[0]["Points"] = []
        trace["SourcePieces"] = sources; snapshot["RotationTrace"] = trace; try rejected(snapshot)
        let forged = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
        snapshot = originalSnapshot
        var placements = try XCTUnwrap(snapshot["Placements"] as? [[String: Any]])
        placements[0]["PlacedPieceId"] = forged; snapshot["Placements"] = placements
        trace = originalTrace; copies = try XCTUnwrap(trace["Copies"] as? [[String: Any]])
        copies[0]["PlacedPieceId"] = forged; trace["Copies"] = copies
        var historic = try XCTUnwrap(trace["LayoutPieces"] as? [[String: Any]])
        historic[0]["Id"] = forged; trace["LayoutPieces"] = historic
        snapshot["RotationTrace"] = trace; try rejected(snapshot)
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
