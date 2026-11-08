import XCTest
@testable import TekstilCADViewer

final class NotchSearchTests: XCTestCase {
    func testDrawingLimitIgnoresUndrawnPiecesAndUsesStrictLimit() {
        XCTAssertFalse(NotchSearch.exceedsDrawingLimit([(false, 900), (true, 500)], limit: 500))
        XCTAssertTrue(NotchSearch.exceedsDrawingLimit([(false, 900), (true, 500), (true, 1)], limit: 500))
        XCTAssertFalse(NotchSearch.exceedsDrawingLimit([(false, 900)], limit: 500))
    }

    func testPieceSearchUsesNameAndSizeInOrderAndCanCancel() throws {
        let items = [("Ön Kol", "Parça 1 · Beden: S"), ("Arka Kol", "Parça 2 · Beden: XL"),
                     ("Ön Kol", "Parça 3 · Beden: XL")]
        let matching = try XCTUnwrap(ModelSearch.matchingIndices(count: items.count, query: "xl on kol", cancelled: { false }) { items[$0] })
        XCTAssertEqual(matching, [2])
        let all = try XCTUnwrap(ModelSearch.matchingIndices(count: items.count, query: "  ", cancelled: { false }) { items[$0] })
        XCTAssertEqual(all, [0, 1, 2])
        var calls = 0
        let cancelled = ModelSearch.matchingIndices(count: 10_000, query: "kol", cancelled: { calls >= 10 }) { _ in
            calls += 1
            return ("Kol", "Beden: M")
        }
        XCTAssertNil(cancelled)
        XCTAssertEqual(calls, 10)
    }

    func testCancellationStopsScanWithoutReturningPartialResults() {
        let groups = [Array(0..<100_000)]
        var visited = 0
        let result = NotchSearch.boundedMatchesCancellable(groups: groups, limit: 500, cancelled: { visited >= 37 }) { _, _ in
            visited += 1
            return false
        }
        XCTAssertNil(result)
        XCTAssertEqual(visited, 37)
    }

    func testLargeNoMatchScanPerformance() {
        let groups = Array(repeating: Array(0..<10_000), count: 20)
        let query = NotchSearch.Query("bulunmayan")
        measure {
            let result = NotchSearch.boundedMatchesCancellable(groups: groups, limit: 500, cancelled: { false }) { _, point in
                query.matches(pieceName: "Ön Kol", details: "Beden: 38", index: point, x: "12", y: "34")
            }
            XCTAssertEqual(result?.groups.count, 20)
            XCTAssertFalse(result?.hasMore ?? true)
        }
    }

    func testBoundedMatchesKeepsOrderAndSearchesPastVisibleLimit() {
        let groups = [Array(0..<500), [500, 501]]
        let first = NotchSearch.boundedMatches(groups: groups, limit: 500) { _, _ in true }
        XCTAssertEqual(first.groups[0], Array(0..<500))
        XCTAssertTrue(first.groups[1].isEmpty)
        XCTAssertTrue(first.hasMore)
        let exact = NotchSearch.boundedMatches(groups: [Array(0..<500)], limit: 500) { _, _ in true }
        XCTAssertFalse(exact.hasMore)
        let refined = NotchSearch.boundedMatches(groups: groups, limit: 500) { _, point in point == 501 }
        XCTAssertEqual(refined.groups[1], [501])
        XCTAssertFalse(refined.hasMore)
    }

    func testPreparedQueryChecksPointNumberAndHandlesOverflow() {
        let query = NotchSearch.Query("kol 38 nokta 12")
        XCTAssertTrue(query.allows(index: 12))
        XCTAssertFalse(query.allows(index: 1))
        XCTAssertTrue(query.matches(pieceName: "Ön Kol", details: "Beden: 38", index: 12, x: "1", y: "2"))
        XCTAssertFalse(NotchSearch.Query("nokta 999999999999999999999999999999").allows(index: 12))
        XCTAssertFalse(NotchSearch.Query("nokta 1 nokta 2").allows(index: 1))
    }

    func testNotchSearchCombinesPieceDetailsAndPointLabel() {
        func matches(_ query: String, point: Int = 2) -> Bool {
            NotchSearch.matches(query: query, pieceName: "Ön Kol", details: "Beden: 38",
                                index: point, x: "12,5", y: "-3")
        }
        XCTAssertTrue(matches("on kol nokta 2"))
        XCTAssertTrue(matches("38 12,5"))
        XCTAssertTrue(matches("kol · -3"))
        XCTAssertFalse(matches("arka 12,5"))
        XCTAssertFalse(matches("kol nokta 9"))
        XCTAssertFalse(NotchSearch.matches(query: "kol nokta 1", pieceName: "Kol", details: "Parça 1 · Beden 10",
                                           index: 0, x: "10", y: "1"))
        XCTAssertFalse(NotchSearch.matches(query: "kol nokta 1", pieceName: "Kol", details: "Parça 1",
                                           index: 10, x: "0", y: "0"))
        XCTAssertTrue(NotchSearch.matches(query: "kol nokta 1", pieceName: "Kol", details: "Parça 1",
                                          index: 1, x: "0", y: "0"))
        XCTAssertTrue(matches("kol 38"))
    }
    func testPieceNamesAndDetailsMatchWithoutAccents() {
        XCTAssertTrue(ModelSearch.matches("Ön Beden", query: "on beden"))
        XCTAssertTrue(ModelSearch.matches("Ölçü: Çiçek", query: "olcu"))
        XCTAssertTrue(ModelSearch.matches("Malzeme: Pamuk", query: "pamuk"))
        XCTAssertFalse(ModelSearch.matches("Arka Beden", query: "on beden"))
    }
    func testLibrarySearchMatchesWordsAcrossNameAndIdentityInAnyOrder() {
        let name = "Ön Beden Kol"
        let identities = ["On Beden - ABC123.tcad", "tcad · ABC123"]
        func matches(_ query: String) -> Bool {
            ModelSearch.matchesLibrary(name: name, identities: identities, query: query)
        }
        XCTAssertTrue(matches("kol on"))
        XCTAssertTrue(matches("ABC123 beden"))
        XCTAssertTrue(matches("  "))
        XCTAssertFalse(matches("kol arka"))
        XCTAssertFalse(matches("ABC999 beden"))
    }

    func testAdjacentNotchNavigationUsesFileOrderWithoutWrapping() {
        let points = [8, 2, 12]
        XCTAssertNil(NotchSearch.adjacent(in: [], current: nil, forward: true))
        XCTAssertEqual(NotchSearch.adjacent(in: points, current: nil, forward: true), 8)
        XCTAssertEqual(NotchSearch.adjacent(in: points, current: nil, forward: false), 12)
        XCTAssertEqual(NotchSearch.adjacent(in: points, current: 8, forward: true), 2)
        XCTAssertEqual(NotchSearch.adjacent(in: points, current: 12, forward: false), 2)
        XCTAssertNil(NotchSearch.adjacent(in: points, current: 12, forward: true))
        XCTAssertNil(NotchSearch.adjacent(in: points, current: 8, forward: false))
        XCTAssertEqual(NotchSearch.adjacent(in: points, current: 99, forward: true), 8)
    }

    func testVisibleNotchLabelCanBeSearched() {
        let label = NotchSearch.label(index: 0, x: "12,5", y: "-3")
        XCTAssertEqual(label, "Nokta 0 · X 12,5 · Y -3 mm")
        for query in ["nokta 0", "12,5", "-3", "mm", "0 X 12,5", "Nokta 0 X 12,5", "X 12,5 Y -3", "Nokta 0 · X 12,5"] {
            XCTAssertTrue(NotchSearch.matches(query: query, index: 0, x: "12,5", y: "-3"))
        }
        XCTAssertFalse(NotchSearch.matches(query: "Nokta 1", index: 0, x: "12,5", y: "-3"))
    }

    func testSmallNotchCoordinatesRemainVisibleAndSearchable() {
        let value = NotchSearch.coordinate(0.0004, locale: Locale(identifier: "en_US"))
        XCTAssertNotEqual(value, "0")
        XCTAssertTrue(NotchSearch.matches(query: value, index: 0, x: value, y: "0"))
    }
}
