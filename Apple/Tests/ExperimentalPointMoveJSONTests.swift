import XCTest
@testable import TekstilCADViewer

final class ExperimentalPointMoveJSONTests: XCTestCase {
    private let id = UUID(uuidString: "11111111-1111-1111-1111-111111111111")!
    private let input = #"[4, {"GradingRules":[], "Name":"Model", "Pieces":[{"Id":"11111111-1111-1111-1111-111111111111","Name":"A","Closed":true,"Points":[{"Y":0,"X":0},{"X":4,"Y":0},{"X":0,"Y":4},{"X":0,"Y":0}]}]}]"#
    func testOnlyCoordinateTokensChangeIncludingExplicitClosure() throws {
        let data = Data(input.utf8)
        let result = try ExperimentalPointMoveJSON.moving(data, snapshot: data, pieceID: id, index: 0, to: .init(x: 1, y: 1))
        let expected = input.replacingOccurrences(of: #"{"Y":0,"X":0}"#, with: #"{"Y":1.0,"X":1.0}"#).replacingOccurrences(of: #"{"X":0,"Y":0}]}"#, with: #"{"X":1.0,"Y":1.0}]}"#)
        XCTAssertEqual(result, Data(expected.utf8))
        XCTAssertEqual(data, Data(input.utf8))
    }
    func testNoOpAndUnsafeInputs() throws {
        let data = Data(input.utf8)
        XCTAssertEqual(try ExperimentalPointMoveJSON.moving(data, snapshot: data, pieceID: id, index: 0, to: .init(x: 0, y: 0)), data)
        XCTAssertThrowsError(try ExperimentalPointMoveJSON.moving(data, snapshot: Data(), pieceID: id, index: 0, to: .init(x: 1, y: 1)))
        XCTAssertThrowsError(try ExperimentalPointMoveJSON.moving(data, snapshot: data, pieceID: UUID(), index: 0, to: .init(x: 1, y: 1)))
        XCTAssertThrowsError(try ExperimentalPointMoveJSON.moving(data, snapshot: data, pieceID: id, index: 1, to: .init(x: 0, y: 4)))
        for field in [#""GradingRules":[{}],"#, #""NestingSnapshot":null,"#, #""ImportProvenance":null,"#, #""Unknown":{},"#] {
            let unsafe = Data(input.replacingOccurrences(of: #"{"Name":"Model"#, with: "{" + field + #""Name":"Model"#).utf8)
            XCTAssertThrowsError(try ExperimentalPointMoveJSON.moving(unsafe, snapshot: unsafe, pieceID: id, index: 0, to: .init(x: 1, y: 1)))
        }
        let duplicate = Data(input.replacingOccurrences(of: #""Y":0,"X":0"#, with: #""Y":0,"X":0,"X":0"#).utf8)
        XCTAssertThrowsError(try ExperimentalPointMoveJSON.moving(duplicate, snapshot: duplicate, pieceID: id, index: 0, to: .init(x: 1, y: 1)))
        let escapedDuplicate = Data(input.replacingOccurrences(of: #""Y":0,"X":0"#, with: #""Y":0,"X":0,"\u0058":0"#).utf8)
        XCTAssertThrowsError(try ExperimentalPointMoveJSON.moving(escapedDuplicate, snapshot: escapedDuplicate, pieceID: id, index: 0, to: .init(x: 1, y: 1)))
        for token in ["1e-999", "9223372036854775808", "-9223372036854775808"] {
            let unsafe = Data(input.replacingOccurrences(of: #""X":4"#, with: "\"X\":" + token).utf8)
            XCTAssertThrowsError(try ExperimentalPointMoveJSON.moving(unsafe, snapshot: unsafe, pieceID: id, index: 0, to: .init(x: 1, y: 1)))
        }
    }

    func testDesktopShapedProjectAndUnchangedCoordinateToken() throws {
        let text = #"[4,{"Name":"Model","Pieces":[{"Id":"11111111-1111-1111-1111-111111111111","Name":"A","Points":[{"X":0.00,"Y":0e0,"IsFinite":true},{"X":4.5,"Y":0,"IsFinite":true},{"X":0,"Y":4,"IsFinite":true}],"Closed":true}],"GradingRules":[]}]"#
        let data = Data(text.utf8)
        let result = try ExperimentalPointMoveJSON.moving(data, snapshot: data, pieceID: id, index: 0, to: .init(x: 0, y: 0.5))
        XCTAssertEqual(result, Data(text.replacingOccurrences(of: #""Y":0e0"#, with: #""Y":0.5"#).utf8))
        let invalid = Data(text.replacingOccurrences(of: #""IsFinite":true"#, with: #""IsFinite":false"#).utf8)
        XCTAssertThrowsError(try ExperimentalPointMoveJSON.moving(invalid, snapshot: invalid, pieceID: id, index: 0, to: .init(x: 0, y: 0.5)))
    }

    func testDesktopSavedSyntheticFixturePreservesEveryOtherByte() throws {
        // Produced by ProjectExternalWriterRaceTests.Document and ProjectStorage.
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "desktop-synthetic-point", withExtension: "tcad"))
        let source = try Data(contentsOf: url)
        let text = try XCTUnwrap(String(data: source, encoding: .utf8))
        let token = #""X": 20"#
        XCTAssertEqual(text.components(separatedBy: token).count, 2)
        let id = try XCTUnwrap(UUID(uuidString: "92a551ad-c6b2-4b04-ae91-41ada47d1e60"))
        let changed = try ExperimentalPointMoveJSON.moving(source, snapshot: source, pieceID: id, index: 1, to: .init(x: 21.5, y: 0))
        XCTAssertEqual(changed, Data(text.replacingOccurrences(of: token, with: #""X": 21.5"#).utf8))
    }

    func testVersionAndOtherPieceValidation() throws {
        func rejects(_ text: String) {
            let data = Data(text.utf8)
            XCTAssertThrowsError(try ExperimentalPointMoveJSON.moving(data, snapshot: data, pieceID: id, index: 0, to: .init(x: 1, y: 1)))
        }
        rejects(input.replacingOccurrences(of: "[4,", with: "[5,"))
        rejects(input.replacingOccurrences(of: "[4,", with: "[3,"))
        rejects(input.replacingOccurrences(of: "[4,", with: "[4.0,"))
        rejects(input.replacingOccurrences(of: #""GradingRules":[], "#, with: ""))
        let second = #",{"Id":"22222222-2222-2222-2222-222222222222","Name":"B","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]}"#
        let two = input.replacingOccurrences(of: #"]}]}]"#, with: "]}" + second + "]}]")
        rejects(two.replacingOccurrences(of: #""Closed":false"#, with: #""Closed":1"#))
        rejects(two.replacingOccurrences(of: #""X":1,"Y":1"#, with: #""X":1e-999,"Y":1"#))
        rejects(two.replacingOccurrences(of: #""X":1,"Y":1"#, with: #""X":100000001,"Y":1"#))
    }

    func testVersionTwoMetadataIsPreservedAndInvalidDependenciesRejected() throws {
        let metadata = #", "Metadata":{"Role":"Kesim","Size":"M","Material":"Pamuk","Quantity":2}"#
        let withMetadata = input.replacingOccurrences(of: "[4,", with: "[2,")
            .replacingOccurrences(of: #""Closed":true"#, with: #""Closed":true"# + metadata)
        let data = Data(withMetadata.utf8)
        let output = try ExperimentalPointMoveJSON.moving(data, snapshot: data, pieceID: id, index: 1, to: .init(x: 5, y: 0))
        XCTAssertEqual(output, Data(withMetadata.replacingOccurrences(of: #""X":4"#, with: #""X":5.0"#).utf8))
        for invalid in [withMetadata.replacingOccurrences(of: "[2,", with: "[1,"),
                        withMetadata.replacingOccurrences(of: #""Quantity":2"#, with: #""Quantity":0"#),
                        withMetadata.replacingOccurrences(of: #""Role":"Kesim"#, with: #""Role":"Bilinmiyor"#),
                        withMetadata.replacingOccurrences(of: metadata, with: #", "Grainline":null"#)] {
            let bytes = Data(invalid.utf8)
            XCTAssertThrowsError(try ExperimentalPointMoveJSON.moving(bytes, snapshot: bytes, pieceID: id, index: 1, to: .init(x: 5, y: 0)))
        }
    }

    func testGeometryDependenciesFailClosedRatherThanBecomeStale() {
        let grading = #""GradingRules":[{"SourcePieceId":"11111111-1111-1111-1111-111111111111","SourceClosed":true,"SourcePoints":[{"X":0,"Y":0},{"X":4,"Y":0},{"X":0,"Y":4}],"Deltas":[{"X":0,"Y":0},{"X":0,"Y":0},{"X":0,"Y":0}]}]"#
        let grain = #", "Grainline":{"Start":{"X":0,"Y":0},"End":{"X":1,"Y":0},"NeedsReview":false}"#
        let variants = [
            input.replacingOccurrences(of: #""GradingRules":[]"#, with: grading),
            input.replacingOccurrences(of: #""Name":"Model"#, with: #""NestingSnapshot":{},"Name":"Model"#),
            input.replacingOccurrences(of: #""Name":"Model"#, with: #""ImportProvenance":{},"Name":"Model"#),
            input.replacingOccurrences(of: #""Closed":true"#, with: #""Closed":true"# + grain)
        ]
        for text in variants {
            let bytes = Data(text.utf8)
            XCTAssertThrowsError(try ExperimentalPointMoveJSON.moving(bytes, snapshot: bytes, pieceID: id, index: 1, to: .init(x: 5, y: 0)))
        }
    }
}
