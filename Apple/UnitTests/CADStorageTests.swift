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
            guard let storeError = error as? SketchStoreError,
                  case .changed = storeError else {
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

    func testSavedProjectRejectsStaleUpdateWithoutTouchingLatestBytes() throws {
        let url = temporaryFile()
        defer { try? FileManager.default.removeItem(at: url) }
        let original = DraftProject.example
        var newer = original
        newer.name = "Güncel proje"
        try CADProjectLibrary.save(original, replacing: nil, to: url)
        try CADProjectLibrary.save(newer, replacing: original, to: url)

        let before = try Data(contentsOf: url)
        var stale = original
        stale.name = "Eski editör"
        XCTAssertThrowsError(try CADProjectLibrary.save(stale, replacing: original, to: url)) { error in
            guard let storeError = error as? SketchStoreError,
                  case .changed = storeError else {
                return XCTFail("Expected saved-project revision conflict, got \(error)")
            }
        }
        XCTAssertEqual(try Data(contentsOf: url), before)
        XCTAssertEqual(try CADLocalDraftStore.read(url), newer)
    }

    func testLibraryRejectsMismatchedIdentityAndAllowsIdenticalRetry() throws {
        let url = temporaryFile()
        defer { try? FileManager.default.removeItem(at: url) }
        let stored = DraftProject.example
        try CADProjectLibrary.save(stored, replacing: nil, to: url)
        let originalBytes = try Data(contentsOf: url)

        // Idempotent retry may not erase the only copy.
        try CADProjectLibrary.save(stored, replacing: nil, to: url)
        XCTAssertEqual(try CADLocalDraftStore.read(url), stored)
        var other = stored
        other.id = UUID()
        XCTAssertThrowsError(try CADProjectLibrary.save(other, replacing: stored, to: url)) { error in
            guard let storeError = error as? SketchStoreError,
                  case .identity = storeError else {
                return XCTFail("Expected mismatched identity, got \(error)")
            }
        }
        XCTAssertEqual(try Data(contentsOf: url), originalBytes)
    }

    func testUnreadableDraftIsSetAsideWithItsBytesAndReadableDraftStays() throws {
        let folder = temporaryFile()
        defer { try? FileManager.default.removeItem(at: folder) }
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let draft = folder.appendingPathComponent("current-draft.json")
        let aside = folder.appendingPathComponent("Unreadable", isDirectory: true)

        // No draft: nothing to move.
        XCTAssertNil(try CADLocalDraftStore.setAsideUnreadableDraft(at: draft, into: aside))

        // A draft that can be read is never moved.
        let readable = DraftProject.example
        try CADLocalDraftStore.write(readable, to: draft)
        XCTAssertNil(try CADLocalDraftStore.setAsideUnreadableDraft(at: draft, into: aside))
        XCTAssertEqual(try CADLocalDraftStore.read(draft), readable)
        XCTAssertFalse(FileManager.default.fileExists(atPath: aside.path))

        // An unreadable one is moved with its exact bytes, so a new drawing can start.
        let damaged = Data("{\"version\":99}".utf8)
        try damaged.write(to: draft)
        XCTAssertThrowsError(try CADLocalDraftStore.read(draft))
        let moved = try XCTUnwrap(try CADLocalDraftStore.setAsideUnreadableDraft(at: draft, into: aside))
        XCTAssertFalse(FileManager.default.fileExists(atPath: draft.path))
        XCTAssertEqual(try Data(contentsOf: moved), damaged)
        XCTAssertEqual(moved.deletingLastPathComponent().lastPathComponent, "Unreadable")

        // The emptied slot accepts a new draft.
        try CADLocalDraftStore.save(readable, replacing: nil, to: draft)
        XCTAssertEqual(try CADLocalDraftStore.read(draft), readable)
        XCTAssertFalse(CADLocalDraftStore.hasUnreadableContent(draft))

        // Bytes that are not JSON at all count as unreadable content too.
        try Data("taslak değil".utf8).write(to: draft)
        XCTAssertTrue(CADLocalDraftStore.hasUnreadableContent(draft))
        XCTAssertNotNil(try CADLocalDraftStore.setAsideUnreadableDraft(at: draft, into: aside))

        // A file that cannot be read at all is not treated as unreadable content: a transient
        // I/O failure on a good draft must not offer to move it away.
        XCTAssertFalse(CADLocalDraftStore.hasUnreadableContent(folder.appendingPathComponent("yok.json")))
    }

    func testConcurrentDraftWritesRejectOneStaleSession() throws {
        let url = temporaryFile()
        defer {
            try? FileManager.default.removeItem(at: url)
            try? FileManager.default.removeItem(at: url.appendingPathExtension("lock"))
        }
        let base = DraftProject.example
        try CADLocalDraftStore.save(base, replacing: nil, to: url)

        let gate = DispatchSemaphore(value: 0)
        let group = DispatchGroup()
        let counterLock = NSLock()
        var committed = 0
        for index in 0..<2 {
            group.enter()
            DispatchQueue.global(qos: .userInitiated).async {
                gate.wait()
                var updated = base
                updated.name = "Editör \(index)"
                if (try? CADLocalDraftStore.save(updated, replacing: base, to: url)) != nil {
                    counterLock.lock()
                    committed += 1
                    counterLock.unlock()
                }
                group.leave()
            }
        }
        gate.signal()
        gate.signal()
        XCTAssertEqual(group.wait(timeout: .now() + 10), .success)
        XCTAssertEqual(committed, 1)
        XCTAssertNotEqual(try CADLocalDraftStore.read(url), base)
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
