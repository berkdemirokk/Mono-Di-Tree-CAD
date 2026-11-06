import XCTest
import ImageIO
import CoreGraphics
import UIKit
@testable import TekstilCADViewer_iOS

private func makeTestRequest(prompt: String, measurements: String, image: Data?, imageName: String?) throws -> Data {
    try ExperimentalDesignBrief.make(.init(style: .describe, description: prompt, bust: "", waist: "", hips: "", length: "", otherMeasurements: measurements, image: image, imageName: imageName))
}


#if DEBUG
@MainActor
final class DesignDraftStoreTests: XCTestCase {
    func testPhotoSelectionSnapshotRejectsChangedSourceAndStaleSelection() {
        let original = ExperimentalDesignBrief.Draft(style: .aline, description: "tarif", bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "not", image: nil, imageName: nil)
        let revision = UUID(), ticket = ExperimentalDesignBrief.PhotoSelectionTicket(draft: original, revision: revision)
        XCTAssertTrue(ticket.matches(id: ticket.id, draft: original, revision: revision))
        XCTAssertFalse(ticket.matches(id: nil, draft: original, revision: revision))
        XCTAssertFalse(ticket.matches(id: UUID(), draft: original, revision: revision))
        XCTAssertFalse(ticket.matches(id: ticket.id, draft: original, revision: UUID()))
        let changed = ExperimentalDesignBrief.Draft(style: .aline, description: "değişti", bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "not", image: nil, imageName: nil)
        XCTAssertFalse(ticket.matches(id: ticket.id, draft: changed, revision: revision))
    }
    func testHEICConversionAppliesOrientationAndOmitsPersonalMetadata() throws {
        guard let context = CGContext(data: nil, width: 64, height: 32, bitsPerComponent: 8, bytesPerRow: 0,
                                      space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue),
              let image = context.makeImage() else { return XCTFail("Test image creation failed") }
        let encoded = NSMutableData()
        guard let destination = CGImageDestinationCreateWithData(encoded as CFMutableData, "public.heic" as CFString, 1, nil) else { throw XCTSkip("HEIC encoder unavailable") }
        let metadata: [CFString: Any] = [kCGImagePropertyOrientation: 6,
            kCGImagePropertyGPSDictionary: [kCGImagePropertyGPSLatitude: 41.0, kCGImagePropertyGPSLongitude: 29.0]]
        CGImageDestinationAddImage(destination, image, metadata as CFDictionary)
        guard CGImageDestinationFinalize(destination) else { throw XCTSkip("HEIC encoder unavailable") }
        let source = encoded as Data
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let url = directory.appendingPathComponent("kaynak.heic")
        try source.write(to: url)
        let filesSelection = try HEICPhotoConversion.prepareReference(ExperimentalDesignBrief.readImage(url), name: url.lastPathComponent)
        let photosSelection = try PhotoLibraryReference.read(url).selection
        XCTAssertEqual(filesSelection.name, "Fotoğraf.png"); XCTAssertEqual(photosSelection.name, "Fotoğraf.png")
        XCTAssertEqual(filesSelection.data, photosSelection.data)
        XCTAssertEqual(try Data(contentsOf: url), source)
        let output = filesSelection.data
        XCTAssertLessThanOrEqual(output.count, ExperimentalDesignBrief.maxImageBytes)
        let decoded = try XCTUnwrap(CGImageSourceCreateWithData(output as CFData, nil))
        XCTAssertEqual(CGImageSourceGetType(decoded) as String?, "public.png")
        let props = try XCTUnwrap(CGImageSourceCopyPropertiesAtIndex(decoded, 0, nil) as? [CFString: Any])
        XCTAssertEqual(props[kCGImagePropertyPixelWidth] as? Int, 32)
        XCTAssertEqual(props[kCGImagePropertyPixelHeight] as? Int, 64)
        XCTAssertNil(props[kCGImagePropertyGPSDictionary])
        XCTAssertNil(props[kCGImagePropertyExifDictionary])
        XCTAssertNoThrow(try ExperimentalDesignBrief.validateV2Image(output))
        XCTAssertEqual(encoded as Data, source)
    }
    func testHEICAllOrientationsAgainstUIKitIncludingMirrors() throws {
        guard let context = CGContext(data: nil, width: 64, height: 32, bitsPerComponent: 8, bytesPerRow: 0,
                                      space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return XCTFail("No test context") }
        for (color, rect) in [(UIColor.red, CGRect(x: 0, y: 0, width: 32, height: 16)),
                              (UIColor.green, CGRect(x: 32, y: 0, width: 32, height: 16)),
                              (UIColor.blue, CGRect(x: 0, y: 16, width: 32, height: 16)),
                              (UIColor.yellow, CGRect(x: 32, y: 16, width: 32, height: 16))] {
            context.setFillColor(color.cgColor); context.fill(rect)
        }
        let image = try XCTUnwrap(context.makeImage())
        let orientations: [UIImage.Orientation] = [.up, .upMirrored, .down, .downMirrored, .leftMirrored, .right, .rightMirrored, .left]
        func samples(_ image: UIImage, size: CGSize) throws -> [[UInt8]] {
            let format = UIGraphicsImageRendererFormat(); format.scale = 1; format.opaque = false
            let rendered = UIGraphicsImageRenderer(size: size, format: format).image { _ in image.draw(in: CGRect(origin: .zero, size: size)) }
            let pixels = try XCTUnwrap(rendered.cgImage)
            var data = [UInt8](repeating: 0, count: pixels.width * pixels.height * 4)
            try data.withUnsafeMutableBytes { memory in
                guard let canvas = CGContext(data: memory.baseAddress, width: pixels.width, height: pixels.height, bitsPerComponent: 8,
                                             bytesPerRow: pixels.width * 4, space: CGColorSpaceCreateDeviceRGB(),
                                             bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue | CGBitmapInfo.byteOrder32Big.rawValue) else { throw CocoaError(.fileReadUnknown) }
                canvas.draw(pixels, in: CGRect(x: 0, y: 0, width: CGFloat(pixels.width), height: CGFloat(pixels.height)))
            }
            return [(pixels.width / 4, pixels.height / 4), (pixels.width * 3 / 4, pixels.height / 4),
                    (pixels.width / 4, pixels.height * 3 / 4), (pixels.width * 3 / 4, pixels.height * 3 / 4)].map { x, y in
                let offset = (y * pixels.width + x) * 4; return Array(data[offset..<(offset + 3)])
            }
        }
        for (index, orientation) in orientations.enumerated() {
            let encoded = NSMutableData()
            guard let dest = CGImageDestinationCreateWithData(encoded as CFMutableData, "public.heic" as CFString, 1, nil) else { throw XCTSkip("HEIC encoder unavailable") }
            CGImageDestinationAddImage(dest, image, [kCGImagePropertyOrientation: index + 1] as CFDictionary)
            guard CGImageDestinationFinalize(dest) else { throw XCTSkip("HEIC encoder unavailable") }
            let source = try XCTUnwrap(CGImageSourceCreateWithData(encoded as Data as CFData, nil))
            let properties = try XCTUnwrap(CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any])
            XCTAssertEqual(properties[kCGImagePropertyOrientation] as? Int, index + 1)
            let sourceImage = try XCTUnwrap(CGImageSourceCreateImageAtIndex(source, 0, nil))
            let converted = try HEICPhotoConversion.convert(encoded as Data)
            let resultSource = try XCTUnwrap(CGImageSourceCreateWithData(converted as CFData, nil))
            let result = try XCTUnwrap(CGImageSourceCreateImageAtIndex(resultSource, 0, nil))
            let expectedWidth = index < 4 ? 64 : 32
            let expectedHeight = index < 4 ? 32 : 64
            XCTAssertEqual(result.width, expectedWidth, "Orientation \(index + 1) width")
            XCTAssertEqual(result.height, expectedHeight, "Orientation \(index + 1) height")
            let size = CGSize(width: CGFloat(expectedWidth), height: CGFloat(expectedHeight))
            let reference = UIImage(cgImage: sourceImage, scale: 1, orientation: orientation)
            let expected = try samples(reference, size: size), actual = try samples(UIImage(cgImage: result), size: size)
            for (a, b) in zip(expected.flatMap({ $0 }), actual.flatMap({ $0 })) {
                XCTAssertLessThanOrEqual(abs(Int(a) - Int(b)), 8, "Orientation \(index + 1)")
            }
        }
    }
    func testHEICOutputRejectsOverflowBeforeAppending() {
        let output = HEICPhotoConversion.Output()
        let bytes = Data(repeating: 1, count: ExperimentalDesignBrief.maxImageBytes + 1)
        bytes.withUnsafeBytes { buffer in
            XCTAssertEqual(output.append(buffer.baseAddress!, count: buffer.count), 0)
        }
        XCTAssertTrue(output.exceeded); XCTAssertTrue(output.bytes.isEmpty)
    }
    func testHEICConversionRejectsMultipleFramesWhenEncoderAvailable() throws {
        guard let context = CGContext(data: nil, width: 64, height: 32, bitsPerComponent: 8, bytesPerRow: 0,
                                      space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue),
              let image = context.makeImage() else { return XCTFail("No test image") }
        let encoded = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(encoded as CFMutableData, "public.heic" as CFString, 2, nil) else { throw XCTSkip("Multi-image HEIC encoder unavailable") }
        CGImageDestinationAddImage(dest, image, nil); CGImageDestinationAddImage(dest, image, nil)
        guard CGImageDestinationFinalize(dest), let source = CGImageSourceCreateWithData(encoded as Data as CFData, nil), CGImageSourceGetCount(source) > 1 else { throw XCTSkip("Multi-image HEIC encoder unavailable") }
        XCTAssertThrowsError(try HEICPhotoConversion.convert(encoded as Data))
    }
    func testHEICConversionRejectsInvalidAndOversizedInput() {
        XCTAssertThrowsError(try HEICPhotoConversion.convert(Data([0, 1, 2])))
        XCTAssertThrowsError(try HEICPhotoConversion.convert(Data(count: ExperimentalDesignBrief.maxImageBytes + 1)))
        let png = Data(base64Encoded: "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/aX8AAAAASUVORK5CYII=")!
        XCTAssertThrowsError(try HEICPhotoConversion.convert(png))
        XCTAssertFalse(ExperimentalDesignBrief.v2ImageGeometryAllowed(width: 12_001, height: 1, frames: 1))
        XCTAssertFalse(ExperimentalDesignBrief.v2ImageGeometryAllowed(width: 6_000, height: 5_000, frames: 1))
        XCTAssertFalse(ExperimentalDesignBrief.v2ImageGeometryAllowed(width: 1, height: 1, frames: 2))
    }
    func testReferenceRevisionRejectsChangedThenRestoredPendingInputs() {
        let draft = ExperimentalDesignBrief.Draft(style: .aline, description: "tarif", bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "not", image: Data([1]), imageName: "eski.png")
        var aLine = draft; aLine.aLineGuideInput = .init(values: ["36"] + Array(repeating: "", count: 10), unit: "cm")
        var form = draft; form.measurementForm = .init(values: ["72"] + Array(repeating: "", count: 14), unit: "cm")
        let photo = ExperimentalDesignBrief.Draft(style: .aline, description: "tarif", bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "not", image: Data([2]), imageName: "yeni.png")
        let model = ExperimentalDesignBrief.Draft(style: .wrap, description: "tarif", bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "not", image: Data([1]), imageName: "eski.png")
        for changed in [aLine, form, photo, model] {
            var revision = ExperimentalDesignBrief.ReferenceRevision()
            let originalToken = revision.token
            let ticket = ExperimentalDesignBrief.PhotoSelectionTicket(draft: draft, revision: originalToken)
            revision.changed() // A → B
            XCTAssertFalse(ticket.matches(id: ticket.id, draft: changed, revision: revision.token))
            revision.changed() // B → A; equal content must not revive the ticket.
            XCTAssertFalse(ticket.matches(id: ticket.id, draft: draft, revision: revision.token))
            XCTAssertNotEqual(originalToken, revision.token)
        }
    }
    func testFileReferenceTicketRejectsChangedModelMeasurementsAndStaleResult() throws {
        let oldImage = Data([1, 2, 3])
        let original = ExperimentalDesignBrief.Draft(style: .aline, description: "tarif", bust: "92 cm", waist: "72 cm", hips: "", length: "", otherMeasurements: "not", image: oldImage, imageName: "önceki.png")
        let revision = UUID(), ticket = ExperimentalDesignBrief.PhotoSelectionTicket(draft: original, revision: revision)
        XCTAssertTrue(ticket.matches(id: ticket.id, draft: original, revision: revision))
        let changedModel = ExperimentalDesignBrief.Draft(style: .wrap, description: original.description, bust: original.bust, waist: original.waist, hips: "", length: "", otherMeasurements: original.otherMeasurements, image: oldImage, imageName: original.imageName)
        let changedMeasure = ExperimentalDesignBrief.Draft(style: .aline, description: original.description, bust: original.bust, waist: "73 cm", hips: "", length: "", otherMeasurements: original.otherMeasurements, image: oldImage, imageName: original.imageName)
        XCTAssertFalse(ticket.matches(id: ticket.id, draft: changedModel, revision: revision))
        XCTAssertFalse(ticket.matches(id: ticket.id, draft: changedMeasure, revision: revision))
        XCTAssertFalse(ticket.matches(id: ticket.id, draft: original, revision: UUID()))
        XCTAssertFalse(ticket.matches(id: nil, draft: original, revision: revision))
        XCTAssertFalse(ticket.matches(id: UUID(), draft: original, revision: revision))
        let old = ExperimentalDesignBrief.ReferenceSelection(data: oldImage, name: "önceki.png")
        let replacement = ExperimentalDesignBrief.ReferenceSelection(data: Data([4]), name: "yeni.png")
        let change = ExperimentalDesignBrief.ReferenceChange(original: old, replacement: replacement)
        XCTAssertEqual(try change.apply(to: old), replacement)
        XCTAssertThrowsError(try change.apply(to: .init(data: Data([9]), name: "başka.png")))
        XCTAssertEqual(original.image, oldImage); XCTAssertEqual(original.waist, "72 cm")
    }
    func testPhotoTransferPreservesBytesAndRejectsInvalidOrOversizedFiles() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("picker-file")
        let png = Data(base64Encoded: "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/aX8AAAAASUVORK5CYII=")!
        try png.write(to: url)
        let result = try PhotoLibraryReference.read(url)
        XCTAssertEqual(result.selection.data, png); XCTAssertEqual(result.selection.name, "Fotoğraf.png")
        let files = try HEICPhotoConversion.prepareReference(ExperimentalDesignBrief.readImage(url), name: "kaynak.png")
        XCTAssertEqual(files.data, png); XCTAssertEqual(files.name, "kaynak.png")
        try Data([0, 1]).write(to: url)
        XCTAssertThrowsError(try PhotoLibraryReference.read(url))
        try Data(repeating: 1, count: ExperimentalDesignBrief.maxImageBytes + 1).write(to: url)
        XCTAssertThrowsError(try PhotoLibraryReference.read(url))
    }
    func testMeasurementAdditionReviewRejectsChangedInputsAndPreservesRecovery() throws {
        var values = Array(repeating: "", count: 15); values[8] = "72"
        let form = ExperimentalDesignBrief.MeasurementForm(values: values, unit: "cm"), revision = UUID()
        let review = BodyMeasurementNotes.AdditionReview(form: form, revision: revision, notes: "önceki", existingNotes: "göğüs çevresi 92 cm", savedForm: form)
        XCTAssertTrue(review.matches(form: form, revision: revision, notes: "önceki", existingNotes: "göğüs çevresi 92 cm", savedForm: form))
        var changedValues = values; changedValues[8] = "73"
        XCTAssertFalse(review.matches(form: .init(values: changedValues, unit: "cm"), revision: revision, notes: "önceki", existingNotes: "göğüs çevresi 92 cm", savedForm: form))
        XCTAssertFalse(review.matches(form: .init(values: values, unit: "mm"), revision: revision, notes: "önceki", existingNotes: "göğüs çevresi 92 cm", savedForm: form))
        XCTAssertFalse(review.matches(form: form, revision: UUID(), notes: "önceki", existingNotes: "göğüs çevresi 92 cm", savedForm: form))
        XCTAssertFalse(review.matches(form: form, revision: revision, notes: "değişti", existingNotes: "göğüs çevresi 92 cm", savedForm: form))
        XCTAssertFalse(review.matches(form: form, revision: revision, notes: "önceki", existingNotes: "göğüs çevresi 94 cm", savedForm: form))
        XCTAssertFalse(review.matches(form: form, revision: revision, notes: "önceki", existingNotes: "göğüs çevresi 92 cm", savedForm: nil))
        var draft = ExperimentalDesignBrief.Draft(style: .aline, description: "tarif", bust: "92 cm", waist: "", hips: "", length: "", otherMeasurements: "önceki", image: nil, imageName: nil)
        draft.measurementForm = form
        let recovered = try ExperimentalDesignBrief.DraftStore.recoveryDraft(ExperimentalDesignBrief.DraftStore.recoveryData(draft))
        XCTAssertEqual(recovered, draft); XCTAssertEqual(recovered.measurementForm, form); XCTAssertEqual(recovered.otherMeasurements, "önceki")
        XCTAssertThrowsError(try ExperimentalDesignBrief.make(recovered)); XCTAssertThrowsError(try ExperimentalDesignBrief.makeV2(recovered))
        let oldURL = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "dress-draft-v1", withExtension: "json"))
        XCTAssertNil(try ExperimentalDesignBrief.DraftStore.recoveryDraft(Data(contentsOf: oldURL)).measurementForm)
    }
    func testBodyFormUnitConversionIsExactAndPreservesAnglesAndPendingForm() throws {
        var values = Array(repeating: "", count: 15)
        values[0] = "31,5"; values[8] = "72"; values[12] = "-2,5"
        let before = ExperimentalDesignBrief.MeasurementForm(values: values, unit: "cm")
        let after = try BodyMeasurementNotes.convertUnit(before, to: "mm")
        XCTAssertEqual(after.values[0], "315"); XCTAssertEqual(after.values[8], "720")
        XCTAssertEqual(after.values[12], "-2,5"); XCTAssertEqual(after.values[1], ""); XCTAssertEqual(after.unit, "mm")
        let restored = try BodyMeasurementNotes.convertUnit(after, to: "cm")
        XCTAssertEqual(restored.values[0], "31.5"); XCTAssertEqual(restored.values[8], "72")
        XCTAssertEqual(restored.values[12], "-2,5")
        var draft = ExperimentalDesignBrief.Draft(style: .aline, description: "tarif", bust: "", waist: "", hips: "", length: "", otherMeasurements: "korunan not", image: nil, imageName: nil)
        draft.measurementForm = before
        let old = try ExperimentalDesignBrief.DraftStore.recoveryDraft(ExperimentalDesignBrief.DraftStore.recoveryData(draft))
        XCTAssertEqual(old.measurementForm, before); XCTAssertEqual(old.otherMeasurements, "korunan not")
        draft.measurementForm = after
        let newer = try ExperimentalDesignBrief.DraftStore.recoveryDraft(ExperimentalDesignBrief.DraftStore.recoveryData(draft))
        XCTAssertEqual(newer.measurementForm, after); XCTAssertEqual(newer.measurements, old.measurements)
        XCTAssertThrowsError(try ExperimentalDesignBrief.make(newer)); XCTAssertThrowsError(try ExperimentalDesignBrief.makeV2(newer))
    }
    func testBodyFormUnitConversionRejectsUnclearAndStaleInputs() throws {
        let blank = ExperimentalDesignBrief.MeasurementForm(values: Array(repeating: "", count: 15), unit: "cm")
        XCTAssertEqual(try BodyMeasurementNotes.convertUnit(blank, to: "mm").unit, "mm")
        for bad in ["0", "-1", "1,000", " ", "1001", "abc"] {
            var form = blank; var values = form.values; values[8] = bad; form = .init(values: values, unit: "cm")
            XCTAssertThrowsError(try BodyMeasurementNotes.convertUnit(form, to: "mm"), bad)
            XCTAssertEqual(form.values[8], bad); XCTAssertEqual(form.unit, "cm")
        }
        var tinyValues = blank.values; tinyValues[0] = "0.00000001"
        XCTAssertThrowsError(try BodyMeasurementNotes.convertUnit(.init(values: tinyValues, unit: "mm"), to: "cm"))
        var angleValues = blank.values; angleValues[12] = "0"
        XCTAssertEqual(try BodyMeasurementNotes.convertUnit(.init(values: angleValues, unit: "cm"), to: "mm").values[12], "0")
        let revision = UUID(), change = BodyMeasurementNotes.UnitChange(before: blank, after: try BodyMeasurementNotes.convertUnit(blank, to: "mm"), revision: revision, notes: "not", existingNotes: "bel")
        XCTAssertTrue(change.matches(form: blank, revision: revision, notes: "not", existingNotes: "bel"))
        XCTAssertFalse(change.matches(form: blank, revision: UUID(), notes: "not", existingNotes: "bel"))
        XCTAssertFalse(change.matches(form: blank, revision: revision, notes: "yeni", existingNotes: "bel"))
        XCTAssertFalse(change.matches(form: change.after, revision: revision, notes: "not", existingNotes: "bel"))
        XCTAssertFalse(change.matches(form: blank, revision: revision, notes: "not", existingNotes: "değişti"))
    }
    func testBodyMeasurementInstructionsMatchLocalResearchDefinitions() {
        let expected: [(String, String, Bool)] = [
            ("üst kol çevresi", "Üst kolun en geniş yerinden çevresini ölçün.", false),
            ("ön göğüs", "Göğsün en dolgun yerinden, bir yan dikiş noktasından diğerine ön taraftan ölçün.", false),
            ("göğüs noktaları arası", "İki göğüs noktası arasındaki mesafeyi ölçün.", false),
            ("üst göğüs çevresi", "Göğsün üstünden geçen üst göğüs çevresini ölçün.", false),
            ("ön üst göğüs", "Üst göğüs hattında yan dikiş noktaları arasında ön kısmı ölçün.", false),
            ("omuz üst noktasından göğse", "Boyun ile omzun birleştiği omuz üst noktasından (HPS), göğüs noktalarından geçen göğüs çevresi hizasına doğru düz aşağı ölçün.", false),
            ("omuz üst noktasından arka bele", "Boyun dibindeki omuz üst noktasından sırt üzerinden bele ölçün.", false),
            ("omuz üst noktasından ön bele", "Boyun dibindeki omuz üst noktasından ön taraftan bele ölçün.", false),
            ("bel çevresi", "Bel çevresini ölçün.", false),
            ("belden kalçaya", "Bedenin yan tarafında bel hattından kalça kemiğinin üst noktasına doğru aşağı ölçün.", false),
            ("boyun çevresi", "Boyun çevresini ölçün.", false),
            ("omuzdan omuza", "Bir omuz ucundan sırt üzerinden diğer omuz ucuna ölçün.", false),
            ("omuz eğimi", "Omuz çizgisinin yataya göre aşağı eğimini dereceyle belirtin.", true),
            ("omuzdan bileğe", "Kol doğal şekilde hafif bükülü dururken omuz ucundan kol boyunca bileğe kadar ölçün.", false),
            ("bilek çevresi", "Bilek çevresini ölçün.", false)
        ]
        XCTAssertEqual(BodyMeasurementNotes.fields.count, expected.count)
        XCTAssertEqual(BodyMeasurementNotes.instructions.count, expected.count)
        guard BodyMeasurementNotes.fields.count == expected.count, BodyMeasurementNotes.instructions.count == expected.count else { return }
        for (index, item) in expected.enumerated() {
            XCTAssertEqual(BodyMeasurementNotes.fields[index].label, item.0, "Etiket \(index)")
            XCTAssertEqual(BodyMeasurementNotes.instructions[index], item.1, "Açıklama \(index)")
            XCTAssertEqual(BodyMeasurementNotes.fields[index].angle, item.2, "Birim türü \(index)")
        }
    }
    func testMeasurementFieldJumpLifetimeRejectsDismissedAndReopenedCallbacks() {
        var lifetime = BodyMeasurementNotes.FieldJumpLifetime()
        let beforeAppearance = lifetime.token
        XCTAssertFalse(lifetime.permits(beforeAppearance))
        lifetime.appear()
        let first = lifetime.token
        XCTAssertTrue(lifetime.permits(first)); XCTAssertFalse(lifetime.permits(beforeAppearance))
        lifetime.disappear()
        XCTAssertFalse(lifetime.permits(first)); XCTAssertFalse(lifetime.permits(lifetime.token))
        lifetime.appear()
        XCTAssertFalse(lifetime.permits(first)); XCTAssertTrue(lifetime.permits(lifetime.token))
    }
    func testMeasurementFieldGroupLookupCoversEachFieldWithoutGuessing() {
        for (groupIndex, group) in BodyMeasurementNotes.groups.enumerated() {
            for field in group.indices { XCTAssertEqual(BodyMeasurementNotes.groupIndex(forField: field), groupIndex) }
        }
        let ids = BodyMeasurementNotes.fields.indices.map { BodyMeasurementNotes.fieldScrollID($0) }
        XCTAssertEqual(Set(ids).count, BodyMeasurementNotes.fields.count)
        XCTAssertEqual(BodyMeasurementNotes.fieldScrollID(4), "measurement-field-4")
        XCTAssertNil(BodyMeasurementNotes.groupIndex(forField: -1))
        XCTAssertNil(BodyMeasurementNotes.groupIndex(forField: BodyMeasurementNotes.fields.count))
    }
    func testMeasurementGroupsCoverFieldsAndReportUnfinishedValues() throws {
        let indices = BodyMeasurementNotes.groups.flatMap { Array($0.indices) }
        XCTAssertEqual(indices, Array(BodyMeasurementNotes.fields.indices))
        var values = Array(repeating: "", count: BodyMeasurementNotes.fields.count)
        values[0] = "31,5"; values[8] = "72"; values[12] = " "
        let missing = BodyMeasurementNotes.missingIndices(values)
        XCTAssertEqual(missing.count, 13); XCTAssertTrue(missing.contains(12)); XCTAssertFalse(missing.contains(0))
        let pending = try XCTUnwrap(ExperimentalDesignBrief.MeasurementForm.pending(values: values, unit: "mm"))
        var draft = ExperimentalDesignBrief.Draft(style: .aline, description: "tarif", bust: "92 cm", waist: "", hips: "100 cm", length: "110 cm", otherMeasurements: "not", image: nil, imageName: nil)
        draft.measurementForm = pending
        let restored = try ExperimentalDesignBrief.DraftStore.recoveryDraft(ExperimentalDesignBrief.DraftStore.recoveryData(draft))
        XCTAssertEqual(restored.measurementForm, pending)
        XCTAssertEqual(restored.measurements, draft.measurements)
        XCTAssertThrowsError(try ExperimentalDesignBrief.make(restored))
        XCTAssertThrowsError(try ExperimentalDesignBrief.makeV2(restored))
    }
    func testUnicodeHorizontalSpacingInExplicitMeasurementsPreservesSource() throws {
        for space in ["\u{00A0}", "\u{202F}", "\u{2009}", "\t"] {
            let message = "bel\(space)çevresi: 36\(space)cm"
            let proposal = try MeasurementMessageParser.parse(message, existing: [:])
            XCTAssertEqual(proposal.source, message); XCTAssertEqual(proposal.values["bel çevresi"], "36 cm")
            XCTAssertTrue(MeasurementMessageParser.containsExplicitMeasurement(message))
            let description = "A kesim elbise istiyorum, " + message
            let draft = ExperimentalDesignBrief.Draft(style: .describe, description: description, bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
            XCTAssertEqual(ExperimentalDesignBrief.suggestedModel(draft), .aline)
            let described = try XCTUnwrap(ExperimentalDesignBrief.descriptionMeasurements(draft))
            XCTAssertEqual(described.source, description); XCTAssertEqual(described.values["bel çevresi"], "36 cm")
            XCTAssertEqual(draft.description, description)
            let grouped = "bel çevresi 36\(space)cm\(space)ve\(space)kalça çevresi 90\(space)cm"
            let groupResult = try MeasurementMessageParser.parse(grouped, existing: [:])
            XCTAssertEqual(groupResult.values["kalça çevresi"], "90 cm"); XCTAssertEqual(groupResult.source, grouped)
        }
    }
    func testUnicodeSpacingDoesNotPermitNegationBareValuesOrSplitUnits() {
        for space in ["\u{00A0}", "\u{202F}"] {
            for text in ["bel çevresi 36\(space)cm değil", "bel çevresi 36\(space)cm?", "36\(space)cm", "eğer bel çevresi 36\(space)cm ise"] {
                XCTAssertThrowsError(try MeasurementMessageParser.parse(text, existing: [:]), text)
            }
            let description = "A kesim elbise istiyorum, 36\(space)cm"
            let draft = ExperimentalDesignBrief.Draft(style: .describe, description: description, bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
            XCTAssertNil(ExperimentalDesignBrief.descriptionMeasurements(draft)); XCTAssertEqual(draft.description, description)
            let unresolved = "bu 36\(space)cm olmasın"
            let mixed = ExperimentalDesignBrief.Draft(style: .describe, description: unresolved, bust: "", waist: "", hips: "", length: "", otherMeasurements: "bel çevresi 72\(space)cm", image: nil, imageName: nil)
            XCTAssertThrowsError(try ExperimentalDesignBrief.measurementMessageProposal(mixed))
            XCTAssertEqual(mixed.description, unresolved); XCTAssertEqual(mixed.otherMeasurements, "bel çevresi 72\(space)cm")
            XCTAssertTrue(MeasurementMessageParser.hasConditionalMeasurementRisk(["eğer kumaş uygunsa", mixed.otherMeasurements]))
        }
        for newline in ["\n", "\r", "\u{0085}", "\u{2028}", "\u{2029}"] {
            XCTAssertThrowsError(try MeasurementMessageParser.parse("bel çevresi 36\(newline)cm", existing: [:]))
            let description = "A kesim elbise istiyorum, bel çevresi 36\(newline)cm"
            let draft = ExperimentalDesignBrief.Draft(style: .describe, description: description, bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
            XCTAssertNil(ExperimentalDesignBrief.descriptionMeasurements(draft)); XCTAssertEqual(draft.description, description)
        }
    }
    func testModelPickerListsEveryChoiceExactlyOnce() {
        let styles = ExperimentalDesignBrief.selectableStyles
        XCTAssertEqual(styles, [.wrap, .aline, .corset, .sheath, .straight, .shirt, .describe])
        XCTAssertEqual(styles.count, Set(styles).count)
        XCTAssertEqual(Set(styles), Set(ExperimentalDesignBrief.DressStyle.allCases))
    }
    func testImportGatePreservesPendingFormsAndRejectsStaleApproval() throws {
        let base = ExperimentalDesignBrief.Draft(style: .aline, description: "tarif", bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "korunan not", image: nil, imageName: nil)
        XCTAssertTrue(ExperimentalDesignBrief.canReplaceWithImport(base, referencePending: false, transition: false))
        XCTAssertFalse(ExperimentalDesignBrief.canReplaceWithImport(base, referencePending: true, transition: false))
        XCTAssertFalse(ExperimentalDesignBrief.canReplaceWithImport(base, referencePending: false, transition: true))
        for raw in ["72", " ", "\t"] {
            var fields = Array(repeating: "", count: 15); fields[8] = raw
            var pending = base; pending.measurementForm = .init(values: fields, unit: "cm")
            XCTAssertFalse(ExperimentalDesignBrief.canReplaceWithImport(pending, referencePending: false, transition: false))
            let recovered = try ExperimentalDesignBrief.DraftStore.recoveryDraft(ExperimentalDesignBrief.DraftStore.recoveryData(pending))
            XCTAssertEqual(recovered, pending); XCTAssertEqual(recovered.measurementForm?.values[8], raw)
        }
        var empty = base; empty.measurementForm = .init(values: Array(repeating: "", count: 15), unit: "cm")
        XCTAssertTrue(ExperimentalDesignBrief.canReplaceWithImport(empty, referencePending: false, transition: false))
        var aLine = base; aLine.aLineGuideInput = .init(values: ["36"] + Array(repeating: "", count: 10), unit: "cm")
        XCTAssertFalse(ExperimentalDesignBrief.canReplaceWithImport(aLine, referencePending: false, transition: false))
        let revision = UUID(), review = ExperimentalDesignBrief.ImportReview(draft: base, revision: revision, version: 1, explicitSelection: true, imported: false)
        XCTAssertTrue(review.matches(current: base, revision: revision, version: 1, explicitSelection: true, imported: false, referencePending: false, transition: false))
        XCTAssertFalse(review.matches(current: base, revision: UUID(), version: 1, explicitSelection: true, imported: false, referencePending: false, transition: false))
        XCTAssertFalse(review.matches(current: base, revision: revision, version: 2, explicitSelection: true, imported: false, referencePending: false, transition: false))
        XCTAssertFalse(review.matches(current: base, revision: revision, version: 1, explicitSelection: false, imported: false, referencePending: false, transition: false))
        XCTAssertFalse(review.matches(current: base, revision: revision, version: 1, explicitSelection: true, imported: true, referencePending: false, transition: false))
        XCTAssertFalse(review.matches(current: base, revision: revision, version: 1, explicitSelection: true, imported: false, referencePending: true, transition: false))
        XCTAssertFalse(review.matches(current: aLine, revision: revision, version: 1, explicitSelection: true, imported: false, referencePending: false, transition: false))
        var changedForm = base; changedForm.measurementForm = .init(values: [" "] + Array(repeating: "", count: 14), unit: "mm")
        XCTAssertFalse(review.matches(current: changedForm, revision: revision, version: 1, explicitSelection: true, imported: false, referencePending: false, transition: false))
    }
    func testPortableImportKeepsValidatedOriginalBytesForSharing() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let image = Data(base64Encoded: "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/aX8AAAAASUVORK5CYII=")!
        let draft = ExperimentalDesignBrief.Draft(style: .aline, description: "tarif", bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "korunan not", image: image, imageName: "örnek.png")
        let documents = [try ExperimentalDesignBrief.make(draft), try ExperimentalDesignBrief.makeV2(draft)]
        for (index, document) in documents.enumerated() {
            var original = document; original.append(0x0a)
            let path = folder.appendingPathComponent("source\(index).tcad-request.json")
            try original.write(to: path)
            let imported = try ExperimentalDesignBrief.readPortableSnapshot(path)
            XCTAssertEqual(imported.version, index + 1); XCTAssertEqual(imported.bytes, original)
            XCTAssertEqual(imported.draft.image, image); XCTAssertEqual(imported.draft.otherMeasurements, draft.measurements)
            let attempt = ExperimentalDesignBrief.ExportAttempt(draft: imported.draft, bytes: imported.bytes, version: imported.version)
            XCTAssertTrue(attempt.canPrepareShare(current: imported.draft, referencePending: false, measurementPending: false, transition: false))
            XCTAssertFalse(attempt.canPrepareShare(current: imported.draft, referencePending: true, measurementPending: false, transition: false))
            let id = UUID(), copy = try DesignRequestShareFile.create(bytes: attempt.bytes, id: id, base: folder)
            XCTAssertEqual(try Data(contentsOf: copy), original); XCTAssertTrue(copy.lastPathComponent.hasSuffix(".tcad-request.json"))
            try DesignRequestShareFile.remove(copy, id: id, base: folder)
            XCTAssertEqual(try Data(contentsOf: path), original)
        }
        XCTAssertThrowsError(try ExperimentalDesignBrief.PortableSnapshot.validate(Data("{\"version\":1}".utf8)))
        XCTAssertThrowsError(try ExperimentalDesignBrief.PortableSnapshot.validate(Data(repeating: 0, count: 12 * 1024 * 1024 + 1)))
    }
    func testBackgroundExportSnapshotRejectsChangedDraftAndStaleRevision() {
        let original = ExperimentalDesignBrief.Draft(style: .aline, description: "tarif", bust: "92 cm", waist: "72 cm", hips: "", length: "", otherMeasurements: "not", image: Data([1]), imageName: "foto.png")
        let revision = UUID()
        let snapshot = ExperimentalDesignBrief.ExportPreparationSnapshot(draft: original, version: 2, revision: revision, explicitSelection: true, imported: false)
        func matches(_ draft: ExperimentalDesignBrief.Draft, version: Int = 2, revision: UUID? = nil, selected: Bool = true, imported: Bool = false, referencePending: Bool = false, transition: Bool = false) -> Bool {
            snapshot.matches(current: draft, version: version, revision: revision ?? snapshot.revision, explicitSelection: selected, imported: imported, referencePending: referencePending, transition: transition)
        }
        XCTAssertTrue(matches(original))
        XCTAssertFalse(matches(original, version: 1)); XCTAssertFalse(matches(original, revision: UUID()))
        XCTAssertFalse(matches(original, selected: false)); XCTAssertFalse(matches(original, imported: true))
        XCTAssertFalse(matches(original, referencePending: true)); XCTAssertFalse(matches(original, transition: true))
        var pending = original; pending.measurementForm = .init(values: ["72"] + Array(repeating: "", count: 14), unit: "cm")
        XCTAssertFalse(matches(pending))
        var aLine = original; aLine.aLineGuideInput = .init(values: ["36"] + Array(repeating: "", count: 10), unit: "cm")
        XCTAssertFalse(matches(aLine))
        let changedImage = ExperimentalDesignBrief.Draft(style: .aline, description: "tarif", bust: "92 cm", waist: "72 cm", hips: "", length: "", otherMeasurements: "not", image: Data([2]), imageName: "foto.png")
        XCTAssertFalse(matches(changedImage))
        var revisionToken = ExperimentalDesignBrief.ReferenceRevision(); let initial = revisionToken.token
        let old = ExperimentalDesignBrief.ExportPreparationSnapshot(draft: original, version: 2, revision: initial, explicitSelection: true, imported: false)
        revisionToken.changed(); revisionToken.changed()
        XCTAssertFalse(old.matches(current: original, version: 2, revision: revisionToken.token, explicitSelection: true, imported: false, referencePending: false, transition: false))
    }
    func testRequestSerializationCanRunOffMainActor() async throws {
        let draft = ExperimentalDesignBrief.Draft(style: .aline, description: "tarif", bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "not", image: nil, imageName: nil)
        for version in [1, 2] {
            let worker = Task.detached { () throws -> Data in
                if version == 2 { return try ExperimentalDesignBrief.makeV2(draft) }
                return try ExperimentalDesignBrief.make(draft)
            }
            let bytes = try await worker.value
            let reopened = try ExperimentalDesignBrief.portableDraft(bytes)
            XCTAssertEqual(reopened.measurements, draft.measurements)
        }
    }
    func testShareUsesOnlyCurrentApprovedSavedSnapshot() throws {
        let draft = ExperimentalDesignBrief.Draft(style: .aline, description: "tarif", bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "not", image: nil, imageName: nil)
        for bytes in [try ExperimentalDesignBrief.make(draft), try ExperimentalDesignBrief.makeV2(draft)] {
            let attempt = ExperimentalDesignBrief.ExportAttempt(draft: draft, bytes: bytes)
            XCTAssertTrue(attempt.canPrepareShare(current: draft, referencePending: false, measurementPending: false, transition: false))
            XCTAssertFalse(attempt.canPrepareShare(current: draft, referencePending: true, measurementPending: false, transition: false))
            XCTAssertFalse(attempt.canPrepareShare(current: draft, referencePending: false, measurementPending: true, transition: false))
            XCTAssertFalse(attempt.canPrepareShare(current: draft, referencePending: false, measurementPending: false, transition: true))
            let changed = ExperimentalDesignBrief.Draft(style: .aline, description: "değişti", bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "not", image: nil, imageName: nil)
            XCTAssertFalse(attempt.canPrepareShare(current: changed, referencePending: false, measurementPending: false, transition: false))
            var pending = draft; pending.measurementForm = .init(values: ["1"] + Array(repeating: "", count: 14), unit: "cm")
            XCTAssertFalse(attempt.canPrepareShare(current: pending, referencePending: false, measurementPending: false, transition: false))
        }
    }
    func testSharePopoverAnchorAndOutcomesAreExplicit() {
        XCTAssertEqual(DesignRequestShareHost.anchor(in: CGRect(x: 0, y: 0, width: 320, height: 240)), CGRect(x: 160, y: 120, width: 1, height: 1))
        XCTAssertEqual(DesignRequestShareHost.anchor(in: CGRect(x: 20, y: 10, width: 500, height: 600)), CGRect(x: 270, y: 310, width: 1, height: 1))
        XCTAssertEqual(DesignRequestShareOutcome.classify(completed: true, error: nil), .completed)
        XCTAssertEqual(DesignRequestShareOutcome.classify(completed: false, error: nil), .cancelled)
        XCTAssertEqual(DesignRequestShareOutcome.classify(completed: true, error: CocoaError(.fileReadUnknown)), .failed)
        XCTAssertTrue(DesignRequestShareOutcome.completed.message.contains("ayrıca kontrol"))
        XCTAssertTrue(DesignRequestShareOutcome.cancelled.message.contains("korundu"))
        XCTAssertTrue(DesignRequestShareOutcome.failed.message.contains("korundu"))
    }
    func testShareCloseKeepsFailedCleanupActionableAndIsolatesCopies() throws {
        let base = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: base) }
        let first = UUID(), second = UUID(), data = Data("{}".utf8)
        let file = try DesignRequestShareFile.create(bytes: data, id: first, base: base)
        let other = try DesignRequestShareFile.create(bytes: data, id: second, base: base)
        XCTAssertEqual(DesignRequestShareClose.decide(work: first, cleanup: first, acknowledged: nil), .cancelPreparation)
        XCTAssertEqual(DesignRequestShareClose.decide(work: nil, cleanup: first, acknowledged: nil), .reviewCleanup(first))
        XCTAssertThrowsError(try DesignRequestShareFile.remove(other, id: first, base: base))
        XCTAssertEqual(DesignRequestShareClose.decide(work: nil, cleanup: first, acknowledged: nil), .reviewCleanup(first))
        XCTAssertEqual(try Data(contentsOf: file), data); XCTAssertEqual(try Data(contentsOf: other), data)
        try DesignRequestShareFile.remove(file, id: first, base: base)
        XCTAssertEqual(DesignRequestShareClose.decide(work: nil, cleanup: nil, acknowledged: nil), .proceed)
        XCTAssertEqual(DesignRequestShareClose.decide(work: nil, cleanup: first, acknowledged: first), .proceed)
        var permission: UUID? = first
        // Leaving the second (unsaved-draft) confirmation cancels the entire close.
        DesignRequestShareClose.cancelClose(acknowledged: &permission)
        XCTAssertNil(permission)
        XCTAssertEqual(DesignRequestShareClose.decide(work: nil, cleanup: first, acknowledged: permission), .reviewCleanup(first))
        permission = first // The user explicitly chooses to leave the copy again.
        XCTAssertEqual(DesignRequestShareClose.decide(work: nil, cleanup: first, acknowledged: permission), .proceed)
        XCTAssertEqual(DesignRequestShareClose.decide(work: nil, cleanup: second, acknowledged: first), .reviewCleanup(second))
        XCTAssertEqual(try Data(contentsOf: other), data)
        try DesignRequestShareFile.remove(other, id: second, base: base)
    }
    func testShareTemporaryCopyIsBoundedAndCleanupCannotRemoveOtherFiles() throws {
        let base = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: base) }
        let id = UUID(), bytes = Data("{\"synthetic\":true}".utf8)
        let file = try DesignRequestShareFile.create(bytes: bytes, id: id, base: base)
        XCTAssertEqual(try Data(contentsOf: file), bytes)
        let unrelated = base.appendingPathComponent("other.json"); try bytes.write(to: unrelated)
        XCTAssertThrowsError(try DesignRequestShareFile.remove(unrelated, id: id, base: base))
        XCTAssertThrowsError(try DesignRequestShareFile.create(bytes: bytes, id: id, base: base))
        XCTAssertThrowsError(try DesignRequestShareFile.create(bytes: Data(count: 12 * 1024 * 1024 + 1), id: UUID(), base: base))
        try DesignRequestShareFile.remove(file, id: id, base: base)
        XCTAssertFalse(FileManager.default.fileExists(atPath: file.path)); XCTAssertEqual(try Data(contentsOf: unrelated), bytes)
    }
    func testShareCompletionAndCancellationCleanupKeepsAnotherActiveShare() throws {
        let base = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: base) }
        let data = Data("{}".utf8), activeID = UUID()
        let active = try DesignRequestShareFile.create(bytes: data, id: activeID, base: base)
        for (completed, error) in [(true, nil), (false, nil), (false, CocoaError(.fileReadUnknown))] as [(Bool, Error?)] {
            let id = UUID(), temporary = try DesignRequestShareFile.create(bytes: data, id: id, base: base)
            let outcome = DesignRequestShareOutcome.classify(completed: completed, error: error)
            XCTAssertFalse(outcome.message.isEmpty)
            try DesignRequestShareFile.remove(temporary, id: id, base: base)
            XCTAssertFalse(FileManager.default.fileExists(atPath: temporary.path))
            XCTAssertEqual(try Data(contentsOf: active), data)
        }
        try DesignRequestShareFile.remove(active, id: activeID, base: base)
    }
    func testCancelledSharePreparationDoesNotLeaveTemporaryCopy() async throws {
        let base = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: base) }
        let id = UUID()
        let task = Task { @MainActor in try DesignRequestShareFile.create(bytes: Data("{}".utf8), id: id, base: base) }
        task.cancel()
        do { _ = try await task.value; XCTFail("Cancelled preparation should fail") }
        catch is CancellationError { }
        let folder = base.appendingPathComponent("TekstilCAD-share-" + id.uuidString, isDirectory: true)
        XCTAssertFalse(FileManager.default.fileExists(atPath: folder.path))
    }
    func testRoundTripAndFailedWritePreservesPrevious() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("draft.json")
        let png = Data(base64Encoded: "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/aX8AAAAASUVORK5CYII=")!
        let original = ExperimentalDesignBrief.Draft(style: .aline, description: "Tarif", bust: "92 cm", waist: "", hips: "", length: "", otherMeasurements: "", image: png, imageName: "örnek.png")
        let revision = try ExperimentalDesignBrief.DraftStore.save(original, to: url, expected: nil)
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.load(url)?.draft, original)
        let changed = ExperimentalDesignBrief.Draft(style: .wrap, description: "Yeni", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        XCTAssertThrowsError(try ExperimentalDesignBrief.DraftStore.save(changed, to: url, expected: revision, write: { _, _ in throw CocoaError(.fileWriteOutOfSpace) }))
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.load(url)?.draft, original)
        _ = try ExperimentalDesignBrief.DraftStore.save(changed, to: url, expected: revision)
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.load(url)?.draft, changed)
    }
    func testCorruptExistingFileIsNotOverwritten() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("draft.json"), bad = Data("corrupt".utf8)
        try bad.write(to: url)
        let draft = ExperimentalDesignBrief.Draft(style: .straight, description: "", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        XCTAssertThrowsError(try ExperimentalDesignBrief.DraftStore.save(draft, to: url, expected: nil))
        XCTAssertEqual(try Data(contentsOf: url), bad)
    }
    func testEmptyDraftAndCompetingWindows() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("draft.json")
        let first = ExperimentalDesignBrief.Draft(style: .describe, description: "Önce", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let empty = ExperimentalDesignBrief.Draft(style: .describe, description: "", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let rev1 = try ExperimentalDesignBrief.DraftStore.save(first, to: url, expected: nil)
        let rev2 = try ExperimentalDesignBrief.DraftStore.save(empty, to: url, expected: rev1)
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.load(url)?.draft, empty)
        XCTAssertThrowsError(try ExperimentalDesignBrief.DraftStore.save(first, to: url, expected: rev1))
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.load(url)?.revision, rev2)
    }
    func testRecoveryCopyPreservesEmptyAndMeasurementOnlyDrafts() throws {
        let empty = ExperimentalDesignBrief.Draft(style: .describe, description: "", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let measurement = ExperimentalDesignBrief.Draft(style: .describe, description: "", bust: "92 cm", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        for value in [empty, measurement] {
            let encoded = try ExperimentalDesignBrief.DraftStore.recoveryData(value)
            XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.recoveryDraft(encoded), value)
        }
        let longDraft = ExperimentalDesignBrief.Draft(style: .describe, description: String(repeating: "ü", count: 16_385), bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        XCTAssertThrowsError(try ExperimentalDesignBrief.DraftStore.validate(longDraft))
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.recoveryDraft(ExperimentalDesignBrief.DraftStore.recoveryData(longDraft)), longDraft)
        XCTAssertThrowsError(try ExperimentalDesignBrief.DraftStore.recoveryDraft(Data("invalid".utf8)))
        XCTAssertThrowsError(try ExperimentalDesignBrief.DraftStore.recoveryDraft(Data(repeating: 0, count: ExperimentalDesignBrief.DraftStore.recoveryMaxBytes + 1)))
    }
    func testSerialWriterCoalescesEditsAndFlushUsesLatest() {
        let finished = expectation(description: "latest flush")
        let first = ExperimentalDesignBrief.Draft(style: .describe, description: "ilk", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let last = ExperimentalDesignBrief.Draft(style: .describe, description: "son", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let writer = ExperimentalDesignBrief.DraftWriter { value, _, revision in
            XCTAssertEqual(value, last); XCTAssertNil(revision); return Data([42])
        }
        let path = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        writer.configure(revision: nil)
        writer.submit(first, to: path) { _ in XCTFail("Eski bekleyen yazı çalışmamalı") }
        writer.submit(last, to: path, immediately: true) { result in
            if case .failure = result { XCTFail("Son taslak yazılmalı") }
            finished.fulfill()
        }
        wait(for: [finished], timeout: 3)
    }
    func testWriterReportsConflictWithoutReplacingRevision() {
        let finished = expectation(description: "conflict")
        let draft = ExperimentalDesignBrief.Draft(style: .describe, description: "yeni", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let writer = ExperimentalDesignBrief.DraftWriter { _, _, revision in
            XCTAssertEqual(revision, Data([1])); throw ExperimentalDesignBrief.DraftStore.Conflict.changed
        }
        writer.configure(revision: Data([1]))
        writer.submit(draft, to: FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString), immediately: true) { result in
            if case .failure(let error) = result { XCTAssertTrue(error is ExperimentalDesignBrief.DraftStore.Conflict) }
            else { XCTFail("Çakışma bildirilmeli") }
            finished.fulfill()
        }
        wait(for: [finished], timeout: 3)
    }
    func testPauseWaitsForStartedWriteBeforeAcknowledging() {
        let started = expectation(description: "write started"), paused = expectation(description: "barrier finished")
        let release = DispatchSemaphore(value: 0)
        let writer = ExperimentalDesignBrief.DraftWriter { _, _, _ in
            started.fulfill(); _ = release.wait(timeout: .now() + 3); return Data([1])
        }
        let draft = ExperimentalDesignBrief.Draft(style: .describe, description: "içerik", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        writer.configure(revision: nil)
        writer.submit(draft, to: FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString), immediately: true) { _ in }
        wait(for: [started], timeout: 2)
        writer.pause { paused.fulfill() }
        DispatchQueue.global().asyncAfter(deadline: .now() + 0.1) { release.signal() }
        wait(for: [paused], timeout: 3)
    }
    func testImmediateCloseFlushReportsWriteFailure() {
        let finished = expectation(description: "failure delivered")
        let writer = ExperimentalDesignBrief.DraftWriter { _, _, _ in throw CocoaError(.fileWriteOutOfSpace) }
        let draft = ExperimentalDesignBrief.Draft(style: .describe, description: "korunacak", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        writer.configure(revision: nil)
        writer.submit(draft, to: FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString), immediately: true) { result in
            if case .success = result { XCTFail("Yazma hatası kapatma akışına iletilmeli") }
            finished.fulfill()
        }
        wait(for: [finished], timeout: 3)
    }
    func testAcknowledgingStaleWarningsDoesNotRemoveNewOrActiveScene() {
        let displayed: Set<String> = ["old", "active"]
        let now: Set<String> = ["old", "active", "new"]
        XCTAssertEqual(DraftPendingLedger.remaining(current: now, displayed: displayed, active: ["active"]), Set(["active", "new"]))
        XCTAssertEqual(DraftPendingLedger.remaining(current: ["new"], displayed: ["old"], active: []), Set(["new"]))
    }
    func testExpiredButRunningMarkerSurvivesAcknowledgement() {
        let visible: Set<String> = ["running"]
        // Expiration alone does not remove an active writer's identity.
        XCTAssertEqual(DraftPendingLedger.remaining(current: visible, displayed: visible, active: visible), visible)
        // Only after the writer completes or a queue barrier confirms it stopped can it be acknowledged.
        XCTAssertEqual(DraftPendingLedger.remaining(current: visible, displayed: visible, active: []), Set<String>())
    }
    func testOldBarrierDoesNotDeactivateNewerWriteIdentity() {
        let old: Set<String> = ["old"]
        let active: Set<String> = ["old", "new"]
        let afterOldBarrier = active.subtracting(old)
        XCTAssertEqual(DraftPendingLedger.remaining(current: ["old", "new"], displayed: ["old", "new"], active: afterOldBarrier), Set(["new"]))
    }
    func testCorruptLedgerAndResetRace() {
        XCTAssertNil(DraftPendingLedger.decode("{broken"))
        XCTAssertNil(DraftPendingLedger.decode("{\"old\":true}"))
        XCTAssertEqual(DraftPendingLedger.decode("[\"a\",\"b\"]"), Set(["a", "b"]))
        let shown = Data([1]), changed = Data([2])
        XCTAssertFalse(DraftPendingLedger.mayReset(expected: shown, current: changed, hasActiveWrites: false))
        XCTAssertFalse(DraftPendingLedger.mayReset(expected: shown, current: shown, hasActiveWrites: true))
        XCTAssertTrue(DraftPendingLedger.mayReset(expected: shown, current: shown, hasActiveWrites: false))
    }
    func testPortableRecoveryFixture() throws {
        let path = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "dress-draft-v1", withExtension: "json"))
        let draft = try ExperimentalDesignBrief.DraftStore.recoveryDraft(Data(contentsOf: path))
        XCTAssertEqual(draft.style, .aline)
        XCTAssertEqual(draft.description, "Diz altı, uzun kollu")
        XCTAssertEqual(draft.bust, "92 cm")
        XCTAssertNil(draft.image)
    }
    func testPortableRequestFixturePreservesUnicodeAndImage() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "portable-design-request-v1", withExtension: "json"))
        let original = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: Data(contentsOf: url))
        XCTAssertEqual(original.format, "tekstilcad.design-request")
        XCTAssertEqual(original.version, 1)
        let reference = try XCTUnwrap(original.reference)
        let image = try XCTUnwrap(Data(base64Encoded: reference.dataBase64))
        let output = try makeTestRequest(prompt: original.prompt, measurements: original.measurements, image: image, imageName: reference.name)
        let roundTrip = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: output)
        XCTAssertEqual(roundTrip.prompt, original.prompt)
        XCTAssertEqual(roundTrip.measurements, original.measurements)
        XCTAssertEqual(roundTrip.reference?.dataBase64, reference.dataBase64)
        XCTAssertEqual(roundTrip.reference?.mediaType, reference.mediaType)
        let imported = try ExperimentalDesignBrief.portableDraft(Data(contentsOf: url))
        XCTAssertEqual(imported.prompt, original.prompt)
        XCTAssertEqual(imported.measurements, original.measurements)
        XCTAssertEqual(imported.image, image)
        let badVersion = Data(#"{"format":"tekstilcad.design-request","version":2,"prompt":"x","measurements":"","reference":null}"#.utf8)
        XCTAssertThrowsError(try ExperimentalDesignBrief.portableDraft(badVersion))
        let whitespace = try makeTestRequest(prompt: "x", measurements: "  ", image: nil, imageName: nil)
        XCTAssertEqual(try ExperimentalDesignBrief.portableDraft(whitespace).measurements, "  ")
    }
    func testPortableImportRejectsUnknownAndDuplicateFields() {
        let cases = [
            #"{"format":"tekstilcad.design-request","version":1,"prompt":"x","measurements":"","reference":null,"future":true}"#,
            #"{"format":"tekstilcad.design-request","version":1,"prompt":"x","prompt":"y","measurements":"","reference":null}"#,
            #"{"format":"tekstilcad.design-request","version":1,"prompt":"x","measurements":"","reference":{"name":"a.png","mediaType":"image/png","dataBase64":"AA==","future":1}}"#,
            #"{"format":"tekstilcad.design-request","version":1,"prompt":"x","measurements":"","reference":{"name":"a.png","name":"b.png","mediaType":"image/png","dataBase64":"AA=="}}"#
        ]
        for json in cases { XCTAssertThrowsError(try ExperimentalDesignBrief.portableDraft(Data(json.utf8))) }
    }
    func testPortableBase64AcceptsDesktopWhitespaceWithoutChangingBytes() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "portable-design-request-v1", withExtension: "json"))
        let source = try String(contentsOf: url, encoding: .utf8)
        let spaced = source.replacingOccurrences(of: "iVBORw0K", with: "iVBO \\nRw0K")
        let a = try ExperimentalDesignBrief.portableDraft(Data(source.utf8))
        let b = try ExperimentalDesignBrief.portableDraft(Data(spaced.utf8))
        XCTAssertEqual(a.image, b.image)
    }
    func testPortableRequestEditAndExportPreservesFieldsAndImage() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "portable-design-request-v1", withExtension: "json"))
        let original = try ExperimentalDesignBrief.portableDraft(Data(contentsOf: url))
        XCTAssertEqual(original.style, .describe) // v1 has no model ID; do not infer it.
        let editedText = original.description + "\nKol: uzun 👗"
        let whitespaceMeasurements = "  \t\n "
        let output = try makeTestRequest(prompt: editedText, measurements: whitespaceMeasurements,
                                                       image: original.image, imageName: original.imageName)
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let originalPath = dir.appendingPathComponent("original.tcad-request.json")
        let newPath = dir.appendingPathComponent("edited.tcad-request.json")
        let originalBytes = try Data(contentsOf: url)
        try originalBytes.write(to: originalPath, options: .withoutOverwriting)
        try output.write(to: newPath, options: .withoutOverwriting)
        XCTAssertEqual(try Data(contentsOf: originalPath), originalBytes)
        let edited = try ExperimentalDesignBrief.portableDraft(Data(contentsOf: newPath))
        XCTAssertEqual(edited.description, editedText)
        XCTAssertEqual(edited.measurements, whitespaceMeasurements)
        XCTAssertEqual(edited.image, original.image)
        XCTAssertEqual(edited.imageName, original.imageName)
        XCTAssertEqual(original.measurements, "Göğüs çevresi: 92 cm\nBel çevresi: 72 cm")
    }
    func testMalformedImportDoesNotChangeExistingDraftOrImage() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "portable-design-request-v1", withExtension: "json"))
        let old = try ExperimentalDesignBrief.portableDraft(Data(contentsOf: url))
        var current = old
        do { current = try ExperimentalDesignBrief.portableDraft(Data("{broken".utf8)); XCTFail("Bozuk belge reddedilmeli") }
        catch { XCTAssertEqual(current, old); XCTAssertEqual(current.image, old.image) }
    }
    func testReferenceSizeBoundariesPreserveEarlierDraft() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "portable-design-request-v1", withExtension: "json"))
        let prior = try ExperimentalDesignBrief.portableDraft(Data(contentsOf: url))
        var maximum = try XCTUnwrap(prior.image)
        maximum.append(Data(count: ExperimentalDesignBrief.maxImageBytes - maximum.count))
        let output = try makeTestRequest(prompt: "Düz", measurements: "", image: maximum, imageName: "örnek.png")
        let restored = try ExperimentalDesignBrief.portableDraft(output)
        XCTAssertEqual(restored.image, maximum)
        var oversized = maximum; oversized.append(0)
        XCTAssertThrowsError(try makeTestRequest(prompt: "Düz", measurements: "", image: oversized, imageName: "örnek.png"))
        XCTAssertEqual(prior.image, try ExperimentalDesignBrief.portableDraft(Data(contentsOf: url)).image)
    }
    func testFifteenMeasurementFieldsAppendOnlyOnExplicitApply() throws {
        XCTAssertEqual(BodyMeasurementNotes.fields.count, 15)
        var values = Array(repeating: "", count: 15)
        values[0] = "310"; values[12] = "-12,5"
        let old = "Mevcut not 👗"
        let result = try BodyMeasurementNotes.append(values: values, unit: "mm", current: old, existingNotes: old)
        XCTAssertEqual(result, "Mevcut not 👗\nüst kol çevresi 310 mm\nomuz eğimi -12,5 derece")
        XCTAssertEqual(old, "Mevcut not 👗")
        XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "mm", current: old, existingNotes: "üst kol çevresi 32 cm"))
        values[12] = "90"
        XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "cm", current: old, existingNotes: old))
        values[12] = ""; values[0] = "10001"
        XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "mm", current: old, existingNotes: old))
        XCTAssertThrowsError(try BodyMeasurementNotes.append(values: Array(repeating: "", count: 15), unit: "cm", current: old, existingNotes: old))
    }
    func testALineGuideWritesOnlyExplicitElevenValuesAndKeepsSource() throws {
        let input = ALineMeasurementGuide.Input(values: ["36", "36", "50", "50", "2", "2", "3", "3", "20", "60", "180"], unit: "cm")
        let draft = ExperimentalDesignBrief.Draft(style: .aline, description: "A kesim elbise", bust: "", waist: "72 cm", hips: "100 cm", length: "", otherMeasurements: "Özgün not 👗", image: nil, imageName: nil)
        let result = try ALineMeasurementGuide.prepare(input, draft: draft)
        XCTAssertTrue(result.hasPrefix("Özgün not 👗\nÖn bel yayı: 36 cm\nArka bel yayı: 36 cm"))
        XCTAssertTrue(result.contains("Ön bel yayı bolluğu: 2 cm"))
        XCTAssertTrue(result.hasSuffix("Bitmiş etek çevresi (bolluk dahil): 180 cm"))
        XCTAssertEqual(draft.otherMeasurements, "Özgün not 👗"); XCTAssertEqual(draft.waist, "72 cm"); XCTAssertEqual(draft.style, .aline)
        let updated = ExperimentalDesignBrief.Draft(style: draft.style, description: draft.description, bust: draft.bust, waist: draft.waist, hips: draft.hips, length: draft.length, otherMeasurements: result, image: nil, imageName: nil)
        let v1 = try ExperimentalDesignBrief.portableDraft(makeTestRequest(prompt: updated.prompt, measurements: updated.measurements, image: nil, imageName: nil))
        let v2 = try ExperimentalDesignBrief.portableDraft(ExperimentalDesignBrief.makeV2(updated))
        XCTAssertEqual(v1.measurements, updated.measurements); XCTAssertEqual(v2.measurements, updated.measurements)
        XCTAssertEqual(v2.style, .aline); XCTAssertEqual(v2.description, draft.description)
    }
    func testALineGuideStopsSharedConditionalAndNegativeSources() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "aline-conditional-conflicts", withExtension: "json"))
        let examples = try JSONDecoder().decode([String].self, from: Data(contentsOf: url))
        let input = ALineMeasurementGuide.Input(values: ["36", "36", "50", "50", "2", "2", "3", "3", "20", "60", "180"], unit: "cm")
        for source in examples {
            let draft = ExperimentalDesignBrief.Draft(style: .aline, description: source, bust: "", waist: "72 cm", hips: "100 cm", length: "", otherMeasurements: "Özgün not", image: nil, imageName: nil)
            XCTAssertThrowsError(try ALineMeasurementGuide.prepare(input, draft: draft), source)
            XCTAssertEqual(draft.description, source)
            XCTAssertEqual(draft.otherMeasurements, "Özgün not")
        }
    }
    func testALineGuideSharedConjoinedSourceFixtures() throws {
        struct Example: Decodable { let text: String; let valid: Bool }
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "aline-conjoined-sources", withExtension: "json"))
        let examples = try JSONDecoder().decode([Example].self, from: Data(contentsOf: url))
        var values = Array(repeating: "", count: 11); values[8] = "20"
        let input = ALineMeasurementGuide.Input(values: values, unit: "cm")
        for example in examples {
            let draft = ExperimentalDesignBrief.Draft(style: .aline, description: "A kesim elbise", bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: example.text, image: nil, imageName: nil)
            if example.valid {
                let result = try ALineMeasurementGuide.prepare(input, draft: draft)
                XCTAssertTrue(result.hasPrefix(example.text + "\n"), example.text)
                XCTAssertTrue(result.hasSuffix("Belden kalçaya düşüklük: 20 cm"), example.text)
            } else {
                XCTAssertThrowsError(try ALineMeasurementGuide.prepare(input, draft: draft), example.text)
            }
            XCTAssertEqual(draft.otherMeasurements, example.text)
        }
        for uncertain in ["ön bel yayi 36 cm", "on bel yayi 36 cm"] {
            let source = ExperimentalDesignBrief.Draft(style: .aline, description: "A kesim", bust: "", waist: "", hips: "", length: "", otherMeasurements: uncertain, image: nil, imageName: nil)
            XCTAssertThrowsError(try ALineMeasurementGuide.prepare(input, draft: source), uncertain)
            XCTAssertEqual(source.otherMeasurements, uncertain)
        }
        let source = "Ön bel yayı 36 cm ve arka bel yayı 36 cm"
        let draft = ExperimentalDesignBrief.Draft(style: .aline, description: "A kesim", bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: source, image: nil, imageName: nil)
        var conflict = input; conflict.values[0] = "37"
        XCTAssertThrowsError(try ALineMeasurementGuide.prepare(conflict, draft: draft))
        XCTAssertEqual(draft.otherMeasurements, source)
    }
    func testALineGuideSharedFixtureKeepsV2Values() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "ios-aline-11.tcad-request", withExtension: "json"))
        let draft = try ExperimentalDesignBrief.portableDraft(Data(contentsOf: url))
        XCTAssertEqual(draft.style, .aline)
        XCTAssertTrue(draft.measurements.contains("Ön bel yayı: 36 cm"))
        XCTAssertTrue(draft.measurements.contains("Bitmiş etek çevresi (bolluk dahil): 180 cm"))
        let restored = try ExperimentalDesignBrief.portableDraft(ExperimentalDesignBrief.makeV2(draft))
        XCTAssertEqual(restored.measurements, draft.measurements)
        XCTAssertEqual(restored.description, draft.description)
    }
    func testALineGuideRejectsUnclearValuesExistingLabelsAndChangedSources() throws {
        func draft(_ description: String = "A kesim elbise", waist: String = "72 cm", notes: String = "", style: ExperimentalDesignBrief.DressStyle = .aline) -> ExperimentalDesignBrief.Draft {
            .init(style: style, description: description, bust: "", waist: waist, hips: "100 cm", length: "", otherMeasurements: notes, image: Data([1]), imageName: "foto.png")
        }
        let values = ["36", "36", "50", "50", "0", "2", "3", "3", "20", "60", "180"]
        let valid = ALineMeasurementGuide.Input(values: values, unit: "cm")
        XCTAssertNoThrow(try ALineMeasurementGuide.prepare(valid, draft: draft()))
        XCTAssertThrowsError(try ALineMeasurementGuide.prepare(.init(values: values, unit: ""), draft: draft()))
        for (index, bad) in [(0, "0"), (1, "1,000"), (3, "1001"), (4, "-1")] {
            var changed = values; changed[index] = bad
            XCTAssertThrowsError(try ALineMeasurementGuide.prepare(.init(values: changed, unit: "cm"), draft: draft()))
        }
        XCTAssertThrowsError(try ALineMeasurementGuide.prepare(valid, draft: draft(waist: "73 cm")))
        XCTAssertThrowsError(try ALineMeasurementGuide.prepare(valid, draft: draft(notes: "ÖN BEL   YAYI: 37 cm")))
        XCTAssertThrowsError(try ALineMeasurementGuide.prepare(valid, draft: draft("eğer kumaş ipekse", notes: "Özgün not")))
        XCTAssertThrowsError(try ALineMeasurementGuide.prepare(valid, draft: draft(style: .wrap)))
        let original = draft(), revision = UUID()
        let request = ALineMeasurementGuide.Request(draft: original, explicitSelection: true, importedPortable: false, version: 2, revision: revision)
        XCTAssertTrue(request.matches(original, explicitSelection: true, importedPortable: false, version: 2, revision: revision, activeID: request.id, pending: false))
        XCTAssertFalse(request.matches(draft(waist: "73 cm"), explicitSelection: true, importedPortable: false, version: 2, revision: revision, activeID: request.id, pending: false))
        XCTAssertFalse(request.matches(original, explicitSelection: true, importedPortable: false, version: 2, revision: revision, activeID: nil, pending: false))
        XCTAssertFalse(request.matches(original, explicitSelection: true, importedPortable: false, version: 2, revision: revision, activeID: request.id, pending: true))
        XCTAssertFalse(request.matches(original, explicitSelection: false, importedPortable: false, version: 2, revision: revision, activeID: request.id, pending: false))
        XCTAssertFalse(request.matches(original, explicitSelection: true, importedPortable: false, version: 1, revision: revision, activeID: request.id, pending: false))
        XCTAssertEqual(original.image, Data([1])); XCTAssertEqual(original.waist, "72 cm"); XCTAssertTrue(original.otherMeasurements.isEmpty)
    }
    func testALineGuidePartialValuesCanBeAddedInStages() throws {
        let draft = ExperimentalDesignBrief.Draft(style: .aline, description: "A kesim", bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "Özgün not", image: nil, imageName: nil)
        var fields = Array(repeating: "", count: 11); fields[0] = "36"
        let first = try ALineMeasurementGuide.prepare(.init(values: fields, unit: "cm"), draft: draft)
        XCTAssertEqual(first, "Özgün not\nÖn bel yayı: 36 cm")
        var updated = draft; updated = .init(style: .aline, description: draft.description, bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: first, image: nil, imageName: nil)
        fields[0] = ""; fields[1] = "360"; fields[4] = "0"
        let second = try ALineMeasurementGuide.prepare(.init(values: fields, unit: "mm"), draft: updated)
        XCTAssertEqual(second, first + "\nArka bel yayı: 360 mm\nÖn bel yayı bolluğu: 0 mm")
        fields[1] = "370"
        XCTAssertThrowsError(try ALineMeasurementGuide.prepare(.init(values: fields, unit: "mm"), draft: updated))
        fields[1] = ""; fields[4] = " "
        XCTAssertThrowsError(try ALineMeasurementGuide.prepare(.init(values: fields, unit: "mm"), draft: updated))
        XCTAssertEqual(updated.otherMeasurements, first)
    }
    func testALineGuideShowsAddedAndPreservedAndNamesConflict() throws {
        let draft = ExperimentalDesignBrief.Draft(style: .aline, description: "Özgün tarif", bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "Ön bel yayı: 360 mm\nÖzgün not", image: nil, imageName: nil)
        var fields = Array(repeating: "", count: 11); fields[0] = "36"; fields[1] = "36"
        let input = ALineMeasurementGuide.Input(values: fields, unit: "cm")
        let result = try ALineMeasurementGuide.prepare(input, draft: draft)
        let review = ALineMeasurementGuide.reviewLines(input: input, notes: result, draft: draft)
        XCTAssertEqual(review, ["Ön bel yayı: 36 cm — mevcut değer korunacak", "Arka bel yayı: 36 cm — eklenecek"])
        XCTAssertTrue(result.hasPrefix(draft.otherMeasurements)); XCTAssertEqual(draft.description, "Özgün tarif")
        fields[0] = "35"
        XCTAssertThrowsError(try ALineMeasurementGuide.prepare(.init(values: fields, unit: "cm"), draft: draft)) { error in
            guard let failure = error as? ALineMeasurementGuide.Failure, case .fieldConflict(let index) = failure else { return XCTFail("Expected field conflict") }
            XCTAssertEqual(index, 0)
        }
    }
    func testALineGuideUnitConversionIsExactAndRejectsAmbiguity() throws {
        var values = Array(repeating: "", count: 11); values[0] = "36"; values[1] = "35,25"; values[4] = "0"
        let original = ALineMeasurementGuide.Input(values: values, unit: "cm")
        let mm = try ALineMeasurementGuide.convert(original, to: "mm")
        XCTAssertEqual(mm.values[0], "360"); XCTAssertEqual(mm.values[1], "352.5"); XCTAssertEqual(mm.values[4], "0")
        XCTAssertEqual(mm.values[2], ""); XCTAssertEqual(mm.unit, "mm")
        let back = try ALineMeasurementGuide.convert(mm, to: "cm")
        XCTAssertEqual(back.values[0], "36"); XCTAssertEqual(back.values[1], "35.25")
        var ambiguous = original; ambiguous.values[0] = "1,000"
        XCTAssertThrowsError(try ALineMeasurementGuide.convert(ambiguous, to: "mm"))
        XCTAssertEqual(ambiguous.values[0], "1,000"); XCTAssertEqual(ambiguous.unit, "cm")
        var whitespace = original; whitespace.values[3] = " "
        XCTAssertThrowsError(try ALineMeasurementGuide.convert(whitespace, to: "mm"))
        var tiny = ALineMeasurementGuide.Input(values: Array(repeating: "", count: 11), unit: "mm")
        tiny.values[0] = "0.00000001"
        XCTAssertThrowsError(try ALineMeasurementGuide.convert(tiny, to: "cm"))
    }
    func testMeasurementUnknownNoteDoesNotImplyExistingValue() throws {
        var values = Array(repeating: "", count: 15); values[8] = "72"
        let current = "bel çevresi ölçülmedi"
        let result = try BodyMeasurementNotes.append(values: values, unit: "cm", current: current, existingNotes: current)
        XCTAssertEqual(result, "bel çevresi ölçülmedi\nbel çevresi 72 cm")
        XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "cm", current: current, existingNotes: "bel çevresi: 70 cm"))
    }
    func testMeasurementSegmentsDoNotAssignAnotherLabelsNumber() throws {
        var values = Array(repeating: "", count: BodyMeasurementNotes.fields.count); values[8] = "72"
        for current in ["bel çevresi ölçülmedi; kalça çevresi 100 cm", "kalça çevresi 100 cm; bel çevresi ölçülmedi", "bel çevresi ölçülmedi\nkalça çevresi 100 cm"] {
            let result = try BodyMeasurementNotes.append(values: values, unit: "cm", current: current, existingNotes: current)
            XCTAssertEqual(result, current + "\nbel çevresi 72 cm")
        }
        for current in ["bel çevresi 70 cm; kalça çevresi 100 cm", "bel çevresi ölçülmedi; bel çevresi 70 cm", "bel çevresi ölçülmedi ama 70 cm"] {
            XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "cm", current: current, existingNotes: current, allowAmbiguous: true)) { error in
                guard let validation = error as? BodyMeasurementNotes.ValidationError, case .duplicate(let label) = validation else { return XCTFail("Expected duplicate for \(current): \(error)") }
                XCTAssertEqual(label, "bel çevresi")
            }
            XCTAssertEqual(values[8], "72")
        }
        let uncertain = "bel çevresi kontrol edilecek; kalça çevresi 100 cm"
        XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "cm", current: uncertain, existingNotes: uncertain)) { error in
            guard let validation = error as? BodyMeasurementNotes.ValidationError, case .ambiguous(let label) = validation else { return XCTFail("Expected ambiguity: \(error)") }
            XCTAssertEqual(label, "bel çevresi")
        }
        XCTAssertEqual(try BodyMeasurementNotes.append(values: values, unit: "cm", current: uncertain, existingNotes: uncertain, allowAmbiguous: true), uncertain + "\nbel çevresi 72 cm")
        let unlabeled = "bel çevresi ölçülmedi; yaklaşık 70 cm"
        XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "cm", current: unlabeled, existingNotes: unlabeled)) { error in
            guard let validation = error as? BodyMeasurementNotes.ValidationError, case .ambiguous(let label) = validation else { return XCTFail("Expected ambiguity: \(error)") }
            XCTAssertEqual(label, "bel çevresi")
        }
        let nextQuestion = "bel çevresi ölçülmedi; kalça çevresi 100 cm?"
        XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "cm", current: nextQuestion, existingNotes: nextQuestion))
        XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "cm", current: "", existingNotes: "Bel çevresi 70 cm"))
    }
    func testMeasurementNotesAllowExactTextLimit() throws {
        var values = Array(repeating: "", count: 15); values[8] = "72"
        let line = "bel çevresi 72 cm"
        let base = String(repeating: "x", count: ExperimentalDesignBrief.maxTextUnits - line.utf16.count - 1)
        let result = try BodyMeasurementNotes.append(values: values, unit: "cm", current: base, existingNotes: base)
        XCTAssertEqual(result.utf16.count, ExperimentalDesignBrief.maxTextUnits)
        XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "cm", current: base + "x", existingNotes: base))
    }
    func testUnlabeledSegmentsRemainAmbiguousUntilNextKnownLabel() throws {
        var values = Array(repeating: "", count: BodyMeasurementNotes.fields.count); values[8] = "72"
        for note in ["bel çevresi ölçülmedi; yaklaşık; 70 cm", "bel çevresi ölçülmedi;;; 70", "bel çevresi ölçülmedi\n\nacaba?", "bel çevresi ölçülmedi; şayet; kalça çevresi 100 cm", "bel çevresi ölçülmedi; açıklama; 70 cm; kalça çevresi 100 cm"] {
            XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "cm", current: note, existingNotes: note)) { error in
                guard let validation = error as? BodyMeasurementNotes.ValidationError, case .ambiguous(let label) = validation else { return XCTFail("Expected ambiguity: \(error)") }
                XCTAssertEqual(label, "bel çevresi")
            }
            XCTAssertEqual(values[8], "72")
        }
        for note in ["bel çevresi ölçülmedi; kalça çevresi 100 cm", "bel çevresi ölçülmedi;;; kalça çevresi 100 cm", "bel çevresi ölçülmedi; not; kalça çevresi 100 cm; başka 70 cm"] {
            XCTAssertEqual(try BodyMeasurementNotes.append(values: values, unit: "cm", current: note, existingNotes: note), note + "\nbel çevresi 72 cm")
        }
        XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "cm", current: "", existingNotes: String(repeating: ";", count: ExperimentalDesignBrief.maxTextUnits + 1)))
    }
    func testMeasurementConflictAndAmbiguousNoteRequireReview() throws {
        var values = Array(repeating: "", count: 15); values[8] = "72"
        for old in ["bel çevresi yaklaşık 70 cm", "bel çevresi = 70 cm", "Not: bel çevresi 70 cm"] {
            XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "cm", current: old, existingNotes: old, allowAmbiguous: true))
        }
        let ambiguous = "bel çevresi daha önce kontrol edilecek"
        XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "cm", current: ambiguous, existingNotes: ambiguous))
        XCTAssertEqual(try BodyMeasurementNotes.append(values: values, unit: "cm", current: ambiguous, existingNotes: ambiguous, allowAmbiguous: true), ambiguous + "\nbel çevresi 72 cm")
        let unknown = "bel çevresi ölçülmedi"
        XCTAssertEqual(try BodyMeasurementNotes.append(values: values, unit: "cm", current: unknown, existingNotes: unknown), unknown + "\nbel çevresi 72 cm")
    }
    func testImportedModelLabelIsNeverSilentlyReplaced() throws {
        let old = "Seçilen elbise modeli: A kesim elbise\nİpek 👗"
        let windows = try makeTestRequest(prompt: old, measurements: "Bel çevresi: 72 cm", image: nil, imageName: nil)
        let imported = try ExperimentalDesignBrief.portableDraft(windows)
        XCTAssertEqual(imported.style, .describe)
        XCTAssertEqual(imported.prompt, old)
        XCTAssertTrue(ExperimentalDesignBrief.hasConflictingModelLabels(style: .wrap, description: imported.description))
        XCTAssertFalse(ExperimentalDesignBrief.hasConflictingModelLabels(style: .describe, description: imported.description))
        let preserved = try makeTestRequest(prompt: imported.prompt, measurements: imported.measurements, image: nil, imageName: nil)
        let reopened = try ExperimentalDesignBrief.portableDraft(preserved)
        XCTAssertEqual(reopened.prompt, old)
        XCTAssertEqual(reopened.measurements, "Bel çevresi: 72 cm")
        XCTAssertTrue(ExperimentalDesignBrief.hasEmbeddedModelLine("Not\n SEÇİLEN ELBİSE MODELİ: Düz elbise"))
        XCTAssertFalse(ExperimentalDesignBrief.hasEmbeddedModelLine("İpek elbise"))
        XCTAssertTrue(ExperimentalDesignBrief.hasEmbeddedModelLine("Model: A kesim elbise"))
        XCTAssertTrue(ExperimentalDesignBrief.hasEmbeddedModelLine("Seçilen elbise modeli : A kesim elbise"))
        XCTAssertTrue(ExperimentalDesignBrief.hasConflictingModelLabels(style: .wrap, description: "Model: A kesim elbise"))
        XCTAssertFalse(ExperimentalDesignBrief.hasEmbeddedModelLine("Modelci: öneri"))
    }
    func testEmptyMeasurementFormDoesNotBlockRegardlessOfUnit() {
        let empty = Array(repeating: "", count: 15)
        XCTAssertNil(ExperimentalDesignBrief.MeasurementForm.pending(values: empty, unit: "cm"))
        XCTAssertNil(ExperimentalDesignBrief.MeasurementForm.pending(values: empty, unit: "mm"))
        let legacyEmpty = ExperimentalDesignBrief.MeasurementForm(values: empty, unit: "mm")
        XCTAssertFalse(legacyEmpty.hasValues)
        let draft = ExperimentalDesignBrief.Draft(style: .describe, description: "", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil, measurementForm: legacyEmpty)
        XCTAssertFalse(draft.hasInput)
        var entered = empty; entered[8] = "72"
        let form = ExperimentalDesignBrief.MeasurementForm.pending(values: entered, unit: "mm")
        XCTAssertEqual(form?.values[8], "72"); XCTAssertEqual(form?.unit, "mm")
        var spaces = empty; spaces[0] = " "
        XCTAssertNotNil(ExperimentalDesignBrief.MeasurementForm.pending(values: spaces, unit: "mm"))
    }
    func testWhitespaceFormRequiresExplicitCleanupWithoutChangingRealValues() throws {
        var values = Array(repeating: "", count: 15)
        values[0] = " \t"; values[8] = " 72 "
        let before = ExperimentalDesignBrief.MeasurementForm.pending(values: values, unit: "mm")
        XCTAssertNotNil(before)
        XCTAssertEqual(BodyMeasurementNotes.whitespaceOnlyIndices(values), [0])
        XCTAssertThrowsError(try BodyMeasurementNotes.append(values: values, unit: "mm", current: "önceki", existingNotes: "önceki"))
        XCTAssertEqual(before?.values, values)
        let after = BodyMeasurementNotes.clearingWhitespaceOnly(values)
        XCTAssertEqual(after[0], ""); XCTAssertEqual(after[8], " 72 ")
        XCTAssertEqual(try BodyMeasurementNotes.append(values: after, unit: "mm", current: "önceki", existingNotes: "önceki"), "önceki\nbel çevresi 72 mm")
        XCTAssertNil(ExperimentalDesignBrief.MeasurementForm.pending(values: BodyMeasurementNotes.clearingWhitespaceOnly([" ", "\t"]), unit: "cm"))
    }
    func testUnapprovedMeasurementFormRoundTripDoesNotModifyNotes() throws {
        var fields = Array(repeating: "", count: 15); fields[0] = "310"; fields[12] = "-12,5"
        let form = ExperimentalDesignBrief.MeasurementForm(values: fields, unit: "mm")
        let original = ExperimentalDesignBrief.Draft(style: .aline, description: "Tarif 👗", bust: "92 cm", waist: "", hips: "", length: "", otherMeasurements: "eski not", image: nil, imageName: nil, measurementForm: form)
        XCTAssertEqual(original.measurements, "Göğüs çevresi: 92 cm\neski not")
        let bytes = try ExperimentalDesignBrief.DraftStore.recoveryData(original)
        let reopened = try ExperimentalDesignBrief.DraftStore.recoveryDraft(bytes)
        XCTAssertEqual(reopened.measurementForm, form)
        XCTAssertEqual(reopened.measurements, original.measurements)
        let parsed = try XCTUnwrap(JSONSerialization.jsonObject(with: bytes) as? [String: Any])
        XCTAssertEqual(parsed["version"] as? Int, 2)
        var downgraded = parsed; downgraded["version"] = 1
        XCTAssertThrowsError(try ExperimentalDesignBrief.DraftStore.recoveryDraft(JSONSerialization.data(withJSONObject: downgraded)))
        let oldFixture = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "dress-draft-v1", withExtension: "json"))
        XCTAssertNil(try ExperimentalDesignBrief.DraftStore.recoveryDraft(Data(contentsOf: oldFixture)).measurementForm)
    }
    func testBothPortableExportVersionsRejectAnyPendingALineInput() throws {
        let base = ExperimentalDesignBrief.Draft(style: .aline, description: "A kesim", bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "korunan not", image: nil, imageName: nil)
        for input in [ALineMeasurementGuide.Input(values: ["36"] + Array(repeating: "", count: 10), unit: ""),
                      ALineMeasurementGuide.Input(values: Array(repeating: "", count: 11), unit: "cm")] {
            var pending = base; pending.aLineGuideInput = input
            XCTAssertThrowsError(try ExperimentalDesignBrief.make(pending)) { error in
                guard let issue = error as? ExperimentalDesignBrief.Error, case .pendingALineGuide = issue else { return XCTFail("Unexpected error: \(error)") }
            }
            XCTAssertThrowsError(try ExperimentalDesignBrief.makeV2(pending)) { error in
                guard let issue = error as? ExperimentalDesignBrief.Error, case .pendingALineGuide = issue else { return XCTFail("Unexpected error: \(error)") }
            }
            XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.recoveryDraft(ExperimentalDesignBrief.DraftStore.recoveryData(pending)), pending)
        }
        XCTAssertEqual(try ExperimentalDesignBrief.portableDraft(ExperimentalDesignBrief.make(base)).measurements, base.measurements)
        XCTAssertEqual(try ExperimentalDesignBrief.portableDraft(ExperimentalDesignBrief.makeV2(base)).measurements, base.measurements)
    }
    func testPortableExportRejectsPendingFifteenFieldFormWithoutChangingRecovery() throws {
        let base = ExperimentalDesignBrief.Draft(style: .aline, description: "A kesim", bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "eski not", image: nil, imageName: nil)
        let exporters: [(ExperimentalDesignBrief.Draft) throws -> Data] = [ExperimentalDesignBrief.make, ExperimentalDesignBrief.makeV2]
        for entry in ["72", "0", " ", "\t"] {
            var fields = Array(repeating: "", count: 15); fields[8] = entry
            var pending = base; pending.measurementForm = .pending(values: fields, unit: "cm")
            XCTAssertNotNil(pending.measurementForm)
            for export in exporters {
                XCTAssertThrowsError(try export(pending)) { error in
                    guard let issue = error as? ExperimentalDesignBrief.Error, case .pendingMeasurementForm = issue else { return XCTFail("Unexpected error: \(error)") }
                }
            }
            let restored = try ExperimentalDesignBrief.DraftStore.recoveryDraft(ExperimentalDesignBrief.DraftStore.recoveryData(pending))
            XCTAssertEqual(restored, pending); XCTAssertEqual(restored.measurements, base.measurements)
            XCTAssertEqual(restored.otherMeasurements, "eski not")
        }
        var empty = base; empty.measurementForm = .init(values: Array(repeating: "", count: 15), unit: "cm")
        for export in exporters {
            XCTAssertEqual(try ExperimentalDesignBrief.portableDraft(export(empty)).measurements, base.measurements)
            XCTAssertEqual(try ExperimentalDesignBrief.portableDraft(export(base)).measurements, base.measurements)
        }
        XCTAssertNil(ExperimentalDesignBrief.MeasurementForm.pending(values: Array(repeating: "", count: 15), unit: "cm"))
    }
    func testPendingALineGuideSurvivesLocalAndRecoveryWithoutApplying() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let url = directory.appendingPathComponent("draft.json")
        var values = Array(repeating: "", count: 11); values[0] = "36,5"; values[4] = "0"
        let pending = ALineMeasurementGuide.Input(values: values, unit: "")
        let draft = ExperimentalDesignBrief.Draft(style: .aline, description: "A kesim", bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "önceki not", image: nil, imageName: nil, aLineGuideInput: pending)
        XCTAssertTrue(draft.hasInput)
        _ = try ExperimentalDesignBrief.DraftStore.save(draft, to: url, expected: nil)
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.load(url)?.draft, draft)
        let bytes = try ExperimentalDesignBrief.DraftStore.recoveryData(draft)
        let restored = try ExperimentalDesignBrief.DraftStore.recoveryDraft(bytes)
        XCTAssertEqual(restored.aLineGuideInput, pending)
        XCTAssertEqual(restored.measurements, draft.measurements)
        XCTAssertFalse(restored.measurements.contains("Ön bel yayı"))
        XCTAssertThrowsError(try ExperimentalDesignBrief.make(restored))
        XCTAssertThrowsError(try ExperimentalDesignBrief.makeV2(restored))
        var approved = restored; approved.aLineGuideInput = nil
        let v1 = try ExperimentalDesignBrief.portableDraft(ExperimentalDesignBrief.make(approved))
        let v2 = try ExperimentalDesignBrief.portableDraft(ExperimentalDesignBrief.makeV2(approved))
        XCTAssertNil(v1.aLineGuideInput); XCTAssertNil(v2.aLineGuideInput)
        XCTAssertEqual(v1.measurements, restored.measurements); XCTAssertEqual(v2.measurements, restored.measurements)
        var both = draft; both.measurementForm = .init(values: Array(repeating: "", count: 15), unit: "cm")
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.recoveryDraft(ExperimentalDesignBrief.DraftStore.recoveryData(both)), both)
        let json = try XCTUnwrap(JSONSerialization.jsonObject(with: bytes) as? [String: Any])
        XCTAssertEqual(json["version"] as? Int, 3)
        for version in [1, 2] {
            var downgraded = json; downgraded["version"] = version
            XCTAssertThrowsError(try ExperimentalDesignBrief.DraftStore.recoveryDraft(JSONSerialization.data(withJSONObject: downgraded)))
        }
        var invalid = draft; invalid.aLineGuideInput = .init(values: ["1"], unit: "cm")
        XCTAssertThrowsError(try ExperimentalDesignBrief.DraftStore.recoveryData(invalid))
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.load(url)?.draft, draft)
        let oldFixture = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "dress-draft-v1", withExtension: "json"))
        XCTAssertNil(try ExperimentalDesignBrief.DraftStore.recoveryDraft(Data(contentsOf: oldFixture)).aLineGuideInput)
    }
    func testUnapprovedFormRevisionConflictRetainsPriorSnapshot() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("draft.json")
        var fields = Array(repeating: "", count: 15); fields[8] = "72"
        let draft = ExperimentalDesignBrief.Draft(style: .describe, description: "", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil, measurementForm: .init(values: fields, unit: "cm"))
        let first = try ExperimentalDesignBrief.DraftStore.save(draft, to: url, expected: nil)
        let second = try ExperimentalDesignBrief.DraftStore.save(draft, to: url, expected: first)
        XCTAssertThrowsError(try ExperimentalDesignBrief.DraftStore.save(draft, to: url, expected: first))
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.load(url)?.revision, second)
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.load(url)?.draft.measurementForm, draft.measurementForm)
    }
    func testReferenceChangesRequireMatchingOriginalAndPreserveBytes() throws {
        let original = ExperimentalDesignBrief.ReferenceSelection(data: Data([1, 2]), name: "ilk.png")
        let replacement = ExperimentalDesignBrief.ReferenceSelection(data: Data([3, 4]), name: "yeni.png")
        let changedElsewhere = ExperimentalDesignBrief.ReferenceSelection(data: Data([5]), name: "başka.png")
        let replace = ExperimentalDesignBrief.ReferenceChange(original: original, replacement: replacement)
        XCTAssertEqual(try replace.apply(to: original), replacement)
        XCTAssertThrowsError(try replace.apply(to: changedElsewhere))
        XCTAssertEqual(original.data, Data([1, 2]))
        let remove = ExperimentalDesignBrief.ReferenceChange(original: original, replacement: nil)
        XCTAssertNil(try remove.apply(to: original))
        XCTAssertThrowsError(try remove.apply(to: changedElsewhere))
    }
    func testPhotoObservationsRequireExplicitChoicesAndPreserveExistingText() throws {
        let empty = PhotoObservation.Selection()
        XCTAssertTrue(empty.lines.isEmpty)
        XCTAssertThrowsError(try PhotoObservation.append(empty, description: "Tarif", notes: ""))
        let selected = PhotoObservation.Selection(silhouette: .aline, sleeve: .long, neck: .unknown)
        XCTAssertEqual(selected.lines, ["Fotoğraf gözlemi · Siluet: A kesim", "Fotoğraf gözlemi · Kol: Uzun kollu"])
        let original = "Kolsuz model 👗"
        let result = try PhotoObservation.append(selected, description: original, notes: "Bel çevresi: 72 cm")
        XCTAssertEqual(result, original + "\n" + selected.lines.joined(separator: "\n"))
        XCTAssertEqual(original, "Kolsuz model 👗") // Contradictions remain visible for manual review.
        XCTAssertEqual(try PhotoObservation.append(selected, description: result, notes: ""), result)
        XCTAssertThrowsError(try PhotoObservation.append(selected, description: original, notes: "Fotoğraf gözlemi · Kol: Kolsuz"))
        XCTAssertThrowsError(try PhotoObservation.append(selected, description: "Not; Fotoğraf gözlemi · Kol: Kolsuz", notes: ""))
        XCTAssertThrowsError(try PhotoObservation.append(selected, description: String(repeating: "x", count: ExperimentalDesignBrief.maxTextUnits), notes: ""))
    }
    func testPhotoObservationSkipsIdenticalChoicesButRejectsConflictsAtomically() throws {
        let selected = PhotoObservation.Selection(silhouette: .aline, sleeve: .long, neck: .round)
        let original = "Özgün tarif\nFotoğraf gözlemi · Kol: Uzun kollu"
        let expected = original + "\nFotoğraf gözlemi · Siluet: A kesim\nFotoğraf gözlemi · Yaka: Yuvarlak yaka"
        XCTAssertEqual(try PhotoObservation.append(selected, description: original, notes: ""), expected)
        XCTAssertEqual(try PhotoObservation.append(selected, description: original, notes: "Fotoğraf gözlemi · Siluet: A kesim"), original + "\nFotoğraf gözlemi · Yaka: Yuvarlak yaka")
        let onlyExisting = PhotoObservation.Selection(silhouette: .unknown, sleeve: .long, neck: .unknown)
        XCTAssertEqual(try PhotoObservation.append(onlyExisting, description: original, notes: ""), original)
        let conflicting = "Fotoğraf gözlemi · Kol: Kolsuz"
        XCTAssertThrowsError(try PhotoObservation.append(selected, description: original, notes: conflicting))
        XCTAssertEqual(original, "Özgün tarif\nFotoğraf gözlemi · Kol: Uzun kollu")
        XCTAssertEqual(conflicting, "Fotoğraf gözlemi · Kol: Kolsuz")
        XCTAssertThrowsError(try PhotoObservation.append(selected, description: String(repeating: "x", count: ExperimentalDesignBrief.maxTextUnits), notes: ""))
    }
    func testPhotoObservationEditReplacesOrRemovesOnlyOneCompleteLine() throws {
        let original = "Tarif 👗\r\nFotoğraf gözlemi · Kol: Kolsuz\r\nSon satır"
        let selection = PhotoObservation.Selection(silhouette: .unknown, sleeve: .long, neck: .unknown)
        let replace = try PhotoObservation.editPlan(.replace, category: .sleeve, selection: selection, description: original, notes: "bel çevresi 72 cm")
        XCTAssertEqual(replace.before, "Fotoğraf gözlemi · Kol: Kolsuz")
        XCTAssertEqual(replace.after, "Fotoğraf gözlemi · Kol: Uzun kollu")
        XCTAssertEqual(replace.updatedDescription, "Tarif 👗\r\nFotoğraf gözlemi · Kol: Uzun kollu\r\nSon satır")
        let remove = try PhotoObservation.editPlan(.remove, category: .sleeve, selection: PhotoObservation.Selection(), description: original, notes: "bel çevresi 72 cm")
        XCTAssertNil(remove.after)
        XCTAssertEqual(remove.updatedDescription, "Tarif 👗\r\n\r\nSon satır")
        XCTAssertThrowsError(try PhotoObservation.editPlan(.replace, category: .sleeve, selection: PhotoObservation.Selection(), description: original, notes: ""))
        XCTAssertEqual(original, "Tarif 👗\r\nFotoğraf gözlemi · Kol: Kolsuz\r\nSon satır")
    }
    func testPhotoObservationEditRejectsDuplicateMalformedOrStaleInputs() throws {
        let line = "Fotoğraf gözlemi · Kol: Kolsuz"
        let selection = PhotoObservation.Selection(silhouette: .unknown, sleeve: .long, neck: .unknown)
        for text in [line + "\n" + line, line + "\nFotoğraf gözlemi · Kol: Kısa kollu", line + "\nFotoğraf gözlemi: Kol: Kısa kollu", " " + line, "Not: " + line, "Fotoğraf gözlemi · Kol: tanımsız", "Fotoğraf gözlemi · Kol"] {
            XCTAssertThrowsError(try PhotoObservation.editPlan(.replace, category: .sleeve, selection: selection, description: text, notes: ""), text)
            XCTAssertThrowsError(try PhotoObservation.editPlan(.remove, category: .sleeve, selection: selection, description: text, notes: ""), text)
        }
        XCTAssertThrowsError(try PhotoObservation.editPlan(.replace, category: .sleeve, selection: selection, description: line, notes: line))
        let same = PhotoObservation.Selection(silhouette: .unknown, sleeve: .sleeveless, neck: .unknown)
        XCTAssertThrowsError(try PhotoObservation.editPlan(.replace, category: .sleeve, selection: same, description: line, notes: ""))
        let filler = String(repeating: "x", count: ExperimentalDesignBrief.maxTextUnits - line.utf16.count - 1)
        let full = filler + "\n" + line
        XCTAssertThrowsError(try PhotoObservation.editPlan(.replace, category: .sleeve, selection: selection, description: full, notes: ""))
        let draft = ExperimentalDesignBrief.Draft(style: .aline, description: line, bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "not", image: Data([1]), imageName: "foto.png")
        let request = PhotoObservation.Request(draft: draft, explicitSelection: true, importedPortable: false, version: 2, revision: UUID())
        let changed = ExperimentalDesignBrief.Draft(style: .aline, description: line, bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "not", image: Data([2]), imageName: "foto.png")
        XCTAssertFalse(request.matches(changed, explicitSelection: true, importedPortable: false, version: 2, revision: request.revision, activeID: request.id, referencePending: false, transition: false))
        XCTAssertFalse(request.matches(draft, explicitSelection: true, importedPortable: false, version: 2, revision: request.revision, activeID: nil, referencePending: false, transition: false))
        XCTAssertEqual(draft.description, line); XCTAssertEqual(draft.waist, "72 cm"); XCTAssertEqual(draft.image, Data([1])); XCTAssertEqual(selection.sleeve, .long)
    }
    func testPhotoEditReviewRejectsSelectionOrCategoryChangedAndRestored() throws {
        var choices = PhotoObservation.ChoiceState()
        var original = choices.selection; original.sleeve = .long
        choices.select(original)
        let note = "Fotoğraf gözlemi · Kol: Kolsuz"
        let plan = try PhotoObservation.editPlan(.replace, category: .sleeve, selection: original, description: note, notes: "")
        let revision = choices.revision
        let review = PhotoObservation.EditReview(plan: plan, revision: revision)
        XCTAssertTrue(review.matches(choices, action: .replace, confirmedSelection: original, confirmedRevision: revision))
        XCTAssertFalse(review.matches(choices, action: .replace, confirmedSelection: nil, confirmedRevision: nil))
        XCTAssertFalse(review.matches(choices, action: .replace, confirmedSelection: original, confirmedRevision: UUID()))
        choices.select(original)
        XCTAssertEqual(choices.revision, revision) // A repeated setter is not a change.
        var other = original; other.sleeve = .short
        choices.select(other); choices.select(original)
        XCTAssertEqual(choices.selection, original)
        XCTAssertNotEqual(choices.revision, revision)
        XCTAssertFalse(review.matches(choices, action: .replace, confirmedSelection: original, confirmedRevision: revision))
        let removalPlan = try PhotoObservation.editPlan(.remove, category: .sleeve, selection: original, description: note, notes: "")
        let removalRevision = choices.revision
        let removalReview = PhotoObservation.EditReview(plan: removalPlan, revision: removalRevision)
        XCTAssertTrue(removalReview.matches(choices, action: .remove, confirmedSelection: nil, confirmedRevision: nil))
        choices.selectCategory(.neck); choices.selectCategory(.sleeve)
        XCTAssertFalse(removalReview.matches(choices, action: .remove, confirmedSelection: nil, confirmedRevision: nil))
        XCTAssertEqual(choices.selection, original); XCTAssertEqual(note, "Fotoğraf gözlemi · Kol: Kolsuz")
    }
    func testPhotoObservationSnapshotRejectsStaleInputsAndPendingWork() {
        func draft(_ text: String = "Tarif", bytes: Data = Data([1]), notes: String = "", waist: String = "72 cm", style: ExperimentalDesignBrief.DressStyle = .aline) -> ExperimentalDesignBrief.Draft {
            .init(style: style, description: text, bust: "", waist: waist, hips: "", length: "", otherMeasurements: notes, image: bytes, imageName: "foto.png")
        }
        let original = draft(), revision = UUID()
        let request = PhotoObservation.Request(draft: original, explicitSelection: true, importedPortable: false, version: 2, revision: revision)
        func matches(_ value: ExperimentalDesignBrief.Draft, explicit: Bool = true, imported: Bool = false, version: Int = 2, revision: UUID? = nil, activeID: UUID? = nil, pending: Bool = false, transition: Bool = false) -> Bool {
            request.matches(value, explicitSelection: explicit, importedPortable: imported, version: version,
                            revision: revision ?? request.revision, activeID: activeID ?? request.id, referencePending: pending, transition: transition)
        }
        XCTAssertTrue(matches(original)); XCTAssertFalse(matches(draft("Değişti")))
        XCTAssertFalse(matches(draft(bytes: Data([2])))); XCTAssertFalse(matches(draft(notes: "yeni not")))
        XCTAssertFalse(matches(draft(waist: "80 cm"))); XCTAssertFalse(matches(draft(style: .wrap)))
        XCTAssertFalse(matches(original, explicit: false)); XCTAssertFalse(matches(original, imported: true))
        XCTAssertFalse(matches(original, version: 1)); XCTAssertFalse(matches(original, revision: UUID()))
        XCTAssertFalse(matches(original, activeID: UUID()))
        XCTAssertFalse(request.matches(original, explicitSelection: true, importedPortable: false, version: 2, revision: revision, activeID: nil, referencePending: false, transition: false))
        XCTAssertFalse(matches(original, pending: true)); XCTAssertFalse(matches(original, transition: true))
        XCTAssertNotEqual(request.id, PhotoObservation.Request(draft: original, explicitSelection: true, importedPortable: false, version: 2, revision: revision).id)
        XCTAssertEqual(original.image, Data([1]))
    }
    func testSyntheticJPEGAndPNGOrientationPreviewObservation() throws {
        let bundle = Bundle(for: Self.self)
        for ext in ["jpg", "png"] {
            let url = try XCTUnwrap(bundle.url(forResource: "synthetic-orientation-6", withExtension: ext))
            let bytes = try Data(contentsOf: url), original = bytes
            let source = try XCTUnwrap(CGImageSourceCreateWithData(bytes as CFData, nil))
            let properties = try XCTUnwrap(CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any])
            let orientation = properties[kCGImagePropertyOrientation] as? Int
            let image = try PhotoObservation.thumbnail(bytes)
            XCTAssertEqual(bytes, original)
            if ext == "jpg" {
                XCTAssertEqual(orientation, 6); XCTAssertEqual(image.width, 32); XCTAssertEqual(image.height, 64)
            } else {
                // PNG eXIf support varies by ImageIO version; report instead of guessing.
                let observation = "PNG eXIf orientation=\(orientation.map(String.init) ?? "unavailable"), preview=\(image.width)x\(image.height)"
                let attachment = XCTAttachment(string: observation); attachment.name = "PNG orientation observation"; attachment.lifetime = .keepAlways; add(attachment)
                XCTAssertTrue((image.width == 32 && image.height == 64) || (image.width == 64 && image.height == 32), observation)
            }
        }
    }
    func testReferenceThumbnailTicketRejectsRemovedChangedAndOlderImage() {
        let original = Data([1, 2, 3]), changed = Data([4, 5, 6])
        let first = PhotoObservation.ThumbnailTicket(bytes: original)
        let next = PhotoObservation.ThumbnailTicket(bytes: original)
        XCTAssertTrue(first.matches(activeID: first.id, currentBytes: original))
        XCTAssertFalse(first.matches(activeID: nil, currentBytes: original))
        XCTAssertFalse(first.matches(activeID: first.id, currentBytes: nil))
        XCTAssertFalse(first.matches(activeID: first.id, currentBytes: changed))
        XCTAssertFalse(first.matches(activeID: next.id, currentBytes: original))
        XCTAssertEqual(original, Data([1, 2, 3]))
    }
    func testPhotoObservationRejectsInvalidImageWithoutAlteringBytes() {
        let bytes = Data([0, 1, 2])
        XCTAssertThrowsError(try PhotoObservation.thumbnail(bytes))
        XCTAssertEqual(bytes, Data([0, 1, 2]))
    }
    func testV2ImageReviewUsesExportValidationAndPreservesBytes() throws {
        let bundle = Bundle(for: Self.self)
        let validURL = try XCTUnwrap(bundle.url(forResource: "ios-unicode-photo.tcad-request", withExtension: "json"))
        let valid = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: Data(contentsOf: validURL))
        let bytes = try XCTUnwrap(Data(base64Encoded: try XCTUnwrap(valid.reference).dataBase64))
        let original = bytes
        XCTAssertEqual(ExperimentalDesignBrief.V2ImageReview.inspect(bytes), .compatible)
        XCTAssertNoThrow(try ExperimentalDesignBrief.validateV2Image(bytes))
        XCTAssertEqual(bytes, original)
        let invalidURL = try XCTUnwrap(bundle.url(forResource: "v2-one-frame-apng", withExtension: "json"))
        let invalid = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: Data(contentsOf: invalidURL))
        let animated = try XCTUnwrap(Data(base64Encoded: try XCTUnwrap(invalid.reference).dataBase64))
        XCTAssertEqual(ExperimentalDesignBrief.V2ImageReview.inspect(animated), .incompatible)
        XCTAssertThrowsError(try ExperimentalDesignBrief.validateV2Image(animated))
        XCTAssertEqual(ExperimentalDesignBrief.V2ImageReview.inspect(Data([0, 1])), .incompatible)
    }
    func testV2ImageReviewStateKeepsResultsBoundToBytesAcrossAsyncCompletion() async {
        let a = Data([1]), b = Data([2])
        var state = ExperimentalDesignBrief.V2ImageReviewState()
        state.publish(.compatible, for: a, current: a)
        XCTAssertEqual(state.status(for: a), ExperimentalDesignBrief.V2ImageReview.compatible.explanation)
        XCTAssertEqual(state.status(for: nil), "")
        XCTAssertEqual(state.status(for: b), "V2 görsel denetimi sürüyor…")
        // The filename is intentionally not part of this pixel-data validation identity.
        let sameBytesDifferentName = Data(a)
        XCTAssertTrue(state.hasResult(for: sameBytesDifferentName))
        state.synchronize(current: sameBytesDifferentName)
        XCTAssertTrue(state.hasResult(for: a))
        state.synchronize(current: b)
        XCTAssertFalse(state.hasResult(for: a))
        let lateA = Task { () -> ExperimentalDesignBrief.V2ImageReview in await Task.yield(); return .compatible }
        state.publish(.incompatible, for: b, current: b)
        let resultA = await lateA.value
        state.publish(resultA, for: a, current: b)
        XCTAssertEqual(state.status(for: b), ExperimentalDesignBrief.V2ImageReview.incompatible.explanation)
        state.synchronize(current: nil)
        XCTAssertFalse(state.hasResult(for: b))
        state.publish(resultA, for: a, current: nil)
        XCTAssertFalse(state.hasResult(for: a))
        XCTAssertEqual(state.status(for: nil), "")
    }
    func testV1LargeReferenceRemainsUnchangedDespiteV2PreviewWarning() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "v2-large-6000x5000", withExtension: "json"))
        let fixture = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: Data(contentsOf: url))
        let bytes = try XCTUnwrap(Data(base64Encoded: try XCTUnwrap(fixture.reference).dataBase64))
        let draft = ExperimentalDesignBrief.Draft(style: .describe, description: "Fotoğraflı istek", bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "", image: bytes, imageName: "foto.png")
        XCTAssertEqual(ExperimentalDesignBrief.V2ImageReview.inspect(bytes), .incompatible)
        let saved = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: ExperimentalDesignBrief.make(draft))
        XCTAssertEqual(Data(base64Encoded: try XCTUnwrap(saved.reference).dataBase64), bytes)
        XCTAssertEqual(saved.prompt, draft.prompt)
        XCTAssertEqual(saved.measurements, draft.measurements)
    }
    func testV1WindowsImageNoticeTracksExactBytesWithoutClaimingCompatibility() {
        let first = Data([1]), second = Data([2])
        var state = ExperimentalDesignBrief.V2ImageReviewState()
        XCTAssertNil(state.v1WindowsPreviewNotice(for: nil))
        XCTAssertTrue(state.v1WindowsPreviewNotice(for: first)?.contains("henüz tamamlanmadı") == true)
        state.publish(.incompatible, for: first, current: first)
        XCTAssertTrue(state.v1WindowsPreviewNotice(for: first)?.contains("göstermeyebilir") == true)
        XCTAssertTrue(state.v1WindowsPreviewNotice(for: second)?.contains("henüz tamamlanmadı") == true)
        state.publish(.compatible, for: second, current: second)
        XCTAssertTrue(state.v1WindowsPreviewNotice(for: second)?.contains("ayrıca doğrulanmadı") == true)
        state.publish(.incompatible, for: first, current: second)
        XCTAssertTrue(state.v1WindowsPreviewNotice(for: second)?.contains("ayrıca doğrulanmadı") == true)
        XCTAssertEqual(first, Data([1])); XCTAssertEqual(second, Data([2]))
    }
    func testPhotoObservationThumbnailDoesNotChangeSourceBytes() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "ios-unicode-photo.tcad-request", withExtension: "json"))
        let doc = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: Data(contentsOf: url))
        let bytes = try XCTUnwrap(Data(base64Encoded: try XCTUnwrap(doc.reference).dataBase64))
        let original = bytes
        let thumbnail = try PhotoObservation.thumbnail(bytes)
        XCTAssertEqual(thumbnail.width, 1); XCTAssertEqual(thumbnail.height, 1)
        XCTAssertEqual(bytes, original)
    }
    func testApprovedPhotoObservationsKeepImageAndMeasurementsInBothFormats() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "ios-unicode-photo.tcad-request", withExtension: "json"))
        let source = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: Data(contentsOf: url))
        let reference = try XCTUnwrap(source.reference)
        let image = try XCTUnwrap(Data(base64Encoded: reference.dataBase64))
        let selection = PhotoObservation.Selection(silhouette: .unknown, sleeve: .short, neck: .round)
        let description = try PhotoObservation.append(selection, description: "Özgün tarif", notes: "Diğer not")
        let draft = ExperimentalDesignBrief.Draft(style: .aline, description: description, bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "Diğer not", image: image, imageName: reference.name)
        let v1 = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: makeTestRequest(prompt: draft.prompt, measurements: draft.measurements, image: image, imageName: reference.name))
        let v2 = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: ExperimentalDesignBrief.makeV2(draft))
        XCTAssertEqual(v1.measurements, draft.measurements); XCTAssertEqual(v2.measurements, draft.measurements)
        XCTAssertTrue(v1.prompt.contains(description)); XCTAssertEqual(v2.prompt, description)
        XCTAssertEqual(v1.reference?.dataBase64, image.base64EncodedString()); XCTAssertEqual(v2.reference?.dataBase64, image.base64EncodedString())
        XCTAssertEqual(draft.style, .aline); XCTAssertEqual(draft.image, image); XCTAssertEqual(draft.otherMeasurements, "Diğer not")
    }
    func testCoordinatedImageReadAndLimit() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let file = dir.appendingPathComponent("foto.png")
        let bytes = Data([137, 80, 78, 71])
        try bytes.write(to: file)
        XCTAssertEqual(try ExperimentalDesignBrief.readImage(file), bytes)
        try Data(count: ExperimentalDesignBrief.maxImageBytes + 1).write(to: file)
        XCTAssertThrowsError(try ExperimentalDesignBrief.readImage(file))
    }
    func testReferenceLoadTicketRejectsCancellationAndChangedSelection() {
        let old = ExperimentalDesignBrief.ReferenceSelection(data: Data([1]), name: "önce.png")
        let other = ExperimentalDesignBrief.ReferenceSelection(data: Data([2]), name: "sonra.png")
        let first = ExperimentalDesignBrief.ReferenceLoadTicket(original: old)
        let second = ExperimentalDesignBrief.ReferenceLoadTicket(original: old)
        XCTAssertTrue(first.matches(id: first.id, current: old))
        XCTAssertFalse(first.matches(id: nil, current: old))
        XCTAssertFalse(first.matches(id: second.id, current: old))
        XCTAssertFalse(first.matches(id: first.id, current: other))
        XCTAssertFalse(first.matches(id: first.id, current: nil))
    }
    func testClosingWaitsForReferenceDecisionAndRejectsStaleDraft() {
        func blocked(_ selecting: Bool = false, _ loading: Bool = false, _ confirmation: Bool = false, _ candidate: Bool = false) -> Bool {
            ExperimentalDesignBrief.referenceBlocksClose(selecting: selecting, loading: loading, awaitingConfirmation: confirmation, hasCandidate: candidate)
        }
        XCTAssertTrue(blocked(true)); XCTAssertTrue(blocked(false, true))
        XCTAssertTrue(blocked(false, false, true)); XCTAssertTrue(blocked(false, false, false, true))
        XCTAssertFalse(blocked()) // Explicit cancellation, completion, or failure cleared the pending state.
        let old = ExperimentalDesignBrief.ReferenceSelection(data: Data([1]), name: "eski.png")
        let ticket = ExperimentalDesignBrief.ReferenceLoadTicket(original: old)
        XCTAssertFalse(ticket.matches(id: nil, current: old)) // A cancelled load cannot install its late result.
        XCTAssertEqual(old.data, Data([1]))
        let saved = ExperimentalDesignBrief.Draft(style: .describe, description: "Elbise", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let changed = ExperimentalDesignBrief.Draft(style: .describe, description: "Elbise", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: Data([2]), imageName: "yeni.png")
        XCTAssertTrue(ExperimentalDesignBrief.canFinishDraftClose(saved: saved, current: saved, referencePending: false))
        XCTAssertFalse(ExperimentalDesignBrief.canFinishDraftClose(saved: saved, current: saved, referencePending: true))
        XCTAssertFalse(ExperimentalDesignBrief.canFinishDraftClose(saved: saved, current: changed, referencePending: false))
    }
    func testReferenceFlowBlocksNavigationAndExportUntilResolved() throws {
        let pending = ExperimentalDesignBrief.referenceDecisionPending
        XCTAssertTrue(pending(true, false, false)) // Loading or a slow provider.
        XCTAssertTrue(pending(false, true, true))  // Awaiting replacement approval.
        XCTAssertTrue(pending(false, false, true)) // Candidate still unresolved.
        XCTAssertTrue(pending(false, true, false)) // Dialog dismissal in flight.
        XCTAssertFalse(pending(false, false, false)) // Applied, cancelled, or failed.
        let original = ExperimentalDesignBrief.ReferenceSelection(data: Data([1, 2]), name: "eski.png")
        let candidate = ExperimentalDesignBrief.ReferenceSelection(data: Data([3, 4]), name: "yeni.png")
        let change = ExperimentalDesignBrief.ReferenceChange(original: original, replacement: candidate)
        // Cancel/error leave the original unchanged; an approved change is explicit.
        var current = original
        XCTAssertEqual(current, original)
        current = try XCTUnwrap(change.apply(to: current))
        XCTAssertEqual(current, candidate)
        XCTAssertThrowsError(try change.apply(to: current))
    }
    func testRequestSummaryUsesExactSnapshotAndDoesNotInferModel() {
        let form = ExperimentalDesignBrief.MeasurementForm(values: ["72"] + Array(repeating: "", count: 14), unit: "cm")
        let draft = ExperimentalDesignBrief.Draft(style: .describe, description: "Model: A kesim 👗", bust: "92 cm", waist: "", hips: "", length: "", otherMeasurements: "  ", image: Data([1]), imageName: "örnek.png", measurementForm: form)
        let review = ExperimentalDesignBrief.RequestSummary(draft)
        XCTAssertNil(review.model)
        XCTAssertEqual(review.description, "Model: A kesim 👗")
        XCTAssertEqual(review.v1Prompt, draft.prompt)
        XCTAssertEqual(review.v2Prompt, draft.description)
        XCTAssertNil(review.v2ModelID)
        XCTAssertEqual(review.measurements, "Göğüs çevresi: 92 cm\n  ")
        XCTAssertEqual(review.referenceName, "örnek.png")
        XCTAssertTrue(review.hasPendingMeasurements)
    }
    func testSummaryFlagsWhitespaceWithoutAlteringDraft() {
        let draft = ExperimentalDesignBrief.Draft(style: .describe, description: " \n", bust: "\t", waist: "72 cm", hips: "", length: "", otherMeasurements: "  ", image: nil, imageName: nil)
        let summary = ExperimentalDesignBrief.RequestSummary(draft)
        XCTAssertTrue(summary.descriptionIsWhitespace)
        XCTAssertEqual(summary.whitespaceMeasurementLabels, ["Göğüs çevresi", "Ek ölçüler veya notlar"])
        XCTAssertEqual(summary.description, " \n")
        XCTAssertEqual(summary.measurements, draft.measurements)
        XCTAssertEqual(draft.bust, "\t"); XCTAssertEqual(draft.otherMeasurements, "  ")
        XCTAssertFalse(ExperimentalDesignBrief.RequestSummary.isOnlyWhitespace(""))
        XCTAssertFalse(ExperimentalDesignBrief.RequestSummary.isOnlyWhitespace(" 72 cm "))
    }
    func testRequestSummarySeparatesSelectedModelAndExactOutput() {
        let draft = ExperimentalDesignBrief.Draft(style: .wrap, description: "İpek", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let review = ExperimentalDesignBrief.RequestSummary(draft)
        XCTAssertEqual(review.model, "Anvelop elbise")
        XCTAssertEqual(review.description, "İpek")
        XCTAssertEqual(review.v1Prompt, "Seçilen elbise modeli: Anvelop elbise\nİpek")
        XCTAssertEqual(review.v2Prompt, "İpek")
        XCTAssertEqual(review.v2ModelID, "wrap")
        XCTAssertNil(review.referenceName); XCTAssertFalse(review.hasPendingMeasurements)
    }
    func testFormatPreviewsMatchEncodedPromptsAndModelIdentity() throws {
        for (style, description) in [(ExperimentalDesignBrief.DressStyle.aline, ""), (.wrap, "  \n"), (.describe, "İpek elbise")] {
            let draft = ExperimentalDesignBrief.Draft(style: style, description: description, bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
            let summary = ExperimentalDesignBrief.RequestSummary(draft)
            let v1 = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: makeTestRequest(prompt: draft.prompt, measurements: draft.measurements, image: nil, imageName: nil))
            let v2Data = try ExperimentalDesignBrief.makeV2(draft)
            let v2 = try XCTUnwrap(JSONSerialization.jsonObject(with: v2Data) as? [String: Any])
            XCTAssertEqual(summary.v1Prompt, v1.prompt)
            XCTAssertEqual(summary.v2Prompt, v2["prompt"] as? String)
            if let id = summary.v2ModelID {
                XCTAssertEqual((v2["model"] as? [String: String])?["id"], id)
            } else { XCTAssertTrue(v2["model"] is NSNull) }
            XCTAssertEqual(summary.v2Prompt, description)
        }
    }
    func testPendingALineGuideIsReportedSeparatelyInReviewAndOverview() {
        var draft = ExperimentalDesignBrief.Draft(style: .describe, description: "", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        XCTAssertFalse(ExperimentalDesignBrief.RequestSummary(draft).hasPendingMeasurements)
        draft.aLineGuideInput = .init(values: Array(repeating: "", count: 11), unit: "cm")
        let summary = ExperimentalDesignBrief.RequestSummary(draft)
        XCTAssertTrue(summary.hasPendingALineGuide)
        XCTAssertFalse(summary.hasPendingMeasurementForm)
        XCTAssertTrue(summary.hasPendingMeasurements)
        XCTAssertTrue(ExperimentalDesignBrief.AtelierOverview(draft).hasPendingMeasurements)
        XCTAssertTrue(summary.measurements.isEmpty)
        draft.measurementForm = .init(values: ["72"] + Array(repeating: "", count: 14), unit: "cm")
        let both = ExperimentalDesignBrief.RequestSummary(draft)
        XCTAssertTrue(both.hasPendingALineGuide)
        XCTAssertTrue(both.hasPendingMeasurementForm)
    }
    func testAtelierOverviewReflectsOnlyEnteredMeasurementsAndReference() {
        let empty = ExperimentalDesignBrief.Draft(style: .describe, description: "", bust: "", waist: " ", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let emptyView = ExperimentalDesignBrief.AtelierOverview(empty)
        XCTAssertEqual(emptyView.bust, "Girilmedi"); XCTAssertEqual(emptyView.waist, "Girilmedi")
        XCTAssertEqual(emptyView.length, "Girilmedi")
        XCTAssertFalse(emptyView.hasOtherMeasurements); XCTAssertNil(emptyView.referenceName)
        let form = ExperimentalDesignBrief.MeasurementForm(values: ["72"] + Array(repeating: "", count: 14), unit: "cm")
        let entered = ExperimentalDesignBrief.Draft(style: .aline, description: "Tarif", bust: "92 cm", waist: "72 cm", hips: "", length: "110 cm", otherMeasurements: "", image: Data([1]), imageName: "örnek.png", measurementForm: form)
        let overview = ExperimentalDesignBrief.AtelierOverview(entered)
        XCTAssertEqual(overview.bust, "92 cm"); XCTAssertEqual(overview.waist, "72 cm")
        XCTAssertEqual(overview.hips, "Girilmedi"); XCTAssertEqual(overview.length, "110 cm")
        XCTAssertFalse(overview.hasOtherMeasurements)
        let withNotes = ExperimentalDesignBrief.Draft(style: .describe, description: "", bust: "", waist: "", hips: "", length: "", otherMeasurements: "Kol 60 cm", image: nil, imageName: nil)
        XCTAssertTrue(ExperimentalDesignBrief.AtelierOverview(withNotes).hasOtherMeasurements)
        XCTAssertTrue(overview.hasPendingMeasurements); XCTAssertEqual(overview.referenceName, "örnek.png")
        XCTAssertEqual(entered.bust, "92 cm")
    }
    func testCompletePortableRequestPreservesAmbiguityWhitespaceAndImage() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "ios-complete-request.tcad-request", withExtension: "json"))
        let bytes = try Data(contentsOf: url)
        let original = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: bytes)
        let draft = try ExperimentalDesignBrief.portableDraft(bytes)
        XCTAssertEqual(draft.prompt, original.prompt)
        XCTAssertEqual(draft.measurements, original.measurements)
        XCTAssertEqual(draft.image, Data(base64Encoded: try XCTUnwrap(original.reference).dataBase64))
        XCTAssertEqual(draft.imageName, original.reference?.name)
        let output = try makeTestRequest(prompt: draft.prompt, measurements: draft.measurements, image: draft.image, imageName: draft.imageName)
        let reopened = try ExperimentalDesignBrief.portableDraft(output)
        XCTAssertEqual(reopened.prompt, draft.prompt)
        XCTAssertEqual(reopened.measurements, draft.measurements)
        XCTAssertEqual(reopened.image, draft.image)
        let entered = ExperimentalDesignBrief.measurements(bust: "92 cm", waist: "  ", hips: "", length: "", other: "bel çevresi yaklaşık 72 cm olsun; kalça çevresi 98 cm olmasın")
        XCTAssertEqual(entered, original.measurements)
    }
    func testModelSuggestionUsesOnlyUnambiguousExplicitModelNames() {
        func draft(_ text: String, notes: String = "") -> ExperimentalDesignBrief.Draft {
            .init(style: .describe, description: text, bust: "", waist: "", hips: "", length: "", otherMeasurements: notes, image: nil, imageName: nil)
        }
        let examples: [(String, ExperimentalDesignBrief.DressStyle)] = [
            ("DÜZ ELBİSE", .straight), ("A kesim elbise istiyorum", .aline), ("Anvelop elbise olsun", .wrap),
            ("GÖMLEK ELBİSE", .shirt), ("Balenli korpus", .corset), ("Minimal kılıf", .sheath)
        ]
        for (text, expected) in examples { XCTAssertEqual(ExperimentalDesignBrief.suggestedModel(draft(text)), expected) }
        XCTAssertEqual(ExperimentalDesignBrief.suggestedModel(draft("", notes: "anvelop elbise")), .wrap)
        XCTAssertEqual(ExperimentalDesignBrief.suggestedModel(draft("A kesim elbise istiyorum, uzun kollu", notes: "göğüs çevresi 92 cm")), .aline)
        XCTAssertEqual(ExperimentalDesignBrief.suggestedModel(draft("Uzun kollu, GÖMLEK ELBİSE istiyorum")), .shirt)
        XCTAssertEqual(ExperimentalDesignBrief.suggestedModel(draft("İpek", notes: "Anvelop elbise olsun; bel çevresi 72 cm")), .wrap)
        for text in ["anvelop elbise değil", "düz elbise mi?", "düz elbise mi", "‘anvelop elbise’", "anvelop elbise veya düz elbise", "anvelop elbise düz elbise", "anvelop elbise anvelop elbise", "anvelop elbiseli", "düz", "anvelop elbise istemiyorum", "anvelop elbise istemem", "belki anvelop elbise", "eğer anvelop elbise olursa", "anvelop elbise. düz olmasın", "anvelop elbise yerine gömlek", "anvelop elbiseden vazgeçtim", "anvelop elbise yasak"] {
            XCTAssertNil(ExperimentalDesignBrief.suggestedModel(draft(text)), text)
        }
        XCTAssertNil(ExperimentalDesignBrief.suggestedModel(draft("anvelop elbise", notes: "düz elbise değil")))
        for text in ["Anvelop elbise tercih etmiyorum", "Anvelop elbise düşünmüyorum", "Anvelop elbise uygun mudur",
                     "Anvelop elbise istiyorum ama tercih etmiyorum", "Anvelop elbise istiyorum, düşünmüyorum",
                     "Anvelop elbise istiyorum, uygun mudur", "Anvelop elbise istiyorum, olmayacak",
                     "Anvelop elbise istiyorum, olabilir", "Anvelop elbise istiyorum, başka model seçersem",
                     "Anvelop elbise istiyorum, gömlek elbise olsun", "Anvelop elbise istiyorum, ‘uzun kollu’"] {
            XCTAssertNil(ExperimentalDesignBrief.suggestedModel(draft(text)), text)
        }
        XCTAssertNil(ExperimentalDesignBrief.suggestedModel(draft("Anvelop elbise", notes: "tercih etmiyorum")))
    }
    func testModelSuggestionSnapshotAndPortableExports() throws {
        let image = try XCTUnwrap(Data(base64Encoded: "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/aO0AAAAASUVORK5CYII="))
        let draft = ExperimentalDesignBrief.Draft(style: .describe, description: "Anvelop elbise istiyorum", bust: "92 cm", waist: "", hips: "", length: "", otherMeasurements: "", image: image, imageName: "örnek.png")
        let revision = UUID()
        let proposal = ExperimentalDesignBrief.ModelSuggestion(draft: draft, suggested: .wrap, explicitSelection: false, importedPortable: true, revision: revision)
        XCTAssertTrue(proposal.matches(draft, explicitSelection: false, importedPortable: true, revision: revision))
        XCTAssertFalse(proposal.matches(draft, explicitSelection: true, importedPortable: true, revision: revision))
        XCTAssertFalse(proposal.matches(draft, explicitSelection: false, importedPortable: false, revision: revision))
        XCTAssertFalse(proposal.matches(draft, explicitSelection: false, importedPortable: true, revision: UUID()))
        let changed = ExperimentalDesignBrief.Draft(style: .describe, description: draft.description, bust: "93 cm", waist: "", hips: "", length: "", otherMeasurements: "", image: image, imageName: "örnek.png")
        XCTAssertFalse(proposal.matches(changed, explicitSelection: false, importedPortable: true, revision: revision))
        let changedPhoto = ExperimentalDesignBrief.Draft(style: .describe, description: draft.description, bust: draft.bust, waist: "", hips: "", length: "", otherMeasurements: "", image: Data([1]), imageName: "örnek.png")
        XCTAssertFalse(proposal.matches(changedPhoto, explicitSelection: false, importedPortable: true, revision: revision))
        let changedStyle = ExperimentalDesignBrief.Draft(style: .aline, description: draft.description, bust: draft.bust, waist: "", hips: "", length: "", otherMeasurements: "", image: image, imageName: "örnek.png")
        XCTAssertFalse(proposal.matches(changedStyle, explicitSelection: false, importedPortable: true, revision: revision))
        let selected = ExperimentalDesignBrief.Draft(style: proposal.suggested, description: draft.description, bust: draft.bust, waist: "", hips: "", length: "", otherMeasurements: "", image: image, imageName: draft.imageName)
        let v1 = try ExperimentalDesignBrief.portableDraft(makeTestRequest(prompt: selected.prompt, measurements: selected.measurements, image: selected.image, imageName: selected.imageName))
        XCTAssertEqual(v1.style, .describe); XCTAssertEqual(v1.description, selected.prompt); XCTAssertEqual(v1.image, image)
        let v2 = try ExperimentalDesignBrief.portableDraft(ExperimentalDesignBrief.makeV2(selected))
        XCTAssertEqual(v2.style, .wrap); XCTAssertEqual(v2.description, draft.description); XCTAssertEqual(v2.measurements, selected.measurements); XCTAssertEqual(v2.image, image)
    }
    func testModelSuggestionRequiresConsistentParsedDetailsAndMeasurements() {
        func draft(_ description: String, notes: String = "", waist: String = "") -> ExperimentalDesignBrief.Draft {
            .init(style: .describe, description: description, bust: "", waist: waist, hips: "", length: "", otherMeasurements: notes, image: nil, imageName: nil)
        }
        XCTAssertNil(ExperimentalDesignBrief.suggestedModel(draft("Anvelop elbise istiyorum, uzun kısa kollu")))
        XCTAssertNil(ExperimentalDesignBrief.suggestedModel(draft("Anvelop elbise istiyorum, uzun kollu, kısa kollu")))
        XCTAssertNil(ExperimentalDesignBrief.suggestedModel(draft("Anvelop elbise istiyorum, yakalı; yakasız")))
        XCTAssertNil(ExperimentalDesignBrief.suggestedModel(draft("Anvelop elbise istiyorum, ipek; keten")))
        XCTAssertNil(ExperimentalDesignBrief.suggestedModel(draft("Anvelop elbise istiyorum, bel çevresi 72 cm; bel çevresi 90 cm")))
        XCTAssertNil(ExperimentalDesignBrief.suggestedModel(draft("Anvelop elbise istiyorum", notes: "bel çevresi 72 cm", waist: "90 cm")))
        XCTAssertEqual(ExperimentalDesignBrief.suggestedModel(draft("Anvelop elbise istiyorum, uzun kollu; uzun kollu", notes: "bel çevresi 72 cm; bel çevresi 720 mm", waist: "72,0 cm")), .wrap)
        XCTAssertNil(ExperimentalDesignBrief.suggestedModel(draft("Anvelop elbise istiyorum", notes: "bel 72 cm")))
        XCTAssertNil(ExperimentalDesignBrief.suggestedModel(draft("Anvelop elbise istiyorum", notes: "göğüs 92 cm")))
        XCTAssertNil(ExperimentalDesignBrief.suggestedModel(draft("Anvelop elbise istiyorum", notes: "kalça 98 cm")))
        XCTAssertEqual(ExperimentalDesignBrief.suggestedModel(draft("Anvelop elbise istiyorum", notes: "bel çevresi 72 cm; göğüs çevresi 92 cm; kalça çevresi 98 cm; elbise boyu 110 cm")), .wrap)
        XCTAssertEqual(ExperimentalDesignBrief.suggestedModel(draft("A kesim elbise istiyorum, uzun kollu", notes: "göğüs çevresi 92 cm")), .aline)
        for notes in ["bel çevresi 0 cm", "bel çevresi 1001 cm", "bel çevresi 72", "bel çevresi 72 in", "bel çevresi 72 cm bel çevresi 72 cm", "bel çevresi 72,123456789 cm", "uzun kolluluk"] {
            XCTAssertNil(ExperimentalDesignBrief.suggestedModel(draft("Anvelop elbise istiyorum", notes: notes)), notes)
        }
    }
    func testCompleteTurkishDescriptionOffersReviewedModelAndMeasurements() throws {
        let original = "A kesim elbise istiyorum uzun kollu bel çevresi 72 cm kalça çevresi 100 cm"
        func draft(_ text: String, style: ExperimentalDesignBrief.DressStyle = .describe, waist: String = "", notes: String = "") -> ExperimentalDesignBrief.Draft {
            .init(style: style, description: text, bust: "", waist: waist, hips: "", length: "", otherMeasurements: notes, image: nil, imageName: nil)
        }
        let initial = draft(original)
        XCTAssertTrue(MeasurementMessageParser.containsExplicitMeasurement(original))
        XCTAssertFalse(MeasurementMessageParser.containsExplicitMeasurement("A kesim elbise istiyorum uzun kollu"))
        XCTAssertEqual(ExperimentalDesignBrief.suggestedModel(initial), .aline)
        let proposal = try XCTUnwrap(ExperimentalDesignBrief.descriptionMeasurements(initial))
        XCTAssertEqual(proposal.source, original)
        XCTAssertEqual(proposal.values, ["bel çevresi": "72 cm", "kalça çevresi": "100 cm"])
        let revision = UUID()
        let snapshot = ExperimentalDesignBrief.DescriptionMeasurementSuggestion(draft: initial, proposal: proposal,
            explicitSelection: false, importedPortable: false, version: 1, revision: revision)
        XCTAssertTrue(snapshot.matches(initial, explicitSelection: false, importedPortable: false, version: 1, revision: revision))
        XCTAssertFalse(snapshot.matches(draft(original + " "), explicitSelection: false, importedPortable: false, version: 1, revision: revision))
        XCTAssertFalse(snapshot.matches(initial, explicitSelection: true, importedPortable: false, version: 1, revision: revision))
        XCTAssertFalse(snapshot.matches(initial, explicitSelection: false, importedPortable: true, version: 1, revision: revision))
        XCTAssertFalse(snapshot.matches(initial, explicitSelection: false, importedPortable: false, version: 2, revision: revision))
        XCTAssertFalse(snapshot.matches(initial, explicitSelection: false, importedPortable: false, version: 1, revision: UUID()))
        XCTAssertNil(ExperimentalDesignBrief.descriptionMeasurements(draft(original, style: .wrap)))
        XCTAssertNil(ExperimentalDesignBrief.descriptionMeasurements(draft(original, waist: "72 cm")))
        for text in [original + " bilinmeyen", original + "?", "eğer " + original,
                     "A kesim elbise istiyorum uzun kollu bel çevresi 72 cm bel çevresi 90 cm",
                     "A kesim elbise istiyorum uzun kollu bel çevresi 1,000 mm",
                     "A kesim elbise istiyorum uzun kollu bel çevresi 72 cm olmasın",
                     "A kesim elbise istiyorum uzun kolluluk bel çevresi 72 cm"] {
            XCTAssertNil(ExperimentalDesignBrief.descriptionMeasurements(draft(text)), text)
        }
        let approved = ExperimentalDesignBrief.Draft(style: .aline, description: original, bust: "", waist: try XCTUnwrap(proposal.values["bel çevresi"]), hips: try XCTUnwrap(proposal.values["kalça çevresi"]), length: "", otherMeasurements: "", image: nil, imageName: nil)
        let v1 = try ExperimentalDesignBrief.portableDraft(makeTestRequest(prompt: approved.prompt, measurements: approved.measurements, image: nil, imageName: nil))
        let v2 = try ExperimentalDesignBrief.portableDraft(ExperimentalDesignBrief.makeV2(approved))
        XCTAssertTrue(v1.description.contains(original)); XCTAssertEqual(v1.measurements, approved.measurements)
        XCTAssertEqual(v2.description, original); XCTAssertEqual(v2.measurements, approved.measurements); XCTAssertEqual(v2.style, .aline)
    }
    func testPortableV2ModelRoundTripAndRejectsAmbiguity() throws {
        let image = try XCTUnwrap(Data(base64Encoded: "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/aO0AAAAASUVORK5CYII="))
        let original = ExperimentalDesignBrief.Draft(style: .aline, description: "İpek 👗", bust: "92 cm", waist: "", hips: "", length: "", otherMeasurements: "bel çevresi 72 cm olmasın", image: image, imageName: "örnek.png")
        let bytes = try ExperimentalDesignBrief.makeV2(original)
        let reopened = try ExperimentalDesignBrief.portableDraft(bytes)
        XCTAssertEqual(reopened.style, .aline)
        XCTAssertEqual(reopened.description, original.description)
        XCTAssertEqual(reopened.measurements, original.measurements)
        XCTAssertEqual(reopened.image, image)
        XCTAssertEqual(reopened.imageName, original.imageName)
        let text = try XCTUnwrap(String(data: bytes, encoding: .utf8))
        XCTAssertThrowsError(try ExperimentalDesignBrief.portableDraft(Data(text.replacingOccurrences(of: "\"aline\"", with: "\"unknown\"").utf8)))
        XCTAssertThrowsError(try ExperimentalDesignBrief.portableDraft(Data(text.replacingOccurrences(of: "\"id\":\"aline\"", with: "\"id\":\"aline\",\"id\":\"wrap\"").utf8)))
        let conflict = ExperimentalDesignBrief.Draft(style: .aline, description: "Model: düz", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let conflictingBytes = try ExperimentalDesignBrief.makeV2(conflict)
        XCTAssertEqual(try ExperimentalDesignBrief.portableDraft(conflictingBytes).description, conflict.description)
        let conflictingImport = #"{"format":"tekstilcad.design-request","version":2,"prompt":"Model: düz","measurements":"","reference":null,"model":{"id":"aline"}}"#
        let conflicting = try ExperimentalDesignBrief.portableDraft(Data(conflictingImport.utf8))
        XCTAssertEqual(conflicting.style, .aline)
        XCTAssertEqual(conflicting.description, "Model: düz")
        let unknownField = #"{"format":"tekstilcad.design-request","version":2,"prompt":"İpek","measurements":"","reference":null,"model":{"id":"aline","extra":true}}"#
        XCTAssertThrowsError(try ExperimentalDesignBrief.portableDraft(Data(unknownField.utf8)))
        let v1 = try makeTestRequest(prompt: original.prompt, measurements: original.measurements, image: image, imageName: original.imageName)
        let v1Reopened = try ExperimentalDesignBrief.portableDraft(v1)
        XCTAssertEqual(v1Reopened.style, .describe)
        XCTAssertEqual(v1Reopened.prompt, original.prompt)
        XCTAssertEqual(v1Reopened.image, image)
        let damaged = Data([137,80,78,71,13,10,26,10])
        let damagedDraft = ExperimentalDesignBrief.Draft(style: .aline, description: "örnek", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: damaged, imageName: "broken.png")
        XCTAssertThrowsError(try ExperimentalDesignBrief.makeV2(damagedDraft))
    }
    func testV2RetainsFreeTextPotentialModelConflictWithoutClaimingDetection() throws {
        let draft = ExperimentalDesignBrief.Draft(style: .aline, description: "Düz elbise istiyorum", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        XCTAssertFalse(ExperimentalDesignBrief.hasConflictingModelLabels(style: draft.style, description: draft.description))
        let recovered = try ExperimentalDesignBrief.portableDraft(ExperimentalDesignBrief.makeV2(draft))
        XCTAssertEqual(recovered.style, .aline)
        XCTAssertEqual(recovered.description, "Düz elbise istiyorum")
    }
    func testV2PhotoOrientationBoundary() {
        XCTAssertTrue(ExperimentalDesignBrief.v2ImageOrientationAllowed(nil))
        for value in 1...8 { XCTAssertTrue(ExperimentalDesignBrief.v2ImageOrientationAllowed(NSNumber(value: value))) }
        for value in [0, 9, -1] { XCTAssertFalse(ExperimentalDesignBrief.v2ImageOrientationAllowed(NSNumber(value: value))) }
        XCTAssertFalse(ExperimentalDesignBrief.v2ImageOrientationAllowed(NSNumber(value: true)))
        XCTAssertFalse(ExperimentalDesignBrief.v2ImageOrientationAllowed(NSNumber(value: false)))
        XCTAssertFalse(ExperimentalDesignBrief.v2ImageOrientationAllowed(NSNumber(value: 1.0)))
        XCTAssertFalse(ExperimentalDesignBrief.v2ImageOrientationAllowed(NSNumber(value: 1.5)))
        XCTAssertFalse(ExperimentalDesignBrief.v2ImageOrientationAllowed("9"))
    }
    func testV2PNGAnimationChunkBoundaries() throws {
        let bundle = Bundle(for: Self.self)
        let url = try XCTUnwrap(bundle.url(forResource: "v2-one-frame-apng", withExtension: "json"))
        let doc = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: Data(contentsOf: url))
        let animated = try XCTUnwrap(Data(base64Encoded: try XCTUnwrap(doc.reference).dataBase64))
        XCTAssertTrue(try ExperimentalDesignBrief.v2PNGHasAnimationControl(animated))
        let plain = try XCTUnwrap(Data(base64Encoded: "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/aO0AAAAASUVORK5CYII="))
        XCTAssertFalse(try ExperimentalDesignBrief.v2PNGHasAnimationControl(plain))
        XCTAssertThrowsError(try ExperimentalDesignBrief.v2PNGHasAnimationControl(Data(plain.prefix(15))))
        XCTAssertThrowsError(try ExperimentalDesignBrief.v2PNGHasAnimationControl(plain + Data([0])))
        let animationAfterEnd = Data([0,0,0,8,97,99,84,76,0,0,0,1,0,0,0,0,0,0,0,0])
        XCTAssertThrowsError(try ExperimentalDesignBrief.v2PNGHasAnimationControl(plain + animationAfterEnd))
    }
    func testV2PhotoGeometryBoundaries() {
        XCTAssertTrue(ExperimentalDesignBrief.v2ImageGeometryAllowed(width: 6000, height: 4000, frames: 1))
        XCTAssertTrue(ExperimentalDesignBrief.v2ImageGeometryAllowed(width: 12000, height: 2000, frames: 1))
        XCTAssertFalse(ExperimentalDesignBrief.v2ImageGeometryAllowed(width: 6000, height: 5000, frames: 1))
        XCTAssertFalse(ExperimentalDesignBrief.v2ImageGeometryAllowed(width: 12001, height: 1, frames: 1))
        XCTAssertFalse(ExperimentalDesignBrief.v2ImageGeometryAllowed(width: 1, height: 12001, frames: 1))
        XCTAssertFalse(ExperimentalDesignBrief.v2ImageGeometryAllowed(width: 6000, height: 4000, frames: 2))
        XCTAssertFalse(ExperimentalDesignBrief.v2ImageGeometryAllowed(width: 1, height: 0, frames: 1))
        XCTAssertFalse(ExperimentalDesignBrief.v2ImageGeometryAllowed(width: Int.max, height: 1, frames: 1))
    }
    func testSharedV2Fixtures() throws {
        let bundle = Bundle(for: Self.self)
        func fixture(_ name: String) throws -> Data {
            try Data(contentsOf: XCTUnwrap(bundle.url(forResource: name, withExtension: "json")))
        }
        for name in ["v2-invalid-signature-png", "v2-invalid-signature-jpeg", "v2-large-6000x5000", "v2-two-frame-apng", "v2-one-frame-apng", "v2-jpeg-orientation-9"] {
            XCTAssertThrowsError(try ExperimentalDesignBrief.portableDraft(fixture(name)), name)
        }
        for name in ["v2-empty-model", "v2-model-measurements-only", "v2-model-matching", "v2-model-conflicting"] {
            let raw = try fixture(name)
            let parsed = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: raw)
            let draft = try ExperimentalDesignBrief.portableDraft(raw)
            XCTAssertEqual(draft.style, .aline)
            XCTAssertEqual(draft.description, parsed.prompt)
            let restored = try ExperimentalDesignBrief.portableDraft(ExperimentalDesignBrief.makeV2(draft))
            XCTAssertEqual(restored.description, parsed.prompt)
            XCTAssertEqual(restored.style, .aline)
        }
    }
    func testEmptyAndMeasurementOnlyRequestsReportMissingContentInBothFormats() {
        for waist in ["", "72 cm"] {
            let draft = ExperimentalDesignBrief.Draft(style: .describe, description: "", bust: "", waist: waist, hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
            for make in [ExperimentalDesignBrief.make, ExperimentalDesignBrief.makeV2] {
                XCTAssertThrowsError(try make(draft)) { error in
                    guard let issue = error as? ExperimentalDesignBrief.Error, case .missingContent = issue else {
                        return XCTFail("Expected missingContent, got \(error)")
                    }
                }
            }
            XCTAssertEqual(draft.waist, waist)
        }
    }
    func testMeasurementComparisonOnlyReportsUnambiguousOverlappingValues() {
        typealias Parser = MeasurementMessageParser
        let input = Parser.ComparisonInput(message: "bel çevresi 75 cm, kalça çevresi 980 mm", fields: ["bel çevresi": "72 cm", "kalça çevresi": "98 cm", "elbise boyu": "110 cm"])
        XCTAssertEqual(Parser.compare(input), .reviewed(compared: 2, differences: [.init(label: "bel çevresi", fieldValue: "72 cm", messageValue: "75 cm")], unrecognizedFields: []))
        XCTAssertEqual(input.fields["bel çevresi"], "72 cm")
        let unknown = Parser.ComparisonInput(message: "bel çevresi 75 cm", fields: ["bel çevresi": "yaklaşık 72 cm"])
        XCTAssertEqual(Parser.compare(unknown), .reviewed(compared: 0, differences: [], unrecognizedFields: ["bel çevresi"]))
        let ambiguous = Parser.ComparisonInput(message: "bel çevresi 75 cm", fields: ["bel çevresi": "1,000 mm"])
        XCTAssertEqual(Parser.compare(ambiguous), .reviewed(compared: 0, differences: [], unrecognizedFields: ["bel çevresi"]))
        for space in ["\u{00A0}", "\u{202F}", "\t"] {
            let unicode = Parser.ComparisonInput(message: "bel çevresi 75 cm", fields: ["bel çevresi": "\(space)72\(space)cm\(space)"])
            XCTAssertEqual(Parser.compare(unicode), .reviewed(compared: 1, differences: [.init(label: "bel çevresi", fieldValue: "\(space)72\(space)cm\(space)", messageValue: "75 cm")], unrecognizedFields: []))
            let ambiguousUnicode = Parser.ComparisonInput(message: "bel çevresi 75 cm", fields: ["bel çevresi": "1,000\(space)mm"])
            XCTAssertEqual(Parser.compare(ambiguousUnicode), .reviewed(compared: 0, differences: [], unrecognizedFields: ["bel çevresi"]))
        }
        let multiline = Parser.ComparisonInput(message: "bel çevresi 75 cm", fields: ["bel çevresi": "72\ncm"])
        XCTAssertEqual(Parser.compare(multiline), .reviewed(compared: 0, differences: [], unrecognizedFields: ["bel çevresi"]))
        let noOverlap = Parser.ComparisonInput(message: "bel çevresi 75 cm", fields: ["kalça çevresi": "98 cm"])
        XCTAssertEqual(Parser.compare(noOverlap), .reviewed(compared: 0, differences: [], unrecognizedFields: []))
        XCTAssertEqual(Parser.compare(.init(message: "  ", fields: [:])), .noMessage)
        for message in ["bel çevresi 75 cm değil", "bel çevresi 75 cm?", "bel çevresi 75 cm, bel çevresi 72 cm", "bel çevresi 1,000 mm", "eğer uygunsa bel çevresi 75 cm"] {
            XCTAssertEqual(Parser.compare(.init(message: message, fields: ["bel çevresi": "72 cm"])), .unrecognizedMessage, message)
        }
    }
    func testReviewNavigationReturnsToEntryStepAndResetsAfterLeavingReview() {
        var navigation = ExperimentalDesignBrief.ReviewNavigation()
        XCTAssertEqual(navigation.previousStep(from: 3), 2)
        navigation.enterFromMeasurements(); navigation.didChangeStep(to: 3)
        XCTAssertEqual(navigation.previousStep(from: 3), 1)
        navigation.didChangeStep(to: 1)
        XCTAssertEqual(navigation.previousStep(from: 1), 0)
        navigation.didChangeStep(to: 2); navigation.didChangeStep(to: 3)
        XCTAssertEqual(navigation.previousStep(from: 3), 2)
        navigation.enterFromMeasurements(); navigation.didChangeStep(to: 3)
        navigation.didChangeStep(to: 0); navigation.didChangeStep(to: 3)
        XCTAssertEqual(navigation.previousStep(from: 3), 2)
    }
    func testUnitlessBasicMeasurementsAreFlaggedWithoutChangingInput() {
        let draft = ExperimentalDesignBrief.Draft(style: .aline, description: "", bust: "92", waist: " 72,5 ", hips: "98 cm", length: "110 mm", otherMeasurements: "kol 60", image: nil, imageName: nil)
        let summary = ExperimentalDesignBrief.RequestSummary(draft)
        XCTAssertEqual(summary.unitlessNumericMeasurementLabels, ["Göğüs çevresi", "Bel çevresi"])
        XCTAssertEqual(draft.bust, "92"); XCTAssertEqual(draft.waist, " 72,5 ")
        XCTAssertTrue(summary.measurements.contains("92")); XCTAssertTrue(summary.measurements.contains("72,5"))
        for text in ["", " ", "72 cm", "72 mm", "yaklaşık 72", "72?", "72\ncm"] {
            XCTAssertFalse(ExperimentalDesignBrief.RequestSummary.isUnitlessNumber(text), text)
        }
        for text in ["72", " 72,5 ", "1,000", "\u{00A0}98\u{00A0}"] {
            XCTAssertTrue(ExperimentalDesignBrief.RequestSummary.isUnitlessNumber(text), text)
        }
    }
    func testV2EmptyPromptAndModelLines() throws {
        let empty = ExperimentalDesignBrief.Draft(style: .aline, description: "", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let emptyResult = try ExperimentalDesignBrief.portableDraft(ExperimentalDesignBrief.makeV2(empty))
        XCTAssertEqual(emptyResult.style, .aline); XCTAssertEqual(emptyResult.description, "")
        let whitespace = ExperimentalDesignBrief.Draft(style: .wrap, description: "  \n", bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let whitespaceResult = try ExperimentalDesignBrief.portableDraft(ExperimentalDesignBrief.makeV2(whitespace))
        XCTAssertEqual(whitespaceResult.description, whitespace.description); XCTAssertEqual(whitespaceResult.measurements, whitespace.measurements)
        let noModel = ExperimentalDesignBrief.Draft(style: .describe, description: "", bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        XCTAssertThrowsError(try ExperimentalDesignBrief.makeV2(noModel))
        XCTAssertThrowsError(try makeTestRequest(prompt: "", measurements: "Bel çevresi: 72 cm", image: nil, imageName: nil))
        for text in ["Seçilen elbise modeli: A kesim elbise", "Seçilen elbise modeli: Düz elbise"] {
            let draft = ExperimentalDesignBrief.Draft(style: .aline, description: text, bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
            let reopened = try ExperimentalDesignBrief.portableDraft(ExperimentalDesignBrief.makeV2(draft))
            XCTAssertEqual(reopened.style, .aline)
            XCTAssertEqual(reopened.description, text)
        }
    }
    func testV2ModelAndMeasurementsWithoutDescriptionRoundTripAndV1Conversion() throws {
        let source = try Data(contentsOf: XCTUnwrap(Bundle(for: Self.self).url(forResource: "v2-model-measurements-only", withExtension: "json")))
        let draft = try ExperimentalDesignBrief.portableDraft(source)
        XCTAssertEqual(draft.style, .aline); XCTAssertEqual(draft.description, "")
        XCTAssertEqual(draft.measurements, "Bel çevresi: 72 cm\nKalça çevresi: 100 cm")
        let v2 = try ExperimentalDesignBrief.portableDraft(ExperimentalDesignBrief.makeV2(draft))
        XCTAssertEqual(v2.style, draft.style); XCTAssertEqual(v2.description, draft.description); XCTAssertEqual(v2.measurements, draft.measurements)
        let v1 = try ExperimentalDesignBrief.portableDraft(makeTestRequest(prompt: draft.prompt, measurements: draft.measurements, image: nil, imageName: nil))
        XCTAssertEqual(v1.style, .describe); XCTAssertEqual(v1.description, draft.prompt); XCTAssertEqual(v1.measurements, draft.measurements)
        for model in ["null", "{\"id\":\"unknown\"}", "{\"id\":\"\"}"] {
            let raw = "{\"format\":\"tekstilcad.design-request\",\"version\":2,\"prompt\":\"\",\"measurements\":\"bel çevresi 72 cm\",\"reference\":null,\"model\":\(model)}"
            XCTAssertThrowsError(try ExperimentalDesignBrief.portableDraft(Data(raw.utf8)))
        }
    }
    func testV2ToV1RejectsOverLimitWithoutTruncatingEmoji() throws {
        let text = String(repeating: "👗", count: 8192)
        let original = ExperimentalDesignBrief.Draft(style: .aline, description: text, bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let v2 = try ExperimentalDesignBrief.makeV2(original)
        let reopened = try ExperimentalDesignBrief.portableDraft(v2)
        XCTAssertEqual(reopened.description, text)
        XCTAssertThrowsError(try makeTestRequest(prompt: reopened.prompt, measurements: reopened.measurements, image: nil, imageName: nil))
        XCTAssertEqual(try ExperimentalDesignBrief.portableDraft(v2).description, text)
    }
    func testPortableV1AndV2AttemptsKeepSnapshotWhenCancelledOrSuperseded() throws {
        let draft = ExperimentalDesignBrief.Draft(style: .aline, description: "İpek 👗", bust: "92 cm", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let v1 = try makeTestRequest(prompt: draft.prompt, measurements: draft.measurements, image: nil, imageName: nil)
        let v2 = try ExperimentalDesignBrief.makeV2(draft)
        let first = ExperimentalDesignBrief.ExportAttempt(draft: draft, bytes: v1)
        let second = ExperimentalDesignBrief.ExportAttempt(draft: draft, bytes: v2)
        var presentation = ExperimentalDesignBrief.ExportPresentation()
        presentation.begin(first.id)
        presentation.setPresented(false, for: first.id) // cancellation changes presentation only
        XCTAssertFalse(presentation.isPresented(for: first.id))
        XCTAssertEqual(first.bytes, v1)
        XCTAssertEqual(first.draft, draft)
        XCTAssertTrue(first.matchesCurrentDraft(draft))
        let changed = ExperimentalDesignBrief.Draft(style: .aline, description: "Değişen tarif", bust: "92 cm", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        XCTAssertFalse(first.matchesCurrentDraft(changed))
        presentation.begin(second.id)
        presentation.setPresented(false, for: first.id) // late callback cannot dismiss v2
        XCTAssertTrue(presentation.isPresented(for: second.id))
        XCTAssertEqual(try ExperimentalDesignBrief.portableDraft(second.bytes).style, .aline)
        XCTAssertEqual(try ExperimentalDesignBrief.portableDraft(first.bytes).style, .describe)
        XCTAssertEqual(first.draft, second.draft)
    }
    func testMeasurementMessagePreservedAsUserAuthoredText() throws {
        let message = "Göğüs çevresi 92 cm, bel çevresi 72 cm.\nKol ölçüsünü bilmiyorum 👗"
        let draft = ExperimentalDesignBrief.Draft(style: .aline, description: "Elbise", bust: "", waist: "", hips: "", length: "", otherMeasurements: message, image: nil, imageName: nil)
        XCTAssertEqual(draft.measurements, message)
        let recovered = try ExperimentalDesignBrief.DraftStore.recoveryDraft(ExperimentalDesignBrief.DraftStore.recoveryData(draft))
        XCTAssertEqual(recovered.otherMeasurements, message)
        let portable = try makeTestRequest(prompt: draft.prompt, measurements: draft.measurements, image: nil, imageName: nil)
        XCTAssertEqual(try ExperimentalDesignBrief.portableDraft(portable).measurements, message)
    }
    func testMeasurementMessageParserAcceptsOnlyExplicitLabelsAndUnits() throws {
        let source = "Göğüs çevresi 92,5 cm, bel çevresi: 720 mm; kalça çevresi 98 cm\nelbise boyu 110 cm"
        let proposal = try MeasurementMessageParser.parse(source, existing: [:])
        XCTAssertEqual(proposal.source, source)
        XCTAssertEqual(proposal.values["göğüs çevresi"], "92,5 cm")
        XCTAssertEqual(proposal.values["bel çevresi"], "720 mm")
        XCTAssertEqual(proposal.values["kalça çevresi"], "98 cm")
        XCTAssertEqual(proposal.values["elbise boyu"], "110 cm")
        XCTAssertTrue(proposal.preview.contains("bel çevresi: 720 mm"))
        let partial = try MeasurementMessageParser.parse("göğüs çevresi 92 cm", existing: [:])
        XCTAssertTrue(partial.preview.contains("Mesajda belirtilmeyenler: bel çevresi"))
    }
    func testMeasurementMessageOlsunPreservesTextAndRejectsAmbiguity() throws {
        let source = "Göğüs çevresi 92,5 cm olsun; bel çevresi 720 mm olsun"
        let result = try MeasurementMessageParser.parse(source, existing: [:])
        XCTAssertEqual(result.source, source)
        XCTAssertEqual(result.values["göğüs çevresi"], "92,5 cm")
        XCTAssertEqual(result.values["bel çevresi"], "720 mm")
        XCTAssertTrue(MeasurementMessageParser.mayNeedDesktopReview("göğüs çevresi 92 cm olsun ve bel çevresi 72 cm"))
        XCTAssertThrowsError(try MeasurementMessageParser.parse("bel çevresi 72 cm olsun; bel çevresi 73 cm olsun", existing: [:]))
        XCTAssertThrowsError(try MeasurementMessageParser.parse("bel çevresi 72 cm olsun; kol uzun olsun", existing: [:]))
        XCTAssertThrowsError(try MeasurementMessageParser.parse("bel çevresi 72 cm olsun", existing: ["bel çevresi": "70 cm"]))
        for ambiguous in ["bel çevresi yaklaşık 72 cm olsun", "bel çevresi 72 cm olmasın", "bel çevresi 72 cm olsun değil"] {
            XCTAssertThrowsError(try MeasurementMessageParser.parse(ambiguous, existing: [:]))
            let draft = ExperimentalDesignBrief.Draft(style: .describe, description: "", bust: "", waist: "", hips: "", length: "", otherMeasurements: ambiguous, image: nil, imageName: nil)
            XCTAssertEqual(draft.measurements, ambiguous)
        }
        let raw = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "ios-message-olsun.tcad-request", withExtension: "json"))
        let data = try Data(contentsOf: raw)
        let draft = try ExperimentalDesignBrief.portableDraft(data)
        XCTAssertEqual(draft.otherMeasurements, source)
        let serialized = try makeTestRequest(prompt: draft.prompt, measurements: draft.measurements, image: nil, imageName: nil)
        XCTAssertEqual(try ExperimentalDesignBrief.portableDraft(serialized).otherMeasurements, source)
    }
    func testMeasurementMessageParserRejectsConflictsWithoutMutatingInputs() {
        let existing = ["bel çevresi": "70 cm"]
        let cases = ["bel çevresi 72 cm, bel çevresi 73 cm", "bel çevresi 72", "bel 72 cm", "bel çevresi 0 cm", "bel çevresi 1001 cm", "bel çevresi 72 cm ve kol uzun", "bel çevresi 72 cm, kalça çevresi 100 cm"]
        for message in cases { XCTAssertThrowsError(try MeasurementMessageParser.parse(message, existing: existing)) }
        XCTAssertEqual(existing["bel çevresi"], "70 cm")
        XCTAssertThrowsError(try MeasurementMessageParser.parse("göğüs çevresi 92 cm, göğüs çevresi 92 cm", existing: [:]))
        XCTAssertThrowsError(try MeasurementMessageParser.parse("göğüs çevresi 92 cm, bel 72 cm", existing: [:]))
        XCTAssertThrowsError(try MeasurementMessageParser.parse("", existing: [:]))
    }
    func testMeasurementMessageChecksDescriptionAndProtectsWholeSnapshot() throws {
        func draft(_ description: String, waist: String = "", notes: String = "bel çevresi 72 cm") -> ExperimentalDesignBrief.Draft {
            .init(style: .describe, description: description, bust: "", waist: waist, hips: "", length: "", otherMeasurements: notes, image: nil, imageName: nil)
        }
        let conflict = draft("bel çevresi 90 cm")
        XCTAssertThrowsError(try ExperimentalDesignBrief.measurementMessageProposal(conflict)) { error in
            XCTAssertEqual(error as? MeasurementMessageParser.Failure, .descriptionConflict)
        }
        XCTAssertEqual(conflict.description, "bel çevresi 90 cm")
        XCTAssertEqual(conflict.otherMeasurements, "bel çevresi 72 cm")
        let same = draft("bel çevresi 720 mm")
        XCTAssertEqual(try ExperimentalDesignBrief.measurementMessageProposal(same).values["bel çevresi"], "72 cm")
        for description in ["A kesim elbise istiyorum bel çevresi 90 cm", "eğer bel çevresi 90 cm ise", "bel çevresi 90 cm?", "bel çevresi 90 cm olmasın", "bel 90 cm"] {
            XCTAssertThrowsError(try ExperimentalDesignBrief.measurementMessageProposal(draft(description)), description)
        }
        for description in ["Bu ölçüler yanlış", "ölçüleri kullanmayın", "değerleri dikkate alma", "Bunlar yanlış", "Bunlar geçersiz", "Bunları dikkate almayın", "Bunları kullanmayın", "Hepsini kullanmayın", "Tümü eski", "Onlardan vazgeçtim", "bu 90 cm olmasın", "bu mu?"] {
            let original = draft(description)
            XCTAssertThrowsError(try ExperimentalDesignBrief.measurementMessageProposal(original), description)
            XCTAssertEqual(original.description, description)
            XCTAssertEqual(original.otherMeasurements, "bel çevresi 72 cm")
        }
        for description in ["Belim 90 cm", "BELİM 90 cm", "Kalçam 100 cm", "Göğsüm 92 cm", "Beliniz 90 cm"] {
            let original = draft(description)
            XCTAssertThrowsError(try ExperimentalDesignBrief.measurementMessageProposal(original), description)
            XCTAssertEqual(original.description, description)
            XCTAssertEqual(original.otherMeasurements, "bel çevresi 72 cm")
        }
        for description in ["İpek olmasın", "Keten değil, pamuklu olsun", "Bu kumaş olmasın", "Bu kumaşları kullanmayın", "Değerli kumaş", "Bu model olmasın", "Hepsiburada desenli kumaş", "Belirgin dikiş", "Belimsi desen", "Kalçamsı desen"] {
            XCTAssertEqual(try ExperimentalDesignBrief.measurementMessageProposal(draft(description)).values["bel çevresi"], "72 cm", description)
        }
        XCTAssertThrowsError(try ExperimentalDesignBrief.measurementMessageProposal(draft("ELBİSE BOYU 120 cm", notes: "elbise boyu 110 cm")))
        XCTAssertThrowsError(try ExperimentalDesignBrief.measurementMessageProposal(draft("ELBISE BOYU 120 cm", notes: "elbise boyu 110 cm")))
        let normal = draft("Mavi elbise")
        let proposal = try ExperimentalDesignBrief.measurementMessageProposal(normal)
        let revision = UUID()
        let snapshot = ExperimentalDesignBrief.MessageMeasurementSuggestion(draft: normal, proposal: proposal,
            explicitSelection: false, importedPortable: false, version: 1, revision: revision)
        XCTAssertTrue(snapshot.matches(normal, explicitSelection: false, importedPortable: false, version: 1, revision: revision))
        XCTAssertFalse(snapshot.matches(draft("bel çevresi 90 cm"), explicitSelection: false, importedPortable: false, version: 1, revision: revision))
        XCTAssertFalse(snapshot.matches(draft("Belim 90 cm"), explicitSelection: false, importedPortable: false, version: 1, revision: revision))
        XCTAssertFalse(snapshot.matches(normal, explicitSelection: true, importedPortable: false, version: 1, revision: revision))
        XCTAssertFalse(snapshot.matches(normal, explicitSelection: false, importedPortable: false, version: 2, revision: revision))
        XCTAssertFalse(snapshot.matches(normal, explicitSelection: false, importedPortable: false, version: 1, revision: UUID()))
        XCTAssertThrowsError(try ExperimentalDesignBrief.measurementMessageProposal(draft("Mavi elbise", waist: "70 cm")))
        let data = try makeTestRequest(prompt: conflict.prompt, measurements: conflict.measurements, image: nil, imageName: nil)
        let saved = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: data)
        XCTAssertEqual(saved.prompt, conflict.description)
        XCTAssertEqual(saved.measurements, conflict.measurements)
    }
    func testAmbiguousNumbersAndQuestionsNeverProduceMeasurementProposals() throws {
        for number in ["1,000", "1.000", "72,000", "72.000"] {
            let message = "göğüs çevresi 92 cm; bel çevresi \(number) mm"
            XCTAssertThrowsError(try MeasurementMessageParser.parse(message, existing: [:])) { error in
                XCTAssertEqual(error as? MeasurementMessageParser.Failure, .ambiguousNumber)
            }
            let draft = ExperimentalDesignBrief.Draft(style: .describe, description: "Anvelop elbise istiyorum", bust: "", waist: "", hips: "", length: "", otherMeasurements: message, image: nil, imageName: nil)
            XCTAssertEqual(draft.measurements, message)
            XCTAssertNil(ExperimentalDesignBrief.suggestedModel(draft))
            let data = try makeTestRequest(prompt: draft.prompt, measurements: draft.measurements, image: nil, imageName: nil)
            XCTAssertEqual(try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: data).measurements, message)
        }
        for message in ["bel çevresi 72 cm?", "bel çevresi 72 cm mı?", "bel çevresi 72 cm olmasın", "bel çevresi 72 cm, olmasın", "eğer bel çevresi 72 cm", "eğer bel çevresi 72 cm ise"] {
            XCTAssertThrowsError(try MeasurementMessageParser.parse(message, existing: [:]))
            XCTAssertTrue(MeasurementMessageParser.mayNeedDesktopReview(message), message)
            let draft = ExperimentalDesignBrief.Draft(style: .describe, description: "Anvelop elbise istiyorum", bust: "", waist: "", hips: "", length: "", otherMeasurements: message, image: nil, imageName: nil)
            XCTAssertEqual(draft.measurements, message)
            XCTAssertNil(ExperimentalDesignBrief.suggestedModel(draft))
            let data = try makeTestRequest(prompt: draft.prompt, measurements: draft.measurements, image: nil, imageName: nil)
            XCTAssertEqual(try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: data).measurements, message)
        }
        XCTAssertEqual(try MeasurementMessageParser.parse("bel çevresi 72,5 cm", existing: [:]).values["bel çevresi"], "72,5 cm")
        XCTAssertEqual(try MeasurementMessageParser.parse("bel çevresi 1000 mm", existing: [:]).values["bel çevresi"], "1000 mm")
        XCTAssertTrue(MeasurementMessageParser.mayNeedDesktopReview("bel çevresi 72 cm?"))
        XCTAssertTrue(MeasurementMessageParser.mayNeedDesktopReview("1,000 mm"))
        XCTAssertTrue(MeasurementMessageParser.mayNeedDesktopReview("1.000 cm"))
        XCTAssertFalse(MeasurementMessageParser.mayNeedDesktopReview("72,5 cm"))
        XCTAssertTrue(MeasurementMessageParser.mayNeedDesktopReview("bel çevresi 72 mm değil"))
        XCTAssertTrue(MeasurementMessageParser.mayNeedDesktopReview("bel çevresi 72 cm olursa"))
        XCTAssertFalse(MeasurementMessageParser.mayNeedDesktopReview("bel çevresi 72 cm olsun"))
        XCTAssertFalse(MeasurementMessageParser.mayNeedDesktopReview("bel çevresi 72 cm, kalça çevresi 98 cm"))
        XCTAssertTrue(MeasurementMessageParser.mayNeedDesktopReview("eğer kumaş ipekse; bel çevresi 72 cm"))
        XCTAssertTrue(MeasurementMessageParser.mayNeedDesktopReview("şayet kumaş ipekse; bel çevresi 72 cm"))
        XCTAssertTrue(MeasurementMessageParser.hasConditionalMeasurementRisk(["ŞAYET kumaş ipekse", "bel çevresi: 72 cm"]))
        XCTAssertFalse(MeasurementMessageParser.hasConditionalMeasurementRisk(["müşayet kumaş", "bel çevresi: 72 cm"]))
        XCTAssertTrue(MeasurementMessageParser.hasConditionalMeasurementRisk(["eğer kumaş ipekse", "bel çevresi: 72 cm"]))
        XCTAssertFalse(MeasurementMessageParser.hasConditionalMeasurementRisk(["eğer kumaş ipekse", "uzun kollu"]))
        XCTAssertFalse(MeasurementMessageParser.hasConditionalMeasurementRisk(["değer", "bel çevresi: 72 cm"]))
        let split = ExperimentalDesignBrief.Draft(style: .describe, description: "eğer kumaş ipekse", bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        XCTAssertTrue(MeasurementMessageParser.hasConditionalMeasurementRisk([split.description, split.measurements]))
        let encoded = try makeTestRequest(prompt: split.prompt, measurements: split.measurements, image: nil, imageName: nil)
        let restored = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: encoded)
        XCTAssertEqual(restored.prompt, "eğer kumaş ipekse")
        XCTAssertEqual(restored.measurements, "Bel çevresi: 72 cm")
    }
    func testDesktopWarningSnapshotRejectsChangedDraftOrExportVersion() {
        func draft(_ notes: String) -> ExperimentalDesignBrief.Draft {
            .init(style: .describe, description: "şayet kumaş ipekse", bust: "", waist: "72 cm", hips: "", length: "", otherMeasurements: notes, image: nil, imageName: nil)
        }
        let original = draft(""), snapshot = ExperimentalDesignBrief.DesktopWarningSnapshot(draft: draft(""), version: 1)
        XCTAssertTrue(snapshot.matches(original, version: 1))
        XCTAssertFalse(snapshot.matches(draft("bel çevresi 90 cm"), version: 1))
        XCTAssertFalse(snapshot.matches(original, version: 2))
    }
    func testMessageLabelsRejectUnknownTurkishICaseWithoutPartialProposal() throws {
        XCTAssertEqual(MeasurementMessageParser.canonicalLabel("BEL ÇEVRESİ"), "bel çevresi")
        XCTAssertNil(MeasurementMessageParser.canonicalLabel("BEL ÇEVRESI"))
        XCTAssertNil(MeasurementMessageParser.canonicalLabel("bilinmeyen"))
        XCTAssertThrowsError(try MeasurementMessageParser.parse("göğüs çevresi 92 cm, BEL ÇEVRESI 72 cm", existing: [:]))
        let bad = MeasurementMessageParser.Proposal(source: "x", values: ["bel çevresı": "72 cm"])
        XCTAssertFalse(bad.isApplicable)
        XCTAssertFalse(MeasurementMessageParser.Proposal(source: "x", values: [:]).isApplicable)
        XCTAssertTrue(try MeasurementMessageParser.parse("bel çevresi 72 cm", existing: [:]).isApplicable)
    }
    func testMessageLineEndingsPreserveOriginalAndParseAll() throws {
        for lineBreak in ["\n", "\r\n", "\r"] {
            let original = "göğüs çevresi 92 cm" + lineBreak + "bel çevresi 72 cm"
            let proposal = try MeasurementMessageParser.parse(original, existing: [:])
            XCTAssertEqual(proposal.source, original)
            XCTAssertEqual(proposal.values["göğüs çevresi"], "92 cm")
            XCTAssertEqual(proposal.values["bel çevresi"], "72 cm")
        }
        XCTAssertThrowsError(try MeasurementMessageParser.parse("göğüs çevresi 92 cm\r\nbilinmeyen 72 cm", existing: [:]))
    }
    func testMeasurementMessageSupportsAndAndSentencePunctuation() throws {
        let examples = [
            "göğüs çevresi 92 cm ve bel çevresi 72 cm",
            "göğüs çevresi 92 cm. bel çevresi 72 cm.",
            "göğüs çevresi 92 cm, bel çevresi 72 cm.",
            "göğüs çevresi 92 cm.\r\nbel çevresi 72 cm."
        ]
        for source in examples {
            let result = try MeasurementMessageParser.parse(source, existing: [:])
            XCTAssertEqual(result.source, source)
            XCTAssertEqual(result.values["göğüs çevresi"], "92 cm")
            XCTAssertEqual(result.values["bel çevresi"], "72 cm")
        }
        let decimal = try MeasurementMessageParser.parse("göğüs çevresi 92.5 cm ve bel çevresi 72,5 cm.", existing: [:])
        XCTAssertEqual(decimal.values["göğüs çevresi"], "92.5 cm")
        XCTAssertEqual(decimal.values["bel çevresi"], "72,5 cm")
    }
    func testMeasurementMessageNeverReturnsPartialForNaturalLanguageFailures() {
        for source in ["göğüs çevresi 92 cm ve bel çevresi 72", "göğüs çevresi 92 cm ve kol 40 cm", "göğüs çevresi 92 cm ve göğüs çevresi 93 cm", "göğüs çevresi 92 cm ve ", "göğüs çevresi 92 cm. not ekle", "göğüs çevresi 92 cm, bel çevresi 0 cm"] {
            XCTAssertThrowsError(try MeasurementMessageParser.parse(source, existing: [:]))
        }
        XCTAssertThrowsError(try MeasurementMessageParser.parse("göğüs çevresi 92 cm ve bel çevresi 72 cm", existing: ["bel çevresi": "71 cm"]))
    }
    func testDesktopMeasurementRiskWarnsWithoutChangingPortableData() throws {
        XCTAssertTrue(MeasurementMessageParser.mayNeedDesktopReview("göğüs çevresi 92 cm ve bel çevresi 72 cm"))
        XCTAssertTrue(MeasurementMessageParser.mayNeedDesktopReview("göğüs çevresi 92 cm. bel çevresi 72 cm."))
        XCTAssertFalse(MeasurementMessageParser.mayNeedDesktopReview("göğüs çevresi 92.5 cm, bel çevresi 72 cm"))
        XCTAssertFalse(MeasurementMessageParser.mayNeedDesktopReview("göğüs çevresi 92 cm.\n"))
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "portable-desktop-and-v1", withExtension: "json"))
        let raw = try Data(contentsOf: url)
        let original = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: raw)
        let imported = try ExperimentalDesignBrief.portableDraft(raw)
        XCTAssertTrue(MeasurementMessageParser.mayNeedDesktopReview(imported.otherMeasurements))
        XCTAssertEqual(imported.measurements, original.measurements)
        let bytes = try makeTestRequest(prompt: imported.prompt, measurements: imported.measurements, image: nil, imageName: nil)
        let reopened = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: bytes)
        XCTAssertEqual(reopened.prompt, original.prompt); XCTAssertEqual(reopened.measurements, original.measurements)
    }
    func testSharedPortableFixturesRoundTripAndRejectInvalid() throws {
        func fixture(_ name: String) throws -> Data {
            let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: name + ".tcad-request", withExtension: "json"))
            return try Data(contentsOf: url)
        }
        for name in ["ios-unicode-photo", "ios-message-ve", "ios-whitespace-notes"] {
            let raw = try fixture(name)
            let expected = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: raw)
            let imported = try ExperimentalDesignBrief.portableDraft(raw)
            XCTAssertEqual(imported.prompt, expected.prompt)
            XCTAssertEqual(imported.measurements, expected.measurements)
            if let reference = expected.reference {
                XCTAssertEqual(imported.image, Data(base64Encoded: reference.dataBase64))
                XCTAssertEqual(imported.imageName, reference.name)
            } else { XCTAssertNil(imported.image) }
            let bytes = try makeTestRequest(prompt: imported.prompt, measurements: imported.measurements, image: imported.image, imageName: imported.imageName)
            let roundTrip = try JSONDecoder().decode(ExperimentalDesignBrief.Document.self, from: bytes)
            XCTAssertEqual(roundTrip.prompt, expected.prompt); XCTAssertEqual(roundTrip.measurements, expected.measurements)
            XCTAssertEqual(roundTrip.reference?.dataBase64, expected.reference?.dataBase64)
        }
        for name in ["ios-invalid-duplicate", "ios-unsupported-version"] {
            XCTAssertThrowsError(try ExperimentalDesignBrief.portableDraft(fixture(name)))
        }
    }
    func testOversizedInputDoesNotReplacePreviousRecord() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("draft.json")
        let valid = ExperimentalDesignBrief.Draft(style: .describe, description: "Önceki", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        let revision = try ExperimentalDesignBrief.DraftStore.save(valid, to: url, expected: nil)
        let oversized = ExperimentalDesignBrief.Draft(style: .describe, description: String(repeating: "x", count: 16_385), bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: nil, imageName: nil)
        XCTAssertThrowsError(try ExperimentalDesignBrief.DraftStore.save(oversized, to: url, expected: revision))
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.load(url)?.draft, valid)
        _ = try ExperimentalDesignBrief.DraftStore.save(valid, to: url, expected: revision)
        XCTAssertEqual(try ExperimentalDesignBrief.DraftStore.load(url)?.draft, valid)
    }
}
#endif
