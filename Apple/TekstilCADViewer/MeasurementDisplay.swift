import Foundation

enum MeasurementDisplay {
    // Six significant digits communicate rounding without erasing small,
    // nonzero values. Scientific notation keeps extreme magnitudes compact.
    static func format(_ value: Double, locale: Locale = .current) -> String? {
        guard value.isFinite else { return nil }
        if value != 0 && (abs(value) < 0.01 || abs(value) >= 1_000_000_000) {
            return value.formatted(.number.notation(.scientific).precision(.significantDigits(1...6)).locale(locale))
        }
        return value.formatted(.number.precision(.significantDigits(1...6)).locale(locale))
    }
}
