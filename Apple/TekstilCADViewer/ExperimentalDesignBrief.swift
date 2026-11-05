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
    enum DressStyle: String, CaseIterable, Identifiable, Codable {
        case describe = "Tarif edeceğim"
        case straight = "Düz elbise"
        case aline = "A kesim elbise"
        case wrap = "Anvelop elbise"
        case shirt = "Gömlek elbise"
        case corset = "Balenli korpus"
        case sheath = "Minimal kılıf"
        var id: String { rawValue }
    }

    struct MeasurementForm: Equatable, Codable {
        let values: [String]
        let unit: String
        var hasValues: Bool { values.contains { !$0.isEmpty } }
        static func pending(values: [String], unit: String) -> MeasurementForm? {
            let form = MeasurementForm(values: values, unit: unit)
            return form.hasValues ? form : nil
        }
    }

    struct Draft: Equatable, Codable {
        let style: DressStyle
        let description: String
        let bust: String
        let waist: String
        let hips: String
        let length: String
        let otherMeasurements: String
        let image: Data?
        let imageName: String?
        var measurementForm: MeasurementForm? = nil

        var hasInput: Bool {
            style != .describe || !description.isEmpty || !bust.isEmpty || !waist.isEmpty ||
                !hips.isEmpty || !length.isEmpty || !otherMeasurements.isEmpty || image != nil || measurementForm?.hasValues == true
        }
        func requiresDiscardConfirmation(saved: Draft?) -> Bool { saved.map { self != $0 } ?? hasInput }
        var prompt: String { ExperimentalDesignBrief.prompt(style: style, description: description) }
        var measurements: String {
            ExperimentalDesignBrief.measurements(bust: bust, waist: waist, hips: hips, length: length, other: otherMeasurements)
        }
    }
    /// Narrow exact-name matching with conservative ambiguity guards; this is
    /// a suggestion for review, not a natural-language interpreter.
    static func suggestedModel(_ draft: Draft) -> DressStyle? {
        guard draft.description.utf16.count <= maxTextUnits, draft.otherMeasurements.utf16.count <= maxTextUnits else { return nil }
        let text = (draft.description + "\n" + draft.otherMeasurements)
            .precomposedStringWithCanonicalMapping.lowercased(with: Locale(identifier: "tr_TR"))
        // Refuse quotes, questions, alternatives, negations and conditional
        // wording anywhere in the input, including the other text field.
        guard text.rangeOfCharacter(from: CharacterSet(charactersIn: "?？\"'‘’“”«»`")) == nil else { return nil }
        let boundary = #"(?<![\p{L}\p{N}_])(?:değil\p{L}*|iste(?:mi|me)\p{L}*|olma\p{L}*|olsa\p{L}*|olursa|eğer|şayet|ise|veya|yahut|ya\s+da|yoksa|ama|fakat|ancak|yerine|hariç|dışında|yok\p{L}*|yasak\p{L}*|vazgeç\p{L}*|acaba|belki|hangi|nasıl|neden|(?:mi|mı|mu|mü)(?:dir|ydi|ydı|ydü|ydu)?)(?![\p{L}\p{N}_])"#
        guard text.range(of: boundary, options: .regularExpression) == nil else { return nil }
        let names: [(String, DressStyle)] = [("düz elbise", .straight), ("a kesim elbise", .aline),
            ("anvelop elbise", .wrap), ("gömlek elbise", .shirt), ("balenli korpus", .corset), ("minimal kılıf", .sheath)]
        var result: DressStyle?
        var resultRange: NSRange?
        var resultName: String?
        for (name, style) in names {
            let pattern = #"(?<![\p{L}\p{N}_])"# + NSRegularExpression.escapedPattern(for: name) + #"(?![\p{L}\p{N}_])"#
            guard let expression = try? NSRegularExpression(pattern: pattern) else { return nil }
            let matches = expression.matches(in: text, range: NSRange(text.startIndex..., in: text))
            let count = matches.count
            if count > 1 || (count == 1 && result != nil) { return nil }
            if count == 1 { result = style; resultRange = matches[0].range; resultName = name }
        }
        guard let result, let match = resultRange, let name = resultName else { return nil }
        let description = draft.description.precomposedStringWithCanonicalMapping.lowercased(with: Locale(identifier: "tr_TR")).trimmingCharacters(in: .whitespacesAndNewlines)
        let notes = draft.otherMeasurements.precomposedStringWithCanonicalMapping.lowercased(with: Locale(identifier: "tr_TR")).trimmingCharacters(in: .whitespacesAndNewlines)
        let standalone = description == name || notes == name
        let source = text as NSString
        let suffix = source.substring(from: NSMaxRange(match))
        let intentPattern = #"\A[ \t]+(?:istiyorum|olsun)(?![\p{L}\p{N}_])"#
        let intent = try? NSRegularExpression(pattern: intentPattern)
        let intentMatch = intent?.firstMatch(in: suffix, range: NSRange(suffix.startIndex..., in: suffix))
        guard standalone || intentMatch != nil else { return nil }
        let intentLength = standalone ? 0 : (intentMatch?.range.length ?? 0)
        let remainder = source.substring(to: match.location) + " " + source.substring(from: NSMaxRange(match) + intentLength)
        // Unknown remaining words are refused rather than guessed. This small
        // vocabulary permits common details and measurement notes only.
        let safeRemainder = #"\A(?:\s+|[,.;:]+|[0-9]+(?:[.,][0-9]+)?|(?<![\p{L}\p{N}_])(?:uzun|kısa|kollu|kol|kolsuz|ipek|pamuklu|keten|midi|sade|yakalı|yakasız|göğüs|bel|kalça|çevresi|elbise|boyu|cm|mm)(?![\p{L}\p{N}_]))*\z"#
        guard remainder.range(of: safeRemainder, options: .regularExpression) != nil else { return nil }
        return result
    }
    struct ModelSuggestion: Equatable {
        let draft: Draft
        let suggested: DressStyle
        let explicitSelection: Bool
        let importedPortable: Bool
        let revision: UUID
        func matches(_ current: Draft, explicitSelection: Bool, importedPortable: Bool, revision: UUID) -> Bool {
            draft == current && self.explicitSelection == explicitSelection && self.importedPortable == importedPortable && self.revision == revision
        }
    }
    /// Values shown for review are derived directly from the snapshot being exported.
    struct RequestSummary {
        let model: String?
        let description: String
        let exportedPrompt: String
        let measurements: String
        let referenceName: String?
        let hasPendingMeasurements: Bool
        let descriptionIsWhitespace: Bool
        let whitespaceMeasurementLabels: [String]
        static func isOnlyWhitespace(_ value: String) -> Bool {
            !value.isEmpty && value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        }
        init(_ draft: Draft) {
            model = draft.style == .describe ? nil : draft.style.rawValue
            description = draft.description
            exportedPrompt = draft.prompt
            measurements = draft.measurements
            referenceName = draft.image == nil ? nil : draft.imageName
            hasPendingMeasurements = draft.measurementForm?.hasValues == true
            descriptionIsWhitespace = Self.isOnlyWhitespace(draft.description)
            whitespaceMeasurementLabels = [("Göğüs çevresi", draft.bust), ("Bel çevresi", draft.waist), ("Kalça çevresi", draft.hips), ("Elbise boyu", draft.length), ("Ek ölçüler veya notlar", draft.otherMeasurements)].compactMap { label, value in Self.isOnlyWhitespace(value) ? label : nil }
        }
    }
    /// Short atelier overview; values remain user-authored and are never inferred.
    struct AtelierOverview {
        let bust: String
        let waist: String
        let hips: String
        let hasOtherMeasurements: Bool
        let hasPendingMeasurements: Bool
        let referenceName: String?
        static func display(_ raw: String) -> String {
            raw.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? "Girilmedi" : raw
        }
        init(_ draft: Draft) {
            bust = Self.display(draft.bust); waist = Self.display(draft.waist); hips = Self.display(draft.hips)
            hasOtherMeasurements = !draft.length.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || !draft.otherMeasurements.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            hasPendingMeasurements = draft.measurementForm?.hasValues == true
            referenceName = draft.image == nil ? nil : draft.imageName
        }
    }
    /// A single private snapshot. A corrupt or unknown record is never replaced.
    enum DraftStore {
        static let maxBytes = 12 * 1024 * 1024
        static let recoveryMaxBytes = 32 * 1024 * 1024
        static let recoveryMaxTextUnits = 1_000_000
        struct Envelope: Codable { let version: Int; let draft: Draft; let revision: UUID? }
        enum StoreError: Swift.Error { case invalid, tooLarge }
        static func validate(_ draft: Draft, textLimit: Int = maxTextUnits) throws {
            let strings = [draft.description, draft.bust, draft.waist, draft.hips, draft.length, draft.otherMeasurements]
            guard strings.allSatisfy({ $0.utf16.count <= textLimit }),
                  (draft.image == nil) == (draft.imageName == nil) else { throw StoreError.invalid }
            if let form = draft.measurementForm {
                guard form.values.count == 15, (form.unit == "cm" || form.unit == "mm"),
                      form.values.allSatisfy({ $0.utf16.count <= textLimit }) else { throw StoreError.invalid }
            }
            if let image = draft.image {
                _ = try ExperimentalDesignBrief.make(prompt: "görsel", measurements: "", image: image, imageName: draft.imageName)
            }
        }
        // Portable recovery copy; unlike a production request, empty and measurement-only drafts are allowed.
        static func recoveryData(_ draft: Draft) throws -> Data {
            try validate(draft, textLimit: recoveryMaxTextUnits)
            let bytes = try JSONEncoder().encode(Envelope(version: draft.measurementForm == nil ? 1 : 2, draft: draft, revision: UUID()))
            guard bytes.count <= recoveryMaxBytes else { throw StoreError.tooLarge }
            return bytes
        }
        static func recoveryDraft(_ bytes: Data) throws -> Draft {
            guard bytes.count <= recoveryMaxBytes else { throw StoreError.tooLarge }
            let record = try JSONDecoder().decode(Envelope.self, from: bytes)
            guard (record.version == 1 && record.draft.measurementForm == nil) || (record.version == 2 && record.draft.measurementForm != nil) else { throw StoreError.invalid }
            try validate(record.draft, textLimit: recoveryMaxTextUnits)
            return record.draft
        }
        static func readRecovery(_ url: URL) throws -> Draft {
            let grant = url.startAccessingSecurityScopedResource()
            defer { if grant { url.stopAccessingSecurityScopedResource() } }
            var coordinationError: NSError?
            var result: Result<Draft, Swift.Error>?
            NSFileCoordinator().coordinate(readingItemAt: url, options: [], error: &coordinationError) { path in
                result = Result {
                    let handle = try FileHandle(forReadingFrom: path)
                    defer { try? handle.close() }
                    var bytes = Data()
                    while let part = try handle.read(upToCount: 64 * 1024), !part.isEmpty {
                        guard bytes.count <= recoveryMaxBytes - part.count else { throw StoreError.tooLarge }
                        bytes.append(part)
                    }
                    return try recoveryDraft(bytes)
                }
            }
            if let coordinationError { throw coordinationError }
            guard let result else { throw StoreError.invalid }
            return try result.get()
        }
        struct Snapshot { let draft: Draft; let revision: Data }
        enum Conflict: Swift.Error { case changed }
        private static func readUncoordinated(_ url: URL) throws -> Snapshot? {
            guard FileManager.default.fileExists(atPath: url.path) else { return nil }
            let handle = try FileHandle(forReadingFrom: url)
            defer { try? handle.close() }
            var bytes = Data()
            while let part = try handle.read(upToCount: 64 * 1024), !part.isEmpty {
                guard bytes.count <= maxBytes - part.count else { throw StoreError.tooLarge }
                bytes.append(part)
            }
            let record = try JSONDecoder().decode(Envelope.self, from: bytes)
            guard (record.version == 1 && record.draft.measurementForm == nil) || (record.version == 2 && record.draft.measurementForm != nil) else { throw StoreError.invalid }
            try validate(record.draft)
            return Snapshot(draft: record.draft, revision: bytes)
        }
        static func load(_ url: URL) throws -> Snapshot? {
            var coordinationError: NSError?
            var result: Result<Snapshot?, Swift.Error>?
            NSFileCoordinator().coordinate(readingItemAt: url, options: [], error: &coordinationError) { path in
                result = Result { try readUncoordinated(path) }
            }
            if let coordinationError { throw coordinationError }
            guard let result else { throw StoreError.invalid }
            return try result.get()
        }
        @discardableResult static func save(_ draft: Draft, to url: URL, expected: Data?,
                         write: (Data, URL) throws -> Void = { data, path in try data.write(to: path, options: [.atomic, .completeFileProtection]) }) throws -> Data {
            try validate(draft)
            let bytes = try JSONEncoder().encode(Envelope(version: draft.measurementForm == nil ? 1 : 2, draft: draft, revision: UUID()))
            guard bytes.count <= maxBytes else { throw StoreError.tooLarge }
            var coordinationError: NSError?
            var result: Result<Void, Swift.Error>?
            NSFileCoordinator().coordinate(writingItemAt: url, options: .forMerging, error: &coordinationError) { path in
                result = Result {
                    let current = try readUncoordinated(path)
                    guard current?.revision == expected else { throw Conflict.changed }
                    try write(bytes, path)
                }
            }
            if let coordinationError { throw coordinationError }
            guard let result else { throw StoreError.invalid }
            try result.get()
            return bytes
        }
    }
    /// Serializes writes and coalesces rapid edits. Does not extend iOS process lifetime.
    final class DraftWriter {
        typealias Save = (Draft, URL, Data?) throws -> Data
        private let queue = DispatchQueue(label: "tr.monoditree.draft-writer", qos: .utility)
        private let save: Save
        private var revision: Data?
        private var generation = UUID()
        private var paused = true
        init(save: @escaping Save = { try DraftStore.save($0, to: $1, expected: $2) }) { self.save = save }
        func configure(revision: Data?) {
            queue.async { self.revision = revision; self.paused = false; self.generation = UUID() }
        }
        func pause(completion: @escaping () -> Void) {
            queue.async {
                self.paused = true; self.generation = UUID()
                DispatchQueue.main.async { completion() }
            }
        }
        func submit(_ draft: Draft, to url: URL, immediately: Bool = false,
                    completion: @escaping (Result<Void, Swift.Error>) -> Void) {
            queue.async {
                guard !self.paused else { DispatchQueue.main.async { completion(.failure(DraftStore.Conflict.changed)) }; return }
                let token = UUID(); self.generation = token
                let operation = {
                    guard !self.paused, self.generation == token else { return }
                    do {
                        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
                        self.revision = try self.save(draft, url, self.revision)
                        DispatchQueue.main.async { completion(.success(())) }
                    } catch {
                        if error is DraftStore.Conflict { self.paused = true }
                        DispatchQueue.main.async { completion(.failure(error)) }
                    }
                }
                if immediately { operation() }
                else { self.queue.asyncAfter(deadline: .now() + .milliseconds(700), execute: operation) }
            }
        }
    }
    struct ExportAttempt: Identifiable {
        let id = UUID()
        let draft: Draft
        let bytes: Data
        func matches(_ callbackID: UUID) -> Bool { id == callbackID }
        func matchesCurrentDraft(_ current: Draft) -> Bool { draft == current }
    }
    struct ExportPresentation {
        private(set) var currentID: UUID?
        private var presented = false
        mutating func begin(_ id: UUID) { currentID = id; presented = true }
        func isPresented(for id: UUID) -> Bool { currentID == id && presented }
        mutating func setPresented(_ value: Bool, for id: UUID) {
            guard currentID == id else { return }
            presented = value
        }
    }
    /// Selection describes an intended style; no pattern geometry is implied.
    /// The v1 format has no model identifier. Never infer or silently remove a prior label.
    static func hasEmbeddedModelLine(_ description: String) -> Bool {
        description.split(whereSeparator: \.isNewline).contains { line in
            guard let colon = line.firstIndex(of: ":") else { return false }
            let heading = line[..<colon].split(whereSeparator: { $0.isWhitespace })
                .joined(separator: " ").lowercased(with: Locale(identifier: "tr_TR"))
            return heading == "model" || heading == "seçilen elbise modeli"
        }
    }
    static func hasConflictingModelLabels(style: DressStyle, description: String) -> Bool {
        style != .describe && hasEmbeddedModelLine(description)
    }
    static func prompt(style: DressStyle, description: String) -> String {
        guard style != .describe else { return description }
        return "Seçilen elbise modeli: \(style.rawValue)\n" + description
    }

    /// Preserve exactly what the user entered, including units and unknowns.
    static func measurements(bust: String, waist: String, hips: String, length: String, other: String) -> String {
        let items = [("Göğüs çevresi", bust), ("Bel çevresi", waist), ("Kalça çevresi", hips), ("Elbise boyu", length)]
        var lines = items.compactMap { label, value -> String? in
            value.isEmpty ? nil : "\(label): \(value)"
        }
        if !other.isEmpty { lines.append(other) }
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

    private static let portableStyleIDs: [String: DressStyle] = [
        "wrap": .wrap, "aline": .aline, "corset": .corset,
        "sheath": .sheath, "straight": .straight, "shirt": .shirt
    ]
    struct ModelIdentity: Codable { let id: String }
    struct DocumentV2: Encodable {
        let format = "tekstilcad.design-request"
        let version = 2
        let prompt: String
        let measurements: String
        let reference: Image?
        let model: ModelIdentity?
        enum CodingKeys: String, CodingKey { case format, version, prompt, measurements, reference, model }
        func encode(to encoder: Encoder) throws {
            var box = encoder.container(keyedBy: CodingKeys.self)
            try box.encode(format, forKey: .format); try box.encode(version, forKey: .version)
            try box.encode(prompt, forKey: .prompt); try box.encode(measurements, forKey: .measurements)
            if let reference { try box.encode(reference, forKey: .reference) } else { try box.encodeNil(forKey: .reference) }
            if let model { try box.encode(model, forKey: .model) } else { try box.encodeNil(forKey: .model) }
        }
    }
    static func v2ImageGeometryAllowed(width: Int, height: Int, frames: Int) -> Bool {
        frames == 1 && width > 0 && height > 0 && width <= 12_000 && height <= 12_000 && width <= 24_000_000 / height
    }
    static func v2ImageOrientationAllowed(_ value: Any?) -> Bool {
        guard let value else { return true }
        guard let number = value as? NSNumber,
              CFGetTypeID(number) != CFBooleanGetTypeID(),
              ["c", "C", "s", "S", "i", "I", "l", "L", "q", "Q"].contains(String(cString: number.objCType)) else { return false }
        let orientation = number.int64Value
        return orientation >= 1 && orientation <= 8
    }
    // Windows v2 rejects PNG animation metadata even when it declares one frame.
    // Inspect chunk boundaries only; never rewrite the image or scan compressed pixels.
    static func v2PNGHasAnimationControl(_ data: Data) throws -> Bool {
        let bytes = [UInt8](data)
        guard bytes.count >= 8, Array(bytes.prefix(8)) == [137, 80, 78, 71, 13, 10, 26, 10] else { throw Error.invalidImage }
        var offset = 8
        while offset <= bytes.count - 12 {
            let length = (Int(bytes[offset]) << 24) | (Int(bytes[offset + 1]) << 16) | (Int(bytes[offset + 2]) << 8) | Int(bytes[offset + 3])
            guard length <= bytes.count - offset - 12 else { throw Error.invalidImage }
            let type = Array(bytes[(offset + 4)..<(offset + 8)])
            if type == [97, 99, 84, 76] { return true } // acTL
            if type == [73, 69, 78, 68] {
                guard length == 0, offset + 12 == bytes.count else { throw Error.invalidImage }
                return false
            } // IEND must end the file, matching the Windows V2 boundary.
            offset += length + 12
        }
        throw Error.invalidImage
    }
    private static func validateV2Image(_ data: Data) throws {
        guard !data.isEmpty, data.count <= maxImageBytes,
              let source = CGImageSourceCreateWithData(data as CFData, nil),
              let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any],
              let width = properties[kCGImagePropertyPixelWidth] as? Int,
              let height = properties[kCGImagePropertyPixelHeight] as? Int,
              v2ImageGeometryAllowed(width: width, height: height, frames: CGImageSourceGetCount(source)),
              ((CGImageSourceGetType(source) as String?) != "public.jpeg" || v2ImageOrientationAllowed(properties[kCGImagePropertyOrientation])),
              CGImageSourceCreateImageAtIndex(source, 0, nil) != nil else { throw Error.invalidImage }
        if (CGImageSourceGetType(source) as String?) == "public.png", try v2PNGHasAnimationControl(data) { throw Error.invalidImage }
    }
    static func makeV2(_ draft: Draft) throws -> Data {
        if let image = draft.image { try validateV2Image(image) }
        let id = portableStyleIDs.first(where: { $0.value == draft.style })?.key
        guard draft.style == .describe || id != nil else { throw Error.invalidText }
        let validated = try JSONDecoder().decode(Document.self, from: make(prompt: draft.description, measurements: draft.measurements, image: draft.image, imageName: draft.imageName))
        guard draft.description.utf16.count <= maxTextUnits else { throw Error.invalidText }
        let result = try JSONEncoder().encode(DocumentV2(prompt: draft.description, measurements: draft.measurements, reference: validated.reference, model: id.map { ModelIdentity(id: $0) }))
        guard result.count <= 12 * 1024 * 1024 else { throw Error.invalidText }
        return result
    }
    /// Import a portable request without changing its text or reference bytes.
    static func portableDraft(_ bytes: Data) throws -> Draft {
        guard bytes.count <= 12 * 1024 * 1024 else { throw Error.invalidText }
        // Desktop rejects duplicates and unknown fields; never drop unrecognized data.
        var stream = ExperimentalJSONStream(maximumBytes: 12 * 1024 * 1024, maximumTokenBytes: 12 * 1024 * 1024, maximumDepth: 8, maximumKeysPerObject: 6)
        try stream.feed(bytes); try stream.finish()
        guard let root = try JSONSerialization.jsonObject(with: bytes) as? [String: Any],
              let rawReference = root["reference"] else { throw Error.invalidText }
        if !(rawReference is NSNull) {
            guard let ref = rawReference as? [String: Any], Set(ref.keys) == Set(["name", "mediaType", "dataBase64"]) else { throw Error.invalidText }
        }
        let doc = try JSONDecoder().decode(Document.self, from: bytes)
        guard doc.format == "tekstilcad.design-request", doc.version == 1 || doc.version == 2 else { throw Error.invalidText }
        let keys: Set<String> = doc.version == 1 ? ["format", "version", "prompt", "measurements", "reference"] : ["format", "version", "prompt", "measurements", "reference", "model"]
        guard Set(root.keys) == keys else { throw Error.invalidText }
        var style = DressStyle.describe
        if doc.version == 2 {
            guard let rawModel = root["model"] else { throw Error.invalidText }
            if !(rawModel is NSNull) {
                guard let object = rawModel as? [String: Any], Set(object.keys) == Set(["id"]), let id = object["id"] as? String, let resolved = portableStyleIDs[id] else { throw Error.invalidText }
                style = resolved
            }
        }
        let image: Data?
        if let reference = doc.reference {
            let encoded = reference.dataBase64
            guard encoded.utf16.count <= ((maxImageBytes + 2) / 3) * 4 else { throw Error.invalidImage }
            let normalized = String(String.UnicodeScalarView(encoded.unicodeScalars.filter { ![0x20, 0x09, 0x0A, 0x0D].contains($0.value) }))
            guard let decoded = Data(base64Encoded: normalized) else { throw Error.invalidImage }
            image = decoded
        } else { image = nil }
        if doc.version == 2, let image { try validateV2Image(image) }
        // Reject unsupported or inconsistent images before replacing any current draft.
        let verified = try JSONDecoder().decode(Document.self, from: make(prompt: doc.prompt, measurements: doc.measurements, image: image, imageName: doc.reference?.name))
        guard doc.prompt.utf16.count <= maxTextUnits else { throw Error.invalidText }
        guard verified.reference?.mediaType == doc.reference?.mediaType else { throw Error.invalidImage }
        return Draft(style: style, description: doc.prompt, bust: "", waist: "", hips: "", length: "", otherMeasurements: doc.measurements, image: image, imageName: doc.reference?.name)
    }
    static func readPortable(_ url: URL) throws -> Draft {
        let grant = url.startAccessingSecurityScopedResource()
        defer { if grant { url.stopAccessingSecurityScopedResource() } }
        var coordinationError: NSError?
        var result: Result<Draft, Swift.Error>?
        NSFileCoordinator().coordinate(readingItemAt: url, options: [], error: &coordinationError) { path in
            result = Result {
                let handle = try FileHandle(forReadingFrom: path); defer { try? handle.close() }
                var bytes = Data()
                while let part = try handle.read(upToCount: 64 * 1024), !part.isEmpty {
                    guard bytes.count <= 12 * 1024 * 1024 - part.count else { throw Error.invalidText }
                    bytes.append(part)
                }
                return try portableDraft(bytes)
            }
        }
        if let coordinationError { throw coordinationError }
        guard let result else { throw Error.invalidText }
        return try result.get()
    }
    struct ReferenceSelection: Equatable { let data: Data; let name: String }
    static func referenceDecisionPending(loading: Bool, awaitingConfirmation: Bool, hasCandidate: Bool) -> Bool {
        loading || awaitingConfirmation || hasCandidate
    }
    struct ReferenceLoadTicket {
        let id = UUID()
        let original: ReferenceSelection?
        func matches(id: UUID?, current: ReferenceSelection?) -> Bool { self.id == id && original == current }
    }
    struct ReferenceChange {
        let original: ReferenceSelection?
        let replacement: ReferenceSelection?
        enum Conflict: Swift.Error { case changed }
        func apply(to current: ReferenceSelection?) throws -> ReferenceSelection? {
            guard original == current else { throw Conflict.changed }
            return replacement
        }
    }
    static func readImage(_ url: URL) throws -> Data {
        try Task.checkCancellation()
        let grant = url.startAccessingSecurityScopedResource()
        defer { if grant { url.stopAccessingSecurityScopedResource() } }
        var coordinationError: NSError?
        var result: Result<Data, Swift.Error>?
        NSFileCoordinator().coordinate(readingItemAt: url, options: [], error: &coordinationError) { path in
            result = Result {
                let handle = try FileHandle(forReadingFrom: path)
                defer { try? handle.close() }
                var bytes = Data()
                while let part = try handle.read(upToCount: 64 * 1024), !part.isEmpty {
                    try Task.checkCancellation()
                    guard bytes.count <= maxImageBytes - part.count else { throw Error.invalidImage }
                    bytes.append(part)
                }
                return bytes
            }
        }
        try Task.checkCancellation()
        if let coordinationError { throw coordinationError }
        guard let result else { throw Error.invalidImage }
        return try result.get()
    }
}
