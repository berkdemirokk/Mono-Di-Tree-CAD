import XCTest
@testable import TekstilCADViewer_iOS

final class ExperimentalV7FingerprintTests: XCTestCase {
    private func point(_ x: Double, _ y: Double) -> ExperimentalV7Fingerprint.Point { .init(x: x.bitPattern, y: y.bitPattern) }
    private func source(markerZero: Double = 0) -> ExperimentalV7Fingerprint.Piece {
        .init(id: UUID(uuidString: "2a8e2548-f12f-4033-aa06-e9dc8cbc982f")!, name: "Deneme ön", closed: true,
              points: [point(0,0),point(60,0),point(60,40),point(0,40)],
              metadata: .init(role: "Kesim", size: nil, material: "Pamuk", quantity: 2),
              grain: .init(start: point(10,10), end: point(10,20), needsReview: false),
              marks: [.init(index: 1, position: point(60,markerZero))])
    }
    func testMatchesDesktopSavedV7SourceFingerprint() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "desktop-synthetic-rotated-v7", withExtension: "tcad"))
        let root = try XCTUnwrap(try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [Any])
        let project = try XCTUnwrap(root[1] as? [String: Any])
        let snapshot = try XCTUnwrap(project["NestingSnapshot"] as? [String: Any])
        let trace = try XCTUnwrap(snapshot["RotationTrace"] as? [String: Any])
        let expected = try XCTUnwrap(trace["SourceFingerprint"] as? String)
        XCTAssertEqual(expected, "F50BCB21B76FFB4FD4E3DB3B68FD021A85A3BF5CA4D04B46B2C5EB26460D50E5")
        XCTAssertEqual(try ExperimentalV7Fingerprint.hash([source()]), expected)
    }
    func testSignedZeroAndMetadataPresenceChangeTheFingerprint() throws {
        let positive = try ExperimentalV7Fingerprint.hash([source()])
        let negative = try ExperimentalV7Fingerprint.hash([source(markerZero: -0.0)])
        XCTAssertNotEqual(positive, negative)
        let p = source()
        let missingMetadata = ExperimentalV7Fingerprint.Piece(id: p.id, name: p.name, closed: p.closed, points: p.points,
                                                              metadata: nil, grain: p.grain, marks: p.marks)
        XCTAssertNotEqual(positive, try ExperimentalV7Fingerprint.hash([missingMetadata]))
        XCTAssertThrowsError(try ExperimentalV7Fingerprint.hash([source()], cancelled: { true }))
    }
}
