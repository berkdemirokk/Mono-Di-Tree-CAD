import Foundation
import XCTest
@testable import TekstilCADViewer_iOS

final class LosslessProjectNameTests: XCTestCase {
    private func replace(_ input: String, with name: String = "New") throws -> String {
        let result = try LosslessProjectName.replacing(in: Data(input.utf8), with: name)
        return try XCTUnwrap(String(data: result, encoding: .utf8))
    }

    func testSupportedVersionsAndBareProject() throws {
        for version in 1...5 {
            let input = "[\(version),{\"Name\":\"Old\",\"Pieces\":[]}]"
            XCTAssertEqual(try replace(input), "[\(version),{\"Name\":\"New\",\"Pieces\":[]}]")
        }
        XCTAssertEqual(try replace("{\"Name\":\"Old\"}"), "{\"Name\":\"New\"}")
    }

    func testPreservesBytesAroundEscapedProjectKeyAndNestedNames() throws {
        let prefix = " \n[ 5, { \"Metadata\": {\"Name\":\"nested\"}, \"Na\\u006de\" : "
        let suffix = ", \"Other\": [ {\"Name\":\"inner\"}, 1.2300e+04 ], \"tail\":true } ]\r\n"
        let input = prefix + "\"old\\nvalue\"" + suffix
        XCTAssertEqual(try replace(input), prefix + "\"New\"" + suffix)
    }

    func testPreservesLargeUnrelatedValue() throws {
        let large = String(repeating: "0123456789", count: 100_000)
        let input = "{\"blob\":\"\(large)\",\"Name\":\"Old\",\"after\":12345678901234567890}"
        let expected = "{\"blob\":\"\(large)\",\"Name\":\"New\",\"after\":12345678901234567890}"
        XCTAssertEqual(try replace(input), expected)
    }

    func testRejectsDuplicateProjectNameIncludingEscapedKey() {
        for input in ["{\"Name\":\"a\",\"Name\":\"b\"}",
                      "[1,{\"Name\":\"a\",\"Na\\u006de\":\"b\"}]"] {
            XCTAssertThrowsError(try replace(input), input)
        }
    }

    func testRejectsUnsupportedVersionAndInvalidShapes() {
        for input in ["[6,{\"Name\":\"a\"}]", "[0,{\"Name\":\"a\"}]",
                      "[true,{\"Name\":\"a\"}]", "[1.5,{\"Name\":\"a\"}]",
                      "[1,{\"Name\":\"a\"},0]", "{\"Other\":1}",
                      "{\"Name\":null}"] {
            XCTAssertThrowsError(try replace(input), input)
        }
    }

    func testNameLimitCountsUTF16CodeUnits() throws {
        let input = "{\"Name\":\"old\"}"
        let atLimit = String(repeating: "😀", count: 512)
        let output = try LosslessProjectName.replacing(in: Data(input.utf8), with: atLimit)
        let object = try XCTUnwrap(JSONSerialization.jsonObject(with: output) as? [String: String])
        XCTAssertEqual(object["Name"], atLimit)
        XCTAssertThrowsError(try replace(input, with: atLimit + "a"))
    }

    func testRejectsOversizedEditWithoutChangingOriginal() {
        let original = Data(("{\"Name\":\"old\",\"padding\":\"" + String(repeating: "x", count: 8 * 1024 * 1024) + "\"}").utf8)
        let before = original
        XCTAssertThrowsError(try LosslessProjectName.replacing(in: original, with: "New"))
        XCTAssertEqual(original, before)
    }

    func testRejectsMalformedInputAndHandlesEncoding() throws {
        for input in ["{\"Name\":\"unterminated}", "{\"Name\":\"a\",}",
                      "{\"Name\":\"a\"} trailing", "{\"Name\":\"\\q\"}"] {
            XCTAssertThrowsError(try replace(input), input)
        }
        XCTAssertThrowsError(try LosslessProjectName.replacing(in: Data([0xFF]), with: "New"))
        let bom = Data([0xEF, 0xBB, 0xBF])
        let input = bom + Data("{\"Name\":\"a\"}".utf8)
        let expected = bom + Data("{\"Name\":\"New\"}".utf8)
        XCTAssertEqual(try LosslessProjectName.replacing(in: input, with: "New"), expected)
        let utf16 = Data([0xFF, 0xFE]) + "{\"Name\":\"a\"}".data(using: .utf16LittleEndian)!
        XCTAssertThrowsError(try LosslessProjectName.replacing(in: utf16, with: "New"))
    }
}
