import Foundation

// Port of CommandParser.Lexer.cs. Offsets (Tok.start / Tok.end) are UTF-16 indices into the normalised input;
// the folded text has the same length, so the lexer works on the folded [UInt16] array.

extension CommandParser {
    enum TokKind { case word, number, sep, joiner, junk }

    /// One lexical item. A class on purpose: the C# code mutates tokens that are shared between the raw and the item
    /// lists (negative sign, unit, end offset), and later code reads those mutations through the other list.
    final class Tok {
        var kind: TokKind
        /// Folded text of a word, the digits of a number as typed, or the suffix of a joiner.
        var text: String
        var start: Int
        var end: Int

        // Numbers only.
        var value: Decimal = 0
        var negative = false
        var fromWords = false
        var bad = false          // more than one separator, too many decimals, ...
        var overflow = false     // too many integer digits or a word number above 99999
        var hazard = false       // "1.500": one separator followed by exactly three digits
        var hazardDecimal = ""
        var hazardWhole = ""
        var unit: LengthUnit?
        var numberEnd = 0        // end of the numeric part itself, before any unit word or suffix
        var hasWordSuffix = false // a number written in words whose last word carries an ending ("beşlik")

        init(kind: TokKind, text: String = "", start: Int, end: Int) {
            self.kind = kind
            self.text = text
            self.start = start
            self.end = end
        }

        func isWord(_ word: String) -> Bool { kind == .word && text == word }
        var isNumber: Bool { kind == .number }
    }

    static let maxIntegerDigits = 9
    static let maxFractionDigits = 6

    // Glued endings that may follow a number without an apostrophe ("100e 150", "2yi").
    static let gluedNumberSuffixes: Set<String> = ["e", "a", "ye", "ya", "lik", "luk", "i", "u", "yi", "yu"]

    // Case and derivation endings accepted after an apostrophe; any other letters after an apostrophe are an ordinary word.
    static let apostropheSuffixes: Set<String> = [
        "e", "a", "ye", "ya", "i", "u", "yi", "yu", "in", "un", "nin", "nun", "ni", "nu", "de", "da", "den", "dan", "te", "ta",
        "ten", "tan", "le", "la", "yle", "yla", "ile", "lik", "luk", "si", "su", "ler", "lar",
    ]

    /// Splits the folded text into words, numbers and separators. Never traps.
    static func lex(_ folded: [UInt16]) -> [Tok] {
        var tokens: [Tok] = []
        let n = folded.count
        var i = 0
        while i < n {
            let c = folded[i]
            if c == 0x20 { i += 1; continue }

            if TurkishText.isDigit(c) {
                i = lexNumber(folded, i, &tokens, negative: false, signStart: -1)
                continue
            }

            if TurkishText.isLetter(c) {
                i = lexWord(folded, i, &tokens)
                continue
            }

            switch c {
            case 0xD7, 0x2A: // × *
                tokens.append(Tok(kind: .sep, text: "x", start: i, end: i + 1))
                i += 1
            case 0x2C: // ,
                // ",5" (a separator right before a digit and not after one) is neither a pair separator nor a number.
                let beforeDigit = i + 1 < n && TurkishText.isDigit(folded[i + 1])
                let afterDigit = i > 0 && TurkishText.isDigit(folded[i - 1])
                let commaKind: TokKind = (beforeDigit && !afterDigit) ? .junk : .sep
                tokens.append(Tok(kind: commaKind, text: ",", start: i, end: i + 1))
                i += 1
            case 0x2E where i + 1 < n && TurkishText.isDigit(folded[i + 1]) && (i == 0 || !TurkishText.isDigit(folded[i - 1])):
                tokens.append(Tok(kind: .junk, text: ".", start: i, end: i + 1))
                i += 1
            case 0x2D, 0x2212, 0x2013: // - − –
                i = lexDash(folded, i, &tokens)
            case 0x3F: // ?
                if n == 1 { tokens.append(Tok(kind: .word, text: "?", start: i, end: i + 1)) }
                i += 1
            case 0x27, 0x2E, 0x3B, 0x3A, 0x28, 0x29, 0x22, 0x201C, 0x201D, 0x201E, 0x21, 0x2B, 0xAB, 0xBB:
                // ' . ; : ( ) " “ ” „ ! + « »
                i += 1
            default:
                tokens.append(Tok(kind: .junk, text: TurkishText.string([c]), start: i, end: i + 1))
                i += 1
            }
        }
        return tokens
    }

    static func lexDash(_ folded: [UInt16], _ i: Int, _ tokens: inout [Tok]) -> Int {
        let n = folded.count
        let next: UInt16 = i + 1 < n ? folded[i + 1] : 0x20
        let prev: UInt16 = i > 0 ? folded[i - 1] : 0x20
        if TurkishText.isDigit(next) {
            if TurkishText.isDigit(prev) {
                // "120-80": a hyphen between two digits is neither a sign nor a separator we trust.
                tokens.append(Tok(kind: .junk, text: "-", start: i, end: i + 1))
                return i + 1
            }
            if prev == 0x20 || !TurkishText.isLetter(prev) {
                return lexNumber(folded, i + 1, &tokens, negative: true, signStart: i)
            }
        }
        // A hyphen between two letters ("sol-kol") is white space. Anywhere else it may be a lost minus sign: refuse.
        if TurkishText.isLetter(prev) && TurkishText.isLetter(next) { return i + 1 }
        if next == 0x2D || next == 0x2212 || next == 0x2013 { return i + 1 } // the last dash of a run decides
        tokens.append(Tok(kind: .junk, text: "-", start: i, end: i + 1))
        return i + 1
    }

    static func lexWord(_ folded: [UInt16], _ from: Int, _ tokens: inout [Tok]) -> Int {
        let n = folded.count
        var i = from
        let start = i
        while i < n && TurkishText.isLetter(folded[i]) { i += 1 }
        var text = TurkishText.string(TurkishText.sub(folded, start, i))

        // "a4" / "a3" paper sizes are one word.
        if text == "a" && i < n && TurkishText.isDigit(folded[i]) {
            var j = i
            while j < n && TurkishText.isDigit(folded[j]) { j += 1 }
            text = TurkishText.string(TurkishText.sub(folded, start, j))
            i = j
        }

        let previousIsNumber = tokens.last?.kind == .number
        tokens.append(Tok(kind: .word, text: text, start: start, end: i))

        // An apostrophe suffix ("100'e", "mm'lik", "bedeni'ni") is dropped; after a number or a unit word it
        // still marks the join between two measures, so it is kept as a joiner token.
        if i + 1 < n && folded[i] == 0x27 && TurkishText.isLetter(folded[i + 1]) {
            var j = i + 1
            while j < n && TurkishText.isLetter(folded[j]) { j += 1 }
            let suffix = TurkishText.string(TurkishText.sub(folded, i + 1, j))
            if apostropheSuffixes.contains(suffix) {
                let isUnit = UnitParser.tryParseUnitWord(text) != nil
                if previousIsNumber || isUnit {
                    tokens.append(Tok(kind: .joiner, text: suffix, start: i, end: j))
                }
                i = j
            }
        }
        return i
    }

    static func lexNumber(_ folded: [UInt16], _ from: Int, _ tokens: inout [Tok], negative: Bool, signStart: Int) -> Int {
        let n = folded.count
        var i = from
        let start = i
        let integerStart = i
        while i < n && TurkishText.isDigit(folded[i]) { i += 1 }
        let integerDigits = i - integerStart
        var separators = 0
        var fractionDigits = 0
        var lastFractionLength = 0
        var firstFraction: [UInt16]?
        while i + 1 < n && (folded[i] == 0x2C || folded[i] == 0x2E) && TurkishText.isDigit(folded[i + 1]) {
            separators += 1
            var j = i + 1
            while j < n && TurkishText.isDigit(folded[j]) { j += 1 }
            lastFractionLength = j - i - 1
            if firstFraction == nil {
                firstFraction = TurkishText.sub(folded, i + 1, i + 1 + lastFractionLength)
            }
            fractionDigits += lastFractionLength
            i = j
        }

        let tok = Tok(kind: .number,
                      text: TurkishText.string(TurkishText.sub(folded, start, i)),
                      start: signStart >= 0 ? signStart : start,
                      end: i)
        tok.numberEnd = i
        tok.negative = negative

        if separators >= 2 || fractionDigits > maxFractionDigits {
            tok.bad = true
        } else if integerDigits > maxIntegerDigits {
            tok.overflow = true
        } else {
            let wholeText = TurkishText.sub(folded, integerStart, integerStart + integerDigits)
            let fraction: [UInt16] = separators == 0 ? [] : (firstFraction ?? [])
            if let value = DecimalMath.fromDigits(wholeText, fraction) {
                tok.value = value
                if separators == 1 && lastFractionLength == 3 && !isZeroDigits(wholeText) {
                    tok.hazard = true
                    var trimmed = fraction
                    while let last = trimmed.last, last == 0x30 { trimmed.removeLast() }
                    let decimalReading = trimmed.isEmpty ? "" : "," + TurkishText.string(trimmed)
                    tok.hazardDecimal = TurkishText.string(wholeText) + decimalReading
                    tok.hazardWhole = TurkishText.string(wholeText) + TurkishText.string(fraction)
                }
            } else {
                tok.bad = true
            }
        }
        tokens.append(tok)

        // Scientific notation ("15e-1") is not supported: refuse it instead of reading a unit suffix.
        if i < n && folded[i] == 0x65 {
            var k = i + 1
            if k < n && (folded[k] == 0x2D || folded[k] == 0x2B) { k += 1 }
            if k < n && TurkishText.isDigit(folded[k]) {
                tokens.append(Tok(kind: .junk, text: "e", start: i, end: i + 1))
                return i + 1
            }
        }

        // Apostrophe suffix: "100'e", "100'lük".
        if i + 1 < n && folded[i] == 0x27 && TurkishText.isLetter(folded[i + 1]) {
            var j = i + 1
            while j < n && TurkishText.isLetter(folded[j]) { j += 1 }
            let suffix = TurkishText.string(TurkishText.sub(folded, i + 1, j))
            if apostropheSuffixes.contains(suffix) {
                tokens.append(Tok(kind: .joiner, text: suffix, start: i, end: j))
                return j
            }
        }

        // Letters glued to the number: "120x80", "12cm", "100e".
        if i < n && TurkishText.isLetter(folded[i]) {
            var j = i
            while j < n && TurkishText.isLetter(folded[j]) { j += 1 }
            let run = TurkishText.string(TurkishText.sub(folded, i, j))
            if gluedNumberSuffixes.contains(run) {
                tokens.append(Tok(kind: .joiner, text: run, start: i, end: j))
                return j
            }
            // Leave the letters for the word lexer so "12cm" and "120x80" read as number + word.
            return i
        }
        return i
    }

    static func isZeroDigits(_ digits: [UInt16]) -> Bool {
        for d in digits where d != 0x30 { return false }
        return true
    }
}
