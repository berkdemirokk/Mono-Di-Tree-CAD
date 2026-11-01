import Foundation

/// Experimental UTF-8 v7 visible-piece extractor. Not connected to the viewer.
/// Successful extraction proves JSON syntax/unique keys and basic v7 root fields,
/// not full v7 schema, project validity, geometry or rotation-history correctness.
struct ExperimentalV7PieceReader {
    enum Failure: Error { case invalid, limit, cancelled }
    private var validator = ExperimentalJSONStream()
    private var stack: [UInt8] = []
    private var string = false, escaped = false
    private var header = 0
    private var keyToken: [UInt8]? = nil
    private var potentialKey: String? = nil
    private var rootValueKey: String? = nil
    private var rootKeys = Set<String>()
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
                  rootKeys.isSuperset(of: ["Name", "Pieces", "GradingRules", "NestingSnapshot"]) else { throw Failure.invalid }
            finished = true
            return pieces
        } catch { failure = true; pieces.removeAll(); piece = nil; throw error }
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
            if keyToken != nil {
                guard keyToken!.count < 4096 else { throw Failure.limit }
                keyToken!.append(byte)
            }
            if escaped { escaped = false; return }
            if byte == 92 { escaped = true; return }
            if byte == 34 {
                string = false
                if let key = keyToken {
                    potentialKey = (try? JSONSerialization.jsonObject(with: Data(key), options: [.fragmentsAllowed])) as? String
                    keyToken = nil
                }
            }
            return
        }
        if [9, 10, 13, 32].contains(byte) { return }
        if let key = rootValueKey {
            let expected: UInt8 = key == "Name" ? 34 : (key == "Pieces" || key == "GradingRules") ? 91 : 123
            guard byte == expected else { throw Failure.invalid }
            rootValueKey = nil
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
            if stack.count == 2 && !inPieces { keyToken = [byte] }
        case 58:
            if stack.count == 2 {
                guard let key = potentialKey,
                      ["Name", "Pieces", "GradingRules", "NestingSnapshot", "ImportProvenance"].contains(key) else { throw Failure.invalid }
                rootKeys.insert(key); rootValueKey = key
                if key == "Pieces" { expectingPiecesArray = true }
            }
            potentialKey = nil
        case 44: potentialKey = nil
        case 123, 91: stack.append(byte)
        case 125, 93:
            guard !stack.isEmpty else { throw Failure.invalid }
            let prior = stack.removeLast()
            guard (byte == 125 && prior == 123) || (byte == 93 && prior == 91) else { throw Failure.invalid }
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
