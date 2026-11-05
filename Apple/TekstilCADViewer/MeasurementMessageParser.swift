import Foundation

/// Narrow, explicit syntax only; original message remains untouched.
enum MeasurementMessageParser {
    enum Failure: Error, Equatable { case empty, unrecognized, duplicate, existing, outOfRange, ambiguousNumber, descriptionConflict, descriptionUncertain }
    static let supportedLabels: Set<String> = ["göğüs çevresi", "bel çevresi", "kalça çevresi", "elbise boyu"]
    /// A three-digit suffix can be a decimal or thousands grouping. Do not guess.
    static func isAmbiguousNumber(_ number: String) -> Bool {
        number.range(of: #"\A[0-9]{1,3}[.,][0-9]{3}\z"#, options: .regularExpression) != nil
    }
    static func canonicalLabel(_ matched: String) -> String? {
        let label = matched.lowercased(with: Locale(identifier: "tr_TR"))
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
        var isApplicable: Bool { !values.isEmpty && Set(values.keys).isSubset(of: MeasurementMessageParser.supportedLabels) }
        var preview: String {
            let labels = ["göğüs çevresi", "bel çevresi", "kalça çevresi", "elbise boyu"]
            let found = labels.compactMap { label in values[label].map { "\(label): \($0)" } }.joined(separator: "\n")
            let missing = labels.filter { values[$0] == nil }
            return missing.isEmpty ? found : found + "\nMesajda belirtilmeyenler: " + missing.joined(separator: ", ") + ". Varsa mevcut alanları kontrol edin."
        }
    }
    /// Desktop v1 splits comma/semicolon/newline, but not conjunctions or sentence periods.
    /// This is only a narrow warning, not proof of downstream interpretation.
    static func containsExplicitMeasurement(_ text: String) -> Bool {
        guard text.utf16.count <= 16_384 else { return true }
        let measurement = #"(?i)(?<![\p{L}\p{N}_])(?:göğüs[ \t]+çevresi|bel[ \t]+çevresi|kalça[ \t]+çevresi|elbise[ \t]+boyu)[ \t]*(?::|=)?[ \t]*[0-9]{1,5}(?:[.,][0-9]{1,8})?[ \t]*(?:cm|mm)(?![\p{L}\p{N}_])"#
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
        let expression = #"(?i)(?:cm|mm)(?:[ \t]+olsun)?(?:[ \t]+ve[ \t]+|[ \t]*\.[ \t\r\n]*(?=\S))"#
        let ambiguousNumber = #"(?i)(?<![0-9.,])[0-9]{1,3}[.,][0-9]{3}[ \t]*(?:cm|mm)(?![\p{L}\p{N}_])"#
        let question = #"(?i)(?:cm|mm)(?:[ \t]+(?:olsun|mi|mı|mu|mü))?[ \t]*[?？]"#
        let qualified = #"(?i)(?:cm|mm)(?:[ \t]+|[ \t]*,[ \t]*)(?:olma\p{L}*|değil\p{L}*|olursa|ise)(?![\p{L}\p{N}_])"#
        return [expression, ambiguousNumber, question, qualified].contains { message.range(of: $0, options: .regularExpression) != nil }
            || hasConditionalMeasurementRisk([message])
    }
    static func parse(_ message: String, existing: [String: String]) throws -> Proposal {
        guard !message.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { throw Failure.empty }
        guard message.utf16.count <= 16_384 else { throw Failure.unrecognized }
        let expression = try NSRegularExpression(pattern: #"[ \t]*(göğüs çevresi|bel çevresi|kalça çevresi|elbise boyu)[ \t]*(?::|=)?[ \t]*([0-9]{1,5}(?:[.,][0-9]{1,8})?)[ \t]*(cm|mm)(?:[ \t]+olsun)?(?:[ \t]+(ve)(?=[ \t])|[ \t]*([,;.\n])[ \t]*(?:\n[ \t]*)?|[ \t]*$)"#, options: [.caseInsensitive])
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
