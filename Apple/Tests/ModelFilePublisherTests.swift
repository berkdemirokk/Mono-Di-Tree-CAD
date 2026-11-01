import Foundation
import XCTest
@testable import TekstilCADViewer_iOS

final class FilePickerCancellationTests: XCTestCase {
    func testOnlyCocoaUserCancellationIsIgnored() {
        XCTAssertTrue(isFilePickerCancellation(CocoaError(.userCancelled)))
        XCTAssertFalse(isFilePickerCancellation(CocoaError(.fileReadNoPermission)))
        XCTAssertFalse(isFilePickerCancellation(NSError(domain: "Other", code: NSUserCancelledError)))
    }
}

final class ModelFilePublisherTests: XCTestCase {
    func testInvalidOrMissingMetadataOffersRawRecoveryWithoutChangingSource() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let bytes = Data([0, 255, 5, 10])
        let cases: [(String, Data?)] = [
            ("future", Data("{\"version\":2,\"name\":\"Future\"}".utf8)),
            ("corrupt", Data([255, 0, 1])),
            ("missing", nil)
        ]
        for (name, metadata) in cases {
            let copy = root.appendingPathComponent("\(name).tcadcopy", isDirectory: true)
            try FileManager.default.createDirectory(at: copy, withIntermediateDirectories: false)
            try bytes.write(to: copy.appendingPathComponent("project.tcad"))
            if let metadata { try metadata.write(to: copy.appendingPathComponent("metadata.json")) }
        }
        let missingPayload = root.appendingPathComponent("no-payload.tcadcopy", isDirectory: true)
        try FileManager.default.createDirectory(at: missingPayload, withIntermediateDirectories: false)
        let directoryPayload = root.appendingPathComponent("directory-payload.tcadcopy", isDirectory: true)
        try FileManager.default.createDirectory(at: directoryPayload.appendingPathComponent("project.tcad", isDirectory: true), withIntermediateDirectories: true)
        let entries = try ModelStore.models(in: root)
        for (name, metadata) in cases {
            let copy = root.appendingPathComponent("\(name).tcadcopy", isDirectory: true)
            let entry = try XCTUnwrap(entries.first { $0.url == copy })
            XCTAssertFalse(entry.canOpen)
            XCTAssertTrue(entry.requiresRecovery)
            XCTAssertEqual(try ModelStore.readListedPayload(entry), bytes)
            XCTAssertEqual(try Data(contentsOf: copy.appendingPathComponent("project.tcad")), bytes)
            if let metadata { XCTAssertEqual(try Data(contentsOf: copy.appendingPathComponent("metadata.json")), metadata) }
        }
        let absent = try XCTUnwrap(entries.first { $0.url == missingPayload })
        XCTAssertFalse(absent.canOpen)
        XCTAssertFalse(absent.requiresRecovery)
        let directoryEntry = try XCTUnwrap(entries.first { $0.url == directoryPayload })
        XCTAssertFalse(directoryEntry.canOpen)
        XCTAssertFalse(directoryEntry.requiresRecovery)
    }

    func testListedModelReadRejectsReplacementBeforeOrDuringRead() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let path = root.appendingPathComponent("model.tcad")
        let original = root.appendingPathComponent("original.saved")
        let first = Data("first model".utf8)
        let second = Data("second model".utf8)
        try first.write(to: path)
        let entry = try XCTUnwrap(ModelStore.models(in: root).first { $0.url == path })
        XCTAssertEqual(try ModelStore.readListedPayload(entry), first)
        XCTAssertThrowsError(try ModelStore.readListedPayload(entry) {
            try FileManager.default.moveItem(at: path, to: original)
            try second.write(to: path)
        })
        XCTAssertEqual(try Data(contentsOf: path), second)
        XCTAssertEqual(try Data(contentsOf: original), first)
        XCTAssertThrowsError(try ModelStore.readListedPayload(entry))
        let newEntry = try XCTUnwrap(ModelStore.models(in: root).first { $0.url == path })
        XCTAssertEqual(try ModelStore.readListedPayload(newEntry), second)
    }

    func testListedCopyReadRejectsChangedMetadata() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let copy = root.appendingPathComponent("model.tcadcopy", isDirectory: true)
        try FileManager.default.createDirectory(at: copy, withIntermediateDirectories: false)
        let payload = copy.appendingPathComponent("project.tcad")
        let metadata = copy.appendingPathComponent("metadata.json")
        try Data("model".utf8).write(to: payload)
        try Data("{\"version\":1,\"name\":\"A\"}".utf8).write(to: metadata)
        let entry = try XCTUnwrap(ModelStore.models(in: root).first { $0.url == copy })
        let changed = Data("{\"version\":2,\"name\":\"Changed\"}".utf8)
        try changed.write(to: metadata)
        XCTAssertThrowsError(try ModelStore.readListedPayload(entry))
        XCTAssertEqual(try Data(contentsOf: metadata), changed)
        XCTAssertEqual(try Data(contentsOf: payload), Data("model".utf8))
    }

    func testDeleteRejectsAReplacedLibraryEntry() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let path = root.appendingPathComponent("model.tcad")
        let original = root.appendingPathComponent("original.saved")
        try Data("first".utf8).write(to: path)
        let entry = try XCTUnwrap(ModelStore.models(in: root).first { $0.url == path })
        try FileManager.default.moveItem(at: path, to: original)
        let replacement = Data("replacement".utf8)
        try replacement.write(to: path)
        XCTAssertThrowsError(try ModelStore.delete(entry, in: root))
        XCTAssertEqual(try Data(contentsOf: path), replacement)
        XCTAssertEqual(try Data(contentsOf: original), Data("first".utf8))
        let current = try XCTUnwrap(ModelStore.models(in: root).first { $0.url == path })
        try ModelStore.delete(current, in: root)
        XCTAssertFalse(FileManager.default.fileExists(atPath: path.path))
    }

    func testDeleteRejectsChangedPayloadInsideSameCopy() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let copy = root.appendingPathComponent("model.tcadcopy", isDirectory: true)
        try FileManager.default.createDirectory(at: copy, withIntermediateDirectories: false)
        let payload = copy.appendingPathComponent("project.tcad")
        try Data("old".utf8).write(to: payload)
        try Data("{\"version\":1,\"name\":\"Model\"}".utf8).write(to: copy.appendingPathComponent("metadata.json"))
        let entry = try XCTUnwrap(ModelStore.models(in: root).first { $0.url == copy })
        let replacement = Data("new longer content".utf8)
        try replacement.write(to: payload)
        XCTAssertThrowsError(try ModelStore.delete(entry, in: root))
        XCTAssertEqual(try Data(contentsOf: payload), replacement)
    }

    func testOversizedMetadataDoesNotBlockLibraryOrItsPayload() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let large = root.appendingPathComponent("large.tcadcopy", isDirectory: true)
        let normal = root.appendingPathComponent("normal.tcadcopy", isDirectory: true)
        for url in [large, normal] {
            try FileManager.default.createDirectory(at: url, withIntermediateDirectories: false)
            try Data("model".utf8).write(to: url.appendingPathComponent("project.tcad"))
        }
        let longName = String(repeating: "a", count: 70_000)
        let metadata = Data("{\"version\":1,\"name\":\"\(longName)\"}".utf8)
        try metadata.write(to: large.appendingPathComponent("metadata.json"))
        try Data("{\"version\":1,\"name\":\"Gömlek\"}".utf8).write(to: normal.appendingPathComponent("metadata.json"))

        let entries = try ModelStore.models(in: root)
        let largeEntry = try XCTUnwrap(entries.first { $0.url == large })
        let normalEntry = try XCTUnwrap(entries.first { $0.url == normal })
        XCTAssertFalse(largeEntry.canOpen)
        XCTAssertTrue(largeEntry.requiresRecovery)
        XCTAssertTrue(largeEntry.name.contains("Kurtarma gerekli"))
        XCTAssertEqual(try Data(contentsOf: ModelStore.payloadURL(largeEntry.url)), Data("model".utf8))
        XCTAssertTrue(normalEntry.canOpen)
        XCTAssertFalse(normalEntry.requiresRecovery)
        XCTAssertEqual(normalEntry.name, "Gömlek")
        XCTAssertEqual(try Data(contentsOf: large.appendingPathComponent("metadata.json")), metadata)
    }

    func testCopyDeletionPreservesUnknownFilesAndNestedDirectories() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let copy = root.appendingPathComponent("model.tcadcopy", isDirectory: true)
        try FileManager.default.createDirectory(at: copy, withIntermediateDirectories: false)
        let payload = copy.appendingPathComponent("project.tcad")
        let unknown = copy.appendingPathComponent("notes.txt")
        try Data("model".utf8).write(to: payload)
        try Data("user notes".utf8).write(to: unknown)
        XCTAssertThrowsError(try ModelStore.delete(copy, in: root))
        XCTAssertEqual(try Data(contentsOf: payload), Data("model".utf8))
        XCTAssertEqual(try Data(contentsOf: unknown), Data("user notes".utf8))

        try FileManager.default.removeItem(at: unknown)
        let nested = copy.appendingPathComponent("metadata.json", isDirectory: true)
        try FileManager.default.createDirectory(at: nested, withIntermediateDirectories: false)
        let inside = nested.appendingPathComponent("data")
        try Data("keep".utf8).write(to: inside)
        XCTAssertThrowsError(try ModelStore.delete(copy, in: root))
        XCTAssertEqual(try Data(contentsOf: inside), Data("keep".utf8))

        try FileManager.default.removeItem(at: nested)
        try Data("{}".utf8).write(to: copy.appendingPathComponent("metadata.json"))
        try ModelStore.delete(copy, in: root)
        XCTAssertFalse(FileManager.default.fileExists(atPath: copy.path))
    }

    func testPublishesExactBytesAndRemovesStagingDirectory() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let staging = root.appendingPathComponent("staging")
        let destination = root.appendingPathComponent("result.tcad")
        let data = Data([0, 1, 0xff, 0x0a])

        try ModelFilePublisher.publish(data, staging: staging, destination: destination)
        XCTAssertEqual(try Data(contentsOf: destination), data)
        XCTAssertFalse(FileManager.default.fileExists(atPath: staging.path))
    }

    func testReplacedStagingDirectoryIsNotRecursivelyDeleted() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let staging = root.appendingPathComponent("staging")
        let original = root.appendingPathComponent("original")
        let destination = root.appendingPathComponent("result.tcad")
        let foreignFile = staging.appendingPathComponent("foreign.tcad")
        let foreignData = Data("foreign".utf8)

        XCTAssertThrowsError(try ModelFilePublisher.publish(Data("new".utf8), staging: staging, destination: destination) {
            try FileManager.default.moveItem(at: staging, to: original)
            try FileManager.default.createDirectory(at: staging, withIntermediateDirectories: false)
            try foreignData.write(to: foreignFile)
            throw CocoaError(.fileWriteUnknown)
        })
        XCTAssertEqual(try Data(contentsOf: foreignFile), foreignData)
        XCTAssertEqual(try Data(contentsOf: original.appendingPathComponent("model.tcad")), Data("new".utf8))
        XCTAssertFalse(FileManager.default.fileExists(atPath: destination.path))
    }

    func testExistingStagingDirectoryAndItsContentsArePreserved() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let staging = root.appendingPathComponent("staging")
        try FileManager.default.createDirectory(at: staging, withIntermediateDirectories: false)
        let otherFile = staging.appendingPathComponent("other.tcad")
        let existing = Data("other model".utf8)
        try existing.write(to: otherFile)
        let destination = root.appendingPathComponent("result.tcad")

        XCTAssertThrowsError(try ModelFilePublisher.publish(Data("new".utf8), staging: staging, destination: destination))
        XCTAssertEqual(try Data(contentsOf: otherFile), existing)
        XCTAssertFalse(FileManager.default.fileExists(atPath: destination.path))
    }

    func testExistingStagingPathIsNeverDeleted() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let staging = root.appendingPathComponent("staging")
        let existing = Data("another save".utf8)
        try existing.write(to: staging)
        let destination = root.appendingPathComponent("result.tcad")

        XCTAssertThrowsError(try ModelFilePublisher.publish(Data("new".utf8), staging: staging, destination: destination))
        XCTAssertEqual(try Data(contentsOf: staging), existing)
        XCTAssertFalse(FileManager.default.fileExists(atPath: destination.path))
    }

    func testExistingDestinationAndItsBytesArePreserved() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let staging = root.appendingPathComponent("staging")
        let destination = root.appendingPathComponent("result.tcad")
        let existing = Data("existing model".utf8)
        try existing.write(to: destination)

        XCTAssertThrowsError(try ModelFilePublisher.publish(Data("new".utf8), staging: staging, destination: destination))
        XCTAssertEqual(try Data(contentsOf: destination), existing)
        XCTAssertEqual(try Data(contentsOf: staging.appendingPathComponent("model.tcad")), Data("new".utf8))
    }
}
