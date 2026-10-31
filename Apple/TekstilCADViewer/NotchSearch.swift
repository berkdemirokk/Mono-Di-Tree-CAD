import Foundation

/// Shared by the notch list and its search so visible labels are searchable.
enum NotchSearch {
    static func label(index: Int, x: String, y: String) -> String {
        "Nokta \(index) · X \(x) · Y \(y) mm"
    }

    static func matches(query: String, index: Int, x: String, y: String) -> Bool {
        label(index: index, x: x, y: y).localizedCaseInsensitiveContains(query) ||
            "\(index) X \(x) Y \(y)".localizedCaseInsensitiveContains(query)
    }
}
