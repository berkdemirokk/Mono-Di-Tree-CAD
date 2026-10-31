import Foundation
import CoreFoundation

/// Experimental token-preserving edit. Callers must warn that saved nesting and
/// import history describe earlier state; this API does not validate production use.
/// The snapshot check compares caller-provided bytes only; it does not lock or
/// re-read a file and does not prevent external changes or concurrent writes.
enum ExperimentalPieceRename {
    enum Failure: Error { case invalid }
    static func replacing(in data: Data, snapshot: Data, pieceID: UUID, with name: String) throws -> Data {
        try replacing(in: data, snapshot: snapshot, selector: .id(pieceID), with: name)
    }

    /// The index and expected name must originate from the exact same snapshot.
    static func replacingLegacy(in data: Data, snapshot: Data, pieceIndex: Int, expectedName: String, with name: String) throws -> Data {
        try replacing(in: data, snapshot: snapshot, selector: .legacy(pieceIndex, expectedName), with: name)
    }

    private enum Selector { case id(UUID), legacy(Int, String) }
    private static func replacing(in data: Data, snapshot: Data, selector: Selector, with name: String) throws -> Data {
        guard data == snapshot, data.count <= 8 * 1024 * 1024, name.utf16.count <= 1024,
              String(data: data, encoding: .utf8) != nil else { throw Failure.invalid }
        // Release the parsed object graph before scanning and copying.
        let selected: Int = try autoreleasepool {
            let root: Any
            do { root = try JSONSerialization.jsonObject(with: data) } catch { throw Failure.invalid }
            let project: [String: Any]
            if let array = root as? [Any], array.count == 2, let version = array[0] as? NSNumber,
               CFGetTypeID(version) != CFBooleanGetTypeID(), version.doubleValue.rounded() == version.doubleValue,
               (1...5).contains(version.doubleValue), let body = array[1] as? [String: Any] { project = body }
            else if let body = root as? [String: Any] { project = body }
            else { throw Failure.invalid }
            guard let pieces = project["Pieces"] as? [[String: Any]], pieces.count <= 10_000 else { throw Failure.invalid }
            var ids = Set<UUID>()
            var selected: Int?
            for (index, piece) in pieces.enumerated() {
                guard let pieceName = piece["Name"] as? String else { throw Failure.invalid }
                var id: UUID?
                if let rawID = piece["Id"] {
                    guard let idText = rawID as? String, let parsed = UUID(uuidString: idText),
                          parsed.uuidString != "00000000-0000-0000-0000-000000000000",
                          ids.insert(parsed).inserted else { throw Failure.invalid }
                    id = parsed
                }
                switch selector {
                case .id(let wanted) where id == wanted: selected = index
                case .legacy(let wantedIndex, let expectedName) where index == wantedIndex:
                    guard id == nil, pieceName == expectedName else { throw Failure.invalid }
                    selected = index
                default: break
                }
            }
            guard let selected else { throw Failure.invalid }
            return selected
        }
        let range: Range<Int> = try data.withUnsafeBytes { raw in
            let bytes = raw.bindMemory(to: UInt8.self)
            let bom = bytes.count >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            var scanner = Scanner(bytes: bytes, index: bom ? 3 : 0, selected: selected)
            return try scanner.scan()
        }
        let encoded = try JSONSerialization.data(withJSONObject: [name])
        // Reject an oversized result before allocating the output buffer.
        let replacementCount = encoded.count - 2
        guard data.count - range.count + replacementCount <= 8 * 1024 * 1024 else { throw Failure.invalid }
        var result = Data(data.prefix(range.lowerBound))
        result.append(encoded.dropFirst().dropLast())
        result.append(data.suffix(data.count - range.upperBound))
        guard result.count <= 8 * 1024 * 1024 else { throw Failure.invalid }
        return result
    }

    private struct Scanner {
        let bytes: UnsafeBufferPointer<UInt8>
        var index: Int
        let selected: Int
        var target: Range<Int>?
        mutating func whitespace() { while index < bytes.count && [9, 10, 13, 32].contains(bytes[index]) { index += 1 } }
        mutating func take(_ byte: UInt8) throws { whitespace(); guard index < bytes.count, bytes[index] == byte else { throw Failure.invalid }; index += 1 }
        mutating func scan() throws -> Range<Int> {
            whitespace()
            if index < bytes.count && bytes[index] == 91 {
                try take(91); try value(depth: 0); try take(44); try object(depth: 0, context: 1); try take(93)
            } else { try object(depth: 0, context: 1) }
            whitespace(); guard index == bytes.count, let target else { throw Failure.invalid }; return target
        }
        mutating func string() throws -> Range<Int> {
            whitespace(); let start = index; try take(34)
            while index < bytes.count {
                let c = bytes[index]; index += 1
                if c == 34 { return start..<index }
                if c < 32 { throw Failure.invalid }
                if c == 92 {
                    guard index < bytes.count else { throw Failure.invalid }
                    let e = bytes[index]; index += 1
                    if e == 117 {
                        guard index + 4 <= bytes.count else { throw Failure.invalid }
                        for c in bytes[index..<index+4] where !(48...57).contains(c) && !(65...70).contains(c) && !(97...102).contains(c) { throw Failure.invalid }
                        index += 4
                    } else if ![34,92,47,98,102,110,114,116].contains(e) { throw Failure.invalid }
                }
            }
            throw Failure.invalid
        }
        mutating func object(depth: Int, context: Int = 0) throws {
            guard depth < 512 else { throw Failure.invalid }
            try take(123); whitespace(); if index < bytes.count && bytes[index] == 125 { index += 1; return }
            var keys = Set<String>()
            while true {
                let range = try string()
                let wrapped = Data([91] + Array(bytes[range]) + [93])
                guard let key = (try? JSONSerialization.jsonObject(with: wrapped) as? [String])?.first,
                      keys.insert(key).inserted else { throw Failure.invalid }
                try take(58); whitespace()
                if context == 1 && key == "Pieces" { try pieceArray(depth: depth + 1) }
                else if context == 2 && key == "Name" { target = try string() }
                else { try value(depth: depth + 1) }
                whitespace(); guard index < bytes.count else { throw Failure.invalid }
                if bytes[index] == 125 { index += 1; return }; try take(44)
            }
        }
        mutating func pieceArray(depth: Int) throws {
            guard depth < 512 else { throw Failure.invalid }
            try take(91); whitespace(); if index < bytes.count && bytes[index] == 93 { index += 1; return }
            var position = 0
            while true {
                if position == selected { try object(depth: depth + 1, context: 2) }
                else { try value(depth: depth + 1) }
                position += 1; whitespace(); guard index < bytes.count else { throw Failure.invalid }
                if bytes[index] == 93 { index += 1; return }; try take(44)
            }
        }
        mutating func value(depth: Int) throws {
            guard depth < 512 else { throw Failure.invalid }; whitespace(); guard index < bytes.count else { throw Failure.invalid }
            switch bytes[index] {
            case 34: _ = try string()
            case 123: try object(depth: depth + 1)
            case 91:
                index += 1; whitespace(); if index < bytes.count && bytes[index] == 93 { index += 1; return }
                while true { try value(depth: depth + 1); whitespace(); guard index < bytes.count else { throw Failure.invalid }; if bytes[index] == 93 { index += 1; return }; try take(44) }
            default:
                let start = index
                while index < bytes.count && ![9,10,13,32,44,93,125].contains(bytes[index]) { index += 1 }
                guard index > start else { throw Failure.invalid }
            }
        }
    }
}
