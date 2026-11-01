import XCTest
@testable import TekstilCADViewer_iOS

final class NotchSearchTests: XCTestCase {
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
}
