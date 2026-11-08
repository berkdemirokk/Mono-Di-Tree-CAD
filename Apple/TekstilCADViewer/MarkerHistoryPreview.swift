import Foundation
import CoreFoundation

/// A bounded, read-only description of a desktop format-11 saved request.
/// It never follows SourcePath and never grants permission for production.
/// The caller validates drawing geometry separately and shares the original Data.
struct MarkerHistoryPreview {
    struct SizeQuantity: Equatable { let size: Int; let sets: Int }
    struct PieceQuantity: Equatable {
        let sourcePieceID: UUID; let size: Int; let cutQuantityPerSet: Int; let effectiveQuantity: Int
    }
    struct SourceReference: Equatable { let sourcePath: String; let sourceSha256: String }
    let modelCode: String
    let sourceText: String
    let sizeQuantities: [SizeQuantity]
    let pieceQuantities: [PieceQuantity]
    let source: SourceReference?
    let fabricWidthMM: Double
    let totalCopies: Int
    let layoutMatchesHistory: Bool

    enum Failure: LocalizedError {
        case invalid, unsupportedHistory
        var errorDescription: String? {
            switch self {
            case .invalid: return "Kayıtlı iş emri eksik, tutarsız veya 8 MB önizleme sınırının dışında; dosya önizlenmedi."
            case .unsupportedHistory: return "Bu dosyada telefonda desteklenmeyen ek geçmiş var; dosya önizlenmedi."
            }
        }
    }

    static func load(_ data: Data, cancelled: @escaping () -> Bool = { false }) throws -> Self {
        if cancelled() { throw CancellationError() }
        let validation = try LosslessProjectName.versionSixValidationData(data)
        try LosslessProjectName.validateUniqueKeys(validation, strictVersion: 11, cancelled: cancelled)
        guard let envelope = try JSONSerialization.jsonObject(with: validation) as? [Any], envelope.count == 2,
              try integer(envelope[0], 11...11) == 11,
              let project = envelope[1] as? [String: Any] else { throw Failure.invalid }
        if project["PhotoTraceHistory"] != nil { throw Failure.unsupportedHistory }
        try fields(project, required: ["Name", "Pieces", "GradingRules", "NestingSnapshot"], optional: ["ImportProvenance"])
        guard let snapshot = project["NestingSnapshot"] as? [String: Any] else { throw Failure.invalid }
        try fields(snapshot, required: ["FabricWidth", "UsedLength", "Gap", "EdgeMargin", "Algorithm", "LayoutFingerprint", "Placements", "WorkOrderHistory"], optional: ["RotationTrace"])
        guard let raw = snapshot["WorkOrderHistory"] as? [String: Any] else { throw Failure.invalid }
        try fields(raw, required: ["ModelCode", "SourceText", "SizeQuantities", "PieceQuantities"], optional: ["Source"])
        let model = try text(raw["ModelCode"], limit: 64)
        guard matches(#"^[0-9]+-[0-9]+-[0-9]+$"#, model), model.utf16.count >= 5 else { throw Failure.invalid }
        let sourceText = try text(raw["SourceText"], limit: 12_000)
        let order = try ParsedOrder(sourceText)
        guard equal(model, order.model) else { throw Failure.invalid }
        let sizeRows = try rows(raw["SizeQuantities"], limit: 35)
        let sizes = try sizeRows.map { row -> SizeQuantity in
            try fields(row, required: ["Size", "Sets"])
            return SizeQuantity(size: try integer(row["Size"], 30...64), sets: try integer(row["Sets"], 1...1000))
        }
        guard sizes == order.sets.keys.sorted().map({ SizeQuantity(size: $0, sets: order.sets[$0]!) }) else { throw Failure.invalid }
        let pieceRows = try rows(raw["PieceQuantities"], limit: 10_000)
        var requested: [UUID: PieceQuantity] = [:], covered = Set<Int>(), total = 0
        let pieces = try pieceRows.map { row -> PieceQuantity in
            if cancelled() { throw CancellationError() }
            try fields(row, required: ["SourcePieceId", "Size", "CutQuantityPerSet", "EffectiveQuantity"])
            let piece = PieceQuantity(sourcePieceID: try guid(row["SourcePieceId"]), size: try integer(row["Size"], 30...64),
                cutQuantityPerSet: try integer(row["CutQuantityPerSet"], 1...100_000), effectiveQuantity: try integer(row["EffectiveQuantity"], 1...10_000))
            guard requested[piece.sourcePieceID] == nil, let sets = order.sets[piece.size],
                  piece.cutQuantityPerSet * sets == piece.effectiveQuantity else { throw Failure.invalid }
            total += piece.effectiveQuantity
            guard total <= 10_000 else { throw Failure.invalid }
            requested[piece.sourcePieceID] = piece; covered.insert(piece.size)
            return piece
        }
        guard covered == Set(order.sets.keys) else { throw Failure.invalid }
        var source: SourceReference?
        if let value = raw["Source"] {
            guard let reference = value as? [String: Any] else { throw Failure.invalid }
            try fields(reference, required: ["SourcePath", "SourceSha256"])
            let path = try text(reference["SourcePath"], limit: 4096), hash = try text(reference["SourceSha256"], limit: 64)
            try validateSourcePath(path)
            guard matches("^[0-9a-fA-F]{64}$", hash) else { throw Failure.invalid }
            source = SourceReference(sourcePath: path, sourceSha256: hash)
        }
        let width = try number(snapshot["FabricWidth"]), length = try number(snapshot["UsedLength"])
        let gap = try number(snapshot["Gap"]), margin = try number(snapshot["EdgeMargin"])
        guard width == order.width, width > 0, width <= 10_000, length > 0, length <= 100_000_000,
              gap >= 0, gap <= 100_000_000, margin >= 0, margin * 2 < width, margin * 2 <= length,
              let algorithm = snapshot["Algorithm"] as? String, equal(algorithm, "deterministic-shelf-v1"),
              let fingerprint = snapshot["LayoutFingerprint"] as? String, fingerprint.utf8.count == 64,
              matches("^[0-9A-F]{64}$", fingerprint) else { throw Failure.invalid }
        let placements = try rows(snapshot["Placements"], limit: 10_000)
        var copies: [UUID: Set<Int>] = [:], placed = Set<UUID>(), mapping: [UUID: PieceQuantity] = [:]
        for row in placements {
            if cancelled() { throw CancellationError() }
            try fields(row, required: ["SourcePieceId", "CopyNumber", "PlacedPieceId"])
            let sourceID = try guid(row["SourcePieceId"]), placedID = try guid(row["PlacedPieceId"])
            let copy = try integer(row["CopyNumber"], 1...10_000)
            guard let piece = requested[sourceID], copy <= piece.effectiveQuantity,
                  placed.insert(placedID).inserted, copies[sourceID, default: []].insert(copy).inserted else { throw Failure.invalid }
            mapping[placedID] = piece
        }
        guard pieces.allSatisfy({ copies[$0.sourcePieceID]?.count == $0.effectiveQuantity }) else { throw Failure.invalid }
        // Decode numbers directly from their JSON tokens to preserve signed zero
        // when checking whether the desktop's historical fingerprint is current.
        let shape = try JSONDecoder().decode(FingerprintEnvelope.self, from: validation)
        let hashPieces = try shape.pieces.map { try $0.fingerprintPiece() }
        let current: Bool
        do { current = try ExperimentalV7Fingerprint.hash(hashPieces, cancelled: cancelled) == fingerprint }
        catch ExperimentalV7Fingerprint.Failure.cancelled { throw CancellationError() }
        if current {
            guard placed == Set(hashPieces.map(\.id)) else { throw Failure.invalid }
            for piece in hashPieces {
                if cancelled() { throw CancellationError() }
                guard let requested = mapping[piece.id], let metadata = piece.metadata,
                      metadata.role.map({ equal($0, "Kesim") }) == true,
                      metadata.material.map({ equal($0, order.material) }) == true,
                      metadata.size.map({ equal($0, String(requested.size)) }) == true,
                      metadata.quantity == 1 else { throw Failure.invalid }
            }
        }
        if cancelled() { throw CancellationError() }
        return Self(modelCode: model, sourceText: sourceText, sizeQuantities: sizes, pieceQuantities: pieces,
                    source: source, fabricWidthMM: width, totalCopies: total, layoutMatchesHistory: current)
    }

    private static func equal(_ a: String, _ b: String) -> Bool { a.utf8.elementsEqual(b.utf8) }
    private static func fields(_ value: [String: Any], required: Set<String>, optional: Set<String> = []) throws {
        guard required.isSubset(of: Set(value.keys)), Set(value.keys).isSubset(of: required.union(optional)) else { throw Failure.invalid }
    }
    private static func text(_ value: Any?, limit: Int) throws -> String {
        guard let value = value as? String, !value.isEmpty, value.utf16.count <= limit else { throw Failure.invalid }; return value
    }
    private static func number(_ value: Any?) throws -> Double {
        guard let n = value as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID(), n.doubleValue.isFinite else { throw Failure.invalid }; return n.doubleValue
    }
    private static func integer(_ value: Any?, _ range: ClosedRange<Int>) throws -> Int {
        let n = try number(value)
        guard n.rounded() == n, n >= Double(range.lowerBound), n <= Double(range.upperBound) else { throw Failure.invalid }; return Int(n)
    }
    private static func guid(_ value: Any?) throws -> UUID {
        guard let text = value as? String, let id = UUID(uuidString: text), id != UUID(uuid: (0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0)) else { throw Failure.invalid }; return id
    }
    private static func rows(_ value: Any?, limit: Int) throws -> [[String: Any]] {
        guard let rows = value as? [[String: Any]], !rows.isEmpty, rows.count <= limit else { throw Failure.invalid }; return rows
    }
    private static func matches(_ pattern: String, _ value: String) -> Bool {
        value.range(of: pattern, options: .regularExpression) != nil
    }
    private static func validateSourcePath(_ path: String) throws {
        let bytes = Array(path.utf8)
        guard bytes.count >= 4, (65...90).contains(bytes[0]) || (97...122).contains(bytes[0]), bytes[1] == 58,
              bytes[2] == 92 || bytes[2] == 47, !path.dropFirst(2).contains(":"),
              !path.unicodeScalars.contains(where: { CharacterSet.controlCharacters.contains($0) }),
              !path.contains(where: { "<>\"|?*".contains($0) }), path.lowercased().hasSuffix(".tcad") else { throw Failure.invalid }
        let segments = path.dropFirst(3).split(omittingEmptySubsequences: false, whereSeparator: { $0 == "/" || $0 == "\\" })
        guard !segments.isEmpty,
              segments.allSatisfy({ !$0.isEmpty && $0 != "." && $0 != ".." && $0.last != " " && $0.last != "." }) else { throw Failure.invalid }
    }

    private struct FingerprintEnvelope: Decodable {
        let pieces: [FingerprintPiece]
        private struct Body: Decodable { let Pieces: [FingerprintPiece] }
        init(from decoder: Decoder) throws {
            var container = try decoder.unkeyedContainer()
            guard try container.decode(Int.self) == 11 else { throw Failure.invalid }
            pieces = try container.decode(Body.self).Pieces
            guard container.isAtEnd, pieces.count <= 10_000 else { throw Failure.invalid }
        }
    }
    private struct FingerprintPoint: Decodable {
        let X: Double, Y: Double
        var bits: ExperimentalV7Fingerprint.Point { .init(x: X.bitPattern, y: Y.bitPattern) }
    }
    private struct FingerprintPiece: Decodable {
        struct MetadataValue: Decodable { let Role: String?, Size: String?, Material: String?; let Quantity: Int32? }
        struct GrainValue: Decodable { let Start: FingerprintPoint, End: FingerprintPoint; let NeedsReview: Bool }
        struct MarkValue: Decodable { let VertexIndex: Int32; let Position: FingerprintPoint }
        let Id: UUID, Name: String, Closed: Bool, Points: [FingerprintPoint]
        let Metadata: MetadataValue?, Grainline: GrainValue?, Notches: [MarkValue]?
        func fingerprintPiece() throws -> ExperimentalV7Fingerprint.Piece {
            guard Points.count <= 10_000 else { throw Failure.invalid }
            return .init(id: Id, name: Name, closed: Closed, points: Points.map(\.bits),
                metadata: Metadata.map { .init(role: $0.Role, size: $0.Size, material: $0.Material, quantity: $0.Quantity) },
                grain: Grainline.map { .init(start: $0.Start.bits, end: $0.End.bits, needsReview: $0.NeedsReview) },
                marks: Notches.map { $0.map { .init(index: $0.VertexIndex, position: $0.Position.bits) } })
        }
    }

    /// Mirrors the desktop's bounded Turkish/English vocabulary. Any unresolved
    /// or conflicting field rejects the preview; parsing is never an action.
    private struct ParsedOrder {
        let model: String, material: String
        let width: Double
        let sets: [Int: Int]
        private struct Match {
            let text: String, result: NSTextCheckingResult
            func g(_ index: Int) -> String {
                guard let range = Range(result.range(at: index), in: text) else { return "" }
                return String(text[range])
            }
        }
        private static func find(_ pattern: String, _ text: String) throws -> [Match] {
            let regex = try NSRegularExpression(pattern: pattern)
            let deadline = ProcessInfo.processInfo.systemUptime + 0.15
            var found: [Match] = [], timedOut = false
            regex.enumerateMatches(in: text, options: [.reportProgress], range: NSRange(text.startIndex..., in: text)) { result, _, stop in
                if ProcessInfo.processInfo.systemUptime > deadline { timedOut = true; stop.pointee = true; return }
                if let result { found.append(Match(text: text, result: result)) }
            }
            guard !timedOut else { throw Failure.invalid }; return found
        }
        private static func replace(_ pattern: String, _ text: String, _ body: (Match) throws -> String) throws -> String {
            let matches = try find(pattern, text)
            var result = text
            for match in matches.reversed() {
                guard let range = Range(match.result.range, in: result) else { throw Failure.invalid }
                result.replaceSubrange(range, with: try body(match))
            }
            return result
        }
        private static func numeric(_ value: String) throws -> Double {
            guard let number = Double(value.replacingOccurrences(of: ",", with: ".")), number.isFinite else { throw Failure.invalid }; return number
        }
        private static func one<T: Hashable>(_ values: [T]) throws -> T {
            let unique = Set(values); guard unique.count == 1, let first = unique.first else { throw Failure.invalid }; return first
        }
        init(_ text: String) throws {
            let n = #"-?[0-9]+(?:[.,][0-9]+)?"#
            let unit = #"(?:santimetre|santim|cm|milimetre|mm|metre|m)(?![a-z])"#
            var input = text.lowercased(with: Locale(identifier: "en_US_POSIX"))
            for (from, to) in [("ı","i"),("İ","i"),("ş","s"),("ğ","g"),("ü","u"),("ö","o"),("ç","c"),("–","-"),("—","-"),("’","'")] {
                input = input.replacingOccurrences(of: from, with: to)
            }
            input = try Self.replace(#"\bfabric\s+width\b"#, input) { _ in "eni" }
            input = try Self.replace(#"\bhalf\s+(?:a\s+)?minute\b"#, input) { _ in "yarim dakika" }
            input = try Self.replace(#"\bone\s+set\s+(?:each|per\s+size)\b"#, input) { _ in "birer takim" }
            input = try Self.replace("(?<![a-z0-9.,])(\(n))\\s+sets?\\s+(?:each|per\\s+size)\\b", input) { "her bedenden \($0.g(1)) takim" }
            input = try Self.replace("\\beach\\s+size\\s+(\(n)|one)\\s+sets?\\b", input) { "her bedenden \($0.g(1) == "one" ? "1" : $0.g(1)) takim" }
            input = try Self.replace("(?:^|[,;]|\\band\\b)\\s*size\\s+(\(n))\\s+(\(n)|one)\\s+sets?\\b", input) { "; \($0.g(1)) beden \($0.g(2) == "one" ? "1" : $0.g(2)) takim" }
            input = try Self.replace(#"\b(?:do\s+not|don't|never|not)\b"#, input) { _ in "iptal" }
            input = try Self.replace(#"\binstead\s+of\b"#, input) { _ in "yerine" }
            input = try Self.replace(#"\b(?:sizes?|sets?|interfacing|lining|fabric|width|wide|centimet(?:er|re)s?|millimet(?:er|re)s?|met(?:er|re)s?|seconds?|secs?|minutes?|mins?|send|print|and|except|excluding|without|cancel)\b"#, input) { match in
                let word = match.g(0)
                let words = ["size":"beden", "sizes":"beden", "set":"takim", "sets":"takim", "interfacing":"tela", "lining":"astar", "fabric":"kumas", "width":"eni", "wide":"eninde", "send":"gonder", "print":"gonder", "and":"ve", "except":"haric", "excluding":"haric", "without":"haric", "cancel":"iptal"]
                return words[word] ?? (word.hasPrefix("centimet") ? "cm" : word.hasPrefix("millimet") ? "mm" : word.hasPrefix("met") ? "m" : word.hasPrefix("sec") ? "saniye" : "dakika")
            }
            guard try Self.find(#"\b(?:degil|haric|iptal|yerine)\b"#, input).isEmpty else { throw Failure.invalid }
            let part = #"(?:[0-9]+|bir|iki|uc|dort|bes|alti|yedi|sekiz|dokuz|sifir)"#
            let words = ["sifir":"0", "bir":"1", "iki":"2", "uc":"3", "dort":"4", "bes":"5", "alti":"6", "yedi":"7", "sekiz":"8", "dokuz":"9"]
            var models: [String] = []
            var remainder = try Self.replace("(?<![a-z0-9-])(\(part))\\s*(?:-|tire)\\s*(\(part))\\s*(?:-|tire)\\s*(\(part))(?![a-z0-9-])", input) { match in
                models.append((1...3).map { words[match.g($0)] ?? match.g($0) }.joined(separator: "-")); return " "
            }
            model = try Self.one(models)
            var widths: [Double] = []
            remainder = try Self.replace("(?:(?:\\b(?:en|eni|eninde|tela)\\s*[:=]?\\s*)(\(n))\\s*(\(unit))|(\(n))\\s*(\(unit))\\s*(?:eninde|en|ende)\\b)", remainder) { match in
                let value = try Self.numeric(match.g(1).isEmpty ? match.g(3) : match.g(1))
                let unit = match.g(2).isEmpty ? match.g(4) : match.g(2)
                widths.append(value * (["m", "metre"].contains(unit) ? 1000 : ["mm", "milimetre"].contains(unit) ? 1 : 10)); return " "
            }
            width = try Self.one(widths)
            guard width > 0, width <= 10_000 else { throw Failure.invalid }
            var seconds: [Double] = []
            remainder = try Self.replace("(?<![a-z0-9])(\(n)|yarim)\\s*(saniye|sn|dakika|dk)(?![a-z])", remainder) { match in
                let value = match.g(1) == "yarim" ? 0.5 : try Self.numeric(match.g(1))
                seconds.append(value * (["dakika", "dk"].contains(match.g(2)) ? 60 : 1)); return " "
            }
            let duration = try Self.one(seconds)
            guard duration >= 1, duration <= 3600, duration.rounded() == duration else { throw Failure.invalid }
            var individual: [Int: Int] = [:]
            remainder = try Self.replace("(?:^|[,;]|\\bve\\b)\\s*(\(n))\\s*beden\\s+(\(n))\\s*(?:adet|takim)\\b", remainder) { match in
                let size = try Self.numeric(match.g(1)), amount = try Self.numeric(match.g(2))
                guard size >= 30, size <= 64, size.rounded() == size, amount >= 1, amount <= 1000,
                      amount.rounded() == amount, individual[Int(size)] == nil else { throw Failure.invalid }
                individual[Int(size)] = Int(amount); return "; "
            }
            var quantities: [Double] = []
            remainder = try Self.replace("\\bbirer\\b|(?<![a-z0-9.,])(\(n))\\s*'?\\s*er\\b|\\bher\\s+bedenden\\s+(\(n))(?![0-9.,])|\\bbeden(?:lerden|den)?\\s+(\(n))\\s*(?:adet|takim)\\b", remainder) { match in
                if match.g(0) == "birer" { quantities.append(1) }
                else { quantities.append(try Self.numeric((1...3).map { match.g($0) }.first(where: { !$0.isEmpty }) ?? "")) }
                return match.g(0).hasPrefix("beden") ? " beden " : " "
            }
            guard individual.isEmpty || quantities.isEmpty else { throw Failure.invalid }
            var general: Int?
            if individual.isEmpty {
                let value = try Self.one(quantities)
                guard value >= 1, value <= 1000, value.rounded() == value else { throw Failure.invalid }; general = Int(value)
            }
            let materials = try Self.find(#"\b(tela(?:lari|lar|nin|si)?|astar|kumas)\b"#, input).map { $0.g(0).hasPrefix("tela") ? "Tela" : $0.g(0) == "astar" ? "Astar" : "Kumaş" }
            material = try Self.one(materials)
            guard try Self.find(#"(?<![a-z0-9.,])[0-9]{2}(?:\.[0-9]+|,[0-9](?![0-9])|,[0-9]{3,})(?![0-9])"#, remainder).isEmpty else { throw Failure.invalid }
            remainder = try Self.replace(#"\bbeden(?:ler|leri)?\s*[:=]?\s*([0-9]{2}(?:\s*(?:[,/;]|\bve\b|\s)\s*[0-9]{2})*)(?![0-9])"#, remainder) { $0.g(1) + " beden " }
            var groups: [[Int]] = []
            for match in try Self.find(#"(?<![a-z0-9.,-])([0-9]{2}(?:\s*(?:[,/;]|\bve\b|\s)\s*[0-9]{2})*)\s*(beden[a-z]*)?(?![a-z0-9])"#, remainder) {
                let values = try Self.find("[0-9]+", match.g(1)).compactMap { Int($0.g(0)) }
                if match.g(2).isEmpty && values.count < 2 { continue }
                guard values.allSatisfy({ (30...64).contains($0) }), Set(values).count == values.count else { throw Failure.invalid }
                groups.append(values.sorted())
            }
            let selected = groups.first ?? individual.keys.sorted()
            guard !selected.isEmpty, groups.allSatisfy({ $0 == selected }), individual.isEmpty || selected == individual.keys.sorted() else { throw Failure.invalid }
            sets = Dictionary(uniqueKeysWithValues: try selected.map { size in
                guard let count = general ?? individual[size] else { throw Failure.invalid }; return (size, count)
            })
        }
    }
}
