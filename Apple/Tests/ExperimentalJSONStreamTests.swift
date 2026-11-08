import Foundation
import XCTest
@testable import TekstilCADViewer

final class ExperimentalJSONStreamTests: XCTestCase {
    private func validate(_ input: Data, chunkSize: Int) throws {
        var scanner = ExperimentalJSONStream()
        var offset = 0
        while offset < input.count {
            let end = min(input.count, offset + chunkSize)
            try scanner.feed(input.subdata(in: offset..<end)); offset = end
        }
        try scanner.finish()
    }
    func testSplitTokensAndUTF8AtEveryBoundary() throws {
        let bytes = Data(#"{"isim":"Ön 👩‍💻","escaped":"\uD83D\uDE00","values":[-0.125e2,true,null,{"x":2}]}"#.utf8)
        for size in [1, 2, 3, 7, bytes.count] { try validate(bytes, chunkSize: size) }
    }
    func testDesktopV7FixtureCanBeScannedInSmallChunks() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "desktop-synthetic-rotated-v7", withExtension: "tcad"))
        try validate(Data(contentsOf: url), chunkSize: 17)
    }
    func testRejectsEscapedDuplicateKeysEvenWhenSplit() {
        let bytes = Data(#"{"Name":1,"\u004Eame":2}"#.utf8)
        XCTAssertThrowsError(try validate(bytes, chunkSize: 1))
    }
    func testKeysCompareDecodedScalarsWithoutUnicodeNormalization() throws {
        let distinct = Data(#"{"é":1,"e\u0301":2}"#.utf8)
        for size in [1, 2, 7, distinct.count] { try validate(distinct, chunkSize: size) }
        let same = Data(#"{"é":1,"\u00E9":2}"#.utf8)
        XCTAssertThrowsError(try validate(same, chunkSize: 1))
        let supplementary = Data(#"{"😀":1,"\uD83D\uDE00":2}"#.utf8)
        XCTAssertThrowsError(try validate(supplementary, chunkSize: 2))
    }
    func testRejectsMalformedStructureNumbersAndTruncatedUnicode() {
        for text in [#"{"a":1,}"#, #"[1,]"#, #"[1e-9999]"#, #"{"a":1} {}"#, #"[01]"#] {
            XCTAssertThrowsError(try validate(Data(text.utf8), chunkSize: 2), text)
        }
        XCTAssertThrowsError(try validate(Data([0x22, 0xe2, 0x82, 0x22]), chunkSize: 1))
        XCTAssertThrowsError(try validate(Data(#""\uD800""#.utf8), chunkSize: 1))
        XCTAssertThrowsError(try validate(Data(#""\uDC00""#.utf8), chunkSize: 2))
    }
    func testLimitsAndCancellationDoNotReturnPartialSuccess() throws {
        var size = ExperimentalJSONStream(maximumBytes: 3)
        XCTAssertThrowsError(try size.feed(Data("[123]".utf8)))
        var token = ExperimentalJSONStream(maximumTokenBytes: 4)
        XCTAssertThrowsError(try token.feed(Data("\"1234\"".utf8)))
        var depth = ExperimentalJSONStream(maximumDepth: 2)
        XCTAssertThrowsError(try depth.feed(Data("[[[]]]".utf8)))
        var keys = ExperimentalJSONStream(maximumKeysPerObject: 1)
        XCTAssertThrowsError(try keys.feed(Data(#"{"a":1,"b":2}"#.utf8)))
        var cancelled = ExperimentalJSONStream(); var calls = 0
        XCTAssertThrowsError(try cancelled.feed(Data("[1,2,3]".utf8), cancelled: { calls += 1; return calls == 4 }))
        XCTAssertThrowsError(try cancelled.finish(), "A cancelled partial scan must not be accepted")
    }
}
