import XCTest
@testable import TekstilCADViewer

final class ExperimentalPointMoveJSONTests: XCTestCase {
    private let id = UUID(uuidString: "11111111-1111-1111-1111-111111111111")!
    private let input = #"[5, {"Name":"Model", "Pieces":[{"Id":"11111111-1111-1111-1111-111111111111","Name":"A","Closed":true,"Points":[{"Y":0,"X":0},{"X":4,"Y":0},{"X":0,"Y":4},{"X":0,"Y":0}]}]}]"#
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
        for field in [#""GradingRules":[],"#, #""NestingSnapshot":null,"#, #""ImportProvenance":null,"#, #""Unknown":{},"#] {
            let unsafe = Data(input.replacingOccurrences(of: #"{"Name":"Model"#, with: "{" + field + #""Name":"Model"#).utf8)
            XCTAssertThrowsError(try ExperimentalPointMoveJSON.moving(unsafe, snapshot: unsafe, pieceID: id, index: 0, to: .init(x: 1, y: 1)))
        }
        let duplicate = Data(input.replacingOccurrences(of: #""Y":0,"X":0"#, with: #""Y":0,"X":0,"X":0"#).utf8)
        XCTAssertThrowsError(try ExperimentalPointMoveJSON.moving(duplicate, snapshot: duplicate, pieceID: id, index: 0, to: .init(x: 1, y: 1)))
    }
}
