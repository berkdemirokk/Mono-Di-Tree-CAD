import XCTest
@testable import TekstilCADViewer

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
    func testDesktopGoldenVectorsAcrossFingerprintDomains() throws {
        let id = try XCTUnwrap(UUID(uuidString: "11111111-1111-1111-1111-111111111111"))
        let vertices = [point(0, 0), point(2, 0), point(0, 2)]
        let grain = ExperimentalV7Fingerprint.Grain(start: point(0, 0), end: point(0, 1), needsReview: false)
        let marks = [ExperimentalV7Fingerprint.Mark(index: 0, position: point(-0.0, 0))]
        func piece(_ grain: ExperimentalV7Fingerprint.Grain?, _ marks: [ExperimentalV7Fingerprint.Mark]?) -> ExperimentalV7Fingerprint.Piece {
            .init(id: id, name: "", closed: true, points: vertices, metadata: nil, grain: grain, marks: marks)
        }
        XCTAssertEqual(try ExperimentalV7Fingerprint.hash([]), "02DCC7E7AAF28CA0FE202CFD74120EBE3E632D05BD0F223F1062CAE017403512")
        XCTAssertEqual(try ExperimentalV7Fingerprint.hash([piece(nil, nil)]), "848B759E3594CA2531F44B8A0DF24BB517AB5E175EB15034C4D636FD82FC48C3")
        XCTAssertEqual(try ExperimentalV7Fingerprint.hash([piece(grain, nil)]), "8D26074E3A10769EFB71C838D67DE88266A64AF2629F8D1EF18D9A6328E6E592")
        XCTAssertEqual(try ExperimentalV7Fingerprint.hash([piece(nil, marks)]), "2C986126E3E97F06E57FE292F6CDAAD617F595498C29E7320D448CFC70C5E716")
        XCTAssertEqual(try ExperimentalV7Fingerprint.hash([piece(grain, marks)]), "08C036A851D01410883D3F235AD311DCCADBD73F62AAEDE061202C9E81C02A44")
    }
}
