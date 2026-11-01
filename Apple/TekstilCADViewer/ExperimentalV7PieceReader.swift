import Foundation
import CoreFoundation
import CryptoKit

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
    private var scopedNumberToken: [UInt8]? = nil, scopedNumberKey: String? = nil
    private var scopedNumberIsTrace = false
    private var snapshotNumbers: [String: UInt64] = [:], traceNumbers: [String: UInt64] = [:]
    private var snapshotFingerprint: String?, traceFingerprint: String?
    private var grainDirection: String?
    private struct Mapping: Equatable { let source: UUID; let number: Int; let placed: UUID }
    private var placementMappings: [Mapping] = [], traceMappings: [Mapping] = []
    private var rotationAngles: [UUID: Int] = [:]
    private var mappingArray: String? = nil, mappingDepth = 0
    private var mappingToken: [UInt8]? = nil
    private var sourceIDs = Set<UUID>(), historyIDs = Set<UUID>(), historicalPoints = 0, historicalMarks = 0
    private var sourceOrder: [UUID] = []
    private var sourceQuantities: [UUID: Int] = [:]
    private var sourceGeometry: [UUID: Drawing.Piece] = [:], historyGeometry: [UUID: Drawing.Piece] = [:]
    private var selectionIDs = Set<UUID>()
    private var expectingPiecesArray = false
    private var inPieces = false, foundPieces = false
    private var piece: [UInt8]? = nil
    private var pieces: [Data] = []
    private var totalPieceBytes = 0
    private var failure = false, finished = false
    private var sourceHasher = SHA256()
    private var sourceLength = 0
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
                try extract(byte, cancelled: cancelled)
            }
            sourceHasher.update(data: data)
            sourceLength += data.count
        } catch { discardPartialState(); throw error }
    }

    mutating func finish(cancelled: () -> Bool = { false }) throws -> [Data] {
        guard !failure, !finished else { throw Failure.invalid }
        do {
            if cancelled() { throw Failure.cancelled }
            try validator.finish(cancelled: cancelled)
            guard header == 4, foundPieces, !inPieces, piece == nil, stack.isEmpty,
                  projectName != nil, rootKeys.isSuperset(of: ["Name", "Pieces", "GradingRules", "NestingSnapshot"]),
                  snapshotKeys == Set(["FabricWidth", "UsedLength", "Gap", "EdgeMargin", "Algorithm", "LayoutFingerprint", "Placements", "RotationTrace"]),
                  traceKeys == Set(["Schema", "Algorithm", "TransformConvention", "SourceFingerprint", "LayoutFingerprint", "SourcePieces", "LayoutPieces", "Selections", "Copies", "FabricWidth", "Gap", "EdgeMargin", "UsedLength", "MaxLength", "GrainDirection", "ToleranceDegrees"]),
                  !placementMappings.isEmpty, placementMappings == traceMappings,
                  !sourceIDs.isEmpty, selectionIDs == sourceIDs, historyIDs.count == traceMappings.count,
                  let fingerprint = snapshotFingerprint, fingerprint == traceFingerprint,
                  ["FabricWidth", "UsedLength", "Gap", "EdgeMargin"].allSatisfy({ snapshotNumbers[$0] != nil && snapshotNumbers[$0] == traceNumbers[$0] }),
                  let direction = grainDirection, let toleranceBits = traceNumbers["ToleranceDegrees"],
                  traceMappings.allSatisfy({ sourceIDs.contains($0.source) && historyIDs.contains($0.placed) }) else { throw Failure.invalid }
            let tolerance = Double(bitPattern: toleranceBits)
            guard let widthBits = snapshotNumbers["FabricWidth"], let lengthBits = snapshotNumbers["UsedLength"],
                  let gapBits = snapshotNumbers["Gap"], let marginBits = snapshotNumbers["EdgeMargin"] else { throw Failure.invalid }
            let width = Double(bitPattern: widthBits), length = Double(bitPattern: lengthBits)
            let gap = Double(bitPattern: gapBits), margin = Double(bitPattern: marginBits)
            guard width > 0, width <= 100_000_000, length > 0, length <= 100_000_000,
                  gap >= 0, gap <= 100_000_000, margin >= 0, margin * 2 < width,
                  tolerance >= 0, tolerance <= 90 else { throw Failure.invalid }
            if let maxBits = traceNumbers["MaxLength"] {
                let maximum = Double(bitPattern: maxBits)
                guard maximum > 0, length <= maximum else { throw Failure.invalid }
            }
            var seenPlaced = Set<UUID>(), seenCopies = Set<String>(), counts: [UUID: Int] = [:]
            for mapping in traceMappings {
                if cancelled() { throw Failure.cancelled }
                guard let quantity = sourceQuantities[mapping.source], mapping.number <= quantity,
                      Self.desktopCopyID(source: mapping.source, number: mapping.number) == mapping.placed,
                      seenPlaced.insert(mapping.placed).inserted,
                      seenCopies.insert("\(mapping.source.uuidString)#\(mapping.number)").inserted else { throw Failure.invalid }
                guard let source = sourceGeometry[mapping.source], let placed = historyGeometry[mapping.placed],
                      let angle = rotationAngles[mapping.placed],
                      try ExperimentalV7Geometry.exactlyMatches(source: source, placed: placed, degrees: angle,
                          direction: direction, tolerance: tolerance, cancelled: cancelled) else { throw Failure.invalid }
                counts[mapping.source, default: 0] += 1
            }
            guard sourceQuantities.allSatisfy({ counts[$0.key] == $0.value }) else { throw Failure.invalid }
            var index = 0
            for source in sourceOrder {
                guard let quantity = sourceQuantities[source] else { throw Failure.invalid }
                for copy in 1...quantity {
                    if cancelled() { throw Failure.cancelled }
                    guard index < traceMappings.count, traceMappings[index].source == source,
                          traceMappings[index].number == copy else { throw Failure.invalid }
                    index += 1
                }
            }
            guard index == traceMappings.count else { throw Failure.invalid }
            try ExperimentalV7Layout.verify(Array(historyGeometry.values), width: width, length: length,
                                            gap: gap, margin: margin, cancelled: cancelled)
            finished = true
            return pieces
        } catch { discardPartialState(); throw error }
    }

    /// Candidate only: rotation history and full v7 schema have not been checked.
    mutating func finishPreview(cancelled: () -> Bool = { false }) throws -> ExperimentalV7Preview {
        do {
            let parts = try finish(cancelled: cancelled)
            guard let name = projectName else { throw Failure.invalid }
            let preview = try ExperimentalV7Contours.preview(name: name, input: parts, cancelled: cancelled)
            guard preview.pieces.reduce(historicalPoints, { $0 + $1.points.count }) <= 200_000,
                  preview.pieces.reduce(historicalMarks, { $0 + $1.notches.count }) <= 200_000 else { throw Failure.limit }
            return preview
        } catch { discardPartialState(); throw error }
    }

    /// Experimental in-memory source retention. Caller owns the original immutable
    /// Data snapshot; the digest only checks it matches the fed bytes, not authenticity.
    /// Full v7 history and geometry are still unverified; never use as a production load.
    mutating func finishDocument(original: Data, cancelled: () -> Bool = { false }) throws -> ExperimentalV7Document {
        guard !failure, !finished else { throw Failure.invalid }
        guard original.count == sourceLength, original.count <= 64 * 1024 * 1024 else { discardPartialState(); throw Failure.invalid }
        var candidateHash = SHA256()
        var offset = 0
        while offset < original.count {
            if cancelled() { discardPartialState(); throw Failure.cancelled }
            let end = min(offset + 64 * 1024, original.count)
            candidateHash.update(data: original[offset..<end]); offset = end
        }
        var expectedHash = sourceHasher
        guard candidateHash.finalize() == expectedHash.finalize() else { discardPartialState(); throw Failure.invalid }
        let preview = try finishPreview(cancelled: cancelled)
        if cancelled() { discardPartialState(); throw Failure.cancelled }
        let visible = Set(preview.pieces.compactMap(\.id))
        let placed = Set(traceMappings.map(\.placed))
        let drawing = Drawing(name: preview.name, pieces: preview.pieces, pieceRenameIdentitiesValid: true,
            formatVersion: 7, hasNonUTF8BOM: false,
            historyPieceIDsDiffer: visible != placed || preview.pieces.count != traceMappings.count,
            insufficientContourCount: preview.pieces.filter { !$0.hasMinimumContourPoints }.count,
            collapsedContourCount: preview.pieces.filter { $0.isCollapsed }.count)
        return ExperimentalV7Document(drawing: drawing, originalBytes: original)
    }

    private mutating func extract(_ byte: UInt8, cancelled: () -> Bool) throws {
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
        if mappingToken != nil {
            let limit = mappingArray == "SourcePieces" || mappingArray == "LayoutPieces" ? maximumPieceBytes : 4096
            guard mappingToken!.count < limit else { throw Failure.limit }
            mappingToken!.append(byte)
        }
        if scopedNumberToken != nil {
            if (48...57).contains(byte) || [43, 45, 46, 69, 101].contains(byte) {
                guard scopedNumberToken!.count < 128 else { throw Failure.limit }
                scopedNumberToken!.append(byte); return
            }
            guard let token = scopedNumberToken, let key = scopedNumberKey,
                  let text = String(bytes: token, encoding: .utf8), let value = Double(text), value.isFinite else { throw Failure.invalid }
            if scopedNumberIsTrace { traceNumbers[key] = value.bitPattern }
            else { snapshotNumbers[key] = value.bitPattern }
            scopedNumberToken = nil; scopedNumberKey = nil
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
                if mappingArray == "Selections", let token = mappingToken {
                    guard let text = (try? JSONSerialization.jsonObject(with: Data(token), options: [.fragmentsAllowed])) as? String,
                          let id = UUID(uuidString: text), id.uuidString != "00000000-0000-0000-0000-000000000000",
                          selectionIDs.count < maximumPieces, selectionIDs.insert(id).inserted else { throw Failure.invalid }
                    mappingToken = nil
                }
                if let token = scopedStringToken, let key = scopedStringKey {
                    guard let value = (try? JSONSerialization.jsonObject(with: Data(token), options: [.fragmentsAllowed])) as? String else { throw Failure.invalid }
                    switch key {
                    case "Schema": guard value == "per-copy-orthogonal-trace-v1" else { throw Failure.invalid }
                    case "TransformConvention": guard value == "origin-quarter-turn-then-translate-v1" else { throw Failure.invalid }
                    case "Algorithm": guard value == (traceActive ? "explicit-per-copy-shelf-v1" : "deterministic-shelf-v1") else { throw Failure.invalid }
                    case "GrainDirection":
                        guard value == "OneWay" || value == "TwoWay" else { throw Failure.invalid }
                        grainDirection = value
                    case "SourceFingerprint", "LayoutFingerprint":
                        guard value.utf8.count == 64, value.utf8.allSatisfy({ (48...57).contains($0) || (65...70).contains($0) }) else { throw Failure.invalid }
                        if key == "LayoutFingerprint" {
                            if traceActive { traceFingerprint = value } else { snapshotFingerprint = value }
                        }
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
                if ["Placements", "Copies", "SourcePieces", "LayoutPieces", "Selections"].contains(key) {
                    guard mappingArray == nil else { throw Failure.invalid }
                    mappingArray = key; mappingDepth = stack.count + 1
                }
            } else {
                guard (48...57).contains(byte) || byte == 45 || (key == "MaxLength" && byte == 110) else { throw Failure.invalid }
                if byte != 110 {
                    scopedNumberToken = [byte]; scopedNumberKey = key; scopedNumberIsTrace = traceActive
                }
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
        if let array = mappingArray, stack.count == mappingDepth, mappingToken == nil, byte != 93 && byte != 44 {
            if array == "Selections" {
                guard byte == 34 else { throw Failure.invalid }
                mappingToken = [byte]; string = true; escaped = false; return
            }
            let count: Int
            switch array { case "Placements": count = placementMappings.count; case "Copies": count = traceMappings.count
            case "SourcePieces": count = sourceIDs.count; default: count = historyIDs.count }
            guard byte == 123, count < maximumPieces else { throw Failure.invalid }
            mappingToken = [byte]; stack.append(byte); return
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
            if let array = mappingArray, let token = mappingToken, byte == 125 && stack.count == mappingDepth {
                if array == "SourcePieces" || array == "LayoutPieces" {
                    let candidates = try ExperimentalV7Contours.decode([Data(token)], cancelled: cancelled)
                    guard let item = candidates.first, let id = item.id, item.grain != nil, item.closed,
                          item.points.count <= 2_000, item.role == "Kesim", item.material != nil else { throw Failure.invalid }
                    if array == "SourcePieces" {
                        guard let quantity = item.quantity, sourceIDs.insert(id).inserted else { throw Failure.invalid }
                        sourceQuantities[id] = quantity
                        sourceOrder.append(id)
                        sourceGeometry[id] = item
                    } else {
                        guard item.quantity == 1, historyIDs.insert(id).inserted else { throw Failure.invalid }
                        historyGeometry[id] = item
                    }
                    historicalPoints += item.points.count; historicalMarks += item.notches.count
                    guard historicalPoints <= 200_000, historicalMarks <= 200_000 else { throw Failure.limit }
                } else {
                    let decoded = try decodeMapping(Data(token), trace: array == "Copies", cancelled: cancelled)
                    if array == "Copies" {
                        guard let angle = decoded.angle, rotationAngles.updateValue(angle, forKey: decoded.mapping.placed) == nil else { throw Failure.invalid }
                        traceMappings.append(decoded.mapping)
                    } else { placementMappings.append(decoded.mapping) }
                }
                mappingToken = nil
            } else if mappingArray != nil && byte == 93 && stack.count == mappingDepth - 1 {
                mappingArray = nil
            }
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

    private func decodeMapping(_ data: Data, trace: Bool, cancelled: () -> Bool) throws -> (mapping: Mapping, angle: Int?) {
        var scanner = ExperimentalJSONStream(maximumBytes: 4096, strictIntegerKeys: true)
        try scanner.feed(data, cancelled: cancelled); try scanner.finish(cancelled: cancelled)
        guard let object = try JSONSerialization.jsonObject(with: data) as? [String: Any],
              Set(object.keys) == Set(trace ? ["SourcePieceId", "CopyNumber", "PlacedPieceId", "Degrees"] : ["SourcePieceId", "CopyNumber", "PlacedPieceId"]),
              let sourceText = object["SourcePieceId"] as? String, let source = UUID(uuidString: sourceText),
              let placedText = object["PlacedPieceId"] as? String, let placed = UUID(uuidString: placedText),
              let n = object["CopyNumber"] as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID(),
              n.intValue >= 1, n.intValue <= 100_000 else { throw Failure.invalid }
        var angle: Int? = nil
        if trace {
            guard let degrees = object["Degrees"] as? NSNumber, CFGetTypeID(degrees) != CFBooleanGetTypeID(),
                  [0, 90, 180, 270].contains(degrees.intValue) else { throw Failure.invalid }
            angle = degrees.intValue
        }
        return (Mapping(source: source, number: n.intValue, placed: placed), angle)
    }

    private mutating func discardPartialState() {
        failure = true
        pieces = []; piece = nil; mappingToken = nil; keyToken = nil; nameToken = nil; scopedStringToken = nil; scopedNumberToken = nil
        sourceGeometry = [:]; historyGeometry = [:]; sourceQuantities = [:]; sourceOrder = []
        sourceIDs = []; historyIDs = []; selectionIDs = []; rotationAngles = [:]
        placementMappings = []; traceMappings = []
        stack = []; rootKeys = []; snapshotKeys = []; traceKeys = []
        validator = ExperimentalJSONStream(); projectName = nil; potentialKey = nil; rootValueKey = nil; scopedValue = nil
        scopedStringKey = nil; scopedNumberKey = nil; snapshotNumbers = [:]; traceNumbers = [:]
        snapshotFingerprint = nil; traceFingerprint = nil; grainDirection = nil
    }

    #if DEBUG
    var retainedEntriesForTesting: Int {
        pieces.count + (piece == nil ? 0 : 1) + (mappingToken == nil ? 0 : 1) + sourceGeometry.count + historyGeometry.count +
        sourceQuantities.count + sourceOrder.count + sourceIDs.count + historyIDs.count + selectionIDs.count + rotationAngles.count +
        placementMappings.count + traceMappings.count
    }
    #endif

    // Guid(ReadOnlySpan<byte>) in the desktop runtime treats its first three
    // fields as little-endian. UUID text uses network byte order for them.
    private static func desktopCopyID(source: UUID, number: Int) -> UUID? {
        let text = "TekstilCAD.production-copy.v1:\(source.uuidString.lowercased()):\(number)"
        let bytes = Array(SHA256.hash(data: Data(text.utf8)).prefix(16))
        let order = [3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15]
        let hex = order.map { String(format: "%02x", Int(bytes[$0])) }.joined()
        let a = hex.index(hex.startIndex, offsetBy: 8), b = hex.index(a, offsetBy: 4)
        let c = hex.index(b, offsetBy: 4), d = hex.index(c, offsetBy: 4)
        return UUID(uuidString: "\(hex[..<a])-\(hex[a..<b])-\(hex[b..<c])-\(hex[c..<d])-\(hex[d...])")
    }
}
