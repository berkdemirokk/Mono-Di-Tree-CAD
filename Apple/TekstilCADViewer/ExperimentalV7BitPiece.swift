import Foundation

/// Experimental bounded per-piece bridge from validated JSON bytes to the
/// desktop hash model. It preserves numeric token sign bits and never retains
/// a pointer into the caller's data. It is not a project parser.
enum ExperimentalV7BitPiece {
    enum Failure: Error { case invalid, limit, cancelled }
    static func parse(_ data: Data, requireHistoricalSchema: Bool = false,
                      cancelled: @escaping () -> Bool = { false }) throws -> ExperimentalV7Fingerprint.Piece {
        guard data.count <= 512 * 1024 else { throw Failure.limit }
        let pieces = try ExperimentalV7Contours.decode([data], cancelled: cancelled)
        guard let piece = pieces.first, let id = piece.id else { throw Failure.invalid }
        var scan = Scanner(bytes: Array(data), cancelled: cancelled)
        try scan.value(path: [], depth: 0)
        try scan.whitespace()
        guard scan.index == scan.bytes.count else { throw Failure.invalid }
        func point(_ prefix: String) throws -> ExperimentalV7Fingerprint.Point {
            if cancelled() { throw Failure.cancelled }
            guard let x = scan.numbers[prefix + ".X"], let y = scan.numbers[prefix + ".Y"] else { throw Failure.invalid }
            return .init(x: x, y: y)
        }
        let points = try piece.points.indices.map { try point("Points.\($0)") }
        let grain: ExperimentalV7Fingerprint.Grain?
        if let line = piece.grain {
            grain = try .init(start: point("Grainline.Start"), end: point("Grainline.End"), needsReview: line.needsReview)
        } else { grain = nil }
        var marks: [ExperimentalV7Fingerprint.Mark]? = nil
        if !piece.notches.isEmpty {
            marks = try piece.notches.enumerated().map { index, vertex in
                guard let exact = Int32(exactly: vertex) else { throw Failure.invalid }
                return try .init(index: exact, position: point("Notches.\(index).Position"))
            }
        }
        let metadata: ExperimentalV7Fingerprint.Metadata?
        let raw = try JSONSerialization.jsonObject(with: data) as? [String: Any]
        if requireHistoricalSchema {
            guard let raw, let fields = raw["Metadata"] as? [String: Any],
                  Set(fields.keys) == Set(["Role", "Size", "Material", "Quantity"]),
                  raw["Grainline"] is [String: Any] else { throw Failure.invalid }
        }
        if let value = raw?["Metadata"], !(value is NSNull) {
            metadata = .init(role: piece.role, size: piece.size, material: piece.material,
                             quantity: piece.quantity.flatMap { Int32(exactly: $0) })
        } else { metadata = nil }
        return .init(id: id, name: piece.name, closed: piece.closed, points: points,
                     metadata: metadata, grain: grain, marks: marks)
    }

    private struct Scanner {
        let bytes: [UInt8]
        let cancelled: () -> Bool
        var index = 0
        var numbers: [String: UInt64] = [:]
        mutating func whitespace() throws {
            while index < bytes.count && [9, 10, 13, 32].contains(bytes[index]) {
                if index % 4096 == 0 && cancelled() { throw Failure.cancelled }; index += 1
            }
        }
        mutating func string() throws -> String {
            guard index < bytes.count && bytes[index] == 34 else { throw Failure.invalid }
            let start = index; index += 1
            while index < bytes.count {
                if index % 4096 == 0 && cancelled() { throw Failure.cancelled }
                let c = bytes[index]; index += 1
                if c == 92 { guard index < bytes.count else { throw Failure.invalid }; index += 1; continue }
                if c == 34 {
                    guard let decoded = try JSONSerialization.jsonObject(with: Data(bytes[start..<index]), options: [.fragmentsAllowed]) as? String else { throw Failure.invalid }
                    return decoded
                }
            }
            throw Failure.invalid
        }
        mutating func value(path: [String], depth: Int) throws {
            if cancelled() { throw Failure.cancelled }
            guard depth < 128 else { throw Failure.limit }; try whitespace()
            guard index < bytes.count else { throw Failure.invalid }
            switch bytes[index] {
            case 123:
                index += 1; try whitespace()
                if index < bytes.count && bytes[index] == 125 { index += 1; return }
                while true {
                    try whitespace(); let key = try string(); try whitespace()
                    guard index < bytes.count && bytes[index] == 58 else { throw Failure.invalid }; index += 1
                    try value(path: path + [key], depth: depth + 1); try whitespace()
                    guard index < bytes.count else { throw Failure.invalid }
                    if bytes[index] == 125 { index += 1; return }
                    guard bytes[index] == 44 else { throw Failure.invalid }; index += 1
                }
            case 91:
                index += 1; try whitespace(); var position = 0
                if index < bytes.count && bytes[index] == 93 { index += 1; return }
                while true {
                    try value(path: path + [String(position)], depth: depth + 1); position += 1; try whitespace()
                    guard index < bytes.count else { throw Failure.invalid }
                    if bytes[index] == 93 { index += 1; return }
                    guard bytes[index] == 44 else { throw Failure.invalid }; index += 1
                }
            case 34: _ = try string()
            default:
                let start = index
                while index < bytes.count && ![9, 10, 13, 32, 44, 93, 125].contains(bytes[index]) {
                    if index % 4096 == 0 && cancelled() { throw Failure.cancelled }; index += 1
                }
                guard index > start else { throw Failure.invalid }
                let coordinate = path.last == "X" || path.last == "Y"
                if coordinate && (path.first == "Points" || path.first == "Notches" || path.first == "Grainline") {
                    guard index - start <= 128 else { throw Failure.limit }
                    let key = path.joined(separator: ".")
                    guard numbers[key] == nil else { throw Failure.invalid }
                    numbers[key] = try ExperimentalJSONDoubleBits.parse(Data(bytes[start..<index]), cancelled: cancelled)
                }
            }
        }
    }
}
