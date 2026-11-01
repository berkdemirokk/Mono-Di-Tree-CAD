import Foundation
import Darwin
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
    func testInvalidImportRawCopyKeepsExactBytesAndOriginal() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let source = root.appendingPathComponent("invalid.tcad")
        let destination = root.appendingPathComponent("saved.tcadraw")
        let bytes = Data([0x5b, 0x36, 0x2c, 0xff, 0x00])
        try bytes.write(to: source)
        try ModelFilePublisher.publish(bytes, staging: root.appendingPathComponent("staging"), destination: destination)
        let raw = try XCTUnwrap(ModelStore.models(in: root).first { $0.url == destination })
        XCTAssertFalse(raw.canOpen)
        XCTAssertTrue(raw.requiresRecovery)
        XCTAssertEqual(try ModelStore.readListedPayload(raw), bytes)
        XCTAssertEqual(try Data(contentsOf: source), bytes)
    }

    func testExpandedUTF16RawRecoveryPreservesExactSourceBytes() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let text = "[6,{\"Name\":\"" + String(repeating: "€", count: 3_000_000) + "\",\"Pieces\":[]}]"
        var bytes = Data([0xfe, 0xff])
        bytes.append(try XCTUnwrap(text.data(using: .utf16BigEndian)))
        XCTAssertLessThan(bytes.count, LosslessProjectName.maximumEditBytes)
        XCTAssertTrue(LosslessProjectName.requiresVersionSixRawRecovery(bytes))
        let source = root.appendingPathComponent("source.tcad")
        let staging = root.appendingPathComponent("staging")
        let destination = root.appendingPathComponent("raw.tcadraw")
        try bytes.write(to: source)
        try ModelFilePublisher.publish(bytes, staging: staging, destination: destination)
        let entry = try XCTUnwrap(ModelStore.models(in: root).first { $0.url == destination })
        XCTAssertFalse(entry.canOpen)
        XCTAssertTrue(entry.requiresRecovery)
        XCTAssertEqual(try ModelStore.readListedPayload(entry), bytes)
        XCTAssertEqual(try Data(contentsOf: source), bytes)
    }

    func testEncodedOversizedRawRecoveryPreservesSourceBytes() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let variants: [(String.Encoding, [UInt8], Int)] = [
            (.utf16BigEndian, [0xfe, 0xff], 2), (.utf32LittleEndian, [0xff, 0xfe, 0, 0], 4)
        ]
        for (index, variant) in variants.enumerated() {
            let (encoding, bom, width) = variant
            let text = String(repeating: " ", count: 5000) + "[6," + String(repeating: " ", count: LosslessProjectName.maximumEditBytes / width) + "{}]"
            var bytes = Data(bom)
            bytes.append(try XCTUnwrap(text.data(using: encoding)))
            XCTAssertTrue(LosslessProjectName.isOversizedVersionSix(bytes))
            let source = root.appendingPathComponent("source-\(index).tcad")
            let staging = root.appendingPathComponent("staging-\(index)")
            let destination = root.appendingPathComponent("copy-\(index).tcadraw")
            try bytes.write(to: source)
            try ModelFilePublisher.publish(bytes, staging: staging, destination: destination)
            let entry = try XCTUnwrap(ModelStore.models(in: root).first { $0.url == destination })
            XCTAssertFalse(entry.canOpen)
            XCTAssertTrue(entry.requiresRecovery)
            XCTAssertEqual(try ModelStore.readListedPayload(entry), bytes)
            XCTAssertEqual(try Data(contentsOf: source), bytes)
        }
    }

    func testRawLibraryRecordIsRecoverableButNeverOfferedAsPreview() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let path = root.appendingPathComponent("Büyük.tcadraw")
        let data = Data([0xff, 0x00, 0x91])
        try data.write(to: path)
        let entry = try XCTUnwrap(ModelStore.models(in: root).first { $0.url == path })
        XCTAssertFalse(entry.canOpen)
        XCTAssertTrue(entry.requiresRecovery)
        XCTAssertTrue(entry.name.contains("Ham kayıt"))
        XCTAssertEqual(try ModelStore.readListedPayload(entry), data)
        XCTAssertEqual(try Data(contentsOf: path), data)
    }

    func testCorruptSavedModelCanBeReadAsRawBytesWithoutChangingIt() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let file = root.appendingPathComponent("broken.tcad")
        let invalid = Data([0xff, 0x00, 0x91])
        try invalid.write(to: file)
        let entry = try XCTUnwrap(ModelStore.models(in: root).first { $0.url == file })
        XCTAssertTrue(entry.canOpen)
        XCTAssertEqual(try ModelStore.readListedPayload(entry), invalid)
        XCTAssertEqual(try Data(contentsOf: file), invalid)
    }

    func testCopyDeleteRejectsNewFileAfterValidationWithoutDeletingModel() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let copy = root.appendingPathComponent("model.tcadcopy", isDirectory: true)
        try FileManager.default.createDirectory(at: copy, withIntermediateDirectories: false)
        let payload = copy.appendingPathComponent("project.tcad")
        let metadata = copy.appendingPathComponent("metadata.json")
        let foreign = copy.appendingPathComponent("notes.txt")
        try Data("model".utf8).write(to: payload)
        try Data("metadata".utf8).write(to: metadata)
        XCTAssertThrowsError(try ModelStore.delete(copy, in: root) {
            try Data("keep".utf8).write(to: foreign)
        })
        XCTAssertEqual(try Data(contentsOf: payload), Data("model".utf8))
        XCTAssertEqual(try Data(contentsOf: metadata), Data("metadata".utf8))
        XCTAssertEqual(try Data(contentsOf: foreign), Data("keep".utf8))
    }

    func testCopyDeleteRejectsReplacedPayloadAfterValidation() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let copy = root.appendingPathComponent("model.tcadcopy", isDirectory: true)
        try FileManager.default.createDirectory(at: copy, withIntermediateDirectories: false)
        let payload = copy.appendingPathComponent("project.tcad")
        let moved = root.appendingPathComponent("original.saved")
        let metadata = copy.appendingPathComponent("metadata.json")
        try Data("original".utf8).write(to: payload)
        try Data("metadata".utf8).write(to: metadata)
        XCTAssertThrowsError(try ModelStore.delete(copy, in: root) {
            try FileManager.default.moveItem(at: payload, to: moved)
            try Data("replacement".utf8).write(to: payload)
        })
        XCTAssertEqual(try Data(contentsOf: moved), Data("original".utf8))
        XCTAssertEqual(try Data(contentsOf: payload), Data("replacement".utf8))
        XCTAssertEqual(try Data(contentsOf: metadata), Data("metadata".utf8))
    }

    func testFileReplacedByDirectoryDuringDeleteIsNeverRecursivelyRemoved() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let path = root.appendingPathComponent("model.tcad")
        let original = root.appendingPathComponent("original.saved")
        let originalBytes = Data("model".utf8)
        let foreignBytes = Data("preserve".utf8)
        try originalBytes.write(to: path)
        XCTAssertThrowsError(try ModelStore.delete(path, in: root) {
            try FileManager.default.moveItem(at: path, to: original)
            try FileManager.default.createDirectory(at: path, withIntermediateDirectories: false)
            try foreignBytes.write(to: path.appendingPathComponent("important.txt"))
        })
        XCTAssertEqual(try Data(contentsOf: original), originalBytes)
        XCTAssertEqual(try Data(contentsOf: path.appendingPathComponent("important.txt")), foreignBytes)
    }

    func testDirectoryNamedTcadCannotBeOpenedOrRecursivelyDeleted() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let folder = root.appendingPathComponent("not-a-file.tcad", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: false)
        let child = folder.appendingPathComponent("important.txt")
        let contents = Data("keep this".utf8)
        try contents.write(to: child)
        let file = root.appendingPathComponent("ordinary.tcad")
        try Data("model".utf8).write(to: file)
        let entries = try ModelStore.models(in: root)
        let folderEntry = try XCTUnwrap(entries.first { $0.url == folder })
        XCTAssertFalse(folderEntry.canOpen)
        XCTAssertThrowsError(try ModelStore.delete(folderEntry, in: root))
        XCTAssertEqual(try Data(contentsOf: child), contents)
        let fileEntry = try XCTUnwrap(entries.first { $0.url == file })
        XCTAssertTrue(fileEntry.canOpen)
        try ModelStore.delete(fileEntry, in: root)
        XCTAssertFalse(FileManager.default.fileExists(atPath: file.path))
    }

    func testCopyWithSymlinkPayloadCannotBeOpenedOrRecovered() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let target = root.appendingPathComponent("source.txt")
        let bytes = Data("preserve source".utf8)
        try bytes.write(to: target)
        let copy = root.appendingPathComponent("linked.tcadcopy", isDirectory: true)
        try FileManager.default.createDirectory(at: copy, withIntermediateDirectories: false)
        try Data(#"{"version":1,"name":"Model"}"#.utf8).write(to: copy.appendingPathComponent("metadata.json"))
        try FileManager.default.createSymbolicLink(at: copy.appendingPathComponent("project.tcad"), withDestinationURL: target)
        let entry = try XCTUnwrap(ModelStore.models(in: root).first { $0.url == copy })
        XCTAssertFalse(entry.canOpen)
        XCTAssertFalse(entry.requiresRecovery)
        XCTAssertThrowsError(try ModelStore.readListedPayload(entry))
        XCTAssertEqual(try Data(contentsOf: target), bytes)
    }

    func testLibraryFilteringPreservesOrderAndCancelsWithoutPartialResults() throws {
        func entry(_ file: String, _ name: String, _ date: Date?) -> ModelStore.Entry {
            ModelStore.Entry(url: URL(fileURLWithPath: "/models/\(file).tcad"), name: name, date: date,
                             canOpen: true, requiresRecovery: false, identity: nil)
        }
        let rows = [entry("z", "Ön Kol", Date(timeIntervalSince1970: 10)),
                    entry("a", "Arka Kol", Date(timeIntervalSince1970: 20)),
                    entry("b", "Ön Beden", nil)]
        let nameOrder = try XCTUnwrap(ModelStore.filteredModels(rows, query: "kol", newest: false, cancelled: { false }))
        XCTAssertEqual(nameOrder.map(\.name), ["Arka Kol", "Ön Kol"])
        let byNewest = try XCTUnwrap(ModelStore.filteredModels(rows, query: "", newest: true, cancelled: { false }))
        XCTAssertEqual(byNewest.map(\.name), ["Arka Kol", "Ön Kol", "Ön Beden"])
        let whitespace = try XCTUnwrap(ModelStore.filteredModels(rows, query: "  \n ", newest: true, cancelled: { false }))
        XCTAssertEqual(whitespace.map(\.id), byNewest.map(\.id))
        let acrossFields = try XCTUnwrap(ModelStore.filteredModels(rows, query: "on z", newest: false, cancelled: { false }))
        XCTAssertEqual(acrossFields.map(\.name), ["Ön Kol"])
        var checks = 0
        let cancelled = ModelStore.filteredModels(rows, query: "", newest: false) {
            checks += 1
            return checks == 2
        }
        XCTAssertNil(cancelled)
    }

    func testOpeningSpecialModelFileFailsWithoutWaitingForWriter() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let fifo = root.appendingPathComponent("waiting.tcad")
        XCTAssertEqual(fifo.path.withCString { mkfifo($0, 0o600) }, 0)
        let regular = root.appendingPathComponent("normal.tcad")
        let bytes = Data("model".utf8)
        try bytes.write(to: regular)
        let entries = try ModelStore.models(in: root)
        let special = try XCTUnwrap(entries.first { $0.url == fifo })
        XCTAssertThrowsError(try ModelStore.readListedPayload(special))
        var info = stat()
        XCTAssertEqual(fifo.path.withCString { lstat($0, &info) }, 0)
        XCTAssertEqual(info.st_mode & S_IFMT, S_IFIFO)
        let ordinary = try XCTUnwrap(entries.first { $0.url == regular })
        XCTAssertEqual(try ModelStore.readListedPayload(ordinary), bytes)
    }

    func testReadSnapshotRejectsFileChangedDuringRead() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: url) }
        let bytes = Data(repeating: 0x41, count: 128 * 1024)
        try bytes.write(to: url)
        XCTAssertEqual(try ModelStore.readFileSnapshot(url), bytes)
        XCTAssertThrowsError(try ModelStore.readFileSnapshot(url, afterFirstChunk: {
            let writer = try FileHandle(forWritingTo: url)
            defer { try? writer.close() }
            try writer.seekToEnd()
            try writer.write(contentsOf: Data([0x42]))
        }))
        XCTAssertEqual(try Data(contentsOf: url).count, bytes.count + 1)
    }

    func testOversizedModelIsRejectedBeforeReadingAnyChunk() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: url) }
        XCTAssertTrue(FileManager.default.createFile(atPath: url.path, contents: nil))
        let writer = try FileHandle(forWritingTo: url)
        try writer.truncate(atOffset: UInt64(64 * 1024 * 1024 + 1))
        try writer.close()
        var didRead = false
        XCTAssertThrowsError(try ModelStore.readFileSnapshot(url, afterFirstChunk: { didRead = true }))
        XCTAssertFalse(didRead)
        let size = try XCTUnwrap(FileManager.default.attributesOfItem(atPath: url.path)[.size] as? NSNumber)
        XCTAssertEqual(size.uint64Value, UInt64(64 * 1024 * 1024 + 1))
    }

    func testSpecialMetadataFilesDoNotBlockLibraryOrFollowLinks() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let fifoCopy = root.appendingPathComponent("fifo.tcadcopy", isDirectory: true)
        let linkedCopy = root.appendingPathComponent("linked.tcadcopy", isDirectory: true)
        let normalCopy = root.appendingPathComponent("normal.tcadcopy", isDirectory: true)
        for copy in [fifoCopy, linkedCopy, normalCopy] {
            try FileManager.default.createDirectory(at: copy, withIntermediateDirectories: false)
            try Data("model".utf8).write(to: copy.appendingPathComponent("project.tcad"))
        }
        let fifo = fifoCopy.appendingPathComponent("metadata.json")
        XCTAssertEqual(fifo.path.withCString { mkfifo($0, 0o600) }, 0)
        let external = root.appendingPathComponent("external.json")
        let metadata = Data("{\"version\":1,\"name\":\"External\"}".utf8)
        try metadata.write(to: external)
        try FileManager.default.createSymbolicLink(at: linkedCopy.appendingPathComponent("metadata.json"), withDestinationURL: external)
        try Data("{\"version\":1,\"name\":\"Normal\"}".utf8).write(to: normalCopy.appendingPathComponent("metadata.json"))

        let entries = try ModelStore.models(in: root)
        for copy in [fifoCopy, linkedCopy] {
            let entry = try XCTUnwrap(entries.first { $0.url == copy })
            XCTAssertFalse(entry.canOpen)
            XCTAssertTrue(entry.requiresRecovery)
            XCTAssertEqual(try ModelStore.readListedPayload(entry), Data("model".utf8))
        }
        XCTAssertEqual(try Data(contentsOf: external), metadata)
        let normal = try XCTUnwrap(entries.first { $0.url == normalCopy })
        XCTAssertTrue(normal.canOpen)
        XCTAssertEqual(normal.name, "Normal")
    }

    func testInvalidOrMissingMetadataOffersRawRecoveryWithoutChangingSource() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let bytes = Data([0, 255, 5, 10])
        let cases: [(String, Data?)] = [
            ("future", Data("{\"version\":2,\"name\":\"Future\"}".utf8)),
            ("corrupt", Data([255, 0, 1])),
            ("duplicate-version", Data(#"{"version":1,"\u0076ersion":2,"name":"Ambiguous"}"#.utf8)),
            ("duplicate-name", Data(#"{"version":1,"name":"First","name":"Second"}"#.utf8)),
            ("unknown-field", Data(#"{"version":1,"name":"Extra","critical":true}"#.utf8)),
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
