import Foundation
import XCTest
@testable import TekstilCADViewer_iOS

final class MeasurementDisplayTests: XCTestCase {
    func testZeroAndSmallNonzeroRemainDistinct() throws {
        let locale = Locale(identifier: "en_US")
        let zero = try XCTUnwrap(MeasurementDisplay.format(0, locale: locale))
        let small = try XCTUnwrap(MeasurementDisplay.format(0.004, locale: locale))
        XCTAssertEqual(zero, "0")
        XCTAssertNotEqual(small, zero)
        XCTAssertTrue(small.contains("4"))
        XCTAssertTrue(small.contains("3"))
        XCTAssertNotEqual(MeasurementDisplay.format(Double.leastNonzeroMagnitude, locale: locale), zero)
    }

    func testLocaleRoundingAndExtremeValues() throws {
        let turkish = Locale(identifier: "tr_TR")
        XCTAssertEqual(MeasurementDisplay.format(1.5, locale: turkish), "1,5")
        XCTAssertEqual(MeasurementDisplay.format(123.456789, locale: Locale(identifier: "en_US")), "123.457")
        let large = try XCTUnwrap(MeasurementDisplay.format(1e99, locale: turkish))
        XCTAssertTrue(large.contains("99"))
        XCTAssertLessThan(large.count, 30)
        XCTAssertNil(MeasurementDisplay.format(.infinity, locale: turkish))
        XCTAssertNil(MeasurementDisplay.format(.nan, locale: turkish))
    }
    func testLongDistanceRoundingIsExplicitlyExpected() throws {
        let formatted = try XCTUnwrap(MeasurementDisplay.format(100_000.1, locale: Locale(identifier: "en_US")))
        XCTAssertEqual(formatted, "100,000")
        XCTAssertNotEqual(formatted, "100,000.1")
    }
}
