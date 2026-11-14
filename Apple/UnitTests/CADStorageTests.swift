import XCTest
@testable import TekstilCADViewer

final class CADStorageTests: XCTestCase {
    private func temporaryFile() -> URL {
        FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
    }

    func testWindowsFixtureAndExporterAgreeOnIdentifiersAndExactVertices() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "mobile-simple", withExtension: "tcad"))
        let bytes = try Data(contentsOf: url)
        let fixture = try XCTUnwrap(CADSketchImport.loadEditableCopy(from: bytes, jsonRoot: JSONSerialization.jsonObject(with: bytes)))
        let encoded = try CADSketchImport.encode(fixture)
        let reopened = try XCTUnwrap(CADSketchImport.loadEditableCopy(from: encoded, jsonRoot: JSONSerialization.jsonObject(with: encoded)))
        XCTAssertEqual(reopened.name, fixture.name)
        XCTAssertEqual(reopened.pieces, fixture.pieces)
        XCTAssertEqual(reopened.pieces[0].points[2], DraftPoint(x: 100, y: 150))
    }

    func testReadOnlyForAmbiguousMetadataOrCoordinate() throws {
        let bytes = try CADSketchImport.encode(.example)
        let json = try XCTUnwrap(String(data: bytes, encoding: .utf8))
        let reference = try XCTUnwrap(json.range(of: "\"GradingRules\":[]"))
        for replacement in ["\"GradingRules\":[1],\"GradingRules\":[]", "\"Layers\":[],\"GradingRules\":[]"] {
            let data = Data(json.replacingCharacters(in: reference, with: replacement).utf8)
            XCTAssertNil(CADSketchImport.loadEditableCopy(from: data, jsonRoot: try JSONSerialization.jsonObject(with: data)))
        }
    }

    func testLegacyDraftKeepsCoordinatesAndUnknownFieldsAreRejected() throws {
        let url = temporaryFile()
        defer { try? FileManager.default.removeItem(at: url) }
        let example = DraftProject.example
        var obj = try XCTUnwrap(JSONSerialization.jsonObject(with: JSONEncoder().encode(example)) as? [String: Any])
        obj.removeValue(forKey: "id")
        try JSONSerialization.data(withJSONObject: obj).write(to: url)
        let recovered = try CADLocalDraftStore.read(url)
        XCTAssertEqual(recovered.pieces, example.pieces)
        XCTAssertEqual(try CADLocalDraftStore.read(url).id, recovered.id)
        obj["version"] = 10
        let future = try JSONSerialization.data(withJSONObject: obj)
        try future.write(to: url)
        XCTAssertThrowsError(try CADLocalDraftStore.read(url))
        XCTAssertEqual(try Data(contentsOf: url), future)
    }

    func testBoundedReadAndRejectedWritePreservePriorFile() throws {
        let url = temporaryFile()
        defer { try? FileManager.default.removeItem(at: url) }
        let original = DraftProject.example
        try CADLocalDraftStore.write(original, to: url)
        let bytes = try Data(contentsOf: url)
        XCTAssertEqual(try CADFileIO.read(url, limit: bytes.count), bytes)
        XCTAssertThrowsError(try CADFileIO.read(url, limit: bytes.count - 1))
        var invalid = original
        invalid.pieces[0].points[0].x = .infinity
        XCTAssertThrowsError(try CADLocalDraftStore.write(invalid, to: url))
        XCTAssertEqual(try CADLocalDraftStore.read(url), original)
    }

    func testStaleEditWithSameProjectIdCannotOverwriteNewerDraft() throws {
        let url = temporaryFile()
        defer { try? FileManager.default.removeItem(at: url) }
        let initial = DraftProject.example
        var newer = initial
        newer.name = "Güncel çizim"
        try CADLocalDraftStore.save(initial, replacing: nil, to: url)
        try CADLocalDraftStore.save(newer, replacing: initial, to: url)

        let before = try Data(contentsOf: url)
        var stale = initial
        stale.name = "Eski oturum"
        XCTAssertThrowsError(try CADLocalDraftStore.save(stale, replacing: initial, to: url)) { error in
            guard case SketchStoreError.changed = error else {
                return XCTFail("Stale revision should report a conflict, got \(error)")
            }
        }
        XCTAssertEqual(try Data(contentsOf: url), before)
        XCTAssertEqual(try CADLocalDraftStore.read(url), newer)
    }

    func testReplacingUnexpectedOrDeletedDraftIsRejected() throws {
        let url = temporaryFile()
        defer { try? FileManager.default.removeItem(at: url) }
        let original = DraftProject.example
        var edited = original
        edited.name = "Yeni isim"
        // A missing draft is not silently recreated by a stale session.
        XCTAssertThrowsError(try CADLocalDraftStore.save(edited, replacing: original, to: url))
        XCTAssertFalse(FileManager.default.fileExists(atPath: url.path))

        try CADLocalDraftStore.save(original, replacing: nil, to: url)
        // A new editor without an existing snapshot may not overwrite an older draft.
        XCTAssertThrowsError(try CADLocalDraftStore.save(edited, replacing: nil, to: url))
        XCTAssertEqual(try CADLocalDraftStore.read(url), original)
    }

    func testUnchangedInputRotationAndSnappedDragKeepPrecision() throws {
        let piece = DraftPiece(name: "Çizgi", closed: false, points: [
            DraftPoint(x: 3.123456, y: -1.123456), DraftPoint(x: 6.123456, y: 2.876544)])
        let unchanged = piece.applyingDimensions(x: DraftPiece.display(piece.minX), y: DraftPiece.display(piece.minY),
            width: DraftPiece.display(piece.lineLength), height: DraftPiece.display(piece.maxY - piece.minY))
        XCTAssertEqual(unchanged, piece.points)
        let rotated = piece.rotatedQuarterTurn()
        XCTAssertEqual(rotated[1].x - rotated[0].x, -4, accuracy: 1e-10)
        XCTAssertEqual(rotated[1].y - rotated[0].y, 3, accuracy: 1e-10)
        let map = try XCTUnwrap(DraftCanvasMap(size: CGSize(width: 120, height: 120),
            minX: 0, maxX: 100, minY: 0, maxY: 100, inset: 10))
        XCTAssertEqual(map.translation(.zero, for: piece, snap: true), DraftPoint(x: 0, y: 0))
        let delta = map.translation(CGSize(width: 7, height: 0), for: piece, snap: true)
        XCTAssertEqual(piece.minX + delta.x, 10, accuracy: 1e-12)
        XCTAssertEqual(delta.y, 0)
    }
}
