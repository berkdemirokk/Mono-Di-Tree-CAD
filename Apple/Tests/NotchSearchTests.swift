import XCTest
@testable import TekstilCADViewer_iOS

final class NotchSearchTests: XCTestCase {
    func testVisibleNotchLabelCanBeSearched() {
        let label = NotchSearch.label(index: 0, x: "12,5", y: "-3")
        XCTAssertEqual(label, "Nokta 0 · X 12,5 · Y -3 mm")
        for query in ["nokta 0", "12,5", "-3", "mm", "0 X 12,5"] {
            XCTAssertTrue(NotchSearch.matches(query: query, index: 0, x: "12,5", y: "-3"))
        }
        XCTAssertFalse(NotchSearch.matches(query: "Nokta 1", index: 0, x: "12,5", y: "-3"))
    }
}
