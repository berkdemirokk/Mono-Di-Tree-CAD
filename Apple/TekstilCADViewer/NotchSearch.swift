import Foundation

/// Use the same forgiving name search as the model library.
enum ModelSearch {
    static func matches(_ text: String, query: String) -> Bool {
        text.localizedStandardContains(query)
    }
}

/// Shared by the notch list and its search so visible labels are searchable.
enum NotchSearch {
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
        let searchable = normalizeSeparators("\(pieceName) \(details) \(label(index: index, x: x, y: y))")
        var remainder = normalizeSeparators(query)
        // A point number must match the point itself, not a piece number,
        // size or coordinate that happens to contain the same digits.
        let pattern = try! NSRegularExpression(pattern: #"\bnokta\s+(\d+)\b"#, options: .caseInsensitive)
        let range = NSRange(remainder.startIndex..., in: remainder)
        let pointQueries = pattern.matches(in: remainder, range: range)
        for match in pointQueries {
            guard let numberRange = Range(match.range(at: 1), in: remainder),
                  Int(remainder[numberRange]) == index else { return false }
        }
        remainder = pattern.stringByReplacingMatches(in: remainder, range: range, withTemplate: " ")
        let words = remainder.split(whereSeparator: { $0.isWhitespace })
        return words.allSatisfy { searchable.localizedStandardContains(String($0)) }
    }

    private static func normalizeSeparators(_ text: String) -> String {
        text.replacingOccurrences(of: "·", with: " ")
            .split(whereSeparator: { $0.isWhitespace })
            .joined(separator: " ")
    }
}
