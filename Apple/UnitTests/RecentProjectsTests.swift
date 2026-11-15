import XCTest
@testable import TekstilCADViewer

/// Deleting and renaming saved projects. Every test works on files in its own temporary
/// folder through the path-injectable library entry points; the real user library and the
/// real current draft are never touched.
final class RecentProjectsTests: XCTestCase {
    private func makeFolder() throws -> URL {
        let folder = FileManager.default.temporaryDirectory
            .appendingPathComponent("recent-projects-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: folder) }
        return folder
    }

    private func projectFile(_ project: DraftProject, in folder: URL) -> URL {
        folder.appendingPathComponent(project.id.uuidString + ".json")
    }

    private func saved(_ name: String, in folder: URL) throws -> (project: DraftProject, file: URL) {
        var project = DraftProject.example
        project.id = UUID()
        project.name = name
        let file = projectFile(project, in: folder)
        try CADProjectLibrary.save(project, replacing: nil, to: file)
        return (project, file)
    }

    // MARK: delete

    func testDeleteRemovesOnlyTheTargetFileAndLeavesDraftAndOtherProjectsAlone() throws {
        let folder = try makeFolder()
        let target = try saved("Silinecek", in: folder)
        let other = try saved("Kalacak", in: folder)
        let draft = folder.appendingPathComponent("current-draft.json")
        try CADLocalDraftStore.write(DraftProject.example, to: draft)
        let draftBytes = try Data(contentsOf: draft)
        let otherBytes = try Data(contentsOf: other.file)

        try CADProjectLibrary.delete(at: target.file)

        XCTAssertFalse(FileManager.default.fileExists(atPath: target.file.path))
        XCTAssertEqual(try Data(contentsOf: other.file), otherBytes)
        XCTAssertEqual(try CADLocalDraftStore.read(other.file), other.project)
        XCTAssertEqual(try Data(contentsOf: draft), draftBytes)
    }

    func testDeletingMissingProjectIsNotAnErrorAndCreatesNothing() throws {
        let folder = try makeFolder()
        let missing = folder.appendingPathComponent("Projects", isDirectory: true)
            .appendingPathComponent(UUID().uuidString + ".json")

        XCTAssertNoThrow(try CADProjectLibrary.delete(at: missing))
        XCTAssertFalse(FileManager.default.fileExists(atPath: missing.deletingLastPathComponent().path))

        // Deleting the same project twice is fine, too.
        let target = try saved("İki kez", in: folder)
        try CADProjectLibrary.delete(at: target.file)
        XCTAssertNoThrow(try CADProjectLibrary.delete(at: target.file))
    }

    func testUnreadableProjectFileCanBeDeleted() throws {
        let folder = try makeFolder()
        let file = folder.appendingPathComponent(UUID().uuidString + ".json")
        try Data("{\"version\":99}".utf8).write(to: file)
        XCTAssertThrowsError(try CADLocalDraftStore.read(file))

        try CADProjectLibrary.delete(at: file)
        XCTAssertFalse(FileManager.default.fileExists(atPath: file.path))

        let notJSON = folder.appendingPathComponent(UUID().uuidString + ".json")
        try Data("proje değil".utf8).write(to: notJSON)
        try CADProjectLibrary.delete(at: notJSON)
        XCTAssertFalse(FileManager.default.fileExists(atPath: notJSON.path))
    }

    func testDeleteRefusesDirectoriesAndNonProjectFiles() throws {
        let folder = try makeFolder()

        let directory = folder.appendingPathComponent(UUID().uuidString + ".json", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let inside = directory.appendingPathComponent("keep.txt")
        try Data("x".utf8).write(to: inside)
        XCTAssertThrowsError(try CADProjectLibrary.delete(at: directory)) { error in
            XCTAssertEqual(error as? SketchStoreError, .invalid)
        }
        XCTAssertTrue(FileManager.default.fileExists(atPath: inside.path))

        let foreign = folder.appendingPathComponent("notlar.txt")
        try Data("x".utf8).write(to: foreign)
        XCTAssertThrowsError(try CADProjectLibrary.delete(at: foreign))
        XCTAssertTrue(FileManager.default.fileExists(atPath: foreign.path))
    }

    // MARK: rename

    func testRenameChangesOnlyTheName() throws {
        let folder = try makeFolder()
        let target = try saved("Eski ad", in: folder)

        try CADProjectLibrary.rename(target.project.id, to: "  Yeni ad  ", at: target.file)

        let stored = try CADLocalDraftStore.read(target.file)
        XCTAssertEqual(stored.name, "Yeni ad")
        XCTAssertEqual(stored.id, target.project.id)
        XCTAssertEqual(stored.pieces, target.project.pieces)
    }

    func testRenameAcceptsExactlyTheLimitAndRejectsEmptyAndTooLongNames() throws {
        let folder = try makeFolder()
        let target = try saved("Ad", in: folder)
        let before = try Data(contentsOf: target.file)

        for bad in ["", "   ", "\n\t", String(repeating: "a", count: 121)] {
            XCTAssertThrowsError(try CADProjectLibrary.rename(target.project.id, to: bad, at: target.file)) { error in
                XCTAssertEqual(error as? SketchStoreError, .name)
            }
            XCTAssertEqual(try Data(contentsOf: target.file), before)
        }

        let longest = String(repeating: "b", count: 120)
        try CADProjectLibrary.rename(target.project.id, to: longest, at: target.file)
        XCTAssertEqual(try CADLocalDraftStore.read(target.file).name, longest)

        let single = "c"
        try CADProjectLibrary.rename(target.project.id, to: single, at: target.file)
        XCTAssertEqual(try CADLocalDraftStore.read(target.file).name, single)
    }

    func testRenameRejectsMismatchedIdentityMissingAndUnreadableFiles() throws {
        let folder = try makeFolder()
        let target = try saved("Ad", in: folder)
        let before = try Data(contentsOf: target.file)

        XCTAssertThrowsError(try CADProjectLibrary.rename(UUID(), to: "Başka", at: target.file)) { error in
            XCTAssertEqual(error as? SketchStoreError, .identity)
        }
        XCTAssertEqual(try Data(contentsOf: target.file), before)

        let missing = folder.appendingPathComponent(UUID().uuidString + ".json")
        XCTAssertThrowsError(try CADProjectLibrary.rename(UUID(), to: "Ad", at: missing)) { error in
            XCTAssertEqual(error as? SketchStoreError, .missing)
        }
        XCTAssertFalse(FileManager.default.fileExists(atPath: missing.path))

        let broken = folder.appendingPathComponent(UUID().uuidString + ".json")
        let brokenBytes = Data("{\"version\":99}".utf8)
        try brokenBytes.write(to: broken)
        XCTAssertThrowsError(try CADProjectLibrary.rename(UUID(), to: "Ad", at: broken))
        XCTAssertEqual(try Data(contentsOf: broken), brokenBytes)
    }

    func testStaleEditorCannotOverwriteRenamedProject() throws {
        let folder = try makeFolder()
        let target = try saved("Ad", in: folder)
        try CADProjectLibrary.rename(target.project.id, to: "Yeni", at: target.file)

        // An editor that still holds the pre-rename snapshot must not overwrite the new name.
        var stale = target.project
        stale.name = "Eski düzenleme"
        XCTAssertThrowsError(try CADProjectLibrary.save(stale, replacing: target.project, to: target.file)) { error in
            XCTAssertEqual(error as? SketchStoreError, .changed)
        }
        XCTAssertEqual(try CADLocalDraftStore.read(target.file).name, "Yeni")
    }

    func testValidatedNameTrimsAndEnforcesLimits() {
        XCTAssertEqual(CADProjectLibrary.validatedName("  Ad  "), "Ad")
        XCTAssertNil(CADProjectLibrary.validatedName(""))
        XCTAssertNil(CADProjectLibrary.validatedName("  \n "))
        XCTAssertNil(CADProjectLibrary.validatedName(String(repeating: "x", count: CADProjectLibrary.nameLimit + 1)))
        XCTAssertNotNil(CADProjectLibrary.validatedName(String(repeating: "x", count: CADProjectLibrary.nameLimit)))
    }
}
