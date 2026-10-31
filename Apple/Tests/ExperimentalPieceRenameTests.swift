import Foundation
import XCTest
@testable import TekstilCADViewer_iOS

final class ExperimentalPieceRenameTests: XCTestCase {
    private let first = UUID(uuidString: "11111111-1111-1111-1111-111111111111")!
    private let second = UUID(uuidString: "22222222-2222-2222-2222-222222222222")!
    private func edit(_ text: String, id: UUID? = nil, snapshot: Data? = nil) throws -> String {
        let data = Data(text.utf8)
        let output = try ExperimentalPieceRename.replacing(in: data, snapshot: snapshot ?? data, pieceID: id ?? second, with: "Yeni \"ad\"")
        return try XCTUnwrap(String(data: output, encoding: .utf8))
    }
    func testPreservesAllOtherBytes() throws {
        let prefix = "[5, {\"Name\":\"project\",\"Pieces\":[{\"Id\":\"11111111-1111-1111-1111-111111111111\",\"Name\":\"first\"},{\"Id\":\"22222222-2222-2222-2222-222222222222\",\"Na\\u006de\": "
        let suffix = ",\"Extra\":{\"Name\":\"untouched\",\"number\":1.2300e+04}}],\"Unknown\":true}]\n"
        XCTAssertEqual(try edit(prefix + "\"old\"" + suffix), prefix + "\"Yeni \\\"ad\\\"\"" + suffix)
    }
    func testRejectsAmbiguityAndInvalidIdentity() {
        let id1 = "11111111-1111-1111-1111-111111111111"
        let id2 = "22222222-2222-2222-2222-222222222222"
        let cases = [
            "[6,{\"Pieces\":[{\"Id\":\"\(id2)\",\"Name\":\"a\"}]}]",
            "{\"Pieces\":[{\"Name\":\"a\"}]}",
            "{\"Pieces\":[{\"Id\":\"00000000-0000-0000-0000-000000000000\",\"Name\":\"a\"}]}",
            "{\"Pieces\":[{\"Id\":\"\(id2)\",\"Name\":\"a\"},{\"Id\":\"\(id2)\",\"Name\":\"b\"}]}",
            "{\"Pieces\":[{\"Id\":\"\(id2)\",\"Name\":\"a\",\"Na\\u006de\":\"b\"}]}",
            "{\"Pieces\":[{\"Id\":\"\(id2)\",\"Name\":\"a\",\"Unknown\":{\"x\":1,\"x\":2}}]}",
            "{\"Pieces\":[{\"Id\":\"\(id1)\",\"Name\":\"a\"}]}"
        ]
        for input in cases { XCTAssertThrowsError(try edit(input), input) }
    }
    func testRejectsChangedSnapshot() {
        let input = "{\"Pieces\":[{\"Id\":\"22222222-2222-2222-2222-222222222222\",\"Name\":\"old\"}]}"
        XCTAssertThrowsError(try edit(input, snapshot: Data("different".utf8)))
    }
    func testRejectsOverlongName() {
        let input = Data("{\"Pieces\":[{\"Id\":\"22222222-2222-2222-2222-222222222222\",\"Name\":\"old\"}]}".utf8)
        XCTAssertThrowsError(try ExperimentalPieceRename.replacing(in: input, snapshot: input, pieceID: second, with: String(repeating: "😀", count: 513)))
    }
}
