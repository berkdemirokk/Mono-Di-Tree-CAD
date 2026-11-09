import Foundation
import ImageIO

enum FactoryCatalogError: LocalizedError {
    case invalid
    var errorDescription: String? { "Model kütüphanesi geçersiz veya desteklenen sınırları aşıyor." }
}

enum ModelCodeFormat {
    static func valid(_ code: String) -> Bool {
        (5...64).contains(code.utf16.count) && code.range(of: #"\A(?:[0-9]+-[0-9]+-[0-9]+|[0-9]+-[0-9]{3,})\z"#, options: .regularExpression) != nil
    }
}

/// Read-only owner declarations. Does not contain contours, cut quantities or a production approval.
struct FactoryModelCatalog: Decodable {
    struct Part: Decodable { let name: String; let group: String }
    struct Model: Decodable, Identifiable {
        let id: String
        let code: String?
        let fullName: String
        let status: String
        let sizes: [Int]
        let seamAllowanceIncluded: Bool
        let sourceIndexSha256: String
        let parts: [Part]
        let notes: [String]
        let thumbnailJpeg: String?
        var thumbnailData: Data? {
            guard let thumbnailJpeg, let data = Data(base64Encoded: thumbnailJpeg),
                  let source = CGImageSourceCreateWithData(data as CFData, nil),
                  let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any],
                  let width = properties[kCGImagePropertyPixelWidth] as? NSNumber,
                  let height = properties[kCGImagePropertyPixelHeight] as? NSNumber,
                  width.doubleValue > 0, height.doubleValue > 0,
                  width.doubleValue * height.doubleValue <= 1_000_000 else { return nil }
            return data
        }
    }
    let format: String
    let version: Int
    let name: String
    let confirmedOn: String
    let materialAliases: [String: String]
    let cuttingGroups: [String]
    let models: [Model]
    static let maximumBytes = 16 * 1024 * 1024

    static func decode(_ data: Data) throws -> Self {
        guard !data.isEmpty, data.count <= maximumBytes else { throw FactoryCatalogError.invalid }
        var syntax = ExperimentalJSONStream(maximumBytes: maximumBytes, maximumTokenBytes: 100_000, maximumDepth: 12)
        try syntax.feed(data); try syntax.finish()
        let value = try JSONDecoder().decode(Self.self, from: data)
        guard let raw = try JSONSerialization.jsonObject(with: data) as? [String: Any],
              Set(raw.keys) == Set(["format", "version", "name", "confirmedOn", "materialAliases", "cuttingGroups", "models"]),
              let entries = raw["models"] as? [[String: Any]] else { throw FactoryCatalogError.invalid }
        for entry in entries {
            let required: Set<String> = ["id", "fullName", "status", "sizes", "seamAllowanceIncluded", "sourceIndexSha256", "parts", "notes"]
            guard required.isSubset(of: Set(entry.keys)), Set(entry.keys).isSubset(of: required.union(["code", "thumbnailJpeg"])),
                  let parts = entry["parts"] as? [[String: Any]], parts.allSatisfy({ Set($0.keys) == Set(["name", "group"]) }) else { throw FactoryCatalogError.invalid }
        }
        try value.validate()
        return value
    }

    private func validate() throws {
        func text(_ value: String, _ limit: Int, empty: Bool = false) -> Bool {
            value.utf16.count <= limit && (empty || !value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty) && value.rangeOfCharacter(from: .controlCharacters) == nil
        }
        guard format == "tekstilcad.factory-catalog", version == 1, text(name, 200), text(confirmedOn, 40),
              (1...1000).contains(models.count), materialAliases.count <= 50, cuttingGroups.count <= 50,
              Set(cuttingGroups).count == cuttingGroups.count, cuttingGroups.allSatisfy({ text($0, 100) }),
              materialAliases.allSatisfy({ text($0.key, 100) && text($0.value, 100) }) else { throw FactoryCatalogError.invalid }
        var ids = Set<String>(), names = Set<String>(), total = 0
        for model in models {
            guard text(model.id, 256), ids.insert(model.id.lowercased()).inserted,
                  text(model.fullName, 256), names.insert(model.fullName.lowercased()).inserted,
                  model.code.map(ModelCodeFormat.valid) ?? true,
                  ["unconfirmed", "cancelled", "untested"].contains(model.status),
                  (1...64).contains(model.sizes.count), Set(model.sizes).count == model.sizes.count,
                  model.sizes.allSatisfy({ (1...64).contains($0) }),
                  model.sourceIndexSha256.range(of: #"\A[0-9a-fA-F]{64}\z"#, options: .regularExpression) != nil,
                  model.parts.count <= 1000, model.notes.count <= 40, model.notes.allSatisfy({ text($0, 1000) }),
                  model.parts.allSatisfy({ text($0.name, 256) && text($0.group, 100, empty: true) }) else { throw FactoryCatalogError.invalid }
            total += model.parts.count
            guard total <= 100_000 else { throw FactoryCatalogError.invalid }
            if let image = model.thumbnailJpeg {
                guard image.utf16.count <= 90_000, let bytes = Data(base64Encoded: image), bytes.count >= 4,
                      bytes.prefix(2) == Data([0xff, 0xd8]), bytes.suffix(2) == Data([0xff, 0xd9]) else { throw FactoryCatalogError.invalid }
            }
        }
    }

    func search(_ query: String) -> [Model] {
        let query = query.trimmingCharacters(in: .whitespacesAndNewlines)
        return models.filter { query.isEmpty || $0.fullName.localizedCaseInsensitiveContains(query) || $0.sizes.map(String.init).contains(query) }
    }

    static func read(_ url: URL) throws -> Data {
        let scoped = url.startAccessingSecurityScopedResource()
        defer { if scoped { url.stopAccessingSecurityScopedResource() } }
        let handle = try FileHandle(forReadingFrom: url); defer { try? handle.close() }
        var data = Data()
        while let chunk = try handle.read(upToCount: 65536), !chunk.isEmpty {
            guard data.count + chunk.count <= maximumBytes else { throw FactoryCatalogError.invalid }
            data.append(chunk)
        }
        return data
    }
}
