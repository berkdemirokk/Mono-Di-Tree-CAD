import XCTest
@testable import TekstilCADViewer_iOS

final class ContourMeasureTests: XCTestCase {
    private func p(_ x: Double, _ y: Double) -> ContourMeasure.Point { .init(x: x, y: y) }

    func testDuplicatePieceNamesCanBeFoundByTheirDisplayedIdentity() throws {
        let json = #"[5,{"Name":"Test","Pieces":[{"Id":"11111111-1111-1111-1111-111111111111","Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":0}]},{"Id":"22222222-2222-2222-2222-222222222222","Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":2,"Y":0}]}]}]"#
        let pieces = try Drawing.load(Data(json.utf8)).pieces
        let hits = try XCTUnwrap(ModelSearch.matchingIndices(count: pieces.count, query: "22222222-2222-2222-2222-222222222222", cancelled: { false }) { index in
            (pieces[index].displayName(index: index), pieces[index].selectionDetails(index: index))
        })
        XCTAssertEqual(hits, [1])
        XCTAssertTrue(pieces[1].selectionDetails(index: 1).contains("Kimlik: 22222222-2222-2222-2222-222222222222"))
    }
    func testComparisonReportIncludesOnlySpansBoundToTheirSelectedPieces() throws {
        let json = #"[5,{"Name":"Test","Pieces":[{"Name":"A","Closed":false,"Points":[{"X":0,"Y":0},{"X":100,"Y":0}]},{"Name":"B","Closed":false,"Points":[{"X":0,"Y":0},{"X":50,"Y":0}]}]}]"#
        let drawing = try Drawing.load(Data(json.utf8))
        let first = ContourMeasure.SpanSelection(pieceIndex: 0, start: "0", end: "1")
        let second = ContourMeasure.SpanSelection(pieceIndex: 1, start: "0", end: "1")
        let report = try XCTUnwrap(PieceComparisonReport.make(pieces: drawing.pieces, projectName: drawing.name, first: 0, second: 1, firstSpan: first, secondSpan: second, locale: Locale(identifier: "en_US")))
        XCTAssertTrue(report.contains("Birinci aralık: 0 → 1 · açık kontur · yaklaşık 100 mm"))
        XCTAssertTrue(report.contains("İkinci aralık: 0 → 1 · açık kontur · yaklaşık 50 mm"))
        XCTAssertTrue(report.contains("Aralık farkı (ikinci − birinci): -50 mm"))
        let stale = try XCTUnwrap(PieceComparisonReport.make(pieces: drawing.pieces, projectName: drawing.name, first: 1, second: 1, firstSpan: first, secondSpan: second))
        XCTAssertTrue(stale.contains("Birinci aralık: seçilmedi veya hesaplanamadı"))
        XCTAssertTrue(stale.contains("Aralık farkı (ikinci − birinci): hesaplanamadı"))
    }
    func testComparisonReportIdentifiesPiecesAndStatesItsLimits() throws {
        let json = #"[5,{"Name":"Test","Pieces":[{"Name":"Ön\u202EKol","Closed":false,"Points":[{"X":0,"Y":0},{"X":100,"Y":200}]},{"Name":"Arka","Closed":false,"Points":[{"X":0,"Y":0},{"X":50,"Y":100}]}]}]"#
        let drawing = try Drawing.load(Data(json.utf8))
        let report = try XCTUnwrap(PieceComparisonReport.make(pieces: drawing.pieces, projectName: drawing.name, first: 0, second: 1, locale: Locale(identifier: "en_US")))
        XCTAssertTrue(report.contains(#"Ön\u{202E}Kol"#))
        XCTAssertTrue(report.contains("Proje: Test"))
        XCTAssertTrue(report.contains("Dosya sırası: 1 / 2"))
        XCTAssertTrue(report.contains("Dosya sırası: 2 / 2"))
        XCTAssertTrue(report.contains("En: -50 mm"))
        XCTAssertTrue(report.contains("üretime uygunluk doğrulaması değildir"))
        XCTAssertNil(PieceComparisonReport.make(pieces: drawing.pieces, projectName: drawing.name, first: -1, second: 1))
        let same = try XCTUnwrap(PieceComparisonReport.make(pieces: drawing.pieces, projectName: drawing.name, first: 0, second: 0))
        XCTAssertTrue(same.contains("Aynı parça iki kez seçildi"))
    }
    func testComparisonDifferenceIsSignedAndRejectsUnavailableOrOverflow() {
        XCTAssertEqual(ContourMeasure.difference(first: 100, second: 50), -50)
        XCTAssertEqual(ContourMeasure.difference(first: 50, second: 100), 50)
        XCTAssertNil(ContourMeasure.difference(first: nil, second: 100))
        XCTAssertNil(ContourMeasure.difference(first: .infinity, second: 100))
        XCTAssertNil(ContourMeasure.difference(first: -Double.greatestFiniteMagnitude, second: Double.greatestFiniteMagnitude))
    }
    func testCollapsedRequiresMultipleIdenticalPoints() {
        XCTAssertFalse(ContourMeasure.isCollapsed([]))
        XCTAssertFalse(ContourMeasure.isCollapsed([p(1, 2)]))
        XCTAssertTrue(ContourMeasure.isCollapsed([p(1, 2), p(1, 2), p(1, 2)]))
        XCTAssertFalse(ContourMeasure.isCollapsed([p(1, 2), p(1, 2), p(1, 3)]))
    }

    func testClosedContourRequiresThreeDistinctVerticesBeyondClosingAlias() {
        XCTAssertFalse(ContourMeasure.hasMinimumContourPoints([p(0, 0), p(0, 0), p(1, 0)], closed: true))
        XCTAssertFalse(ContourMeasure.hasMinimumContourPoints([p(0, 0), p(1, 0), p(1, 0), p(0, 0)], closed: true))
        XCTAssertTrue(ContourMeasure.hasMinimumContourPoints([p(0, 0), p(1, 0), p(0, 1), p(0, 0)], closed: true))
        XCTAssertTrue(ContourMeasure.hasMinimumContourPoints([p(0, 0), p(0, 0)], closed: false))
    }

    func testOpenClosedAndExplicitClosingPoint() throws {
        let triangle = [p(0, 0), p(3, 0), p(3, 4)]
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.length(triangle, closed: false)), 7, accuracy: 1e-12)
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.length(triangle, closed: true)), 12, accuracy: 1e-12)
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.length(triangle + [p(0, 0)], closed: true)), 12, accuracy: 1e-12)
    }

    func testZeroTwoPointsAndFiniteLimits() throws {
        XCTAssertEqual(ContourMeasure.length([], closed: true), 0)
        XCTAssertEqual(ContourMeasure.length([p(1, 1), p(1, 1)], closed: false), 0)
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.length([p(0, 0), p(3, 4)], closed: true)), 10, accuracy: 1e-12)
        XCTAssertNotNil(ContourMeasure.length([p(1e99, 0), p(-1e99, 0)], closed: false))
        XCTAssertNil(ContourMeasure.length([p(.infinity, 0)], closed: false))
        XCTAssertNil(ContourMeasure.length([p(-Double.greatestFiniteMagnitude, 0), p(Double.greatestFiniteMagnitude, 0)], closed: false))
    }

    func testStraightDistanceUsesSelectedFileIndicesAndRejectsInvalidValues() throws {
        let points = [p(0, 0), p(3, 4), p(0, 0)]
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.distance(points, from: 0, to: 1)), 5, accuracy: 1e-12)
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.distance(points, from: 0, to: 2)), 0, accuracy: 1e-12)
        XCTAssertNil(ContourMeasure.distance(points, from: -1, to: 0))
        XCTAssertNil(ContourMeasure.distance(points, from: 0, to: 3))
        XCTAssertNil(ContourMeasure.distance([p(.infinity, 0), p(0, 0)], from: 0, to: 1))
        XCTAssertNil(ContourMeasure.distance([p(-Double.greatestFiniteMagnitude, 0), p(Double.greatestFiniteMagnitude, 0)], from: 0, to: 1))
    }
    func testSpanSelectionIsBoundToPieceAndValidIndices() throws {
        let triangle = [p(0, 0), p(3, 0), p(3, 4), p(0, 0)]
        var selection = ContourMeasure.SpanSelection(pieceIndex: 2)
        XCTAssertNil(selection.length(points: triangle, closed: true, pieceIndex: 2))
        selection.start = "3"; selection.end = "1"
        XCTAssertEqual(try XCTUnwrap(selection.length(points: triangle, closed: true, pieceIndex: 2)), 3, accuracy: 1e-12)
        XCTAssertNil(selection.length(points: triangle, closed: true, pieceIndex: 1))
        selection.forward = false
        XCTAssertEqual(try XCTUnwrap(selection.length(points: triangle, closed: true, pieceIndex: 2)), 9, accuracy: 1e-12)
        selection.end = "999"
        XCTAssertNil(selection.length(points: triangle, closed: true, pieceIndex: 2))
    }
    func testHighlightedPathUsesSameOpenAndClosedVertexOrder() throws {
        let triangle = [p(0, 0), p(3, 0), p(3, 4)]
        XCTAssertEqual(ContourMeasure.pathIndices(triangle, from: 2, to: 0, closed: false), [0, 1, 2])
        XCTAssertEqual(ContourMeasure.pathIndices(triangle, from: 0, to: 1, closed: true), [0, 1])
        XCTAssertEqual(ContourMeasure.pathIndices(triangle, from: 0, to: 1, closed: true, forward: false), [1, 2, 0])
        XCTAssertEqual(ContourMeasure.pathIndices(triangle + [p(0, 0)], from: 3, to: 1, closed: true), [0, 1])
        XCTAssertEqual(ContourMeasure.pathIndices(triangle + [p(0, 0)], from: 3, to: 0, closed: true), [0])
        XCTAssertNil(ContourMeasure.pathIndices(triangle, from: 3, to: 0, closed: true))
    }
    func testPathBetweenVerticesHandlesOpenClosedAndClosingAlias() throws {
        let triangle = [p(0, 0), p(3, 0), p(3, 4)]
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.pathLength(triangle, from: 2, to: 0, closed: false)), 7, accuracy: 1e-12)
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.pathLength(triangle, from: 0, to: 1, closed: true)), 3, accuracy: 1e-12)
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.pathLength(triangle, from: 0, to: 1, closed: true, forward: false)), 9, accuracy: 1e-12)
        let repeated = triangle + [p(0, 0)]
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.pathLength(repeated, from: 3, to: 1, closed: true)), 3, accuracy: 1e-12)
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.pathLength(repeated, from: 3, to: 0, closed: true, forward: false)), 0, accuracy: 1e-12)
        XCTAssertNil(ContourMeasure.pathLength(triangle, from: -1, to: 0, closed: true))
        XCTAssertNil(ContourMeasure.pathLength([p(.infinity, 0), p(0, 0)], from: 0, to: 0, closed: false))
        XCTAssertNil(ContourMeasure.pathLength([p(0, 0), p(0, 0)], from: 0, to: 1, closed: true))
    }
    func testTenThousandPoints() throws {
        let points = (0..<10_000).map { p(Double($0), 0) }
        XCTAssertEqual(try XCTUnwrap(ContourMeasure.length(points, closed: false)), 9_999, accuracy: 1e-9)
    }
}
