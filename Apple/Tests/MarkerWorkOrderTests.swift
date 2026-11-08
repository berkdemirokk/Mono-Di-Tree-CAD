#if canImport(UIKit)
import XCTest
import PDFKit
@testable import TekstilCADViewer

final class MarkerWorkOrderTests: XCTestCase {
    private let request = "1-2-3 model, 36 beden, her bedenden 2 takım, tela, en 90 cm, 30 saniye"
    private func drawing(quantity: Int = 2, width: Double = 100, material: String = "Tela", size: String = "36", points: [[String: Double]]? = nil, annotated: Bool = false) throws -> Drawing {
        let contour = points ?? [["X": 0, "Y": 0], ["X": width, "Y": 0], ["X": width, "Y": 80], ["X": 0, "Y": 80]]
        var piece: [String: Any] = ["Id":"11111111-1111-1111-1111-111111111111", "Name":"Ön", "Closed":true, "Points":contour,
                                    "Metadata":["Role":"Kesim", "Size":size, "Material":material, "Quantity":quantity]]
        piece["Notches"] = [["VertexIndex":0, "Position":contour[0]]]
        if annotated {
            piece["Grainline"] = ["Start":["X":10, "Y":10], "End":["X":10, "Y":60], "NeedsReview":true]
            piece["Notches"] = [["VertexIndex":0, "Position":["X":0, "Y":0]]]
        }
        return try Drawing.load(JSONSerialization.data(withJSONObject: [6, ["Name":"Model", "Pieces":[piece], "GradingRules":[]]]))
    }
    func testTurkishAndEnglishRequestsHaveSameSemantics() {
        let tr = MarkerWorkOrderParser.parse(request)
        let en = MarkerWorkOrderParser.parse("model 1-2-3, sizes 36, 2 sets per size, interfacing, width 90 cm, 30 seconds")
        XCTAssertTrue(tr.complete, tr.issues.map(\.tr).joined(separator: ";"))
        XCTAssertTrue(en.complete, en.issues.map(\.en).joined(separator: ";"))
        XCTAssertEqual(tr.model, en.model); XCTAssertEqual(tr.sizes, en.sizes)
        XCTAssertEqual(tr.sets, 2); XCTAssertEqual(en.material, "Tela")
        XCTAssertEqual(en.width, 900); XCTAssertEqual(en.seconds, 30)
    }
    func testPerSizeQuantitiesAndConflicts() {
        let text = "1-2-3; 36 beden 2 takım; 38 beden 3 takım; tela, en 90 cm, 30 saniye"
        let order = MarkerWorkOrderParser.parse(text)
        XCTAssertTrue(order.complete, order.issues.map(\.tr).joined(separator: ";"))
        XCTAssertEqual(order.sizes, [36, 38]); XCTAssertEqual(order.sizeQuantities, [36:2, 38:3]); XCTAssertNil(order.sets)
        for bad in [text + "; her bedenden 2", text + "; 36 beden 4 takım", request + "; en 80 cm", request + "; astar", request + "; 4-5-6", request + "; 42 beden", request.replacingOccurrences(of: "36 beden", with: "38.5 beden")] {
            XCTAssertFalse(MarkerWorkOrderParser.parse(bad).complete, bad)
        }
    }
    func testEnglishPrefixSizeListsAcceptFieldPunctuationButRejectDecimals() {
        for field in ["sizes 36,", "sizes: 36;", "sizes 36/38,", "sizes 36,38,", "sizes 36, 38;"] {
            let order = MarkerWorkOrderParser.parse("model 1-2-3, \(field) 2 sets per size, interfacing, width 90 cm, 30 seconds")
            XCTAssertTrue(order.complete, "\(field): " + order.issues.map(\.en).joined(separator: ";"))
            XCTAssertEqual(order.sizes, field.contains("38") ? [36, 38] : [36])
        }
        for field in ["sizes 38.5,", "sizes 38,5,", "sizes 36,38.5,", "sizes 36,38,5,"] {
            XCTAssertFalse(MarkerWorkOrderParser.parse("model 1-2-3, \(field) 2 sets per size, interfacing, width 90 cm, 30 seconds").complete, field)
        }
    }
    func testEnglishQuantityPhrasesConsumeCompletePunctuatedRequests() {
        for (phrase, count) in [("2 sets per size", 2), ("one set each", 1), ("each size 2 sets", 2)] {
            for separator in [", ", ",", "; "] {
                let text = ["model 1-2-3", "sizes 36/38", phrase, "interfacing", "width 90 cm", "30 seconds"].joined(separator: separator)
                let order = MarkerWorkOrderParser.parse(text)
                XCTAssertTrue(order.complete, "\(text): " + order.issues.map(\.en).joined(separator: ";"))
                XCTAssertEqual(order.model, "1-2-3"); XCTAssertEqual(order.sizes, [36, 38])
                XCTAssertEqual(order.sets, count); XCTAssertEqual(order.material, "Tela")
                XCTAssertEqual(order.width, 900); XCTAssertEqual(order.seconds, 30)
            }
        }
        for phrase in ["2.5 sets per size", "2,5 sets per size", "0 sets per size"] {
            XCTAssertFalse(MarkerWorkOrderParser.parse("model 1-2-3, sizes 36, \(phrase), interfacing, width 90 cm, 30 seconds").complete)
            XCTAssertFalse(MarkerWorkOrderParser.parse("model 1-2-3,sizes 36/38,\(phrase),interfacing,width 90 cm,30 seconds").complete)
        }
    }
    func testParserRejectsLimitsNegationsAndPartialNumbers() {
        for bad in [String(repeating: "x", count: 12_001), request.replacingOccurrences(of: "2 takım", with: "1001 takım"), request.replacingOccurrences(of: "90 cm", with: "-90 cm"), request.replacingOccurrences(of: "30 saniye", with: "0.5 saniye"), request + " değil", request + " 123"] {
            XCTAssertFalse(MarkerWorkOrderParser.parse(bad).complete)
        }
    }
    func testRequestStoragePreservesExactSourceAndDesktopEnvelope() throws {
        let raw = "  \(request)\n  "
        let bytes = try MarkerRequestStorage.data(source: raw)
        let json = try XCTUnwrap(JSONSerialization.jsonObject(with: bytes) as? [String: Any])
        XCTAssertEqual(Set(json.keys), Set(["format", "version", "source"]))
        XCTAssertEqual(json["source"] as? String, raw)
        XCTAssertEqual(json["format"] as? String, "tekstilcad.marker-request")
        XCTAssertEqual(json["version"] as? Int, 1)
        XCTAssertThrowsError(try MarkerRequestStorage.data(source: " \n "))
    }
    func testV5RequiresOriginalBytesAndRejectsUnknownFieldsOrMismatchedPreview() throws {
        let bytes = Data(#"[5,{"Name":"Model","GradingRules":[],"Pieces":[{"Id":"11111111-1111-1111-1111-111111111111","Name":"Ön","Closed":true,"Points":[{"X":0,"Y":0},{"X":100,"Y":0},{"X":100,"Y":80},{"X":0,"Y":80}],"Metadata":{"Role":"Kesim","Size":"36","Material":"Tela","Quantity":2}}]}]"#.utf8)
        let preview = try Drawing.load(bytes)
        let order = MarkerWorkOrderParser.parse(request)
        XCTAssertThrowsError(try MarkerLayoutEngine.calculate(drawing: preview, order: order, confirmed: true))
        XCTAssertEqual(try MarkerLayoutEngine.calculate(drawing: preview, sourceData: bytes, order: order, confirmed: true).placements.count, 4)
        let unknown = Data(String(decoding: bytes, as: UTF8.self).replacingOccurrences(of: "\"Closed\":true", with: "\"UnknownMark\":{},\"Closed\":true").utf8)
        XCTAssertThrowsError(try MarkerLayoutEngine.calculate(drawing: Drawing.load(unknown), sourceData: unknown, order: order, confirmed: true))
        let changed = Data(String(decoding: bytes, as: UTF8.self).replacingOccurrences(of: "\"Quantity\":2", with: "\"Quantity\":3").utf8)
        XCTAssertThrowsError(try MarkerLayoutEngine.calculate(drawing: preview, sourceData: changed, order: order, confirmed: true))
    }
    func testLayoutMultipliesSetsWithoutMutatingSourceAndDoesNotOverlap() throws {
        let source = try drawing()
        let oldPoints = source.pieces[0].points.map { [$0.x, $0.y] }
        let layout = try MarkerLayoutEngine.calculate(drawing: source, order: MarkerWorkOrderParser.parse(request), confirmed: true)
        XCTAssertEqual(layout.placements.count, 4)
        XCTAssertEqual(source.pieces[0].quantity, 2)
        XCTAssertEqual(source.pieces[0].points.map { [$0.x, $0.y] }, oldPoints)
        for i in layout.placements.indices {
            let a = layout.placements[i].bounds
            XCTAssertGreaterThanOrEqual(a.minX, 0); XCTAssertLessThanOrEqual(a.maxX, layout.width)
            for j in layout.placements.indices where j > i {
                let b = layout.placements[j].bounds
                XCTAssertTrue(a.maxX <= b.minX || b.maxX <= a.minX || a.maxY <= b.minY || b.maxY <= a.minY)
            }
        }
    }
    func testMissingSourceConfirmationMetadataAndBoundsFail() throws {
        let order = MarkerWorkOrderParser.parse(request)
        XCTAssertThrowsError(try MarkerLayoutEngine.calculate(drawing: nil, order: order, confirmed: true))
        XCTAssertThrowsError(try MarkerLayoutEngine.calculate(drawing: drawing(), order: order, confirmed: false))
        XCTAssertThrowsError(try MarkerLayoutEngine.calculate(drawing: drawing(quantity: 251), order: order, confirmed: true))
        XCTAssertThrowsError(try MarkerLayoutEngine.calculate(drawing: drawing(width: 899), order: order, confirmed: true))
        XCTAssertThrowsError(try MarkerLayoutEngine.calculate(drawing: drawing(material: ""), order: order, confirmed: true))
        XCTAssertThrowsError(try MarkerLayoutEngine.calculate(drawing: drawing(size: ""), order: order, confirmed: true))
        XCTAssertThrowsError(try MarkerLayoutEngine.calculate(drawing: drawing(), order: order, confirmed: true, cancelled: { true }))
        let crossed = [["X":0.0, "Y":0.0], ["X":100.0, "Y":80.0], ["X":0.0, "Y":80.0], ["X":100.0, "Y":0.0]]
        XCTAssertThrowsError(try MarkerLayoutEngine.calculate(drawing: drawing(points: crossed), order: order, confirmed: true))
    }
    func testPDFIncludesUnusedFabricWidthA4ScaleAndAnnotations() throws {
        let layout = try MarkerLayoutEngine.calculate(drawing: drawing(annotated: true), order: MarkerWorkOrderParser.parse(request), confirmed: true)
        let plan = try MarkerPDFExporter.pagePlan(layout)
        XCTAssertEqual(plan.columns, 5) // 900 mm fabric, even though four copies use only 416 mm.
        XCTAssertEqual(plan.rows, 1)
        XCTAssertGreaterThanOrEqual(plan.widthCovered, layout.width)
        XCTAssertGreaterThanOrEqual(plan.heightCovered, layout.length)
        XCTAssertEqual(MarkerPDFExporter.calibrationMM * MarkerPDFExporter.pointsPerMM, 141.73228346456693, accuracy: 0.000001)
        let bytes = try MarkerPDFExporter.data(layout: layout, english: true)
        let pdf = try XCTUnwrap(PDFDocument(data: bytes))
        XCTAssertEqual(pdf.pageCount, plan.count)
        let page = try XCTUnwrap(pdf.page(at: 0))
        XCTAssertEqual(page.bounds(for: .mediaBox).width, 210 * MarkerPDFExporter.pointsPerMM, accuracy: 0.01)
        XCTAssertEqual(page.bounds(for: .mediaBox).height, 297 * MarkerPDFExporter.pointsPerMM, accuracy: 0.01)
        let text = pdf.string ?? ""
        XCTAssertTrue(text.contains("50 mm CHECK")); XCTAssertTrue(text.contains("ROW 1/1"))
        XCTAssertTrue(text.contains("Grain review pending: 4")); XCTAssertTrue(text.contains("11111111-1111-1111-1111-111111111111"))
        XCTAssertThrowsError(try MarkerPDFExporter.data(layout: layout, english: false, cancelled: { true }))
    }
}
#endif
