import Foundation

/// Experimental incremental UTF-8 JSON structure validator. Not used by the viewer.
/// Input chunks are consumed without retaining the document; this does not build a model.
struct ExperimentalJSONStream {
    enum Failure: Error { case invalid, limit, cancelled }
    private enum Kind { case object, array }
    private enum Phase { case objectFirst, objectKey, objectColon, objectValue, objectAfter, arrayFirst, arrayValue, arrayAfter }
    private struct Frame { var kind: Kind; var phase: Phase; var keys = Set<String>(); var keyBytes = 0 }
    private enum Lex { case none, string, atom }
    private var frames: [Frame] = []
    private var lex: Lex = .none
    private var token: [UInt8] = []
    private var escaped = false
    private var rootDone = false
    private var byteCount = 0
    private var keyBytes = 0
    private var failed = false
    private var finished = false
    private let maximumBytes: Int
    private let maximumTokenBytes: Int
    private let maximumDepth: Int
    private let maximumKeysPerObject: Int

    init(maximumBytes: Int = 64 * 1024 * 1024, maximumTokenBytes: Int = 64 * 1024,
         maximumDepth: Int = 128, maximumKeysPerObject: Int = 128) {
        self.maximumBytes = maximumBytes; self.maximumTokenBytes = maximumTokenBytes
        self.maximumDepth = maximumDepth; self.maximumKeysPerObject = maximumKeysPerObject
    }

    mutating func feed(_ data: Data, cancelled: () -> Bool = { false }) throws {
        guard !failed, !finished else { throw Failure.invalid }
        do {
            for byte in data {
                if cancelled() { throw Failure.cancelled }
                byteCount += 1
                guard byteCount <= maximumBytes else { throw Failure.limit }
                try consume(byte)
            }
        } catch { failed = true; throw error }
    }
    mutating func finish(cancelled: () -> Bool = { false }) throws {
        guard !failed, !finished else { throw Failure.invalid }
        do {
            if cancelled() { throw Failure.cancelled }
            if case .atom = lex { try endAtom() }
            guard case .none = lex, frames.isEmpty, rootDone else { throw Failure.invalid }
            finished = true
        } catch { failed = true; throw error }
    }
    private mutating func append(_ byte: UInt8) throws {
        guard token.count < maximumTokenBytes else { throw Failure.limit }
        token.append(byte)
    }
    private mutating func consume(_ byte: UInt8) throws {
        switch lex {
        case .string:
            try append(byte)
            if escaped { escaped = false; return }
            if byte == 92 { escaped = true; return }
            if byte == 34 { try endString() }
            return
        case .atom:
            if ![9, 10, 13, 32, 44, 93, 125].contains(byte) { try append(byte); return }
            try endAtom(); try consume(byte); return
        case .none: break
        }
        if [9, 10, 13, 32].contains(byte) { return }
        switch byte {
        case 34: lex = .string; token = []; escaped = false; try append(byte)
        case 123:
            try beginValue(); guard frames.count < maximumDepth else { throw Failure.limit }
            frames.append(Frame(kind: .object, phase: .objectFirst))
        case 91:
            try beginValue(); guard frames.count < maximumDepth else { throw Failure.limit }
            frames.append(Frame(kind: .array, phase: .arrayFirst))
        case 125:
            guard let frame = frames.last, frame.kind == .object,
                  frame.phase == .objectFirst || frame.phase == .objectAfter else { throw Failure.invalid }
            keyBytes -= frame.keyBytes; frames.removeLast(); try completeValue()
        case 93:
            guard let frame = frames.last, frame.kind == .array,
                  frame.phase == .arrayFirst || frame.phase == .arrayAfter else { throw Failure.invalid }
            frames.removeLast(); try completeValue()
        case 58:
            guard !frames.isEmpty, frames[frames.count - 1].phase == .objectColon else { throw Failure.invalid }
            frames[frames.count - 1].phase = .objectValue
        case 44:
            guard !frames.isEmpty else { throw Failure.invalid }
            let i = frames.count - 1
            if frames[i].phase == .objectAfter { frames[i].phase = .objectKey }
            else if frames[i].phase == .arrayAfter { frames[i].phase = .arrayValue }
            else { throw Failure.invalid }
        default:
            try beginValue(); lex = .atom; token = []; try append(byte)
        }
    }
    private func beginValue() throws {
        guard let frame = frames.last else { if rootDone { throw Failure.invalid }; return }
        guard frame.phase == .objectValue || frame.phase == .arrayFirst || frame.phase == .arrayValue else { throw Failure.invalid }
    }
    private mutating func completeValue() throws {
        guard !frames.isEmpty else { guard !rootDone else { throw Failure.invalid }; rootDone = true; return }
        let i = frames.count - 1
        switch frames[i].phase {
        case .objectValue: frames[i].phase = .objectAfter
        case .arrayFirst, .arrayValue: frames[i].phase = .arrayAfter
        default: throw Failure.invalid
        }
    }
    private mutating func endString() throws {
        func codeUnit(_ offset: Int) -> Int? {
            guard offset + 4 <= token.count else { return nil }
            var value = 0
            for byte in token[offset..<(offset + 4)] {
                let digit: Int
                switch byte {
                case 48...57: digit = Int(byte - 48)
                case 65...70: digit = Int(byte - 65 + 10)
                case 97...102: digit = Int(byte - 97 + 10)
                default: return nil
                }
                value = value * 16 + digit
            }
            return value
        }
        var index = 1
        while index < token.count - 1 {
            if token[index] != 92 { index += 1; continue }
            guard index + 1 < token.count - 1 else { throw Failure.invalid }
            if token[index + 1] != 117 { index += 2; continue }
            guard let unit = codeUnit(index + 2) else { throw Failure.invalid }
            index += 6
            if (0xD800...0xDBFF).contains(unit) {
                guard index + 6 <= token.count - 1, token[index] == 92, token[index + 1] == 117,
                      let low = codeUnit(index + 2), (0xDC00...0xDFFF).contains(low) else { throw Failure.invalid }
                index += 6
            } else if (0xDC00...0xDFFF).contains(unit) { throw Failure.invalid }
        }
        let parsed = try? JSONSerialization.jsonObject(with: Data(token), options: [.fragmentsAllowed])
        guard let value = parsed as? String else { throw Failure.invalid }
        let tokenBytes = token.count
        lex = .none; token.removeAll(keepingCapacity: true)
        if !frames.isEmpty && (frames[frames.count - 1].phase == .objectFirst || frames[frames.count - 1].phase == .objectKey) {
            let i = frames.count - 1
            keyBytes += tokenBytes
            guard keyBytes <= 1024 * 1024 else { throw Failure.limit }
            frames[i].keyBytes += tokenBytes
            guard frames[i].keys.count < maximumKeysPerObject else { throw Failure.limit }
            guard frames[i].keys.insert(value).inserted else { throw Failure.invalid }
            frames[i].phase = .objectColon
        } else { try beginValue(); try completeValue() }
    }
    private mutating func endAtom() throws {
        guard let text = String(bytes: token, encoding: .utf8) else { throw Failure.invalid }
        if text != "true" && text != "false" && text != "null" {
            let pattern = #"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$"#
            guard text.range(of: pattern, options: .regularExpression) != nil,
                  let number = Double(text), number.isFinite else { throw Failure.invalid }
            let mantissa = text.prefix { $0 != "e" && $0 != "E" }
            guard number != 0 || !mantissa.contains(where: { ("1"..."9").contains(String($0)) }) else { throw Failure.invalid }
        }
        lex = .none; token.removeAll(keepingCapacity: true); try completeValue()
    }
}
