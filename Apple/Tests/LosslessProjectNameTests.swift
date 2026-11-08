import Foundation
import XCTest
@testable import TekstilCADViewer

final class LosslessProjectNameTests: XCTestCase {
    func testUniqueKeyScanCanCancelWithinLongString() {
        let text = "{\"Name\":\"" + String(repeating: "a", count: 40_000) + "\",\"Pieces\":[]}"
        var calls = 0
        XCTAssertThrowsError(try LosslessProjectName.validateUniqueKeys(Data(text.utf8), strictV6: false, cancelled: {
            calls += 1
            return calls >= 4
        })) { error in XCTAssertTrue(error is CancellationError) }
        XCTAssertEqual(calls, 4)
    }
    func testNameEditorDetectsNonUTF8ByteOrderMarks() {
        XCTAssertFalse(LosslessProjectName.hasNonUTF8BOM(Data("[1,{}]".utf8)))
        XCTAssertFalse(LosslessProjectName.hasNonUTF8BOM(Data([0xef, 0xbb, 0xbf]) + Data("[1,{}]".utf8)))
        let boms: [[UInt8]] = [[0xff, 0xfe], [0xfe, 0xff], [0xff, 0xfe, 0, 0], [0, 0, 0xfe, 0xff]]
        for bom in boms {
            XCTAssertTrue(LosslessProjectName.hasNonUTF8BOM(Data(bom)))
        }
    }

    func testVersionSixRejectsNonzeroNumbersThatUnderflowToZero() throws {
        for token in ["1e-999", "-1e-999", "0.0000000000000000001e-999"] {
            let input = "[6,{\"Name\":\"Model\",\"Pieces\":[{\"Points\":[{\"X\":\(token),\"Y\":0}]}]}]"
            XCTAssertThrowsError(try LosslessProjectName.validateUniqueKeys(Data(input.utf8)), token)
        }
        for token in ["0", "-0.0", "0e-999", "5e-324"] {
            let input = "[6,{\"Name\":\"Model\",\"Pieces\":[{\"Points\":[{\"X\":\(token),\"Y\":0}]}]}]"
            XCTAssertNoThrow(try LosslessProjectName.validateUniqueKeys(Data(input.utf8)), token)
        }
        let grading = #"[6,{"Name":"Model","Pieces":[],"GradingRules":[{"Deltas":[{"X":1e-999,"Y":0}]}]}]"#
        XCTAssertThrowsError(try LosslessProjectName.validateUniqueKeys(Data(grading.utf8)))
    }

    func testVersionSevenAndTenNumberTokensRejectUnderflowButAcceptZeroAndSubnormal() {
        for version in [7, 10] {
            for token in ["1e-400", "-1e-400", "0.000001e-999"] {
                let input = "[\(version),{\"Name\":\"Model\",\"Pieces\":[{\"Points\":[{\"X\":\(token),\"Y\":0}]}]}]"
                XCTAssertThrowsError(try LosslessProjectName.validateUniqueKeys(Data(input.utf8), strictVersion: version), "version \(version): \(token)")
            }
            for token in ["0", "-0.0", "0e-999", "5e-324"] {
                let input = "[\(version),{\"Name\":\"Model\",\"Pieces\":[{\"Points\":[{\"X\":\(token),\"Y\":0}]}]}]"
                XCTAssertNoThrow(try LosslessProjectName.validateUniqueKeys(Data(input.utf8), strictVersion: version), "version \(version): \(token)")
            }
        }
    }

    func testQuantityTokenRuleIsLimitedToVersionTen() {
        for token in ["1.0", "1e0"] {
            for version in [7, 10] {
                let input = "[\(version),{\"Name\":\"Model\",\"Pieces\":[{\"Metadata\":{\"Quantity\":\(token)}}]}]"
                if version == 7 { XCTAssertNoThrow(try LosslessProjectName.validateUniqueKeys(Data(input.utf8), strictVersion: version)) }
                else { XCTAssertThrowsError(try LosslessProjectName.validateUniqueKeys(Data(input.utf8), strictVersion: version)) }
            }
        }
    }

    func testVersionSixValidationAcceptsBOMEncodingsWithoutChangingSource() throws {
        let text = #"[6,{"Name":"Ön","Pieces":[{"Notches":[{"VertexIndex":1,"Position":{"X":4,"Y":0}}]}]}]"#
        let encodings: [(String.Encoding, [UInt8])] = [
            (.utf16LittleEndian, [0xff, 0xfe]), (.utf16BigEndian, [0xfe, 0xff]),
            (.utf32LittleEndian, [0xff, 0xfe, 0, 0]), (.utf32BigEndian, [0, 0, 0xfe, 0xff])
        ]
        for (encoding, bom) in encodings {
            var original = Data(bom)
            original.append(try XCTUnwrap(text.data(using: encoding)))
            let snapshot = original
            let validation = try LosslessProjectName.versionSixValidationData(original)
            XCTAssertNoThrow(try LosslessProjectName.validateUniqueKeys(validation))
            XCTAssertEqual(original, snapshot)
            let duplicate = #"[6,{"Name":"Ön","Name":"Arka","Pieces":[]}]"#
            var invalid = Data(bom)
            invalid.append(try XCTUnwrap(duplicate.data(using: encoding)))
            XCTAssertThrowsError(try LosslessProjectName.validateUniqueKeys(LosslessProjectName.versionSixValidationData(invalid)))
        }
    }

    func testVersionSixValidationEnforcesDecodedSizeLimit() throws {
        let text = "[6,{\"Name\":\"" + String(repeating: "€", count: 3_000_000) + "\",\"Pieces\":[]}]"
        var encoded = Data([0xfe, 0xff])
        encoded.append(try XCTUnwrap(text.data(using: .utf16BigEndian)))
        let original = encoded
        XCTAssertLessThan(encoded.count, LosslessProjectName.maximumEditBytes)
        XCTAssertTrue(LosslessProjectName.requiresVersionSixRawRecovery(encoded))
        XCTAssertThrowsError(try LosslessProjectName.versionSixValidationData(encoded))
        XCTAssertEqual(encoded, original)
        let legacy = text.replacingOccurrences(of: "[6,", with: "[5,")
        var legacyBytes = Data([0xfe, 0xff])
        legacyBytes.append(try XCTUnwrap(legacy.data(using: .utf16BigEndian)))
        XCTAssertFalse(LosslessProjectName.requiresVersionSixRawRecovery(legacyBytes))
        var small = Data([0xff, 0xfe])
        small.append(try XCTUnwrap("[6,{}]".data(using: .utf16LittleEndian)))
        XCTAssertFalse(LosslessProjectName.requiresVersionSixRawRecovery(small))
    }

    func testOversizedVersionSixPreflightRecognizesExactEncodedEnvelope() {
        let limit = LosslessProjectName.maximumEditBytes
        func sample(_ prefix: [UInt8], count: Int) -> Data {
            var data = Data(prefix)
            if count > data.count { data.append(Data(repeating: 32, count: count - data.count)) }
            return data
        }
        XCTAssertFalse(LosslessProjectName.isOversizedVersionSix(sample(Array("[6,".utf8), count: limit)))
        XCTAssertTrue(LosslessProjectName.isOversizedVersionSix(sample(Array("[ 6 ,".utf8), count: limit + 1)))
        XCTAssertTrue(LosslessProjectName.isOversizedVersionSix(sample([0xef, 0xbb, 0xbf] + Array("\n[6,".utf8), count: limit + 1)))
        XCTAssertFalse(LosslessProjectName.isOversizedVersionSix(Data(repeating: 32, count: limit + 1)))
        XCTAssertFalse(LosslessProjectName.isOversizedVersionSix(sample(Array("[5,".utf8), count: limit + 1)))
        let encodings: [(String.Encoding, [UInt8])] = [
            (.utf16LittleEndian, [0xff, 0xfe]), (.utf16BigEndian, [0xfe, 0xff]),
            (.utf32LittleEndian, [0xff, 0xfe, 0x00, 0x00]), (.utf32BigEndian, [0x00, 0x00, 0xfe, 0xff])
        ]
        for (encoding, bom) in encodings {
            let prefix = bom + Array(" \n[ 6 ,".data(using: encoding)!)
            XCTAssertTrue(LosslessProjectName.isOversizedVersionSix(sample(prefix, count: limit + 1)))
            let decimal = bom + Array("[6.0,".data(using: encoding)!)
            XCTAssertFalse(LosslessProjectName.isOversizedVersionSix(sample(decimal, count: limit + 1)))
            let other = bom + Array("[60,".data(using: encoding)!)
            XCTAssertFalse(LosslessProjectName.isOversizedVersionSix(sample(other, count: limit + 1)))
            let late = bom + Array((String(repeating: " ", count: 5000) + "[ \n 6 ,").data(using: encoding)!)
            XCTAssertTrue(LosslessProjectName.isOversizedVersionSix(sample(late, count: limit + 1)))
        }
        let tooLate = Array(String(repeating: " ", count: 4095).utf8) + Array("[6,".utf8)
        XCTAssertTrue(LosslessProjectName.isOversizedVersionSix(sample(tooLate, count: limit + 1)))
        XCTAssertFalse(LosslessProjectName.isOversizedVersionSix(sample(Array("[6.0,".utf8), count: limit + 1)))
        XCTAssertFalse(LosslessProjectName.isOversizedVersionSix(sample(Array("[6,".utf8), count: 64 * 1024 * 1024 + 1)))
        XCTAssertFalse(LosslessProjectName.requiresVersionSixRawRecovery(sample(Array("[6,".utf8), count: 64 * 1024 * 1024 + 1)))
    }

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

    func testReplacementEscapesSpecialCharactersWithoutChangingOtherBytes() throws {
        let prefix = "[5, {\"Name\" : "
        let suffix = ", \"other\": {\"Name\":\"untouched\"}}]"
        let name = "quote\" slash\\ newline\n emoji😀"
        let output = try LosslessProjectName.replacing(in: Data((prefix + "\"old\"" + suffix).utf8), with: name)
        let text = try XCTUnwrap(String(data: output, encoding: .utf8))
        XCTAssertTrue(text.hasPrefix(prefix))
        XCTAssertTrue(text.hasSuffix(suffix))
        let root = try XCTUnwrap(JSONSerialization.jsonObject(with: output) as? [Any])
        let project = try XCTUnwrap(root[1] as? [String: Any])
        XCTAssertEqual(project["Name"] as? String, name)
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
