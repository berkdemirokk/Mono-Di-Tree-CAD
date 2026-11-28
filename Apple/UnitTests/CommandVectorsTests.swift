import XCTest
@testable import TekstilCADViewer

/// Runs every case of the shared, language-neutral vector file against the Swift parser. The .NET
/// parser answers the same file (tests/TekstilCAD.Tests/Commands/CommandVectorsTests.cs), so the
/// two parsers cannot drift apart. Differences that are platform facts (Y direction, origin
/// corner, rotation direction) are not parser matters: the parser's answer is in screen terms.
final class CommandVectorsTests: XCTestCase {
    private func loadFile() throws -> [String: Any] {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "command-vectors", withExtension: "json"),
                                "command-vectors.json must be a resource of the unit-test bundle")
        let data = try Data(contentsOf: url)
        return try XCTUnwrap(JSONSerialization.jsonObject(with: data) as? [String: Any])
    }

    func testVectorFileHasTheDocumentedShape() throws {
        let root = try loadFile()
        XCTAssertFalse((root["_comment"] as? String ?? "").isEmpty)
        let contexts = try XCTUnwrap(root["contexts"] as? [String: [String]])
        for name in ["B0", "B1", "B8"] { XCTAssertNotNil(contexts[name], name) }
        let cases = try XCTUnwrap(root["cases"] as? [[String: Any]])
        XCTAssertGreaterThanOrEqual(cases.count, 400)
        for item in cases {
            XCTAssertNotNil(item["input"] as? String)
            let context = item["context"] as? String ?? ""
            XCTAssertNotNil(contexts[context], "unknown context \(context)")
            let expect = item["expect"] as? [String: Any] ?? [:]
            XCTAssertTrue((expect["kind"] != nil) != (expect["refusal"] != nil), "expect needs exactly one of kind/refusal")
        }
    }

    func testEveryVectorMatchesTheParser() throws {
        let root = try loadFile()
        let contexts = try XCTUnwrap(root["contexts"] as? [String: [String]])
        let cases = try XCTUnwrap(root["cases"] as? [[String: Any]])
        var failures: [String] = []
        for (index, item) in cases.enumerated() {
            let input = item["input"] as? String ?? ""
            let contextName = item["context"] as? String ?? "B0"
            let names = contexts[contextName] ?? []
            let result = CommandParser.parse(input, context: ParserContext(pieceNames: names))
            let expect = item["expect"] as? [String: Any] ?? [:]
            if let failure = compare(expect, result) {
                let shown = input.replacingOccurrences(of: "\n", with: "\\n").replacingOccurrences(of: "\t", with: "\\t")
                failures.append("[\(index)] \"\(shown)\" (\(contextName)): \(failure)")
            }
        }
        XCTAssertTrue(failures.isEmpty,
                      "\(failures.count) of \(cases.count) vectors differ:\n" + failures.prefix(120).joined(separator: "\n"))
    }

    // MARK: Comparison

    private func compare(_ expect: [String: Any], _ result: ParseResult) -> String? {
        if let code = expect["refusal"] as? String {
            if let command = result.command { return "expected refusal \(code), got command \(describe(command))" }
            guard let notice = result.notice else { return "no result" }
            if notice.code != code { return "expected refusal \(code), got \(notice.code): \(notice.message)" }
            if let incomplete = expect["incomplete"] as? Bool, incomplete != result.isIncomplete {
                return "incomplete: expected \(incomplete), got \(result.isIncomplete)"
            }
            if let buttons = expect["buttons"] as? [String] {
                let got = notice.buttons.map { $0.label }
                if buttons != got { return "buttons: expected [\(buttons.joined(separator: " | "))], got [\(got.joined(separator: " | "))]" }
            }
            if let fills = expect["fills"] as? [String] {
                let got = notice.buttons.map { $0.fillText }
                if fills != got { return "fills: expected [\(fills.joined(separator: " | "))], got [\(got.joined(separator: " | "))]" }
            }
            return nil
        }

        guard let command = result.command else {
            return "expected \(expect["kind"] as? String ?? "?"), got refusal \(result.notice?.code ?? "?"): \(result.notice?.message ?? "")"
        }
        let actual = canonical(command)
        let kind = expect["kind"] as? String
        if actual["kind"] as? String != kind { return "expected kind \(kind ?? "?"), got \(describe(command))" }
        if let assumed = expect["unitAssumed"] as? Bool, assumed != result.unitAssumed {
            return "unitAssumed: expected \(assumed), got \(result.unitAssumed)"
        }
        var keys = Set(actual.keys)
        for key in expect.keys { keys.insert(key) }
        for key in keys where key != "kind" && key != "unitAssumed" {
            let have = actual[key]
            var want: Any? = expect[key]
            if want is NSNull { want = nil }
            if want == nil && have == nil { continue }
            guard let want, let have else {
                return "field \(key): expected \(want.map { "\($0)" } ?? "absent"), got \(describe(command))"
            }
            if let number = have as? Double {
                guard let expected = (want as? NSNumber)?.doubleValue, abs(expected - number) <= 1e-9 else {
                    return "field \(key): expected \(want), got \(number)"
                }
            } else if let number = have as? Int {
                guard let expected = (want as? NSNumber)?.intValue, expected == number else {
                    return "field \(key): expected \(want), got \(number)"
                }
            } else if let flag = have as? Bool {
                guard let expected = (want as? NSNumber)?.boolValue, expected == flag else {
                    return "field \(key): expected \(want), got \(flag)"
                }
            } else if let text = have as? String {
                guard let expected = want as? String, expected == text else {
                    return "field \(key): expected \(want), got \(text)"
                }
            }
        }
        return nil
    }

    private func describe(_ command: ParsedCommand) -> String {
        canonical(command).sorted { $0.key < $1.key }.map { "\($0.key)=\($0.value)" }.joined(separator: ", ")
    }

    /// The neutral field names and values documented in the vector file; absent fields are missing.
    private func canonical(_ command: ParsedCommand) -> [String: Any] {
        var d: [String: Any] = [:]
        switch command {
        case .rectangle(let width, let height, let isSquare):
            d["kind"] = "Rect"; d["width"] = width; d["height"] = height
            if isSquare { d["square"] = true }
        case .line(let length, let orientation):
            d["kind"] = "Line"; d["length"] = length; d["orientation"] = String(describing: orientation)
        case .moveBy(let right, let down):
            d["kind"] = "Move"; d["right"] = right; d["down"] = down
        case .moveTo(let x, let y):
            d["kind"] = "MoveTo"; d["x"] = x; d["y"] = y
        case .rotate(let degrees, let direction):
            d["kind"] = "Rotate"; d["degrees"] = degrees
            d["direction"] = direction == .clockwise ? "clockwise" : "counterclockwise"
        case .mirror(let axis):
            d["kind"] = "Mirror"; d["axis"] = String(describing: axis)
        case .resize(let width, let height):
            d["kind"] = "Resize"
            if let width { d["width"] = width }
            if let height { d["height"] = height }
        case .rename(let name):
            d["kind"] = "Rename"; d["name"] = name
        case .delete(let target, let name):
            d["kind"] = "Delete"; d["target"] = String(describing: target)
            if let name { d["name"] = name }
        case .select(let target, let name):
            d["kind"] = "Select"; d["target"] = String(describing: target)
            if let name { d["name"] = name }
        case .undo(let steps):
            d["kind"] = "Undo"; d["steps"] = steps
        case .redo:
            d["kind"] = "Redo"
        case .view(let action):
            d["kind"] = "View"; d["action"] = String(describing: action)
        case .tool(let tool):
            d["kind"] = "Tool"; d["tool"] = String(describing: tool)
        case .finishDrawing(let action):
            d["kind"] = "FinishDrawing"; d["action"] = String(describing: action)
        case .file(let action):
            d["kind"] = "File"; d["action"] = String(describing: action)
        case .handoff(let target, let tab, let value):
            d["kind"] = "Handoff"; d["target"] = String(describing: target)
            if let tab { d["tab"] = tab }
            if let value { d["value"] = value }
        case .help:
            d["kind"] = "Help"
        case .cancelPreview:
            d["kind"] = "CancelPreview"
        case .measuredApron(let width, let length):
            d["kind"] = "MeasuredApron"; d["width"] = width; d["length"] = length
        case .markerBrief:
            d["kind"] = "MarkerBrief"
        case .plotterCalibration:
            d["kind"] = "PlotterCalibration"
        case .previewDrawing:
            d["kind"] = "PreviewDrawing"
        case .addStudioPattern(let pattern):
            d["kind"] = "StudioPattern"; d["name"] = pattern.rawValue
        case .selectAt, .deleteAt:
            d["kind"] = "PieceChoice"
        }
        return d
    }
}
