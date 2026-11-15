import Foundation

/// Number and size text for the preview sentences: Turkish decimal comma, at most three decimals,
/// always millimetres with the centimetre value beside it.
enum CommandFormat {
    /// 12,5 and 120.
    static func number(_ value: Double) -> String {
        let rounded = (value * 1000).rounded() / 1000
        var text = String(format: "%.3f", rounded)
        while text.hasSuffix("0") { text.removeLast() }
        if text.hasSuffix(".") { text.removeLast() }
        if text == "-0" { text = "0" }
        return text.replacingOccurrences(of: ".", with: ",")
    }

    /// "150 mm (15 cm)".
    static func length(_ millimetres: Double) -> String {
        "\(number(millimetres)) mm (\(number(millimetres / 10)) cm)"
    }

    /// "120 × 80 mm (12 × 8 cm)".
    static func size(_ width: Double, _ height: Double) -> String {
        "\(number(width)) × \(number(height)) mm (\(number(width / 10)) × \(number(height / 10)) cm)"
    }
}

extension CommandRefusal {
    /// A refusal whose buttons are plain examples that fill the command field.
    init(code: String, message: String, suggestions: [String]) {
        self.init(code: code, message: message, buttons: suggestions.map { RefusalButton($0) })
    }

    /// The texts the buttons put into the command field.
    var suggestions: [String] { buttons.map { $0.fillText } }
}
