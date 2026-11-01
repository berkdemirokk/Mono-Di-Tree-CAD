import Foundation
import CoreFoundation

/// Replaces only the root project name token, leaving all other bytes untouched.
enum LosslessProjectName {
    static let maximumEditBytes = 8 * 1024 * 1024
    // Scan the existing, at-most-64-MB snapshot without creating a decoded
    // copy. The envelope hint permits raw recovery; it is not validation.
    static func isOversizedVersionSix(_ data: Data) -> Bool {
        guard data.count > maximumEditBytes, data.count <= 64 * 1024 * 1024 else { return false }
        return hasVersionSixPrefix(data)
    }

    static func requiresVersionSixRawRecovery(_ data: Data) -> Bool {
        if isOversizedVersionSix(data) { return true }
        guard data.count <= maximumEditBytes, let encoding = bomEncoding(data), hasVersionSixPrefix(data),
              let text = String(data: data, encoding: encoding) else { return false }
        return text.utf8.count > maximumEditBytes
    }

    private static func bomEncoding(_ data: Data) -> String.Encoding? {
        if data.starts(with: [0xff, 0xfe, 0x00, 0x00]) { return .utf32LittleEndian }
        if data.starts(with: [0x00, 0x00, 0xfe, 0xff]) { return .utf32BigEndian }
        if data.starts(with: [0xff, 0xfe]) { return .utf16LittleEndian }
        if data.starts(with: [0xfe, 0xff]) { return .utf16BigEndian }
        return nil
    }

    // Drawing.load has already validated the text; only its BOM is needed
    // here to decide whether the UTF-8-only name editor can be offered.
    static func hasNonUTF8BOM(_ data: Data) -> Bool { bomEncoding(data) != nil }

    private static func hasVersionSixPrefix(_ data: Data) -> Bool {
        return data.withUnsafeBytes { raw in
            let bytes = raw.bindMemory(to: UInt8.self)
            let limit = bytes.count
            var i = 0, width = 1
            var littleEndian = false
            if bytes.starts(with: [0xff, 0xfe, 0x00, 0x00]) { i = 4; width = 4; littleEndian = true }
            else if bytes.starts(with: [0x00, 0x00, 0xfe, 0xff]) { i = 4; width = 4 }
            else if bytes.starts(with: [0xff, 0xfe]) { i = 2; width = 2; littleEndian = true }
            else if bytes.starts(with: [0xfe, 0xff]) { i = 2; width = 2 }
            else if bytes.starts(with: [0xef, 0xbb, 0xbf]) { i = 3 }
            func next() -> UInt32? {
                while i + width <= limit {
                    var unit: UInt32 = 0
                    for offset in 0..<width {
                        if littleEndian { unit |= UInt32(bytes[i + offset]) << (8 * offset) }
                        else { unit = (unit << 8) | UInt32(bytes[i + offset]) }
                    }
                    i += width
                    if unit == 9 || unit == 10 || unit == 13 || unit == 32 { continue }
                    return unit
                }
                return nil
            }
            return next() == 91 && next() == 54 && next() == 44
        }
    }

    // Only for checking the v6 JSON structure. The caller must retain and
    // save the original bytes, not this temporary UTF-8 representation.
    static func versionSixValidationData(_ data: Data) throws -> Data {
        guard data.count <= maximumEditBytes else { throw Failure.invalid }
        let encoding = bomEncoding(data) ?? .utf8
        if encoding == .utf8 {
            guard String(data: data, encoding: .utf8) != nil else { throw Failure.invalid }
            return data
        }
        guard let text = String(data: data, encoding: encoding),
              text.utf8.count <= maximumEditBytes else { throw Failure.invalid }
        return Data(text.utf8)
    }
    enum Failure: LocalizedError {
        case invalid
        var errorDescription: String? { "Proje adı güvenli biçimde değiştirilemedi. En fazla 8 MB boyutunda UTF-8 proje ve en fazla 1024 UTF-16 birimlik bir ad kullanın; UTF-16 ve UTF-32 dosyaları desteklenmez." }
    }

    // The viewer already parsed JSON; scan the original UTF-8 bytes for
    // duplicate keys without rewriting or normalizing the project.
    static func validateUniqueKeys(_ data: Data, strictV6: Bool = true, cancelled: @escaping () -> Bool = { false }) throws {
        if cancelled() { throw CancellationError() }
        // Bound the second scan until a streaming parser is available.
        guard data.count <= (strictV6 ? maximumEditBytes : 96 * 1024 * 1024), String(data: data, encoding: .utf8) != nil else { throw Failure.invalid }
        try data.withUnsafeBytes { raw in
            let bytes = raw.bindMemory(to: UInt8.self)
            let bom = bytes.count >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            var scanner = Scanner(bytes: bytes, i: bom ? 3 : 0, strictV6: strictV6, cancelled: cancelled)
            _ = try scanner.projectName()
        }
    }

    // Validate a bounded auxiliary JSON document without requiring project
    // fields; escaped spellings of the same key count as duplicates.
    static func validateAuxiliaryJSONKeys(_ data: Data, limit: Int) throws {
        guard data.count <= limit, String(data: data, encoding: .utf8) != nil else { throw Failure.invalid }
        try data.withUnsafeBytes { raw in
            let bytes = raw.bindMemory(to: UInt8.self)
            let bom = bytes.count >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            var scanner = Scanner(bytes: bytes, i: bom ? 3 : 0)
            try scanner.value(depth: 0)
            try scanner.whitespace()
            guard scanner.i == bytes.count else { throw Failure.invalid }
        }
    }

    static func replacing(in data: Data, with name: String) throws -> Data {
        // Renaming reparses the JSON while the preview is still resident.
        // Keep this lower than the viewer limit until streaming validation is available.
        let limit = maximumEditBytes
        guard data.count <= limit, name.utf16.count <= 1024 else { throw Failure.invalid }
        // Release the potentially large Foundation object graph before scanning or copying.
        try autoreleasepool {
            guard String(data: data, encoding: .utf8) != nil else { throw Failure.invalid }
            let root: Any
            do { root = try JSONSerialization.jsonObject(with: data) } catch { throw Failure.invalid }
            if let array = root as? [Any] {
                guard array.count == 2, let version = array[0] as? NSNumber,
                      CFGetTypeID(version) != CFBooleanGetTypeID(),
                      version.doubleValue.rounded() == version.doubleValue,
                      (1.0...5.0).contains(version.doubleValue), array[1] is [String: Any] else { throw Failure.invalid }
            } else if !(root is [String: Any]) { throw Failure.invalid }
        }
        let range: Range<Int> = try data.withUnsafeBytes { raw in
            let bytes = raw.bindMemory(to: UInt8.self)
            let bom = bytes.count >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            var scanner = Scanner(bytes: bytes, i: bom ? 3 : 0)
            return try scanner.projectName()
        }
        let encoded = try JSONSerialization.data(withJSONObject: [name], options: [.fragmentsAllowed])
        var output = Data(data.prefix(range.lowerBound))
        output.append(encoded.dropFirst().dropLast())
        output.append(data.suffix(data.count - range.upperBound))
        guard output.count <= limit else { throw Failure.invalid }
        return output
    }

    private struct Scanner {
        let bytes: UnsafeBufferPointer<UInt8>
        var i = 0
        var nameRange: Range<Int>?
        var strictV6 = false
        var cancelled: () -> Bool = { false }
        var nextCancellationCheck = 0
        mutating func checkCancellation() throws {
            if i >= nextCancellationCheck {
                if cancelled() { throw CancellationError() }
                nextCancellationCheck = i + 4096
            }
        }
        mutating func whitespace() throws {
            while i < bytes.count && [9, 10, 13, 32].contains(bytes[i]) { try checkCancellation(); i += 1 }
        }
        mutating func take(_ c: UInt8) throws { try whitespace(); guard i < bytes.count, bytes[i] == c else { throw Failure.invalid }; i += 1 }
        mutating func projectName() throws -> Range<Int> {
            try whitespace()
            if i < bytes.count && bytes[i] == 91 {
                try take(91); try whitespace(); let start = i; try value(depth: 0)
                if strictV6 && String(bytes: bytes[start..<i], encoding: .utf8) != "6" { throw Failure.invalid }
                try take(44); try object(depth: 0, project: true); try take(93)
            } else { try object(depth: 0, project: true) }
            try whitespace()
            guard i == bytes.count, let range = nameRange else { throw Failure.invalid }
            return range
        }
        mutating func string() throws -> Range<Int> {
            try whitespace(); let start = i; try take(34)
            while i < bytes.count {
                try checkCancellation()
                let c = bytes[i]; i += 1
                if c == 34 { return start..<i }
                if c < 32 { throw Failure.invalid }
                if c == 92 {
                    guard i < bytes.count else { throw Failure.invalid }
                    let escape = bytes[i]; i += 1
                    if escape == 117 {
                        guard i + 4 <= bytes.count else { throw Failure.invalid }
                        for c in bytes[i..<i+4] where !(48...57).contains(c) && !(65...70).contains(c) && !(97...102).contains(c) { throw Failure.invalid }
                        i += 4
                    } else if ![34, 92, 47, 98, 102, 110, 114, 116].contains(escape) { throw Failure.invalid }
                }
            }
            throw Failure.invalid
        }
        mutating func object(depth: Int, project: Bool = false) throws {
            guard depth < 512 else { throw Failure.invalid }
            try take(123); try whitespace()
            if i < bytes.count && bytes[i] == 125 { i += 1; return }
            var keys = Set<String>()
            while true {
                let keyRange = try string()
                let keyData = Data([91] + Array(bytes[keyRange]) + [93])
                guard let key = (try? JSONSerialization.jsonObject(with: keyData) as? [String])?.first,
                      keys.insert(key).inserted else { throw Failure.invalid }
                let isName = project && key == "Name"
                try take(58); try whitespace()
                if strictV6 && key == "VertexIndex" {
                    let start = i; try value(depth: depth + 1)
                    guard let token = String(bytes: bytes[start..<i], encoding: .utf8),
                          !token.isEmpty, token.allSatisfy({ $0 >= "0" && $0 <= "9" }),
                          (token == "0" || token.first != "0"), Int32(token) != nil else { throw Failure.invalid }
                } else if isName {
                    guard nameRange == nil, i < bytes.count, bytes[i] == 34 else { throw Failure.invalid }
                    nameRange = try string()
                } else { try value(depth: depth + 1) }
                try whitespace(); guard i < bytes.count else { throw Failure.invalid }
                if bytes[i] == 125 { i += 1; return }
                try take(44)
            }
        }
        mutating func value(depth: Int) throws {
            guard depth < 512 else { throw Failure.invalid }; try checkCancellation(); try whitespace()
            guard i < bytes.count else { throw Failure.invalid }
            switch bytes[i] {
            case 34: _ = try string()
            case 123: try object(depth: depth + 1)
            case 91:
                i += 1; try whitespace()
                if i < bytes.count && bytes[i] == 93 { i += 1; return }
                while true {
                    try value(depth: depth + 1); try whitespace()
                    guard i < bytes.count else { throw Failure.invalid }
                    if bytes[i] == 93 { i += 1; return }
                    try take(44)
                }
            default:
                let start = i
                while i < bytes.count && ![9, 10, 13, 32, 44, 93, 125].contains(bytes[i]) { try checkCancellation(); i += 1 }
                guard i > start else { throw Failure.invalid }
                guard let token = String(bytes: bytes[start..<i], encoding: .utf8) else { throw Failure.invalid }
                if token != "true" && token != "false" && token != "null" {
                    let pattern = #"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$"#
                    guard token.range(of: pattern, options: .regularExpression) != nil else { throw Failure.invalid }
                    if strictV6 {
                        // Desktop rejects nonzero numbers that deserialize to
                        // zero. Otherwise distinct geometry can appear equal.
                        guard let number = Double(token), number.isFinite else { throw Failure.invalid }
                        let mantissa = token.prefix { $0 != "e" && $0 != "E" }
                        guard number != 0 || !mantissa.contains(where: { ("1"..."9").contains(String($0)) }) else { throw Failure.invalid }
                    }
                }
            }
        }
    }
}
