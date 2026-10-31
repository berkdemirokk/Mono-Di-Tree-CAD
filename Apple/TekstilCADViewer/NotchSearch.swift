import Foundation

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

    private static func normalizeSeparators(_ text: String) -> String {
        text.replacingOccurrences(of: "·", with: " ")
            .split(whereSeparator: { $0.isWhitespace })
            .joined(separator: " ")
    }
}
