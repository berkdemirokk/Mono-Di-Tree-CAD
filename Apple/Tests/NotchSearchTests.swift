import XCTest
@testable import TekstilCADViewer_iOS

final class NotchSearchTests: XCTestCase {
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
