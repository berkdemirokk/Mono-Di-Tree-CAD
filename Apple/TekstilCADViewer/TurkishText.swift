import Foundation

// Port of TurkishText.cs, NumberWords.cs and UnitParser.cs (commit 35f3e5d of feat/command-parser).
// Text is handled as UTF-16 unit arrays ([UInt16]), exactly like the C# `string`, so token offsets taken on the
// normalised text can be reused on the folded text (every mapping is one unit to one unit). Nothing here reads the
// current locale: I -> ı and İ -> i are written out, and `String.lowercased()` is never used on user text.

enum TurkishText {
    /// Longest accepted input, in UTF-16 units, after leading and trailing white space is removed.
    static let maxInputLength = 200

    // MARK: String <-> UTF-16 helpers

    static func units(_ text: String?) -> [UInt16] {
        guard let text else { return [] }
        return Array(text.utf16)
    }

    static func string(_ units: [UInt16]) -> String {
        String(decoding: units, as: UTF16.self)
    }

    /// units[lo..<hi], clamped so it can never trap (C# Substring would throw instead).
    static func sub(_ units: [UInt16], _ lo: Int, _ hi: Int) -> [UInt16] {
        let l = max(0, min(lo, units.count))
        let h = max(l, min(hi, units.count))
        return Array(units[l..<h])
    }

    /// C# string.Trim(): removes char.IsWhiteSpace units at both ends.
    static func trim(_ units: [UInt16]) -> [UInt16] {
        var lo = 0
        var hi = units.count
        while lo < hi && isWhiteSpace(units[lo]) { lo += 1 }
        while hi > lo && isWhiteSpace(units[hi - 1]) { hi -= 1 }
        return Array(units[lo..<hi])
    }

    /// Ordinal substring search (an empty needle is found, as in C#).
    static func containsOrdinal(_ hay: [UInt16], _ needle: [UInt16]) -> Bool {
        if needle.isEmpty { return true }
        if needle.count > hay.count { return false }
        let last = hay.count - needle.count
        var i = 0
        while i <= last {
            var j = 0
            while j < needle.count && hay[i + j] == needle[j] { j += 1 }
            if j == needle.count { return true }
            i += 1
        }
        return false
    }

    /// Ordinal string.Replace(old, new): non-overlapping, left to right, on UTF-16 units.
    static func replaceOrdinal(_ text: String, _ old: String, _ new: String) -> String {
        let hay = Array(text.utf16)
        let needle = Array(old.utf16)
        let repl = Array(new.utf16)
        if needle.isEmpty || needle.count > hay.count { return text }
        var out: [UInt16] = []
        out.reserveCapacity(hay.count)
        var i = 0
        while i < hay.count {
            if i + needle.count <= hay.count {
                var j = 0
                while j < needle.count && hay[i + j] == needle[j] { j += 1 }
                if j == needle.count {
                    out.append(contentsOf: repl)
                    i += needle.count
                    continue
                }
            }
            out.append(hay[i])
            i += 1
        }
        return string(out)
    }

    // MARK: Character classes (char.IsLetter, char.IsLetterOrDigit, char.IsWhiteSpace, char.IsControl)

    /// Unicode categories L* (a surrogate unit is not a letter, as in C#).
    static func isLetter(_ u: UInt16) -> Bool {
        if u < 0x80 {
            let upper = u >= 0x41 && u <= 0x5A
            let lower = u >= 0x61 && u <= 0x7A
            return upper || lower
        }
        guard let scalar = Unicode.Scalar(UInt32(u)) else { return false }
        switch scalar.properties.generalCategory {
        case .uppercaseLetter, .lowercaseLetter, .titlecaseLetter, .modifierLetter, .otherLetter:
            return true
        default:
            return false
        }
    }

    /// L* plus Nd.
    static func isLetterOrDigit(_ u: UInt16) -> Bool {
        if isLetter(u) { return true }
        if u < 0x80 { return u >= 0x30 && u <= 0x39 }
        guard let scalar = Unicode.Scalar(UInt32(u)) else { return false }
        return scalar.properties.generalCategory == .decimalNumber
    }

    /// char.IsWhiteSpace: Zs, Zl, Zp, U+0009-000D and U+0085.
    static func isWhiteSpace(_ u: UInt16) -> Bool {
        switch u {
        case 0x09...0x0D, 0x20, 0x85, 0xA0, 0x1680, 0x2000...0x200A, 0x2028, 0x2029, 0x202F, 0x205F, 0x3000:
            return true
        default:
            return false
        }
    }

    /// char.IsControl: category Cc.
    static func isControl(_ u: UInt16) -> Bool {
        return u <= 0x1F || (u >= 0x7F && u <= 0x9F)
    }

    /// True for the ASCII digits 0-9 only (never other Unicode digits).
    static func isDigit(_ c: UInt16) -> Bool {
        return c >= 0x30 && c <= 0x39
    }

    /// Same for a Swift Character: one ASCII digit and nothing else.
    static func isDigit(_ c: Character) -> Bool {
        let u = Array(String(c).utf16)
        return u.count == 1 && isDigit(u[0])
    }

    // MARK: Case mapping

    /// I -> ı and İ -> i; every other letter uses simple (invariant) lower-casing of its own UTF-16 unit.
    static func lowerChar(_ c: UInt16) -> UInt16 {
        if c == 0x49 { return 0x131 }
        if c == 0x130 { return 0x69 }
        if c < 0x80 {
            if c >= 0x41 && c <= 0x5A { return c + 32 }
            return c
        }
        guard let scalar = Unicode.Scalar(UInt32(c)) else { return c }
        let mapped = Array(scalar.properties.lowercaseMapping.unicodeScalars)
        if mapped.count == 1 && mapped[0].value <= 0xFFFF { return UInt16(truncatingIfNeeded: mapped[0].value) }
        return c
    }

    static func upperChar(_ c: UInt16) -> UInt16 {
        if c == 0x69 { return 0x130 }
        if c == 0x131 { return 0x49 }
        if c < 0x80 {
            if c >= 0x61 && c <= 0x7A { return c - 32 }
            return c
        }
        guard let scalar = Unicode.Scalar(UInt32(c)) else { return c }
        let mapped = Array(scalar.properties.uppercaseMapping.unicodeScalars)
        if mapped.count == 1 && mapped[0].value <= 0xFFFF { return UInt16(truncatingIfNeeded: mapped[0].value) }
        return c
    }

    /// Lower-cases and folds one unit: ç->c ğ->g ı->i ö->o ş->s ü->u â->a î->i û->u (and a few more accents).
    static func foldChar(_ c: UInt16) -> UInt16 {
        let l = lowerChar(c)
        switch l {
        case 0xE7: return 0x63                       // ç
        case 0x11F: return 0x67                      // ğ
        case 0x131: return 0x69                      // ı
        case 0xF6: return 0x6F                       // ö
        case 0x15F: return 0x73                      // ş
        case 0xFC: return 0x75                       // ü
        case 0xE2, 0xE1, 0xE0, 0xE4: return 0x61     // â á à ä
        case 0xEE, 0xED, 0xEC, 0xEF: return 0x69     // î í ì ï
        case 0xFB, 0xFA, 0xF9: return 0x75           // û ú ù
        case 0xE9, 0xE8, 0xEA, 0xEB: return 0x65     // é è ê ë
        case 0xF3, 0xF2, 0xF4: return 0x6F           // ó ò ô
        default: return l
        }
    }

    static func toLower16(_ units: [UInt16]) -> [UInt16] { units.map { lowerChar($0) } }
    static func fold16(_ units: [UInt16]) -> [UInt16] { units.map { foldChar($0) } }

    /// Lower-cases with Turkish rules: I -> ı and İ -> i.
    static func toLower(_ text: String?) -> String { string(toLower16(units(text))) }

    /// Upper-cases with Turkish rules: i -> İ and ı -> I.
    static func toUpper(_ text: String?) -> String { string(units(text).map { upperChar($0) }) }

    /// Lower-cases (Turkish rules) and folds diacritics so "dikdörtgen", "dikdortgen" and "DİKDÖRTGEN" are equal.
    static func fold(_ text: String?) -> String { string(fold16(units(text))) }

    // MARK: Normalize

    /// Cleans raw input without changing letters: composes decomposed Turkish letters, turns typographic apostrophes
    /// into ', turns control characters and every kind of white space into one space, collapses runs of spaces,
    /// trims, and drops trailing '.', '!' and '?' (a lone "?" is kept; it asks for help).
    static func normalize(_ text: String?) -> String { string(normalize16(units(text))) }

    static func normalize16(_ text: [UInt16]) -> [UInt16] {
        if text.isEmpty { return [] }
        let composed = compose(text)
        var sb: [UInt16] = []
        sb.reserveCapacity(composed.count)
        var pendingSpace = false
        for raw in composed {
            var c = raw
            // ’ ‘ ´ ` ʼ ′ -> '
            let apostropheLike = c == 0x2019 || c == 0x2018 || c == 0xB4 || c == 0x60 || c == 0x2BC || c == 0x2032
            if apostropheLike { c = 0x27 }
            // white space, control characters, zero width space, BOM, soft hyphen -> one space
            let invisible = c == 0x200B || c == 0xFEFF || c == 0xAD
            let spaceLike = isWhiteSpace(c) || isControl(c) || invisible
            if spaceLike {
                pendingSpace = !sb.isEmpty
                continue
            }
            if pendingSpace {
                sb.append(0x20)
                pendingSpace = false
            }
            sb.append(c)
        }
        var end = sb.count
        while end > 1 && isTrailingPunctuation(sb[end - 1]) { end -= 1 }
        while end > 1 && sb[end - 1] == 0x20 { end -= 1 }
        return end == sb.count ? sb : Array(sb[0..<end])
    }

    private static func isTrailingPunctuation(_ c: UInt16) -> Bool {
        return c == 0x2E || c == 0x21 || c == 0x3F
    }

    private static func isCombiningMark(_ c: UInt16) -> Bool {
        // U+0307 dot above, U+0308 diaeresis, U+0327 cedilla, U+0306 breve, U+0302 circumflex
        return c == 0x0307 || c == 0x0308 || c == 0x0327 || c == 0x0306 || c == 0x0302
    }

    // Composes the few decomposed sequences a dictation or paste can produce (letter + combining mark).
    private static func compose(_ text: [UInt16]) -> [UInt16] {
        var any = false
        for c in text where isCombiningMark(c) {
            any = true
            break
        }
        if !any { return text }
        var sb: [UInt16] = []
        sb.reserveCapacity(text.count)
        var i = 0
        while i < text.count {
            let c = text[i]
            if i + 1 < text.count {
                if let composed = combine(c, text[i + 1]) {
                    sb.append(composed)
                    i += 2
                    continue
                }
            }
            if isCombiningMark(c) { // stray combining mark
                i += 1
                continue
            }
            sb.append(c)
            i += 1
        }
        return sb
    }

    private static func combine(_ letter: UInt16, _ mark: UInt16) -> UInt16? {
        switch (letter, mark) {
        case (0x49, 0x0307): return 0x130   // I + dot above -> İ
        case (0x69, 0x0307): return 0x69    // i + dot above -> i
        case (0x6F, 0x0308): return 0xF6    // ö
        case (0x4F, 0x0308): return 0xD6    // Ö
        case (0x75, 0x0308): return 0xFC    // ü
        case (0x55, 0x0308): return 0xDC    // Ü
        case (0x63, 0x0327): return 0xE7    // ç
        case (0x43, 0x0327): return 0xC7    // Ç
        case (0x73, 0x0327): return 0x15F   // ş
        case (0x53, 0x0327): return 0x15E   // Ş
        case (0x67, 0x0306): return 0x11F   // ğ
        case (0x47, 0x0306): return 0x11E   // Ğ
        case (0x61, 0x0302): return 0xE2    // â
        case (0x41, 0x0302): return 0xC2    // Â
        case (0x69, 0x0302): return 0xEE    // î
        case (0x75, 0x0302): return 0xFB    // û
        default: return nil
        }
    }

    // MARK: Number formatting

    /// Formats a number the way messages show it: Turkish decimal comma, no thousands separator and no trailing
    /// zeros (12.5 -> "12,5", 120 -> "120"). Like C# `(decimal)value`, the double is first rounded to 15 significant
    /// digits, then to 6 decimals (half away from zero). Non-finite or huge values give "0".
    static func formatNumber(_ value: Double) -> String {
        if !value.isFinite || abs(value) > 1e15 { return "0" }
        let printed = String(format: "%.14e", value)
        var negative = false
        var digits: [UInt8] = []
        var exponent = 0
        var exponentSign = 1
        var inExponent = false
        var exponentDigits = 0
        for b in printed.utf8 {
            if inExponent {
                if b == 0x2D { exponentSign = -1 } else if b >= 0x30 && b <= 0x39 {
                    exponent = exponent * 10 + Int(b - 0x30)
                    exponentDigits += 1
                }
                continue
            }
            if b == 0x65 || b == 0x45 { inExponent = true; continue }
            if b == 0x2D { negative = true; continue }
            if b >= 0x30 && b <= 0x39 { digits.append(b) }
        }
        if digits.isEmpty || exponentDigits == 0 { return "0" }
        exponent *= exponentSign
        // value = 0.d0d1d2... x 10^(exponent + 1): `pointPos` digits stand before the decimal point.
        let pointPos = exponent + 1
        var whole: [UInt8]
        var fraction: [UInt8]
        if pointPos <= 0 {
            whole = [0x30]
            fraction = [UInt8](repeating: 0x30, count: min(-pointPos, 40)) + digits
        } else if pointPos >= digits.count {
            whole = digits + [UInt8](repeating: 0x30, count: min(pointPos - digits.count, 40))
            fraction = []
        } else {
            whole = Array(digits[0..<pointPos])
            fraction = Array(digits[pointPos...])
        }
        if fraction.count > 6 {
            let roundUp = fraction[6] >= 0x35
            fraction = Array(fraction[0..<6])
            if roundUp {
                var all = whole + fraction
                var k = all.count - 1
                var carry = true
                while carry && k >= 0 {
                    if all[k] == 0x39 { all[k] = 0x30; k -= 1 } else { all[k] += 1; carry = false }
                }
                if carry { all.insert(0x31, at: 0) }
                whole = Array(all[0..<(all.count - fraction.count)])
                fraction = Array(all[(all.count - fraction.count)...])
            }
        }
        while let last = fraction.last, last == 0x30 { fraction.removeLast() }
        while whole.count > 1 && whole[0] == 0x30 { whole.removeFirst() }
        let isZero = fraction.isEmpty && whole.allSatisfy { $0 == 0x30 }
        if isZero { return "0" }
        var result = negative ? "-" : ""
        result += String(decoding: whole, as: UTF8.self)
        if !fraction.isEmpty { result += "," + String(decoding: fraction, as: UTF8.self) }
        return result
    }

    // MARK: Forbidden words

    /// True when the folded text contains one of the words the product never uses in its messages.
    static func containsForbiddenWord(_ text: String?) -> Bool {
        let folded = fold16(units(text))
        let a = containsOrdinal(folded, Array("yapay zeka".utf16))
        let b = containsOrdinal(folded, Array("asistan".utf16))
        let c = containsOrdinal(folded, Array("model".utf16))
        return a || b || c
    }
}

// MARK: - Decimal helpers

enum DecimalMath {
    /// Exact decimal from ASCII digit units: whole digits, then fraction digits. Nil when empty or too long.
    static func fromDigits(_ whole: [UInt16], _ fraction: [UInt16]) -> Decimal? {
        let total = whole.count + fraction.count
        if total == 0 || total > 18 { return nil }
        var mantissa: UInt64 = 0
        for d in whole + fraction {
            if d < 0x30 || d > 0x39 { return nil }
            mantissa = mantissa * 10 + UInt64(d - 0x30)
        }
        return Decimal(sign: .plus, exponent: -fraction.count, significand: Decimal(mantissa))
    }

    /// (double)decimal. Goes through the exact decimal text so the result is the correctly rounded double.
    static func toDouble(_ value: Decimal) -> Double {
        if let parsed = Double(value.description), parsed.isFinite { return parsed }
        return NSDecimalNumber(decimal: value).doubleValue
    }

    /// True when the value has no fractional part.
    static func isWhole(_ value: Decimal) -> Bool {
        var source = value
        var rounded = Decimal()
        NSDecimalRound(&rounded, &source, 0, .down)
        return rounded == value
    }
}

// MARK: - NumberWords

/// Result of reading one Turkish number phrase.
struct NumberWordMatch: Equatable {
    /// Exact decimal value that was read.
    var value: Decimal
    /// How many input words the number consumed (always at least 1).
    var wordCount: Int
    /// True when the last consumed word carried a glued suffix such as "yuze" or "bese".
    var hasSuffix: Bool
    /// True when the integer part is above `NumberWords.maxValue`; the value is still consumed.
    var tooLarge: Bool
}

private enum NWKind { case zero, digit, tens, hundred, thousand, bucuk, yarim, virgul }

private struct NWToken {
    var kind: NWKind
    var value: Int
    var suffixed: Bool
}

/// Reads Turkish number words. Input words must already be lower-cased and ASCII-folded
/// ("yuz", "alti", "dort", "kirk", "bucuk", "virgul"). Pure, deterministic, bounded work per call.
enum NumberWords {
    /// Largest integer value that is accepted without `tooLarge`.
    static let maxValue = 99999

    private static let maxWordLength = 16
    private static let maxFractionDigits = 6

    private static let vocabulary: [String: NWToken] = buildVocabulary()

    private static let powersOfTen: [Decimal] = [1, 10, 100, 1_000, 10_000, 100_000, 1_000_000]

    /// Is this folded word part of the number vocabulary: a number word, "bucuk", "yarim", "virgul",
    /// or a number word with an attached suffix ("yuze", "bese", "onluk")?
    static func isNumberWord(_ foldedWord: String?) -> Bool {
        return resolve(foldedWord) != nil
    }

    /// Reads the longest valid Turkish number that starts at `start`; nil when no number starts there.
    static func tryRead(_ words: [String], _ start: Int) -> NumberWordMatch? {
        guard start >= 0, start < words.count, let first = resolveAt(words, start) else { return nil }

        switch first.kind {
        case .bucuk, .virgul:
            return nil
        case .yarim:
            return NumberWordMatch(value: Decimal(sign: .plus, exponent: -1, significand: 5), wordCount: 1,
                                   hasSuffix: first.suffixed, tooLarge: false)
        default:
            break
        }

        var value: Decimal
        var end: Int
        var suffixed: Bool
        let isZero = first.kind == .zero
        if isZero {
            value = 0
            end = start + 1
            suffixed = first.suffixed
        } else {
            guard let integer = tryReadInteger(words, start) else { return nil }
            value = Decimal(integer.value)
            end = integer.end
            suffixed = integer.suffixed
        }

        let tooLarge = value > Decimal(maxValue)

        if !suffixed, let next = resolveAt(words, end) {
            if next.kind == .bucuk && !isZero {
                value += Decimal(sign: .plus, exponent: -1, significand: 5)
                end += 1
                suffixed = next.suffixed
            } else if next.kind == .virgul, let fraction = tryReadFraction(words, end + 1) {
                value += fraction.fraction
                end = fraction.end
                suffixed = fraction.suffixed
            }
        }

        return NumberWordMatch(value: value, wordCount: end - start, hasSuffix: suffixed, tooLarge: tooLarge)
    }

    // Integer part 1..999999: [group "bin"] [group], or bare "bin" [group], or a lone group.
    private static func tryReadInteger(_ words: [String], _ start: Int) -> (value: Int, end: Int, suffixed: Bool)? {
        var suffixed = false
        let thousands: Int
        let cursor: Int
        if let group = tryReadGroup(words, start) {
            if !group.suffixed, let bin = resolveAt(words, group.end), bin.kind == .thousand {
                thousands = group.value
                cursor = group.end + 1
                suffixed = bin.suffixed
            } else {
                return (group.value, group.end, group.suffixed)
            }
        } else if let lone = resolveAt(words, start), lone.kind == .thousand {
            thousands = 1
            cursor = start + 1
            suffixed = lone.suffixed
        } else {
            return nil
        }

        var value = thousands * 1000
        var end = cursor
        if !suffixed, let lower = tryReadGroup(words, cursor) {
            value += lower.value
            end = lower.end
            suffixed = lower.suffixed
        }
        return (value, end, suffixed)
    }

    // One below-1000 group: [digit "yuz" | "yuz"] [tens] [ones]; at least one part, value 1..999.
    // A suffixed word is always the last word of the group.
    private static func tryReadGroup(_ words: [String], _ start: Int) -> (value: Int, end: Int, suffixed: Bool)? {
        var value = 0
        var suffixed = false
        var cursor = start

        if let token = resolveAt(words, cursor) {
            if token.kind == .digit && !token.suffixed, let hundred = resolveAt(words, cursor + 1), hundred.kind == .hundred {
                value = token.value * 100
                cursor += 2
                suffixed = hundred.suffixed
            } else if token.kind == .hundred {
                value = 100
                cursor += 1
                suffixed = token.suffixed
            }
        }

        if !suffixed, let token = resolveAt(words, cursor), token.kind == .tens {
            value += token.value
            cursor += 1
            suffixed = token.suffixed
        }

        if !suffixed, let token = resolveAt(words, cursor), token.kind == .digit {
            value += token.value
            cursor += 1
            suffixed = token.suffixed
        }

        if cursor > start { return (value, cursor, suffixed) }
        return nil
    }

    // Fraction after "virgul": zero or more "sifir", then at most one group. At most 6 digits in total.
    private static func tryReadFraction(_ words: [String], _ start: Int) -> (fraction: Decimal, end: Int, suffixed: Bool)? {
        var cursor = start
        var zeros = 0
        while let token = resolveAt(words, cursor), token.kind == .zero {
            zeros += 1
            cursor += 1
            if zeros > maxFractionDigits { return nil }
            if token.suffixed { return (0, cursor, true) }
        }

        if let group = tryReadGroup(words, cursor) {
            let digits = digitCount(group.value)
            let total = zeros + digits
            if total > maxFractionDigits { return nil }
            return (Decimal(group.value) / powersOfTen[total], group.end, group.suffixed)
        }

        if zeros == 0 { return nil }
        return (0, cursor, false)
    }

    private static func digitCount(_ value: Int) -> Int {
        var count = 1
        var v = value
        while v >= 10 {
            v /= 10
            count += 1
        }
        return count
    }

    private static func resolveAt(_ words: [String], _ index: Int) -> NWToken? {
        if index < 0 || index >= words.count { return nil }
        return resolve(words[index])
    }

    private static func resolve(_ word: String?) -> NWToken? {
        guard let word, !word.isEmpty, word.utf16.count <= maxWordLength else { return nil }
        return vocabulary[word]
    }

    private static func isVowel(_ c: Character) -> Bool {
        return c == "a" || c == "e" || c == "i" || c == "o" || c == "u"
    }

    private static func buildVocabulary() -> [String: NWToken] {
        var map: [String: NWToken] = [:]

        // back/rounded drive vowel harmony of the last vowel of the ORIGINAL (unfolded) word;
        // voiceless is true when the original word ends in a voiceless consonant (t, k, s, c(ç), p, h, f, ş).
        func add(_ stem: String, _ kind: NWKind, _ value: Int, back: Bool, rounded: Bool, voiceless: Bool,
                 softStem: String? = nil) {
            map[stem] = NWToken(kind: kind, value: value, suffixed: false)
            let suffixedToken = NWToken(kind: kind, value: value, suffixed: true)
            let vowelEnd = stem.last.map { isVowel($0) } ?? false
            let dativeStem = softStem ?? stem

            // Dative: -e / -a after consonants, -ye / -ya after vowels.
            let dative = vowelEnd ? (back ? "ya" : "ye") : (back ? "a" : "e")
            map[dativeStem + dative] = suffixedToken
            if softStem != nil && stem == "dort" {
                map[dativeStem + (back ? "e" : "a")] = suffixedToken // tolerant: "dorda" next to "dorde"
            }

            // "-lik" family (four-way harmony, folded: lik / luk).
            map[stem + (rounded ? "luk" : "lik")] = suffixedToken

            // Ablative: -den / -dan, or -ten / -tan after voiceless consonants (the voiced spelling is tolerated).
            let vowel = back ? "an" : "en"
            map[stem + "d" + vowel] = suffixedToken
            if voiceless {
                map[stem + "t" + vowel] = suffixedToken
            }
        }

        add("sifir", .zero, 0, back: true, rounded: false, voiceless: false)

        add("bir", .digit, 1, back: false, rounded: false, voiceless: false)
        add("iki", .digit, 2, back: false, rounded: false, voiceless: false)
        add("uc", .digit, 3, back: false, rounded: true, voiceless: true)
        add("dort", .digit, 4, back: false, rounded: true, voiceless: true, softStem: "dord")
        add("bes", .digit, 5, back: false, rounded: false, voiceless: true)
        add("alti", .digit, 6, back: true, rounded: false, voiceless: false)
        add("yedi", .digit, 7, back: false, rounded: false, voiceless: false)
        add("sekiz", .digit, 8, back: false, rounded: false, voiceless: false)
        add("dokuz", .digit, 9, back: true, rounded: true, voiceless: false)

        add("on", .tens, 10, back: true, rounded: true, voiceless: false)
        add("yirmi", .tens, 20, back: false, rounded: false, voiceless: false)
        add("otuz", .tens, 30, back: true, rounded: true, voiceless: false)
        add("kirk", .tens, 40, back: true, rounded: false, voiceless: true)
        add("elli", .tens, 50, back: false, rounded: false, voiceless: false)
        add("altmis", .tens, 60, back: true, rounded: false, voiceless: true)
        add("yetmis", .tens, 70, back: false, rounded: false, voiceless: true)
        add("seksen", .tens, 80, back: false, rounded: false, voiceless: false)
        add("doksan", .tens, 90, back: true, rounded: false, voiceless: false)

        add("yuz", .hundred, 100, back: false, rounded: true, voiceless: false)
        add("bin", .thousand, 1000, back: false, rounded: false, voiceless: false)

        add("bucuk", .bucuk, 0, back: true, rounded: true, voiceless: true, softStem: "bucug")
        add("yarim", .yarim, 0, back: true, rounded: false, voiceless: false)

        map["virgul"] = NWToken(kind: .virgul, value: 0, suffixed: false)
        return map
    }
}

// MARK: - UnitParser

/// Length units understood by the command parser, ordered from smallest to largest.
enum LengthUnit: Int, Comparable {
    case millimeter = 0
    case centimeter = 1
    case meter = 2

    static func < (lhs: LengthUnit, rhs: LengthUnit) -> Bool { lhs.rawValue < rhs.rawValue }
}

/// Recognises folded (lower-case, ASCII) Turkish unit words and compound measures.
enum UnitParser {
    private static let maxWordLength = 24

    private static let words: [String: (unit: LengthUnit, hasSuffix: Bool)] = buildWords()

    /// Folded unit word, optionally with a glued suffix, to unit. `hasSuffix` tells that a suffix was glued
    /// ("santimlik"). The short stems mm, cm and m accept no glued suffix.
    static func tryParseUnitWord(_ foldedWord: String?) -> (unit: LengthUnit, hasSuffix: Bool)? {
        guard let foldedWord, !foldedWord.isEmpty, foldedWord.utf16.count <= maxWordLength else { return nil }
        return words[foldedWord]
    }

    /// Millimeters in one unit: 1, 10 or 1000.
    static func millimetersPerUnit(_ unit: LengthUnit) -> Decimal {
        switch unit {
        case .centimeter: return 10
        case .meter: return 1000
        case .millimeter: return 1
        }
    }

    /// value * factor. Callers range-check the result.
    static func toMillimeters(_ value: Decimal, _ unit: LengthUnit) -> Decimal {
        return value * millimetersPerUnit(unit)
    }

    /// Short display name: "mm", "cm" or "m".
    static func shortName(_ unit: LengthUnit) -> String {
        switch unit {
        case .centimeter: return "cm"
        case .meter: return "m"
        case .millimeter: return "mm"
        }
    }

    /// Sums compound measures such as "1 santim 2 milim" (12 mm). Needs two or three parts, every part carries an
    /// explicit unit, units strictly descending in size (m, cm, mm) and no unit repeats; otherwise nil.
    static func tryCombine(_ parts: [(value: Decimal, unit: LengthUnit)]) -> Decimal? {
        if parts.count < 2 || parts.count > 3 { return nil }
        var sum: Decimal = 0
        var previous: LengthUnit?
        for part in parts {
            if let p = previous, !(part.unit < p) { return nil }
            previous = part.unit
            sum += part.value * millimetersPerUnit(part.unit)
        }
        return sum
    }

    private static func buildWords() -> [String: (unit: LengthUnit, hasSuffix: Bool)] {
        var map: [String: (unit: LengthUnit, hasSuffix: Bool)] = [
            "mm": (.millimeter, false),
            "cm": (.centimeter, false),
            "m": (.meter, false),
        ]

        // Long stems only. Every stem has a front last vowel (i / e), so harmony gives e / ye / den / lik.
        func addStem(_ stem: String, _ unit: LengthUnit, endsWithVowel: Bool) {
            map[stem] = (unit, false)
            map[stem + "lik"] = (unit, true)
            map[stem + (endsWithVowel ? "ye" : "e")] = (unit, true)
            map[stem + "den"] = (unit, true)
        }

        addStem("milim", .millimeter, endsWithVowel: false)
        addStem("milimetre", .millimeter, endsWithVowel: true)
        addStem("santim", .centimeter, endsWithVowel: false)
        addStem("santimetre", .centimeter, endsWithVowel: true)
        addStem("metre", .meter, endsWithVowel: true)
        return map
    }
}
