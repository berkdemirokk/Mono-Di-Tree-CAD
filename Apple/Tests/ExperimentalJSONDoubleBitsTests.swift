import Foundation
import XCTest
@testable import TekstilCADViewer_iOS

final class ExperimentalJSONDoubleBitsTests: XCTestCase {
    private func bits(_ token: String) throws -> UInt64 { try ExperimentalJSONDoubleBits.parse(Data(token.utf8)) }
    func testSignedZeroAndFiniteNumbersKeepBinaryBits() throws {
        for token in ["-0", "-0.0", "-0e10", "-0.000E-22"] { XCTAssertEqual(try bits(token), (-0.0 as Double).bitPattern, token) }
        for token in ["0", "0.0", "0e-999"] { XCTAssertEqual(try bits(token), (0.0 as Double).bitPattern, token) }
        XCTAssertEqual(try bits("1.5"), (1.5 as Double).bitPattern)
        XCTAssertEqual(try bits("-5e-324"), (-Double.leastNonzeroMagnitude).bitPattern)
    }
    func testMalformedOverflowUnderflowBoundsAndCancellationReject() {
        for token in ["", "+1", "01", "-01", "1.", ".1", "1e", "NaN", "1e9999", "1e-9999", "0 0", "\"0\""] {
            XCTAssertThrowsError(try bits(token), token)
        }
        XCTAssertThrowsError(try bits(String(repeating: "1", count: 129)))
        var count = 0
        XCTAssertThrowsError(try ExperimentalJSONDoubleBits.parse(Data("12345".utf8), cancelled: { count += 1; return count == 3 }))
        XCTAssertThrowsError(try ExperimentalJSONDoubleBits.parse(Data([0xff])))
    }
    func testDesktopV3GoldenUsesDecodedNegativeZeroToken() throws {
        func p(_ x: String, _ y: String) throws -> ExperimentalV7Fingerprint.Point {
            try .init(x: bits(x), y: bits(y))
        }
        let piece = try ExperimentalV7Fingerprint.Piece(
            id: XCTUnwrap(UUID(uuidString: "11111111-1111-1111-1111-111111111111")), name: "", closed: true,
            points: [p("0", "0"), p("2", "0"), p("0", "2")], metadata: nil, grain: nil,
            marks: [.init(index: 0, position: p("-0", "0"))])
        XCTAssertEqual(try ExperimentalV7Fingerprint.hash([piece]), "2C986126E3E97F06E57FE292F6CDAAD617F595498C29E7320D448CFC70C5E716")
        let input = Data("-0".utf8), before = input
        _ = try ExperimentalJSONDoubleBits.parse(input)
        XCTAssertEqual(input, before)
        var reused = input
        let captured = try ExperimentalJSONDoubleBits.parse(reused)
        reused[0] = 32
        XCTAssertEqual(captured, (-0.0 as Double).bitPattern)
    }
    func testNegativeZeroTokenFromDesktopV7FixtureKeepsItsSign() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "desktop-synthetic-rotated-v7-four-angle", withExtension: "tcad"))
        let bytes = try Data(contentsOf: url), before = bytes
        let text = try XCTUnwrap(String(data: bytes, encoding: .utf8))
        let start = try XCTUnwrap(text.range(of: "\"SourcePieces\""))
        let source = text[start.lowerBound...]
        let marker = try XCTUnwrap(source.range(of: "\"X\": -0"))
        let token = Data(source[marker].suffix(2).utf8)
        XCTAssertEqual(try ExperimentalJSONDoubleBits.parse(token), (-0.0 as Double).bitPattern)
        XCTAssertEqual(bytes, before)
    }
    func testPieceJSONCarriesNegativeZeroIntoDesktopGoldenHash() throws {
        let json = #"{"Id":"11111111-1111-1111-1111-111111111111","Name":"","Closed":true,"Points":[{"X":0,"Y":0},{"X":2,"Y":0},{"X":0,"Y":2}],"Notches":[{"VertexIndex":0,"Position":{"X":-0,"Y":0}}]}"#
        let input = Data(json.utf8), before = input
        let piece = try ExperimentalV7BitPiece.parse(input)
        XCTAssertEqual(try ExperimentalV7Fingerprint.hash([piece]), "2C986126E3E97F06E57FE292F6CDAAD617F595498C29E7320D448CFC70C5E716")
        XCTAssertEqual(piece.marks?.first?.position.x, (-0.0 as Double).bitPattern)
        XCTAssertEqual(input, before)
        XCTAssertThrowsError(try ExperimentalV7BitPiece.parse(input, cancelled: { true }))
    }
}
