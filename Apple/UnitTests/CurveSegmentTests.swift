import XCTest
@testable import TekstilCADViewer

final class CurveSegmentTests: XCTestCase {

    func testLineSegmentLengthAndPoints() {
        let line = CurveSegment.line(start: DraftPoint(x: 0, y: 0), end: DraftPoint(x: 3, y: 4))
        XCTAssertEqual(line.length, 5, accuracy: 1e-9)
        XCTAssertEqual(line.pointAt(0), DraftPoint(x: 0, y: 0))
        XCTAssertEqual(line.pointAt(1), DraftPoint(x: 3, y: 4))
        XCTAssertEqual(line.pointAt(0.5), DraftPoint(x: 1.5, y: 2))
        XCTAssertEqual(line.pointAt(-0.5), DraftPoint(x: 0, y: 0))   // clamped
        XCTAssertEqual(line.pointAt(1.5), DraftPoint(x: 3, y: 4))   // clamped
        XCTAssertEqual(line.sampled(), [DraftPoint(x: 0, y: 0), DraftPoint(x: 3, y: 4)])
        XCTAssertEqual(line.startPoint, DraftPoint(x: 0, y: 0))
        XCTAssertEqual(line.endPoint, DraftPoint(x: 3, y: 4))
    }

    func testCubicBezierEndpointsAndMidpoint() {
        let bezier = CurveSegment.cubicBezier(
            p0: DraftPoint(x: 0, y: 0),
            p1: DraftPoint(x: 0, y: 10),
            p2: DraftPoint(x: 10, y: 10),
            p3: DraftPoint(x: 10, y: 0))
        XCTAssertEqual(bezier.pointAt(0), DraftPoint(x: 0, y: 0))
        XCTAssertEqual(bezier.pointAt(1), DraftPoint(x: 10, y: 0))
        let mid = bezier.pointAt(0.5)
        XCTAssertEqual(mid.x, 5, accuracy: 1e-9)
        XCTAssertEqual(mid.y, 7.5, accuracy: 1e-9)
        let sampled = bezier.sampled()
        XCTAssertEqual(sampled.first, DraftPoint(x: 0, y: 0))
        XCTAssertEqual(sampled.last, DraftPoint(x: 10, y: 0))
        XCTAssertGreaterThan(sampled.count, 2)
        XCTAssertGreaterThan(bezier.length, 0)
    }

    func testBezierLengthGreaterThanChord() {
        let bezier = CurveSegment.cubicBezier(
            p0: DraftPoint(x: 0, y: 0),
            p1: DraftPoint(x: 1, y: 2),
            p2: DraftPoint(x: 3, y: 2),
            p3: DraftPoint(x: 4, y: 0))
        let line = CurveSegment.line(start: DraftPoint(x: 0, y: 0), end: DraftPoint(x: 4, y: 0))
        XCTAssertGreaterThan(bezier.length, line.length)
        XCTAssertEqual(line.length, 4, accuracy: 1e-9)
    }

    func testArcQuarterCircle() {
        let arc = CurveSegment.arc(
            center: DraftPoint(x: 0, y: 0),
            radius: 10,
            startAngle: 0,
            sweepAngle: .pi / 2)
        XCTAssertEqual(arc.length, 10 * .pi / 2, accuracy: 1e-9)
        XCTAssertEqual(arc.pointAt(0), DraftPoint(x: 10, y: 0))
        let end = arc.pointAt(1)
        XCTAssertEqual(end.x, 0, accuracy: 1e-6)
        XCTAssertEqual(end.y, 10, accuracy: 1e-6)
        let sampled = arc.sampled()
        XCTAssertEqual(sampled.first?.x, 10, accuracy: 1e-6)
        XCTAssertEqual(sampled.first?.y, 0, accuracy: 1e-6)
        XCTAssertEqual(sampled.last?.x, 0, accuracy: 1e-6)
        XCTAssertEqual(sampled.last?.y, 10, accuracy: 1e-6)
    }

    func testArcZeroSweep() {
        let arc = CurveSegment.arc(
            center: DraftPoint(x: 0, y: 0),
            radius: 10,
            startAngle: 0,
            sweepAngle: 0)
        XCTAssertEqual(arc.length, 0)
        XCTAssertEqual(arc.sampled().count, 1)
        XCTAssertEqual(arc.sampled().first, DraftPoint(x: 10, y: 0))
    }

    func testContourGeometrySampledPoints() {
        let contour = ContourGeometry(
            segments: [
                .line(start: DraftPoint(x: 0, y: 0), end: DraftPoint(x: 10, y: 0)),
                .line(start: DraftPoint(x: 10, y: 0), end: DraftPoint(x: 10, y: 10)),
            ],
            closed: true)
        let pts = contour.sampledPoints
        XCTAssertEqual(pts.first, DraftPoint(x: 0, y: 0))
        XCTAssertEqual(pts.last, DraftPoint(x: 0, y: 0))  // closed -> first repeated
        XCTAssertEqual(contour.totalLength, 20, accuracy: 1e-9)
        XCTAssertEqual(contour.bounds, (0, 0, 10, 10))
    }

    func testContourGeometryOpen() {
        let contour = ContourGeometry(
            segments: [
                .line(start: DraftPoint(x: 0, y: 0), end: DraftPoint(x: 10, y: 0)),
            ],
            closed: false)
        let pts = contour.sampledPoints
        XCTAssertEqual(pts.count, 2)
        XCTAssertEqual(contour.totalLength, 10, accuracy: 1e-9)
    }
}
