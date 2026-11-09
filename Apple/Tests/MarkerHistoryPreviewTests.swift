import Foundation
import XCTest
@testable import TekstilCADViewer

final class MarkerHistoryPreviewTests: XCTestCase {
    private let sourceID = "11111111-1111-1111-1111-111111111111"
    private let placedID = "22222222-2222-2222-2222-222222222222"
    private let order = "Model 1-2-3052, size 38, one set each, interfacing width 80 cm, 30 seconds, send to plotter."

    private func project(sourceText: String? = nil, size: Int = 38, sets: Int = 1, cut: Int = 1) throws -> [String: Any] {
        let quantity = sets * cut
        let placed = (0..<quantity).map { index in index == 0 ? placedID : String(format: "22222222-2222-2222-2222-%012d", index) }
        let pieces: [[String: Any]] = placed.map { id in
            ["Id": id, "Name": "Test", "Closed": true,
             "Points": [["X": 0, "Y": 0], ["X": 100, "Y": 0], ["X": 0, "Y": 150]],
             "Metadata": ["Role": "Kesim", "Size": String(size), "Material": "Tela", "Quantity": 1]]
        }
        let hashPieces = placed.map { id -> ExperimentalV7Fingerprint.Piece in
            func point(_ x: Double, _ y: Double) -> ExperimentalV7Fingerprint.Point { .init(x: x.bitPattern, y: y.bitPattern) }
            return .init(id: UUID(uuidString: id)!, name: "Test", closed: true,
                         points: [point(0, 0), point(100, 0), point(0, 150)],
                         metadata: .init(role: "Kesim", size: String(size), material: "Tela", quantity: 1), grain: nil, marks: nil)
        }
        let fingerprint = try ExperimentalV7Fingerprint.hash(hashPieces)
        return ["Name": "Saved marker", "Pieces": pieces, "GradingRules": [], "NestingSnapshot": [
            "FabricWidth": 800, "UsedLength": 150, "Gap": 5, "EdgeMargin": 0,
            "Algorithm": "deterministic-shelf-v1", "LayoutFingerprint": fingerprint,
            "Placements": placed.enumerated().map { ["SourcePieceId": sourceID, "CopyNumber": $0.offset + 1, "PlacedPieceId": $0.element] as [String: Any] },
            "WorkOrderHistory": ["ModelCode": "1-2-3052", "SourceText": sourceText ?? order,
                "SizeQuantities": [["Size": size, "Sets": sets]],
                "PieceQuantities": [["SourcePieceId": sourceID, "Size": size, "CutQuantityPerSet": cut, "EffectiveQuantity": quantity]],
                "Source": ["SourcePath": "Z:\\does-not-exist\\source.tcad", "SourceSha256": String(repeating: "A", count: 64)]]]]
    }
    private func data(_ project: [String: Any]) throws -> Data {
        try JSONSerialization.data(withJSONObject: [11, project], options: [.sortedKeys])
    }
    private func changeHistory(_ source: [String: Any], _ edit: (inout [String: Any]) -> Void) -> [String: Any] {
        var project = source, snapshot = source["NestingSnapshot"] as! [String: Any]
        var history = snapshot["WorkOrderHistory"] as! [String: Any]
        edit(&history); snapshot["WorkOrderHistory"] = history; project["NestingSnapshot"] = snapshot
        return project
    }
    private func changeSnapshot(_ source: [String: Any], _ edit: (inout [String: Any]) -> Void) -> [String: Any] {
        var project = source, snapshot = source["NestingSnapshot"] as! [String: Any]
        edit(&snapshot); project["NestingSnapshot"] = snapshot; return project
    }

    func testTwoPartModelCodeSurvivesHistoricalRequestValidation() throws {
        let original = try project(sourceText: order.replacingOccurrences(of: "1-2-3052", with: "2-26558"))
        let changed = changeHistory(original) { $0["ModelCode"] = "2-26558" }
        let preview = try MarkerHistoryPreview.load(data(changed))
        XCTAssertEqual(preview.modelCode, "2-26558")
    }

    func testReadsHistoricalRequestWithoutOpeningLinkedSourceAndKeepsBytes() throws {
        let original = try data(project()), before = original
        let preview = try MarkerHistoryPreview.load(original)
        XCTAssertEqual(preview.modelCode, "1-2-3052")
        XCTAssertEqual(preview.sourceText, order)
        XCTAssertEqual(preview.sizeQuantities, [.init(size: 38, sets: 1)])
        XCTAssertEqual(preview.totalCopies, 1)
        XCTAssertEqual(preview.fabricWidthMM, 800)
        XCTAssertEqual(preview.source?.sourcePath, "Z:\\does-not-exist\\source.tcad")
        XCTAssertTrue(preview.layoutMatchesHistory)
        XCTAssertEqual(original, before)
    }
    func testReadsActualDesktopSavedFixture() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "desktop-marker-work-order-v11", withExtension: "tcad"))
        let original = try Data(contentsOf: url)
        let preview = try MarkerHistoryPreview.load(original)
        XCTAssertEqual(preview.modelCode, "1-2-3052")
        XCTAssertEqual(preview.sizeQuantities.map(\.size), [36, 38, 40, 42])
        XCTAssertEqual(preview.pieceQuantities.map(\.effectiveQuantity), [1, 2, 1, 1])
        XCTAssertEqual(preview.totalCopies, 5)
        XCTAssertEqual(preview.fabricWidthMM, 800)
        XCTAssertTrue(preview.layoutMatchesHistory)
        XCTAssertNotNil(preview.source)
        let drawing = try Drawing.load(original)
        XCTAssertEqual(drawing.formatVersion, 11)
        XCTAssertEqual(drawing.pieces.count, 5)
        XCTAssertEqual(drawing.markerHistory?.totalCopies, 5)
    }
    func testTurkishEnglishAndIndividualQuantities() throws {
        for text in [
            "1-2-3052, 38 beden, her bedenden 2 takim, tela eni 80 cm, yarim dakika",
            "Model 1-2-3052, size 38, 2 sets each, interfacing width 80 cm, half a minute",
            "Model 1-2-3052; size 38 2 sets; interfacing width 800 mm; 30 seconds",
            "Model bir tire iki tire 3052; 38 beden 2 takim; tela eni 0.8 m; 30 sn"
        ] {
            let preview = try MarkerHistoryPreview.load(data(project(sourceText: text, sets: 2, cut: 2)))
            XCTAssertEqual(preview.totalCopies, 4, text)
            XCTAssertEqual(preview.sizeQuantities, [.init(size: 38, sets: 2)], text)
        }
    }
    func testRejectsConflictingOrIncompleteSourceText() throws {
        let base = try project()
        for text in ["", "Model 1-2-3052", order + " not", order + " width 90 cm", order + " 40 42 sizes",
                     order.replacingOccurrences(of: "30 seconds", with: "30.5 seconds"),
                     order.replacingOccurrences(of: "size 38", with: "size 38.5"),
                     order.replacingOccurrences(of: "1-2-3052", with: "1-2-9999"),
                     order.replacingOccurrences(of: "one set each", with: "2 sets each")] {
            XCTAssertThrowsError(try MarkerHistoryPreview.load(data(changeHistory(base) { $0["SourceText"] = text })), text)
        }
    }
    func testRejectsMissingUnknownNullAndDuplicateHistoryFields() throws {
        let base = try project()
        for key in ["ModelCode", "SourceText", "SizeQuantities", "PieceQuantities"] {
            XCTAssertThrowsError(try MarkerHistoryPreview.load(data(changeHistory(base) { $0.removeValue(forKey: key) })))
            XCTAssertThrowsError(try MarkerHistoryPreview.load(data(changeHistory(base) { $0[key] = NSNull() })))
        }
        XCTAssertThrowsError(try MarkerHistoryPreview.load(data(changeHistory(base) { $0["Approved"] = true })))
        XCTAssertThrowsError(try MarkerHistoryPreview.load(data(changeHistory(base) { $0["Source"] = NSNull() })))
        let original = String(decoding: try data(base), as: UTF8.self)
        let duplicate = original.replacingOccurrences(of: "\"ModelCode\":", with: "\"ModelCode\":\"1-2-3052\",\"Model\\u0043ode\":")
        XCTAssertThrowsError(try MarkerHistoryPreview.load(Data(duplicate.utf8)))
    }
    func testRejectsNonIntegerTokensAndBooleans() throws {
        let original = String(decoding: try data(project()), as: UTF8.self)
        for key in ["Sets", "CutQuantityPerSet", "EffectiveQuantity", "CopyNumber", "Quantity"] {
            for token in ["1.0", "1e0", "true", "\"1\"", "2147483648", "1e-999"] {
                let altered = original.replacingOccurrences(of: "\"\(key)\":1", with: "\"\(key)\":\(token)")
                XCTAssertNotEqual(altered, original)
                XCTAssertThrowsError(try MarkerHistoryPreview.load(Data(altered.utf8)), "\(key): \(token)")
            }
        }
        for token in ["38.0", "38e0", "true", "\"38\""] {
            XCTAssertThrowsError(try MarkerHistoryPreview.load(Data(original.replacingOccurrences(of: "\"Size\":38", with: "\"Size\":\(token)").utf8)))
        }
    }
    func testRejectsCopyMappingAndWidthMismatch() throws {
        let base = try project()
        for width in [0, 801, 10_001] {
            XCTAssertThrowsError(try MarkerHistoryPreview.load(data(changeSnapshot(base) { $0["FabricWidth"] = width })))
        }
        for key in ["SourcePieceId", "PlacedPieceId", "CopyNumber"] {
            let altered = changeSnapshot(base) { snapshot in
                var placements = snapshot["Placements"] as! [[String: Any]]
                if key == "CopyNumber" { placements[0][key] = 2 }
                else { placements[0][key] = "00000000-0000-0000-0000-000000000000" }
                snapshot["Placements"] = placements
            }
            XCTAssertThrowsError(try MarkerHistoryPreview.load(data(altered)))
        }
        XCTAssertThrowsError(try MarkerHistoryPreview.load(data(changeSnapshot(base) { $0["Placements"] = [] })))
        XCTAssertThrowsError(try MarkerHistoryPreview.load(data(changeSnapshot(base) {
            let rows = $0["Placements"] as! [[String: Any]]; $0["Placements"] = rows + rows
        })))
    }
    func testRejectsQuantityProductsCoverageAndSizeOrder() throws {
        let base = try project()
        for key in ["CutQuantityPerSet", "EffectiveQuantity", "Size"] {
            let altered = changeHistory(base) { history in
                var pieces = history["PieceQuantities"] as! [[String: Any]]
                pieces[0][key] = 2; history["PieceQuantities"] = pieces
            }
            XCTAssertThrowsError(try MarkerHistoryPreview.load(data(altered)))
        }
        XCTAssertThrowsError(try MarkerHistoryPreview.load(data(changeHistory(base) {
            let pieces = $0["PieceQuantities"] as! [[String: Any]]; $0["PieceQuantities"] = pieces + pieces
        })))
        XCTAssertThrowsError(try MarkerHistoryPreview.load(data(changeHistory(base) {
            $0["SizeQuantities"] = [["Size": 38, "Sets": 1], ["Size": 40, "Sets": 1]]
        })))
    }
    func testSourceReferenceIsOptionalButStrictWhenPresent() throws {
        let base = try project()
        XCTAssertNil(try MarkerHistoryPreview.load(data(changeHistory(base) { $0.removeValue(forKey: "Source") })).source)
        for path in ["relative.tcad", "\\\\server\\share\\a.tcad", "C:\\..\\a.tcad", "C:\\/a.tcad", "C:\\a.tcad:stream", "C:\\a.txt", "C:\\trailing.\\a.tcad"] {
            XCTAssertThrowsError(try MarkerHistoryPreview.load(data(changeHistory(base) {
                $0["Source"] = ["SourcePath": path, "SourceSha256": String(repeating: "A", count: 64)]
            })), path)
        }
        XCTAssertThrowsError(try MarkerHistoryPreview.load(data(changeHistory(base) {
            $0["Source"] = ["SourcePath": "C:\\a.tcad", "SourceSha256": "wrong"]
        })))
    }
    func testStaleSnapshotStillChecksHistoricalCopies() throws {
        let base = try project()
        let stale = changeSnapshot(base) { $0["LayoutFingerprint"] = String(repeating: "A", count: 64) }
        XCTAssertFalse(try MarkerHistoryPreview.load(data(stale)).layoutMatchesHistory)
        XCTAssertThrowsError(try MarkerHistoryPreview.load(data(changeSnapshot(stale) { $0["Placements"] = [] })))
    }
    func testCurrentMetadataMustMatchOrder() throws {
        // Change the order material while retaining the exact current geometry hash.
        let base = try project()
        XCTAssertThrowsError(try MarkerHistoryPreview.load(data(changeHistory(base) {
            $0["SourceText"] = order.replacingOccurrences(of: "interfacing", with: "lining")
        })))
        // A stale history can still be displayed without pretending it describes current pieces.
        let stale = changeSnapshot(base) { $0["LayoutFingerprint"] = String(repeating: "A", count: 64) }
        XCTAssertFalse(try MarkerHistoryPreview.load(data(changeHistory(stale) {
            $0["SourceText"] = order.replacingOccurrences(of: "interfacing", with: "lining")
        })).layoutMatchesHistory)
    }
    func testRejectsUnsupportedExtraHistoryAndWrongEnvelope() throws {
        var base = try project(); base["PhotoTraceHistory"] = []
        XCTAssertThrowsError(try MarkerHistoryPreview.load(data(base)))
        base.removeValue(forKey: "PhotoTraceHistory")
        for version in [7, 10, 12] {
            XCTAssertThrowsError(try MarkerHistoryPreview.load(JSONSerialization.data(withJSONObject: [version, base])))
        }
        XCTAssertThrowsError(try MarkerHistoryPreview.load(data(changeSnapshot(base) { $0.removeValue(forKey: "WorkOrderHistory") })))
    }
    func testDrawingIntegrationRejectsInconsistentHistory() throws {
        let base = try project()
        XCTAssertThrowsError(try Drawing.load(data(changeSnapshot(base) { $0["FabricWidth"] = 900 })))
        XCTAssertThrowsError(try Drawing.load(data(changeHistory(base) { $0["SourceText"] = "Model 1-2-3052" })))
    }
    func testBoundedCancellationAndUTF16() throws {
        let original = try data(project())
        XCTAssertThrowsError(try MarkerHistoryPreview.load(original, cancelled: { true }))
        XCTAssertThrowsError(try MarkerHistoryPreview.load(Data(repeating: 32, count: 8 * 1024 * 1024 + 1)))
        let text = String(decoding: original, as: UTF8.self)
        var utf16 = Data([0xff, 0xfe]); utf16.append(try XCTUnwrap(text.data(using: .utf16LittleEndian)))
        XCTAssertEqual(try MarkerHistoryPreview.load(utf16).modelCode, "1-2-3052")
    }
}
