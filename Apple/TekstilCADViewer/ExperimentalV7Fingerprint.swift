import Foundation
import CryptoKit

/// Experimental port of the desktop historical layout hash encoding. This is
/// deliberately not connected to JSON parsing or the production viewer.
/// Callers must supply exact IEEE-754 bits, including each sign bit of zero.
enum ExperimentalV7Fingerprint {
    struct Point { let x: UInt64, y: UInt64 }
    struct Grain { let start: Point, end: Point; let needsReview: Bool }
    struct Mark { let index: Int32; let position: Point }
    struct Metadata { let role: String?, size: String?, material: String?; let quantity: Int32? }
    struct Piece { let id: UUID; let name: String?; let closed: Bool; let points: [Point]; let metadata: Metadata?; let grain: Grain?; let marks: [Mark]? }
    enum Failure: Error { case limit, cancelled }

    static func hash(_ pieces: [Piece], cancelled: () -> Bool = { false }) throws -> String {
        guard pieces.count <= 10_000 else { throw Failure.limit }
        var points = 0, marks = 0
        for piece in pieces {
            if cancelled() { throw Failure.cancelled }
            guard piece.points.count <= 10_000 else { throw Failure.limit }
            points += piece.points.count; marks += piece.marks?.count ?? 0
            guard points <= 200_000, marks <= 200_000 else { throw Failure.limit }
        }
        var hash = SHA256(), buffer = Data()
        func flush() { if !buffer.isEmpty { hash.update(data: buffer); buffer.removeAll(keepingCapacity: true) } }
        func byte(_ value: UInt8) { buffer.append(value); if buffer.count >= 8192 { flush() } }
        func word(_ value: UInt64, _ count: Int) { for i in 0..<count { byte(UInt8(truncatingIfNeeded: value >> (8 * i))) } }
        func boolean(_ value: Bool) { byte(value ? 1 : 0) }
        func text(_ value: String?) throws {
            boolean(value != nil); guard let value else { return }
            guard value.utf16.count <= 1024 else { throw Failure.limit }
            word(UInt64(value.utf16.count), 4)
            for (i, unit) in value.utf16.enumerated() {
                if i % 256 == 0 && cancelled() { throw Failure.cancelled }
                word(UInt64(unit), 2)
            }
        }
        func point(_ p: Point) { word(p.x, 8); word(p.y, 8) }
        let includeGrain = pieces.contains { $0.grain != nil }, includeMarks = pieces.contains { $0.marks != nil }
        let domain = includeMarks ? "TekstilCAD.nesting-layout.v3" : includeGrain ? "TekstilCAD.nesting-layout.v2" : "TekstilCAD.nesting-layout.v1"
        let domainBytes = Array(domain.utf8)
        // BinaryWriter.Write(string) uses a 7-bit encoded byte length; all
        // fixed versioned domain strings here have fewer than 128 bytes.
        byte(UInt8(domainBytes.count)); for value in domainBytes { byte(value) }
        if includeMarks { boolean(includeGrain) }
        word(UInt64(pieces.count), 4)
        for piece in pieces {
            if cancelled() { throw Failure.cancelled }
            var guid = piece.id.uuid
            let raw = withUnsafeBytes(of: &guid) { Array($0) }
            for index in [3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15] { byte(raw[index]) }
            try text(piece.name); boolean(piece.closed); word(UInt64(piece.points.count), 4)
            for p in piece.points { if cancelled() { throw Failure.cancelled }; point(p) }
            boolean(piece.metadata != nil)
            if let metadata = piece.metadata {
                try text(metadata.role); try text(metadata.size); try text(metadata.material)
                boolean(metadata.quantity != nil)
                if let quantity = metadata.quantity { word(UInt64(UInt32(bitPattern: quantity)), 4) }
            }
            if includeGrain {
                boolean(piece.grain != nil)
                if let grain = piece.grain { point(grain.start); point(grain.end); boolean(grain.needsReview) }
            }
            if includeMarks {
                boolean(piece.marks != nil)
                if let item = piece.marks {
                    word(UInt64(item.count), 4)
                    for mark in item {
                        if cancelled() { throw Failure.cancelled }
                        word(UInt64(UInt32(bitPattern: mark.index)), 4); point(mark.position)
                    }
                }
            }
        }
        if cancelled() { throw Failure.cancelled }; flush()
        return hash.finalize().map { String(format: "%02X", Int($0)) }.joined()
    }
}
