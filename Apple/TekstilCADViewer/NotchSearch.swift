import Foundation

/// Use the same forgiving name search as the model library.
enum ModelSearch {
    static func matches(_ text: String, query: String) -> Bool {
        text.localizedStandardContains(query)
    }

    /// Match each search word across the visible name and record identity.
    static func matchesLibrary(name: String, identities: [String], query: String) -> Bool {
        let fields = [name] + identities
        return query.split(whereSeparator: { $0.isWhitespace }).allSatisfy { word in
            fields.contains { $0.localizedStandardContains(String(word)) }
        }
    }
}

/// Shared by the notch list and its search so visible labels are searchable.
enum NotchSearch {
    private static let pointPattern = try! NSRegularExpression(pattern: #"\bnokta\s+(\d+)\b"#, options: .caseInsensitive)

    struct Query {
        private let pointNumbers: [Int?]
        private let words: [String]
        let isEmpty: Bool

        init(_ query: String) {
            let normalized = NotchSearch.normalizeSeparators(query)
            isEmpty = normalized.isEmpty
            let range = NSRange(normalized.startIndex..., in: normalized)
            let matches = NotchSearch.pointPattern.matches(in: normalized, range: range)
            pointNumbers = matches.map { match in
                guard let number = Range(match.range(at: 1), in: normalized) else { return nil }
                return Int(normalized[number])
            }
            let remainder = NotchSearch.pointPattern.stringByReplacingMatches(in: normalized, range: range, withTemplate: " ")
            words = remainder.split(whereSeparator: { $0.isWhitespace }).map(String.init)
        }

        func allows(index: Int) -> Bool { pointNumbers.allSatisfy { $0 == index } }

        func matches(pieceName: String, details: String, index: Int, x: String, y: String) -> Bool {
            guard allows(index: index) else { return false }
            let searchable = NotchSearch.normalizeSeparators("\(pieceName) \(details) \(NotchSearch.label(index: index, x: x, y: y))")
            return words.allSatisfy { searchable.localizedStandardContains($0) }
        }
    }

    // Scan every group when necessary, but retain at most limit rows. One
    // additional match distinguishes truncation from an exact-size result.
    static func boundedMatches(groups: [[Int]], limit: Int, matches: (Int, Int) -> Bool) -> (groups: [[Int]], hasMore: Bool) {
        var result = Array(repeating: [Int](), count: groups.count)
        var count = 0
        for (groupIndex, group) in groups.enumerated() {
            for point in group where matches(groupIndex, point) {
                if count >= max(0, limit) { return (result, true) }
                result[groupIndex].append(point)
                count += 1
            }
        }
        return (result, false)
    }
    /// Returns the neighboring marked point in file order, without wrapping.
    static func adjacent(in points: [Int], current: Int?, forward: Bool) -> Int? {
        guard !points.isEmpty else { return nil }
        guard let current = current, let position = points.firstIndex(of: current) else {
            return forward ? points.first : points.last
        }
        let next = position + (forward ? 1 : -1)
        return points.indices.contains(next) ? points[next] : nil
    }

    static func label(index: Int, x: String, y: String) -> String {
        "Nokta \(index) · X \(x) · Y \(y) mm"
    }

    static func matches(query: String, index: Int, x: String, y: String) -> Bool {
        let normalizedLabel = normalizeSeparators(label(index: index, x: x, y: y))
        return normalizedLabel.localizedCaseInsensitiveContains(normalizeSeparators(query))
    }

    /// Match words across the piece heading and point label, as displayed in the picker.
    static func matches(query: String, pieceName: String, details: String, index: Int, x: String, y: String) -> Bool {
        Query(query).matches(pieceName: pieceName, details: details, index: index, x: x, y: y)
    }

    private static func normalizeSeparators(_ text: String) -> String {
        text.replacingOccurrences(of: "·", with: " ")
            .split(whereSeparator: { $0.isWhitespace })
            .joined(separator: " ")
    }
}
