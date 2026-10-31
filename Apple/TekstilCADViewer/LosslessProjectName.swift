import Foundation
import CoreFoundation

/// Replaces only the root project name token, leaving all other bytes untouched.
enum LosslessProjectName {
    static let maximumEditBytes = 8 * 1024 * 1024
    enum Failure: LocalizedError {
        case invalid
        var errorDescription: String? { "Proje adı güvenli biçimde değiştirilemedi. En fazla 8 MB boyutunda UTF-8 proje ve en fazla 1024 UTF-16 birimlik bir ad kullanın; UTF-16 ve UTF-32 dosyaları desteklenmez." }
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
        mutating func whitespace() { while i < bytes.count && [9, 10, 13, 32].contains(bytes[i]) { i += 1 } }
        mutating func take(_ c: UInt8) throws { whitespace(); guard i < bytes.count, bytes[i] == c else { throw Failure.invalid }; i += 1 }
        mutating func projectName() throws -> Range<Int> {
            whitespace()
            if i < bytes.count && bytes[i] == 91 {
                try take(91); try value(depth: 0); try take(44); try object(depth: 0, project: true); try take(93)
            } else { try object(depth: 0, project: true) }
            whitespace()
            guard i == bytes.count, let range = nameRange else { throw Failure.invalid }
            return range
        }
        mutating func string() throws -> Range<Int> {
            whitespace(); let start = i; try take(34)
            while i < bytes.count {
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
            try take(123); whitespace()
            if i < bytes.count && bytes[i] == 125 { i += 1; return }
            while true {
                let keyRange = try string()
                var isName = false
                if project {
                    let keyData = Data([91] + Array(bytes[keyRange]) + [93])
                    guard let key = (try? JSONSerialization.jsonObject(with: keyData) as? [String])?.first else { throw Failure.invalid }
                    isName = key == "Name"
                }
                try take(58); whitespace()
                if isName {
                    guard nameRange == nil, i < bytes.count, bytes[i] == 34 else { throw Failure.invalid }
                    nameRange = try string()
                } else { try value(depth: depth + 1) }
                whitespace(); guard i < bytes.count else { throw Failure.invalid }
                if bytes[i] == 125 { i += 1; return }
                try take(44)
            }
        }
        mutating func value(depth: Int) throws {
            guard depth < 512 else { throw Failure.invalid }; whitespace()
            guard i < bytes.count else { throw Failure.invalid }
            switch bytes[i] {
            case 34: _ = try string()
            case 123: try object(depth: depth + 1)
            case 91:
                i += 1; whitespace()
                if i < bytes.count && bytes[i] == 93 { i += 1; return }
                while true {
                    try value(depth: depth + 1); whitespace()
                    guard i < bytes.count else { throw Failure.invalid }
                    if bytes[i] == 93 { i += 1; return }
                    try take(44)
                }
            default:
                let start = i
                while i < bytes.count && ![9, 10, 13, 32, 44, 93, 125].contains(bytes[i]) { i += 1 }
                guard i > start else { throw Failure.invalid }
                guard let token = String(bytes: bytes[start..<i], encoding: .utf8) else { throw Failure.invalid }
                if token != "true" && token != "false" && token != "null" {
                    let pattern = #"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$"#
                    guard token.range(of: pattern, options: .regularExpression) != nil else { throw Failure.invalid }
                }
            }
        }
    }
}
