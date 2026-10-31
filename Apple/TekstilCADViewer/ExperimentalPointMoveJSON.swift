import Foundation
import CoreFoundation

// Experimental; snapshot equality does not lock or reread a source file.
// Unknown schema fields are rejected because their geometry dependencies are unknown.
enum ExperimentalPointMoveJSON {
    enum Failure: Error { case invalid }
    typealias Point = ExperimentalContourValidation.Point
    static func moving(_ data: Data, snapshot: Data, pieceID: UUID, index: Int, to position: Point) throws -> Data {
        guard data == snapshot, data.count <= 8 * 1024 * 1024, String(data: data, encoding: .utf8) != nil else { throw Failure.invalid }
        guard position.x.isFinite, position.y.isFinite, abs(position.x) <= 100_000_000,
              abs(position.y) <= 100_000_000 else { throw Failure.invalid }
        let root = try JSONSerialization.jsonObject(with: data)
        let project: [String: Any]
        let versioned: Bool
        let version: Int
        if let array = root as? [Any], array.count == 2, let v = array[0] as? NSNumber,
           CFGetTypeID(v) != CFBooleanGetTypeID(), v.doubleValue.rounded() == v.doubleValue,
           [1.0, 2.0, 4.0, 6.0].contains(v.doubleValue), let body = array[1] as? [String: Any] { project = body; versioned = true; version = v.intValue }
        else if let body = root as? [String: Any] { project = body; versioned = false; version = 0 }
        else { throw Failure.invalid }
        // A grading snapshot of the edited piece can describe the old contour;
        // it is preserved byte-for-byte for explicit desktop review.
        // Selected grainlines are preserved and marked for review on a move.
        // Unknown fields may also describe geometry dependencies.
        guard Set(project.keys).isSubset(of: ["Name", "Pieces", "GradingRules"]), project["Name"] is String,
              let pieces = project["Pieces"] as? [[String: Any]], pieces.count <= 10_000 else { throw Failure.invalid }
        if versioned && project["GradingRules"] == nil { throw Failure.invalid }
        let rules: [[String: Any]]
        if let rawRules = project["GradingRules"] {
            guard let parsed = rawRules as? [[String: Any]], parsed.count <= 10_000 else { throw Failure.invalid }
            rules = parsed
        } else { rules = [] }
        guard let projectName = project["Name"] as? String, projectName.utf16.count <= 1024 else { throw Failure.invalid }
        var ids = Set<UUID>(); var selected: Int?; var total = 0; var selectedGrainReview: Bool?
        var totalNotches = 0; var selectedMarkPositions = Set<Int>()
        for (i, piece) in pieces.enumerated() {
            guard Set(piece.keys).isSubset(of: ["Id", "Name", "Points", "Closed", "Metadata", "Grainline", "Notches"]), let name = piece["Name"] as? String, name.utf16.count <= 1024,
                  let raw = piece["Id"] as? String, let id = UUID(uuidString: raw),
                  id.uuidString != "00000000-0000-0000-0000-000000000000", ids.insert(id).inserted else { throw Failure.invalid }
            if let rawGrain = piece["Grainline"] {
                guard (version == 4 || version == 6), id == pieceID, let grain = rawGrain as? [String: Any],
                      Set(grain.keys) == Set(["Start", "End", "NeedsReview"]),
                      let review = grain["NeedsReview"] as? NSNumber, CFGetTypeID(review) == CFBooleanGetTypeID() else { throw Failure.invalid }
                func coordinate(_ raw: Any?) throws -> Point {
                    guard let value = raw as? [String: Any], Set(value.keys).isSubset(of: ["X", "Y", "IsFinite"]),
                          let x = value["X"] as? NSNumber, CFGetTypeID(x) != CFBooleanGetTypeID(),
                          let y = value["Y"] as? NSNumber, CFGetTypeID(y) != CFBooleanGetTypeID(),
                          x.doubleValue.isFinite, y.doubleValue.isFinite,
                          abs(x.doubleValue) <= 100_000_000, abs(y.doubleValue) <= 100_000_000 else { throw Failure.invalid }
                    if let finite = value["IsFinite"] {
                        guard let finite = finite as? NSNumber, CFGetTypeID(finite) == CFBooleanGetTypeID(), finite.boolValue else { throw Failure.invalid }
                    }
                    return Point(x: x.doubleValue, y: y.doubleValue)
                }
                guard try coordinate(grain["Start"]) != coordinate(grain["End"]) else { throw Failure.invalid }
                selectedGrainReview = review.boolValue
            }
            if let rawMetadata = piece["Metadata"] {
                guard version >= 2, let metadata = rawMetadata as? [String: Any],
                      Set(metadata.keys).isSubset(of: ["Role", "Size", "Material", "Quantity"]) else { throw Failure.invalid }
                var hasValue = false
                for key in ["Role", "Size", "Material"] {
                    guard let value = metadata[key], !(value is NSNull) else { continue }
                    guard let value = value as? String, !value.isEmpty, value.utf16.count <= 128,
                          value == value.trimmingCharacters(in: .whitespacesAndNewlines),
                          !value.unicodeScalars.contains(where: { CharacterSet.controlCharacters.contains($0) }) else { throw Failure.invalid }
                    if key == "Role" && value != "Kesim" && value != "Yardımcı" { throw Failure.invalid }
                    hasValue = true
                }
                if let quantity = metadata["Quantity"], !(quantity is NSNull) {
                    guard let n = quantity as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID(),
                          !["f", "d"].contains(String(cString: n.objCType)), n.doubleValue >= 1, n.doubleValue <= 100_000 else { throw Failure.invalid }
                    hasValue = true
                }
                guard hasValue else { throw Failure.invalid }
            }
            guard let flag = piece["Closed"] as? NSNumber, CFGetTypeID(flag) == CFBooleanGetTypeID(),
                  let otherPoints = piece["Points"] as? [[String: Any]], otherPoints.count >= (flag.boolValue ? 3 : 2), otherPoints.count <= 10_000,
                  otherPoints.allSatisfy({ point in
                      guard Set(point.keys).isSubset(of: ["X", "Y", "IsFinite"]), point["X"] != nil, point["Y"] != nil else { return false }
                      for key in ["X", "Y"] {
                          guard let n = point[key] as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID(), n.doubleValue.isFinite, abs(n.doubleValue) <= 100_000_000 else { return false }
                      }
                      if let value = point["IsFinite"] as? NSNumber { return CFGetTypeID(value) == CFBooleanGetTypeID() && value.boolValue }
                      return point["IsFinite"] == nil
                  }) else { throw Failure.invalid }
            total += otherPoints.count
            guard total <= 200_000 else { throw Failure.invalid }
            if let rawMarks = piece["Notches"] {
                guard version == 6, let marks = rawMarks as? [[String: Any]], !marks.isEmpty, marks.count <= 10_000 else { throw Failure.invalid }
                totalNotches += marks.count
                guard totalNotches <= 200_000 else { throw Failure.invalid }
                var seen = Set<Int>()
                func pointValues(_ raw: [String: Any]) throws -> Point {
                    guard Set(raw.keys).isSubset(of: ["X", "Y", "IsFinite"]),
                          let x = raw["X"] as? NSNumber, CFGetTypeID(x) != CFBooleanGetTypeID(),
                          let y = raw["Y"] as? NSNumber, CFGetTypeID(y) != CFBooleanGetTypeID(),
                          x.doubleValue.isFinite, y.doubleValue.isFinite, abs(x.doubleValue) <= 100_000_000, abs(y.doubleValue) <= 100_000_000 else { throw Failure.invalid }
                    if let finite = raw["IsFinite"] { guard let v = finite as? NSNumber, CFGetTypeID(v) == CFBooleanGetTypeID(), v.boolValue else { throw Failure.invalid } }
                    return Point(x: x.doubleValue, y: y.doubleValue)
                }
                let firstPoint = try pointValues(otherPoints[0])
                let lastPoint = try pointValues(otherPoints[otherPoints.count - 1])
                let repeated = flag.boolValue && otherPoints.count > 1 && firstPoint == lastPoint
                for (markPosition, mark) in marks.enumerated() {
                    guard Set(mark.keys) == Set(["VertexIndex", "Position"]), let n = mark["VertexIndex"] as? NSNumber,
                          CFGetTypeID(n) != CFBooleanGetTypeID(), n.doubleValue.rounded() == n.doubleValue,
                          n.doubleValue >= 0, n.doubleValue < Double(otherPoints.count),
                          let p = mark["Position"] as? [String: Any] else { throw Failure.invalid }
                    let vertex = n.intValue
                    let markPoint = try pointValues(p)
                    let vertexPoint = try pointValues(otherPoints[vertex])
                    guard seen.insert(vertex).inserted, !(repeated && vertex == otherPoints.count - 1),
                          markPoint == vertexPoint else { throw Failure.invalid }
                    if id == pieceID && (vertex == index || (repeated && index == otherPoints.count - 1 && vertex == 0)) {
                        selectedMarkPositions.insert(markPosition)
                    }
                }
            }
            if id == pieceID { selected = i }
        }
        if version == 6 && totalNotches == 0 { throw Failure.invalid }
        // Rules are preserved byte-for-byte; a selected rule may no longer
        // match the contour and must be reviewed in the desktop application.
        var ruleIDs = Set<UUID>(); var rulePointCount = 0
        for rule in rules {
            guard Set(rule.keys) == Set(["SourcePieceId", "SourceClosed", "SourcePoints", "Deltas"]),
                  let rawID = rule["SourcePieceId"] as? String, let id = UUID(uuidString: rawID),
                  ids.contains(id), ruleIDs.insert(id).inserted,
                  let closed = rule["SourceClosed"] as? NSNumber, CFGetTypeID(closed) == CFBooleanGetTypeID(),
                  let source = rule["SourcePoints"] as? [[String: Any]], let deltas = rule["Deltas"] as? [[String: Any]],
                  source.count == deltas.count, source.count >= (closed.boolValue ? 3 : 2), source.count <= 10_000 else { throw Failure.invalid }
            rulePointCount += source.count
            guard rulePointCount <= 200_000 else { throw Failure.invalid }
            for point in source + deltas {
                guard Set(point.keys).isSubset(of: ["X", "Y", "IsFinite"]),
                      let x = point["X"] as? NSNumber, CFGetTypeID(x) != CFBooleanGetTypeID(),
                      let y = point["Y"] as? NSNumber, CFGetTypeID(y) != CFBooleanGetTypeID(),
                      x.doubleValue.isFinite, y.doubleValue.isFinite,
                      abs(x.doubleValue) <= 100_000_000, abs(y.doubleValue) <= 100_000_000 else { throw Failure.invalid }
                if let finite = point["IsFinite"] {
                    guard let value = finite as? NSNumber, CFGetTypeID(value) == CFBooleanGetTypeID(), value.boolValue else { throw Failure.invalid }
                }
            }
        }
        guard let selected, let rawPoints = pieces[selected]["Points"] as? [[String: Any]],
              let flag = pieces[selected]["Closed"] as? NSNumber, CFGetTypeID(flag) == CFBooleanGetTypeID(),
              rawPoints.indices.contains(index) else { throw Failure.invalid }
        func number(_ value: Any?) throws -> Double {
            guard let value = value as? NSNumber, CFGetTypeID(value) != CFBooleanGetTypeID(), value.doubleValue.isFinite else { throw Failure.invalid }
            return value.doubleValue
        }
        let points = try rawPoints.map { point -> Point in
            return try Point(x: number(point["X"]), y: number(point["Y"]))
        }
        let closed = flag.boolValue
        _ = try ExperimentalContourValidation.movingPoint(points, closed: closed, index: index, to: position)
        var targets: Set<Int> = [index]
        if closed && points.count > 1 && points.first == points.last && (index == 0 || index == points.count - 1) { targets = [0, points.count - 1] }
        let ranges = try data.withUnsafeBytes { raw -> [(Range<Int>, String)] in
            let bytes = raw.bindMemory(to: UInt8.self)
            let bom = bytes.count >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            var scanner = Scanner(bytes: bytes, index: bom ? 3 : 0, selected: selected, targets: targets, hasGrain: selectedGrainReview != nil, markTargets: selectedMarkPositions)
            return try scanner.scan()
        }
        if points[index] == position { return data }
        let x = Data(String(position.x).utf8), y = Data(String(position.y).utf8), review = Data("true".utf8)
        let changed = ranges.filter { item in
            switch item.1 { case "X", "NotchX": return points[index].x != position.x; case "Y", "NotchY": return points[index].y != position.y; default: return selectedGrainReview == false }
        }
        func replacement(_ key: String) -> Data { key == "X" || key == "NotchX" ? x : key == "Y" || key == "NotchY" ? y : review }
        let resultSize = changed.reduce(data.count) { size, item in size - item.0.count + replacement(item.1).count }
        guard resultSize <= 8 * 1024 * 1024 else { throw Failure.invalid }
        var result = data
        for (range, key) in changed.sorted(by: { $0.0.lowerBound > $1.0.lowerBound }) {
            result.replaceSubrange(range, with: replacement(key))
        }
        guard result.count <= 8 * 1024 * 1024 else { throw Failure.invalid }
        return result
    }
    private struct Scanner {
        let bytes: UnsafeBufferPointer<UInt8>
        var index: Int
        let selected: Int
        let targets: Set<Int>
        let hasGrain: Bool
        let markTargets: Set<Int>
        var ranges: [(Range<Int>, String)] = []
        var capturePoint = false
        var selectedPiece = false
        var captureMark = false
        mutating func whitespace() { while index < bytes.count && [9, 10, 13, 32].contains(bytes[index]) { index += 1 } }
        mutating func take(_ byte: UInt8) throws { whitespace(); guard index < bytes.count, bytes[index] == byte else { throw Failure.invalid }; index += 1 }
        mutating func scan() throws -> [(Range<Int>, String)] {
            whitespace()
            if index < bytes.count && bytes[index] == 91 {
                try take(91); whitespace(); let start = index; try value(depth: 0)
                guard let version = String(bytes: bytes[start..<index], encoding: .utf8), ["1", "2", "4", "6"].contains(version) else { throw Failure.invalid }
                try take(44); try object(depth: 0, context: 1); try take(93)
            } else { try object(depth: 0, context: 1) }
            whitespace(); guard index == bytes.count, ranges.count == targets.count * 2 + (hasGrain ? 1 : 0) + markTargets.count * 2 else { throw Failure.invalid }; return ranges
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
                else if context == 2 && key == "Points" { try pointArray(depth: depth + 1) }
                else if context == 2 && key == "Metadata" { try object(depth: depth + 1, context: 8) }
                else if context == 8 && key == "Quantity" {
                    let start = index; try value(depth: depth + 1)
                    guard let token = String(bytes: bytes[start..<index], encoding: .utf8) else { throw Failure.invalid }
                    if token != "null" {
                        guard !token.isEmpty, token.allSatisfy({ $0 >= "0" && $0 <= "9" }),
                              token.first != "0", let quantity = Int32(token), (1...100_000).contains(quantity) else { throw Failure.invalid }
                    }
                }
                else if context == 2 && key == "Notches" { try notchArray(depth: depth + 1) }
                else if context == 6 && key == "VertexIndex" {
                    let start = index; try value(depth: depth + 1)
                    guard let token = String(bytes: bytes[start..<index], encoding: .utf8),
                          !token.isEmpty, token.allSatisfy({ $0 >= "0" && $0 <= "9" }),
                          (token == "0" || token.first != "0"), Int32(token) != nil else { throw Failure.invalid }
                }
                else if context == 6 && key == "Position" { try object(depth: depth + 1, context: 7) }
                else if context == 2 && key == "Grainline" && selectedPiece { try object(depth: depth + 1, context: 4) }
                else if context == 4 && key == "NeedsReview" { let start = index; try value(depth: depth + 1); ranges.append((start..<index, key)) }
                else if context == 4 && (key == "Start" || key == "End") { try object(depth: depth + 1, context: 5) }
                else if (context == 3 || context == 5 || context == 7) && (key == "X" || key == "Y") {
                    let start = index
                    try value(depth: depth + 1)
                    // Reject nonzero mantissas that underflow to zero.
                    guard let token = String(bytes: bytes[start..<index], encoding: .utf8),
                          let parsed = Double(token), parsed.isFinite, abs(parsed) <= 100_000_000 else { throw Failure.invalid }
                    let mantissa = token.prefix { $0 != "e" && $0 != "E" }
                    guard parsed != 0 || !mantissa.contains(where: { ("1"..."9").contains(String($0)) }) else { throw Failure.invalid }
                    if context == 3 && capturePoint { ranges.append((start..<index, key)) }
                    if context == 7 && captureMark { ranges.append((start..<index, "Notch" + key)) }
                }
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
                selectedPiece = position == selected
                try object(depth: depth + 1, context: 2)
                position += 1; whitespace(); guard index < bytes.count else { throw Failure.invalid }
                if bytes[index] == 93 { index += 1; return }; try take(44)
            }
        }
        mutating func pointArray(depth: Int) throws {
            guard depth < 512 else { throw Failure.invalid }
            try take(91); whitespace(); if index < bytes.count && bytes[index] == 93 { index += 1; return }
            var position = 0
            while true {
                capturePoint = selectedPiece && targets.contains(position)
                try object(depth: depth + 1, context: 3)
                position += 1; whitespace(); guard index < bytes.count else { throw Failure.invalid }
                if bytes[index] == 93 { index += 1; return }; try take(44)
            }
        }
        mutating func notchArray(depth: Int) throws {
            guard depth < 512 else { throw Failure.invalid }
            try take(91); whitespace(); if index < bytes.count && bytes[index] == 93 { index += 1; return }
            var position = 0
            while true {
                captureMark = selectedPiece && markTargets.contains(position)
                try object(depth: depth + 1, context: 6)
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
