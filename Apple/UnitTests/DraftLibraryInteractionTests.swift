import XCTest
@testable import TekstilCADViewer

/// How deleting and renaming a saved project treats the autosaved drawing ("Son çizime devam
/// et"). The production `CADProjectLibrary.delete(_:)` and `rename(_:to:)` forward to the
/// overloads used here with `url(id)` and `CADLocalDraftStore.current`; these tests pass files
/// in their own temporary folder instead, so the real library and draft are never touched.
final class DraftLibraryInteractionTests: XCTestCase {
    private struct Fixture {
        let folder: URL
        let project: DraftProject
        let file: URL
        let draft: URL
    }

    private func makeFixture(name: String = "Etek") throws -> Fixture {
        let folder = FileManager.default.temporaryDirectory
            .appendingPathComponent("draft-library-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: folder) }
        var project = DraftProject.example
        project.id = UUID()
        project.name = name
        let file = folder.appendingPathComponent("Projects", isDirectory: true)
            .appendingPathComponent(project.id.uuidString + ".json")
        try CADProjectLibrary.save(project, replacing: nil, to: file)
        return Fixture(folder: folder, project: project, file: file,
                       draft: folder.appendingPathComponent("current-draft.json"))
    }

    private func edited(_ project: DraftProject) -> DraftProject {
        var copy = project
        copy.pieces.append(DraftPiece(name: "Ek parça", closed: false, points: [
            DraftPoint(x: 300, y: 0), DraftPoint(x: 400, y: 50)
        ]))
        return copy
    }

    // MARK: delete

    func testDeletingAProjectAlsoRemovesItsAutosavedDraft() throws {
        let fixture = try makeFixture()
        try CADLocalDraftStore.write(fixture.project, to: fixture.draft)

        try CADProjectLibrary.delete(fixture.project.id, file: fixture.file, draft: fixture.draft)

        XCTAssertFalse(FileManager.default.fileExists(atPath: fixture.file.path))
        XCTAssertFalse(FileManager.default.fileExists(atPath: fixture.draft.path),
                       "The draft would otherwise be archived back into the library on the next switch")
    }

    func testDeletingAProjectAlsoRemovesAnEditedDraftOfTheSameProject() throws {
        let fixture = try makeFixture()
        try CADLocalDraftStore.write(edited(fixture.project), to: fixture.draft)

        try CADProjectLibrary.delete(fixture.project.id, file: fixture.file, draft: fixture.draft)

        XCTAssertFalse(FileManager.default.fileExists(atPath: fixture.draft.path))
    }

    func testDeletingAProjectLeavesTheDraftOfAnotherDrawingAlone() throws {
        let fixture = try makeFixture()
        var other = DraftProject.example
        other.id = UUID()
        try CADLocalDraftStore.write(other, to: fixture.draft)
        let draftBytes = try Data(contentsOf: fixture.draft)

        try CADProjectLibrary.delete(fixture.project.id, file: fixture.file, draft: fixture.draft)

        XCTAssertFalse(FileManager.default.fileExists(atPath: fixture.file.path))
        XCTAssertEqual(try Data(contentsOf: fixture.draft), draftBytes)
    }

    func testDeletingAProjectLeavesAnUnreadableDraftAlone() throws {
        let fixture = try makeFixture()
        let bytes = Data("{\"version\":99}".utf8)
        try bytes.write(to: fixture.draft)

        try CADProjectLibrary.delete(fixture.project.id, file: fixture.file, draft: fixture.draft)

        XCTAssertFalse(FileManager.default.fileExists(atPath: fixture.file.path))
        XCTAssertEqual(try Data(contentsOf: fixture.draft), bytes)
    }

    func testDeletingWithoutADraftWorksAndCreatesNoDraft() throws {
        let fixture = try makeFixture()

        try CADProjectLibrary.delete(fixture.project.id, file: fixture.file, draft: fixture.draft)

        XCTAssertFalse(FileManager.default.fileExists(atPath: fixture.file.path))
        XCTAssertFalse(FileManager.default.fileExists(atPath: fixture.draft.path))
    }

    // MARK: rename

    func testRenamingUpdatesTheNameOfAnUntouchedDraft() throws {
        let fixture = try makeFixture(name: "Eski ad")
        try CADLocalDraftStore.write(fixture.project, to: fixture.draft)

        try CADProjectLibrary.rename(fixture.project.id, to: "  Yeni ad  ",
                                     file: fixture.file, draft: fixture.draft)

        let stored = try CADLocalDraftStore.read(fixture.file)
        let draft = try CADLocalDraftStore.read(fixture.draft)
        XCTAssertEqual(stored.name, "Yeni ad")
        XCTAssertEqual(draft.name, "Yeni ad")
        // Everything else in the draft stays as it was, so it still equals the saved project
        // and is not archived as a second "(taslak)" copy later.
        XCTAssertEqual(draft, stored)
        XCTAssertEqual(draft.pieces, fixture.project.pieces)
    }

    func testResumingEditedDraftAndSavingPreservesRenameAndGeometry() throws {
        let fixture = try makeFixture(name: "Eski ad")
        let working = edited(fixture.project)
        try CADLocalDraftStore.write(working, to: fixture.draft)
        try CADProjectLibrary.rename(fixture.project.id, to: "Yeni ad",
                                     file: fixture.file, draft: fixture.draft)
        let stored = try CADLocalDraftStore.read(fixture.file)
        let resumed = try CADLocalDraftStore.read(fixture.draft)
        XCTAssertEqual(stored.pieces, fixture.project.pieces)
        XCTAssertEqual(resumed.pieces, working.pieces)
        XCTAssertEqual(resumed.name, "Yeni ad")
        XCTAssertThrowsError(try CADLocalDraftStore.save(working, replacing: working, to: fixture.draft))
        try CADProjectLibrary.save(resumed, replacing: stored, to: fixture.file)
        XCTAssertEqual(try CADLocalDraftStore.read(fixture.file), resumed)
    }

    func testIndependentDraftRenameIsPreservedOnConflictAndRetry() throws {
        let fixture = try makeFixture()
        var working = edited(fixture.project)
        working.name = "Taslak adı"
        try CADLocalDraftStore.write(working, to: fixture.draft)
        XCTAssertThrowsError(try CADProjectLibrary.rename(fixture.project.id, to: "Başka ad",
                                                          file: fixture.file, draft: fixture.draft))
        XCTAssertEqual(try CADLocalDraftStore.read(fixture.file), fixture.project)
        XCTAssertEqual(try CADLocalDraftStore.read(fixture.draft), working)
        try CADProjectLibrary.rename(fixture.project.id, to: working.name,
                                     file: fixture.file, draft: fixture.draft)
        XCTAssertEqual(try CADLocalDraftStore.read(fixture.file).name, working.name)
        XCTAssertEqual(try CADLocalDraftStore.read(fixture.draft), working)
    }

    func testRenamingLeavesTheDraftOfAnotherDrawingAlone() throws {
        let fixture = try makeFixture(name: "Eski ad")
        var other = DraftProject.example
        other.id = UUID()
        other.name = "Eski ad"
        try CADLocalDraftStore.write(other, to: fixture.draft)
        let draftBytes = try Data(contentsOf: fixture.draft)

        try CADProjectLibrary.rename(fixture.project.id, to: "Yeni ad",
                                     file: fixture.file, draft: fixture.draft)

        XCTAssertEqual(try Data(contentsOf: fixture.draft), draftBytes)
    }

    func testRefusedRenameChangesNeitherTheProjectNorTheDraft() throws {
        let fixture = try makeFixture(name: "Eski ad")
        try CADLocalDraftStore.write(fixture.project, to: fixture.draft)
        let projectBytes = try Data(contentsOf: fixture.file)
        let draftBytes = try Data(contentsOf: fixture.draft)

        for bad in ["", "   ", String(repeating: "a", count: 121)] {
            XCTAssertThrowsError(try CADProjectLibrary.rename(fixture.project.id, to: bad,
                                                              file: fixture.file, draft: fixture.draft)) { error in
                XCTAssertEqual(error as? SketchStoreError, .name)
            }
        }
        XCTAssertEqual(try Data(contentsOf: fixture.file), projectBytes)
        XCTAssertEqual(try Data(contentsOf: fixture.draft), draftBytes)
    }

    func testRenamingAMissingProjectFailsAndLeavesTheDraftAlone() throws {
        let fixture = try makeFixture()
        try CADLocalDraftStore.write(fixture.project, to: fixture.draft)
        let draftBytes = try Data(contentsOf: fixture.draft)
        try CADProjectLibrary.delete(at: fixture.file)

        XCTAssertThrowsError(try CADProjectLibrary.rename(fixture.project.id, to: "Yeni",
                                                          file: fixture.file, draft: fixture.draft)) { error in
            XCTAssertEqual(error as? SketchStoreError, .missing)
        }
        XCTAssertEqual(try Data(contentsOf: fixture.draft), draftBytes)
    }
}
