#if DEBUG
import Foundation
import ImageIO

/// A user-authored request for manual review, not a generated pattern.
enum ExperimentalDesignBrief {
    enum Error: Swift.Error { case missingContent, invalidText, invalidImage }
    static let maxTextUnits = 16_384
    static let maxImageBytes = 8 * 1024 * 1024
    struct Image: Codable {
        let name: String
        let mediaType: String
        let dataBase64: String
    }
    struct Document: Codable {
        let format: String
        let version: Int
        let prompt: String
        let measurements: String
        let reference: Image?
        enum CodingKeys: String, CodingKey { case format, version, prompt, measurements, reference }
        func encode(to encoder: Encoder) throws {
            var box = encoder.container(keyedBy: CodingKeys.self)
            try box.encode(format, forKey: .format); try box.encode(version, forKey: .version)
            try box.encode(prompt, forKey: .prompt); try box.encode(measurements, forKey: .measurements)
            if let reference { try box.encode(reference, forKey: .reference) }
            else { try box.encodeNil(forKey: .reference) }
        }
    }
    enum DressStyle: String, CaseIterable, Identifiable {
        case describe = "Tarif edeceğim"
        case straight = "Düz elbise"
        case aline = "A kesim elbise"
        case wrap = "Anvelop elbise"
        case shirt = "Gömlek elbise"
        var id: String { rawValue }
    }

    struct Draft: Equatable {
        let style: DressStyle
        let description: String
        let bust: String
        let waist: String
        let hips: String
        let length: String
        let otherMeasurements: String
        let image: Data?
        let imageName: String?

        var hasInput: Bool {
            style != .describe || !description.isEmpty || !bust.isEmpty || !waist.isEmpty ||
                !hips.isEmpty || !length.isEmpty || !otherMeasurements.isEmpty || image != nil
        }
        func requiresDiscardConfirmation(saved: Draft?) -> Bool { saved.map { self != $0 } ?? hasInput }
        var prompt: String { ExperimentalDesignBrief.prompt(style: style, description: description) }
        var measurements: String {
            ExperimentalDesignBrief.measurements(bust: bust, waist: waist, hips: hips, length: length, other: otherMeasurements)
        }
    }
    /// Selection describes an intended style; no pattern geometry is implied.
    static func prompt(style: DressStyle, description: String) -> String {
        guard style != .describe else { return description }
        return "Seçilen elbise modeli: \(style.rawValue)\n" + description
    }

    /// Preserve exactly what the user entered, including units and unknowns.
    static func measurements(bust: String, waist: String, hips: String, length: String, other: String) -> String {
        let items = [("Göğüs çevresi", bust), ("Bel çevresi", waist), ("Kalça çevresi", hips), ("Elbise boyu", length)]
        var lines = items.compactMap { label, value -> String? in
            value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : "\(label): \(value)"
        }
        if !other.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { lines.append(other) }
        return lines.joined(separator: "\n")
    }
    static func make(prompt: String, measurements: String, image: Data?, imageName: String?) throws -> Data {
        guard prompt.utf16.count <= maxTextUnits, measurements.utf16.count <= maxTextUnits else { throw Error.invalidText }
        guard !prompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || image != nil else { throw Error.missingContent }
        let reference: Image?
        if let image {
            guard !image.isEmpty, image.count <= maxImageBytes,
                  let name = imageName, !name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
                  name.utf16.count <= 255, name != ".", name != "..",
                  !name.contains("/"), !name.contains("\\"), !name.contains(":"),
                  let source = CGImageSourceCreateWithData(image as CFData, nil),
                  CGImageSourceGetCount(source) > 0, let type = CGImageSourceGetType(source) as String? else {
                throw Error.invalidImage
            }
            let mediaType: String
            switch type {
            case "public.png": mediaType = "image/png"
            case "public.jpeg": mediaType = "image/jpeg"
            default: throw Error.invalidImage
            }
            reference = Image(name: name, mediaType: mediaType, dataBase64: image.base64EncodedString())
        } else { reference = nil }
        let document = Document(format: "tekstilcad.design-request", version: 1, prompt: prompt,
                                measurements: measurements, reference: reference)
        let encoded = try JSONEncoder().encode(document)
        guard encoded.count <= 12 * 1024 * 1024 else { throw Error.invalidText }
        return encoded
    }

    static func readImage(_ url: URL) throws -> Data {
        let grant = url.startAccessingSecurityScopedResource()
        defer { if grant { url.stopAccessingSecurityScopedResource() } }
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }
        var bytes = Data()
        while let part = try handle.read(upToCount: 64 * 1024), !part.isEmpty {
            guard bytes.count <= maxImageBytes - part.count else { throw Error.invalidImage }
            bytes.append(part)
        }
        return bytes
    }
}
#endif
