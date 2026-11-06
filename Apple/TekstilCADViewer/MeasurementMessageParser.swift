import Foundation

/// Narrow, explicit syntax only; original message remains untouched.
enum MeasurementMessageParser {
    enum Failure: Error, Equatable { case empty, unrecognized, duplicate, existing, existingConflict, existingUncertain, alreadyMatching, outOfRange, ambiguousNumber, descriptionConflict, descriptionUncertain }
    static let supportedLabels: Set<String> = ["göğüs çevresi", "bel çevresi", "kalça çevresi", "elbise boyu"]
    /// A three-digit suffix can be a decimal or thousands grouping. Do not guess.
    static func isAmbiguousNumber(_ number: String) -> Bool {
        number.range(of: #"\A[0-9]{1,3}[.,][0-9]{3}\z"#, options: .regularExpression) != nil
    }
    /// Recognize only a complete bare number or a number with cm/mm in a basic field.
    static func isAmbiguousBasicField(_ value: String) -> Bool {
        guard value.utf16.count <= 64,
              let regex = try? NSRegularExpression(pattern: #"\A[\t\p{Zs}]*([0-9]{1,5}(?:[.,][0-9]{1,8})?)[\t\p{Zs}]*(?:cm|mm)?[\t\p{Zs}]*\z"#, options: [.caseInsensitive]),
              let match = regex.firstMatch(in: value, range: NSRange(value.startIndex..., in: value)),
              let range = Range(match.range(at: 1), in: value) else { return false }
        return isAmbiguousNumber(String(value[range]))
    }
    static func canonicalLabel(_ matched: String) -> String? {
        let label = matched.lowercased(with: Locale(identifier: "tr_TR")).split(whereSeparator: { $0.isWhitespace }).joined(separator: " ")
        return supportedLabels.contains(label) ? label : nil
    }
    /// Only values already produced by parse are eligible for comparison.
    static func millimeters(_ value: String) -> Decimal? {
        let parts = value.split(separator: " ")
        guard parts.count == 2, let number = Decimal(string: String(parts[0]).replacingOccurrences(of: ",", with: "."), locale: Locale(identifier: "en_US_POSIX")),
              parts[1] == "cm" || parts[1] == "mm" else { return nil }
        return parts[1] == "cm" ? number * 10 : number
    }
    struct Proposal: Equatable {
        let source: String
        let values: [String: String]
        let preserved: [String: String]
        init(source: String, values: [String: String], preserved: [String: String] = [:]) {
            self.source = source; self.values = values; self.preserved = preserved
        }
        var isApplicable: Bool {
            !values.isEmpty && Set(values.keys).isSubset(of: MeasurementMessageParser.supportedLabels)
                && Set(preserved.keys).isSubset(of: MeasurementMessageParser.supportedLabels)
                && Set(values.keys).isDisjoint(with: Set(preserved.keys))
        }
        var preview: String {
            let labels = ["göğüs çevresi", "bel çevresi", "kalça çevresi", "elbise boyu"]
            var lines = labels.compactMap { label in values[label].map { "\(label): \($0)" } }
            lines += labels.compactMap { label in preserved[label].map { "\(label): mevcut alan \($0), değiştirilmeyecek" } }
            let missing = labels.filter { values[$0] == nil && preserved[$0] == nil }
            if !missing.isEmpty { lines.append("Mesajda belirtilmeyenler: " + missing.joined(separator: ", ") + ". Varsa mevcut alanları kontrol edin.") }
            return lines.joined(separator: "\n")
        }
    }
    /// Comparison never changes the source and only accepts fully understood messages and fields.
    struct ComparisonInput: Equatable {
        let message: String
        let fields: [String: String]
    }
    struct Difference: Equatable {
        let label: String
        let fieldValue: String
        let messageValue: String
    }
    enum Comparison: Equatable {
        case noMessage
        case unrecognizedMessage
        case reviewed(compared: Int, differences: [Difference], unrecognizedFields: [String])
    }
    static func compare(_ input: ComparisonInput) -> Comparison {
        if input.message.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { return .noMessage }
        guard let message = try? parse(input.message, existing: [:]) else { return .unrecognizedMessage }
        let labels = ["göğüs çevresi", "bel çevresi", "kalça çevresi", "elbise boyu"]
        let fieldPattern = #"\A[\t\p{Zs}]*[0-9]{1,5}(?:[.,][0-9]{1,8})?[\t\p{Zs}]*(?:cm|mm)[\t\p{Zs}]*\z"#
        var compared = 0, differences: [Difference] = [], unrecognized: [String] = []
        for label in labels {
            guard let messageValue = message.values[label], let field = input.fields[label], !field.isEmpty else { continue }
            guard field.utf16.count <= 64,
                  field.range(of: fieldPattern, options: [.regularExpression, .caseInsensitive]) != nil,
                  let parsed = try? parse("\(label): \(field)", existing: [:]),
                  let fieldValue = parsed.values[label],
                  let a = millimeters(fieldValue), let b = millimeters(messageValue) else {
                unrecognized.append(label); continue
            }
            compared += 1
            if a != b { differences.append(.init(label: label, fieldValue: field, messageValue: messageValue)) }
        }
        return .reviewed(compared: compared, differences: differences, unrecognizedFields: unrecognized)
    }
    /// Desktop v1 splits comma/semicolon/newline, but not conjunctions or sentence periods.
    /// This is only a narrow warning, not proof of downstream interpretation.
    static func containsExplicitMeasurement(_ text: String) -> Bool {
        guard text.utf16.count <= 16_384 else { return true }
        let measurement = #"(?i)(?<![\p{L}\p{N}_])(?:göğüs[\t\p{Zs}]+çevresi|bel[\t\p{Zs}]+çevresi|kalça[\t\p{Zs}]+çevresi|elbise[\t\p{Zs}]+boyu)[\t\p{Zs}]*(?::|=)?[\t\p{Zs}]*[0-9]{1,5}(?:[.,][0-9]{1,8})?[\t\p{Zs}]*(?:cm|mm)(?![\p{L}\p{N}_])"#
        return text.range(of: measurement, options: .regularExpression) != nil
    }
    static func hasConditionalMeasurementRisk(_ parts: [String]) -> Bool {
        guard parts.allSatisfy({ $0.utf16.count <= 16_384 }) else { return true }
        let condition = #"(?i)(?<![\p{L}\p{N}_])(?:eğer|şayet)(?![\p{L}\p{N}_])"#
        // A condition and a measurement in different segments or fields can
        // still be related; warn without claiming to interpret the sentence.
        return parts.contains { $0.range(of: condition, options: .regularExpression) != nil }
            && parts.contains { containsExplicitMeasurement($0) }
    }
    static func mayNeedDesktopReview(_ message: String) -> Bool {
        guard message.utf16.count <= 16_384 else { return true }
        let expression = #"(?i)(?:cm|mm)(?:[\t\p{Zs}]+olsun)?(?:[\t\p{Zs}]+ve[\t\p{Zs}]+|[\t\p{Zs}]*\.[\t\p{Zs}\r\n]*(?=\S))"#
        let ambiguousNumber = #"(?i)(?<![0-9.,])[0-9]{1,3}[.,][0-9]{3}[\t\p{Zs}]*(?:cm|mm)(?![\p{L}\p{N}_])"#
        let question = #"(?i)(?:cm|mm)(?:[\t\p{Zs}]+(?:olsun|mi|mı|mu|mü))?[\t\p{Zs}]*[?？]"#
        let qualified = #"(?i)(?:cm|mm)(?:[\t\p{Zs}]+|[\t\p{Zs}]*,[\t\p{Zs}]*)(?:olma\p{L}*|değil\p{L}*|olursa|ise)(?![\p{L}\p{N}_])"#
        return [expression, ambiguousNumber, question, qualified].contains { message.range(of: $0, options: .regularExpression) != nil }
            || hasConditionalMeasurementRisk([message])
    }
    static func parse(_ message: String, existing: [String: String]) throws -> Proposal {
        guard !message.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { throw Failure.empty }
        guard message.utf16.count <= 16_384 else { throw Failure.unrecognized }
        let expression = try NSRegularExpression(pattern: #"[\t\p{Zs}]*(göğüs[\t\p{Zs}]+çevresi|bel[\t\p{Zs}]+çevresi|kalça[\t\p{Zs}]+çevresi|elbise[\t\p{Zs}]+boyu)[\t\p{Zs}]*(?::|=)?[\t\p{Zs}]*([0-9]{1,5}(?:[.,][0-9]{1,8})?)[\t\p{Zs}]*(cm|mm)(?:[\t\p{Zs}]+olsun)?(?:[\t\p{Zs}]+(ve)(?=[\t\p{Zs}])|[\t\p{Zs}]*([,;.\n])[\t\p{Zs}]*(?:\n[\t\p{Zs}]*)?|[\t\p{Zs}]*$)"#, options: [.caseInsensitive])
        let normalized = message.replacingOccurrences(of: "\r\n", with: "\n").replacingOccurrences(of: "\r", with: "\n")
        let source = normalized as NSString
        var cursor = 0, found: [String: String] = [:]
        while cursor < source.length {
            let remaining = source.substring(from: cursor)
            if remaining.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { cursor = source.length; break }
            let region = NSRange(location: cursor, length: source.length - cursor)
            guard let match = expression.firstMatch(in: normalized, options: [.anchored], range: region), match.range.length > 0 else { throw Failure.unrecognized }
            guard let label = canonicalLabel(source.substring(with: match.range(at: 1))) else { throw Failure.unrecognized }
            let remainder = source.substring(from: NSMaxRange(match.range)).trimmingCharacters(in: .whitespacesAndNewlines)
            if remainder.isEmpty {
                if match.range(at: 4).location != NSNotFound { throw Failure.unrecognized }
                if match.range(at: 5).location != NSNotFound {
                    let separator = source.substring(with: match.range(at: 5))
                    if separator == "," || separator == ";" { throw Failure.unrecognized }
                }
            }
            let number = source.substring(with: match.range(at: 2))
            let unit = source.substring(with: match.range(at: 3)).lowercased()
            guard !isAmbiguousNumber(number) else { throw Failure.ambiguousNumber }
            guard found[label] == nil else { throw Failure.duplicate }
            guard (existing[label] ?? "").isEmpty else { throw Failure.existing }
            guard let value = Decimal(string: number.replacingOccurrences(of: ",", with: "."), locale: Locale(identifier: "en_US_POSIX")), value > 0, value <= (unit == "cm" ? 1000 : 10000) else { throw Failure.outOfRange }
            found[label] = number + " " + unit
            cursor = NSMaxRange(match.range)
        }
        guard !found.isEmpty else { throw Failure.empty }
        return Proposal(source: message, values: found)
    }
}
