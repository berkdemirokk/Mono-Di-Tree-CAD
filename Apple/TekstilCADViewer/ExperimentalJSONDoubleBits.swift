import Foundation

/// Research-only conversion of one complete ASCII JSON number token to the
/// represented binary64 bits. The input is borrowed for the duration of the
/// call; no pointer or slice is retained. This is not a JSON document parser.
enum ExperimentalJSONDoubleBits {
    enum Failure: Error { case invalid, limit, cancelled }
    static func parse(_ token: Data, cancelled: () -> Bool = { false }) throws -> UInt64 {
        if cancelled() { throw Failure.cancelled }
        guard !token.isEmpty, token.count <= 128 else { throw Failure.limit }
        let bytes = Array(token)
        var index = 0, negative = false, nonzeroMantissa = false
        if bytes[index] == 45 { negative = true; index += 1 }
        guard index < bytes.count else { throw Failure.invalid }
        if bytes[index] == 48 {
            index += 1
            if index < bytes.count && (48...57).contains(bytes[index]) { throw Failure.invalid }
        } else {
            guard (49...57).contains(bytes[index]) else { throw Failure.invalid }
            while index < bytes.count && (48...57).contains(bytes[index]) {
                if cancelled() { throw Failure.cancelled }
                if bytes[index] != 48 { nonzeroMantissa = true }; index += 1
            }
        }
        if index < bytes.count && bytes[index] == 46 {
            index += 1; let start = index
            while index < bytes.count && (48...57).contains(bytes[index]) {
                if cancelled() { throw Failure.cancelled }
                if bytes[index] != 48 { nonzeroMantissa = true }; index += 1
            }
            guard index > start else { throw Failure.invalid }
        }
        if index < bytes.count && (bytes[index] == 101 || bytes[index] == 69) {
            index += 1
            if index < bytes.count && (bytes[index] == 43 || bytes[index] == 45) { index += 1 }
            let start = index
            while index < bytes.count && (48...57).contains(bytes[index]) {
                if cancelled() { throw Failure.cancelled }; index += 1
            }
            guard index > start else { throw Failure.invalid }
        }
        guard index == bytes.count, let text = String(bytes: bytes, encoding: .utf8),
              let number = Double(text), number.isFinite else { throw Failure.invalid }
        if cancelled() { throw Failure.cancelled }
        // Desktop rejects nonzero values rounded all the way to zero. Decimal
        // spellings of zero retain their explicit sign even if a Foundation
        // bridge would otherwise normalize it.
        if number == 0 {
            guard !nonzeroMantissa else { throw Failure.invalid }
            return negative ? (UInt64(1) << 63) : 0
        }
        return number.bitPattern
    }
}
