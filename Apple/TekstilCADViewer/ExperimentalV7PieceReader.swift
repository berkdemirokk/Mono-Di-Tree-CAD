import Foundation

/// Experimental UTF-8 v7 visible-piece extractor. Not connected to the viewer.
/// Successful extraction checks JSON syntax, unique keys and v7 root/history
/// header fields, not full schema, geometry or rotation-history correctness.
struct ExperimentalV7PieceReader {
    enum Failure: Error { case invalid, limit, cancelled }
    private var validator = ExperimentalJSONStream()
    private var stack: [UInt8] = []
    private var string = false, escaped = false
    private var header = 0
    private var keyToken: [UInt8]? = nil
    private var nameToken: [UInt8]? = nil
    private var projectName: String? = nil
    private var potentialKey: String? = nil
    private var rootValueKey: String? = nil
    private var rootKeys = Set<String>()
    private var snapshotActive = false, traceActive = false
    private var snapshotKeys = Set<String>(), traceKeys = Set<String>()
    private var scopedValue: String? = nil
    private var scopedStringToken: [UInt8]? = nil
    private var scopedStringKey: String? = nil
    private var expectingPiecesArray = false
    private var inPieces = false, foundPieces = false
    private var piece: [UInt8]? = nil
    private var pieces: [Data] = []
    private var totalPieceBytes = 0
    private var failure = false, finished = false
    private let maximumPieceBytes: Int
    private let maximumExtractedBytes: Int
    private let maximumPieces: Int

    init(maximumPieceBytes: Int = 512 * 1024, maximumExtractedBytes: Int = 16 * 1024 * 1024,
         maximumPieces: Int = 10_000) {
        self.maximumPieceBytes = maximumPieceBytes
        self.maximumExtractedBytes = maximumExtractedBytes
        self.maximumPieces = maximumPieces
    }

    mutating func feed(_ data: Data, cancelled: () -> Bool = { false }) throws {
        guard !failure, !finished else { throw Failure.invalid }
        do {
            try validator.feed(data, cancelled: cancelled)
            for byte in data {
                if cancelled() { throw Failure.cancelled }
                try extract(byte)
            }
        } catch { failure = true; pieces.removeAll(); piece = nil; throw error }
    }

    mutating func finish(cancelled: () -> Bool = { false }) throws -> [Data] {
        guard !failure, !finished else { throw Failure.invalid }
        do {
            if cancelled() { throw Failure.cancelled }
            try validator.finish(cancelled: cancelled)
            guard header == 4, foundPieces, !inPieces, piece == nil, stack.isEmpty,
                  projectName != nil, rootKeys.isSuperset(of: ["Name", "Pieces", "GradingRules", "NestingSnapshot"]),
                  snapshotKeys == Set(["FabricWidth", "UsedLength", "Gap", "EdgeMargin", "Algorithm", "LayoutFingerprint", "Placements", "RotationTrace"]),
                  traceKeys == Set(["Schema", "Algorithm", "TransformConvention", "SourceFingerprint", "LayoutFingerprint", "SourcePieces", "LayoutPieces", "Selections", "Copies", "FabricWidth", "Gap", "EdgeMargin", "UsedLength", "MaxLength", "GrainDirection", "ToleranceDegrees"]) else { throw Failure.invalid }
            finished = true
            return pieces
        } catch { failure = true; pieces.removeAll(); piece = nil; throw error }
    }

    /// Candidate only: rotation history and full v7 schema have not been checked.
    mutating func finishPreview(cancelled: () -> Bool = { false }) throws -> ExperimentalV7Preview {
        let parts = try finish(cancelled: cancelled)
        guard let name = projectName else { throw Failure.invalid }
        return try ExperimentalV7Contours.preview(name: name, input: parts, cancelled: cancelled)
    }

    private mutating func extract(_ byte: UInt8) throws {
        if header < 4 {
            if [9, 10, 13, 32].contains(byte) { return }
            let expected: [UInt8] = [91, 55, 44, 123]
            guard byte == expected[header] else { throw Failure.invalid }
            header += 1
            if byte == 91 || byte == 123 { stack.append(byte) }
            return
        }
        if piece != nil {
            guard piece!.count < maximumPieceBytes else { throw Failure.limit }
            piece!.append(byte)
        }
        if string {
            if scopedStringToken != nil {
                guard scopedStringToken!.count < 4096 else { throw Failure.limit }
                scopedStringToken!.append(byte)
            }
            if nameToken != nil {
                guard nameToken!.count < 16 * 1024 else { throw Failure.limit }
                nameToken!.append(byte)
            }
            if keyToken != nil {
                guard keyToken!.count < 4096 else { throw Failure.limit }
                keyToken!.append(byte)
            }
            if escaped { escaped = false; return }
            if byte == 92 { escaped = true; return }
            if byte == 34 {
                string = false
                if let token = scopedStringToken, let key = scopedStringKey {
                    guard let value = (try? JSONSerialization.jsonObject(with: Data(token), options: [.fragmentsAllowed])) as? String else { throw Failure.invalid }
                    switch key {
                    case "Schema": guard value == "per-copy-orthogonal-trace-v1" else { throw Failure.invalid }
                    case "TransformConvention": guard value == "origin-quarter-turn-then-translate-v1" else { throw Failure.invalid }
                    case "Algorithm": guard value == (traceActive ? "explicit-per-copy-shelf-v1" : "deterministic-shelf-v1") else { throw Failure.invalid }
                    case "GrainDirection": guard value == "OneWay" || value == "TwoWay" else { throw Failure.invalid }
                    case "SourceFingerprint", "LayoutFingerprint":
                        guard value.utf8.count == 64, value.utf8.allSatisfy({ (48...57).contains($0) || (65...70).contains($0) }) else { throw Failure.invalid }
                    default: throw Failure.invalid
                    }
                    scopedStringToken = nil; scopedStringKey = nil
                }
                if let name = nameToken {
                    guard let decoded = (try? JSONSerialization.jsonObject(with: Data(name), options: [.fragmentsAllowed])) as? String,
                          decoded.utf16.count <= 1024 else { throw Failure.invalid }
                    projectName = decoded; nameToken = nil
                }
                if let key = keyToken {
                    potentialKey = (try? JSONSerialization.jsonObject(with: Data(key), options: [.fragmentsAllowed])) as? String
                    keyToken = nil
                }
            }
            return
        }
        if [9, 10, 13, 32].contains(byte) { return }
        var isRootStringValue = false, isScopedStringValue = false
        if let key = rootValueKey {
            let expected: UInt8 = key == "Name" ? 34 : (key == "Pieces" || key == "GradingRules") ? 91 : 123
            guard byte == expected else { throw Failure.invalid }
            isRootStringValue = key == "Name"
            if key == "NestingSnapshot" { snapshotActive = true }
            rootValueKey = nil
        }
        if let key = scopedValue {
            let strings = ["Schema", "Algorithm", "TransformConvention", "SourceFingerprint", "LayoutFingerprint", "GrainDirection"]
            let arrays = ["Placements", "SourcePieces", "LayoutPieces", "Selections", "Copies"]
            if key == "RotationTrace" {
                guard byte == 123 else { throw Failure.invalid }; traceActive = true
            } else if strings.contains(key) {
                guard byte == 34 else { throw Failure.invalid }; isScopedStringValue = true; scopedStringKey = key
            } else if arrays.contains(key) {
                guard byte == 91 else { throw Failure.invalid }
            } else {
                guard (48...57).contains(byte) || byte == 45 || (key == "MaxLength" && byte == 110) else { throw Failure.invalid }
            }
            scopedValue = nil
        }
        if expectingPiecesArray {
            guard byte == 91, stack.count == 2, !foundPieces else { throw Failure.invalid }
            expectingPiecesArray = false; inPieces = true; foundPieces = true; stack.append(byte); return
        }
        if inPieces && stack.count == 3 && piece == nil && byte != 93 && byte != 44 {
            guard byte == 123, pieces.count < maximumPieces else { throw Failure.invalid }
            piece = [byte]; stack.append(byte); return
        }
        switch byte {
        case 34:
            string = true; escaped = false
            if isRootStringValue { nameToken = [byte] }
            if isScopedStringValue { scopedStringToken = [byte] }
            if !isRootStringValue && !isScopedStringValue &&
                (stack.count == 2 && !inPieces || snapshotActive && stack.count == 3 || traceActive && stack.count == 4) {
                keyToken = [byte]
            }
        case 58:
            if stack.count == 2 {
                guard let key = potentialKey,
                      ["Name", "Pieces", "GradingRules", "NestingSnapshot", "ImportProvenance"].contains(key) else { throw Failure.invalid }
                rootKeys.insert(key); rootValueKey = key
                if key == "Pieces" { expectingPiecesArray = true }
            } else if snapshotActive && stack.count == 3 {
                guard let key = potentialKey,
                      ["FabricWidth", "UsedLength", "Gap", "EdgeMargin", "Algorithm", "LayoutFingerprint", "Placements", "RotationTrace"].contains(key) else { throw Failure.invalid }
                snapshotKeys.insert(key); scopedValue = key
            } else if traceActive && stack.count == 4 {
                guard let key = potentialKey,
                      ["Schema", "Algorithm", "TransformConvention", "SourceFingerprint", "LayoutFingerprint", "SourcePieces", "LayoutPieces", "Selections", "Copies", "FabricWidth", "Gap", "EdgeMargin", "UsedLength", "MaxLength", "GrainDirection", "ToleranceDegrees"].contains(key) else { throw Failure.invalid }
                traceKeys.insert(key); scopedValue = key
            }
            potentialKey = nil
        case 44: potentialKey = nil
        case 123, 91: stack.append(byte)
        case 125, 93:
            guard !stack.isEmpty else { throw Failure.invalid }
            let prior = stack.removeLast()
            guard (byte == 125 && prior == 123) || (byte == 93 && prior == 91) else { throw Failure.invalid }
            if traceActive && stack.count == 3 && byte == 125 { traceActive = false }
            if snapshotActive && stack.count == 2 && byte == 125 { snapshotActive = false }
            if piece != nil && stack.count == 3 {
                guard let captured = piece else { throw Failure.invalid }
                totalPieceBytes += captured.count
                guard totalPieceBytes <= maximumExtractedBytes else { throw Failure.limit }
                pieces.append(Data(captured)); piece = nil
            } else if inPieces && byte == 93 && stack.count == 2 { inPieces = false }
        default: break
        }
    }
}
